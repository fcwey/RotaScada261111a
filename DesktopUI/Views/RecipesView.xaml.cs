using DesktopUI.ViewModels;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    public partial class RecipesView : UserControl
    {
        // Görünüm modelini sayfaya bağlar.
        public RecipesView(RecipesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}