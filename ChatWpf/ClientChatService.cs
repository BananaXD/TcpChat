using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharedModels;
using EncryptionLibrary;

namespace ChatWpf
{
    public class ChatClient
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private RSAKeyPair _clientEncryption;
        private RSAKeyPair.PublicKey _serverPublicKey;
        private bool _isConnected;
        private bool _keyExchangeComplete;
        private readonly Dictionary<string, List<MessagePacket>> _downloadBuffer;
        private string _clientId;
        private const string SERVER_HOST = "localhost";
        private const int SERVER_PORT = 8888;

        // Events for GUI integration
        public event Action Connected;
        public event Action Disconnected;
        public event Action<MessagePacket> MessageReceived;
        public event Action KeyExchangeCompleted;
        public event Action<string> StatusChanged;

        public string ClientId => _clientId;
        public bool IsConnected => _isConnected && _keyExchangeComplete;

        public ChatClient()
        {
            _downloadBuffer = new Dictionary<string, List<MessagePacket>>();
            _clientId = Environment.UserName + "_" + DateTime.Now.Ticks;
        }

        public async Task StartAsync()
        {
            try
            {
                await ConnectToServerAsync();
                StatusChanged?.Invoke("Connected to server");
                Connected?.Invoke();

                // Start listening for messages
                _ = Task.Run(ListenForMessagesAsync);

                // Wait for key exchange to complete
                StatusChanged?.Invoke("Waiting for key exchange...");
                while (!_keyExchangeComplete && _isConnected)
                {
                    await Task.Delay(100);
                }

                if (_keyExchangeComplete)
                {
                    StatusChanged?.Invoke("Ready");
                    KeyExchangeCompleted?.Invoke();
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Connection failed: {ex.Message}");
                throw;
            }
        }

        private async Task ConnectToServerAsync()
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(SERVER_HOST, SERVER_PORT);
            _stream = _tcpClient.GetStream();
            _isConnected = true;

            // Generate client's own encryption keys
            _clientEncryption = RSAEncryption.GenerateKeyPair();
        }

        public async Task SendTextMessageAsync(string message)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected or key exchange not complete");

