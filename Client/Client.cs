using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SharedModels;
using EncryptionLibrary;

namespace Client {
    public class MessageReceivedEventArgs : EventArgs {
        public MessagePacket Packet { get; set; }
        public string DecryptedContent { get; set; }
        public byte[] DecryptedFileData { get; set; }
        public bool IsOwnMessage { get; set; } = false; // Indicates if the message is sent by the client itself
    }

    public class ConnectionStatusEventArgs : EventArgs {
        public bool IsConnected { get; set; }
        public string Message { get; set; }
    }

    public class FileTransferProgressEventArgs : EventArgs {
        public string MessageId { get; set; }
        public int CurrentPacket { get; set; }
        public int TotalPackets { get; set; }
        public string FileName { get; set; }
        public bool IsUpload { get; set; }
    }

    public class ChatClient {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private RSAKeyPair _clientEncryption;
        private RSAKeyPair.PublicKey _serverPublicKey;
        private bool _isConnected;
        private bool _keyExchangeComplete;
        private readonly Dictionary<string, List<MessagePacket>> _downloadBuffer;
        private string _clientId;
        private string _serverHost;
        private int _serverPort;

        // Events
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler KeyExchangeCompleted;
        public event EventHandler<FileTransferProgressEventArgs> FileTransferProgress;

        // Properties
        public string ClientId => _clientId;
        public bool IsConnected => _isConnected;
        public bool IsReady => _isConnected && _keyExchangeComplete;

        public ChatClient(string serverHost = "localhost", int serverPort = 4933) {
            _downloadBuffer = new Dictionary<string, List<MessagePacket>>();
            _clientId = Environment.UserName + "_" + DateTime.Now.Ticks;
            _serverHost = serverHost;
            _serverPort = serverPort;
        }

        public async Task ConnectAsync() {
            try {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_serverHost, _serverPort);
                _stream = _tcpClient.GetStream();
                _isConnected = true;

                _clientEncryption = RSAEncryption.GenerateKeyPair();

                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = true,
                    Message = "Connected to server"
                });

                // Start listening for messages
                _ = Task.Run(ListenForMessagesAsync);

