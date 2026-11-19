using DesktopUI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace DesktopUI.Views
{
    // Kullanıcılar sekmesinin görünümü
    public partial class UsersView : UserControl
    {
        private readonly UsersViewModel _viewModel;

        // Görünümü kurar ve görünüm modeline bağlar.
        public UsersView(UsersViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
        }

        // Seçili kullanıcının şifresini kutudaki şifreyle değiştirir; başarılıysa kutuyu temizler.
        private async void OnResetPasswordClick(object sender, RoutedEventArgs e)
        {
            if (await _viewModel.ResetPasswordAsync(ResetPasswordBox.Password))
                ResetPasswordBox.Clear();
        }

        // Formdaki bilgilerle yeni kullanıcı açar; başarılıysa şifre kutusunu temizler.
        private async void OnCreateUserClick(object sender, RoutedEventArgs e)
        {
            if (await _viewModel.CreateUserAsync(NewPasswordBox.Password))
                NewPasswordBox.Clear();
        }
    }
}