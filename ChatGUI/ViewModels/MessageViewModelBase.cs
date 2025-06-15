using Client;
using SharedModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChatMaui.ViewModels;

public abstract class MessageViewModelBase : INotifyPropertyChanged {
    protected MessageReceivedEventArgs _message;

    public MessageReceivedEventArgs Message => _message;
    public string SenderId => _message.Packet.SenderId;
    public string SenderName => _message.Packet.SenderId?.Split('_')[0];
    public DateTime Timestamp { get; }
    public string TimestampFormatted => Timestamp.ToString("HH:mm");
    public bool IsOwnMessage { get; }

    protected MessageViewModelBase(MessageReceivedEventArgs message) {
        _message = message;
        Timestamp = DateTime.Now;
        IsOwnMessage = message.IsOwnMessage;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}