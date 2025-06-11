using ChatWpf;
using SharedModels;

namespace ChatWpf
{
    public class TextMessageViewModel : MessageViewModelBase
    {
        public string Content { get; }

        public TextMessageViewModel(MessagePacket packet, bool isOwnMessage = false)
            : base(packet, isOwnMessage)
        {
            Content = packet.DecryptedContent ?? "[Encrypted Message]";
        }

        public TextMessageViewModel(string content, string senderId, bool isOwnMessage = true)
            : base(new MessagePacket { SenderId = senderId }, isOwnMessage)
        {
            Content = content;
        }
    }
}