using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SharedModels;
using EncryptionLibrary;
using System.Text.Json;
using Client;
using Microsoft.Extensions.Logging;
using ChatMaui.ViewModels;

namespace ChatMaui;
public partial class MainPage : ContentPage {
    private readonly ChatClient _chatClient;
    private readonly ChatViewModel _viewModel;
    private readonly ILogger<MainPage> _logger;

    public MainPage(ILogger<MainPage> logger) {
        InitializeComponent();
        _logger = logger;
        _viewModel = new ChatViewModel();
        BindingContext = _viewModel;

        _chatClient = new ChatClient();
        InitializeChatClient();
    }

    private void InitializeChatClient() {
        _chatClient.ConnectionStatusChanged += OnConnectionStatusChanged;
        _chatClient.MessageReceived += OnMessageReceived;
        _chatClient.KeyExchangeCompleted += OnKeyExchangeCompleted;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        await StartChatClient();
    }

    private async Task StartChatClient() {
        try {
            _viewModel.ConnectionStatus = "Connecting...";
            await _chatClient.ConnectAsync();
        } catch (Exception ex) {
            _viewModel.ConnectionStatus = "Connection Failed";
            await DisplayAlert("Connection Error", $"Failed to connect: {ex.Message}", "OK");
        }
    }

    private void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e) {
        MainThread.BeginInvokeOnMainThread(() => {
            _viewModel.ConnectionStatus = e.Message;
            _viewModel.IsConnected = e.IsConnected;
        });
    }

    private void OnKeyExchangeCompleted(object sender, EventArgs e) {
        MainThread.BeginInvokeOnMainThread(() => {
            _viewModel.ConnectionStatus = "Ready";
        });
    }

    private void OnMessageReceived(object sender, MessageReceivedEventArgs message) {
        MainThread.BeginInvokeOnMainThread(() => {
            MessageViewModelBase messageViewModel = message.Packet.Type switch {
                MessageType.Text => new TextMessageViewModel(message),
                MessageType.Photo => new PhotoMessageViewModel(message),
                MessageType.File => new FileMessageViewModel(message),
                _ => null
            };

            if (messageViewModel != null) {
                _viewModel.Messages.Add(messageViewModel);
                ScrollToBottom();
            }

            // Handle file data updates
            if (message.Packet.Type == MessageType.Photo ||
                message.Packet.Type == MessageType.FileDownloadResponse) {
                var existingMessage = _viewModel.Messages
                    .FirstOrDefault(m => m.Message.Packet.MessageId == message.Packet.MessageId);

                if (existingMessage != null) {
                    existingMessage.Message.DecryptedFileData = message.DecryptedFileData;
                    if (existingMessage is PhotoMessageViewModel photoMessage) {
                        photoMessage.LoadImage();
                    }
                }
            }
        });
    }

    private async void OnSendMessage(object sender, EventArgs e) {
        await SendTextMessage();
    }

    private async Task SendTextMessage() {
        var message = MessageEntry.Text?.Trim();
        if (string.IsNullOrEmpty(message) || !_viewModel.IsConnected)
            return;

        try {
            await _chatClient.SendTextMessageAsync(message);
            MessageEntry.Text = string.Empty;
            ScrollToBottom();
        } catch (Exception ex) {
            await DisplayAlert("Send Error", $"Failed to send message: {ex.Message}", "OK");
        }
    }

    private async void OnAttachFile(object sender, EventArgs e) {
        if (!_viewModel.IsConnected) return;

        try {
            var result = await FilePicker.PickAsync();
            if (result != null) {
                await _chatClient.SendFileAsync(result.FullPath, MessageType.File);
                ScrollToBottom();
            }
        } catch (Exception ex) {
            await DisplayAlert("Send Error", $"Failed to send file: {ex.Message}", "OK");
        }
    }

    private async void OnAttachPhoto(object sender, EventArgs e) {
        if (!_viewModel.IsConnected) return;

        try {
            var result = await MediaPicker.PickPhotoAsync();
            if (result != null) {
                await _chatClient.SendFileAsync(result.FullPath, MessageType.Photo);
                ScrollToBottom();
            }
        } catch (Exception ex) {
            await DisplayAlert("Send Error", $"Failed to send photo: {ex.Message}", "OK");
        }
    }

    public async Task RequestFileDownload(string messageId) {
        try {
            await _chatClient.RequestFileDownloadAsync(messageId);
        } catch (Exception ex) {
            await DisplayAlert("Download Error", $"Failed to request download: {ex.Message}", "OK");
        }
    }

    private void ScrollToBottom() {
        if (_viewModel.Messages.Count > 0) {
            MessagesCollectionView.ScrollTo(_viewModel.Messages.Last(), position: ScrollToPosition.End);
        }
    }

    protected override async void OnDisappearing() {
        await _chatClient?.DisconnectAsync();
        base.OnDisappearing();
    }

    private async void OnDownloadFile(object sender, EventArgs e) {
        if (sender is Button button && button.BindingContext is FileMessageViewModel viewModel) {
            await viewModel.DownloadFile();
        }
    }
}