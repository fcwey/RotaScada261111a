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

        // Program açılışı: ayarları okur, saklı token varsa doğrular; geçerliyse ana pencereyi, değilse giriş ekranını açar.
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _settings = AppSettings.Load();
            _api = new ApiClient(_settings.ServerUrl);
            _session = new SessionService();
            _api.Unauthorized += () => Dispatcher.BeginInvoke(OnUnauthorized);

            string? message = null;
            string? savedToken = _session.LoadSavedToken();

            if (savedToken != null)
            {
                _api.SetToken(savedToken);
                var user = await _api.GetCurrentUserAsync();

                if (user.IsSuccess && user.Data != null)
                {
                    _session.Begin(savedToken, user.Data, remember: true);
                    ShowShell();
                    return;
                }

                _api.SetToken(null);
                message = user.Message;
            }

            ShowLogin(message);
        }

        // Giriş ekranını açar; giriş başarılı olunca ana pencereye geçer.
        private void ShowLogin(string? message)
        {
            var viewModel = new LoginViewModel(_api, _session, _settings.ServerUrl, message);
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

            _shellWindow = new ShellWindow(_shellViewModel);
            _shellWindow.Closed += (_, _) =>
            {
                if (!_switchingWindow)
                    Shutdown();
            };
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
            window.Close();
            _switchingWindow = false;
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