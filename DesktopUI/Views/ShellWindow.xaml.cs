using DesktopUI.ViewModels;
using System.Windows;

namespace DesktopUI.Views
{
    public partial class ShellWindow : Window
    {
        // Görünüm modelini pencereye bağlar.
        public ShellWindow(ShellViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}