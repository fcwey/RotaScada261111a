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

    // Çalışan reçetenin bir adımı: değerler ve programdaki durumu (bitti, çalışıyor, sırada)
    public class ProgramStepRow : ObservableObject
    {
        private readonly bool _userCanEdit;
        private bool _isPast;
        private bool _isActiveStep;
        private string _stateText = string.Empty;

        public RecipeStepRow Step { get; }

        public bool IsPast { get => _isPast; private set => SetProperty(ref _isPast, value); }
        public bool IsActiveStep { get => _isActiveStep; private set => SetProperty(ref _isActiveStep, value); }
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
            StateText = active ? $"çalışıyor · {elapsedMinutes} dk" : past ? "tamamlandı" : "sırada";

            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanChangeType));
            OnPropertyChanged(nameof(CanRemove));
        }
    }

    // Program sayfası: çalışan reçete, çalışırken değişiklik, grafikten adıma atlama ve şarj bilgisi
    public class ProgramViewModel : ObservableObject, IDisposable
    {
        private const int MaxSteps = 20;
        private const float MaxLoadKg = 100000f;
        private const float TimelineStartTemp = 20f;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private readonly bool _canEdit;
        private readonly bool _canOperate;

        private Recipe? _recipe;
        private RecipeTimeline? _timeline;
        private DateTime _lastDataAt = DateTime.MinValue;
        private bool _plcOk;
        private bool _isLive;
        private short _seqState;
        private int _activeStep;
        private int _stepElapsed;
        private float _tempPota;
        private int _batchId;
        private bool _loadingRecipe;
        private bool _syncingTarget;

        private bool _hasProgram;
        private bool _isDirty;
        private string _title = string.Empty;
        private string _message = string.Empty;
        private string _messageLevel = "Info";
        private ProfileChartData? _chartData;
        private double _targetX = -1;
        private string _targetText = string.Empty;
        private Option? _selectedGotoStep;
        private string _gotoMinuteText = "0";
        private string _batchCode = string.Empty;
        private string _loadKgText = string.Empty;
        private string _newNote = string.Empty;
        private string _infoMessage = string.Empty;

        public ObservableCollection<ProgramStepRow> Steps { get; } = new();
        public ObservableCollection<Option> GotoSteps { get; } = new();
        public ObservableCollection<NoteRow> Notes { get; } = new();

        public List<Option> StepTypes { get; } = new()
        {
            new Option(1, "Vakum"), new Option(2, "Isıtma"), new Option(3, "Soğutma")
        };

        public IAsyncRelayCommand ApplyCommand { get; }
        public IAsyncRelayCommand RevertCommand { get; }
        public IRelayCommand AddStepCommand { get; }
        public IRelayCommand<ProgramStepRow> RemoveStepCommand { get; }
        public IAsyncRelayCommand GotoCommand { get; }
        public IRelayCommand ClearTargetCommand { get; }
        public IAsyncRelayCommand SaveLoadCommand { get; }
        public IAsyncRelayCommand AddNoteCommand { get; }

        public bool HasProgram { get => _hasProgram; private set => SetProperty(ref _hasProgram, value); }
        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string MessageLevel { get => _messageLevel; private set => SetProperty(ref _messageLevel, value); }     // Info, Ok, Bad
        public ProfileChartData? ChartData { get => _chartData; private set => SetProperty(ref _chartData, value); }
        public string TargetText { get => _targetText; private set => SetProperty(ref _targetText, value); }
        public string BatchCode { get => _batchCode; private set => SetProperty(ref _batchCode, value); }
        public string InfoMessage { get => _infoMessage; private set => SetProperty(ref _infoMessage, value); }

        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (SetProperty(ref _isDirty, value))
                    RefreshCommands();
            }
        }

        // Adıma atlama yalnızca ısıtma ya da soğutmada, kaydedilmemiş değişiklik yokken yapılabilir.
        public bool CanDragTarget => _isLive && _canEdit && !IsDirty && _seqState is 30 or 50;

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

            ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => _isLive && _canEdit && IsDirty && ProgramRunning);
            RevertCommand = new AsyncRelayCommand(LoadRecipeAsync, () => IsDirty);
            AddStepCommand = new RelayCommand(AddStep, CanAddStep);
            RemoveStepCommand = new RelayCommand<ProgramStepRow>(RemoveStep);
            GotoCommand = new AsyncRelayCommand(GotoAsync, () => CanDragTarget && SelectedGotoStep != null);
            ClearTargetCommand = new RelayCommand(ClearTarget);
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

        // Canlı veriden sekans durumunu, çalışan adımı ve sıcaklığı izler; grafikteki yeri günceller, şarj değişince sayfayı yeniden yükler.
        private void OnDataReceived(ScadaLiveDto data)
        {
            _lastDataAt = DateTime.Now;
            _plcOk = data.IsConnected && data.Status != null;
            UpdateLive();

            if (!_plcOk)
                return;

            var status = data.Status!;
            bool seqChanged = status.SeqState != _seqState;

            _seqState = status.SeqState;
            _activeStep = status.ActiveStep;
            _stepElapsed = status.StepElapsedMin;
            _tempPota = status.TempPota;

            UpdateRowStates();
            RefreshChart();

            if (seqChanged)
                RefreshCommands();

            if (status.BatchId != _batchId)
            {
                _batchId = status.BatchId;
                _ = ReloadAllAsync();
            }
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

        // Şarj açıksa reçeteyi ve şarj bilgisini yükler; değilse sayfayı boşaltır.
        private async Task ReloadAllAsync()
        {
            if (_batchId <= 0)
            {
                HasProgram = false;
                _recipe = null;
                _timeline = null;
                Steps.Clear();
                GotoSteps.Clear();
                Notes.Clear();
                ChartData = null;
                SetTarget(-1);
                IsDirty = false;
                return;
            }

            HasProgram = true;
            await LoadRecipeAsync();
            await LoadInfoAsync();
        }

        // PLC'de çalışan reçeteyi (DB203) okuyup adım tablosuna yerleştirir; kaydedilmemiş değişiklikler atılır.
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

            Title = $"{_recipe.RecipeName} · {DescribeType(_recipe.RecipeType)} · basınç seti {_recipe.PressureSetPoint} mbar";
            IsDirty = false;
            UpdateRowStates();
            RebuildTimeline();
            RefreshCommands();
        }

        // Adımların bitti, çalışıyor, sırada durumlarını çalışan adıma göre günceller.
        private void UpdateRowStates()
        {
            foreach (var row in Steps)
                row.SetState(_activeStep, _stepElapsed, ProgramRunning);
        }

        // Adım tablosundan zaman çizgisini yeniden kurar, atlanabilecek adımları listeler ve seçili hedefi kaldırır.
        private void RebuildTimeline()
        {
            if (_recipe == null)
                return;

            _timeline = RecipeTimeline.Build(BuildRecipe(), TimelineStartTemp);

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

        // Grafiği zaman çizgisinden üretir; program yürüyorsa şu anki yeri ve ölçülen sıcaklığı işaretler.
        private void RefreshChart()
        {
            if (_timeline == null)
            {
                ChartData = null;
                return;
            }

            var data = _timeline.ToChartData();

            if (ProgramRunning)
            {
                double nowX = _timeline.GetPositionX(_activeStep, _stepElapsed, _tempPota);
                if (nowX >= 0)
                {
                    data.NowX = nowX;
                    data.Actual = new List<Point> { new Point(nowX, _tempPota) };
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
            {
                SetTarget(-1);
                return;
            }

            SetTarget(_timeline.GetGotoX(SelectedGotoStep.Value, minute));
        }

        // Seçili hedefi ve adım seçimini kaldırır.
        private void ClearTarget()
        {
            _syncingTarget = true;
            SelectedGotoStep = null;
            GotoMinuteText = "0";
            _syncingTarget = false;

            SetTarget(-1);
        }

        // Onay alıp değiştirilen reçeteyi PLC'ye gönderir; kabul edilirse çalışan reçeteyi yeniden okur.
        private async Task ApplyAsync()
        {
            if (_recipe == null)
                return;

            var answer = MessageBox.Show(
                "Değişiklik yürüyen programa uygulanacak.\n\nÇalışan adımın süresi kısaltıldıysa ve geçen süre yeni süreyi aştıysa adım hemen biter.\n\nUygulamak istiyor musunuz?",
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
            if (!_canEdit || !ProgramRunning || _recipe == null || Steps.Count == 0 || Steps.Count >= MaxSteps)
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

        // Onay alıp seçilen adımın verilen dakikasına atlar.
        private async Task GotoAsync()
        {
            if (SelectedGotoStep == null)
                return;

            if (!int.TryParse((GotoMinuteText ?? string.Empty).Trim(), out int minute) || minute < 0 || minute > short.MaxValue)
            {
                ShowMessage("Geçmiş sayılacak dakika 0 ya da daha büyük bir tam sayı olmalı.", "Bad");
                return;
            }

            var answer = MessageBox.Show(
                $"Program {SelectedGotoStep.Text} adımına atlayacak ve adımın {minute} dakikası geçmiş sayılacak.\n\nAtlamak istiyor musunuz?",
                "Adıma git", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.SendCommandAsync((int)ScadaCommandCode.GotoStep, SelectedGotoStep.Value, minute);

            if (result.IsSuccess)
                ClearTarget();

            ShowMessage(result.Message ?? (result.IsSuccess ? "Adıma atlandı." : "Adıma atlanamadı."), result.IsSuccess ? "Ok" : "Bad");
        }

        // Çalışan şarjın ağırlığını ve notlarını sunucudan alır.
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

            Notes.Clear();
            foreach (var note in result.Data.Notes.OrderByDescending(n => n.Timestamp))
                Notes.Add(new NoteRow { Header = $"{note.Timestamp:dd.MM.yyyy HH:mm} · {note.UserEmail}", Text = note.Text });

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

        // Reçetenin kaydedilmemiş değişikliği olduğunu işaretler ve eğriyi yeni plana göre çizer (yükleme sırasında çalışmaz).
        private void MarkDirty()
        {
            if (_loadingRecipe)
                return;

            IsDirty = true;
            RebuildTimeline();
            ShowMessage("Değişiklik henüz PLC'ye gönderilmedi; grafik yeni planı gösteriyor.", "Info");
        }

        // Adım tablosunun altındaki sonuç mesajını ve rengini ayarlar.
        private void ShowMessage(string text, string level)
        {
            Message = text;
            MessageLevel = level;
        }

        // Düğmelerin ve grafikteki sürüklemenin etkin/pasif durumunu yeniden hesaplatır.
        private void RefreshCommands()
        {
            ApplyCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
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
    }
}