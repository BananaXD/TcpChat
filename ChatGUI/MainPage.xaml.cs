using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SharedModels;
using EncryptionLibrary;
using System.Text.Json;
using Client;
using Microsoft.Extensions.Logging;
using ChatMaui.ViewModels;
using CommunityToolkit.Maui.Core;
using ChatMaui.Popups;
using CommunityToolkit.Maui.Views;

namespace ChatMaui;
public partial class MainPage : ContentPage {
    private readonly ChatClient _chatClient;
    private readonly ChatViewModel _viewModel;
    private readonly ILogger<MainPage> _logger;
    private readonly IPopupService _popupService;

    public MainPage(ILogger<MainPage> logger, IPopupService popupService) {
        InitializeComponent();
        _logger = logger;
        _popupService = popupService;
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
        string ipAddress = null; // Start with no IP, can be set to a default

        while (!_viewModel.IsConnected) {
            try {
                // Update status before attempting to connect
                var status = string.IsNullOrWhiteSpace(ipAddress)
                    ? "Connecting..."
                    : $"Connecting to {ipAddress}...";
                MainThread.BeginInvokeOnMainThread(() => {
                    _viewModel.ConnectionStatus = status;
                });

                // Pass the IP to ConnectAsync. A null value could mean "use default".
                await _chatClient.ConnectAsync(ipAddress);

                // If ConnectAsync completes without an exception, the loop will exit
                // because OnConnectionStatusChanged will set IsConnected to true.

            } catch (Exception ex) {
                _logger.LogError(ex, "Connection failed.");

                // Use the custom popup instead of DisplayPromptAsync
                var result = await this.ShowPopupAsync(new InputPromptPopup());

                // The result is the object passed to the Close() method
                ipAddress = result as string;

                if (string.IsNullOrWhiteSpace(ipAddress)) {
                    MainThread.BeginInvokeOnMainThread(() => {
                        _viewModel.ConnectionStatus = "Connection Canceled";
                    });
                    break;
                }
            }
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