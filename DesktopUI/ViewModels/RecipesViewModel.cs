using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Controls;
using DesktopUI.Helpers;
using DesktopUI.Services;
using DesktopUI.Views;
using Entities.Concrete;
using Entities.Dtos;
using Entities.Structures;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace DesktopUI.ViewModels
{
    // Açılır listelerde kullanılan sayı-metin çifti
    public class Option
    {
        public int Value { get; }
        public string Text { get; }

        // Değeri ve görünen metni alır.
        public Option(int value, string text)
        {
            Value = value;
            Text = text;
        }

        // Açılır listenin seçili kutusunda görünen metni döner.
        public override string ToString()
        {
            return Text;
        }
    }

    // Reçete listesindeki bir satır
    public class RecipeListItem
    {
        public int Id { get; set; }
        public int RecipeType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Info { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;   // Menevişler, Nitrasyonlar, Oksidasyonlar
        public bool IsPinned { get; set; }
        public int MaxTemp { get; set; }                        // Reçetedeki en yüksek ısıtma sıcaklığı
        public string TempText { get; set; } = string.Empty;
        public bool ShowTemp { get; set; }                      // Sıcaklık etiketi yalnızca sıcaklığa göre sıralamada görünür
        public string PinTip => IsPinned ? "Sabitlemeyi kaldır" : "Grubun başına sabitle";
    }

    // Okuma görünümündeki bir adım satırı: adım cümle olarak yazılır, ek değerler küçük etiketlerde durur
    public class StepLine
    {
        public int StepNo { get; set; }
        public int StepType { get; set; }
        public string KindText { get; set; } = string.Empty;
        public string Main { get; set; } = string.Empty;
        public List<string> Chips { get; set; } = new();
        public string ErrorText { get; set; } = string.Empty;
        public bool HasError => ErrorText.Length > 0;
    }

    // Reçeteler sayfası: liste, salt okunur reçete görünümü, düzenleme tablosu, PLC'de kontrol ve başlatma
    public class RecipesViewModel : ObservableObject, IDisposable
    {
        private const int MaxSteps = 20;
        private const int NameMaxLength = 30;
        private const float PreviewStartTemp = 20f;
        private const int SortByName = 0;
        private const int SortByTemp = 1;

        private readonly ApiClient _api;
        private readonly bool _canEdit;
        private readonly bool _canOperate;
        private readonly List<RecipeListItem> _allRecipes = new();

        private bool _loading;
        private int _editId;
        private bool _hasEditor;
        private bool _isDirty;
        private bool _isEditing;
        private RecipeStepRow? _selectedStep;
        private int _plcErrorStep = -1;                 // PLC'nin hata bulduğu adım; 0 reçetenin bütünü, -1 hata yok
        private string _plcErrorText = string.Empty;
        private string _errorSummary = string.Empty;
        private string _generalError = string.Empty;
        private GasLaneData? _laneData;
        private RecipeListItem? _selectedRecipe;
        private string _searchText = string.Empty;
        private int _typeFilter;
        private int _sortMode;
        private string _name = string.Empty;
        private int _recipeType = 2;
        private int _pressureSetPoint;
        private int _pressureMode;
        private int _pressureModeTime;
        private int _pressureModeTemp;
        private int _gsoakTcId;
        private int _co2Period;
        private string _message = string.Empty;
        private string _messageLevel = "Info";
        private string _profileNote = string.Empty;
        private ProfileChartData? _chartData;

        // Başka bir sayfaya geçilmesi istendiğinde sayfanın anahtarıyla tetiklenir.
        public event Action<string>? NavigateRequested;

        public ObservableCollection<RecipeListItem> Recipes { get; } = new();
        public ObservableCollection<RecipeStepRow> Steps { get; } = new();
        public ObservableCollection<StepLine> ReadLines { get; } = new();
        public ObservableCollection<StatusCell> MetaItems { get; } = new();
        public ObservableCollection<StatusCell> SummaryItems { get; } = new();

        public List<Option> TypeFilters { get; } = new()
        {
            new Option(0, "Bütün tipler"), new Option(1, "Meneviş"), new Option(2, "Nitrasyon"), new Option(3, "Oksidasyon")
        };

        public List<Option> SortModes { get; } = new()
        {
            new Option(SortByName, "Ada göre sırala"), new Option(SortByTemp, "Sıcaklığa göre sırala")
        };

        public List<Option> RecipeTypes { get; } = new()
        {
            new Option(1, "Meneviş"), new Option(2, "Nitrasyon"), new Option(3, "Oksidasyon")
        };

        public List<Option> StepTypes { get; } = new()
        {
            new Option(1, "Vakum"), new Option(2, "Isıtma"), new Option(3, "Soğutma")
        };

        public List<Option> PressureModes { get; } = new()
        {
            new Option(0, "Sürekli temizle"), new Option(10, "Atmosferi koru"), new Option(20, "Sıcaklığa bağlı")
        };

        public List<Option> GsoakSensors { get; } = new()
        {
            new Option(0, "Pota"), new Option(1, "Malzeme"), new Option(2, "Opsiyonel 1"), new Option(3, "Opsiyonel 2")
        };

        public IRelayCommand NewCommand { get; }
        public IRelayCommand CopyCommand { get; }
        public IAsyncRelayCommand DeleteCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand RevertCommand { get; }
        public IRelayCommand EditCommand { get; }
        public IRelayCommand<RecipeStepRow> DuplicateStepCommand { get; }
        public IRelayCommand AddStepCommand { get; }
        public IRelayCommand<RecipeStepRow> RemoveStepCommand { get; }
        public IRelayCommand<RecipeStepRow> MoveUpCommand { get; }
        public IRelayCommand<RecipeStepRow> MoveDownCommand { get; }
        public IAsyncRelayCommand<RecipeListItem> TogglePinCommand { get; }
        public IAsyncRelayCommand ValidateCommand { get; }
        public IAsyncRelayCommand StartCommand { get; }

        public bool CanEdit => _canEdit;
        public bool IsListEnabled => !IsDirty;
        public string EditorTitle => !HasEditor ? string.Empty : _editId == 0 ? "YENİ REÇETE" : $"REÇETE NO {_editId} · DÜZENLEME";
        public string NoText => _editId == 0 ? "kaydedilmedi" : $"no {_editId}";
        public string TypeText => RecipeTypes.FirstOrDefault(t => t.Value == RecipeType)?.Text.ToUpper(new System.Globalization.CultureInfo("tr-TR")) ?? string.Empty;

        // Reçete açık ve düzenleme kapalıyken salt okunur görünüm gösterilir.
        public bool IsReading => HasEditor && !IsEditing;

        public bool HasEditor
        {
            get => _hasEditor;
            private set
            {
                if (SetProperty(ref _hasEditor, value))
                {
                    OnPropertyChanged(nameof(EditorTitle));
                    OnPropertyChanged(nameof(IsReading));
                }
            }
        }

        // Düzenleme tablosu açık mı; kapalıyken reçete yalnızca okunur.
        public bool IsEditing
        {
            get => _isEditing;
            private set
            {
                if (SetProperty(ref _isEditing, value))
                {
                    OnPropertyChanged(nameof(IsReading));
                    RefreshCommands();
                }
            }
        }

        // Düzenleme tablosunda seçili adım; satıra tıklanınca ya da kutusuna girilince görünüm ayarlar.
        public RecipeStepRow? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (!SetProperty(ref _selectedStep, value))
                    return;

                foreach (var step in Steps)
                    step.IsSelected = ReferenceEquals(step, value);
            }
        }

        public string ErrorSummary
        {
            get => _errorSummary;
            private set
            {
                if (SetProperty(ref _errorSummary, value))
                    OnPropertyChanged(nameof(HasErrors));
            }
        }

        public bool HasErrors => _errorSummary.Length > 0;
        public string GeneralError { get => _generalError; private set => SetProperty(ref _generalError, value); }
        public GasLaneData? LaneData { get => _laneData; private set => SetProperty(ref _laneData, value); }

        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    OnPropertyChanged(nameof(IsListEnabled));
                    RefreshCommands();
                }
            }
        }

        public RecipeListItem? SelectedRecipe
        {
            get => _selectedRecipe;
            set
            {
                if (SetProperty(ref _selectedRecipe, value) && value != null && !_loading)
                    _ = LoadRecipeAsync(value.Id);
            }
        }

        // Listeyi ada göre süzen arama metni
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        // Listeyi reçete tipine göre süzer; 0 hepsini gösterir.
        public int TypeFilter
        {
            get => _typeFilter;
            set
            {
                if (SetProperty(ref _typeFilter, value))
                    ApplyFilter();
            }
        }

        // Grup içi sıralama: 0 ada göre, 1 en yüksek sıcaklığa göre
        public int SortMode
        {
            get => _sortMode;
            set
            {
                if (SetProperty(ref _sortMode, value))
                    ApplyFilter();
            }
        }

        public string Name { get => _name; set => Edit(ref _name, value); }
        public int PressureSetPoint { get => _pressureSetPoint; set => Edit(ref _pressureSetPoint, value); }
        public int PressureModeTime { get => _pressureModeTime; set => Edit(ref _pressureModeTime, value); }
        public int PressureModeTemp { get => _pressureModeTemp; set => Edit(ref _pressureModeTemp, value); }
        public int GsoakTcId { get => _gsoakTcId; set => Edit(ref _gsoakTcId, value); }
        public int CO2Period { get => _co2Period; set => Edit(ref _co2Period, value); }

        public int RecipeType
        {
            get => _recipeType;
            set
            {
                if (!Edit(ref _recipeType, value))
                    return;

                foreach (var step in Steps)
                    step.RecipeType = value;

                NotifyHeaderVisibility();
                OnPropertyChanged(nameof(TypeText));
                UpdatePreview();
            }
        }

        public int PressureMode
        {
            get => _pressureMode;
            set
            {
                if (Edit(ref _pressureMode, value))
                    NotifyHeaderVisibility();
            }
        }

        // Başlıkta reçete tipine göre görünen alanlar
        public bool ShowPressureMode => RecipeType == 1;
        public bool ShowPressureModeTime => RecipeType == 1 && PressureMode == 10;
        public bool ShowPressureModeTemp => RecipeType == 1 && PressureMode == 20;
        public bool ShowCO2Period => RecipeType == 2;

        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string MessageLevel { get => _messageLevel; private set => SetProperty(ref _messageLevel, value); }     // Info, Ok, Bad
        public string ProfileNote { get => _profileNote; private set => SetProperty(ref _profileNote, value); }
        public ProfileChartData? ChartData { get => _chartData; private set => SetProperty(ref _chartData, value); }

        // Bağımlılıkları alır, komutları kurar ve reçete listesini ister.
        public RecipesViewModel(ApiClient api, SessionService session)
        {
            _api = api;
            _canEdit = session.HasRole(PlcPermissions.ProgramEdit);
            _canOperate = session.HasRole(PlcPermissions.Operate);

            NewCommand = new RelayCommand(NewRecipe, () => _canEdit && !IsDirty);
            CopyCommand = new RelayCommand(CopyRecipe, () => _canEdit && HasEditor && !IsDirty && _editId > 0);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => _canEdit && HasEditor && _editId > 0 && !IsDirty);
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => _canEdit && HasEditor && IsDirty);
            RevertCommand = new AsyncRelayCommand(RevertAsync, () => HasEditor && IsEditing);
            EditCommand = new RelayCommand(BeginEdit, () => _canEdit && HasEditor && !IsEditing);
            DuplicateStepCommand = new RelayCommand<RecipeStepRow>(DuplicateStep, row => _canEdit && row != null && !row.IsCooling && Steps.Count < MaxSteps);
            AddStepCommand = new RelayCommand(AddStep, () => _canEdit && HasEditor && Steps.Count < MaxSteps);
            RemoveStepCommand = new RelayCommand<RecipeStepRow>(RemoveStep, _ => _canEdit && Steps.Count > 1);
            MoveUpCommand = new RelayCommand<RecipeStepRow>(row => MoveStep(row, -1), _ => _canEdit);
            MoveDownCommand = new RelayCommand<RecipeStepRow>(row => MoveStep(row, 1), _ => _canEdit);
            TogglePinCommand = new AsyncRelayCommand<RecipeListItem>(TogglePinAsync, _ => _canEdit);
            ValidateCommand = new AsyncRelayCommand(ValidateAsync, () => _canOperate && HasEditor && _editId > 0 && !IsDirty);
            StartCommand = new AsyncRelayCommand(StartAsync, () => _canOperate && HasEditor && _editId > 0 && !IsDirty);

            _ = LoadListAsync();
        }

        // Sayfa kapanırken yapılacak bir şey yok; ortak sayfa arayüzü için vardır.
        public void Dispose()
        {
        }

        // Reçete listesini sunucudan alır ve süzgeci uygular.
        private async Task LoadListAsync()
        {
            var result = await _api.GetAsync<List<Recipe>>("api/recipes");
            if (!result.IsSuccess || result.Data == null)
            {
                ShowMessage(result.Message ?? "Reçeteler alınamadı.", "Bad");
                return;
            }

            _allRecipes.Clear();
            foreach (var recipe in result.Data)
            {
                // En yüksek sıcaklık yalnızca ısıtma adımlarından alınır.
                int maxTemp = recipe.Steps.Where(s => s.StepType == 2).Select(s => s.TargetTemp).DefaultIfEmpty(0).Max();

                _allRecipes.Add(new RecipeListItem
                {
                    Id = recipe.Id,
                    RecipeType = recipe.RecipeType,
                    Name = recipe.RecipeName,
                    Info = $"{recipe.TotalStepCount} adım · no {recipe.Id}",
                    GroupName = DescribeGroup(recipe.RecipeType),
                    IsPinned = recipe.IsPinned,
                    MaxTemp = maxTemp,
                    TempText = $"{maxTemp} °C"
                });
            }

            ApplyFilter();
        }

        // Süzgece uyan reçeteleri tipe göre gruplar; grup içinde önce sabitlenenler, sonra seçilen sıralama gelir.
        private void ApplyFilter()
        {
            string search = (SearchText ?? string.Empty).Trim();
            bool byTemp = SortMode == SortByTemp;

            var visible = _allRecipes
                .Where(r => TypeFilter == 0 || r.RecipeType == TypeFilter)
                .Where(r => search.Length == 0 || r.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(r => r.RecipeType)
                .ThenByDescending(r => r.IsPinned)
                .ThenBy(r => byTemp ? r.MaxTemp : 0)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _loading = true;
            Recipes.Clear();
            foreach (var item in visible)
            {
                item.ShowTemp = byTemp;
                Recipes.Add(item);
            }
            SelectedRecipe = _editId > 0 ? Recipes.FirstOrDefault(r => r.Id == _editId) : null;
            _loading = false;
        }

        // Reçetenin sabitleme işaretini sunucuda değiştirir ve listeyi yeniler.
        private async Task TogglePinAsync(RecipeListItem? item)
        {
            if (item == null)
                return;

            var result = await _api.PutAsync<JsonElement>($"api/recipes/{item.Id}/pin?pinned={(!item.IsPinned).ToString().ToLowerInvariant()}");
            if (!result.IsSuccess)
            {
                ShowMessage(result.Message ?? "Sabitleme değiştirilemedi.", "Bad");
                return;
            }

            await LoadListAsync();
        }

        // Açık reçeteyi düzenleme tablosuna geçirir.
        private void BeginEdit()
        {
            SelectedStep = null;
            IsEditing = true;
        }

        // Seçilen reçeteyi adımlarıyla sunucudan alır ve salt okunur görünümde açar.
        private async Task LoadRecipeAsync(int id)
        {
            var result = await _api.GetAsync<Recipe>($"api/recipes/{id}");
            if (!result.IsSuccess || result.Data == null)
            {
                ShowMessage(result.Message ?? "Reçete alınamadı.", "Bad");
                return;
            }

            FillEditor(result.Data, result.Data.Id);
            IsDirty = false;
            IsEditing = false;
            ShowMessage(string.Empty, "Info");
        }

        // Verilen reçeteyi düzenleme alanına yerleştirir ve eğriyi çizer.
        private void FillEditor(Recipe recipe, int editId)
        {
            _loading = true;

            _editId = editId;
            _name = recipe.RecipeName;
            _recipeType = recipe.RecipeType;
            _pressureSetPoint = recipe.PressureSetPoint;
            _pressureMode = recipe.PressureMode;
            _pressureModeTime = recipe.PressureModeTime;
            _pressureModeTemp = recipe.PressureModeTemp;
            _gsoakTcId = recipe.GsoakTcId;
            _co2Period = recipe.CO2_Period;
            _plcErrorStep = -1;
            _plcErrorText = string.Empty;
            _selectedStep = null;

            Steps.Clear();
            foreach (var step in recipe.Steps.OrderBy(s => s.StepNo))
                Steps.Add(RecipeStepRow.FromEntity(step, recipe.RecipeType, MarkDirty));
            Renumber();

            _loading = false;

            HasEditor = true;
            OnPropertyChanged(string.Empty);
            UpdatePreview();
            RefreshCommands();
        }

        // Vakum (200 mbar, 20 dk), ısıtma ve soğutma adımlı boş bir nitrasyon reçetesiyle düzenlemeyi açar.
        private void NewRecipe()
        {
            var recipe = new Recipe
            {
                RecipeName = "Yeni reçete",
                RecipeType = 2,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = RecipeStepRow.DefaultVacSetPoint, Vac_Timeout = RecipeStepRow.DefaultVacTimeout },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = 500, StepTime = 60, FanSpeed = RecipeStepRow.DefaultFanSpeed },
                    new RecipeStep { StepNo = 3, StepType = 3, TargetTemp = 150, FanSpeed = RecipeStepRow.DefaultFanSpeed }
                }
            };

            OpenAsNew(recipe, "Yeni reçete kaydedilene kadar listede görünmez.");
        }

        // Açık reçetenin kopyasını yeni reçete olarak düzenlemeye açar.
        private void CopyRecipe()
        {
            var copy = BuildRecipe();
            string name = copy.RecipeName + " kopya";
            copy.RecipeName = name.Length > NameMaxLength ? name.Substring(0, NameMaxLength) : name;

            OpenAsNew(copy, "Kopya kaydedilene kadar listede görünmez.");
        }

        // Verilen reçeteyi henüz kaydedilmemiş yeni reçete olarak düzenlemeye açar.
        private void OpenAsNew(Recipe recipe, string note)
        {
            _loading = true;
            SelectedRecipe = null;
            _loading = false;

            FillEditor(recipe, 0);
            IsDirty = true;
            IsEditing = true;
            ShowMessage(note, "Info");
        }

        // Onay alıp açık reçeteyi siler.
        private async Task DeleteAsync()
        {
            var answer = MessageBox.Show($"'{Name}' reçetesi silinecek.\n\nSilmek istiyor musunuz?", "Reçeteyi sil",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.DeleteAsync<JsonElement>($"api/recipes/{_editId}");
            if (!result.IsSuccess)
            {
                ShowMessage(result.Message ?? "Reçete silinemedi.", "Bad");
                return;
            }

            ClearEditor();
            await LoadListAsync();
            ShowMessage(result.Message ?? "Reçete silindi.", "Ok");
        }

        // Reçeteyi sunucuya kaydeder (yeniyse ekler, değilse günceller) ve listeyi yeniler.
        private async Task SaveAsync()
        {
            var recipe = BuildRecipe();
            int savedId;
            string? message;

            if (_editId == 0)
            {
                var added = await _api.PostAsync<int>("api/recipes", recipe);
                if (!added.IsSuccess)
                {
                    ShowMessage(added.Message ?? "Reçete kaydedilemedi.", "Bad");
                    return;
                }
                savedId = added.Data;
                message = added.Message;
            }
            else
            {
                var updated = await _api.PutAsync<JsonElement>($"api/recipes/{_editId}", recipe);
                if (!updated.IsSuccess)
                {
                    ShowMessage(updated.Message ?? "Reçete kaydedilemedi.", "Bad");
                    return;
                }
                savedId = _editId;
                message = updated.Message;
            }

            _editId = savedId;
            IsDirty = false;
            await LoadListAsync();
            await LoadRecipeAsync(savedId);
            ShowMessage(string.IsNullOrEmpty(message) ? "Reçete kaydedildi." : message, "Ok");
        }

        // Düzenlemeden çıkar: kayıtlı reçeteyi yeniden yükleyip okuma görünümüne döner, yeni reçeteyi kapatır.
        private async Task RevertAsync()
        {
            if (_editId > 0)
            {
                await LoadRecipeAsync(_editId);
                return;
            }

            ClearEditor();
        }

        // Reçeteyi PLC'ye yazıp yalnızca kontrol ettirir; program başlamaz.
        private async Task ValidateAsync()
        {
            var result = await _api.PostAsync<ScadaCommandResult>($"api/recipes/{_editId}/validate");
            ApplyPlcResult(result.IsSuccess, result.Message);
            ShowMessage(result.Message ?? (result.IsSuccess ? "PLC reçeteyi kabul etti." : "PLC reçeteyi reddetti."),
                result.IsSuccess ? "Ok" : "Bad");
        }

        // Şarj ağırlığını ve notu sorar, reçeteyi PLC'ye yazıp programı başlatır; başlarsa ana ekrana döner.
        private async Task StartAsync()
        {
            var dialog = new StartDialog(Name)
            {
                Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            };

            if (dialog.ShowDialog() != true)
                return;

            var request = new StartRequestDto { LoadKg = dialog.LoadKg, Note = dialog.Note };
            var result = await _api.PostAsync<ScadaCommandResult>($"api/recipes/{_editId}/start", request);
            if (!result.IsSuccess)
            {
                ApplyPlcResult(false, result.Message);
                ShowMessage(result.Message ?? "Program başlatılamadı.", "Bad");
                return;
            }

            // PLC uyarıyla kabul ettiyse (örneğin fırın sıcak) ya da şarj bilgisi yazılamadıysa operatör görmeden sayfa değişmemeli.
            bool infoProblem = (result.Message ?? string.Empty).Contains("kaydedilemedi");
            if ((result.Data != null && result.Data.HasWarning) || infoProblem)
                MessageBox.Show(result.Message ?? "Program uyarıyla başladı.", "Program başladı", MessageBoxButton.OK, MessageBoxImage.Warning);

            NavigateRequested?.Invoke("overview");
        }

        // PLC'nin reçete kontrolü sonucunu işler: hata bir adımı gösteriyorsa o adım işaretlenir, kabul edildiyse işaret kalkar.
        private void ApplyPlcResult(bool accepted, string? message)
        {
            _plcErrorStep = -1;
            _plcErrorText = string.Empty;

            if (!accepted && message != null && message.StartsWith("Reçete hatası"))
            {
                var match = Regex.Match(message, @"adım (\d+)");
                _plcErrorStep = match.Success ? int.Parse(match.Groups[1].Value) : 0;

                int colon = message.IndexOf(": ", StringComparison.Ordinal);
                _plcErrorText = colon >= 0 ? message.Substring(colon + 2) : message;
            }

            UpdatePreview();
        }

        // Fanı %100 olan yeni bir ısıtma adımı ekler: seçili ısıtma adımının altına, seçim yoksa soğutmadan önceye.
        private void AddStep()
        {
            var step = new RecipeStep { StepType = 2, TargetTemp = 500, StepTime = 60, FanSpeed = RecipeStepRow.DefaultFanSpeed };
            var row = RecipeStepRow.FromEntity(step, RecipeType, MarkDirty);

            int index = Steps.Count > 0 && Steps[^1].IsCooling ? Steps.Count - 1 : Steps.Count;

            if (SelectedStep != null && SelectedStep.IsHeating)
                index = Steps.IndexOf(SelectedStep) + 1;
            else if (SelectedStep != null && SelectedStep.IsVacuum)
                index = Steps.Count(s => s.IsVacuum);

            Steps.Insert(index, row);

            Renumber();
            SelectedStep = row;
            MarkDirty();
        }

        // Verilen adımın kopyasını hemen altına ekler.
        private void DuplicateStep(RecipeStepRow? row)
        {
            if (row == null || Steps.Count >= MaxSteps)
                return;

            var copy = row.Clone();
            Steps.Insert(Steps.IndexOf(row) + 1, copy);

            Renumber();
            SelectedStep = copy;
            MarkDirty();
        }

        // Verilen adımı siler.
        private void RemoveStep(RecipeStepRow? row)
        {
            if (row == null || Steps.Count <= 1)
                return;

            if (ReferenceEquals(SelectedStep, row))
                SelectedStep = null;

            Steps.Remove(row);
            Renumber();
            MarkDirty();
        }

        // Verilen adımı bir satır yukarı ya da aşağı taşır.
        private void MoveStep(RecipeStepRow? row, int direction)
        {
            if (row == null)
                return;

            int index = Steps.IndexOf(row);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= Steps.Count)
                return;

            Steps.Move(index, target);
            Renumber();
            MarkDirty();
        }

        // Adımları sırayla 1'den numaralar.
        private void Renumber()
        {
            for (int i = 0; i < Steps.Count; i++)
                Steps[i].StepNo = i + 1;
        }

        // Düzenleme alanındaki değerlerden sunucuya gönderilecek reçeteyi üretir.
        private Recipe BuildRecipe()
        {
            return new Recipe
            {
                Id = _editId,
                RecipeName = (Name ?? string.Empty).Trim(),
                RecipeType = RecipeType,
                TotalStepCount = Steps.Count,
                PressureSetPoint = PressureSetPoint,
                PressureMode = ShowPressureMode ? PressureMode : 0,
                PressureModeTime = ShowPressureModeTime ? PressureModeTime : 0,
                PressureModeTemp = ShowPressureModeTemp ? PressureModeTemp : 0,
                GsoakTcId = GsoakTcId,
                CO2_Period = ShowCO2Period ? CO2Period : 0,
                Steps = Steps.Select(s => s.ToEntity()).ToList()
            };
        }

        // Düzenleme alanını kapatır.
        private void ClearEditor()
        {
            _loading = true;
            SelectedRecipe = null;
            Steps.Clear();
            _editId = 0;
            _loading = false;

            HasEditor = false;
            IsEditing = false;
            ChartData = null;
            LaneData = null;
            ReadLines.Clear();
            MetaItems.Clear();
            SummaryItems.Clear();
            ErrorSummary = string.Empty;
            GeneralError = string.Empty;
            ProfileNote = string.Empty;
            IsDirty = false;
            OnPropertyChanged(nameof(EditorTitle));
            ShowMessage(string.Empty, "Info");
            RefreshCommands();
        }

        // Açık reçeteden eğriyi, gaz şeritlerini, özet değerleri, okuma satırlarını ve hata işaretlerini yeniden üretir.
        private void UpdatePreview()
        {
            if (!HasEditor && Steps.Count == 0)
                return;

            var recipe = BuildRecipe();

            CheckRecipe();
            BuildSummary(recipe);
            BuildReadLines(recipe);

            if (!recipe.Steps.Any(s => s.StepType == 2))
            {
                ChartData = null;
                LaneData = null;
                ProfileNote = "Eğri için en az bir ısıtma adımı gerekir.";
                return;
            }

            var timeline = RecipeTimeline.Build(recipe, PreviewStartTemp);

            var data = timeline.ToChartData();
            foreach (var step in timeline.Steps)
                data.HoverZones.Add(new ProfileHoverZone { X1 = step.X1, X2 = step.X2, Text = RecipeTimeline.DescribeStep(step.Step) });

            ChartData = data;
            LaneData = BuildLanes(timeline);
            ProfileNote = "Üstteki numaralar adım numarasıdır. Vakum ve soğutmanın süresi önceden bilinmez; bölgelerinin genişliği süreyi göstermez.";
        }

        // Eğrinin altındaki gaz şeritlerini üretir: her şerit bir gazı, her çubuk o gazın verildiği adımı gösterir.
        private GasLaneData BuildLanes(RecipeTimeline timeline)
        {
            var data = new GasLaneData { XMax = timeline.XMax };
            var errorSteps = Steps.Where(s => s.HasH2Error).Select(s => s.StepNo).ToHashSet();

            AddLane(data, timeline, "Azot", "#8FB7FF", s => s.N2_Flow > 0 ? s.N2_Flow.ToString() : null, null);
            AddLane(data, timeline, "Amonyak", "#C58BFF", s => s.NH3_Flow > 0 ? s.NH3_Flow.ToString() : null, null);
            AddLane(data, timeline, "CO₂", "#7FD4C1", s => s.CO2_Perc > 0 ? $"%{s.CO2_Perc}" : null, null);
            AddLane(data, timeline, "Su", "#5BC0F0", s => s.Su_On != 0 ? "açık" : null, null);
            AddLane(data, timeline, "H₂ oto", "#FFD23B", s => s.H2_Auto != 0 ? $"%{s.H2_Set / 10.0:0.0}" : null, errorSteps);
            AddLane(data, timeline, "Fan", "#8FA0BC", s => s.FanSpeed > 0 ? $"%{s.FanSpeed}" : null, null);

            return data;
        }

        // Verilen gaz için şerit ekler; hiçbir adımda kullanılmıyorsa şerit eklenmez.
        private static void AddLane(GasLaneData data, RecipeTimeline timeline, string name, string color,
            Func<RecipeStep, string?> describe, HashSet<int>? errorSteps)
        {
            var lane = new GasLane { Name = name, Color = (Color)ColorConverter.ConvertFromString(color) };

            foreach (var step in timeline.Steps.Where(s => s.Kind != 1))
            {
                string? text = describe(step.Step);
                if (text == null)
                    continue;

                lane.Bars.Add(new GasBar
                {
                    X1 = step.X1,
                    X2 = step.X2,
                    Text = text,
                    IsError = errorSteps != null && errorSteps.Contains(step.Step.StepNo)
                });
            }

            if (lane.Bars.Count > 0)
                data.Lanes.Add(lane);
        }

        // Reçete kurallarından ekranda denetlenebilenleri denetler; hatalı adımları işaretler ve hata özetini yazar. Son söz PLC'nindir.
        private void CheckRecipe()
        {
            var general = new List<string>();
            bool nonVacuumSeen = false;

            for (int i = 0; i < Steps.Count; i++)
            {
                var row = Steps[i];
                string error = string.Empty;
                bool h2Error = false;

                if (row.IsVacuum && nonVacuumSeen)
                    error = "Vakum adımları en başta ve art arda olmalı.";

                if (!row.IsVacuum)
                    nonVacuumSeen = true;

                if (row.IsCooling && i != Steps.Count - 1)
                    error = "Soğutma tek olmalı ve son adım olmalı.";

                if (row.ShowH2 && row.H2Auto && row.NH3Flow < 1)
                {
                    h2Error = true;
                    error = "H₂ otomatik seçiliyken amonyak akışı en az 1 L/h olmalı.";
                }
                else if (row.ShowH2 && row.H2Auto && row.H2SetPercent < 0.1)
                {
                    error = "H₂ otomatik seçiliyken H₂ seti en az %0,1 olmalı.";
                }

                row.HasH2Error = h2Error;
                row.ErrorText = error;
            }

            if (Steps.Count > 0 && !Steps[^1].IsCooling)
                general.Add("Son adım soğutma olmalı.");

            if (!Steps.Any(s => s.IsHeating))
                general.Add("En az bir ısıtma adımı olmalı.");

            if (RecipeType == 2 && !Steps.Any(s => s.IsVacuum))
                general.Add("Nitrasyon reçetesinde en az bir vakum adımı olmalı.");

            // PLC'nin bulduğu hata, ekranın kendi bulamadığı bir kuralsa ilgili adıma ya da genel hatalara eklenir.
            if (_plcErrorStep > 0)
            {
                var row = Steps.FirstOrDefault(s => s.StepNo == _plcErrorStep);
                if (row != null && !row.HasError)
                    row.ErrorText = "PLC: " + _plcErrorText;
            }
            else if (_plcErrorStep == 0)
            {
                general.Add("PLC: " + _plcErrorText);
            }

            var badSteps = Steps.Where(s => s.HasError).Select(s => s.StepNo).ToList();
            int count = badSteps.Count + general.Count;

            GeneralError = string.Join("  ", general);
            ErrorSummary = count == 0
                ? string.Empty
                : badSteps.Count > 0 ? $"{count} hata · adım {string.Join(", ", badSteps)}" : $"{count} hata";
        }

        // Reçetenin başlık değerlerini ve hesaplanan toplamları özet hücrelerine yazar.
        private void BuildSummary(Recipe recipe)
        {
            var heating = recipe.Steps.Where(s => s.StepType == 2).ToList();
            int heatMinutes = heating.Sum(s => Math.Max(0, s.StepTime));
            double n2 = heating.Sum(s => s.N2_Flow * Math.Max(0, s.StepTime) / 60.0);
            double nh3 = heating.Sum(s => s.NH3_Flow * Math.Max(0, s.StepTime) / 60.0);

            var totals = new List<(string Label, string Value)>
            {
                ("Isıtma süresi", StatusTexts.FormatMinutes(heatMinutes)),
                ("Azot (ısıtmada)", $"≈ {n2:0} L")
            };

            if (recipe.RecipeType == 2)
                totals.Add(("Amonyak", $"≈ {nh3:0} L"));

            var meta = new List<(string Label, string Value)>
            {
                ("Basınç seti", $"{recipe.PressureSetPoint} mbar"),
                ("Gsoak ölçümü", GsoakSensors.FirstOrDefault(o => o.Value == recipe.GsoakTcId)?.Text ?? "—")
            };

            if (ShowCO2Period)
                meta.Add(("CO₂ periyodu", $"{recipe.CO2_Period} dk"));

            if (ShowPressureMode)
                meta.Add(("Basınç modu", PressureModes.FirstOrDefault(o => o.Value == recipe.PressureMode)?.Text ?? "—"));

            if (ShowPressureModeTime)
                meta.Add(("Tazeleme aralığı", $"{recipe.PressureModeTime} dk"));

            if (ShowPressureModeTemp)
                meta.Add(("Geçiş sıcaklığı", $"{recipe.PressureModeTemp} °C"));

            meta.Add(("Adım sayısı", recipe.Steps.Count.ToString()));
            meta.AddRange(totals);

            Fill(MetaItems, meta);
            Fill(SummaryItems, totals);
        }

        // Hücre listesini verilen başlık ve değerlerle yeniden doldurur.
        private static void Fill(ObservableCollection<StatusCell> target, List<(string Label, string Value)> items)
        {
            target.Clear();
            foreach (var item in items)
                target.Add(new StatusCell(item.Label, item.Value));
        }

        // Okuma görünümündeki adım satırlarını üretir: her adım bir cümle, ek değerler etiket olarak yazılır.
        private void BuildReadLines(Recipe recipe)
        {
            ReadLines.Clear();
            double temp = PreviewStartTemp;

            foreach (var step in recipe.Steps.OrderBy(s => s.StepNo))
            {
                var line = new StepLine
                {
                    StepNo = step.StepNo,
                    StepType = step.StepType,
                    ErrorText = Steps.FirstOrDefault(s => s.StepNo == step.StepNo)?.ErrorText ?? string.Empty
                };

                switch (step.StepType)
                {
                    case 1:
                        line.KindText = "VAKUM";
                        line.Main = $"{step.Vac_SetPoint} mbar mutlak basınca in";
                        line.Chips.Add($"zaman aşımı {step.Vac_Timeout} dk");
                        break;

                    case 2:
                        line.KindText = "ISITMA";
                        line.Main = Math.Abs(step.TargetTemp - temp) < 0.5
                            ? $"{step.TargetTemp} °C sıcaklıkta {step.StepTime} dk bekle"
                            : $"{step.StepTime} dk içinde {step.TargetTemp} °C sıcaklığa git";

                        if (step.GSoak > 0) line.Chips.Add($"Gsoak {step.GSoak} °C");
                        if (step.N2_Flow > 0) line.Chips.Add($"Azot {step.N2_Flow} L/h");
                        if (step.NH3_Flow > 0) line.Chips.Add($"Amonyak {step.NH3_Flow} L/h");
                        if (step.CO2_Perc > 0) line.Chips.Add($"CO₂ %{step.CO2_Perc}");
                        if (step.Su_On != 0) line.Chips.Add("Su açık");
                        if (step.H2_Auto != 0) line.Chips.Add($"H₂ oto %{step.H2_Set / 10.0:0.0}");
                        line.Chips.Add($"Fan %{step.FanSpeed}");
                        temp = step.TargetTemp;
                        break;

                    case 3:
                        line.KindText = "SOĞUTMA";
                        line.Main = $"{step.TargetTemp} °C altına soğut";
                        if (step.N2_Flow > 0) line.Chips.Add($"Azot {step.N2_Flow} L/h");
                        line.Chips.Add($"Fan %{step.FanSpeed}");
                        temp = step.TargetTemp;
                        break;

                    default:
                        line.KindText = "ADIM";
                        break;
                }

                ReadLines.Add(line);
            }
        }

        // Başlık alanını günceller ve değiştiyse reçeteyi değişmiş sayar.
        private bool Edit<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (!SetProperty(ref field, value, name))
                return false;

            MarkDirty();
            return true;
        }

        // Reçetenin kaydedilmemiş değişikliği olduğunu işaretler ve eğriyi yeniler (yükleme sırasında çalışmaz).
        private void MarkDirty()
        {
            if (_loading)
                return;

            // Reçete değişti; PLC'nin eski kontrol sonucu artık geçerli değil.
            _plcErrorStep = -1;
            _plcErrorText = string.Empty;

            IsDirty = true;
            UpdatePreview();
            RefreshCommands();
        }

        // Başlıkta reçete tipine bağlı alanların görünürlüğünü yeniden hesaplatır.
        private void NotifyHeaderVisibility()
        {
            OnPropertyChanged(nameof(ShowPressureMode));
            OnPropertyChanged(nameof(ShowPressureModeTime));
            OnPropertyChanged(nameof(ShowPressureModeTemp));
            OnPropertyChanged(nameof(ShowCO2Period));
        }

        // Sayfanın altındaki sonuç mesajını ve rengini ayarlar.
        private void ShowMessage(string text, string level)
        {
            Message = text;
            MessageLevel = level;
        }

        // Düğmelerin etkin/pasif durumunu yeniden hesaplatır.
        private void RefreshCommands()
        {
            NewCommand.NotifyCanExecuteChanged();
            CopyCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
            EditCommand.NotifyCanExecuteChanged();
            DuplicateStepCommand.NotifyCanExecuteChanged();
            AddStepCommand.NotifyCanExecuteChanged();
            RemoveStepCommand.NotifyCanExecuteChanged();
            ValidateCommand.NotifyCanExecuteChanged();
            StartCommand.NotifyCanExecuteChanged();
        }

        // Reçete tipinin liste başlığını döner.
        private static string DescribeGroup(int recipeType)
        {
            return recipeType switch
            {
                1 => "MENEVİŞLER",
                2 => "NİTRASYONLAR",
                3 => "OKSİDASYONLAR",
                _ => "DİĞER"
            };
        }
    }
}