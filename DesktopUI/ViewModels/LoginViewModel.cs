using CommunityToolkit.Mvvm.ComponentModel;
using DesktopUI.Services;

namespace DesktopUI.ViewModels
{
    // Giriş ekranının verisi ve giriş işlemi
    public class LoginViewModel : ObservableObject
    {
        private const int MinUserNameLength = 2;

        private readonly ApiClient _api;
        private readonly SessionService _session;

        private string _userName = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isBusy;
        private bool _rememberMe = true;

        // Giriş başarılı olduğunda tetiklenir.
        public event Action? LoggedIn;

        public string ServerUrl { get; }

        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    OnPropertyChanged(nameof(CanLogin));
            }
        }

        public bool CanLogin => !IsBusy;

        public bool RememberMe
        {
            get => _rememberMe;
            set => SetProperty(ref _rememberMe, value);
        }

        // Bağımlılıkları ve ekranda gösterilecek ilk mesajı alır.
        public LoginViewModel(ApiClient api, SessionService session, string serverUrl, string? initialMessage)
        {
            _api = api;
            _session = session;
            ServerUrl = serverUrl;
            _errorMessage = initialMessage ?? string.Empty;
        }

        // Kullanıcı adı ve şifreyle (şifre boş olabilir) giriş yapar, kullanıcı bilgisini alır ve oturumu başlatır.
        public async Task LoginAsync(string password)
        {
            if (IsBusy)
                return;

            string userName = (UserName ?? string.Empty).Trim();

            if (userName.Length < MinUserNameLength)
            {
                ErrorMessage = "Kullanıcı adını girin.";
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                var login = await _api.LoginAsync(userName, password ?? string.Empty);
                if (!login.IsSuccess || login.Data == null || string.IsNullOrEmpty(login.Data.Token))
                {
                    ErrorMessage = login.Message ?? "Giriş yapılamadı.";
                    return;
                }

                _api.SetToken(login.Data.Token);

                var user = await _api.GetCurrentUserAsync();
                if (!user.IsSuccess || user.Data == null)
                {
                    _api.SetToken(null);
                    ErrorMessage = user.Message ?? "Kullanıcı bilgisi alınamadı.";
                    return;
                }

                _session.Begin(login.Data.Token, user.Data, RememberMe);
                LoggedIn?.Invoke();
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}