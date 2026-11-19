using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Controls;
using DesktopUI.Helpers;
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
    // Ana ekrandaki aktif alarm tablosunun bir satırı
    public class AlarmRow
    {
        public string Time { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ClassText { get; set; } = string.Empty;
        public string Level { get; set; } = "Warn";     // Info, Warn, Bad
    }

    // Aktif adım panelindeki bir değer: üstte ad, altta değer
    public class StepItem
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    // Ana ekran: fırın şeması, program durumu, aktif adım, program eğrisi, şarj toplamları, komutlar ve aktif alarmlar
    public class OverviewViewModel : ObservableObject, IDisposable
    {
        private const int MaxAlarmRows = 3;
        private const float TimelineStartTemp = 20f;
        private const int TraceIntervalSeconds = 30;    // Canlı ölçümün çizgiye eklenme aralığı
        private const int TraceHistoryPoints = 600;     // Geçmişten okunan nokta sayısı
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly SessionService _session;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, string> _values = new();
        private readonly List<TraceSample> _trace = new();

        private Recipe? _recipe;
        private RecipeTimeline? _timeline;
        private DateTime _lastDataAt = DateTime.MinValue;
        private DateTime _lastTraceAt = DateTime.MinValue;
        private DateTime? _traceStart;                  // Şarjın başladığı an; ölçülen çizginin sıfır noktası
        private bool _plcOk;
        private bool _isLive;
        private short _seqState;
        private short _holdReason;
        private bool _canStart;
        private int _batchId;
        private int _activeStep;
        private int _stepElapsed;
        private float _tempPota;
        private int _shownStepNo = -1;
        private double _stepProgress;
        private ProfileChartData? _chartData;

        // Başka bir sayfaya geçilmesi istendiğinde sayfanın anahtarıyla tetiklenir.
        public event Action<string>? NavigateRequested;

        public ObservableCollection<AlarmRow> Alarms { get; } = new();
        public ObservableCollection<StepItem> ActiveStepItems { get; } = new();

        public IRelayCommand StartCommand { get; }
        public IAsyncRelayCommand HoldCommand { get; }
        public IAsyncRelayCommand ResumeCommand { get; }
        public IAsyncRelayCommand StopCommand { get; }
        public IAsyncRelayCommand VacuumOkCommand { get; }
        public IAsyncRelayCommand SkipToCoolingCommand { get; }

        // Ekranın adla okuduğu metin ve durum değerleri; olmayan ad boş döner.
        public string this[string key] => _values.TryGetValue(key, out var value) ? value : string.Empty;

        public bool IsLive { get => _isLive; private set => SetProperty(ref _isLive, value); }
        public double StepProgress { get => _stepProgress; private set => SetProperty(ref _stepProgress, value); }
        public ProfileChartData? ChartData { get => _chartData; private set => SetProperty(ref _chartData, value); }

        // Program yürürken (vakum, ısıtma, soğutma, duraklatılmış) adım ve ilerleme gösterilir.
        private bool ProgramRunning => _seqState is 20 or 30 or 50 or 70;

        // Bağımlılıkları alır, komutları kurar, canlı veri olaylarına abone olur ve ilk verileri ister.
        public OverviewViewModel(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _session = session;
            _live = live;
            _settings = settings;

            bool canOperate = session.HasRole(PlcPermissions.Operate);
            bool canEdit = session.HasRole(PlcPermissions.ProgramEdit);

            StartCommand = new RelayCommand(() => NavigateRequested?.Invoke("recipes"), () => IsLive && _canStart);

            HoldCommand = new AsyncRelayCommand(
                () => SendAsync(ScadaCommandCode.Hold, "Beklet", null),
                () => IsLive && canOperate && _seqState is 20 or 30 or 50);

            ResumeCommand = new AsyncRelayCommand(
                () => SendAsync(ScadaCommandCode.Resume, "Devam", null),
                () => IsLive && canOperate && _seqState == 70);

            StopCommand = new AsyncRelayCommand(
                () => SendAsync(ScadaCommandCode.Stop, "Stop", "Program durdurulacak ve fırın yıkamaya geçecek. Bu işlem geri alınamaz.\n\nProgramı durdurmak istiyor musunuz?"),
                () => IsLive && canOperate && _seqState is 20 or 30 or 50 or 70);

            VacuumOkCommand = new AsyncRelayCommand(
                () => SendAsync(ScadaCommandCode.VacuumOk, "Vakum Onay", "Vakum hedefe ulaşmadan adım geçilecek.\n\nVakumu onaylıyor musunuz?"),
                () => IsLive && canOperate && _seqState == 70 && _holdReason is 6 or 7);

            SkipToCoolingCommand = new AsyncRelayCommand(
                () => SendAsync(ScadaCommandCode.SkipToCooling, "Soğutmaya Geç", "Kalan ısıtma adımları atlanacak ve soğutma başlayacak.\n\nSoğutmaya geçmek istiyor musunuz?"),
                () => IsLive && canEdit && _seqState is 20 or 30 or 70);

            _live.DataReceived += OnDataReceived;
            _live.AlarmsReceived += OnAlarmsReceived;
            _live.ProfileChanged += OnProfileChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => UpdateLive();
            _timer.Start();

            Set("AlarmSummary", "yok");
            Set("ChartNote", "Çalışan program yok");
            Set("StepPanel", "Off");
            Publish();

            _ = LoadAlarmsAsync();
        }

        // Olay aboneliklerini bırakır ve zamanlayıcıyı durdurur.
        public void Dispose()
        {
            _timer.Stop();
            _live.DataReceived -= OnDataReceived;
            _live.AlarmsReceived -= OnAlarmsReceived;
            _live.ProfileChanged -= OnProfileChanged;
        }

        // Canlı veri paketini işler: PLC okunabiliyorsa bütün değerleri günceller.
        private void OnDataReceived(ScadaLiveDto data)
        {
            _lastDataAt = DateTime.Now;
            _plcOk = data.IsConnected && data.Status != null;

            if (_plcOk)
                Apply(data.Status!);

            UpdateLive();
        }

        // Çalışan reçete değişince (çalışırken değişiklik) reçeteyi yeniden okur; eğri ve adım paneli yenilenir.
        private void OnProfileChanged()
        {
            if (_batchId > 0)
                _ = LoadRecipeAsync();
        }

        // Veri taze ve PLC okunabiliyorsa ekranı canlı sayar; değilse değerlerin üstü örtülür.
        private void UpdateLive()
        {
            bool fresh = (DateTime.Now - _lastDataAt).TotalSeconds <= _settings.StaleSeconds;
            bool live = _live.IsConnected && fresh && _plcOk;

            if (live != IsLive)
            {
                IsLive = live;
                RefreshCommands();
            }
        }

        // PLC durumundaki bütün değerleri ekranın okuduğu metinlere ve cihaz durumlarına çevirir.
        private void Apply(ScadaStatus s)
        {
            _seqState = s.SeqState;
            _holdReason = s.HoldReason;
            _canStart = s.SeqState == 0 && s.HasPermit(0);
            _activeStep = s.ActiveStep;
            _stepElapsed = s.StepElapsedMin;
            _tempPota = s.TempPota;

            bool programActive = ProgramRunning;
            bool knApprox = s.GetDeviceBit(1, 13);

            // Program durumu
            Set("Temp", TempText(s.TempPota, s.IsTcValid(0)));
            Set("TempSet", $"set {N0(s.TempSet)} °C");
            Set("Pressure", N1(s.Pressure));
            Set("PressureSet", $"set {N1(s.PressureSet)} mbar");
            Set("Kn", N1(s.Kn));
            Set("KnNote", knApprox ? "yaklaşık (CO₂ veriliyor)" : "ölçülen");
            Set("H2", "%" + N1(s.H2Act));
            Set("H2Set", $"set %{N1(s.H2Set)}");

            string message = StatusTexts.DescribeSeqMessage(s.SeqMessage);
            Set("StepInfo", programActive ? $"Adım {s.ActiveStep} · {message}" : message);

            int stepTotal = s.StepElapsedMin + s.StepRemainMin;
            Set("StepTime", programActive ? $"{s.StepElapsedMin} / {stepTotal} dk" : "—");
            StepProgress = programActive && stepTotal > 0 ? 100.0 * s.StepElapsedMin / stepTotal : 0;

            // Şarj toplamları
            Set("Energy", $"{N1(s.BatchEnergyKwh)} kWh");
            Set("Power", $"{N1(s.PowerKw)} kW");
            Set("N2Total", $"{N0(s.N2Total)} L");
            Set("NH3Total", $"{N0(s.NH3Total)} L");
            Set("CO2Total", $"{N0(s.CO2Total)} L");
            Set("WaterTotal", $"{N1(s.WaterTotal)} L");

            // Gaz hatları
            bool n2 = s.GetDeviceBit(0, 3), nh3 = s.GetDeviceBit(0, 4), co2 = s.GetDeviceBit(0, 5);
            bool water = s.GetDeviceBit(0, 6), extN2 = s.GetDeviceBit(0, 2), emgN2 = s.GetDeviceBit(0, 14);

            Set("N2Valve", Level(n2));
            Set("NH3Valve", Level(nh3));
            Set("CO2Valve", Level(co2));
            Set("WaterPump", Level(water));
            Set("ExtN2Valve", Level(extN2));
            Set("EmgN2Valve", Level(emgN2));
            Set("N2Mfc", Level(n2, s.HasFault(4)));
            Set("NH3Mfc", Level(nh3, s.HasFault(5)));
            Set("N2Flow", s.HasFault(4) ? "MFC arızası" : $"{N0(s.N2FlowAct)} L/h");
            Set("NH3Flow", s.HasFault(5) ? "MFC arızası" : $"{N0(s.NH3FlowAct)} L/h");
            Set("CO2Text", OpenText(co2));
            Set("WaterText", water ? "pompa çalışıyor" : "pompa duruyor");
            Set("ExtN2Text", OpenText(extN2));
            Set("EmgN2Text", OpenText(emgN2));
            Set("GasIn", Level(n2 || nh3 || co2 || water || extN2 || emgN2));

            // Isıtma ve sirkülasyon
            Set("Heater", Level(s.GetDeviceBit(0, 7), s.HasFault(7)));
            Set("HeatUstTemp", TempText(s.TempUst, s.IsTcValid(2)) + " °C");
            Set("HeatAltTemp", TempText(s.TempAlt, s.IsTcValid(1)) + " °C");
            Set("HeatUstOut", $"güç %{N0(s.HeatOutUst)}");
            Set("HeatAltOut", $"güç %{N0(s.HeatOutAlt)}");
            Set("CircFan", Level(s.GetDeviceBit(0, 8), s.HasFault(1)));
            Set("CircText", s.HasFault(1) ? "Sirkülasyon arızası" : $"Sirkülasyon {N1(s.VfdFreq)} Hz");

            // Pota içi ve kapak üstü ölçümler
            Set("PotaTemp", TempText(s.TempPota, s.IsTcValid(0)) + " °C");
            Set("PotaSet", $"Pota · set {N0(s.TempSet)} °C");
            Set("Material", Level(s.IsTcValid(5)));
            Set("MaterialText", $"Malzeme {N0(s.TempMalzeme)} °C");
            Set("PressureMimic", $"{N1(s.Pressure)} mbar");
            Set("H2Mimic", $"%{N1(s.H2Act)} H₂");
            Set("H2Sensor", Level(s.GetDeviceBit(1, 5)));
            Set("H2Line", s.GetDeviceBit(1, 5) ? "örnek hattı açık" : "sensör kapalı");

            // Su soğutma (eşanjör)
            Set("PreFill", Level(s.GetDeviceBit(1, 2)));
            Set("MainWater", Level(s.GetDeviceBit(1, 1), s.HasFault(6)));
            Set("DrainPump", Level(s.GetDeviceBit(1, 3)));
            Set("PreFillText", OpenText(s.GetDeviceBit(1, 2)));
            Set("MainWaterText", s.HasFault(6) ? "arıza" : OpenText(s.GetDeviceBit(1, 1)));
            Set("DrainPumpText", RunText(s.GetDeviceBit(1, 3), false));
            Set("ExchWater", s.GetDeviceBit(1, 4) ? "su var" : "su yok");
            Set("Exchanger", Level(s.GetDeviceBit(1, 4)));

            // Soğutma havası
            bool damperOpen = s.GetDeviceBit(0, 10), damperClosed = s.GetDeviceBit(0, 11);
            Set("Damper", Level(damperOpen));
            Set("DamperText", damperOpen ? "Klepe açık" : damperClosed ? "Klepe kapalı" : "Klepe arada");
            Set("CoolFan", Level(s.GetDeviceBit(0, 9), s.HasFault(2)));
            Set("CoolFanText", "Soğutma fanı: " + RunText(s.GetDeviceBit(0, 9), s.HasFault(2)));

            // Vakum, hidrolik ve çıkış
            bool vacValve = s.GetDeviceBit(0, 1), pcv = s.GetDeviceBit(0, 12), drain = s.GetDeviceBit(0, 13);
            Set("VacValve", Level(vacValve));
            Set("VacPump", Level(s.GetDeviceBit(0, 0), s.HasFault(0)));
            Set("VacPumpText", "Vakum pompası: " + RunText(s.GetDeviceBit(0, 0), s.HasFault(0)));
            Set("Hydraulic", Level(s.GetDeviceBit(1, 6), s.HasFault(3)));
            Set("HydraulicText", "Hidrolik ünite: " + RunText(s.GetDeviceBit(1, 6), s.HasFault(3)));
            Set("Pcv", Level(pcv));
            Set("Drain", Level(drain));
            Set("Outlet", Level(pcv || drain || vacValve));
            Set("ToFlare", Level(pcv || drain));
            Set("Burner", Level(s.GetDeviceBit(0, 15)));
            Set("BurnerText", s.GetDeviceBit(0, 15) ? "Brülör yanıyor" : "Brülör sönük");

            // Şemanın üstündeki durum etiketleri
            bool lidLocked = s.GetDeviceBit(1, 0), lidOverride = s.GetDeviceBit(1, 12), ammonia = s.GetDeviceBit(1, 7);
            Set("LidLevel", lidOverride ? "Fault" : lidLocked ? "On" : "Warn");
            Set("LidText", lidOverride ? "Kapak kilidi aşılmış" : lidLocked ? "Kapak kapalı ve kilitli" : "Kapak kilitli değil");
            Set("NH3Level", ammonia ? "Warn" : "Off");
            Set("NH3Text", ammonia ? "İçeride amonyak var" : "Atmosfer temiz");
            Set("KnLevel", knApprox ? "Warn" : "Off");
            Set("KnChip", knApprox ? "Kn yaklaşık" : "Kn ölçülüyor");

            Set("Hint", BuildHint(s));

            // Şarj değiştiyse çalışan reçete ve ölçüm geçmişi yeniden okunur; şarj yoksa eğri ve adım paneli boşaltılır.
            if (s.BatchId != _batchId)
            {
                _batchId = s.BatchId;
                _recipe = null;
                _timeline = null;
                _shownStepNo = -1;
                _trace.Clear();
                _traceStart = null;
                _lastTraceAt = DateTime.MinValue;

                if (_batchId > 0)
                {
                    _ = LoadRecipeAsync();
                    _ = LoadTraceAsync();
                }
                else
                {
                    Set("ChartNote", "Çalışan program yok");
                }
            }

            AddTraceSample();
            UpdateStepPanel();
            RefreshChart();

            Publish();
            RefreshCommands();
        }

        // PLC'de çalışan reçeteyi (DB203) okuyup zaman çizgisini kurar.
        private async Task LoadRecipeAsync()
        {
            int batchId = _batchId;

            var result = await _api.GetAsync<Recipe>("api/recipes/active");
            if (batchId != _batchId)
                return;

            if (!result.IsSuccess || result.Data == null)
            {
                Set("ChartNote", "Çalışan reçete okunamadı");
                Publish();
                return;
            }

            _recipe = result.Data;
            _timeline = RecipeTimeline.Build(_recipe, TimelineStartTemp);
            _shownStepNo = -1;

            Set("ChartNote", $"{_recipe.RecipeName} · adımların üzerine gelince ayrıntıları görünür");
            UpdateStepPanel();
            RefreshChart();
            Publish();
        }

        // Şarjın başından bu yana ölçülen pota sıcaklığını ve şarjın başlangıç anını sunucudan okur; sayfa açıkken biriken canlı kayıtlar sona eklenir.
        private async Task LoadTraceAsync()
        {
            int batchId = _batchId;

            var result = await _api.GetAsync<TrendDataDto>($"api/trends/batch/{batchId}?tags=TempPota&points={TraceHistoryPoints}");
            if (batchId != _batchId || !result.IsSuccess || result.Data == null)
                return;

            var temps = result.Data.Series.FirstOrDefault(s => s.Tag == "TempPota");
            if (temps == null)
                return;

            int count = Math.Min(result.Data.Timestamps.Count, temps.Values.Count);
            var history = new List<TraceSample>();

            for (int i = 0; i < count; i++)
                history.Add(new TraceSample { Time = result.Data.Timestamps[i], Temp = temps.Values[i] });

            DateTime lastTime = history.Count > 0 ? history[^1].Time : DateTime.MinValue;
            history.AddRange(_trace.Where(t => t.Time > lastTime));

            _trace.Clear();
            _trace.AddRange(history);
            _traceStart = result.Data.Start;
            RefreshChart();
        }

        // Şarj sürerken ölçülen sıcaklığı belirli aralıkla çizgiye ekler; programın hangi adımda olduğuna bakmaz.
        private void AddTraceSample()
        {
            if (_batchId <= 0)
                return;

            DateTime now = DateTime.Now;
            if ((now - _lastTraceAt).TotalSeconds < TraceIntervalSeconds)
                return;

            _lastTraceAt = now;

            // Geçmiş henüz okunmadıysa çizgi geçici olarak ilk canlı kayıttan başlar; geçmiş gelince gerçek başlangıca çekilir.
            _traceStart ??= now;

            _trace.Add(new TraceSample { Time = now, StepNo = _activeStep, Temp = _tempPota });
        }

        // Aktif adım panelini çalışan adımın değerleriyle doldurur; adım değişmediyse dokunmaz.
        private void UpdateStepPanel()
        {
            var step = ProgramRunning ? _recipe?.Steps.FirstOrDefault(s => s.StepNo == _activeStep) : null;

            if (step == null)
            {
                if (_shownStepNo != 0)
                {
                    ActiveStepItems.Clear();
                    Set("StepPanel", "Off");
                    _shownStepNo = 0;
                }
                return;
            }

            if (_shownStepNo == step.StepNo)
                return;

            _shownStepNo = step.StepNo;
            ActiveStepItems.Clear();

            switch (step.StepType)
            {
                case 1:
                    Set("StepTitle", $"AKTİF ADIM {step.StepNo} · VAKUM");
                    AddItem("Vakum seti", $"{step.Vac_SetPoint} mbar mutlak");
                    AddItem("Zaman aşımı", $"{step.Vac_Timeout} dk");
                    break;

                case 2:
                    Set("StepTitle", $"AKTİF ADIM {step.StepNo} · ISITMA");
                    AddItem("Hedef sıcaklık", $"{step.TargetTemp} °C");
                    AddItem("Adım süresi", $"{step.StepTime} dk");
                    if (step.GSoak > 0) AddItem("Gsoak", $"{step.GSoak} °C");
                    AddItem("Azot", $"{step.N2_Flow} L/h");
                    if (step.NH3_Flow > 0) AddItem("Amonyak", $"{step.NH3_Flow} L/h");
                    if (step.CO2_Perc > 0) AddItem("CO₂", $"%{step.CO2_Perc}");
                    if (step.Su_On != 0) AddItem("Su", "açık");
                    if (step.H2_Auto != 0) AddItem("H₂ seti", "%" + (step.H2_Set / 10.0).ToString("0.0", Tr));
                    AddItem("Fan", $"%{step.FanSpeed}");
                    break;

                case 3:
                    Set("StepTitle", $"AKTİF ADIM {step.StepNo} · SOĞUTMA");
                    AddItem("Bitiş sıcaklığı", $"{step.TargetTemp} °C");
                    AddItem("Azot", $"{step.N2_Flow} L/h");
                    AddItem("Fan", $"%{step.FanSpeed}");
                    break;

                default:
                    Set("StepTitle", $"AKTİF ADIM {step.StepNo}");
                    break;
            }

            Set("StepPanel", "On");
        }

        // Aktif adım paneline bir değer ekler.
        private void AddItem(string label, string value)
        {
            ActiveStepItems.Add(new StepItem { Label = label, Value = value });
        }

        // Program eğrisini zaman çizgisinden üretir; program yürüyorsa ilerleme çizgisini, şarj sürüyorsa saat zamanına göre ölçülen sıcaklık çizgisini ekler.
        private void RefreshChart()
        {
            if (_timeline == null)
            {
                if (ChartData != null)
                    ChartData = null;
                return;
            }

            var data = _timeline.ToSimpleChartData();

            if (ProgramRunning)
            {
                double nowX = _timeline.GetPositionX(_activeStep, _stepElapsed, _tempPota);
                if (nowX >= 0)
                    data.Progress = _timeline.GetProgressLine(nowX);
            }

            if (_batchId > 0 && _traceStart.HasValue)
            {
                var trace = BuildRealTrace(_traceStart.Value);
                data.Actual = trace;

                // Şarj plandan uzun sürdüyse grafik ölçülen çizgiyi içine alacak kadar genişler.
                if (trace.Count > 0 && trace[^1].X > data.XMax)
                    data.XMax = trace[^1].X * 1.03;
            }

            ChartData = data;
        }

        // Ölçülen sıcaklık kayıtlarını şarjın başlangıcından itibaren geçen saate göre dizer ve şu anki değerle bitirir.
        private List<Point> BuildRealTrace(DateTime start)
        {
            var points = new List<Point>();

            foreach (var sample in _trace)
            {
                double hours = (sample.Time - start).TotalHours;
                if (hours >= 0)
                    points.Add(new Point(hours, sample.Temp));
            }

            double nowHours = (DateTime.Now - start).TotalHours;
            if (nowHours >= 0 && (points.Count == 0 || nowHours >= points[^1].X))
                points.Add(new Point(nowHours, _tempPota));

            return points;
        }

        // Komut satırının sağında gösterilen açıklamayı üretir: duraklatma sebebi ya da izni olmayan istek.
        private static string BuildHint(ScadaStatus s)
        {
            if (s.SeqState == 70)
                return StatusTexts.DescribeHoldReason(s.HoldReason);

            if (s.HasInterlock(1)) return "Isıtma isteniyor ama izni yok";
            if (s.HasInterlock(0)) return "Vakum isteniyor ama izni yok";
            if (s.HasInterlock(2)) return "Soğutma fanı isteniyor ama izni yok";
            if (s.HasInterlock(3)) return "Sirkülasyon isteniyor ama izni yok";
            if (s.HasInterlock(4)) return "Kapak isteniyor ama izni yok";

            if (s.SeqState == 0 && !s.HasPermit(0)) return "Başlatma izni yok";
            if (s.SeqState is 60 or 80) return "Devam etmek için Alarm Reset verin";

            return string.Empty;
        }

        // Aktif alarm listesini tabloya yansıtır; en yeni alarmlar üstte, en çok üç satır.
        private void OnAlarmsReceived(List<ActiveAlarmDto> alarms)
        {
            Alarms.Clear();

            foreach (var alarm in alarms.OrderByDescending(a => a.TriggeredAt).Take(MaxAlarmRows))
            {
                Alarms.Add(new AlarmRow
                {
                    Time = alarm.TriggeredAt.ToString("HH:mm:ss"),
                    Tag = alarm.TagName,
                    Description = alarm.Description,
                    ClassText = alarm.PriorityLevel >= 3 ? "Ciddi" : alarm.PriorityLevel == 2 ? "Uyarı" : "Bilgi",
                    Level = alarm.PriorityLevel >= 3 ? "Bad" : alarm.PriorityLevel == 2 ? "Warn" : "Info"
                });
            }

            Set("AlarmSummary", alarms.Count == 0 ? "yok" : alarms.Count > MaxAlarmRows ? $"{alarms.Count} aktif, son {MaxAlarmRows} gösteriliyor" : $"{alarms.Count} aktif");
            Publish();
        }

        // Aktif alarm listesini sunucudan ister (hub listeyi yalnızca değişiklikte gönderir).
        private async Task LoadAlarmsAsync()
        {
            var result = await _api.GetAsync<List<ActiveAlarmDto>>("api/alarms/active");
            if (result.IsSuccess && result.Data != null)
                OnAlarmsReceived(result.Data);
        }

        // Gerekiyorsa onay alır, komutu PLC'ye gönderir ve sonucu komut satırında gösterir.
        private async Task SendAsync(ScadaCommandCode code, string name, string? confirmText)
        {
            if (confirmText != null)
            {
                var answer = MessageBox.Show(confirmText, name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var result = await _api.SendCommandAsync((int)code);

            Set("CmdResult", result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  {name} gönderildi"
                : $"{DateTime.Now:HH:mm:ss}  {name} başarısız: {result.Message}");
            Publish();
        }

        // Düğmelerin etkin/pasif durumunu yeniden hesaplatır.
        private void RefreshCommands()
        {
            StartCommand.NotifyCanExecuteChanged();
            HoldCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            VacuumOkCommand.NotifyCanExecuteChanged();
            SkipToCoolingCommand.NotifyCanExecuteChanged();
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

        // Cihaz durumunu şemadaki renk seviyesine çevirir.
        private static string Level(bool on, bool fault = false)
        {
            return fault ? "Fault" : on ? "On" : "Off";
        }

        // Vana durumunu metne çevirir.
        private static string OpenText(bool open)
        {
            return open ? "açık" : "kapalı";
        }

        // Motor durumunu metne çevirir.
        private static string RunText(bool running, bool fault)
        {
            return fault ? "arıza" : running ? "çalışıyor" : "duruyor";
        }

        // Sıcaklığı yazar; termokupl sağlam değilse çizgi koyar.
        private static string TempText(float value, bool valid)
        {
            return valid ? N0(value) : "—";
        }

        // Sayıyı ondalıksız yazar.
        private static string N0(float value)
        {
            return value.ToString("0", Tr);
        }

        // Sayıyı bir ondalıkla, virgülle yazar.
        private static string N1(float value)
        {
            return value.ToString("0.0", Tr);
        }
    }
}