using System.Windows;
using System.Windows.Controls;

namespace ChatWpf {
    public static class InputPrompt {
        public static string? Show(string title, string message) {
            var window = new Window {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            var stackPanel = new StackPanel { Margin = new Thickness(15) };
            stackPanel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 10) });

            var textBox = new TextBox();
            stackPanel.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            var okButton = new Button { Content = "Connect", IsDefault = true, Width = 75, Margin = new Thickness(5) };
            var cancelButton = new Button { Content = "Cancel", IsCancel = true, Width = 75, Margin = new Thickness(5) };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(buttonPanel);
            window.Content = stackPanel;

            okButton.Click += (s, e) => window.DialogResult = true;
            cancelButton.Click += (s, e) => window.DialogResult = false;

            textBox.Focus();

            bool? result = window.ShowDialog();

            return result == true ? textBox.Text : null;
        }
    }
}