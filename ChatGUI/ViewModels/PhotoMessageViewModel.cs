using Client;

namespace ChatMaui.ViewModels;

public class PhotoMessageViewModel : MessageViewModelBase {
    private ImageSource _imageSource;

    public string FileName => _message.Packet.FileName;
    public long FileSize => _message.Packet.FileSize ?? 0;
    public string FileSizeFormatted => FormatFileSize(FileSize);

    public ImageSource ImageSource {
        get => _imageSource;
        private set {
            _imageSource = value;
            OnPropertyChanged();
        }
    }

    public PhotoMessageViewModel(MessageReceivedEventArgs message) : base(message) {
        LoadImage();
    }

    public void LoadImage() {
        try {
            if (_message.DecryptedFileData != null) {
                var stream = new MemoryStream(_message.DecryptedFileData);
                ImageSource = ImageSource.FromStream(() => stream);
            }
            else {
                // Request download from server
                MainThread.BeginInvokeOnMainThread(async () => {
                    if (Application.Current?.MainPage is MainPage mainPage) {
                        await mainPage.RequestFileDownload(Message.Packet.MessageId);
                    }
                });
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error loading image: {ex.Message}");
        }
    }

    private static string FormatFileSize(long bytes) {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1) {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}