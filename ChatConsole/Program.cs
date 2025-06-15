using Client;
using EncryptionLibrary;
using SharedModels;

bool _keyExchangeCompleted = false;

var client = new ChatClient();
client.ConnectionStatusChanged += (s, e) => Console.WriteLine(e.Message);
client.MessageReceived += Client_MessageReceived;
client.KeyExchangeCompleted += (s, e) => { _keyExchangeCompleted = true; Console.WriteLine("Key exchange completed. You can now send messages."); } ;
await client.ConnectAsync();


void Client_MessageReceived(object? sender, MessageReceivedEventArgs e) {
    switch (e.Packet.Type) {
        case MessageType.Text:
            Console.WriteLine($"[{e.Packet.SenderId}]: {e.DecryptedContent}");
            break;
        case MessageType.File:
            Console.WriteLine($"[{e.Packet.SenderId}] sent a file: {e.Packet.FileName} ({e.Packet.FileSize.Value.ToString("N")})");
            break;
        case MessageType.Photo:
            Console.WriteLine($"[{e.Packet.SenderId}] sent a photo: {e.Packet.FileName} ({e.Packet.FileSize.Value.ToString("N")})");
            break;
        case MessageType.KeyExchange:
            Console.WriteLine($"Key exchange initiated by {e.Packet.SenderId}. Waiting for response...");
            break;
        case MessageType.KeyExchangeResponse:
            Console.WriteLine($"Key exchange response received from {e.Packet.SenderId}. You can now send messages securely.");
            break;
        case MessageType.FileDownloadRequest:
            Console.WriteLine($"File download requested by {e.Packet.SenderId} for file: {e.Packet.FileName}");
            break;
        case MessageType.FileDownloadResponse:
            Console.WriteLine($"File download response received from {e.Packet.SenderId} for file: {e.Packet.FileName}");
            break;
        case MessageType.Heartbeat:
            Console.WriteLine($"Heartbeat received from {e.Packet.SenderId} at {e.Packet.Timestamp}");
            break;
    }
}


async Task HandleUserInputAsync() {
    while (client.IsConnected) {
        Console.Write("> ");
        string? input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input)) continue;

        if (!_keyExchangeCompleted) {
            Console.WriteLine("Please wait for key exchange to complete...");
            continue;
        }

        try {
            if (input.StartsWith("/")) {
                await ProcessCommandAsync(input);
            }
            else {
                await client.SendTextMessageAsync(input);
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error processing input: {ex.Message}");
        }
    }
}

async Task ProcessCommandAsync(string command) {
    var parts = command.Split(' ', 2);
    var cmd = parts[0].ToLower();

    switch (cmd) {
        case "/send":
            if (parts.Length > 1)
                await client.SendTextMessageAsync(parts[1]);
            else
                Console.WriteLine("Usage: /send <message>");
            break;

        case "/file":
            if (parts.Length > 1)
                await client.SendFileAsync(parts[1], MessageType.File);
            else
                Console.WriteLine("Usage: /file <path>");
            break;

        case "/photo":
            if (parts.Length > 1)
                await client.SendFileAsync(parts[1], MessageType.Photo);
            else
                Console.WriteLine("Usage: /photo <path>");
            break;

        case "/download":
            if (parts.Length > 1)
                await client.RequestFileDownloadAsync(parts[1]);
            else
                Console.WriteLine("Usage: /download <fileId>");
            break;

        case "/quit":
            await client.DisconnectAsync();
            break;

        default:
            Console.WriteLine("Unknown command. Type /quit to exit.");
            break;
    }
}

await HandleUserInputAsync();