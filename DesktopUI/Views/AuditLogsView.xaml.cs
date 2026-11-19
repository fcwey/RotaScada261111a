using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // İşlem kayıtları sekmesinin görünümü
    public partial class AuditLogsView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public AuditLogsView(AuditLogsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}