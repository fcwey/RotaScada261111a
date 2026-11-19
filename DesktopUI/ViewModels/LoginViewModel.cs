using CommunityToolkit.Mvvm.ComponentModel;
using DesktopUI.Services;
using System.Windows.Threading;

namespace DesktopUI.ViewModels
{
    // Giriş ekranının verisi ve giriş işlemi; sunucuya ulaşılamazsa bağlantı gelene kadar kendiliğinden yeniden dener
    public class LoginViewModel : ObservableObject
    {
        private const int MinUserNameLength = 2;
        private const int RetrySeconds = 3;

        private readonly ApiClient _api;
        private readonly SessionService _session;
        private readonly DispatcherTimer _retryTimer;

        private string? _pendingToken;      // Sunucuya ulaşılamadığı için doğrulanamamış saklı oturum
        private bool _probing;
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

        // Bağımlılıkları ve ilk mesajı alır; açılışta sunucuya ulaşılamadıysa bağlantıyı beklemeye başlar.
        public LoginViewModel(ApiClient api, SessionService session, string serverUrl, string? initialMessage, bool serverUnreachable, string? pendingToken)
        {
            _api = api;
            _session = session;
            ServerUrl = serverUrl;
            _errorMessage = initialMessage ?? string.Empty;
            _pendingToken = pendingToken;

            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RetrySeconds) };
            _retryTimer.Tick += async (_, _) => await ProbeServerAsync();

            if (serverUnreachable)
                WaitForServer();
        }

        // Bağlantı bekleme zamanlayıcısını durdurur; pencere kapanırken çağrılır.
        public void Stop()
        {
            _retryTimer.Stop();
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

                    if (login.IsUnreachable)
                        WaitForServer();

                    return;
                }

                // Elle giriş yapıldı; bekleyen saklı oturum artık kullanılmaz.
                _pendingToken = null;
                _retryTimer.Stop();
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

        // Sunucu gelene kadar belirli aralıkla yeniden denemeyi başlatır ve durumu yazar.
        private void WaitForServer()
        {
            ErrorMessage = _pendingToken != null
                ? "Sunucuya ulaşılamıyor. Bağlantı gelince oturumunuz kendiliğinden açılacak."
                : "Sunucuya ulaşılamıyor. Bağlantı bekleniyor…";

            _retryTimer.Start();
        }

        // Sunucunun ayağa kalkıp kalkmadığına bakar; kalktıysa mesajı kaldırır, saklı oturum geçerliyse girişi kendiliğinden yapar.
        private async Task ProbeServerAsync()
        {
            if (IsBusy || _probing)
                return;

            _probing = true;

            try
            {
                string? token = _pendingToken;

                // Token yokken de bu istek işe yarar: sunucu ayaktaysa "yetkisiz" cevabı verir, yani ulaşılabilir demektir.
                _api.SetToken(token);
                var user = await _api.GetCurrentUserAsync();

                if (user.IsUnreachable)
                {
                    _api.SetToken(null);
                    return;
                }

                _retryTimer.Stop();

                if (token != null && user.IsSuccess && user.Data != null)
                {
                    _session.Begin(token, user.Data, remember: true);
                    LoggedIn?.Invoke();
                    return;
                }

                // Sunucu geldi ama saklı oturum yok ya da artık geçerli değil: kullanıcı elle giriş yapar.
                _api.SetToken(null);
                ErrorMessage = token != null ? "Saklı oturum geçersiz. Yeniden giriş yapın." : string.Empty;
                _pendingToken = null;
            }
            finally
            {
                _probing = false;
            }
        }
    }
}