            try
            {
                // Encrypt message with SERVER's public key
                (var encryptedMessage, var key) = HybridEncryption.Encrypt(message, _serverPublicKey);

                var packet = new MessagePacket
                {
                    Type = MessageType.Text,
                    Content = encryptedMessage,
                    EncryptedSessionKey = key,
                    SenderId = _clientId,
                    DecryptedContent = message // Store for local display
                };

                await SendPacketAsync(packet);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Failed to send message: {ex.Message}");
                throw;
            }
        }

        public async Task SendFileAsync(string filePath, MessageType messageType)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected or key exchange not complete");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found!");

            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(filePath);
                // Encrypt with SERVER's encryption
                (byte[] encryptedData, var key) = HybridEncryption.Encrypt(fileData, _serverPublicKey);

                string fileName = Path.GetFileName(filePath);
                string messageId = Guid.NewGuid().ToString();

                var chunks = ChunkData(encryptedData, 4096);

                StatusChanged?.Invoke($"Sending {messageType.ToString().ToLower()}: {fileName}...");

                for (int i = 0; i < chunks.Count; i++)
                {
                    var packet = new MessagePacket
                    {
                        Type = messageType,
                        Content = Convert.ToBase64String(chunks[i]),
                        EncryptedSessionKey = key,
                        TotalPackets = chunks.Count,
                        PacketNumber = i + 1,
                        FileName = fileName,
                        FileSize = fileData.Length,
                        MessageId = messageId,
                        SenderId = _clientId,
                        DecryptedFileData = fileData // Store for local display
                    };

                    await SendPacketAsync(packet);
                }

                StatusChanged?.Invoke($"{messageType} sent successfully!");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Failed to send file: {ex.Message}");
                throw;
            }
        }

        public async Task RequestFileDownloadAsync(string fileId)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected or key exchange not complete");

            try
            {
                (var content, var key) = HybridEncryption.Encrypt(fileId, _serverPublicKey);
                var packet = new MessagePacket
                {
                    Type = MessageType.FileDownloadRequest,
                    Content = content,
                    EncryptedSessionKey = key,
                    SenderId = _clientId
                };

                await SendPacketAsync(packet);
                StatusChanged?.Invoke($"Requesting download for file: {fileId}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Failed to request download: {ex.Message}");
                throw;
            }
        }

        private async Task ListenForMessagesAsync()
        {
            var buffer = new byte[8192];
            var messageBuffer = new StringBuilder();

            try
            {
                while (_isConnected && _tcpClient.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuffer.Append(data);

                    string bufferContent = messageBuffer.ToString();
                    string[] messages = bufferContent.Split('\n');

                    for (int i = 0; i < messages.Length - 1; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(messages[i]))
                        {
                            await ProcessIncomingMessageAsync(messages[i]);
                        }
                    }

                    messageBuffer.Clear();
                    if (!string.IsNullOrWhiteSpace(messages[messages.Length - 1]))
                    {
                        messageBuffer.Append(messages[messages.Length - 1]);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isConnected)
                {
                    StatusChanged?.Invoke($"Connection error: {ex.Message}");
                    _isConnected = false;
                    Disconnected?.Invoke();
                }
            }
        }

        private async Task ProcessIncomingMessageAsync(string jsonMessage)
        {
            try
            {
                var packet = JsonSerializer.Deserialize<MessagePacket>(jsonMessage);
                if (packet == null) return;

                switch (packet.Type)
                {
                    case MessageType.KeyExchange:
                        await HandleKeyExchange(packet);
                        break;

                    case MessageType.Text:
                        HandleTextMessage(packet);
                        break;

                    case MessageType.File:
                    case MessageType.Photo:
                        HandleFileNotification(packet);
                        break;

                    case MessageType.FileDownloadResponse:
                        await HandleFileDownloadResponse(packet);
                        break;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error processing message: {ex.Message}");
            }
        }

        private async Task HandleKeyExchange(MessagePacket packet)
        {
            try
            {
                var keyData = JsonSerializer.Deserialize<JsonElement>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(packet.Content)));

                if (packet.SenderId == "SERVER")
                {
                    // This is the server's public key
                    var eStr = keyData.GetProperty("E").GetString();
                    var nStr = keyData.GetProperty("N").GetString();

                    _serverPublicKey = new RSAKeyPair.PublicKey(
                        System.Numerics.BigInteger.Parse(eStr),
                        System.Numerics.BigInteger.Parse(nStr)
                    );

                    // Send our public key to server
                    await SendClientPublicKeyAsync();

                    _keyExchangeComplete = true;
                    StatusChanged?.Invoke("Encryption initialized!");
                    KeyExchangeCompleted?.Invoke();
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Key exchange failed: {ex.Message}");
            }
        }

        private async Task SendClientPublicKeyAsync()
        {
            var keyExchangePacket = new MessagePacket
            {
                Type = MessageType.KeyExchange,
                Content = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        E = _clientEncryption.Public.E.ToString(),
                        N = _clientEncryption.Public.N.ToString()
                    }))
                ),
                EncryptedSessionKey = string.Empty,
                SenderId = _clientId
            };

            await SendPacketAsync(keyExchangePacket);
        }

        private void HandleTextMessage(MessagePacket packet)
        {
            try
            {
                // Decrypt message with CLIENT's private key
                string decryptedMessage = HybridEncryption.Decrypt(
                    packet.Content,
                    packet.EncryptedSessionKey,
                    _clientEncryption.Private);

                packet.DecryptedContent = decryptedMessage;
                MessageReceived?.Invoke(packet);
            }
            catch (Exception ex)
            {
                packet.DecryptedContent = $"[Decryption failed: {ex.Message}]";
                MessageReceived?.Invoke(packet);
            }
        }

        private void HandleFileNotification(MessagePacket packet)
        {
            // For file notifications, we don't decrypt here - just notify the UI
            MessageReceived?.Invoke(packet);
        }

        private async Task HandleFileDownloadResponse(MessagePacket packet)
        {
            try
            {
                if (!_downloadBuffer.ContainsKey(packet.MessageId))
                {
                    _downloadBuffer[packet.MessageId] = new List<MessagePacket>();
                }

                _downloadBuffer[packet.MessageId].Add(packet);

                if (_downloadBuffer[packet.MessageId].Count == packet.TotalPackets)
                {
                    var fileData = ReassembleAndDecryptFile(_downloadBuffer[packet.MessageId]);

                    // Create a complete packet with decrypted data
                    var completePacket = new MessagePacket
                    {
                        Type = packet.Type,
                        FileName = packet.FileName,
                        FileSize = packet.FileSize,
                        MessageId = packet.MessageId,
                        SenderId = packet.SenderId,
                        DecryptedFileData = fileData
                    };

                    MessageReceived?.Invoke(completePacket);
                    _downloadBuffer.Remove(packet.MessageId);

                    StatusChanged?.Invoke($"File downloaded: {packet.FileName}");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Download failed: {ex.Message}");
            }
        }

        private byte[] ReassembleAndDecryptFile(List<MessagePacket> packets)
        {
            packets.Sort((a, b) => a.PacketNumber.CompareTo(b.PacketNumber));

            using var ms = new MemoryStream();
            foreach (var packet in packets)
            {
                byte[] data = Convert.FromBase64String(packet.Content);
                ms.Write(data, 0, data.Length);
            }

            byte[] encryptedData = ms.ToArray();

            // Decrypt with CLIENT's private key
            return HybridEncryption.Decrypt(
                encryptedData,
                packets[0].EncryptedSessionKey,
                _clientEncryption.Private);
        }

        private List<byte[]> ChunkData(byte[] data, int chunkSize)
        {
            var chunks = new List<byte[]>();
            for (int i = 0; i < data.Length; i += chunkSize)
            {
                int size = Math.Min(chunkSize, data.Length - i);
                byte[] chunk = new byte[size];
                Array.Copy(data, i, chunk, 0, size);
                chunks.Add(chunk);
            }
            return chunks;
        }

        private async Task SendPacketAsync(MessagePacket packet)
        {
            if (!_isConnected || _stream == null)
                throw new InvalidOperationException("Not connected to server");

            string json = JsonSerializer.Serialize(packet);
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            await _stream.WriteAsync(data, 0, data.Length);
        }

        public void Disconnect()
        {
            _isConnected = false;
            _keyExchangeComplete = false;

            try
            {
                _stream?.Close();
                _tcpClient?.Close();
            }
            catch { }

            StatusChanged?.Invoke("Disconnected");
            Disconnected?.Invoke();
        }
    }
}