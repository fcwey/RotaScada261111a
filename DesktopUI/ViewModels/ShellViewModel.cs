using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Helpers;
using DesktopUI.Services;
using DesktopUI.Views;
using Entities.Dtos;
using Entities.Enums;
using Entities.Structures;
using System.Collections.ObjectModel;
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

    // Üst şeritteki ya da bağlantı panelindeki bir bilgi hücresi: küçük başlık ve değer
    public class StatusCell : ObservableObject
    {
        private string _value;

        public string Label { get; }
        public string Value { get => _value; set => SetProperty(ref _value, value); }

        // Hücrenin başlığını ve ilk değerini alır.
        public StatusCell(string label, string value)
        {
            Label = label;
            _value = value;
        }
    }

    // Ana pencerenin sabit çerçevesi: sol menü, renkli durum şeridi, ışık kulesi, bağlantı paneli, alt alarm şeridi ve seçili sayfa
    public class ShellViewModel : ObservableObject
    {
        private const string NoValue = "—";
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        // Durum simgeleri (24x24 alana çizilir)
        private const string Ring = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 ";
        private static readonly Geometry IconNone = Icon("M6,12 H18");
        private static readonly Geometry IconReady = Icon(Ring + "M8,12.5 L11,15.5 L16.5,9");
        private static readonly Geometry IconNotReady = Icon(Ring + "M5.8,5.8 L18.2,18.2");
        private static readonly Geometry IconRun = Icon(Ring + "M10,8 L16,12 L10,16 Z");
        private static readonly Geometry IconWait = Icon(Ring + "M10,8.5 V15.5 M14,8.5 V15.5");
        private static readonly Geometry IconPurge = Icon("M7,4 H17 M7,20 H17 M8,4 C8,10 16,14 16,20 M16,4 C16,10 8,14 8,20");
        private static readonly Geometry IconDone = Icon("M6,21 V4 M6,5 H17 L14.5,8.5 L17,12 H6");
        private static readonly Geometry IconStop = Icon("M12,3 L22,20 H2 Z M12,10 V14.5 M12,17 V17.3");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly PageFactory _pages;
        private readonly DispatcherTimer _timer;
        private readonly MenuEntry _alarmMenu;

        private ScadaLiveDto? _lastData;
        private DateTime _lastDataAt = DateTime.MinValue;
        private IDisposable? _pageOwner;
        private bool _blinkOn;

        // Üst şeritte adı gösterilen şarj; numara değişince adlar sunucudan yeniden istenir.
        private int _namedBatchId;
        private string? _recipeName;
        private string? _batchCode;

        private string _barLevel = "Off";
        private string _stateName = "VERİ YOK";
        private Geometry _stateIcon = IconNone;
        private string _actionText = string.Empty;
        private bool _lampRed;
        private bool _lampGreen;
        private bool _lampYellow;
        private bool _isSimulation;
        private string _modeText = NoValue;
        private string _linkText = "BAĞLANIYOR";
        private string _linkLevel = "Bad";
        private string _linkDetail = string.Empty;
        private bool _isConnectionOpen;
        private string _timeText = string.Empty;
        private string _dateText = string.Empty;
        private int _alarmCount;
        private string _alarmText = string.Empty;
        private string _commandMessage = string.Empty;
        private MenuEntry? _selectedMenu;
        private object? _currentPage;

        // Kullanıcı çıkış istediğinde tetiklenir.
        public event Action? LogoutRequested;

        // Yetkili kullanıcı Ayarlar sayfasından programı kapatmak istediğinde tetiklenir.
        public event Action? ExitRequested;

        public string FurnaceName { get; }
        public string UserName { get; }
        public List<MenuEntry> MenuItems { get; }
        public bool CanOpenConnectionSettings { get; }

        public ObservableCollection<StatusCell> Cells { get; } = new();
        public ObservableCollection<StatusCell> ConnectionRows { get; } = new();

        public IAsyncRelayCommand MuteCommand { get; }
        public IAsyncRelayCommand AlarmResetCommand { get; }
        public IRelayCommand LogoutCommand { get; }
        public IRelayCommand OpenConnectionSettingsCommand { get; }

        public string BarLevel { get => _barLevel; private set => SetProperty(ref _barLevel, value); }             // Off, Idle, Run, RunDim, Wait, Stop, StopDim
        public string StateName { get => _stateName; private set => SetProperty(ref _stateName, value); }
        public Geometry StateIcon { get => _stateIcon; private set => SetProperty(ref _stateIcon, value); }
        public string ActionText { get => _actionText; private set => SetProperty(ref _actionText, value); }
        public bool LampRed { get => _lampRed; private set => SetProperty(ref _lampRed, value); }
        public bool LampGreen { get => _lampGreen; private set => SetProperty(ref _lampGreen, value); }
        public bool LampYellow { get => _lampYellow; private set => SetProperty(ref _lampYellow, value); }
        public bool IsSimulation { get => _isSimulation; private set => SetProperty(ref _isSimulation, value); }
        public string ModeText { get => _modeText; private set => SetProperty(ref _modeText, value); }
        public string LinkText { get => _linkText; private set => SetProperty(ref _linkText, value); }
        public string LinkLevel { get => _linkLevel; private set => SetProperty(ref _linkLevel, value); }          // Ok, Warn, Bad
        public string LinkDetail { get => _linkDetail; private set => SetProperty(ref _linkDetail, value); }
        public bool IsConnectionOpen { get => _isConnectionOpen; set => SetProperty(ref _isConnectionOpen, value); }
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

        // Bağımlılıkları alır, menüyü kurar, canlı veri olaylarına abone olur ve yarım saniyelik zamanlayıcıyı başlatır.
        public ShellViewModel(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _live = live;
            _settings = settings;
            _pages = new PageFactory(api, session, live, settings);
            _pages.ExitAction = () => ExitRequested?.Invoke();

            FurnaceName = settings.FurnaceName;
            UserName = !string.IsNullOrWhiteSpace(session.User?.Name) ? session.User!.Name : session.User?.Email ?? string.Empty;
            CanOpenConnectionSettings = session.HasRole(ParameterPermissions.PlcIpConfigWrite);

            _alarmMenu = new MenuEntry("alarms", "Alarmlar", "M9,2.5 L16,15 H2 Z M9,7.5 V11 M9,13 V13.2");
            MenuItems = BuildMenu(session);

            MuteCommand = new AsyncRelayCommand(() => SendCommandAsync(ScadaCommandCode.BuzzerMute, "Buzzer susturma"));
            AlarmResetCommand = new AsyncRelayCommand(() => SendCommandAsync(ScadaCommandCode.AlarmReset, "Alarm reset"));
            LogoutCommand = new RelayCommand(() => LogoutRequested?.Invoke());
            OpenConnectionSettingsCommand = new RelayCommand(OpenConnectionSettings);

            _live.DataReceived += OnDataReceived;
            _live.AlarmsReceived += OnAlarmsReceived;
            _live.ConnectionChanged += OnConnectionChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => OnTick();
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

        // Çizim yolunu dondurulmuş simgeye çevirir.
        private static Geometry Icon(string path)
        {
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            return geometry;
        }

        // Menüyü kullanıcının rollerine göre kurar; Manuel ve Bakım yalnızca yetkisi olana görünür.
        private List<MenuEntry> BuildMenu(SessionService session)
        {
            var items = new List<MenuEntry>
            {
                new MenuEntry("overview", "Genel Bakış", "M2.5,8.5 L9,3 L15.5,8.5 V15 H11.5 V11 H6.5 V15 H2.5 Z"),
                new MenuEntry("program", "Program", "M3,4.5 H15 M3,9 H15 M3,13.5 H10 M12.5,12 L15,13.5 L12.5,15 Z"),
                new MenuEntry("recipes", "Reçeteler", "M4,2.5 H12 L14.5,5 V15.5 H4 Z M6.5,8 H11.5 M6.5,11 H11.5"),
                new MenuEntry("trend", "Trend", "M2.5,3 V15 H15.5 M5,11.5 L8,7.5 L10.5,10 L15,5"),
                _alarmMenu
            };

            if (session.HasRole(PlcPermissions.Manual, PlcPermissions.Override))
                items.Add(new MenuEntry("manual", "Manuel", "M3,5 H15 M3,9 H15 M3,13 H15 M6.5,3.5 V6.5 M11.5,7.5 V10.5 M7.5,11.5 V14.5"));

            items.Add(new MenuEntry("batches", "Şarjlar", "M9,2.5 L15.5,5.7 L9,9 L2.5,5.7 Z M2.5,9 L9,12.3 L15.5,9 M2.5,12.3 L9,15.5 L15.5,12.3"));

            if (session.HasRole(PlcPermissions.Operate, PlcPermissions.Maintenance))
                items.Add(new MenuEntry("maintenance", "Bakım", "M3,15 L10,8 M10,8 A3,3 0 1 1 13,4.5 L11.5,6 L12.5,7 L14.5,5.5 A3,3 0 0 1 10,8"));

            items.Add(new MenuEntry("settings", "Ayarlar", "M9,6.5 A2.5,2.5 0 1 0 9,11.5 A2.5,2.5 0 1 0 9,6.5 M9,2 V4.2 M9,13.8 V16 M2,9 H4.2 M13.8,9 H16 M4,4 L5.6,5.6 M12.4,12.4 L14,14 M14,4 L12.4,5.6 M5.6,12.4 L4,14"));

            return items;
        }

        // Önceki sayfayı kapatır ve menüden seçilen sayfayı gösterir.
        private void ShowPage(MenuEntry entry)
        {
            _pageOwner?.Dispose();

            var page = _pages.Create(entry.Key, entry.Title, Navigate);
            _pageOwner = page.Owner;
            CurrentPage = page.Content;
        }

        // Anahtarı verilen sayfaya geçer; sayfa zaten açıksa yeniden kurar.
        private void Navigate(string key)
        {
            var target = MenuItems.FirstOrDefault(m => m.Key == key);
            if (target == null)
                return;

            if (ReferenceEquals(SelectedMenu, target))
                ShowPage(target);
            else
                SelectedMenu = target;
        }

        // Bağlantı panelini kapatır ve Ayarlar sayfasını Bağlantı sekmesi seçili olarak açar.
        private void OpenConnectionSettings()
        {
            IsConnectionOpen = false;
            _pages.PendingSettingsTab = "connection";
            Navigate("settings");
        }

        // Yarım saniyede bir yanıp sönme evresini çevirir ve şeridi yeniler.
        private void OnTick()
        {
            _blinkOn = !_blinkOn;
            Refresh();
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

        // Saati, bağlantı göstergesini, durum şeridini ve ışık kulesini son veriye göre yeniler.
        private void Refresh()
        {
            DateTime now = DateTime.Now;
            TimeText = now.ToString("HH:mm:ss");
            DateText = now.ToString("dd.MM.yyyy dddd", Turkish);

            bool hasFreshData = _lastData != null &&
                                (now - _lastDataAt).TotalSeconds <= _settings.StaleSeconds;

            if (!_live.IsConnected)
                SetLink("SUNUCU YOK", "Bad", $"SCADA sunucusuna ulaşılamıyor ({_settings.ServerUrl}).");
            else if (!hasFreshData)
                SetLink("VERİ GELMİYOR", "Bad", "Sunucu bağlı ama canlı veri göndermiyor.");
            else if (!_lastData!.IsConnected || _lastData.Status == null)
                SetLink("PLC BAĞLI DEĞİL", "Bad", _lastData.Error ?? "Sunucu PLC'yi okuyamıyor.");
            else if (_lastData.IsStale)
                SetLink("PLC VERİSİ ESKİ", "Warn", "PLC yaşam sinyali değişmiyor; değerler güncel olmayabilir.");
            else
                SetLink("PLC BAĞLI", "Ok", string.Empty);

            UpdateConnectionRows();

            // Güvenilir veri yoksa eski durum gösterilmez.
            var status = LinkLevel == "Bad" ? null : _lastData?.Status;
            if (status == null)
            {
                ModeText = NoValue;
                IsSimulation = false;
                SetLamps(false, false, false);
                SetState("Off", "VERİ YOK", IconNone, LinkDetail);
                SetCells(Cells);
                return;
            }

            ModeText = StatusTexts.DescribeMode(status);

            // Şarj değiştiyse adları yeniden istenir; gelene kadar numaralar gösterilir.
            if (status.BatchId != _namedBatchId)
            {
                _namedBatchId = status.BatchId;
                _recipeName = null;
                _batchCode = null;

                if (status.BatchId > 0)
                    _ = LoadBatchNamesAsync(status.BatchId);
            }

            UpdateState(status);
            UpdateTower(status);
        }

        // Fırının sekiz durumundan hangisinde olduğunu belirler; şeridin rengini, adını, hücrelerini ve yapılacak işi yazar.
        private void UpdateState(ScadaStatus s)
        {
            bool alarmAny = s.AlarmActive.Any(word => word != 0);
            bool unackAny = s.AlarmUnack.Any(word => word != 0);
            bool hasBatch = s.BatchId > 0;

            string recipe = !hasBatch ? NoValue : _recipeName ?? $"no {s.ActiveRecipeID}";
            string batch = !hasBatch ? NoValue : _batchCode ?? $"no {s.BatchId}";
            string pot = s.IsTcValid(0) ? $"{s.TempPota:0} °C" : NoValue;
            string pressure = $"{s.Pressure:0.0} mbar";
            string remain = StatusTexts.FormatMinutes(s.PrgRemainMin);

            switch (s.SeqState)
            {
                case 0:
                    if (s.Mode == 2 && s.HasPermit(0))
                        SetState("Idle", "HAZIR", IconReady, "Program başlatılabilir");
                    else
                        SetState("Idle", "HAZIR DEĞİL", IconNotReady, DescribeNotReady(s, alarmAny));

                    SetCells(Cells, ("Pota", pot), ("Basınç", pressure));
                    break;

                case 20:
                case 30:
                case 50:
                    SetState("Run", "ÇALIŞIYOR", IconRun,
                        alarmAny ? $"{Math.Max(AlarmCount, 1)} uyarı alarmı var; Alarmlar sayfasına bakın" : "Müdahale gerekmiyor");
                    SetCells(Cells,
                        ("Aşama", $"{StatusTexts.DescribeSeqMessage(s.SeqMessage)} · adım {s.ActiveStep}"),
                        ("Kalan", remain),
                        ("Bitiş", FormatEnd(s.PrgEndEstUnix)),
                        ("Reçete", recipe));
                    break;

                case 70:
                    SetState("Wait", s.HoldReason == 2 ? "GSOAK BEKLİYOR" : "SİZİ BEKLİYOR", IconWait, DescribeHoldAction(s.HoldReason));
                    SetCells(Cells,
                        ("Sebep", DescribeHoldShort(s.HoldReason)),
                        ("Adım", s.ActiveStep.ToString()),
                        ("Kalan", remain),
                        ("Reçete", recipe));
                    break;

                case 40:
                    SetState("Wait", "YIKAMA", IconPurge, "Azotla yıkama sürüyor; bitince kendiliğinden geçer");
                    SetCells(Cells, ("Pota", pot), ("Basınç", pressure), ("Reçete", recipe), ("Şarj", batch));
                    break;

                case 45:
                    SetState("Wait", "KORUMALI BEKLEME", IconPurge, "Kapak açma sıcaklığına soğuması bekleniyor");
                    SetCells(Cells, ("Pota", pot), ("Basınç", pressure), ("Reçete", recipe), ("Şarj", batch));
                    break;

                case 60:
                    SetState(_blinkOn ? "Run" : "RunDim", "PROGRAM BİTTİ", IconDone, "Alarm Reset verin");
                    SetCells(Cells, ("Pota", pot), ("Reçete", recipe), ("Şarj", batch));
                    break;

                case 80:
                    SetState(unackAny && !_blinkOn ? "StopDim" : "Stop", "GÜVENLİ DURDURMA", IconStop,
                        unackAny ? "Alarmı onaylayın ve sebebini giderin" : "Sebep giderilince Alarm Reset verin");
                    SetCells(Cells, ("Pota", pot), ("Basınç", pressure), ("Reçete", recipe), ("Şarj", batch));
                    break;

                default:
                    SetState("Idle", StatusTexts.DescribeSeqState(s.SeqState), IconNone, string.Empty);
                    SetCells(Cells, ("Pota", pot), ("Basınç", pressure));
                    break;
            }
        }

        // Işık kulesinin üç lambasını kule belgesindeki kurallarla hesaplar; simülasyonda gerçek kule kapalı olduğundan lambalar söner.
        private void UpdateTower(ScadaStatus s)
        {
            IsSimulation = s.GetDeviceBit(1, 11);
            if (IsSimulation)
            {
                SetLamps(false, false, false);
                return;
            }

            bool alarmAny = s.AlarmActive.Any(word => word != 0);
            bool unackAny = s.AlarmUnack.Any(word => word != 0);
            bool safeStop = (s.AlarmActive[0] & 0xFF) != 0;
            bool paused = s.SeqState == 70;
            bool running = s.SeqState is 20 or 30 or 40 or 45 or 50 or 70;
            bool finished = s.SeqState == 60;

            bool red = safeStop && (_blinkOn || !unackAny);
            bool yellow = (alarmAny && !safeStop) || paused || s.SeqState is 40 or 45;
            bool green = (running && !alarmAny && !paused) || (finished && _blinkOn);

            SetLamps(red, green, yellow);
        }

        // Program başlatılamıyorsa en olası sebebi tek cümleyle söyler.
        private static string DescribeNotReady(ScadaStatus s, bool alarmAny)
        {
            if (s.Mode != 2) return "Panelden otomatik mod seçilmeli";
            if (s.LeakState != 0) return "Kaçak testi sürüyor";
            if (!s.GetDeviceBit(1, 0)) return "Kapak kapalı ve kilitli değil";
            if (alarmAny) return "Aktif alarm var; giderip Alarm Reset verin";
            return "Başlatma izni yok";
        }

        // Duraklatma sebebini şerit hücresine sığacak kısa metne çevirir.
        private static string DescribeHoldShort(short holdReason)
        {
            return holdReason switch
            {
                1 => "Operatör bekletti",
                2 => "Gsoak",
                3 => "Alarm",
                4 => "Otomatik mod kapatıldı",
                5 => "Enerji geri geldi",
                6 => "Vakum zaman aşımı",
                _ => NoValue
            };
        }

        // Duraklatma sebebine göre operatörün yapması gereken işi söyler.
        private static string DescribeHoldAction(short holdReason)
        {
            return holdReason switch
            {
                1 => "Sürdürmek için Devam verin",
                2 => "Sıcaklık banda girince kendiliğinden sürer",
                3 => "Alarmı giderin, Alarm Reset verip Devam verin",
                4 => "Panelden otomatik modu seçin, sonra Devam verin",
                5 => "Fırını kontrol edin, uygunsa Devam verin",
                6 => "Vakum Onay verin ya da Devam ile yeniden deneyin",
                _ => "Devam komutu bekleniyor"
            };
        }

        // PLC'nin yerel saatle verdiği tahmini bitiş anını yazar; bugün değilse tarihi de ekler.
        private static string FormatEnd(int unixLocal)
        {
            if (unixLocal <= 0)
                return NoValue;

            DateTime end = new DateTime(1970, 1, 1).AddSeconds(unixLocal);
            return end.Date == DateTime.Today ? end.ToString("HH:mm") : end.ToString("dd.MM HH:mm");
        }

        // Bağlantı panelindeki satırları son veriye göre yazar.
        private void UpdateConnectionRows()
        {
            var status = _lastData?.Status;
            bool plcOk = _lastData != null && _lastData.IsConnected && status != null;

            string server = _live.IsConnected ? "bağlı" : "ulaşılamıyor";
            string plc = !_live.IsConnected ? NoValue : plcOk ? "bağlı" : "bağlı değil";
            string lastData = _lastDataAt == DateTime.MinValue ? NoValue : _lastDataAt.ToString("HH:mm:ss");
            string heartbeat = !plcOk ? NoValue : _lastData!.IsStale ? "değişmiyor" : $"değişiyor ({status!.PlcHeartbeat})";
            string version = plcOk ? status!.LayoutVersion.ToString() : NoValue;

            SetCells(ConnectionRows,
                ("SCADA sunucusu", _settings.ServerUrl),
                ("Sunucu bağlantısı", server),
                ("PLC", plc),
                ("Son veri", lastData),
                ("PLC yaşam sinyali", heartbeat),
                ("Arayüz sürümü", version));
        }

        // Hücre listesini verilen başlık ve değerlerle eşitler; başlıklar aynıysa yalnızca değerleri değiştirir.
        private static void SetCells(ObservableCollection<StatusCell> target, params (string Label, string Value)[] items)
        {
            bool sameLabels = target.Count == items.Length;
            for (int i = 0; sameLabels && i < items.Length; i++)
                sameLabels = target[i].Label == items[i].Label;

            if (sameLabels)
            {
                for (int i = 0; i < items.Length; i++)
                    target[i].Value = items[i].Value;
                return;
            }

            target.Clear();
            foreach (var item in items)
                target.Add(new StatusCell(item.Label, item.Value));
        }

        // Şeridin renk seviyesini, durum adını, simgesini ve yapılacak iş cümlesini ayarlar.
        private void SetState(string barLevel, string name, Geometry icon, string action)
        {
            BarLevel = barLevel;
            StateName = name;
            StateIcon = icon;
            ActionText = action;
        }

        // Kule lambalarını ayarlar.
        private void SetLamps(bool red, bool green, bool yellow)
        {
            LampRed = red;
            LampGreen = green;
            LampYellow = yellow;
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