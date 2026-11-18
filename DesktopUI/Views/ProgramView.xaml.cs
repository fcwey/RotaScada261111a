using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    public partial class ProgramView : UserControl
    {
        // Görünüm modelini sayfaya bağlar.
        public ProgramView(ProgramViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}