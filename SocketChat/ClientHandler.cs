using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SharedModels;
using EncryptionLibrary;

namespace Server {
    public class ClientHandler {
        public string ClientId { get; }
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly ChatServer _server;
        private readonly Dictionary<string, List<MessagePacket>> _packetBuffer;
        public RSAKeyPair.PublicKey? PublicKey;

        public bool HasPublicKey => PublicKey != null;

        public ClientHandler(string clientId, TcpClient tcpClient, ChatServer server) {
            ClientId = clientId;
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _server = server;
            _packetBuffer = new Dictionary<string, List<MessagePacket>>();
        }

        public async Task HandleClientAsync() {
            // First, send server's public key to client
            await SendServerPublicKeyAsync();

            var buffer = new byte[8192];
            var messageBuffer = new StringBuilder();

            try {
                while (_tcpClient.Connected) {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuffer.Append(data);

                    string bufferContent = messageBuffer.ToString();
                    string[] messages = bufferContent.Split('\n');

                    for (int i = 0; i < messages.Length - 1; i++) {
                        if (!string.IsNullOrWhiteSpace(messages[i])) {
                            await ProcessMessageAsync(messages[i]);
                        }
                    }

                    messageBuffer.Clear();
                    if (!string.IsNullOrWhiteSpace(messages[messages.Length - 1])) {
                        messageBuffer.Append(messages[messages.Length - 1]);
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Error handling client {ClientId}: {ex.Message}");
            } finally {
                Disconnect();
            }
        }

        private async Task SendServerPublicKeyAsync() {
            var serverKeyPacket = new MessagePacket {
                Type = MessageType.KeyExchange,
                Content = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                        E = _server.ServerPublicKey.E.ToString(),
                        N = _server.ServerPublicKey.N.ToString(),
                        IsServerKey = true
                    }))
                ),
                EncryptedSessionKey = "", // No session key for server key exchange
                SenderId = "SERVER"
            };

            await SendMessageAsync(JsonSerializer.Serialize(serverKeyPacket));
        }

        private async Task ProcessMessageAsync(string jsonMessage) {
            try {
                var packet = JsonSerializer.Deserialize<MessagePacket>(jsonMessage);
                if (packet == null) return;

                packet.SenderId = ClientId;

                switch (packet.Type) {
                    case MessageType.KeyExchange:
                        await HandleKeyExchange(packet);
                        break;

                    case MessageType.Text:
                        await HandleTextMessage(packet);
                        break;

                    case MessageType.File:
                    case MessageType.Photo:
                        await HandleFileMessage(packet);
                        break;

                    case MessageType.FileDownloadRequest:
                        await HandleFileDownloadRequest(packet);
                        break;
                }
            } catch (Exception ex) {
                Console.WriteLine($"Error processing message from {ClientId}: {ex.Message}");
            }
        }

        private async Task HandleKeyExchange(MessagePacket packet) {
            try {
                var keyData = JsonSerializer.Deserialize<dynamic>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(packet.Content)));

                // Store client's public key
                var eStr = keyData.GetProperty("E").GetString();
                var nStr = keyData.GetProperty("N").GetString();

                PublicKey = new RSAKeyPair.PublicKey(
                    System.Numerics.BigInteger.Parse(eStr),
                    System.Numerics.BigInteger.Parse(nStr)
                );
                Console.WriteLine($"Received public key from client {ClientId}");
            } catch (Exception ex) {
                Console.WriteLine($"Error handling key exchange from {ClientId}: {ex.Message}");
            }
        }

        private async Task HandleTextMessage(MessagePacket packet) {
            try {
                string decryptedMessage = HybridEncryption.Decrypt(packet.Content, packet.EncryptedSessionKey, _server.ServerPrivateKey);
                Console.WriteLine($"Text from {ClientId}: {decryptedMessage}");
                await _server.BroadcastMessageAsync(packet, ClientId);
            } catch (Exception ex) {
                Console.WriteLine($"Error decrypting message from {ClientId}: {ex.Message}");
            }
        }

        private async Task HandleFileMessage(MessagePacket packet) {
            if (!_packetBuffer.ContainsKey(packet.MessageId)) {
                _packetBuffer[packet.MessageId] = new List<MessagePacket>();
            }

            _packetBuffer[packet.MessageId].Add(packet);

            if (_packetBuffer[packet.MessageId].Count == packet.TotalPackets) {
                // Decrypt and reassemble file
                var encryptedFile = ReassembleFile(_packetBuffer[packet.MessageId]);
                var decryptedFile = HybridEncryption.Decrypt(encryptedFile, packet.EncryptedSessionKey, _server.ServerPrivateKey);

                _server.StoreFile(packet.MessageId, decryptedFile);

                // Broadcast file availability (re-encrypt for each client)
                (var content, var key) = HybridEncryption.Encrypt($"File available: {packet.FileName}", PublicKey);
                var notification = new MessagePacket {
                    Type = packet.Type,
                    Content = content,
                    EncryptedSessionKey = key,
                    FileName = packet.FileName,
                    FileSize = packet.FileSize,
                    MessageId = packet.MessageId,
                    SenderId = ClientId
                };

                await _server.BroadcastMessageAsync(notification, ClientId);
                _packetBuffer.Remove(packet.MessageId);
            }
        }

        private async Task HandleFileDownloadRequest(MessagePacket packet) {
            var decryptedContent = HybridEncryption.Decrypt(packet.Content, packet.EncryptedSessionKey, _server.ServerPrivateKey);
            var fileData = _server.GetFile(decryptedContent);
            if (fileData != null && PublicKey != null) {
                // Encrypt file for this specific client
                (var encryptedFileData, string key) = HybridEncryption.Encrypt(fileData, PublicKey);
                var chunks = ChunkData(encryptedFileData, 4096);

                for (int i = 0; i < chunks.Count; i++) {
                    var response = new MessagePacket {
                        Type = MessageType.FileDownloadResponse,
                        Content = Convert.ToBase64String(chunks[i]),
                        EncryptedSessionKey = key,
                        TotalPackets = chunks.Count,
                        PacketNumber = i + 1,
                        MessageId = packet.MessageId
                    };

                    await SendMessageAsync(JsonSerializer.Serialize(response));
                }
            }
        }

        private byte[] ReassembleFile(List<MessagePacket> packets) {
            packets.Sort((a, b) => a.PacketNumber.CompareTo(b.PacketNumber));

            using var ms = new MemoryStream();
            foreach (var packet in packets) {
                byte[] data = Convert.FromBase64String(packet.Content);
                ms.Write(data, 0, data.Length);
            }

            return ms.ToArray();
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

        public async Task SendMessageAsync(string message) {
            try {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                await _stream.WriteAsync(data, 0, data.Length);
            } catch (Exception ex) {
                Console.WriteLine($"Error sending message to {ClientId}: {ex.Message}");
            }
        }

        public void Disconnect() {
            try {
                _stream?.Close();
                _tcpClient?.Close();
                _server.RemoveClient(ClientId);
            } catch (Exception ex) {
                Console.WriteLine($"Error disconnecting client {ClientId}: {ex.Message}");
            }
        }
    }
}