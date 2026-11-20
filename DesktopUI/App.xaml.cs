using DesktopUI.Helpers;
using DesktopUI.Services;
using DesktopUI.ViewModels;
using DesktopUI.Views;
using System.Windows;

namespace DesktopUI
{
    public partial class App : Application
    {
        private AppSettings _settings = new AppSettings();
        private ApiClient _api = null!;
        private SessionService _session = null!;

        private LiveDataService? _live;
        private ShellViewModel? _shellViewModel;
        private ShellWindow? _shellWindow;
        private bool _switchingWindow;

        // Program açılışı: pencere temasını kaydeder, ayarları okur, saklı token varsa doğrular; geçerliyse ana pencereyi, değilse giriş ekranını açar.
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Açılan her pencerenin (ana pencere, iletişim kutuları) başlık çubuğu koyu yapılır.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAnyWindowLoaded));

            _settings = AppSettings.Load();
            _api = new ApiClient(_settings.ServerUrl);
            _session = new SessionService();
            _api.Unauthorized += () => Dispatcher.BeginInvoke(OnUnauthorized);

            string? savedToken = _session.LoadSavedToken();

            if (savedToken == null)
            {
                ShowLogin(null);
                return;
            }

            _api.SetToken(savedToken);
            var user = await _api.GetCurrentUserAsync();

            if (user.IsSuccess && user.Data != null)
            {
                _session.Begin(savedToken, user.Data, remember: true);
                ShowShell();
                return;
            }

            _api.SetToken(null);

            // Sunucu henüz ayakta değilse saklı oturum atılmaz; giriş ekranı sunucuyu bekler ve gelince oturumu kendiliğinden açar.
            if (user.IsUnreachable)
                ShowLogin(user.Message, serverUnreachable: true, pendingToken: savedToken);
            else
                ShowLogin(user.Message);
        }

        // Yüklenen pencerenin başlık çubuğunu koyu temaya çevirir.
        private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
                WindowTheme.ApplyDarkTitleBar(window);
        }

        // Giriş ekranını açar; giriş başarılı olunca (elle ya da saklı oturumla) ana pencereye geçer.
        private void ShowLogin(string? message, bool serverUnreachable = false, string? pendingToken = null)
        {
            var viewModel = new LoginViewModel(_api, _session, _settings.ServerUrl, message, serverUnreachable, pendingToken);
            var window = new LoginWindow(viewModel);
            bool loggedIn = false;

            viewModel.LoggedIn += () =>
            {
                loggedIn = true;
                ShowShell();
                window.Close();
            };

            // Giriş yapmadan pencere kapatılırsa program kapanır.
            window.Closed += (_, _) =>
            {
                if (!loggedIn)
                    Shutdown();
            };

            window.Show();
        }

        // Canlı veri bağlantısını başlatır ve ana pencereyi açar.
        private void ShowShell()
        {
            _live = new LiveDataService(_settings.ServerUrl);
            _live.Start(_session.Token!);

            _shellViewModel = new ShellViewModel(_api, _session, _live, _settings);
            _shellViewModel.LogoutRequested += () => Logout(null);
            _shellViewModel.ExitRequested += ExitProgram;

            _shellWindow = new ShellWindow(_shellViewModel);
            _shellWindow.Closed += (_, _) =>
            {
                if (!_switchingWindow)
                    Shutdown();
            };

            // İletişim kutuları ana pencerenin üstünde açılsın diye ana pencere olarak işaretlenir.
            MainWindow = _shellWindow;
            _shellWindow.Show();
        }

        // Sunucu token'ı reddettiğinde ana pencere açıksa oturumu kapatır.
        private void OnUnauthorized()
        {
            if (_shellWindow != null)
                Logout("Oturum geçersiz. Yeniden giriş yapın.");
        }

        // Oturumu ve canlı bağlantıyı kapatır, ana pencereyi kapatıp giriş ekranına döner.
        private async void Logout(string? message)
        {
            if (_shellWindow == null)
                return;

            var window = _shellWindow;
            _shellWindow = null;

            _shellViewModel?.Shutdown();
            _shellViewModel = null;

            if (_live != null)
            {
                await _live.StopAsync();
                _live = null;
            }

            _session.End();
            _api.SetToken(null);

            ShowLogin(message);

            _switchingWindow = true;
            window.AllowClose = true;
            window.Close();
            _switchingWindow = false;
        }

        // Ayarlar sayfasından istenince ana pencereyi kapatır; pencere kapanınca program sonlanır.
        private void ExitProgram()
        {
            if (_shellWindow == null)
                return;

            _shellWindow.AllowClose = true;
            _shellWindow.Close();
        }

        // Windows oturumu kapanırken ana pencerenin kapanmasına izin verir; yoksa pencere bilgisayarın kapanmasını engeller.
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            if (_shellWindow != null)
                _shellWindow.AllowClose = true;

            base.OnSessionEnding(e);
        }

        // Program kapanırken canlı bağlantıyı kapatır.
        protected override async void OnExit(ExitEventArgs e)
        {
            if (_live != null)
                await _live.StopAsync();

            base.OnExit(e);
        }
    }
}