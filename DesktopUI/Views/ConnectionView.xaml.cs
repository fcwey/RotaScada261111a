using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Ayarlar sayfasındaki Bağlantı sekmesi
    public partial class ConnectionView : UserControl
    {
        // Görünümü kurar, görünüm modeline bağlar; sekme görünürken canlı veriyi izletir.
        public ConnectionView(ConnectionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Loaded += (_, _) => viewModel.Start();
            Unloaded += (_, _) => viewModel.Stop();
        }
    }
}