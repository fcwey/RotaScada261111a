using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Controls;
using DesktopUI.Helpers;
using DesktopUI.Services;
using Entities.Concrete;
using Entities.Structures;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

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
    }

    // Reçeteler sayfası: liste, düzenleme, PLC'de kontrol ve başlatma
    public class RecipesViewModel : ObservableObject, IDisposable
    {
        private const int MaxSteps = 20;
        private const int NameMaxLength = 30;
        private const int DefaultFanSpeed = 100;
        private const float PreviewStartTemp = 20f;

        private readonly ApiClient _api;
        private readonly bool _canEdit;
        private readonly bool _canOperate;
        private readonly List<RecipeListItem> _allRecipes = new();

        private bool _loading;
        private int _editId;
        private bool _hasEditor;
        private bool _isDirty;
        private RecipeListItem? _selectedRecipe;
        private string _searchText = string.Empty;
        private int _typeFilter;
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

        public List<Option> TypeFilters { get; } = new()
        {
            new Option(0, "Tümü"), new Option(1, "Meneviş"), new Option(2, "Nitrasyon"), new Option(3, "Oksidasyon")
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
        public IRelayCommand AddStepCommand { get; }
        public IRelayCommand<RecipeStepRow> RemoveStepCommand { get; }
        public IRelayCommand<RecipeStepRow> MoveUpCommand { get; }
        public IRelayCommand<RecipeStepRow> MoveDownCommand { get; }
        public IAsyncRelayCommand ValidateCommand { get; }
        public IAsyncRelayCommand StartCommand { get; }

        public bool CanEdit => _canEdit;
        public bool IsListEnabled => !IsDirty;
        public string EditorTitle => !HasEditor ? string.Empty : _editId == 0 ? "YENİ REÇETE" : $"REÇETE NO {_editId}";

        public bool HasEditor
        {
            get => _hasEditor;
            private set
            {
                if (SetProperty(ref _hasEditor, value))
                    OnPropertyChanged(nameof(EditorTitle));
            }
        }

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
            RevertCommand = new AsyncRelayCommand(RevertAsync, () => HasEditor && IsDirty);
            AddStepCommand = new RelayCommand(AddStep, () => _canEdit && HasEditor && Steps.Count < MaxSteps);
            RemoveStepCommand = new RelayCommand<RecipeStepRow>(RemoveStep, _ => _canEdit && Steps.Count > 1);
            MoveUpCommand = new RelayCommand<RecipeStepRow>(row => MoveStep(row, -1), _ => _canEdit);
            MoveDownCommand = new RelayCommand<RecipeStepRow>(row => MoveStep(row, 1), _ => _canEdit);
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
            foreach (var recipe in result.Data.OrderBy(r => r.RecipeName))
            {
                _allRecipes.Add(new RecipeListItem
                {
                    Id = recipe.Id,
                    RecipeType = recipe.RecipeType,
                    Name = recipe.RecipeName,
                    Info = $"{DescribeType(recipe.RecipeType)} · {recipe.TotalStepCount} adım · no {recipe.Id}"
                });
            }

            ApplyFilter();
        }

        // Arama metnine ve tip süzgecine uyan reçeteleri listeye koyar; açık reçete listede kalıyorsa seçili gösterir.
        private void ApplyFilter()
        {
            string search = (SearchText ?? string.Empty).Trim();

            var visible = _allRecipes
                .Where(r => TypeFilter == 0 || r.RecipeType == TypeFilter)
                .Where(r => search.Length == 0 || r.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            _loading = true;
            Recipes.Clear();
            foreach (var item in visible)
                Recipes.Add(item);
            SelectedRecipe = _editId > 0 ? Recipes.FirstOrDefault(r => r.Id == _editId) : null;
            _loading = false;
        }

        // Seçilen reçeteyi adımlarıyla sunucudan alıp düzenleme alanına yükler.
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

        // Vakum, ısıtma ve soğutma adımlı boş bir nitrasyon reçetesiyle düzenlemeyi açar.
        private void NewRecipe()
        {
            var recipe = new Recipe
            {
                RecipeName = "Yeni reçete",
                RecipeType = 2,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 100, Vac_Timeout = 30 },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = 500, StepTime = 60, FanSpeed = DefaultFanSpeed },
                    new RecipeStep { StepNo = 3, StepType = 3, TargetTemp = 150, FanSpeed = DefaultFanSpeed }
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

        // Kaydedilmemiş değişiklikleri atar: kayıtlı reçeteyi yeniden yükler, yeni reçeteyi kapatır.
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
            ShowMessage(result.Message ?? (result.IsSuccess ? "PLC reçeteyi kabul etti." : "PLC reçeteyi reddetti."),
                result.IsSuccess ? "Ok" : "Bad");
        }

        // Onay alıp reçeteyi PLC'ye yazar ve programı başlatır; başlarsa ana ekrana döner.
        private async Task StartAsync()
        {
            var answer = MessageBox.Show($"'{Name}' reçetesiyle program başlatılacak.\n\nBaşlatmak istiyor musunuz?", "Programı başlat",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.PostAsync<ScadaCommandResult>($"api/recipes/{_editId}/start");
            if (!result.IsSuccess)
            {
                ShowMessage(result.Message ?? "Program başlatılamadı.", "Bad");
                return;
            }

            // PLC başlatmayı uyarıyla kabul ettiyse (örneğin fırın sıcak) operatör görmeden sayfa değişmemeli.
            if (result.Data != null && result.Data.HasWarning)
                MessageBox.Show(result.Message ?? "Program uyarıyla başladı.", "Program başladı", MessageBoxButton.OK, MessageBoxImage.Warning);

            NavigateRequested?.Invoke("overview");
        }

        // Soğutma adımından önce (yoksa sona) fanı %100 olan yeni bir ısıtma adımı ekler.
        private void AddStep()
        {
            var step = new RecipeStep { StepType = 2, TargetTemp = 500, StepTime = 60, FanSpeed = DefaultFanSpeed };
            var row = RecipeStepRow.FromEntity(step, RecipeType, MarkDirty);

            int index = Steps.Count > 0 && Steps[^1].IsCooling ? Steps.Count - 1 : Steps.Count;
            Steps.Insert(index, row);

            Renumber();
            MarkDirty();
        }

        // Verilen adımı siler.
        private void RemoveStep(RecipeStepRow? row)
        {
            if (row == null || Steps.Count <= 1)
                return;

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
            ChartData = null;
            ProfileNote = string.Empty;
            IsDirty = false;
            OnPropertyChanged(nameof(EditorTitle));
            ShowMessage(string.Empty, "Info");
            RefreshCommands();
        }

        // Düzenleme alanındaki adımlardan teorik eğriyi ve adım bölgelerini hesaplayıp grafiği günceller.
        private void UpdatePreview()
        {
            var recipe = BuildRecipe();
            var points = RecipeProfile.Calculate(recipe, PreviewStartTemp);

            if (points.Count < 2)
            {
                ChartData = null;
                ProfileNote = "Eğri için en az bir ısıtma adımı gerekir.";
                return;
            }

            double heatHours = Math.Max(points[^1].Minute / 60.0, 0.01);

            // Vakum ve soğutmanın süresi önceden bilinmez; sırayı göstermek için sabit genişlikte bölge ayrılır.
            double unit = Math.Max(0.2, heatHours * 0.07);

            var data = new ProfileChartData();
            double x = 0;

            foreach (var row in Steps.Where(s => s.IsVacuum))
            {
                data.Bands.Add(new ProfileBand { X1 = x, X2 = x + unit, Label = row.StepNo.ToString(), Kind = 1 });
                x += unit;
            }

            double heatStart = x;
            if (heatStart > 0)
                data.Theory.Add(new List<Point> { new Point(0, PreviewStartTemp), new Point(heatStart, PreviewStartTemp) });

            for (int i = 1; i < points.Count; i++)
            {
                data.Bands.Add(new ProfileBand
                {
                    X1 = heatStart + points[i - 1].Minute / 60.0,
                    X2 = heatStart + points[i].Minute / 60.0,
                    Label = $"{points[i].StepNo} · {points[i].Temp:0} °C",
                    Kind = 2
                });
            }
            data.Theory.Add(points.Select(p => new Point(heatStart + p.Minute / 60.0, p.Temp)).ToList());

            double heatEnd = heatStart + heatHours;
            double end = heatEnd;

            var cooling = Steps.LastOrDefault(s => s.IsCooling);
            if (cooling != null)
            {
                end = heatEnd + unit * 2;
                data.Bands.Add(new ProfileBand { X1 = heatEnd, X2 = end, Label = $"{cooling.StepNo} · {cooling.TargetTemp} °C", Kind = 3 });
                data.Theory.Add(new List<Point> { new Point(heatEnd, points[^1].Temp), new Point(end, cooling.TargetTemp) });
            }

            data.TickStart = heatStart;
            data.TickEnd = heatEnd;
            data.XMax = end;
            data.YMax = Math.Max(150, Math.Ceiling(points.Max(p => p.Temp) / 150.0) * 150);

            ChartData = data;
            ProfileNote = $"Isıtma toplamı {StatusTexts.FormatMinutes((int)points[^1].Minute)} (saat:dk). Vakum ve soğutma bölgelerinin genişliği süreyi göstermez.";
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
            AddStepCommand.NotifyCanExecuteChanged();
            RemoveStepCommand.NotifyCanExecuteChanged();
            ValidateCommand.NotifyCanExecuteChanged();
            StartCommand.NotifyCanExecuteChanged();
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