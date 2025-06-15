using CommunityToolkit.Maui.Views;

namespace ChatMaui.Popups;

public partial class InputPromptPopup : Popup {
    public InputPromptPopup() {
        InitializeComponent();
    }

    private void OnConnectClicked(object sender, EventArgs e) {
        // Close the popup and return the text from the Entry
        Close(InputEntry.Text?.Trim());
    }

    private void OnCancelClicked(object sender, EventArgs e) {
        // Close the popup and return null
        Close(null);
    }
}