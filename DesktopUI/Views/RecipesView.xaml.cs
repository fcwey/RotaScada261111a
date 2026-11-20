using DesktopUI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopUI.Views
{
    // Reçeteler sayfası: solda liste, sağda salt okunur reçete görünümü ya da düzenleme tablosu
    public partial class RecipesView : UserControl
    {
        // Görünüm modelini sayfaya bağlar.
        public RecipesView(RecipesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        // Adım satırına tıklanınca o satırı seçili yapar.
        private void OnStepRowMouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectRow(sender);
        }

        // Adım satırındaki bir kutuya klavyeyle girilince o satırı seçili yapar.
        private void OnStepRowGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SelectRow(sender);
        }

        // Olayı gönderen satırın adımını görünüm modelinde seçili adım olarak ayarlar.
        private void SelectRow(object sender)
        {
            if (sender is FrameworkElement element && element.DataContext is RecipeStepRow row && DataContext is RecipesViewModel viewModel)
                viewModel.SelectedStep = row;
        }
    }
}