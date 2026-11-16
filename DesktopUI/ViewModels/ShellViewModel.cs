using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopUI.Helpers;
using DesktopUI.Services;
using Entities.Dtos;
using Entities.Enums;
using System.Windows.Threading;

namespace DesktopUI.ViewModels
{
    // Sol menüdeki bir sayfa girişi
    public class MenuEntry
    {
        public string Key { get; }
        public string Title { get; }

        // Sayfanın anahtarını ve menüde görünen adını alır.
        public MenuEntry(string key, string title)
        {
            Key = key;
            Title = title;
        }
    }

    // Ana pencerenin sabit çerçevesi: üst durum şeridi, sol menü, alt alarm şeridi ve seçili sayfa
    public class ShellViewModel : ObservableObject
    {
        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;

        private ScadaLiveDto? _lastData;
        private DateTime _lastDataAt = DateTime.MinValue;

        private string _seqText = "—";
        private string _stepText = string.Empty;
        private string _batchText = string.Empty;
        private string _modeText = string.Empty;
        private string _linkText = "BAĞLANIYOR";
        private string _linkLevel = "Bad";
        private string _linkDetail = string.Empty;
        private string _clockText = string.Empty;
        private int _alarmCount;
        private string _alarmText = "Aktif alarm yok";
        private string _commandMessage = string.Empty;
        private MenuEntry? _selectedMenu;
        private object? _currentPage;

        // Kullanıcı çıkış istediğinde tetiklenir.
        public event Action? LogoutRequested;

        public string FurnaceName { get; }
        public string UserName { get; }
        public List<MenuEntry> MenuItems { get; }

        public IAsyncRelayCommand MuteCommand { get; }
        public IAsyncRelayCommand AlarmResetCommand { get; }
        public IRelayCommand LogoutCommand { get; }

        public string SeqText { get => _seqText; private set => SetProperty(ref _seqText, value); }
        public string StepText { get => _stepText; private set => SetProperty(ref _stepText, value); }
        public string BatchText { get => _batchText; private set => SetProperty(ref _batchText, value); }
        public string ModeText { get => _modeText; private set => SetProperty(ref _modeText, value); }
        public string LinkText { get => _linkText; private set => SetProperty(ref _linkText, value); }
        public string LinkLevel { get => _linkLevel; private set => SetProperty(ref _linkLevel, value); }      // Ok, Warn, Bad
        public string LinkDetail { get => _linkDetail; private set => SetProperty(ref _linkDetail, value); }
        public string ClockText { get => _clockText; private set => SetProperty(ref _clockText, value); }
        public string AlarmText { get => _alarmText; private set => SetProperty(ref _alarmText, value); }
        public string CommandMessage { get => _commandMessage; private set => SetProperty(ref _commandMessage, value); }
        public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

        public int AlarmCount
        {
            get => _alarmCount;
            private set
            {
                if (SetProperty(ref _alarmCount, value))
                    OnPropertyChanged(nameof(HasAlarm));
            }
        }

        public bool HasAlarm => AlarmCount > 0;

        public MenuEntry? SelectedMenu
        {
            get => _selectedMenu;
            set
            {
                if (SetProperty(ref _selectedMenu, value) && value != null)
                    CurrentPage = CreatePage(value);
            }
        }

        // Bağımlılıkları alır, menüyü kurar, canlı veri olaylarına abone olur ve saat zamanlayıcısını başlatır.
        public ShellViewModel(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _live = live;
            _settings = settings;

            FurnaceName = settings.FurnaceName;
            UserName = session.User?.Name ?? string.Empty;

            MenuItems = new List<MenuEntry>
            {
                new MenuEntry("overview", "Genel Bakış"),
                new MenuEntry("program", "Program"),
                new MenuEntry("recipes", "Reçeteler"),
                new MenuEntry("trend", "Trend"),
                new MenuEntry("alarms", "Alarmlar"),
                new MenuEntry("manual", "Manuel"),
                new MenuEntry("batches", "Şarjlar"),
                new MenuEntry("maintenance", "Bakım"),
                new MenuEntry("settings", "Ayarlar")
            };

            MuteCommand = new AsyncRelayCommand(() => SendCommandAsync(ScadaCommandCode.BuzzerMute, "Buzzer susturma"));
            AlarmResetCommand = new AsyncRelayCommand(() => SendCommandAsync(ScadaCommandCode.AlarmReset, "Alarm reset"));
            LogoutCommand = new RelayCommand(() => LogoutRequested?.Invoke());

            _live.DataReceived += OnDataReceived;
            _live.AlarmsReceived += OnAlarmsReceived;
            _live.ConnectionChanged += OnConnectionChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();

            SelectedMenu = MenuItems[0];
            Refresh();
            _ = LoadAlarmsAsync();
        }

