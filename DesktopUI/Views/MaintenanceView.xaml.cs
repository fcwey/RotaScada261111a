using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Bakım sayfasının görünümü
    public partial class MaintenanceView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public MaintenanceView(MaintenanceViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}