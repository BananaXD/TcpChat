using Client;

namespace ChatMaui.ViewModels;

public class FileMessageViewModel : MessageViewModelBase {
    public string FileName => _message.Packet.FileName;
    public long FileSize => _message.Packet.FileSize ?? 0;
    public string MessageId => _message.Packet.MessageId;
    public string FileSizeFormatted => FormatFileSize(FileSize);

    public FileMessageViewModel(MessageReceivedEventArgs message) : base(message) {
    }

    public async Task DownloadFile() {
        try {
            if (_message.DecryptedFileData != null) {
                var downloadsPath = Path.Combine(FileSystem.AppDataDirectory, "Downloads");
                Directory.CreateDirectory(downloadsPath);

                var filePath = Path.Combine(downloadsPath, FileName);
                await File.WriteAllBytesAsync(filePath, _message.DecryptedFileData);

                await Application.Current.MainPage.DisplayAlert(
                    "Download Complete",
                    $"File saved to: {filePath}",
                    "OK");
            }
            else {
                if (Application.Current?.MainPage is MainPage mainPage) {
                    await mainPage.RequestFileDownload(MessageId);
                }
            }
        } catch (Exception ex) {
            await Application.Current.MainPage.DisplayAlert(
                "Download Error",
                $"Error downloading file: {ex.Message}",
                "OK");
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