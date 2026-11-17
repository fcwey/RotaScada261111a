using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    public partial class OverviewView : UserControl
    {
        // Görünüm modelini sayfaya bağlar.
        public OverviewView(OverviewViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}