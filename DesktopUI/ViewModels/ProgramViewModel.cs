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

    // Program sayfası: çalışan reçete, çalışırken değişiklik, adıma atlama, grafik ve şarj bilgisi
    public class ProgramViewModel : ObservableObject, IDisposable
    {
        private const int MaxSteps = 20;
        private const int ChartRefreshSeconds = 60;
        private const float MaxLoadKg = 100000f;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private readonly bool _canEdit;
        private readonly bool _canOperate;

        private Recipe? _recipe;
        private DateTime _lastDataAt = DateTime.MinValue;
        private bool _plcOk;
        private bool _isLive;
        private short _seqState;
        private int _activeStep;
        private int _stepElapsed;
        private int _batchId;
        private int _chartAge;
        private bool _loadingRecipe;

        private bool _hasProgram;
        private bool _isDirty;
        private string _title = string.Empty;
        private string _message = string.Empty;
        private string _messageLevel = "Info";
        private string _chartNote = string.Empty;
        private ProfileChartData? _chartData;
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
        public IAsyncRelayCommand SaveLoadCommand { get; }
        public IAsyncRelayCommand AddNoteCommand { get; }

        public bool HasProgram { get => _hasProgram; private set => SetProperty(ref _hasProgram, value); }
        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string MessageLevel { get => _messageLevel; private set => SetProperty(ref _messageLevel, value); }     // Info, Ok, Bad
        public string ChartNote { get => _chartNote; private set => SetProperty(ref _chartNote, value); }
        public ProfileChartData? ChartData { get => _chartData; private set => SetProperty(ref _chartData, value); }
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

        public Option? SelectedGotoStep
        {
            get => _selectedGotoStep;
            set
            {
                if (SetProperty(ref _selectedGotoStep, value))
                    GotoCommand.NotifyCanExecuteChanged();
            }
        }

        public string GotoMinuteText { get => _gotoMinuteText; set => SetProperty(ref _gotoMinuteText, value); }
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
            GotoCommand = new AsyncRelayCommand(GotoAsync, () => _isLive && _canEdit && !IsDirty && SelectedGotoStep != null && _seqState is 30 or 50);
            SaveLoadCommand = new AsyncRelayCommand(SaveLoadAsync, () => _canOperate && _batchId > 0);
            AddNoteCommand = new AsyncRelayCommand(AddNoteAsync, () => _canOperate && _batchId > 0 && !string.IsNullOrWhiteSpace(NewNote));

            _live.DataReceived += OnDataReceived;
            _live.ProfileChanged += OnProfileChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }

        // Olay aboneliklerini bırakır ve zamanlayıcıyı durdurur.
        public void Dispose()
        {
            _timer.Stop();
            _live.DataReceived -= OnDataReceived;
            _live.ProfileChanged -= OnProfileChanged;
        }

        // Canlı veriden sekans durumunu, çalışan adımı ve şarj numarasını izler; şarj değişince sayfayı yeniden yükler.
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

            UpdateRowStates();

            if (seqChanged)
                RefreshCommands();

            if (status.BatchId != _batchId)
            {
                _batchId = status.BatchId;
                _ = ReloadAllAsync();
            }
        }

        // Her saniye verinin tazeliğini denetler, belirli aralıkla grafiği yeniler.
        private void OnTick()
        {
            UpdateLive();

            _chartAge++;
            if (_chartAge >= ChartRefreshSeconds && _batchId > 0)
                _ = LoadChartAsync();
        }

        // Plan değişince (değişiklik, adıma atlama, soğutmaya geçiş) grafiği ve düzenlenmiyorsa reçeteyi yeniler.
        private void OnProfileChanged()
        {
            if (_batchId <= 0)
                return;

            _ = LoadChartAsync();

            if (!IsDirty)
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

        // Şarj açıksa reçeteyi, grafiği ve şarj bilgisini yükler; değilse sayfayı boşaltır.
        private async Task ReloadAllAsync()
        {
            if (_batchId <= 0)
            {
                HasProgram = false;
                _recipe = null;
                Steps.Clear();
                GotoSteps.Clear();
                Notes.Clear();
                ChartData = null;
                IsDirty = false;
                return;
            }

            HasProgram = true;
            await LoadRecipeAsync();
            await LoadChartAsync();
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
            RebuildGotoSteps();
            RefreshCommands();
        }

        // Adımların bitti, çalışıyor, sırada durumlarını çalışan adıma göre günceller.
        private void UpdateRowStates()
        {
            foreach (var row in Steps)
                row.SetState(_activeStep, _stepElapsed, ProgramRunning);
        }

        // Atlanabilecek adımları (ısıtma ve soğutma) açılır listeye koyar.
        private void RebuildGotoSteps()
        {
            int? selected = SelectedGotoStep?.Value;

            GotoSteps.Clear();
            foreach (var row in Steps.Where(r => !r.Step.IsVacuum))
            {
                string kind = row.Step.IsHeating ? "Isıtma" : "Soğutma";
                GotoSteps.Add(new Option(row.Step.StepNo, $"Adım {row.Step.StepNo} · {kind} · {row.Step.TargetTemp} °C"));
            }

            SelectedGotoStep = GotoSteps.FirstOrDefault(o => o.Value == selected);
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
            await LoadChartAsync();
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
            ShowMessage(result.Message ?? (result.IsSuccess ? "Adıma atlandı." : "Adıma atlanamadı."), result.IsSuccess ? "Ok" : "Bad");
        }

        // Çalışan şarjın teorik eğrisini adım bölgeleriyle ve gerçekleşen pota sıcaklığını alıp grafiğe çevirir.
        private async Task LoadChartAsync()
        {
            _chartAge = 0;
            int batchId = _batchId;
            if (batchId <= 0)
                return;

            var profileTask = _api.GetAsync<RecipeProfileDto>("api/profiles/active");
            var trendTask = _api.GetAsync<TrendDataDto>($"api/trends/batch/{batchId}?tags=TempPota&points=600");
            await Task.WhenAll(profileTask, trendTask);

            if (batchId != _batchId)
                return;

            var profile = profileTask.Result.IsSuccess ? profileTask.Result.Data : null;
            var trend = trendTask.Result.IsSuccess ? trendTask.Result.Data : null;
            DateTime now = DateTime.Now;

            // Teorik noktaların saatleri: ısıtma başlamadıysa eğri şu andan başlıyormuş gibi yerleştirilir.
            var theory = new List<(int Segment, DateTime Time, float Temp, int StepNo)>();
            if (profile != null)
            {
                DateTime baseTime = profile.HeatingStart ?? now;
                foreach (var point in profile.Points)
                    theory.Add((point.Segment, point.Timestamp ?? baseTime.AddMinutes(point.Minute), point.Temp, point.StepNo));
            }

            var actual = new List<(DateTime Time, float Temp)>();
            var series = trend?.Series.FirstOrDefault();
            if (trend != null && series != null)
            {
                int count = Math.Min(trend.Timestamps.Count, series.Values.Count);
                for (int i = 0; i < count; i++)
                    actual.Add((trend.Timestamps[i], series.Values[i]));
            }

            var allTimes = theory.Select(t => t.Time).Concat(actual.Select(a => a.Time)).ToList();
            if (allTimes.Count == 0)
            {
                ChartData = null;
                ChartNote = "Henüz ölçüm ve teorik eğri yok.";
                return;
            }

            DateTime origin = allTimes.Min();
            double Hours(DateTime time) => (time - origin).TotalHours;

            var data = new ProfileChartData
            {
                Actual = actual.Select(a => new Point(Hours(a.Time), a.Temp)).ToList(),
                NowX = Hours(now)
            };

            // Her eğri parçasında ardışık iki nokta bir adımdır: bölge, numara ve bitiş noktasının sıcaklığı.
            foreach (var segment in theory.GroupBy(t => t.Segment))
            {
                var points = segment.ToList();
                data.Theory.Add(points.Select(p => new Point(Hours(p.Time), p.Temp)).ToList());

                for (int i = 0; i < points.Count; i++)
                {
                    data.Markers.Add(new ProfileMarker { X = Hours(points[i].Time), Y = points[i].Temp, Label = $"{points[i].Temp:0} °C" });

                    if (i > 0)
                    {
                        data.Bands.Add(new ProfileBand
                        {
                            X1 = Hours(points[i - 1].Time),
                            X2 = Hours(points[i].Time),
                            Label = points[i].StepNo.ToString(),
                            Kind = 2
                        });
                    }
                }
            }

            double maxTemp = theory.Select(t => t.Temp).Concat(actual.Select(a => a.Temp)).DefaultIfEmpty(100).Max();
            data.XMax = Math.Max(1, Math.Ceiling(Math.Max(Hours(allTimes.Max()), data.NowX)));
            data.YMax = Math.Max(150, Math.Ceiling(maxTemp * 1.15 / 150.0) * 150);

            ChartData = data;
            ChartNote = profile == null
                ? "Teorik eğri alınamadı."
                : profile.IsAnchored
                    ? $"Saat ekseni şarjın ilk ölçümünden başlar. Planlanan ısıtma bitişi: {profile.PlannedEnd:dd.MM HH:mm}."
                    : "Isıtma henüz başlamadı; teorik eğri şu andan başlıyormuş gibi çizildi.";
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

        // Reçetenin kaydedilmemiş değişikliği olduğunu işaretler (yükleme sırasında işaretlemez).
        private void MarkDirty()
        {
            if (_loadingRecipe)
                return;

            IsDirty = true;
            ShowMessage("Değişiklik henüz PLC'ye gönderilmedi.", "Info");
        }

        // Adım tablosunun altındaki sonuç mesajını ve rengini ayarlar.
        private void ShowMessage(string text, string level)
        {
            Message = text;
            MessageLevel = level;
        }

        // Düğmelerin etkin/pasif durumunu yeniden hesaplatır.
        private void RefreshCommands()
        {
            ApplyCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
            AddStepCommand.NotifyCanExecuteChanged();
            GotoCommand.NotifyCanExecuteChanged();
            SaveLoadCommand.NotifyCanExecuteChanged();
            AddNoteCommand.NotifyCanExecuteChanged();
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