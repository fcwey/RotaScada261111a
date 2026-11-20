using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Ayarlar sayfasındaki Program sekmesi
    public partial class ApplicationView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public ApplicationView(ApplicationViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}