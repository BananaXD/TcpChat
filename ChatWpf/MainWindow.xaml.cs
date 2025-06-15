using ChatWpf;
using Client;
using Microsoft.Win32;
using SharedModels;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChatWpf
{
    public partial class MainWindow : Window
    {
        private ChatClient _chatClient;
        private ChatViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new ChatViewModel();
            DataContext = _viewModel;

            InitializeChatClient();
        }

        private void InitializeChatClient()
        {
            _chatClient = new ChatClient();
            _chatClient.ConnectionStatusChanged += OnConnectionStatusChanged;
            _chatClient.MessageReceived += OnMessageReceived;
            _chatClient.KeyExchangeCompleted += OnKeyExchangeCompleted;
            //_chatClient.FileTransferProgress += OnFileTransferProgress;

            Loaded += async (s, e) => await StartChatClient();
        }

        private void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e) {
            _viewModel.ConnectionStatus = e.Message;
            _viewModel.IsConnected = e.IsConnected;
        }

        private async Task StartChatClient() {
            string? ipAddress = null; // Start with a default IP (or null)

            while (!_viewModel.IsConnected) {
                try {
                    // Update status on the UI thread before attempting to connect
                    var status = string.IsNullOrWhiteSpace(ipAddress)
                        ? "Connecting..."
                        : $"Connecting to {ipAddress}...";
                    Dispatcher.Invoke(() => _viewModel.ConnectionStatus = status);

                    // Pass the IP to ConnectAsync.
                    // Assumes ChatClient.ConnectAsync is modified to accept an IP.
                    await _chatClient.ConnectAsync(ipAddress);

                    // If ConnectAsync succeeds, the loop will exit on the next check
                    // because OnConnectionStatusChanged will set IsConnected to true.
                } catch (Exception ex) {
                    // Prompt user for a new IP address on the UI thread
                    ipAddress = Dispatcher.Invoke(() => InputPrompt.Show(
                        "Connection Error",
                        $"Failed to connect. Please enter the server IP address:"
                    ));

                    // If the user cancels the prompt, stop trying
                    if (string.IsNullOrWhiteSpace(ipAddress)) {
                        Dispatcher.Invoke(() => _viewModel.ConnectionStatus = "Connection Canceled");
                        break;
                    }
                }
            }
        }

        private void OnKeyExchangeCompleted(object o, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.ConnectionStatus = "Ready";
            });
        }

        private void OnMessageReceived(object o, MessageReceivedEventArgs message)
        {
            Dispatcher.Invoke(() =>
            {
                MessageViewModelBase? messageViewModel = message.Packet.Type switch
                {
                    MessageType.Text => new TextMessageViewModel(message),
                    MessageType.Photo => new PhotoMessageViewModel(message),
                    MessageType.File => new FileMessageViewModel(message),
                    _ => null
                };

                if (messageViewModel != null)
                {
                    _viewModel.Messages.Add(messageViewModel);
                    ChatScrollViewer.ScrollToEnd();
                }

                switch (message.Packet.Type) {
                    case MessageType.Photo:
                    case MessageType.FileDownloadResponse:

                        // find viewmodel with same MessageId
                        MessageViewModelBase? viewmodel = _viewModel.Messages.FirstOrDefault(m => m.Message.Packet.MessageId == message.Packet.MessageId);
                        if (viewmodel is FileMessageViewModel fileMessage) {
                            viewmodel.Message.DecryptedFileData = message.DecryptedFileData;
                        }
                        else if (viewmodel is PhotoMessageViewModel photoMessage) {
                            photoMessage.Message.DecryptedFileData = message.DecryptedFileData;
                            photoMessage.LoadImage(); // Reload image after decryption
                        }

                    break;
                }
            });
        }

        private async void SendMessage_Click(object sender, RoutedEventArgs e)
        {
            await SendTextMessage();
        }

        private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                await SendTextMessage();
            }
        }

        private async Task SendTextMessage()
        {
            string message = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message) || !_viewModel.IsConnected) return;

            try
            {
                // Add to UI immediately
                // Client will send the message to the event.
                // var ownMessage = new TextMessageViewModel(message, _chatClient.ClientId, true);
                // _viewModel.Messages.Add(ownMessage);

                // Send to server
                await _chatClient.SendTextMessageAsync(message);
                MessageTextBox.Clear();
                ChatScrollViewer.ScrollToEnd();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send message: {ex.Message}", "Send Error");
            }
        }

        private async void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.IsConnected) return;

            var dialog = new OpenFileDialog
            {
                Title = "Select File to Send",
                Filter = "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Send file
                    await _chatClient.SendFileAsync(dialog.FileName, MessageType.File);
                    ChatScrollViewer.ScrollToEnd();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to send file: {ex.Message}", "Send Error");
                }
            }
        }

        private async void AttachPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.IsConnected) return;

            var dialog = new OpenFileDialog
            {
                Title = "Select Photo to Send",
                Filter = "Image files (*.jpg;*.jpeg;*.png;*.gif;*.bmp)|*.jpg;*.jpeg;*.png;*.gif;*.bmp"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Send photo
                    await _chatClient.SendFileAsync(dialog.FileName, MessageType.Photo);
                    ChatScrollViewer.ScrollToEnd();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to send photo: {ex.Message}", "Send Error");
                }
            }
        }

        public async void RequestFileDownload(string messageId)
        {
            try
            {
                await _chatClient.RequestFileDownloadAsync(messageId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to request download: {ex.Message}", "Download Error");
            }
        }

        // Window controls
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _chatClient?.DisconnectAsync().Wait();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _chatClient?.DisconnectAsync().Wait();
            base.OnClosed(e);
        }
    }
    public class MessageTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (container is FrameworkElement element)
            {
                return item switch
                {
                    TextMessageViewModel => element.FindResource("TextMessageTemplate") as DataTemplate,
                    PhotoMessageViewModel => element.FindResource("PhotoMessageTemplate") as DataTemplate,
                    FileMessageViewModel => element.FindResource("FileMessageTemplate") as DataTemplate,
                    _ => null
                };
            }
            return null;
        }
    }
}