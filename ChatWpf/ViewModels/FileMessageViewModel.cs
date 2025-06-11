using ChatWpf;
using SharedModels;
using System;
using System.IO;
using System.Windows;

namespace ChatWpf
{
    public class FileMessageViewModel : MessageViewModelBase
    {
        public string FileName => _packet.FileName;
        public long FileSize => _packet.FileSize.Value;
        public string FileSizeFormatted => FormatFileSize(FileSize);
        public string MessageId => _packet.MessageId;

        public FileMessageViewModel(MessagePacket packet, bool isOwnMessage = false)
            : base(packet, isOwnMessage)
        {
        }

        public FileMessageViewModel(string filePath, string senderId, bool isOwnMessage = true)
            : base(new MessagePacket
            {
                SenderId = senderId,
                FileName = Path.GetFileName(filePath),
                FileSize = new FileInfo(filePath).Length,
                MessageId = Guid.NewGuid().ToString(),
            }, isOwnMessage)
        {
        }

        public async void DownloadFile()
        {
            try
            {
                if (_packet.DecryptedFileData != null)
                {
                    var saveDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        FileName = FileName,
                        Filter = "All files (*.*)|*.*"
                    };

                    if (saveDialog.ShowDialog() == true)
                    {
                        await File.WriteAllBytesAsync(saveDialog.FileName, _packet.DecryptedFileData);
                        MessageBox.Show($"File saved to: {saveDialog.FileName}", "Download Complete");
                    }
                }
                else
                {
                    // Request download from server
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (Application.Current.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.RequestFileDownload(MessageId);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading file: {ex.Message}", "Download Error");
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