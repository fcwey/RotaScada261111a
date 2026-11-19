using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Services;
using Entities.Concrete;
using Entities.Dtos;
using Entities.Enums;
using Entities.Structures;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace DesktopUI.ViewModels
{
    // Çalışma saati tablosunun bir satırı; numara komut 16'nın sayaç numarasıdır
    public class RunHourRow : ObservableObject
    {
        private string _hours = "—";

        public int No { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Hours { get => _hours; set => SetProperty(ref _hours, value); }
    }

    // Kaçak testi geçmişinin bir satırı
    public class LeakTestRow
    {
        public string Time { get; set; } = string.Empty;
        public string ResultText { get; set; } = string.Empty;
        public string Level { get; set; } = "Warn";     // On geçti, Bad kaldı, Warn tamamlanamadı
        public string Rise { get; set; } = string.Empty;
        public string Rate { get; set; } = string.Empty;
        public string Settings { get; set; } = string.Empty;
        public string Temp { get; set; } = string.Empty;
    }

    // Bakım sayfası: kaçak testi, motor çalışma saatleri ve PID otomatik ayar
    public class MaintenanceViewModel : ObservableObject, IDisposable
    {
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, string> _values = new();
        private readonly bool _canOperate;
        private readonly bool _canMaintain;

        private DateTime _lastDataAt = DateTime.MinValue;
        private bool _plcOk;
        private bool _isLive;
        private short _leakState;
        private short _tuneStatus;
        private int _lastLeakCount = -1;

        public ObservableCollection<RunHourRow> RunHours { get; } = new();
        public ObservableCollection<LeakTestRow> LeakTests { get; } = new();

        public IAsyncRelayCommand LeakStartCommand { get; }
        public IAsyncRelayCommand LeakCancelCommand { get; }
        public IAsyncRelayCommand<RunHourRow> ResetHoursCommand { get; }
        public IAsyncRelayCommand<string> TuneStartCommand { get; }
        public IAsyncRelayCommand TuneCancelCommand { get; }

        // Ekranın adla okuduğu metin ve durum değerleri; olmayan ad boş döner.
        public string this[string key] => _values.TryGetValue(key, out var value) ? value : string.Empty;

        public bool IsLive { get => _isLive; private set => SetProperty(ref _isLive, value); }

        // Sayaç listesini ve komutları kurar, canlı veriye abone olur, test geçmişini ve test ayarlarını ister.
        public MaintenanceViewModel(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _live = live;
            _settings = settings;
            _canOperate = session.HasRole(PlcPermissions.Operate);
            _canMaintain = session.HasRole(PlcPermissions.Maintenance);

            // Sayaç numaraları kılavuzdaki komut 16 sırasıyla aynıdır.
            RunHours.Add(new RunHourRow { No = 1, Name = "Sirkülasyon fanı" });
            RunHours.Add(new RunHourRow { No = 2, Name = "Soğutma fanı" });
            RunHours.Add(new RunHourRow { No = 3, Name = "Vakum motoru" });
            RunHours.Add(new RunHourRow { No = 4, Name = "Hidrolik motoru" });
            RunHours.Add(new RunHourRow { No = 5, Name = "Rezistanslar" });

            LeakStartCommand = new AsyncRelayCommand(StartLeakTestAsync, () => IsLive && _canOperate && _leakState == 0);
            LeakCancelCommand = new AsyncRelayCommand(CancelLeakTestAsync, () => IsLive && _canOperate && _leakState != 0);
            ResetHoursCommand = new AsyncRelayCommand<RunHourRow>(ResetHoursAsync, _ => IsLive && _canMaintain);
            TuneStartCommand = new AsyncRelayCommand<string>(StartTuneAsync, _ => IsLive && _canMaintain && _tuneStatus is not (1 or 2));
            TuneCancelCommand = new AsyncRelayCommand(CancelTuneAsync, () => IsLive && _canMaintain && _tuneStatus is 1 or 2);

            _live.DataReceived += OnDataReceived;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => UpdateLive();
            _timer.Start();

            Set("LeakState", "—");
            Set("LeakLevel", "Off");
            Set("MaintainNote", _canMaintain ? string.Empty : $"Sayaç sıfırlama ve PID ayarı için '{PlcPermissions.Maintenance}' yetkisi gerekir.");
            Publish();

            _ = LoadLeakTestsAsync();
            _ = LoadLeakSettingsAsync();
        }

        // Olay aboneliğini bırakır ve zamanlayıcıyı durdurur.
        public void Dispose()
        {
            _timer.Stop();
            _live.DataReceived -= OnDataReceived;
        }

        // Canlı veri paketini işler: PLC okunabiliyorsa test, sayaç ve PID alanları güncellenir.
        private void OnDataReceived(ScadaLiveDto data)
        {
            _lastDataAt = DateTime.Now;
            _plcOk = data.IsConnected && data.Status != null;

            if (_plcOk)
                Apply(data.Status!);

            UpdateLive();
        }

        // Veri taze ve PLC okunabiliyorsa sayfayı canlı sayar; değilse düğmeler kapanır.
        private void UpdateLive()
        {
            bool fresh = (DateTime.Now - _lastDataAt).TotalSeconds <= _settings.StaleSeconds;
            bool live = _live.IsConnected && fresh && _plcOk;

            if (live == IsLive)
                return;

            IsLive = live;
            Set("LiveNote", live ? string.Empty : "Canlı veri yok; komut gönderilemez.");
            Publish();
            RefreshCommands();
        }

        // PLC durumundaki kaçak testi, çalışma saati ve PID alanlarını ekran metinlerine çevirir.
        private void Apply(ScadaStatus s)
        {
            _leakState = s.LeakState;
            _tuneStatus = s.TuneStatus;

            // Kaçak testi
            bool testRunning = s.LeakState != 0;
            Set("LeakState", s.LeakState switch
            {
                0 => "Boşta",
                1 => "Vakum çekiliyor",
                2 => "Dinlenme",
                3 => "Ölçüm",
                4 => "Azot dolumu",
                _ => $"Bilinmiyor ({s.LeakState})"
            });
            Set("LeakLevel", testRunning ? "On" : "Off");
            Set("LeakRemain", testRunning ? FormatSeconds(s.LeakRemainSec) : "—");
            Set("LeakRise", s.LeakState == 3 || s.LeakResult != 0 ? s.LeakRise.ToString("0.0", Tr) + " mbar" : "—");
            Set("LeakPressure", s.Pressure.ToString("0.0", Tr) + " mbar");
            Set("LeakTemp", (s.IsTcValid(0) ? s.TempPota.ToString("0", Tr) : "—") + " °C");
            Set("LeakLast", testRunning ? "Test sürüyor" : DescribeLeakResult(s.LeakResult));
            Set("LeakLastLevel", testRunning ? "Off" : s.LeakResult switch { 0 => "Off", 1 => "On", 2 => "Bad", _ => "Warn" });

            // Test sonuçlanınca sunucu kaydı yazar; kısa bir gecikmeyle geçmiş yeniden okunur.
            if (_lastLeakCount >= 0 && s.LeakCount != _lastLeakCount)
                _ = ReloadLeakTestsLaterAsync();
            _lastLeakCount = s.LeakCount;

            // Çalışma saatleri
            SetHours(1, s.CircHours);
            SetHours(2, s.CoolFanHours);
            SetHours(3, s.VacPumpHours);
            SetHours(4, s.HydPumpHours);
            SetHours(5, s.HeatHours);

            // PID
            Set("PidState", s.PidState switch
            {
                0 => "Pasif",
                1 => "Ön ayar yapılıyor",
                2 => "İnce ayar yapılıyor",
                3 => "Otomatik kontrol",
                4 => "Manuel",
                _ => $"Bilinmiyor ({s.PidState})"
            });
            Set("TuneStatus", s.TuneStatus switch
            {
                0 => "Ayar yok",
                1 => "Ön ayar sürüyor",
                2 => "İnce ayar sürüyor (saatler sürebilir)",
                3 => "Son ayar başarılı",
                4 => $"Son ayar başarısız ya da iptal edildi (PID hata kodu {s.PidError})",
                _ => $"Bilinmiyor ({s.TuneStatus})"
            });
            Set("TuneLevel", s.TuneStatus switch { 1 or 2 => "On", 3 => "Ok", 4 => "Bad", _ => "Off" });
            Set("PidGain", s.PidGain.ToString("0.####", Tr));
            Set("PidTi", s.PidTi.ToString("0.##", Tr) + " sn");
            Set("PidTd", s.PidTd.ToString("0.##", Tr) + " sn");
            Set("PidTemp", $"{(s.IsTcValid(0) ? s.TempPota.ToString("0", Tr) : "—")} °C · set {s.TempSet.ToString("0", Tr)} °C");

            Publish();
            RefreshCommands();
        }

        // Verilen numaralı sayacın saatini tabloya yazar.
        private void SetHours(int no, float hours)
        {
            var row = RunHours.FirstOrDefault(r => r.No == no);
            if (row != null)
                row.Hours = hours.ToString("0.0", Tr) + " saat";
        }

        // Onay alıp kaçak testini başlatır.
        private async Task StartLeakTestAsync()
        {
            var answer = MessageBox.Show(
                "Kaçak testi başlatılacak: fırın vakuma alınır, dinlendirilir, basınç artışı ölçülür ve azotla doldurulur.\n\nKoşullar: program çalışmıyor, fırın soğuk, kapak kapalı ve kilitli, atmosfer temiz. Test sürerken program başlatılamaz.\n\nTesti başlatmak istiyor musunuz?",
                "Kaçak testi", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.LeakTest, 1);
            ShowResult(result.IsSuccess ? "Kaçak testi başlatıldı." : $"Kaçak testi başlatılamadı: {result.Message}");
        }

        // Süren kaçak testini iptal eder; test azot dolumuna geçer, dolumdayken verilirse dolum da durur.
        private async Task CancelLeakTestAsync()
        {
            string text = _leakState == 4
                ? "Azot dolumu da durdurulacak; fırın vakumda kalabilir.\n\nDolumu durdurmak istiyor musunuz?"
                : "Test iptal edilecek ve fırın azotla doldurulacak.\n\nTesti iptal etmek istiyor musunuz?";

            if (MessageBox.Show(text, "Kaçak testi", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.LeakTest, 0);
            ShowResult(result.IsSuccess ? "Kaçak testi iptal komutu kabul edildi." : $"Kaçak testi iptal edilemedi: {result.Message}");
        }

        // Onay alıp seçilen çalışma saati sayacını sıfırlar.
        private async Task ResetHoursAsync(RunHourRow? row)
        {
            if (row == null)
                return;

            var answer = MessageBox.Show(
                $"{row.Name} çalışma saati ({row.Hours}) sıfırlanacak. Bu işlem geri alınamaz; değeri bakım defterine not etmek isteyebilirsiniz.\n\nSıfırlamak istiyor musunuz?",
                "Çalışma saati sıfırla", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.RunHoursReset, row.No);
            ShowResult(result.IsSuccess ? $"{row.Name} sayacı sıfırlandı." : $"{row.Name} sayacı sıfırlanamadı: {result.Message}");
        }

        // Onay alıp PID otomatik ayarını başlatır: "1" ön ayar, "2" ince ayar.
        private async Task StartTuneAsync(string? mode)
        {
            if (!int.TryParse(mode, out int modeValue) || modeValue is not (1 or 2))
                return;

            string name = modeValue == 1 ? "Ön ayar" : "İnce ayar";
            string condition = modeValue == 1
                ? "Ön ayar soğuk fırında yapılır."
                : "İnce ayar fırın setteyken yapılır ve saatler sürebilir.";

            var answer = MessageBox.Show(
                $"{name} başlatılacak. {condition}\n\nKoşul: ısıtma yapılıyor ve set sabit olmalı (manuel modda ısıtma ya da program Beklet ile duraklatılmış). Ayar sırasında sıcaklık dalgalanır; içeride ürün varken yapmayın.\n\nBaşarılı olursa PID değerleri değişir. Başlatmak istiyor musunuz?",
                "PID otomatik ayar", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.TuneStart, modeValue);
            ShowResult(result.IsSuccess ? $"{name} başlatıldı." : $"{name} başlatılamadı: {result.Message}");
        }

        // Süren PID otomatik ayarını iptal eder.
        private async Task CancelTuneAsync()
        {
            var result = await _api.SendCommandAsync((int)ScadaCommandCode.TuneCancel);
            ShowResult(result.IsSuccess ? "PID ayarı iptal komutu kabul edildi." : $"PID ayarı iptal edilemedi: {result.Message}");
        }

        // Sunucunun test kaydını yazmasına zaman tanıyıp geçmişi yeniden okur.
        private async Task ReloadLeakTestsLaterAsync()
        {
            await Task.Delay(3000);
            await LoadLeakTestsAsync();
        }

        // Son 90 günün kaçak testi kayıtlarını en yenisi üstte olacak şekilde okur.
        private async Task LoadLeakTestsAsync()
        {
            var result = await _api.GetAsync<List<LeakTestRecord>>("api/leaktests");
            if (!result.IsSuccess || result.Data == null)
            {
                Set("LeakHistoryNote", result.Message ?? "Test geçmişi alınamadı.");
                Publish();
                return;
            }

            LeakTests.Clear();

            foreach (var test in result.Data.OrderByDescending(t => t.Timestamp))
            {
                LeakTests.Add(new LeakTestRow
                {
                    Time = test.Timestamp.ToString("dd.MM.yyyy HH:mm", Tr),
                    ResultText = test.ResultText,
                    Level = test.Result == 1 ? "On" : test.Result == 2 ? "Bad" : "Warn",
                    Rise = test.RiseMbar.ToString("0.0", Tr) + " mbar",
                    Rate = test.RiseRateMbarPerMin.ToString("0.00", Tr) + " mbar/dk",
                    Settings = $"{test.TestMin} dk · sınır {test.MaxRiseMbar.ToString("0.0", Tr)} mbar · vakum {test.VacTargetMbar} mbar",
                    Temp = test.TempPota.ToString("0", Tr) + " °C"
                });
            }

            Set("LeakHistoryNote", LeakTests.Count == 0 ? "Son 90 günde test yapılmamış." : $"{LeakTests.Count} test (son 90 gün)");
            Publish();
        }

        // Geçerli kaçak testi ayarlarını (DB204) okuyup tek satırlık özet olarak gösterir.
        private async Task LoadLeakSettingsAsync()
        {
            var result = await _api.GetAsync<List<ParamValueDto>>("api/settings/parameters");
            if (!result.IsSuccess || result.Data == null)
                return;

            int Value(string name) => result.Data.FirstOrDefault(p => p.Name == name)?.Value ?? 0;

            Set("LeakSettings",
                $"En yüksek sıcaklık {Value("LeakMaxTemp")} °C · hedef vakum {Value("LeakVacTarget")} mbar mutlak · dinlenme {Value("LeakSettleSec")} sn · " +
                $"ölçüm {Value("LeakTestMin")} dk · izin verilen artış {(Value("LeakMaxRise") / 10.0).ToString("0.0", Tr)} mbar");
            Publish();
        }

        // Kaçak testi sonuç kodunu metne çevirir.
        private static string DescribeLeakResult(int result)
        {
            return result switch
            {
                0 => "Sonuç yok",
                1 => "Geçti",
                2 => "Kaldı (artış sınırın üstünde)",
                3 => "Hedef vakuma inilemedi",
                4 => "Operatör iptal etti",
                5 => "İzin kalktı (kapak, acil stop, alarm, motor arızası)",
                6 => "Basınç sensörü arızası",
                7 => "Sıcaklık sınırı aşıldı",
                _ => $"Bilinmeyen sonuç ({result})"
            };
        }

        // Saniyeyi "12 dk 05 sn" biçiminde yazar.
        private static string FormatSeconds(int seconds)
        {
            if (seconds < 0)
                seconds = 0;

            return seconds >= 60 ? $"{seconds / 60} dk {seconds % 60:00} sn" : $"{seconds} sn";
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
            LeakStartCommand.NotifyCanExecuteChanged();
            LeakCancelCommand.NotifyCanExecuteChanged();
            ResetHoursCommand.NotifyCanExecuteChanged();
            TuneStartCommand.NotifyCanExecuteChanged();
            TuneCancelCommand.NotifyCanExecuteChanged();
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
    }
}