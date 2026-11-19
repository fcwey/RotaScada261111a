using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Şarjlar sayfasının görünümü
    public partial class BatchesView : UserControl
    {
        // Görünümü kurar ve görünüm modeline bağlar.
        public BatchesView(BatchesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}