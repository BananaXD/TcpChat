using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SharedModels;

namespace ChatWpf
{
    public abstract class MessageViewModelBase : INotifyPropertyChanged
    {
        protected MessagePacket _packet;

        public string SenderId => _packet.SenderId;
        public string SenderName => _packet.SenderId.Split('_')[0];
        public DateTime Timestamp { get; }
        public string TimestampFormatted => Timestamp.ToString("HH:mm");
        public bool IsOwnMessage { get; }

        protected MessageViewModelBase(MessagePacket packet, bool isOwnMessage = false)
        {
            _packet = packet;
            Timestamp = DateTime.Now;
            IsOwnMessage = isOwnMessage;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}