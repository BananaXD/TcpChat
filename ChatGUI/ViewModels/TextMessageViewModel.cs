using Client;

namespace ChatMaui.ViewModels;

public class TextMessageViewModel : MessageViewModelBase {
    public string Content { get; }

    public TextMessageViewModel(MessageReceivedEventArgs message) : base(message) {
        Content = message.DecryptedContent ?? "[Encrypted Message]";
    }
}