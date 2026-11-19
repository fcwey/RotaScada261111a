using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Ayarlar sayfasının görünümü
    public partial class SettingsView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}