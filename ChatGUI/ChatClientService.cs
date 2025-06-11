using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SharedModels;
using EncryptionLibrary;
using System.Runtime.Serialization;

namespace ChatGUI {
    public class MessageReceivedEventArgs : EventArgs {
        public MessagePacket Packet { get; set; }
        public string DecryptedContent { get; set; }
    }

    public class FileReceivedEventArgs : EventArgs {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public MessageType MessageType { get; set; }
    }

    public class ChatClientService {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private RSAKeyPair _clientEncryption;
        private RSAKeyPair.PublicKey _serverPublicKey;
        private bool _isConnected;
        private bool _keyExchangeComplete;
        private readonly Dictionary<string, List<MessagePacket>> _downloadBuffer;
        private string _clientId;
        private const string SERVER_HOST = "192.168.1.175";
        private const int SERVER_PORT = 8888;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<bool> ConnectionStatusChanged;
        public event EventHandler<FileReceivedEventArgs> FileReceived;

        public ChatClientService() {
            _downloadBuffer = new Dictionary<string, List<MessagePacket>>();
            _clientId = Environment.UserName + "_" + DateTime.Now.Ticks;
        }

        public async Task ConnectAsync() {
            try {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(SERVER_HOST, SERVER_PORT);
                _stream = _tcpClient.GetStream();
                _isConnected = true;

                _clientEncryption = RSAEncryption.GenerateKeyPair();

                ConnectionStatusChanged?.Invoke(this, true);

                // Start listening for messages
                _ = Task.Run(ListenForMessagesAsync);

                // Wait for key exchange
                while (!_keyExchangeComplete && _isConnected) {
                    await Task.Delay(100);
                }
            } catch (Exception) {
                _isConnected = false;
                throw;
            }
        }

        public async Task DisconnectAsync() {
            _isConnected = false;
            _stream?.Close();
            _tcpClient?.Close();
            ConnectionStatusChanged?.Invoke(this, false);
        }

        public async Task SendTextMessageAsync(string message) {
            if (!_keyExchangeComplete)
                throw new InvalidOperationException("Key exchange not complete");

            // Encrypt with SERVER's public key
            (var encryptedMessage, var key) = HybridEncryption.Encrypt(message, _serverPublicKey);

            var packet = new MessagePacket {
                Type = MessageType.Text,
                Content = encryptedMessage,
                EncryptedSessionKey = key,
                SenderId = _clientId
            };

            await SendPacketAsync(packet);
        }

        public async Task SendFileAsync(string filePath, MessageType messageType) {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found");

            if (!_keyExchangeComplete)
                throw new InvalidOperationException("Key exchange not complete");

            byte[] fileData = await File.ReadAllBytesAsync(filePath);
            // Encrypt with SERVER's encryption
            (byte[] encryptedData, var key) = HybridEncryption.Encrypt(fileData, _serverPublicKey);

            string fileName = Path.GetFileName(filePath);
            string messageId = Guid.NewGuid().ToString();

            var chunks = ChunkData(encryptedData, 4096);

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
            }
        }

        public async Task RequestFileDownloadAsync(string fileId) {
            (var content, var key) = HybridEncryption.Encrypt(fileId, _serverPublicKey);
            var packet = new MessagePacket {
                Type = MessageType.FileDownloadRequest,
                Content = content,
                EncryptedSessionKey = key,
                SenderId = _clientId
            };

            await SendPacketAsync(packet);
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
            } catch (Exception) {
                if (_isConnected) {
                    _isConnected = false;
                    ConnectionStatusChanged?.Invoke(this, false);
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
                            var decryptedText = HybridEncryption.Decrypt(packet.Content, packet.EncryptedSessionKey, _clientEncryption.Private);
                            MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                                Packet = packet,
                                DecryptedContent = decryptedText
                            });
                        }
                        break;

                    case MessageType.File:
                    case MessageType.Photo:
                        if (packet.SenderId != _clientId) {
                            MessageReceived?.Invoke(this, new MessageReceivedEventArgs {
                                Packet = packet,
                                DecryptedContent = string.Empty
                            });
                        }
                        break;

                    case MessageType.FileDownloadResponse:
                        await HandleFileDownloadResponse(packet);
                        break;
                }
            } catch (Exception ex) {
                Console.WriteLine($"Error processing message: {ex.Message}");
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
                }
            } catch (Exception ex) {
                Console.WriteLine($"Error handling key exchange: {ex.Message}");
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

        private async Task HandleFileDownloadResponse(MessagePacket packet) {
            if (!_downloadBuffer.ContainsKey(packet.MessageId)) {
                _downloadBuffer[packet.MessageId] = new List<MessagePacket>();
            }

            _downloadBuffer[packet.MessageId].Add(packet);

            if (_downloadBuffer[packet.MessageId].Count == packet.TotalPackets) {
                var fileData = ReassembleAndDecryptFile(_downloadBuffer[packet.MessageId]);
                string fileName = $"downloaded_{DateTime.Now:yyyyMMdd_HHmmss}_{packet.MessageId}";
                string filePath = Path.Combine(FileSystem.Current.CacheDirectory, fileName);

                await File.WriteAllBytesAsync(filePath, fileData);

                var messageType = fileName.Contains("photo") ? MessageType.Photo : MessageType.File;

                FileReceived?.Invoke(this, new FileReceivedEventArgs {
                    FilePath = filePath,
                    FileName = fileName,
                    MessageType = messageType
                });

                _downloadBuffer.Remove(packet.MessageId);
            }
        }

        private byte[] ReassembleAndDecryptFile(List<MessagePacket> packets) {
            packets.Sort((a, b) => a.PacketNumber.CompareTo(b.PacketNumber));

            using var ms = new MemoryStream();
            foreach (var packet in packets) {
                byte[] data = Convert.FromBase64String(packet.Content);
                var decrypted = HybridEncryption.Decrypt(data, packet.EncryptedSessionKey, _clientEncryption.Private);
                ms.Write(decrypted, 0, data.Length);
            }

            byte[] complete = ms.ToArray();
            return complete;
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
            string json = JsonSerializer.Serialize(packet);
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            await _stream.WriteAsync(data, 0, data.Length);
        }
    }
}