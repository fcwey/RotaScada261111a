using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopUI.Helpers;
using DesktopUI.Services;
using DesktopUI.Views;
using Entities.Dtos;
using Entities.Enums;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopUI.ViewModels
{
    // Sol menüdeki bir sayfa girişi: simge, ad ve isteğe bağlı sayı rozeti
    public class MenuEntry : ObservableObject
    {
        private int _badge;

        public string Key { get; }
        public string Title { get; }
        public Geometry Icon { get; }

        public int Badge
        {
            get => _badge;
            set
            {
                if (SetProperty(ref _badge, value))
                    OnPropertyChanged(nameof(HasBadge));
            }
        }

        public bool HasBadge => Badge > 0;

        // Sayfanın anahtarını, menüde görünen adını ve simgesinin çizim yolunu alır.
        public MenuEntry(string key, string title, string iconPath)
        {
            Key = key;
            Title = title;
            Icon = Geometry.Parse(iconPath);
            Icon.Freeze();
        }
    }

    // Ana pencerenin sabit çerçevesi: sol menü, üst durum şeridi, alt alarm şeridi ve seçili sayfa
    public class ShellViewModel : ObservableObject
    {
        private const string NoValue = "—";
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly PageFactory _pages;
        private readonly DispatcherTimer _timer;
        private readonly MenuEntry _alarmMenu;

        private ScadaLiveDto? _lastData;
        private DateTime _lastDataAt = DateTime.MinValue;
        private IDisposable? _pageOwner;

        // Üst şeritte adı gösterilen şarj; numara değişince adlar sunucudan yeniden istenir.
        private int _namedBatchId;
        private string? _recipeName;
        private string? _batchCode;

        private string _seqText = NoValue;
        private string _seqLevel = "Idle";
        private string _stepText = NoValue;
        private string _remainText = NoValue;
        private string _recipeText = NoValue;
        private string _batchText = NoValue;
        private string _modeText = NoValue;
        private string _linkText = "BAĞLANIYOR";
        private string _linkLevel = "Bad";
        private string _linkDetail = string.Empty;
        private string _timeText = string.Empty;
        private string _dateText = string.Empty;
        private int _alarmCount;
        private string _alarmText = string.Empty;
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
        public string SeqLevel { get => _seqLevel; private set => SetProperty(ref _seqLevel, value); }          // Run, Hold, Stop, Idle
        public string StepText { get => _stepText; private set => SetProperty(ref _stepText, value); }
        public string RemainText { get => _remainText; private set => SetProperty(ref _remainText, value); }
        public string RecipeText { get => _recipeText; private set => SetProperty(ref _recipeText, value); }
        public string BatchText { get => _batchText; private set => SetProperty(ref _batchText, value); }
        public string ModeText { get => _modeText; private set => SetProperty(ref _modeText, value); }
        public string LinkText { get => _linkText; private set => SetProperty(ref _linkText, value); }
        public string LinkLevel { get => _linkLevel; private set => SetProperty(ref _linkLevel, value); }       // Ok, Warn, Bad
        public string LinkDetail { get => _linkDetail; private set => SetProperty(ref _linkDetail, value); }
        public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }
        public string DateText { get => _dateText; private set => SetProperty(ref _dateText, value); }
        public string AlarmText { get => _alarmText; private set => SetProperty(ref _alarmText, value); }
        public string CommandMessage { get => _commandMessage; private set => SetProperty(ref _commandMessage, value); }
        public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

        public int AlarmCount
        {
            get => _alarmCount;
            private set
            {
                if (SetProperty(ref _alarmCount, value))
                {
                    OnPropertyChanged(nameof(HasAlarm));
                    OnPropertyChanged(nameof(AlarmCountText));
                }
            }
        }

        public bool HasAlarm => AlarmCount > 0;
        public string AlarmCountText => AlarmCount > 0 ? $"{AlarmCount} aktif alarm" : "Aktif alarm yok";

        public MenuEntry? SelectedMenu
        {
            get => _selectedMenu;
            set
            {
                if (SetProperty(ref _selectedMenu, value) && value != null)
                    ShowPage(value);
            }
        }

        // Bağımlılıkları alır, menüyü kurar, canlı veri olaylarına abone olur ve saat zamanlayıcısını başlatır.
        public ShellViewModel(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _live = live;
            _settings = settings;
            _pages = new PageFactory(api, session, live, settings);

            FurnaceName = settings.FurnaceName;
            UserName = session.User?.Name ?? string.Empty;

            _alarmMenu = new MenuEntry("alarms", "Alarmlar", "M9,2.5 L16,15 H2 Z M9,7.5 V11 M9,13 V13.2");

            MenuItems = new List<MenuEntry>
            {
                new MenuEntry("overview", "Genel Bakış", "M2.5,8.5 L9,3 L15.5,8.5 V15 H11.5 V11 H6.5 V15 H2.5 Z"),
                new MenuEntry("program", "Program", "M3,4.5 H15 M3,9 H15 M3,13.5 H10 M12.5,12 L15,13.5 L12.5,15 Z"),
                new MenuEntry("recipes", "Reçeteler", "M4,2.5 H12 L14.5,5 V15.5 H4 Z M6.5,8 H11.5 M6.5,11 H11.5"),
                new MenuEntry("trend", "Trend", "M2.5,3 V15 H15.5 M5,11.5 L8,7.5 L10.5,10 L15,5"),
                _alarmMenu,
                new MenuEntry("manual", "Manuel", "M3,5 H15 M3,9 H15 M3,13 H15 M6.5,3.5 V6.5 M11.5,7.5 V10.5 M7.5,11.5 V14.5"),
                new MenuEntry("batches", "Şarjlar", "M9,2.5 L15.5,5.7 L9,9 L2.5,5.7 Z M2.5,9 L9,12.3 L15.5,9 M2.5,12.3 L9,15.5 L15.5,12.3"),
                new MenuEntry("maintenance", "Bakım", "M3,15 L10,8 M10,8 A3,3 0 1 1 13,4.5 L11.5,6 L12.5,7 L14.5,5.5 A3,3 0 0 1 10,8"),
                new MenuEntry("settings", "Ayarlar", "M9,6.5 A2.5,2.5 0 1 0 9,11.5 A2.5,2.5 0 1 0 9,6.5 M9,2 V4.2 M9,13.8 V16 M2,9 H4.2 M13.8,9 H16 M4,4 L5.6,5.6 M12.4,12.4 L14,14 M14,4 L12.4,5.6 M5.6,12.4 L4,14")
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

        // Olay aboneliklerini bırakır, açık sayfayı kapatır ve zamanlayıcıyı durdurur.
        public void Shutdown()
        {
            _timer.Stop();
            _live.DataReceived -= OnDataReceived;
            _live.AlarmsReceived -= OnAlarmsReceived;
            _live.ConnectionChanged -= OnConnectionChanged;

            _pageOwner?.Dispose();
            _pageOwner = null;
        }

        // Önceki sayfayı kapatır ve menüden seçilen sayfayı gösterir.
        private void ShowPage(MenuEntry entry)
        {
            _pageOwner?.Dispose();

            var page = _pages.Create(entry.Key, entry.Title, Navigate);
            _pageOwner = page.Owner;
            CurrentPage = page.Content;
        }

        // Anahtarı verilen sayfaya geçer.
        private void Navigate(string key)
        {
            var target = MenuItems.FirstOrDefault(m => m.Key == key);
            if (target != null)
                SelectedMenu = target;
        }

        // Sunucudan gelen canlı veri paketini saklar ve üst şeridi günceller.
        private void OnDataReceived(ScadaLiveDto data)
        {
            _lastData = data;
            _lastDataAt = DateTime.Now;
            Refresh();
        }

        // Sunucudan gelen aktif alarm listesini alt şeride ve menüdeki rozete yansıtır.
        private void OnAlarmsReceived(List<ActiveAlarmDto> alarms)
        {
            AlarmCount = alarms.Count;
            _alarmMenu.Badge = alarms.Count;

            var latest = alarms.OrderByDescending(a => a.TriggeredAt).FirstOrDefault();
            AlarmText = latest == null
                ? string.Empty
                : $"{latest.TriggeredAt:HH:mm:ss}   {latest.TagName}   {latest.Description}";
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

        // Çalışan şarjın kodunu ve reçete adını sunucudan alır; üst şerit numara yerine bunları gösterir.
        private async Task LoadBatchNamesAsync(int batchId)
        {
            var result = await _api.GetAsync<BatchDetailDto>($"api/batches/{batchId}");
            if (batchId != _namedBatchId)
                return;

            if (result.IsSuccess && result.Data != null)
            {
                _recipeName = result.Data.Batch.RecipeName;
                _batchCode = result.Data.Batch.BatchCode;
                Refresh();
            }
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
            DateTime now = DateTime.Now;
            TimeText = now.ToString("HH:mm:ss");
            DateText = now.ToString("dd.MM.yyyy dddd", Turkish);

            bool hasFreshData = _lastData != null &&
                                (now - _lastDataAt).TotalSeconds <= _settings.StaleSeconds;

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
                SeqText = NoValue;
                SeqLevel = "Idle";
                StepText = NoValue;
                RemainText = NoValue;
                RecipeText = NoValue;
                BatchText = NoValue;
                ModeText = NoValue;
                return;
            }

            SeqText = StatusTexts.DescribeSeqState(status.SeqState);
            SeqLevel = DescribeSeqLevel(status.SeqState);
            ModeText = StatusTexts.DescribeMode(status);

            bool programActive = status.SeqState is 20 or 30 or 50 or 70;
            StepText = programActive ? status.ActiveStep.ToString() : NoValue;
            RemainText = programActive ? StatusTexts.FormatMinutes(status.PrgRemainMin) : NoValue;

            // Şarj değiştiyse adları yeniden istenir; gelene kadar numaralar gösterilir.
            if (status.BatchId != _namedBatchId)
            {
                _namedBatchId = status.BatchId;
                _recipeName = null;
                _batchCode = null;

                if (status.BatchId > 0)
                    _ = LoadBatchNamesAsync(status.BatchId);
            }

            bool hasBatch = status.BatchId > 0;
            RecipeText = !hasBatch ? NoValue : _recipeName ?? $"no {status.ActiveRecipeID}";
            BatchText = !hasBatch ? NoValue : _batchCode ?? $"no {status.BatchId}";
        }

        // Sekans durumunu üst şeritteki durum noktasının rengini belirleyen seviyeye çevirir.
        private static string DescribeSeqLevel(short seqState)
        {
            return seqState switch
            {
                20 or 30 or 50 => "Run",
                40 or 45 or 70 => "Hold",
                80 => "Stop",
                _ => "Idle"
            };
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