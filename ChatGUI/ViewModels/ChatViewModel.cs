using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChatMaui.ViewModels;

public class ChatViewModel : INotifyPropertyChanged {
    private bool _isConnected;
    private string _connectionStatus = "Connecting...";

    public ObservableCollection<MessageViewModelBase> Messages { get; }

    public bool IsConnected {
        get => _isConnected;
        set {
            _isConnected = value;
            OnPropertyChanged();
        }
    }

    public string ConnectionStatus {
        get => _connectionStatus;
        set {
            _connectionStatus = value;
            OnPropertyChanged();
        }
    }

    public ChatViewModel() {
        Messages = new ObservableCollection<MessageViewModelBase>();
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}