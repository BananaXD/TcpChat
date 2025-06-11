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

namespace Client {
    public class ChatClient {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private RSAKeyPair _clientEncryption; // For encrypting messages TO server
        private RSAKeyPair.PublicKey _serverPublicKey; // For encrypting messages TO server
        private bool _isConnected;
        private bool _keyExchangeComplete;
        private readonly Dictionary<string, List<MessagePacket>> _downloadBuffer;
        private string _clientId;
        private const string SERVER_HOST = "localhost";
        private const int SERVER_PORT = 8888;

        public ChatClient() {
            _downloadBuffer = new Dictionary<string, List<MessagePacket>>();
            _clientId = Environment.UserName + "_" + DateTime.Now.Ticks;
        }

        public async Task StartAsync() {
            try {
                await ConnectToServerAsync();
                Console.WriteLine("Connected to chat server!");
                Console.WriteLine("Waiting for key exchange...");

                // Start listening for messages
                _ = Task.Run(ListenForMessagesAsync);

                // Wait for key exchange to complete
                while (!_keyExchangeComplete) {
                    await Task.Delay(100);
                }

                Console.WriteLine("Encryption initialized!");
                Console.WriteLine("Commands:");
                Console.WriteLine("  /send <message> - Send text message");
                Console.WriteLine("  /file <path> - Send file");
                Console.WriteLine("  /photo <path> - Send photo");
                Console.WriteLine("  /download <fileId> - Download file");
                Console.WriteLine("  /quit - Exit");
                Console.WriteLine();


                // Handle user input
                await HandleUserInputAsync();
            } catch (Exception ex) {
                Console.WriteLine($"Error: {ex.Message}");
            } finally {
                Disconnect();
            }
        }

        private async Task ConnectToServerAsync() {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(SERVER_HOST, SERVER_PORT);
            _stream = _tcpClient.GetStream();
            _isConnected = true;

            // Generate client's own encryption keys
            _clientEncryption = RSAEncryption.GenerateKeyPair();
        }

        private async Task HandleUserInputAsync() {
            while (_isConnected) {
                Console.Write("> ");
                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input)) continue;

                if (!_keyExchangeComplete) {
                    Console.WriteLine("Please wait for key exchange to complete...");
                    continue;
                }

