using System.Windows.Controls;

namespace ChatWpf.Controls
{
    public partial class FileMessageControl : UserControl
    {
        public FileMessageControl()
        {
            InitializeComponent();
        }

        private void Download_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is FileMessageViewModel viewModel)
            {
                viewModel.DownloadFile();
            }
        }
    }
}