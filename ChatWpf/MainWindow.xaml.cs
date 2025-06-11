using ChatWpf;
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

            // Subscribe to events (you'll need to add these to your ChatClient)
            _chatClient.Connected += OnConnected;
            _chatClient.Disconnected += OnDisconnected;
            _chatClient.MessageReceived += OnMessageReceived;
            _chatClient.KeyExchangeCompleted += OnKeyExchangeCompleted;

            Loaded += async (s, e) => await StartChatClient();
        }

        private async Task StartChatClient()
        {
            try
            {
                _viewModel.ConnectionStatus = "Connecting...";
                await _chatClient.StartAsync();
            }
            catch (Exception ex)
            {
                _viewModel.ConnectionStatus = "Connection Failed";
                MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnConnected()
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.IsConnected = true;
                _viewModel.ConnectionStatus = "Connected - Waiting for encryption...";
            });
        }

        private void OnDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.IsConnected = false;
                _viewModel.ConnectionStatus = "Disconnected";
            });
        }

        private void OnKeyExchangeCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.ConnectionStatus = "Ready";
            });
        }

        private void OnMessageReceived(MessagePacket packet)
        {
            Dispatcher.Invoke(() =>
            {
                MessageViewModelBase messageViewModel = packet.Type switch
                {
                    MessageType.Text => new TextMessageViewModel(packet),
                    MessageType.Photo => new PhotoMessageViewModel(packet),
                    MessageType.File => new FileMessageViewModel(packet),
                    _ => null
                };

                if (messageViewModel != null)
                {
                    _viewModel.Messages.Add(messageViewModel);
                    ChatScrollViewer.ScrollToEnd();
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
                var ownMessage = new TextMessageViewModel(message, _chatClient.ClientId, true);
                _viewModel.Messages.Add(ownMessage);

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
                    // Add to UI
                    var fileMessage = new FileMessageViewModel(dialog.FileName, _chatClient.ClientId, true);
                    _viewModel.Messages.Add(fileMessage);

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
                    // Add to UI
                    var photoMessage = new PhotoMessageViewModel(dialog.FileName, _chatClient.ClientId, true);
                    _viewModel.Messages.Add(photoMessage);

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
            _chatClient?.Disconnect();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _chatClient?.Disconnect();
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