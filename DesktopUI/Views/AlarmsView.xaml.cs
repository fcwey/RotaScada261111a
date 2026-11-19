using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Alarmlar sayfasının görünümü
    public partial class AlarmsView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public AlarmsView(AlarmsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}