using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedModels
{
    public enum MessageType
    {
        Text,
        File,
        Photo,
        KeyExchange,
        KeyExchangeResponse,
        FileDownloadRequest,
        FileDownloadResponse,
        Heartbeat
    }

    public class MessagePacket
    {
        [JsonPropertyName("type")]
        public MessageType Type { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("encryptedSessionKey")]
        public string EncryptedSessionKey { get; set; } = string.Empty; // RSA-encrypted Playfair key

        [JsonPropertyName("totalPackets")]
        public int TotalPackets { get; set; } = 1;

        [JsonPropertyName("packetNumber")]
        public int PacketNumber { get; set; } = 1;

        [JsonPropertyName("fileName")]
        public string? FileName { get; set; }

        [JsonPropertyName("fileSize")]
        public long? FileSize { get; set; }

        [JsonPropertyName("senderId")]
        public string? SenderId { get; set; }

        [JsonPropertyName("recipientId")]
        public string? RecipientId { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("messageId")]
        public string MessageId { get; set; } = Guid.NewGuid().ToString();

        // New properties for GUI support
        public string DecryptedContent { get; set; }
        public byte[] DecryptedFileData { get; set; }
    }
}
