using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharedModels;
using EncryptionLibrary;

namespace Server {
    public class ChatServer {
        private TcpListener _listener;
        private readonly ConcurrentDictionary<string, ClientHandler> _clients;
        private readonly ConcurrentDictionary<string, byte[]> _fileStorage;
        private readonly RSAKeyPair _serverEncryption; // Server's own encryption
        private bool _isRunning;
        private const int PORT = 4933;

        public ChatServer() {
            _clients = new ConcurrentDictionary<string, ClientHandler>();
            _fileStorage = new ConcurrentDictionary<string, byte[]>();
            _serverEncryption = RSAEncryption.GenerateKeyPair(); // Server generates its own keys
        }

        public RSAKeyPair.PublicKey ServerPublicKey => _serverEncryption.Public;
        public RSAKeyPair.PrivateKey ServerPrivateKey => _serverEncryption.Private;

        public async Task StartAsync() {
            _listener = new TcpListener(IPAddress.Any, PORT);
            _listener.Start();
            _isRunning = true;

            Console.WriteLine($"Server started on port {PORT}");
            Console.WriteLine($"Server Public Key - E: {_serverEncryption.Public.E}");
            Console.WriteLine($"Server Public Key - N: {_serverEncryption.Public.N}");
            Console.WriteLine("Press 'q' to quit");

            // Handle quit command
            _ = Task.Run(() => {
                while (_isRunning) {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q') {
                        Stop();
                        break;
                    }
                }
            });

            // Accept clients
            while (_isRunning) {
                try {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    var clientId = Guid.NewGuid().ToString();
                    var clientHandler = new ClientHandler(clientId, tcpClient, this);

                    _clients.TryAdd(clientId, clientHandler);
                    Console.WriteLine($"Client {clientId} connected");

                    _ = Task.Run(() => clientHandler.HandleClientAsync());
                } catch (ObjectDisposedException) {
                    break;
                }
            }
        }

        public async Task BroadcastMessageAsync(MessagePacket originalPacket, string senderId) {
            // Decrypt the message first
            string decryptedContent = HybridEncryption.Decrypt(originalPacket.Content, originalPacket.EncryptedSessionKey, ServerPrivateKey);

            var tasks = new List<Task>();

            foreach (var client in _clients.Values) {
                if (client.ClientId != senderId && client.HasPublicKey) {
                    (var content, var key) = HybridEncryption.Encrypt(decryptedContent, client.PublicKey);
                    // Re-encrypt for each client
                    var reencryptedPacket = new MessagePacket {
                        Type = originalPacket.Type,
                        Content = content,
                        EncryptedSessionKey = key,
                        TotalPackets = originalPacket.TotalPackets,
                        PacketNumber = originalPacket.PacketNumber,
                        FileName = originalPacket.FileName,
                        FileSize = originalPacket.FileSize,
                        SenderId = senderId,
                        MessageId = originalPacket.MessageId,
                        Timestamp = originalPacket.Timestamp
                    };

                    var json = JsonSerializer.Serialize(reencryptedPacket);
                    tasks.Add(client.SendMessageAsync(json));
                }
            }

            await Task.WhenAll(tasks);
        }

        public async Task BroadcastFileAsync(MessagePacket originalPacket, string senderId, byte[] decryptedFileChunk) {
            var tasks = new List<Task>();

            foreach (var client in _clients.Values) {
                if (client.ClientId != senderId && client.HasPublicKey) {
                    // Re-encrypt file chunk for each client
                    (byte[] reencryptedChunk, string key) = HybridEncryption.Encrypt(decryptedFileChunk, client.PublicKey);

                    var reencryptedPacket = new MessagePacket {
                        Type = originalPacket.Type,
                        Content = Convert.ToBase64String(reencryptedChunk),
                        EncryptedSessionKey = key,
                        TotalPackets = originalPacket.TotalPackets,
                        PacketNumber = originalPacket.PacketNumber,
                        FileName = originalPacket.FileName,
                        FileSize = originalPacket.FileSize,
                        SenderId = senderId,
                        MessageId = originalPacket.MessageId,
                        Timestamp = originalPacket.Timestamp
                    };

                    var json = JsonSerializer.Serialize(reencryptedPacket);
                    tasks.Add(client.SendMessageAsync(json));
                }
            }

            await Task.WhenAll(tasks);
        }

        public void Stop() {
            _isRunning = false;
            _listener?.Stop();

            foreach (var client in _clients.Values) {
                client.Disconnect();
            }

            Console.WriteLine("Server stopped");
        }

        public void RemoveClient(string clientId) {
            _clients.TryRemove(clientId, out _);
            Console.WriteLine($"Client {clientId} disconnected");
        }

        public void StoreFile(string fileId, byte[] fileData) {
            _fileStorage.TryAdd(fileId, fileData);
            Console.WriteLine($"File {fileId} stored ({fileData.Length} bytes)");
        }

        public byte[]? GetFile(string fileId) {
            _fileStorage.TryGetValue(fileId, out var fileData);
            return fileData;
        }
    }
}