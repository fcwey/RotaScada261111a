using DesktopUI.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace DesktopUI.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;

        // Görünüm modelini bağlar ve imleci e-posta kutusuna koyar.
        public LoginWindow(LoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            Loaded += (_, _) => EmailBox.Focus();
        }

        // Giriş düğmesine basılınca şifreyi kutudan alıp giriş yapar.
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.LoginAsync(PasswordBox.Password);
        }

        // Şifre kutusunda Enter'a basılınca giriş yapar.
        private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await _viewModel.LoginAsync(PasswordBox.Password);
        }
    }
}