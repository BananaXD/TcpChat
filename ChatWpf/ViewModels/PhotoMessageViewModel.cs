using ChatWpf;
using Client;
using SharedModels;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ChatWpf
{
    public class PhotoMessageViewModel : MessageViewModelBase
    {
        private BitmapImage _imageSource;

        public string? FileName => _message.Packet.FileName;
        public long FileSize => _message.Packet.FileSize.Value;
        public string FileSizeFormatted => FormatFileSize(FileSize);

        public BitmapImage ImageSource
        {
            get => _imageSource;
            private set
            {
                _imageSource = value;
                OnPropertyChanged();
            }
        }

        public PhotoMessageViewModel(MessageReceivedEventArgs message)
            : base(message)
        {
            LoadImage();
        }

        public void LoadImage()
        {
            try
            {
                if (_message.DecryptedFileData != null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(_message.DecryptedFileData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    ImageSource = bitmap;
                }
                // Request download from server
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow) {
                        mainWindow.RequestFileDownload(Message.Packet.MessageId);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image: {ex.Message}");
            }
        }

        private void LoadImageFromFile(string filePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                ImageSource = bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image from file: {ex.Message}");
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
