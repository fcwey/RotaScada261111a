using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Helpers;
using DesktopUI.Services;
using Entities.Dtos;
using Entities.Enums;
using Entities.Structures;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace DesktopUI.ViewModels
{
    // Manuel sayfasındaki bir cihaz kartı: komut 20'nin cihaz numarası, gerçek durumu ve gönderilecek değer
    public class ManualDeviceRow : ObservableObject
    {
        private string _valueText = string.Empty;
        private string _stateText = string.Empty;
        private string _detail = string.Empty;
        private string _note = string.Empty;
        private string _level = "Off";
        private bool _isEnabled;

        public int No { get; set; }                         // Komut 20, Param1
        public string Name { get; set; } = string.Empty;
        public bool RestrictedBlocked { get; set; }         // Kısıtlı manuelde PLC otomatik yönetir
        public bool NeedsValveMode { get; set; }            // Yalnızca basınç modu "elle vana" iken kabul edilir
        public bool HasValue { get; set; }                  // Param3 ile değer gönderilir
        public string ValueLabel { get; set; } = string.Empty;
        public int ValueMax { get; set; }

        public string ValueText { get => _valueText; set => SetProperty(ref _valueText, value); }
        public string StateText { get => _stateText; set => SetProperty(ref _stateText, value); }
        public string Detail { get => _detail; set => SetProperty(ref _detail, value); }
        public string Note { get => _note; set => SetProperty(ref _note, value); }
        public string Level { get => _level; set => SetProperty(ref _level, value); }       // On, Off, Fault
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    }

    // Manuel sayfası: manuel modda cihazları elle çalıştırma, basınç modu seçimi ve yetkili aşma komutları
    public class ManualViewModel : ObservableObject, IDisposable
    {
        private const int PressureModeDevice = 14;
        private const int MaxPressureSet = 500;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, string> _values = new();
        private readonly bool _canManual;
        private readonly bool _canOverride;

        private DateTime _lastDataAt = DateTime.MinValue;
        private bool _plcOk;
        private bool _isLive;
        private bool _isManual;
        private bool _restricted;
        private short _seqState;
        private bool _lidOverrideActive;
        private bool _ammoniaInside;
        private string _pressureSetText = string.Empty;

        public ObservableCollection<ManualDeviceRow> Devices { get; } = new();

        public IAsyncRelayCommand<ManualDeviceRow> TurnOnCommand { get; }
        public IAsyncRelayCommand<ManualDeviceRow> TurnOffCommand { get; }
        public IAsyncRelayCommand<string> SetPressureModeCommand { get; }
        public IAsyncRelayCommand PurgeOverrideCommand { get; }
        public IAsyncRelayCommand HoldExitCommand { get; }
        public IAsyncRelayCommand LidOverrideCommand { get; }
        public IAsyncRelayCommand LidOverrideCancelCommand { get; }

        // Ekranın adla okuduğu metin ve durum değerleri; olmayan ad boş döner.
        public string this[string key] => _values.TryGetValue(key, out var value) ? value : string.Empty;

        public bool IsLive { get => _isLive; private set => SetProperty(ref _isLive, value); }

        // Otomatik basınç kontrolü için girilen set (mbar); boş bırakılırsa PLC eski seti korur.
        public string PressureSetText { get => _pressureSetText; set => SetProperty(ref _pressureSetText, value); }

        // Cihaz kartlarını ve komutları kurar, canlı veriye abone olur.
        public ManualViewModel(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _live = live;
            _settings = settings;
            _canManual = session.HasRole(PlcPermissions.Manual);
            _canOverride = session.HasRole(PlcPermissions.Override);

            // Cihaz numaraları kılavuzdaki komut 20 tablosuyla aynıdır.
            Add(1, "Vakum", true);
            Add(2, "Azot dolum (harici hat)", true);
            Add(3, "Sirkülasyon", true, "Hız (%)", 100);
            Add(4, "Isıtma", false, "Sıcaklık seti (°C)", 800);
            Add(5, "Fan soğutma", false);
            Add(6, "Azot MFC", true, "Debi (L/h)", 32767);
            Add(7, "Amonyak MFC", true, "Debi (L/h)", 32767);
            Add(8, "CO₂ vanası", true);
            Add(9, "Su dozajı", true);
            Add(10, "Basınç kontrol vanası", true, needsValveMode: true);
            Add(11, "Tahliye vanası", true, needsValveMode: true);
            Add(12, "Acil azot vanası", true);
            Add(13, "Eşanjörü boşalt", false);

            TurnOnCommand = new AsyncRelayCommand<ManualDeviceRow>(row => SendDeviceAsync(row, true));
            TurnOffCommand = new AsyncRelayCommand<ManualDeviceRow>(row => SendDeviceAsync(row, false));
            SetPressureModeCommand = new AsyncRelayCommand<string>(SetPressureModeAsync, _ => IsLive && _canManual && _isManual && !_restricted);

            PurgeOverrideCommand = new AsyncRelayCommand(
                () => SendOverrideAsync(ScadaCommandCode.PurgeOverride, 0, "Yıkamayı onayla",
                    "\"İçeride amonyak var\" bilgisi ölçüme bakılmadan kaldırılacak. Atmosfer gerçekten temiz değilse kapak açıldığında amonyak ve hidrojen dışarı çıkar.\n\nBu komut alarm kaydına işlenir. Yıkamayı onaylıyor musunuz?"),
                () => IsLive && _canOverride && _ammoniaInside);

            HoldExitCommand = new AsyncRelayCommand(
                () => SendOverrideAsync(ScadaCommandCode.HoldExit, 0, "Korumalı beklemeyi bitir",
                    "Korumalı bekleme sıcaklık düşmeden sonlandırılacak: azot kesilir, tahliye açılır. Fırın hâlâ sıcaksa içeri hava girebilir.\n\nBu komut alarm kaydına işlenir. Korumalı beklemeyi bitirmek istiyor musunuz?"),
                () => IsLive && _canOverride && _seqState == 45);

            LidOverrideCommand = new AsyncRelayCommand(
                () => SendOverrideAsync(ScadaCommandCode.LidOverride, 1, "Kapak kilidini aş",
                    "Kapak kilidi sıcaklık ve basınç ölçümüne bakılmadan açılabilir hale gelecek; yalnızca tahliye vanasının ayarlı süredir açık olması aranır. Fırın sıcak ya da basınçlıysa kapağı açmak tehlikelidir.\n\nGeçerli olduğu sürece alarm aktif kalır. Kapak kilidini aşmak istiyor musunuz?"),
                () => IsLive && _canOverride && !_lidOverrideActive);

            LidOverrideCancelCommand = new AsyncRelayCommand(
                () => SendOverrideAsync(ScadaCommandCode.LidOverride, 0, "Kapak kilidi aşmayı iptal et", null),
                () => IsLive && _canOverride && _lidOverrideActive);

            _live.DataReceived += OnDataReceived;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => UpdateLive();
            _timer.Start();

            Set("Mode", "—");
            Set("ModeLevel", "Off");
            Set("Banner", "PLC verisi bekleniyor.");
            Set("OverrideNote", _canOverride ? string.Empty : $"Aşma komutları için '{PlcPermissions.Override}' yetkisi gerekir.");
            Publish();
        }

        // Olay aboneliğini bırakır ve zamanlayıcıyı durdurur.
        public void Dispose()
        {
            _timer.Stop();
            _live.DataReceived -= OnDataReceived;
        }

        // Cihaz listesine bir kart ekler.
        private void Add(int no, string name, bool restrictedBlocked, string? valueLabel = null, int valueMax = 0, bool needsValveMode = false)
        {
            Devices.Add(new ManualDeviceRow
            {
                No = no,
                Name = name,
                RestrictedBlocked = restrictedBlocked,
                NeedsValveMode = needsValveMode,
                HasValue = valueLabel != null,
                ValueLabel = valueLabel ?? string.Empty,
                ValueMax = valueMax
            });
        }

        // Canlı veri paketini işler: PLC okunabiliyorsa mod, cihaz durumları ve düğmeler güncellenir.
        private void OnDataReceived(ScadaLiveDto data)
        {
            _lastDataAt = DateTime.Now;
            _plcOk = data.IsConnected && data.Status != null;

            if (_plcOk)
                Apply(data.Status!);

            UpdateLive();
        }

        // Veri taze ve PLC okunabiliyorsa sayfayı canlı sayar; değilse bütün düğmeler kapanır.
        private void UpdateLive()
        {
            bool fresh = (DateTime.Now - _lastDataAt).TotalSeconds <= _settings.StaleSeconds;
            bool live = _live.IsConnected && fresh && _plcOk;

            if (live == IsLive)
                return;

            IsLive = live;

            if (!live)
            {
                foreach (var row in Devices)
                    row.IsEnabled = false;

                Set("Banner", "Canlı veri yok; komut gönderilemez.");
                Publish();
            }

            RefreshCommands();
        }

        // PLC durumunu mod şeridine, ölçümlere ve cihaz kartlarına yansıtır.
        private void Apply(ScadaStatus s)
        {
            _isManual = s.Mode == 1;
            _restricted = s.GetDeviceBit(1, 10);
            _seqState = s.SeqState;
            _lidOverrideActive = s.GetDeviceBit(1, 12);
            _ammoniaInside = s.GetDeviceBit(1, 7);
            int pressureMode = s.GetManualPressureMode();

            Set("Mode", StatusTexts.DescribeMode(s));
            Set("ModeLevel", _isManual ? (_restricted ? "Warn" : "On") : "Off");
            Set("Banner", BuildBanner());
            Set("Seq", StatusTexts.DescribeSeqState(s.SeqState));

            Set("Temp", (s.IsTcValid(0) ? s.TempPota.ToString("0", Tr) : "—") + " °C");
            Set("TempSet", $"set {s.TempSet.ToString("0", Tr)} °C");
            Set("Pressure", s.Pressure.ToString("0.0", Tr) + " mbar");
            Set("PressureSet", $"set {s.PressureSet.ToString("0.0", Tr)} mbar");
            Set("N2", s.N2FlowAct.ToString("0", Tr) + " L/h");
            Set("NH3", s.NH3FlowAct.ToString("0", Tr) + " L/h");
            Set("H2", "%" + s.H2Act.ToString("0.0", Tr));

            Set("PressureMode", pressureMode switch
            {
                0 => "Atmosfere açık",
                1 => "Otomatik kontrol",
                2 => "Elle vana",
                _ => $"Bilinmiyor ({pressureMode})"
            });
            Set("PressureNote", !_isManual ? "Basınç modu yalnızca manuel modda seçilir."
                : _restricted ? "Kısıtlı manuelde basıncı PLC yönetir."
                : string.Empty);

            Set("LidOverride", _lidOverrideActive ? "Kapak kilidi şu anda aşılmış durumda." : string.Empty);

            foreach (var row in Devices)
                UpdateDevice(row, s, pressureMode);

            Publish();
            RefreshCommands();
        }

        // Mod şeridinin altındaki açıklamayı üretir: komutların neden kabul edilmeyeceği ya da kısıtlı manuelin anlamı.
        private string BuildBanner()
        {
            if (!_isManual)
                return "Manuel mod seçili değil. Mod anahtarı panodadır; ekrandan değiştirilemez. Cihaz komutları yalnızca manuel modda kabul edilir.";

            if (!_canManual)
                return $"Cihazları elle çalıştırmak için '{PlcPermissions.Manual}' yetkisi gerekir.";

            if (_restricted)
                return "Kısıtlı manuel: içeride nitrasyon atmosferi var ya da korumalı bekleme sürüyor. Basınç, gaz, vakum ve sirkülasyonu PLC yönetir; ısıtma, fan soğutma ve eşanjör boşaltma serbesttir.";

            return "Manuel mod. Kilitlemeler geçerlidir: komut kabul edilse de izni olmayan cihaz çalışmaz.";
        }

        // Tek bir cihaz kartının durumunu, ayrıntısını, uyarısını ve düğmelerinin açık olup olmadığını günceller.
        private void UpdateDevice(ManualDeviceRow row, ScadaStatus s, int pressureMode)
        {
            bool on = false, fault = false, blocked = false;
            string detail = string.Empty;

            switch (row.No)
            {
                case 1:
                    on = s.GetDeviceBit(0, 0);
                    fault = s.HasFault(0);
                    blocked = s.HasInterlock(0);
                    detail = $"Motor {RunText(on)} · vana {OpenText(s.GetDeviceBit(0, 1))}";
                    break;
                case 2:
                    on = s.GetDeviceBit(0, 2);
                    detail = $"Vana {OpenText(on)}";
                    break;
                case 3:
                    on = s.GetDeviceBit(0, 8);
                    fault = s.HasFault(1);
                    blocked = s.HasInterlock(3);
                    detail = $"Hız %{s.FanSpeedAct.ToString("0", Tr)} · {s.VfdFreq.ToString("0.0", Tr)} Hz";
                    break;
                case 4:
                    on = s.GetDeviceBit(0, 7);
                    fault = s.HasFault(7);
                    blocked = s.HasInterlock(1);
                    detail = $"Set {s.TempSet.ToString("0", Tr)} °C · güç alt %{s.HeatOutAlt.ToString("0", Tr)}, üst %{s.HeatOutUst.ToString("0", Tr)}";
                    break;
                case 5:
                    on = s.GetDeviceBit(0, 9);
                    fault = s.HasFault(2);
                    blocked = s.HasInterlock(2);
                    detail = s.GetDeviceBit(0, 10) ? "Klepe açık" : s.GetDeviceBit(0, 11) ? "Klepe kapalı" : "Klepe arada";
                    break;
                case 6:
                    on = s.GetDeviceBit(0, 3);
                    fault = s.HasFault(4);
                    detail = $"{s.N2FlowAct.ToString("0", Tr)} L/h · set {s.N2FlowSet.ToString("0", Tr)} L/h";
                    break;
                case 7:
                    on = s.GetDeviceBit(0, 4);
                    fault = s.HasFault(5);
                    detail = $"{s.NH3FlowAct.ToString("0", Tr)} L/h · manuelde amonyak izni verilmez";
                    break;
                case 8:
                    on = s.GetDeviceBit(0, 5);
                    detail = $"Vana {OpenText(on)}";
                    break;
                case 9:
                    on = s.GetDeviceBit(0, 6);
                    detail = $"Pompa {RunText(on)}";
                    break;
                case 10:
                    on = s.GetDeviceBit(0, 12);
                    detail = $"Vana {OpenText(on)}";
                    break;
                case 11:
                    on = s.GetDeviceBit(0, 13);
                    detail = $"Vana {OpenText(on)}";
                    break;
                case 12:
                    on = s.GetDeviceBit(0, 14);
                    detail = $"Vana {OpenText(on)}";
                    break;
                case 13:
                    on = s.GetDeviceBit(1, 3);
                    fault = s.HasFault(6);
                    detail = $"Pompa {RunText(on)} · eşanjörde {(s.GetDeviceBit(1, 4) ? "su var" : "su yok")}";
                    break;
            }

            string reason = string.Empty;
            if (_isManual && _restricted && row.RestrictedBlocked)
                reason = "Kısıtlı manuelde PLC yönetir";
            else if (_isManual && row.NeedsValveMode && pressureMode != 2)
                reason = "Basınç modu \"elle vana\" olmalı";

            row.Level = fault ? "Fault" : on ? "On" : "Off";
            row.StateText = fault ? "ARIZA" : on ? "AÇIK" : "KAPALI";
            row.Detail = detail;
            row.Note = fault ? "Arıza var; alarm listesine bakın" : blocked ? "İsteniyor ama izni yok" : reason;
            row.IsEnabled = IsLive && _isManual && _canManual && reason.Length == 0;
        }

        // Cihaza aç ya da kapat komutunu gönderir; değer isteyen cihazda kutudaki değeri denetleyip ekler.
        private async Task SendDeviceAsync(ManualDeviceRow? row, bool on)
        {
            if (row == null)
                return;

            int value = 0;

            if (on && row.HasValue)
            {
                string text = (row.ValueText ?? string.Empty).Trim();

                // Boş bırakılırsa 0 gönderilir; PLC eski seti korur.
                if (text.Length > 0 && (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0 || value > row.ValueMax))
                {
                    ShowResult($"{row.Name}: {row.ValueLabel} 0 ile {row.ValueMax} arasında tam sayı olmalı.");
                    return;
                }
            }

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.Manual, row.No, on ? 1 : 0, value);
            ShowResult(result.IsSuccess
                ? $"{row.Name}: {(on ? "aç" : "kapat")} komutu kabul edildi."
                : $"{row.Name}: {result.Message}");
        }

        // Manuel basınç modunu seçer: 0 atmosfere açık, 1 otomatik kontrol (set ile), 2 elle vana.
        private async Task SetPressureModeAsync(string? mode)
        {
            if (!int.TryParse(mode, out int modeValue))
                return;

            int set = 0;

            if (modeValue == 1)
            {
                string text = (PressureSetText ?? string.Empty).Trim();
                if (text.Length > 0 && (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out set) || set < 0 || set > MaxPressureSet))
                {
                    ShowResult($"Basınç seti 0 ile {MaxPressureSet} mbar arasında tam sayı olmalı.");
                    return;
                }
            }

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.Manual, PressureModeDevice, modeValue, set);
            ShowResult(result.IsSuccess ? "Basınç modu komutu kabul edildi." : $"Basınç modu: {result.Message}");
        }

        // Onay alıp aşma komutunu gönderir; onay metni verilmezse sormadan gönderir.
        private async Task SendOverrideAsync(ScadaCommandCode code, int param1, string name, string? confirmText)
        {
            if (confirmText != null)
            {
                var answer = MessageBox.Show(confirmText, name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var result = await _api.SendCommandAsync((int)code, param1);
            ShowResult(result.IsSuccess ? $"{name}: komut kabul edildi." : $"{name}: {result.Message}");
        }

        // Son komutun sonucunu saatle birlikte gösterir.
        private void ShowResult(string text)
        {
            Set("CmdResult", $"{DateTime.Now:HH:mm:ss}  {text}");
            Publish();
        }

        // Düğmelerin etkin/pasif durumunu yeniden hesaplatır.
        private void RefreshCommands()
        {
            SetPressureModeCommand.NotifyCanExecuteChanged();
            PurgeOverrideCommand.NotifyCanExecuteChanged();
            HoldExitCommand.NotifyCanExecuteChanged();
            LidOverrideCommand.NotifyCanExecuteChanged();
            LidOverrideCancelCommand.NotifyCanExecuteChanged();
        }

        // Adla okunan bir değeri günceller.
        private void Set(string key, string value)
        {
            _values[key] = value;
        }

        // Adla okunan bütün değerlerin değiştiğini ekrana bildirir.
        private void Publish()
        {
            OnPropertyChanged("Item[]");
        }

        // Vana durumunu metne çevirir.
        private static string OpenText(bool open)
        {
            return open ? "açık" : "kapalı";
        }

        // Motor durumunu metne çevirir.
        private static string RunText(bool running)
        {
            return running ? "çalışıyor" : "duruyor";
        }
    }
}