                // Wait for key exchange
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = true,
                    Message = "Waiting for key exchange..."
                });

                while (!_keyExchangeComplete && _isConnected) {
                    await Task.Delay(100);
                }

                if (_keyExchangeComplete) {
                    ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                        IsConnected = true,
                        Message = "Ready"
                    });
                }
            } catch (Exception ex) {
                _isConnected = false;
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = false,
                    Message = $"Connection failed: {ex.Message}"
                });
                throw;
            }
        }

        public async Task DisconnectAsync() {
            _isConnected = false;
            _keyExchangeComplete = false;

            try {
                _stream?.Close();
                _tcpClient?.Close();
            } catch { }

            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                IsConnected = false,
                Message = "Disconnected"
            });
        }

        public async Task SendTextMessageAsync(string message) {
            if (!IsReady)
                throw new InvalidOperationException("Client not ready");

            try {
                // Encrypt with SERVER's public key
                (var encryptedMessage, var key) = HybridEncryption.Encrypt(
                    message, _serverPublicKey);

                var packet = new MessagePacket {
                    Type = MessageType.Text,
                    Content = encryptedMessage,
                    EncryptedSessionKey = key,
                    SenderId = _clientId
                };

                await SendPacketAsync(packet);

                // Notify local display
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                    Packet = packet,
                    DecryptedContent = message,
                    IsOwnMessage = true // Indicate this is the client's own message
                });
            } catch (Exception ex) {
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = _isConnected,
                    Message = $"Failed to send message: {ex.Message}"
                });
                throw;
            }
        }

        public async Task SendFileAsync(string filePath, MessageType messageType) {
            if (!IsReady)
                throw new InvalidOperationException("Client not ready");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found");

            try {
                byte[] fileData = await File.ReadAllBytesAsync(filePath);
                (byte[] encryptedData, var key) = HybridEncryption.Encrypt(
                    fileData, _serverPublicKey);

                string fileName = Path.GetFileName(filePath);
                string messageId = Guid.NewGuid().ToString();
                var chunks = ChunkData(encryptedData, 4096);

                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = true,
                    Message = $"Sending {messageType.ToString().ToLower()}: {fileName}..."
                });

                for (int i = 0; i < chunks.Count; i++) {
                    var packet = new MessagePacket {
                        Type = messageType,
                        Content = Convert.ToBase64String(chunks[i]),
                        EncryptedSessionKey = key,
                        TotalPackets = chunks.Count,
                        PacketNumber = i + 1,
                        FileName = fileName,
                        FileSize = fileData.Length,
                        MessageId = messageId,
                        SenderId = _clientId
                    };

                    await SendPacketAsync(packet);

                    FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs {
                        MessageId = messageId,
                        CurrentPacket = i + 1,
                        TotalPackets = chunks.Count,
                        FileName = fileName,
                        IsUpload = true
                    });
                }

                // Notify local display
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                    Packet = new MessagePacket {
                        Type = messageType,
                        FileName = fileName,
                        FileSize = fileData.Length,
                        MessageId = messageId,
                        SenderId = _clientId,
                        EncryptedSessionKey = string.Empty
                    },
                    DecryptedFileData = fileData,
                    IsOwnMessage = true // Indicate this is the client's own message
                });

                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = true,
                    Message = $"{messageType} sent successfully!"
                });
            } catch (Exception ex) {
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = _isConnected,
                    Message = $"Failed to send file: {ex.Message}"
                });
                throw;
            }
        }

        public async Task RequestFileDownloadAsync(string fileId) {
            if (!IsReady)
                throw new InvalidOperationException("Client not ready");

            try {
                (var content, var key) = HybridEncryption.Encrypt(fileId, _serverPublicKey);
                var packet = new MessagePacket {
                    Type = MessageType.FileDownloadRequest,
                    Content = content,
                    EncryptedSessionKey = key,
                    SenderId = _clientId,
                    MessageId = fileId
                };

                await SendPacketAsync(packet);

                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = true,
                    Message = $"Requesting download for file: {fileId}"
                });
            } catch (Exception ex) {
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = _isConnected,
                    Message = $"Failed to request download: {ex.Message}"
                });
                throw;
            }
        }

        private async Task ListenForMessagesAsync() {
            var buffer = new byte[8192];
            var messageBuffer = new StringBuilder();

            try {
                while (_isConnected && _tcpClient.Connected) {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuffer.Append(data);

                    string bufferContent = messageBuffer.ToString();
                    string[] messages = bufferContent.Split('\n');

                    for (int i = 0; i < messages.Length - 1; i++) {
                        if (!string.IsNullOrWhiteSpace(messages[i])) {
                            await ProcessIncomingMessageAsync(messages[i]);
                        }
                    }

                    messageBuffer.Clear();
                    if (!string.IsNullOrWhiteSpace(messages[messages.Length - 1])) {
                        messageBuffer.Append(messages[messages.Length - 1]);
                    }
                }
            } catch (Exception ex) {
                if (_isConnected) {
                    _isConnected = false;
                    ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                        IsConnected = false,
                        Message = $"Connection error: {ex.Message}"
                    });
                }
            }
        }

        private async Task ProcessIncomingMessageAsync(string jsonMessage) {
            try {
                var packet = JsonSerializer.Deserialize<MessagePacket>(jsonMessage);
                if (packet == null) return;

                switch (packet.Type) {
                    case MessageType.KeyExchange:
                        await HandleKeyExchange(packet);
                        break;

                    case MessageType.Text:
                        if (packet.SenderId != _clientId) {
                            HandleTextMessage(packet);
                        }
                        break;

                    case MessageType.File:
                    case MessageType.Photo:
                        if (packet.SenderId != _clientId) {
                            HandleFileNotification(packet);
                        }
                        break;

                    case MessageType.FileDownloadResponse:
                        await HandleFileDownloadResponse(packet);
                        break;
                }
            } catch (Exception ex) {
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = _isConnected,
                    Message = $"Error processing message: {ex.Message}"
                });
            }
        }

        private async Task HandleKeyExchange(MessagePacket packet) {
            try {
                var keyData = JsonSerializer.Deserialize<JsonElement>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(packet.Content)));

                if (packet.SenderId == "SERVER") {
                    var eStr = keyData.GetProperty("E").GetString();
                    var nStr = keyData.GetProperty("N").GetString();

                    _serverPublicKey = new RSAKeyPair.PublicKey(
                        System.Numerics.BigInteger.Parse(eStr),
                        System.Numerics.BigInteger.Parse(nStr)
                    );

                    await SendClientPublicKeyAsync();
                    _keyExchangeComplete = true;

                    KeyExchangeCompleted?.Invoke(this, EventArgs.Empty);
                }
            } catch (Exception ex) {
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = _isConnected,
                    Message = $"Key exchange failed: {ex.Message}"
                });
            }
        }

        private async Task SendClientPublicKeyAsync() {
            var keyExchangePacket = new MessagePacket {
                Type = MessageType.KeyExchange,
                Content = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                        E = _clientEncryption.Public.E.ToString(),
                        N = _clientEncryption.Public.N.ToString()
                    }))
                ),
                EncryptedSessionKey = string.Empty,
                SenderId = _clientId
            };

            await SendPacketAsync(keyExchangePacket);
        }

        private void HandleTextMessage(MessagePacket packet) {
            try {
                string decryptedMessage = HybridEncryption.Decrypt(
                    packet.Content,
                    packet.EncryptedSessionKey,
                    _clientEncryption.Private);

                MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                    Packet = packet,
                    DecryptedContent = decryptedMessage
                });
            } catch (Exception ex) {
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                    Packet = packet,
                    DecryptedContent = $"[Decryption failed: {ex.Message}]"
                });
            }
        }

        private void HandleFileNotification(MessagePacket packet) {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                Packet = packet,
                DecryptedContent = string.Empty
            });
        }

        private async Task HandleFileDownloadResponse(MessagePacket packet) {
            try {
                if (!_downloadBuffer.ContainsKey(packet.MessageId)) {
                    _downloadBuffer[packet.MessageId] = new List<MessagePacket>();
                }

                _downloadBuffer[packet.MessageId].Add(packet);

                FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs {
                    MessageId = packet.MessageId,
                    CurrentPacket = _downloadBuffer[packet.MessageId].Count,
                    TotalPackets = packet.TotalPackets,
                    FileName = packet.FileName,
                    IsUpload = false
                });

                if (_downloadBuffer[packet.MessageId].Count == packet.TotalPackets) {
                    var fileData = ReassembleAndDecryptFile(_downloadBuffer[packet.MessageId]);

                    MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                        Packet = new MessagePacket {
                            Type = packet.Type,
                            FileName = packet.FileName,
                            FileSize = packet.FileSize,
                            MessageId = packet.MessageId,
                            SenderId = packet.SenderId,
                            EncryptedSessionKey = string.Empty
                        },
                        DecryptedFileData = fileData
                    });

                    _downloadBuffer.Remove(packet.MessageId);

                    ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                        IsConnected = true,
                        Message = $"File downloaded: {packet.FileName}"
                    });
                }
            } catch (Exception ex) {
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs {
                    IsConnected = _isConnected,
                    Message = $"Download failed: {ex.Message}"
                });
            }
        }

        private byte[] ReassembleAndDecryptFile(List<MessagePacket> packets) {
            packets.Sort((a, b) => a.PacketNumber.CompareTo(b.PacketNumber));

            using var ms = new MemoryStream();
            foreach (var packet in packets) {
                byte[] data = Convert.FromBase64String(packet.Content);
                ms.Write(data, 0, data.Length);
            }

            byte[] encryptedData = ms.ToArray();
            return HybridEncryption.Decrypt(
                encryptedData,
                packets[0].EncryptedSessionKey,
                _clientEncryption.Private);
        }

        private List<byte[]> ChunkData(byte[] data, int chunkSize) {
            var chunks = new List<byte[]>();
            for (int i = 0; i < data.Length; i += chunkSize) {
                int size = Math.Min(chunkSize, data.Length - i);
                byte[] chunk = new byte[size];
                Array.Copy(data, i, chunk, 0, size);
                chunks.Add(chunk);
            }
            return chunks;
        }

        private async Task SendPacketAsync(MessagePacket packet) {
            if (!_isConnected || _stream == null)
                throw new InvalidOperationException("Not connected to server");

            string json = JsonSerializer.Serialize(packet);
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            await _stream.WriteAsync(data, 0, data.Length);
        }
    }
}