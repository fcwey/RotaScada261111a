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
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace DesktopUI.ViewModels
{
    // Şarj notları listesindeki bir satır
    public class NoteRow
    {
        public string Header { get; set; } = string.Empty;   // Tarih, saat ve yazan
        public string Text { get; set; } = string.Empty;
    }

    // "Şimdi" kartındaki bir ölçüm: ad, ölçülen değer ve altında set değeri
    public class NowMetric : ObservableObject
    {
        private string _value = string.Empty;
        private string _sub = string.Empty;
        private bool _isWarn;

        public string Label { get; }
        public string Value { get => _value; set => SetProperty(ref _value, value); }
        public string Sub { get => _sub; set => SetProperty(ref _sub, value); }
        public bool IsWarn { get => _isWarn; set => SetProperty(ref _isWarn, value); }

        // Ölçümün adını alır.
        public NowMetric(string label)
        {
            Label = label;
        }
    }

    // İzleme tablosundaki bir adım satırı: reçete değerleri ve programdaki durumu
    public class ProgramLine : StepLine
    {
        public string StateText { get; set; } = string.Empty;
        public string StateLevel { get; set; } = "Wait";     // Done, Run, Wait, Skip, Target
        public bool IsActive { get; set; }
        public bool IsFaded { get; set; }                    // Biten ya da atlanacak adım soluk gösterilir
    }

    // Düzenleme tablosundaki bir adım: değerler, programdaki durumu ve değişip değişmediği
    public class ProgramStepRow : ObservableObject
    {
        private readonly bool _userCanEdit;
        private bool _isPast;
        private bool _isActiveStep;
        private bool _isChanged;
        private string _stateText = string.Empty;

        public RecipeStepRow Step { get; }

        public bool IsPast { get => _isPast; private set => SetProperty(ref _isPast, value); }
        public bool IsActiveStep { get => _isActiveStep; private set => SetProperty(ref _isActiveStep, value); }
        public bool IsChanged { get => _isChanged; set => SetProperty(ref _isChanged, value); }
        public string StateText { get => _stateText; private set => SetProperty(ref _stateText, value); }

        // Biten adımlar değiştirilemez; çalışan adımın tipi değişmez ve çalışan adım silinemez (PLC kuralları).
        public bool CanEdit => _userCanEdit && !IsPast;
        public bool CanChangeType => CanEdit && !IsActiveStep;
        public bool CanRemove => CanEdit && !IsActiveStep;

        // Adımın değerlerini ve kullanıcının düzenleme yetkisini alır.
        public ProgramStepRow(RecipeStepRow step, bool userCanEdit)
        {
            Step = step;
            _userCanEdit = userCanEdit;
        }

        // Adımın programdaki durumunu günceller; program bittiyse bütün adımlar kilitlenir.
        public void SetState(int activeStep, int elapsedMinutes, bool programRunning)
        {
            bool past = !programRunning || Step.StepNo < activeStep;
            bool active = programRunning && Step.StepNo == activeStep;

            IsPast = past;
            IsActiveStep = active;
            StateText = active ? $"çalışıyor · {elapsedMinutes} dk · tipi değiştirilemez" : past ? "tamamlandı · kilitli" : "sırada";

            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanChangeType));
            OnPropertyChanged(nameof(CanRemove));
        }
    }

    // Program sayfası: önce izleme görünümü açılır; adıma gitme ve çalışan reçeteyi değiştirme ayrı hallerdir
    public class ProgramViewModel : ObservableObject, IDisposable
    {
        private const int MaxSteps = 20;
        private const float MaxLoadKg = 100000f;
        private const float TimelineStartTemp = 20f;
        private const int TraceIntervalSeconds = 30;    // Canlı ölçümün çizgiye eklenme aralığı
        private const int TraceHistoryPoints = 600;     // Geçmişten okunan nokta sayısı
        private const string ModeWatch = "Watch";
        private const string ModeJump = "Jump";
        private const string ModeEdit = "Edit";
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private readonly bool _canEdit;
        private readonly bool _canOperate;
        private readonly List<TraceSample> _trace = new();
        private readonly Dictionary<int, (DateTime Start, int Minutes)> _stepTimes = new();

        private Recipe? _recipe;
        private RecipeTimeline? _timeline;
        private ScadaStatus? _status;
        private DateTime _lastDataAt = DateTime.MinValue;
        private DateTime _lastTraceAt = DateTime.MinValue;
        private DateTime? _batchStart;
        private bool _plcOk;
        private bool _isLive;
        private short _seqState;
        private int _activeStep;
        private int _stepElapsed;
        private float _tempPota;
        private int _batchId;
        private bool _loadingRecipe;
        private bool _syncingTarget;
        private string _lineSignature = string.Empty;

        private string _mode = ModeWatch;
        private bool _hasProgram;
        private bool _isDirty;
        private string _title = string.Empty;
        private string _typeText = string.Empty;
        private int _recipeType;
        private string _infoLine = string.Empty;
        private string _message = string.Empty;
        private string _messageLevel = "Info";
        private ProfileChartData? _chartData;
        private GasLaneData? _laneData;
        private double _targetX = -1;
        private string _targetText = string.Empty;
        private Option? _selectedGotoStep;
        private string _gotoMinuteText = "0";
        private string _jumpSummary = string.Empty;
        private string _jumpDetail = string.Empty;
        private string _changeSummary = string.Empty;
        private string _changeDetail = string.Empty;
        private string _nowTitle = string.Empty;
        private string _nowBadge = string.Empty;
        private double _stepProgress;
        private string _nextTitle = string.Empty;
        private string _nextMain = string.Empty;
        private string _nextSub = string.Empty;
        private string _batchCode = string.Empty;
        private string _loadKgText = string.Empty;
        private string _loadKgDisplay = "—";
        private string _newNote = string.Empty;
        private string _infoMessage = string.Empty;
        private bool _isNotesOpen;

        public ObservableCollection<ProgramStepRow> Steps { get; } = new();
        public ObservableCollection<ProgramLine> Lines { get; } = new();
        public ObservableCollection<NowMetric> NowItems { get; } = new();
        public ObservableCollection<Option> GotoSteps { get; } = new();
        public ObservableCollection<NoteRow> Notes { get; } = new();

        public List<Option> StepTypes { get; } = new()
        {
            new Option(1, "Vakum"), new Option(2, "Isıtma"), new Option(3, "Soğutma")
        };

        public IRelayCommand EnterJumpCommand { get; }
        public IRelayCommand EnterEditCommand { get; }
        public IAsyncRelayCommand CancelModeCommand { get; }
        public IAsyncRelayCommand ApplyCommand { get; }
        public IRelayCommand AddStepCommand { get; }
        public IRelayCommand<ProgramStepRow> RemoveStepCommand { get; }
        public IAsyncRelayCommand GotoCommand { get; }
        public IAsyncRelayCommand SaveLoadCommand { get; }
        public IAsyncRelayCommand AddNoteCommand { get; }

        public bool HasProgram { get => _hasProgram; private set => SetProperty(ref _hasProgram, value); }
        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string TypeText { get => _typeText; private set => SetProperty(ref _typeText, value); }
        public string InfoLine { get => _infoLine; private set => SetProperty(ref _infoLine, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string MessageLevel { get => _messageLevel; private set => SetProperty(ref _messageLevel, value); }     // Info, Ok, Bad
        public ProfileChartData? ChartData { get => _chartData; private set => SetProperty(ref _chartData, value); }
        public GasLaneData? LaneData { get => _laneData; private set => SetProperty(ref _laneData, value); }
        public string TargetText { get => _targetText; private set => SetProperty(ref _targetText, value); }
        public string JumpSummary { get => _jumpSummary; private set => SetProperty(ref _jumpSummary, value); }
        public string JumpDetail { get => _jumpDetail; private set => SetProperty(ref _jumpDetail, value); }
        public string ChangeSummary { get => _changeSummary; private set => SetProperty(ref _changeSummary, value); }
        public string ChangeDetail { get => _changeDetail; private set => SetProperty(ref _changeDetail, value); }
        public string NowTitle { get => _nowTitle; private set => SetProperty(ref _nowTitle, value); }
        public double StepProgress { get => _stepProgress; private set => SetProperty(ref _stepProgress, value); }
        public string NextTitle { get => _nextTitle; private set => SetProperty(ref _nextTitle, value); }
        public string NextMain { get => _nextMain; private set => SetProperty(ref _nextMain, value); }
        public string NextSub { get => _nextSub; private set => SetProperty(ref _nextSub, value); }
        public string BatchCode { get => _batchCode; private set => SetProperty(ref _batchCode, value); }
        public string LoadKgDisplay { get => _loadKgDisplay; private set => SetProperty(ref _loadKgDisplay, value); }
        public string InfoMessage { get => _infoMessage; private set => SetProperty(ref _infoMessage, value); }
        public string NotesLabel => $"Notlar ({Notes.Count})";

        // Reçete tipi; başlıktaki renk noktasını ve tablodaki sütunları belirler.
        public int RecipeType
        {
            get => _recipeType;
            private set
            {
                if (SetProperty(ref _recipeType, value))
                {
                    OnPropertyChanged(nameof(IsNitriding));
                    OnPropertyChanged(nameof(IsOxidation));
                }
            }
        }

        public bool IsNitriding => RecipeType == 2;
        public bool IsOxidation => RecipeType == 3;

        // Çalışan adımın bekleme sebebi; boşsa etiket gösterilmez.
        public string NowBadge
        {
            get => _nowBadge;
            private set
            {
                if (SetProperty(ref _nowBadge, value))
                    OnPropertyChanged(nameof(HasNowBadge));
            }
        }

        public bool HasNowBadge => _nowBadge.Length > 0;

        // Sayfanın hali: izleme, adıma gitme ya da reçeteyi değiştirme.
        public string Mode
        {
            get => _mode;
            private set
            {
                if (!SetProperty(ref _mode, value))
                    return;

                OnPropertyChanged(nameof(IsWatch));
                OnPropertyChanged(nameof(IsJump));
                OnPropertyChanged(nameof(IsEdit));
                OnPropertyChanged(nameof(IsReadTable));
                RefreshCommands();
                RebuildLines(true);
            }
        }

        public bool IsWatch => Mode == ModeWatch;
        public bool IsJump => Mode == ModeJump;
        public bool IsEdit => Mode == ModeEdit;
        public bool IsReadTable => Mode != ModeEdit;

        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (SetProperty(ref _isDirty, value))
                    RefreshCommands();
            }
        }

        // Şarj bilgisi ve notlar paneli açık mı.
        public bool IsNotesOpen { get => _isNotesOpen; set => SetProperty(ref _isNotesOpen, value); }

        // Adıma atlama yalnızca ısıtma ya da soğutmada, kaydedilmemiş değişiklik yokken yapılabilir.
        private bool CanJump => _isLive && _canEdit && !IsDirty && _seqState is 30 or 50;

        // Grafikte hedef yalnızca "adıma git" halinde sürüklenebilir.
        public bool CanDragTarget => IsJump && CanJump;

        // Grafikte sürüklenen hedef çizgisinin konumu; gidilebilecek bir yere çekilir, adım ve dakika kutularını günceller.
        public double TargetX
        {
            get => _targetX;
            set
            {
                if (_timeline == null || value < 0)
                {
                    SetTarget(-1);
                    return;
                }

                double snapped = _timeline.Snap(value);
                SetTarget(snapped);

                if (snapped < 0)
                    return;

                var located = _timeline.Locate(snapped);

                _syncingTarget = true;
                SelectedGotoStep = GotoSteps.FirstOrDefault(o => o.Value == located.StepNo);
                GotoMinuteText = located.Minute.ToString();
                _syncingTarget = false;

                UpdateJumpSummary();
            }
        }

        public Option? SelectedGotoStep
        {
            get => _selectedGotoStep;
            set
            {
                if (!SetProperty(ref _selectedGotoStep, value))
                    return;

                GotoCommand.NotifyCanExecuteChanged();
                UpdateTargetFromInputs();
            }
        }

        public string GotoMinuteText
        {
            get => _gotoMinuteText;
            set
            {
                if (SetProperty(ref _gotoMinuteText, value))
                    UpdateTargetFromInputs();
            }
        }

        public string LoadKgText { get => _loadKgText; set => SetProperty(ref _loadKgText, value); }

        public string NewNote
        {
            get => _newNote;
            set
            {
                if (SetProperty(ref _newNote, value))
                    AddNoteCommand.NotifyCanExecuteChanged();
            }
        }

        // Program yürürken (vakum, ısıtma, soğutma, duraklatılmış) reçete değiştirilebilir.
        private bool ProgramRunning => _seqState is 20 or 30 or 50 or 70;

        // Bağımlılıkları alır, komutları kurar ve canlı veri olaylarına abone olur.
        public ProgramViewModel(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _live = live;
            _settings = settings;
            _canEdit = session.HasRole(PlcPermissions.ProgramEdit);
            _canOperate = session.HasRole(PlcPermissions.Operate);

            EnterJumpCommand = new RelayCommand(EnterJump, () => IsWatch && CanJump);
            EnterEditCommand = new RelayCommand(EnterEdit, () => IsWatch && _isLive && _canEdit && ProgramRunning && _recipe != null);
            CancelModeCommand = new AsyncRelayCommand(CancelModeAsync, () => !IsWatch);
            ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => IsEdit && _isLive && _canEdit && IsDirty && ProgramRunning);
            AddStepCommand = new RelayCommand(AddStep, CanAddStep);
            RemoveStepCommand = new RelayCommand<ProgramStepRow>(RemoveStep);
            GotoCommand = new AsyncRelayCommand(GotoAsync, () => CanDragTarget && SelectedGotoStep != null);
            SaveLoadCommand = new AsyncRelayCommand(SaveLoadAsync, () => _canOperate && _batchId > 0);
            AddNoteCommand = new AsyncRelayCommand(AddNoteAsync, () => _canOperate && _batchId > 0 && !string.IsNullOrWhiteSpace(NewNote));

            _live.DataReceived += OnDataReceived;
            _live.ProfileChanged += OnProfileChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => UpdateLive();
            _timer.Start();
        }

        // Olay aboneliklerini bırakır ve zamanlayıcıyı durdurur.
        public void Dispose()
        {
            _timer.Stop();
            _live.DataReceived -= OnDataReceived;
            _live.ProfileChanged -= OnProfileChanged;
        }

        // Canlı veriden sekans durumunu, çalışan adımı ve ölçümleri izler; kartları, tabloyu ve grafiği günceller, şarj değişince sayfayı yeniden yükler.
        private void OnDataReceived(ScadaLiveDto data)
        {
            _lastDataAt = DateTime.Now;
            _plcOk = data.IsConnected && data.Status != null;
            UpdateLive();

            if (!_plcOk)
                return;

            var status = data.Status!;
            bool seqChanged = status.SeqState != _seqState;
            bool stepChanged = status.ActiveStep != _activeStep;

            _status = status;
            _seqState = status.SeqState;
            _activeStep = status.ActiveStep;
            _stepElapsed = status.StepElapsedMin;
            _tempPota = status.TempPota;

            if (status.BatchId != _batchId)
            {
                _batchId = status.BatchId;
                _ = ReloadAllAsync();
            }
            else if (stepChanged && _batchId > 0)
            {
                // Adım bitince gerçekleşen süresi sunucuda oluşur; durum sütunu için yeniden okunur.
                _ = LoadStepTimesAsync();
            }

            // Adıma gitme koşulu ortadan kalktıysa izleme görünümüne dönülür.
            if (IsJump && !CanJump)
                LeaveJump();

            AddTraceSample();
            UpdateRowStates();
            UpdateNow();
            UpdateInfoLine();
            RebuildLines(false);
            RefreshChart();

            if (seqChanged || stepChanged)
                RefreshCommands();
        }

        // Plan başka bir ekrandan değişince, düzenleme yapılmıyorsa çalışan reçeteyi yeniden okur.
        private void OnProfileChanged()
        {
            if (_batchId > 0 && !IsDirty)
                _ = LoadRecipeAsync();
        }

        // Veri taze ve PLC okunabiliyorsa komutlara izin verir.
        private void UpdateLive()
        {
            bool fresh = (DateTime.Now - _lastDataAt).TotalSeconds <= _settings.StaleSeconds;
            bool live = _live.IsConnected && fresh && _plcOk;

            if (live != _isLive)
            {
                _isLive = live;
                RefreshCommands();
            }
        }

        // Şarj açıksa reçeteyi, ölçüm geçmişini, adım sürelerini ve şarj bilgisini yükler; değilse sayfayı boşaltır.
        private async Task ReloadAllAsync()
        {
            _trace.Clear();
            _stepTimes.Clear();
            _batchStart = null;
            _lastTraceAt = DateTime.MinValue;

            if (_batchId <= 0)
            {
                HasProgram = false;
                _recipe = null;
                _timeline = null;
                Steps.Clear();
                Lines.Clear();
                GotoSteps.Clear();
                Notes.Clear();
                OnPropertyChanged(nameof(NotesLabel));
                ChartData = null;
                LaneData = null;
                SetTarget(-1);
                IsDirty = false;
                Mode = ModeWatch;
                return;
            }

            HasProgram = true;
            await LoadRecipeAsync();
            await LoadStepTimesAsync();
            await LoadTraceAsync();
            await LoadInfoAsync();
        }

        // PLC'de çalışan reçeteyi (DB203) okuyup tablolara yerleştirir; kaydedilmemiş değişiklikler atılır.
        private async Task LoadRecipeAsync()
        {
            var result = await _api.GetAsync<Recipe>("api/recipes/active");
            if (!result.IsSuccess || result.Data == null)
            {
                ShowMessage(result.Message ?? "Çalışan reçete okunamadı.", "Bad");
                return;
            }

            _recipe = result.Data;
            _loadingRecipe = true;

            Steps.Clear();
            foreach (var step in _recipe.Steps.OrderBy(s => s.StepNo))
                Steps.Add(new ProgramStepRow(RecipeStepRow.FromEntity(step, _recipe.RecipeType, MarkDirty), _canEdit));

            _loadingRecipe = false;

            Title = _recipe.RecipeName;
            TypeText = DescribeType(_recipe.RecipeType).ToUpper(Tr);
            RecipeType = _recipe.RecipeType;
            IsDirty = false;
            ChangeSummary = string.Empty;
            ChangeDetail = string.Empty;

            UpdateRowStates();
            UpdateInfoLine();
            RebuildTimeline();
            UpdateNow();
            RebuildLines(true);
            RefreshCommands();
        }

        // Biten adımların gerçekleşen sürelerini ve şarjın başlangıç anını sunucudan alır; adımların saatleri buradan hesaplanır.
        private async Task LoadStepTimesAsync()
        {
            int batchId = _batchId;
            if (batchId <= 0)
                return;

            var result = await _api.GetAsync<BatchDetailDto>($"api/batches/{batchId}");
            if (batchId != _batchId || !result.IsSuccess || result.Data == null)
                return;

            _batchStart = result.Data.Batch.StartTime;
            _stepTimes.Clear();

            DateTime cursor = result.Data.Batch.StartTime;
            foreach (var summary in result.Data.Steps.OrderBy(s => s.StepNo))
            {
                _stepTimes[summary.StepNo] = (cursor, summary.DurationMin);
                cursor = cursor.AddMinutes(summary.DurationMin);
            }

            UpdateInfoLine();
            RebuildLines(true);
        }

        // Şarjın başından bu yana ölçülen pota sıcaklığını adım numarasıyla birlikte sunucudan okur; sayfa açıkken biriken canlı kayıtlar sona eklenir.
        private async Task LoadTraceAsync()
        {
            int batchId = _batchId;
            if (batchId <= 0)
                return;

            var result = await _api.GetAsync<TrendDataDto>($"api/trends/batch/{batchId}?tags=TempPota,ActiveStep&points={TraceHistoryPoints}");
            if (batchId != _batchId || !result.IsSuccess || result.Data == null)
                return;

            var temps = result.Data.Series.FirstOrDefault(s => s.Tag == "TempPota");
            var steps = result.Data.Series.FirstOrDefault(s => s.Tag == "ActiveStep");
            if (temps == null || steps == null)
                return;

            int count = Math.Min(result.Data.Timestamps.Count, Math.Min(temps.Values.Count, steps.Values.Count));
            var history = new List<TraceSample>();

            for (int i = 0; i < count; i++)
            {
                history.Add(new TraceSample
                {
                    Time = result.Data.Timestamps[i],
                    StepNo = (int)Math.Round(steps.Values[i]),
                    Temp = temps.Values[i]
                });
            }

            DateTime lastTime = history.Count > 0 ? history[^1].Time : DateTime.MinValue;
            history.AddRange(_trace.Where(t => t.Time > lastTime));

            _trace.Clear();
            _trace.AddRange(history);
            RefreshChart();
        }

        // Program yürürken ölçülen sıcaklığı belirli aralıkla çizgiye ekler.
        private void AddTraceSample()
        {
            if (_batchId <= 0 || !ProgramRunning)
                return;

            DateTime now = DateTime.Now;
            if ((now - _lastTraceAt).TotalSeconds < TraceIntervalSeconds)
                return;

            _lastTraceAt = now;
            _trace.Add(new TraceSample { Time = now, StepNo = _activeStep, Temp = _tempPota });
        }

        // Düzenleme tablosundaki adımların bitti, çalışıyor, sırada durumlarını çalışan adıma göre günceller.
        private void UpdateRowStates()
        {
            foreach (var row in Steps)
                row.SetState(_activeStep, _stepElapsed, ProgramRunning);
        }

        // Başlığın altındaki satırı yazar: basınç seti, başlangıç saati ve PLC'nin tahmini bitişi.
        private void UpdateInfoLine()
        {
            if (_recipe == null)
            {
                InfoLine = string.Empty;
                return;
            }

            string text = $"basınç seti {_recipe.PressureSetPoint} mbar";

            if (_batchStart.HasValue)
                text += $" · başlangıç {_batchStart.Value:HH:mm}";

            if (ProgramRunning && _status != null && _status.PrgEndEstUnix > 0)
            {
                DateTime end = new DateTime(1970, 1, 1).AddSeconds(_status.PrgEndEstUnix);
                text += end.Date == DateTime.Today ? $" · tahmini bitiş {end:HH:mm}" : $" · tahmini bitiş {end:dd.MM HH:mm}";
            }

            InfoLine = text;
        }

        // "Şimdi" ve "Sıradaki" kartlarını yazar: çalışan adım, bekleme sebebi, ölçülen ve set değerler, bir sonraki adım.
        private void UpdateNow()
        {
            var s = _status;
            if (s == null || _recipe == null)
                return;

            var ordered = _recipe.Steps.OrderBy(x => x.StepNo).ToList();
            var step = ProgramRunning ? ordered.FirstOrDefault(x => x.StepNo == s.ActiveStep) : null;

            if (step == null)
            {
                NowTitle = StatusTexts.DescribeSeqMessage(s.SeqMessage);
                NowBadge = string.Empty;
                StepProgress = 0;
                NextTitle = string.Empty;
                NextMain = "Sırada adım yok";
                NextSub = string.Empty;

                SetMetrics(
                    ("Sıcaklık", $"{s.TempPota.ToString("0", Tr)} °C", $"set {s.TempSet.ToString("0", Tr)} °C", false),
                    ("Basınç", $"{s.Pressure.ToString("0.0", Tr)} mbar", $"set {s.PressureSet.ToString("0.0", Tr)} mbar", false));
                return;
            }

            double previousTemp = ordered.Where(x => x.StepNo < step.StepNo && x.StepType != 1).Select(x => (double)x.TargetTemp).DefaultIfEmpty(TimelineStartTemp).Last();

            NowTitle = $"Adım {step.StepNo} · {DescribeStepKind(step.StepType)} — {RecipeText.DescribeMain(step, previousTemp)}";
            NowBadge = DescribeWait(s);

            int total = s.StepElapsedMin + s.StepRemainMin;
            StepProgress = step.StepType == 2 && total > 0 ? 100.0 * s.StepElapsedMin / total : 0;

            var metrics = new List<(string Label, string Value, string Sub, bool Warn)>();

            bool outOfBand = step.StepType == 2 && step.GSoak > 0 && Math.Abs(s.TempPota - s.TempSet) > step.GSoak;
            string tempSub = $"set {s.TempSet.ToString("0", Tr)} °C" + (step.StepType == 2 && step.GSoak > 0 ? $" · bant ±{step.GSoak} °C" : string.Empty);
            metrics.Add(("Sıcaklık", $"{s.TempPota.ToString("0", Tr)} °C", tempSub, outOfBand));

            if (step.StepType == 2)
                metrics.Add(("Adım süresi", $"{s.StepElapsedMin} / {total} dk", s.StepElapsedMin == 0 ? "süre henüz başlamadı" : $"kalan {s.StepRemainMin} dk", false));
            else if (step.StepType == 1)
                metrics.Add(("Adım süresi", $"{s.StepElapsedMin} dk", $"zaman aşımı {step.Vac_Timeout} dk", false));
            else
                metrics.Add(("Adım süresi", $"{s.StepElapsedMin} dk", $"bitiş {step.TargetTemp} °C altında", false));

            metrics.Add(("Basınç", $"{s.Pressure.ToString("0.0", Tr)} mbar", $"set {s.PressureSet.ToString("0.0", Tr)} mbar", false));

            if (step.N2_Flow > 0 || s.N2FlowSet > 0)
                metrics.Add(("Azot", $"{s.N2FlowAct.ToString("0", Tr)} L/h", $"set {s.N2FlowSet.ToString("0", Tr)} L/h", false));

            if (step.NH3_Flow > 0 || s.NH3FlowSet > 0)
                metrics.Add(("Amonyak", $"{s.NH3FlowAct.ToString("0", Tr)} L/h", $"set {s.NH3FlowSet.ToString("0", Tr)} L/h", false));

            if (step.H2_Auto != 0)
                metrics.Add(("Hidrojen", $"%{s.H2Act.ToString("0.0", Tr)}", $"set %{s.H2Set.ToString("0.0", Tr)}", false));

            SetMetrics(metrics.ToArray());

            var next = ordered.FirstOrDefault(x => x.StepNo == step.StepNo + 1);
            if (next == null)
            {
                NextTitle = string.Empty;
                NextMain = "Bu son adım";
                NextSub = string.Empty;
                return;
            }

            NextTitle = $"Adım {next.StepNo} · {DescribeStepKind(next.StepType)}";
            NextMain = RecipeText.DescribeMain(next, step.StepType == 1 ? previousTemp : step.TargetTemp)
                       + (next.StepType == 2 ? $" · {next.StepTime} dk" : string.Empty);
            NextSub = next.StepType == 1 ? $"zaman aşımı {next.Vac_Timeout} dk" : RecipeText.DescribeGases(next);
        }

        // Çalışan adımın neden beklediğini söyler; bekleme yoksa boş döner.
        private static string DescribeWait(ScadaStatus s)
        {
            if (s.SeqState == 70)
                return StatusTexts.DescribeHoldReason(s.HoldReason);

            return s.SeqMessage switch
            {
                21 => "Gsoak bekleniyor: sıcaklık banda girince süre başlar",
                22 => "Amonyak izni bekleniyor",
                _ => string.Empty
            };
        }

        // "Şimdi" kartındaki ölçümleri eşitler; adlar aynıysa yalnızca değerleri değiştirir.
        private void SetMetrics(params (string Label, string Value, string Sub, bool Warn)[] items)
        {
            bool same = NowItems.Count == items.Length;
            for (int i = 0; same && i < items.Length; i++)
                same = NowItems[i].Label == items[i].Label;

            if (!same)
            {
                NowItems.Clear();
                foreach (var item in items)
                    NowItems.Add(new NowMetric(item.Label));
            }

            for (int i = 0; i < items.Length; i++)
            {
                NowItems[i].Value = items[i].Value;
                NowItems[i].Sub = items[i].Sub;
                NowItems[i].IsWarn = items[i].Warn;
            }
        }

        // İzleme tablosunun satırlarını üretir: adımın değerleri ve durumu. Görünen bir şey değişmediyse tabloya dokunmaz.
        private void RebuildLines(bool force)
        {
            if (_recipe == null)
                return;

            var located = IsJump && _timeline != null && _targetX >= 0 ? _timeline.Locate(_targetX) : (StepNo: 0, Minute: 0);
            string signature = $"{_activeStep}|{_stepElapsed}|{_seqState}|{Mode}|{located.StepNo}|{located.Minute}";

            if (!force && signature == _lineSignature)
                return;

            _lineSignature = signature;
            Lines.Clear();

            double temp = TimelineStartTemp;
            bool running = ProgramRunning;

            foreach (var step in _recipe.Steps.OrderBy(s => s.StepNo))
            {
                var line = new ProgramLine();
                RecipeText.Fill(line, step, _recipe.RecipeType, temp);

                if (step.StepType != 1)
                    temp = step.TargetTemp;

                if (!running || step.StepNo < _activeStep)
                {
                    line.StateLevel = "Done";
                    line.IsFaded = true;
                    line.StateText = _stepTimes.TryGetValue(step.StepNo, out var time)
                        ? $"✓ {time.Start:HH:mm} – {time.Start.AddMinutes(time.Minutes):HH:mm} · {time.Minutes} dk"
                        : running ? "✓ tamamlandı" : "—";
                }
                else if (step.StepNo == _activeStep)
                {
                    line.StateLevel = "Run";
                    line.IsActive = true;
                    line.StateText = step.StepType == 2
                        ? $"▶ çalışıyor · {_stepElapsed} / {step.StepTime} dk"
                        : $"▶ çalışıyor · {_stepElapsed} dk";
                }
                else
                {
                    line.StateLevel = "Wait";
                    line.StateText = "sırada";
                }

                // Adıma gitme seçiliyken atlanacak adımlar ve gidilecek adım işaretlenir.
                if (located.StepNo > 0 && step.StepNo == located.StepNo && step.StepNo != _activeStep)
                {
                    line.StateLevel = "Target";
                    line.IsFaded = false;
                    line.StateText = step.StepType == 2 ? $"➜ buraya gidilecek · {located.Minute}. dakika" : "➜ buraya gidilecek";
                }
                else if (located.StepNo > _activeStep && step.StepNo > _activeStep && step.StepNo < located.StepNo)
                {
                    line.StateLevel = "Skip";
                    line.IsFaded = true;
                    line.StateText = "atlanacak";
                }

                Lines.Add(line);
            }
        }

        // Adım tablosundan zaman çizgisini ve gaz şeritlerini yeniden kurar, atlanabilecek adımları listeler ve seçili hedefi kaldırır.
        private void RebuildTimeline()
        {
            if (_recipe == null)
                return;

            _timeline = RecipeTimeline.Build(BuildRecipe(), TimelineStartTemp);
            LaneData = RecipeText.BuildLanes(_timeline);

            _syncingTarget = true;
            GotoSteps.Clear();
            foreach (var row in Steps.Where(r => !r.Step.IsVacuum))
            {
                string kind = row.Step.IsHeating ? "Isıtma" : "Soğutma";
                GotoSteps.Add(new Option(row.Step.StepNo, $"Adım {row.Step.StepNo} · {kind} · {row.Step.TargetTemp} °C"));
            }
            SelectedGotoStep = null;
            _syncingTarget = false;

            SetTarget(-1);
            RefreshChart();
        }

        // Grafiği zaman çizgisinden üretir; program yürüyorsa geçilen kısmı, şu anki yeri ve ölçülen sıcaklık çizgisini ekler.
        private void RefreshChart()
        {
            if (_timeline == null)
            {
                ChartData = null;
                return;
            }

            var data = _timeline.ToChartData();

            // Adıma gitme halinde fare hedefi sürükler; üzerine gelince açılan adım bilgisi yalnızca diğer hallerde gösterilir.
            if (!IsJump)
            {
                foreach (var step in _timeline.Steps)
                    data.HoverZones.Add(new ProfileHoverZone { X1 = step.X1, X2 = step.X2, Text = RecipeTimeline.DescribeStep(step.Step) });
            }

            if (ProgramRunning)
            {
                double nowX = _timeline.GetPositionX(_activeStep, _stepElapsed, _tempPota);
                if (nowX >= 0)
                {
                    data.NowX = nowX;
                    data.Progress = _timeline.GetProgressLine(nowX);

                    var trace = _timeline.BuildTrace(_trace, nowX);
                    trace.Add(new Point(nowX, _tempPota));
                    data.Actual = trace;
                }
            }

            ChartData = data;
        }

        // Hedef çizgisinin konumunu ve yanındaki açıklamayı ayarlar; eksi değer hedefi kaldırır.
        private void SetTarget(double x)
        {
            _targetX = x;
            OnPropertyChanged(nameof(TargetX));
            TargetText = x < 0 || _timeline == null ? string.Empty : _timeline.Describe(x);
        }

        // Adım ya da dakika kutusu elle değişince hedef çizgisini oraya taşır.
        private void UpdateTargetFromInputs()
        {
            if (_syncingTarget || _timeline == null)
                return;

            if (SelectedGotoStep == null || !int.TryParse((GotoMinuteText ?? string.Empty).Trim(), out int minute) || minute < 0)
                SetTarget(-1);
            else
                SetTarget(_timeline.GetGotoX(SelectedGotoStep.Value, minute));

            UpdateJumpSummary();
        }

        // Seçilen hedefe gidilirse ne olacağını iki cümleyle yazar: hangi adımlar atlanır, set ve gazlar ne olur.
        private void UpdateJumpSummary()
        {
            RebuildLines(false);

            if (_timeline == null || _recipe == null || _targetX < 0)
            {
                JumpSummary = "Gidilecek adımı ve dakikayı seçin ya da grafikte gidilecek yere tıklayın.";
                JumpDetail = string.Empty;
                return;
            }

            var located = _timeline.Locate(_targetX);
            var target = _timeline.Steps.FirstOrDefault(s => s.Step.StepNo == located.StepNo);
            if (target == null)
                return;

            string where = target.Kind == 2 ? $"adım {located.StepNo}, {located.Minute}. dakikadan" : $"adım {located.StepNo} başından";

            if (located.StepNo > _activeStep)
            {
                int skipped = located.StepNo - _activeStep - 1;
                string between = skipped == 0 ? string.Empty
                    : skipped == 1 ? $", adım {_activeStep + 1} atlanacak"
                    : $", adım {_activeStep + 1}–{located.StepNo - 1} atlanacak";

                JumpSummary = $"Adım {_activeStep} yarıda bırakılacak{between}; program {where} sürecek.";
            }
            else if (located.StepNo == _activeStep)
            {
                JumpSummary = $"Çalışan adımın içinde yer değişecek; program {where} sürecek.";
            }
            else
            {
                JumpSummary = $"Geri dönülecek; program {where} yeniden yürüyecek ve aradaki adımlar tekrar çalışacak.";
            }

            double fraction = target.Kind == 2 && target.Step.StepTime > 0 ? located.Minute / (double)target.Step.StepTime : 0;
            double setTemp = target.Kind == 2 ? target.StartTemp + (target.EndTemp - target.StartTemp) * fraction : target.EndTemp;
            string setText = target.Kind == 2 ? $"Sıcaklık seti yaklaşık {setTemp:0} °C olacak" : $"Soğutma {target.Step.TargetTemp} °C altına kadar sürecek";

            JumpDetail = $"{setText} (ölçülen {_tempPota.ToString("0", Tr)} °C). O adımın değerleri: {RecipeText.DescribeGases(target.Step)}.";
        }

        // "Adıma git" haline geçer ve çalışan adımdan sonraki ilk adımı hedef olarak önerir.
        private void EnterJump()
        {
            Mode = ModeJump;

            var suggestion = GotoSteps.FirstOrDefault(o => o.Value > _activeStep) ?? GotoSteps.FirstOrDefault(o => o.Value == _activeStep);

            _syncingTarget = true;
            GotoMinuteText = "0";
            _syncingTarget = false;

            SelectedGotoStep = suggestion;
            UpdateTargetFromInputs();
            RefreshChart();
        }

        // Hedefi kaldırıp izleme görünümüne döner.
        private void LeaveJump()
        {
            _syncingTarget = true;
            SelectedGotoStep = null;
            GotoMinuteText = "0";
            _syncingTarget = false;

            SetTarget(-1);
            Mode = ModeWatch;
            RefreshChart();
        }

        // "Reçeteyi değiştir" haline geçer.
        private void EnterEdit()
        {
            ChangeSummary = "Henüz değişiklik yok";
            ChangeDetail = "Bir değeri değiştirince burada eski ve yeni hali listelenir.";
            Mode = ModeEdit;
        }

        // Vazgeç: adıma gitmede hedefi kaldırır, düzenlemede değişiklikleri atıp çalışan reçeteyi yeniden okur; izleme görünümüne döner.
        private async Task CancelModeAsync()
        {
            if (IsJump)
            {
                LeaveJump();
                return;
            }

            if (IsDirty)
                await LoadRecipeAsync();

            Mode = ModeWatch;
            ShowMessage(string.Empty, "Info");
        }

        // Onay alıp değiştirilen reçeteyi PLC'ye gönderir; kabul edilirse çalışan reçeteyi yeniden okur ve izleme görünümüne döner.
        private async Task ApplyAsync()
        {
            if (_recipe == null)
                return;

            var answer = MessageBox.Show(
                $"{ChangeDetail}\n\nDeğişiklik yürüyen programa uygulanacak. Çalışan adımın süresi kısaltıldıysa ve geçen süre yeni süreyi aştıysa adım hemen biter.\n\nUygulamak istiyor musunuz?",
                "Çalışan programı değiştir", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.PostAsync<ScadaCommandResult>("api/recipes/apply-edit", BuildRecipe());
            if (!result.IsSuccess)
            {
                ShowMessage(result.Message ?? "Değişiklik uygulanamadı.", "Bad");
                return;
            }

            await LoadRecipeAsync();
            Mode = ModeWatch;
            ShowMessage(result.Message ?? "Değişiklik uygulandı.", "Ok");
        }

        // Adım tablosundan PLC'ye gönderilecek reçeteyi üretir; başlık alanları çalışan reçetedeki gibi kalır.
        private Recipe BuildRecipe()
        {
            var source = _recipe!;

            return new Recipe
            {
                Id = source.Id,
                RecipeName = source.RecipeName,
                RecipeType = source.RecipeType,
                TotalStepCount = Steps.Count,
                PressureSetPoint = source.PressureSetPoint,
                PressureMode = source.PressureMode,
                PressureModeTime = source.PressureModeTime,
                PressureModeTemp = source.PressureModeTemp,
                GsoakTcId = source.GsoakTcId,
                CO2_Period = source.CO2_Period,
                Steps = Steps.Select(r => r.Step.ToEntity()).ToList()
            };
        }

        // Soğutma adımı henüz başlamadıysa ondan önce yeni bir ısıtma adımı eklenebilir.
        private bool CanAddStep()
        {
            if (!IsEdit || !_canEdit || !ProgramRunning || _recipe == null || Steps.Count == 0 || Steps.Count >= MaxSteps)
                return false;

            var last = Steps[^1];
            return !last.Step.IsCooling || last.Step.StepNo > _activeStep;
        }

        // Soğutma adımından önce (yoksa sona) fanı %100 olan yeni bir ısıtma adımı ekler.
        private void AddStep()
        {
            if (_recipe == null)
                return;

            var entity = new RecipeStep { StepType = 2, TargetTemp = 500, StepTime = 60, FanSpeed = RecipeStepRow.DefaultFanSpeed };
            var row = new ProgramStepRow(RecipeStepRow.FromEntity(entity, _recipe.RecipeType, MarkDirty), _canEdit);

            int index = Steps[^1].Step.IsCooling ? Steps.Count - 1 : Steps.Count;
            Steps.Insert(index, row);

            Renumber();
            MarkDirty();
        }

        // Henüz başlamamış bir adımı siler.
        private void RemoveStep(ProgramStepRow? row)
        {
            if (row == null || !row.CanRemove || Steps.Count <= 1)
                return;

            Steps.Remove(row);
            Renumber();
            MarkDirty();
        }

        // Adımları sırayla 1'den numaralar ve durumlarını yeniler.
        private void Renumber()
        {
            for (int i = 0; i < Steps.Count; i++)
                Steps[i].Step.StepNo = i + 1;

            UpdateRowStates();
        }

        // Onay alıp seçilen adımın verilen dakikasına atlar; başarılıysa izleme görünümüne döner.
        private async Task GotoAsync()
        {
            if (SelectedGotoStep == null)
                return;

            if (!int.TryParse((GotoMinuteText ?? string.Empty).Trim(), out int minute) || minute < 0 || minute > short.MaxValue)
            {
                ShowMessage("Dakika 0 ya da daha büyük bir tam sayı olmalı.", "Bad");
                return;
            }

            var answer = MessageBox.Show(
                $"{JumpSummary}\n\n{JumpDetail}\n\nAtlamak istiyor musunuz?",
                "Adıma git", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.GotoStep, SelectedGotoStep.Value, minute);

            if (result.IsSuccess)
                LeaveJump();

            ShowMessage(result.Message ?? (result.IsSuccess ? "Adıma atlandı." : "Adıma atlanamadı."), result.IsSuccess ? "Ok" : "Bad");
        }

        // Çalışan şarjın kodunu, ağırlığını ve notlarını sunucudan alır.
        private async Task LoadInfoAsync()
        {
            int batchId = _batchId;
            if (batchId <= 0)
                return;

            var result = await _api.GetAsync<BatchInfoDto>($"api/batches/{batchId}/info");
            if (batchId != _batchId)
                return;

            if (!result.IsSuccess || result.Data == null)
            {
                InfoMessage = result.Message ?? "Şarj bilgisi alınamadı.";
                return;
            }

            BatchCode = result.Data.BatchCode;
            LoadKgText = result.Data.LoadKg.HasValue ? result.Data.LoadKg.Value.ToString("0.#", Tr) : string.Empty;
            LoadKgDisplay = result.Data.LoadKg.HasValue ? $"{result.Data.LoadKg.Value.ToString("0.#", Tr)} kg" : "girilmedi";

            Notes.Clear();
            foreach (var note in result.Data.Notes.OrderByDescending(n => n.Timestamp))
                Notes.Add(new NoteRow { Header = $"{note.Timestamp:dd.MM.yyyy HH:mm} · {note.UserEmail}", Text = note.Text });

            OnPropertyChanged(nameof(NotesLabel));
            InfoMessage = string.Empty;
            RefreshCommands();
        }

        // Girilen şarj ağırlığını denetleyip sunucuya yazar.
        private async Task SaveLoadAsync()
        {
            string text = (LoadKgText ?? string.Empty).Trim().Replace('.', ',');

            if (!float.TryParse(text, NumberStyles.Float, Tr, out float kg) || kg <= 0 || kg > MaxLoadKg)
            {
                InfoMessage = $"Şarj ağırlığı 0'dan büyük, en fazla {MaxLoadKg:0} kg olan bir sayı olmalı.";
                return;
            }

            var result = await _api.PutAsync<JsonElement>($"api/batches/{_batchId}/load?kg={kg.ToString(CultureInfo.InvariantCulture)}");

            if (result.IsSuccess)
                LoadKgDisplay = $"{kg.ToString("0.#", Tr)} kg";

            InfoMessage = result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  Ağırlık kaydedildi."
                : result.Message ?? "Ağırlık kaydedilemedi.";
        }

        // Yazılan notu şarja ekler ve not listesini yeniler.
        private async Task AddNoteAsync()
        {
            var result = await _api.PostAsync<BatchNoteDto>($"api/batches/{_batchId}/notes", new BatchNoteRequestDto { Text = NewNote });
            if (!result.IsSuccess)
            {
                InfoMessage = result.Message ?? "Not eklenemedi.";
                return;
            }

            NewNote = string.Empty;
            await LoadInfoAsync();
        }

        // Reçetenin gönderilmemiş değişikliği olduğunu işaretler, eğriyi yeni plana göre çizer ve değişiklik listesini yazar (yükleme sırasında çalışmaz).
        private void MarkDirty()
        {
            if (_loadingRecipe)
                return;

            IsDirty = true;
            RebuildTimeline();
            UpdateChanges();
            ShowMessage(string.Empty, "Info");
        }

        // Düzenlenen adımları çalışan reçeteyle karşılaştırır; değişen adımları işaretler ve "eski → yeni" listesini yazar.
        private void UpdateChanges()
        {
            if (_recipe == null)
                return;

            var original = _recipe.Steps.OrderBy(s => s.StepNo).ToList();
            var current = Steps.Select(r => r.Step.ToEntity()).ToList();
            var changes = new List<string>();

            foreach (var row in Steps)
                row.IsChanged = false;

            if (original.Count != current.Count)
            {
                changes.Add($"adım sayısı {original.Count} → {current.Count}");
            }
            else
            {
                for (int i = 0; i < current.Count; i++)
                {
                    var before = original[i];
                    var after = current[i];
                    var parts = new List<string>();

                    if (before.StepType != after.StepType) parts.Add($"tip {DescribeStepKind(before.StepType)} → {DescribeStepKind(after.StepType)}");
                    if (before.Vac_SetPoint != after.Vac_SetPoint) parts.Add($"vakum seti {before.Vac_SetPoint} → {after.Vac_SetPoint} mbar");
                    if (before.Vac_Timeout != after.Vac_Timeout) parts.Add($"zaman aşımı {before.Vac_Timeout} → {after.Vac_Timeout} dk");
                    if (before.TargetTemp != after.TargetTemp) parts.Add($"hedef {before.TargetTemp} → {after.TargetTemp} °C");
                    if (before.StepTime != after.StepTime) parts.Add($"süre {before.StepTime} → {after.StepTime} dk");
                    if (before.GSoak != after.GSoak) parts.Add($"Gsoak {before.GSoak} → {after.GSoak} °C");
                    if (before.N2_Flow != after.N2_Flow) parts.Add($"azot {before.N2_Flow} → {after.N2_Flow} L/h");
                    if (before.NH3_Flow != after.NH3_Flow) parts.Add($"amonyak {before.NH3_Flow} → {after.NH3_Flow} L/h");
                    if (before.CO2_Perc != after.CO2_Perc) parts.Add($"CO₂ %{before.CO2_Perc} → %{after.CO2_Perc}");
                    if (before.Su_On != after.Su_On) parts.Add(after.Su_On != 0 ? "su açıldı" : "su kapatıldı");
                    if (before.H2_Auto != after.H2_Auto) parts.Add(after.H2_Auto != 0 ? "H₂ otomatik açıldı" : "H₂ otomatik kapatıldı");
                    if (before.H2_Set != after.H2_Set) parts.Add($"H₂ seti {RecipeText.FormatH2(before.H2_Set)} → {RecipeText.FormatH2(after.H2_Set)}");
                    if (before.FanSpeed != after.FanSpeed) parts.Add($"fan %{before.FanSpeed} → %{after.FanSpeed}");

                    if (parts.Count == 0)
                        continue;

                    Steps[i].IsChanged = true;
                    changes.Add($"Adım {after.StepNo}: {string.Join(", ", parts)}");
                }
            }

            int beforeMinutes = original.Where(s => s.StepType == 2).Sum(s => s.StepTime);
            int afterMinutes = current.Where(s => s.StepType == 2).Sum(s => s.StepTime);
            if (beforeMinutes != afterMinutes)
                changes.Add($"ısıtma süresi toplamı {beforeMinutes} → {afterMinutes} dk");

            if (changes.Count == 0)
            {
                ChangeSummary = "Değişiklik kalmadı";
                ChangeDetail = "Değerler çalışan reçeteyle aynı.";
                return;
            }

            ChangeSummary = "Uygulanacak değişiklikler";
            ChangeDetail = string.Join("   ·   ", changes);
        }

        // Sonuç mesajını ve rengini ayarlar.
        private void ShowMessage(string text, string level)
        {
            Message = text;
            MessageLevel = level;
        }

        // Düğmelerin ve grafikteki sürüklemenin etkin/pasif durumunu yeniden hesaplatır.
        private void RefreshCommands()
        {
            EnterJumpCommand.NotifyCanExecuteChanged();
            EnterEditCommand.NotifyCanExecuteChanged();
            CancelModeCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
            AddStepCommand.NotifyCanExecuteChanged();
            GotoCommand.NotifyCanExecuteChanged();
            SaveLoadCommand.NotifyCanExecuteChanged();
            AddNoteCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanDragTarget));
        }

        // Reçete tipini metne çevirir.
        private static string DescribeType(int recipeType)
        {
            return recipeType switch
            {
                1 => "Meneviş",
                2 => "Nitrasyon",
                3 => "Oksidasyon",
                _ => "Tip bilinmiyor"
            };
        }

        // Adım tipini metne çevirir.
        private static string DescribeStepKind(int stepType)
        {
            return stepType switch
            {
                1 => "Vakum",
                2 => "Isıtma",
                3 => "Soğutma",
                _ => "Adım"
            };
        }
    }
}