        // Olay aboneliklerini bırakır ve zamanlayıcıyı durdurur.
        public void Shutdown()
        {
            _timer.Stop();
            _live.DataReceived -= OnDataReceived;
            _live.AlarmsReceived -= OnAlarmsReceived;
            _live.ConnectionChanged -= OnConnectionChanged;
        }

        // Menüden seçilen sayfanın içeriğini üretir; sayfalar sonraki aşamalarda eklenecek.
        private object CreatePage(MenuEntry entry)
        {
            return new PlaceholderPageViewModel(entry.Title);
        }

        // Sunucudan gelen canlı veri paketini saklar ve üst şeridi günceller.
        private void OnDataReceived(ScadaLiveDto data)
        {
            _lastData = data;
            _lastDataAt = DateTime.Now;
            Refresh();
        }

        // Sunucudan gelen aktif alarm listesini alt şeride yansıtır.
        private void OnAlarmsReceived(List<ActiveAlarmDto> alarms)
        {
            AlarmCount = alarms.Count;

            var latest = alarms.OrderByDescending(a => a.TriggeredAt).FirstOrDefault();
            AlarmText = latest == null
                ? "Aktif alarm yok"
                : $"{latest.TriggeredAt:HH:mm:ss}  {latest.TagName}  {latest.Description}";
        }

        // Sunucu bağlantısı kurulunca ya da kopunca durumu günceller; bağlantı geldiyse alarm listesini yeniden ister.
        private void OnConnectionChanged()
        {
            Refresh();

            if (_live.IsConnected)
                _ = LoadAlarmsAsync();
        }

        // Aktif alarm listesini sunucudan ister (hub listeyi yalnızca değişiklikte gönderir).
        private async Task LoadAlarmsAsync()
        {
            var result = await _api.GetAsync<List<ActiveAlarmDto>>("api/alarms/active");
            if (result.IsSuccess && result.Data != null)
                OnAlarmsReceived(result.Data);
        }

        // Alt şeritteki komut düğmelerinin komutunu PLC'ye gönderir ve sonucu şeritte gösterir.
        private async Task SendCommandAsync(ScadaCommandCode code, string name)
        {
            var result = await _api.SendCommandAsync((int)code);
            string outcome = result.IsSuccess ? "gönderildi" : $"başarısız: {result.Message}";
            CommandMessage = $"{DateTime.Now:HH:mm:ss}  {name} {outcome}";
        }

        // Saati, bağlantı durumunu ve üst şeritteki fırın bilgilerini son veriye göre yeniler.
        private void Refresh()
        {
            ClockText = DateTime.Now.ToString("dd.MM.yyyy  HH:mm:ss");

            bool hasFreshData = _lastData != null &&
                                (DateTime.Now - _lastDataAt).TotalSeconds <= _settings.StaleSeconds;

            if (!_live.IsConnected)
            {
                SetLink("SUNUCU YOK", "Bad", $"SCADA sunucusuna ulaşılamıyor ({_settings.ServerUrl}).");
            }
            else if (!hasFreshData)
            {
                SetLink("VERİ GELMİYOR", "Bad", "Sunucu bağlı ama canlı veri göndermiyor.");
            }
            else if (!_lastData!.IsConnected || _lastData.Status == null)
            {
                SetLink("PLC BAĞLI DEĞİL", "Bad", _lastData.Error ?? "Sunucu PLC'yi okuyamıyor.");
            }
            else if (_lastData.IsStale)
            {
                SetLink("PLC VERİSİ ESKİ", "Warn", "PLC yaşam sinyali değişmiyor; değerler güncel olmayabilir.");
            }
            else
            {
                SetLink("PLC BAĞLI", "Ok", string.Empty);
            }

            // Güvenilir veri yoksa eski değerler gösterilmez.
            var status = LinkLevel == "Bad" ? null : _lastData?.Status;
            if (status == null)
            {
                SeqText = "—";
                StepText = string.Empty;
                BatchText = string.Empty;
                ModeText = string.Empty;
                return;
            }

            SeqText = StatusTexts.DescribeSeqState(status.SeqState);
            ModeText = StatusTexts.DescribeMode(status);

            bool programActive = status.SeqState is 20 or 30 or 50 or 70;
            StepText = programActive
                ? $"Adım {status.ActiveStep}   Kalan {StatusTexts.FormatMinutes(status.PrgRemainMin)}"
                : string.Empty;

            BatchText = status.BatchId > 0
                ? $"Reçete no {status.ActiveRecipeID}   Şarj no {status.BatchId}"
                : string.Empty;
        }

        // Bağlantı göstergesinin metnini, seviyesini ve açıklamasını ayarlar.
        private void SetLink(string text, string level, string detail)
        {
            LinkText = text;
            LinkLevel = level;
            LinkDetail = detail;
        }
    }
}