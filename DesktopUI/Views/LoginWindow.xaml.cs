using DesktopUI.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace DesktopUI.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;

        // Görünüm modelini bağlar, imleci kullanıcı adı kutusuna koyar ve pencere kapanınca sunucu beklemeyi durdurur.
        public LoginWindow(LoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Loaded += (_, _) => UserNameBox.Focus();
            Closed += (_, _) => _viewModel.Stop();
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

        // Başlık çubuğu olmadığı için kartın boş bir yerinden tutulup sürüklenmesini sağlar.
        private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        // Pencereyi kapatır; giriş yapılmadıysa program da kapanır.
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}