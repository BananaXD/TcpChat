using ChatMaui.ViewModels;

namespace ChatMaui.Templates;

public partial class MessageTemplates : ResourceDictionary {
    public MessageTemplates() {
        InitializeComponent();
    }

    private async void OnDownloadFile(object sender, EventArgs e) {
        if (sender is Button button && button.BindingContext is FileMessageViewModel viewModel) {
            await viewModel.DownloadFile();
        }
    }
}