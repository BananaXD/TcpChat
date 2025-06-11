using ChatWpf;
using SharedModels;
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace ChatWpf
{
    public class PhotoMessageViewModel : MessageViewModelBase
    {
        private BitmapImage _imageSource;

        public string FileName => _packet.FileName;
        public long FileSize => _packet.FileSize.Value;
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

        public PhotoMessageViewModel(MessagePacket packet, bool isOwnMessage = false)
            : base(packet, isOwnMessage)
        {
            LoadImage();
        }

        public PhotoMessageViewModel(string filePath, string senderId, bool isOwnMessage = true)
            : base(new MessagePacket
            {
                SenderId = senderId,
                FileName = Path.GetFileName(filePath),
                FileSize = new FileInfo(filePath).Length
            }, isOwnMessage)
        {
            LoadImageFromFile(filePath);
        }

        private void LoadImage()
        {
            try
            {
                if (_packet.DecryptedFileData != null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(_packet.DecryptedFileData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    ImageSource = bitmap;
                }
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