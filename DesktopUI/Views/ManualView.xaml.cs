using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Manuel sayfasının görünümü
    public partial class ManualView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public ManualView(ManualViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}