                try {
                    if (input.StartsWith("/")) {
                        await ProcessCommandAsync(input);
                    }
                    else {
                        await SendTextMessageAsync(input);
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"Error processing input: {ex.Message}");
                }
            }
        }

        private async Task ProcessCommandAsync(string command) {
            var parts = command.Split(' ', 2);
            var cmd = parts[0].ToLower();

            switch (cmd) {
                case "/send":
                    if (parts.Length > 1)
                        await SendTextMessageAsync(parts[1]);
                    else
                        Console.WriteLine("Usage: /send <message>");
                    break;

                case "/file":
                    if (parts.Length > 1)
                        await SendFileAsync(parts[1], MessageType.File);
                    else
                        Console.WriteLine("Usage: /file <path>");
                    break;

                case "/photo":
                    if (parts.Length > 1)
                        await SendFileAsync(parts[1], MessageType.Photo);
                    else
                        Console.WriteLine("Usage: /photo <path>");
                    break;

                case "/download":
                    if (parts.Length > 1)
                        await RequestFileDownloadAsync(parts[1]);
                    else
                        Console.WriteLine("Usage: /download <fileId>");
                    break;

                case "/quit":
                    _isConnected = false;
                    break;

                default:
                    Console.WriteLine("Unknown command. Type /quit to exit.");
                    break;
            }
        }

        private async Task SendTextMessageAsync(string message) {
            // Encrypt message with SERVER's public key
            (var encryptedMessage, var key) = HybridEncryption.Encrypt(message, _serverPublicKey);

            var packet = new MessagePacket {
                Type = MessageType.Text,
                Content = encryptedMessage,
                EncryptedSessionKey = key,
                SenderId = _clientId
            };

            await SendPacketAsync(packet);
            Console.WriteLine($"[You]: {message}");
        }

        private async Task SendFileAsync(string filePath, MessageType messageType) {
            if (!File.Exists(filePath)) {
                Console.WriteLine("File not found!");
                return;
            }

            try {
                byte[] fileData = await File.ReadAllBytesAsync(filePath);
                // Encrypt with SERVER's encryption
                (byte[] encryptedData, var key) = HybridEncryption.Encrypt(fileData, _serverPublicKey);

                string fileName = Path.GetFileName(filePath);
                string messageId = Guid.NewGuid().ToString();

                var chunks = ChunkData(encryptedData, 4096);

                Console.WriteLine($"Sending {messageType.ToString().ToLower()}: {fileName} ({fileData.Length} bytes, {chunks.Count} packets)");

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
                    Console.Write($"\rProgress: {i + 1}/{chunks.Count}");
                }

                Console.WriteLine($"\n{messageType} sent successfully!");
            } catch (Exception ex) {
                Console.WriteLine($"Error sending file: {ex.Message}");
            }
        }

        private async Task RequestFileDownloadAsync(string fileId) {
            (var content, var key) = HybridEncryption.Encrypt(fileId, _serverPublicKey);
            var packet = new MessagePacket {
                Type = MessageType.FileDownloadRequest,
                Content = content,
                EncryptedSessionKey = key,
                SenderId = _clientId
            };

            await SendPacketAsync(packet);
            Console.WriteLine($"Requesting download for file: {fileId}");
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
                    Console.WriteLine($"\nConnection error: {ex.Message}");
                    _isConnected = false;
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
            } catch (Exception ex) {
                Console.WriteLine($"Error processing incoming message: {ex.Message}");
            }
        }

        private async Task HandleKeyExchange(MessagePacket packet) {
            try {
                var keyData = JsonSerializer.Deserialize<JsonElement>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(packet.Content)));

                if (packet.SenderId == "SERVER") {
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
                    Console.WriteLine("Key exchange completed with server!");
                }
                else {
                    // Key exchange from another client (handled by server)
                    Console.WriteLine($"[System] Key exchange from {packet.SenderId}");
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

        private void HandleTextMessage(MessagePacket packet) {
            try {
                // Decrypt message with CLIENT's private key (server re-encrypted it for us)
                string decryptedMessage = HybridEncryption.Decrypt(packet.Content, packet.EncryptedSessionKey, _clientEncryption.Private);
                Console.WriteLine($"\n[{packet.SenderId}]: {decryptedMessage}");
                Console.Write("> ");
            } catch (Exception ex) {
                Console.WriteLine($"\n[{packet.SenderId}]: [Decryption failed: {ex.Message}]");
                Console.Write("> ");
            }
        }

        private void HandleFileNotification(MessagePacket packet) {
            string fileType = packet.Type == MessageType.Photo ? "Photo" : "File";
            Console.WriteLine($"\n[{packet.SenderId}] {fileType} available: {packet.FileName} ({packet.FileSize} bytes)");
            Console.WriteLine($"Use '/download {packet.MessageId}' to download");
            Console.Write("> ");
        }

        private async Task HandleFileDownloadResponse(MessagePacket packet) {
            if (!_downloadBuffer.ContainsKey(packet.MessageId)) {
                _downloadBuffer[packet.MessageId] = new List<MessagePacket>();
            }

            _downloadBuffer[packet.MessageId].Add(packet);

            if (_downloadBuffer[packet.MessageId].Count == packet.TotalPackets) {
                var fileData = ReassembleAndDecryptFile(_downloadBuffer[packet.MessageId]);
                string fileName = $"downloaded_{packet.MessageId}";

                await File.WriteAllBytesAsync(fileName, fileData);
                Console.WriteLine($"\nFile downloaded: {fileName} ({fileData.Length} bytes)");
                Console.Write("> ");

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
            // Decrypt with CLIENT's private key
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

        private void Disconnect() {
            _isConnected = false;
            _stream?.Close();
            _tcpClient?.Close();
            Console.WriteLine("Disconnected from server.");
        }
    }
}