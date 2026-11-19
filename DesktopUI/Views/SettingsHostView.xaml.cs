using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Ayarlar sayfasının sekmeli çerçevesi
    public partial class SettingsHostView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public SettingsHostView(SettingsHostViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}