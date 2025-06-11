using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SharedModels;
using EncryptionLibrary;
using System.Text.Json;

namespace ChatGUI;

public partial class MainPage : ContentPage {
    private ChatClientService _chatClient;
    private string _attachedFilePath;
    private MessageType _attachedFileType;

    public MainPage() {
        InitializeComponent();
        _chatClient = new ChatClientService();
        _chatClient.MessageReceived += OnMessageReceived;
        _chatClient.ConnectionStatusChanged += OnConnectionStatusChanged;
        _chatClient.FileReceived += OnFileReceived;
    }

    private async void OnConnectClicked(object sender, EventArgs e) {
        try {
            await _chatClient.ConnectAsync();
        } catch (Exception ex) {
            await DisplayAlert("Connection Error", ex.Message, "OK");
        }
    }

    private async void OnDisconnectClicked(object sender, EventArgs e) {
        await _chatClient.DisconnectAsync();
    }

    private async void OnSendClicked(object sender, EventArgs e) {
        if (string.IsNullOrWhiteSpace(MessageEntry.Text) && string.IsNullOrEmpty(_attachedFilePath))
            return;

        try {
            if (!string.IsNullOrEmpty(_attachedFilePath)) {
                await _chatClient.SendFileAsync(_attachedFilePath, _attachedFileType);
                ClearAttachment();
            }
            else {
                await _chatClient.SendTextMessageAsync(MessageEntry.Text);
                AddMessageToUI($"You: {MessageEntry.Text}", Colors.Blue);
            }

            MessageEntry.Text = string.Empty;
        } catch (Exception ex) {
            await DisplayAlert("Send Error", ex.Message, "OK");
        }
    }

    private async void OnAttachFileClicked(object sender, EventArgs e) {
        try {
            var result = await FilePicker.Default.PickAsync();
            if (result != null) {
                _attachedFilePath = result.FullPath;
                _attachedFileType = MessageType.File;
                AttachmentLabel.Text = $"File: {result.FileName}";
                ClearAttachmentButton.IsVisible = true;
            }
        } catch (Exception ex) {
            await DisplayAlert("File Error", ex.Message, "OK");
        }
    }

    private async void OnAttachPhotoClicked(object sender, EventArgs e) {
        try {
            var result = await FilePicker.Default.PickAsync(new PickOptions {
                PickerTitle = "Select a photo",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null) {
                _attachedFilePath = result.FullPath;
                _attachedFileType = MessageType.Photo;
                AttachmentLabel.Text = $"Photo: {result.FileName}";
                ClearAttachmentButton.IsVisible = true;
            }
        } catch (Exception ex) {
            await DisplayAlert("Photo Error", ex.Message, "OK");
        }
    }

    private void OnClearAttachmentClicked(object sender, EventArgs e) {
        ClearAttachment();
    }

    private void ClearAttachment() {
        _attachedFilePath = string.Empty;
        AttachmentLabel.Text = string.Empty;
        ClearAttachmentButton.IsVisible = false;
    }

    private void OnMessageReceived(object sender, MessageReceivedEventArgs e) {
        MainThread.BeginInvokeOnMainThread(() => {
            switch (e.Packet.Type) {
                case MessageType.Text:
                    AddMessageToUI($"{e.Packet.SenderId}: {e.DecryptedContent}", Colors.Green);
                    break;

                case MessageType.File:
                    AddFileMessageToUI(e.Packet, "📄");
                    break;

                case MessageType.Photo:
                    AddFileMessageToUI(e.Packet, "📷");
                    break;
            }
        });
    }

    private void OnConnectionStatusChanged(object sender, bool isConnected) {
        MainThread.BeginInvokeOnMainThread(() => {
            ConnectionStatusLabel.Text = isConnected ? "Connected" : "Disconnected";
            ConnectionStatusLabel.TextColor = isConnected ? Colors.Green : Colors.Red;
            ConnectButton.IsEnabled = !isConnected;
            DisconnectButton.IsEnabled = isConnected;
        });
    }

    private void OnFileReceived(object sender, FileReceivedEventArgs e) {
        MainThread.BeginInvokeOnMainThread(async () => {
            if (e.MessageType == MessageType.Photo) {
                await DisplayPhotoAsync(e.FilePath, e.FileName);
            }
            else {
                await DisplayAlert("File Downloaded", $"File saved: {e.FileName}", "OK");
            }
        });
    }

    private void AddMessageToUI(string message, Color color) {
        var label = new Label {
            Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
            TextColor = color,
            FontSize = 14,
            Margin = new Thickness(0, 2)
        };

        MessagesContainer.Children.Add(label);
        ScrollToBottom();
    }

    private void AddFileMessageToUI(MessagePacket packet, string icon) {
        var stackLayout = new StackLayout {
            Orientation = StackOrientation.Horizontal,
            Margin = new Thickness(0, 5)
        };

        var messageLabel = new Label {
            Text = $"[{DateTime.Now:HH:mm:ss}] {packet.SenderId}: {icon} {packet.FileName} ({packet.FileSize} bytes)",
            TextColor = Colors.Orange,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center
        };

        var downloadButton = new Button {
            Text = "Download",
            FontSize = 12,
            Padding = new Thickness(10, 5),
            BackgroundColor = Colors.LightBlue
        };

        downloadButton.Clicked += async (s, e) => {
            try {
                await _chatClient.RequestFileDownloadAsync(packet.MessageId);
                downloadButton.Text = "Downloading...";
                downloadButton.IsEnabled = false;
            } catch (Exception ex) {
                await DisplayAlert("Download Error", ex.Message, "OK");
            }
        };

        stackLayout.Children.Add(messageLabel);
        stackLayout.Children.Add(downloadButton);
        MessagesContainer.Children.Add(stackLayout);
        ScrollToBottom();
    }

    private async Task DisplayPhotoAsync(string filePath, string fileName) {
        try {
            var image = new Image {
                Source = ImageSource.FromFile(filePath),
                HeightRequest = 200,
                Aspect = Aspect.AspectFit,
                Margin = new Thickness(0, 5)
            };

            var photoContainer = new StackLayout {
                Children = {
                    new Label {
                        Text = $"[{DateTime.Now:HH:mm:ss}] Photo: {fileName}",
                        FontSize = 12,
                        TextColor = Colors.Gray
                    },
                    image
                }
            };

            MessagesContainer.Children.Add(photoContainer);
            ScrollToBottom();
        } catch (Exception ex) {
            await DisplayAlert("Photo Display Error", ex.Message, "OK");
        }
    }

    private async void ScrollToBottom() {
        await Task.Delay(100); // Small delay to ensure UI is updated
        await ChatScrollView.ScrollToAsync(0, MessagesContainer.Height, true);
    }
}