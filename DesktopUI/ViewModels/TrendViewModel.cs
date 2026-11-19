using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopUI.Services;
using Entities.Dtos;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopUI.ViewModels
{
    // Sol eksende gösterilebilecek bir sıcaklık ölçümü ve seçili olup olmadığı
    public class TrendTagItem : ObservableObject
    {
        private readonly Action _changed;
        private bool _isChecked;

        public string Tag { get; }
        public string Name { get; }
        public Brush Swatch { get; }

        // İşaret değişince grafiğin yeniden yüklenmesini ister.
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value))
                    _changed();
            }
        }

        // Ölçümün adını, rengini, başlangıç seçimini ve değişiklikte çağrılacak işlemi alır.
        public TrendTagItem(string tag, string name, Brush swatch, bool isChecked, Action changed)
        {
            Tag = tag;
            Name = name;
            Swatch = swatch;
            _isChecked = isChecked;
            _changed = changed;
        }
    }

    // Grafikte çizilen bir seri: zaman ve değer dizileri, rengi, ekseni ve fare altındaki değeri
    public class TrendSeriesView : ObservableObject
    {
        private string _valueText = string.Empty;

        public string Tag { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#FFFFFF";
        public Brush Swatch { get; set; } = Brushes.White;
        public bool IsRight { get; set; }           // Sağ eksene çizilir
        public bool IsDashed { get; set; }          // Set değerleri kesikli çizilir
        public string Format { get; set; } = "0.0";
        public double[] X { get; set; } = Array.Empty<double>();    // OLE tarih sayısı
        public double[] Y { get; set; } = Array.Empty<double>();

        public string ValueText { get => _valueText; set => SetProperty(ref _valueText, value); }
    }

    // Trend sayfası: seçilen aralığın ya da şarjın ölçümlerini sunucudan okur ve grafiğe hazırlar
    public class TrendViewModel : ObservableObject, IDisposable
    {
        private const int Points = 1200;
        private const int RefreshSeconds = 15;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Ölçüm tanımları. Grup 0 sol eksendeki sıcaklıklardır; diğer gruplar sağ eksen seçenekleridir.
        private static readonly TagDef[] Tags =
        {
            new TagDef("TempPota", "Pota", "°C", "#3B9BFF", false, 0, "0.0"),
            new TagDef("TempSet", "Set", "°C", "#D5DEEC", true, 0, "0.0"),
            new TagDef("TempAlt", "Alt cehennemlik", "°C", "#FF9F45", false, 0, "0.0"),
            new TagDef("TempUst", "Üst cehennemlik", "°C", "#F5B84A", false, 0, "0.0"),
            new TagDef("TempPotaAlt", "Pota alt", "°C", "#3CC8D9", false, 0, "0.0"),
            new TagDef("TempPotaUst", "Pota üst", "°C", "#7BC96F", false, 0, "0.0"),
            new TagDef("TempMalzeme", "Malzeme", "°C", "#E57BD0", false, 0, "0.0"),

            new TagDef("Pressure", "Basınç", "mbar", "#5FE39A", false, 1, "0.0"),
            new TagDef("PressureSet", "Basınç seti", "mbar", "#5FE39A", true, 1, "0.0"),

            new TagDef("N2FlowAct", "Azot", "L/h", "#5FE39A", false, 2, "0"),
            new TagDef("NH3FlowAct", "Amonyak", "L/h", "#C792EA", false, 2, "0"),

            new TagDef("H2Act", "Hidrojen", "%", "#5FE39A", false, 3, "0.0"),
            new TagDef("HeatOutAlt", "Alt ısıtma çıkışı", "%", "#C792EA", false, 3, "0"),
            new TagDef("HeatOutUst", "Üst ısıtma çıkışı", "%", "#FF6B8B", false, 3, "0"),
            new TagDef("FanSpeedAct", "Fan", "%", "#B8E986", false, 3, "0"),

            new TagDef("Kn", "Kn", "", "#5FE39A", false, 4, "0.00"),

            new TagDef("ActiveStep", "Adım", "", "#C792EA", false, 5, "0")
        };

        private readonly ApiClient _api;
        private readonly DispatcherTimer _timer;

        private double[] _times = Array.Empty<double>();
        private bool _ready;
        private bool _loading;
        private int _request;
        private Option _selectedRange;
        private Option _selectedBatch;
        private Option _selectedRightGroup;
        private bool _followLive = true;
        private string _message = string.Empty;
        private string _cursorText = string.Empty;
        private string _rightAxisLabel = string.Empty;

        // Seriler yenilendiğinde tetiklenir; görünüm grafiği yeniden çizer.
        public event Action? DataChanged;

        public ObservableCollection<Option> Ranges { get; } = new();
        public ObservableCollection<Option> Batches { get; } = new();
        public ObservableCollection<Option> RightGroups { get; } = new();
        public ObservableCollection<TrendTagItem> TempTags { get; } = new();
        public ObservableCollection<TrendSeriesView> Series { get; } = new();

        public IAsyncRelayCommand RefreshCommand { get; }

        // Gösterilen süre (saat); şarj seçiliyken kullanılmaz.
        public Option SelectedRange
        {
            get => _selectedRange;
            set
            {
                if (value != null && SetProperty(ref _selectedRange, value))
                    Reload();
            }
        }

        // Seçili şarj; değeri 0 ise süreye göre gösterilir.
        public Option SelectedBatch
        {
            get => _selectedBatch;
            set
            {
                if (value != null && SetProperty(ref _selectedBatch, value))
                    Reload();
            }
        }

        // Sağ eksende gösterilen ölçüm grubu; değeri 0 ise sağ eksen boştur.
        public Option SelectedRightGroup
        {
            get => _selectedRightGroup;
            set
            {
                if (value != null && SetProperty(ref _selectedRightGroup, value))
                    Reload();
            }
        }

        public bool FollowLive { get => _followLive; set => SetProperty(ref _followLive, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string CursorText { get => _cursorText; private set => SetProperty(ref _cursorText, value); }
        public string RightAxisLabel { get => _rightAxisLabel; private set => SetProperty(ref _rightAxisLabel, value); }

        // Seçenek listelerini kurar, şarj listesini ve ilk grafiği ister, canlı izleme zamanlayıcısını başlatır.
        public TrendViewModel(ApiClient api)
        {
            _api = api;

            Ranges.Add(new Option(1, "Son 1 saat"));
            Ranges.Add(new Option(4, "Son 4 saat"));
            Ranges.Add(new Option(12, "Son 12 saat"));
            Ranges.Add(new Option(24, "Son 24 saat"));
            Ranges.Add(new Option(168, "Son 7 gün"));
            _selectedRange = Ranges[1];

            Batches.Add(new Option(0, "Şarj seçilmedi (süreye göre)"));
            _selectedBatch = Batches[0];

            RightGroups.Add(new Option(0, "Boş"));
            RightGroups.Add(new Option(1, "Basınç (mbar)"));
            RightGroups.Add(new Option(2, "Gaz debileri (L/h)"));
            RightGroups.Add(new Option(3, "Yüzdeler: H₂, ısıtma, fan (%)"));
            RightGroups.Add(new Option(4, "Kn"));
            RightGroups.Add(new Option(5, "Adım numarası"));
            _selectedRightGroup = RightGroups[0];

            foreach (var def in Tags.Where(t => t.Group == 0))
                TempTags.Add(new TrendTagItem(def.Tag, def.Name, ToBrush(def.Color), def.Tag is "TempPota" or "TempSet", Reload));

            RefreshCommand = new AsyncRelayCommand(LoadAsync);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RefreshSeconds) };
            _timer.Tick += (_, _) => OnTimerTick();
            _timer.Start();

            _ready = true;
            _ = LoadBatchesAsync();
            _ = LoadAsync();
        }

        // Zamanlayıcıyı durdurur.
        public void Dispose()
        {
            _timer.Stop();
        }

        // Fare altındaki zamana en yakın kaydın değerlerini göstergeye yazar; o kaydın zamanını döner, veri yoksa null döner.
        public double? ShowValuesAt(double x)
        {
            if (_times.Length == 0)
                return null;

            int index = Array.BinarySearch(_times, x);
            if (index < 0)
            {
                index = ~index;
                if (index >= _times.Length)
                    index = _times.Length - 1;
                else if (index > 0 && x - _times[index - 1] < _times[index] - x)
                    index--;
            }

            foreach (var series in Series)
            {
                series.ValueText = index < series.Y.Length
                    ? (series.Y[index].ToString(series.Format, Tr) + " " + series.Unit).Trim()
                    : string.Empty;
            }

            CursorText = DateTime.FromOADate(_times[index]).ToString("dd.MM.yyyy HH:mm:ss", Tr);
            return _times[index];
        }

        // Fare grafikten çıkınca göstergedeki değerleri temizler.
        public void ClearValues()
        {
            foreach (var series in Series)
                series.ValueText = string.Empty;

            CursorText = string.Empty;
        }

        // Canlı izleme açıksa ve süreye göre bakılıyorsa grafiği yeniler.
        private void OnTimerTick()
        {
            if (FollowLive && SelectedBatch.Value == 0 && !_loading)
                _ = LoadAsync();
        }

        // Bir seçim değişince grafiği yeniden yükler (kurulum sırasında çalışmaz).
        private void Reload()
        {
            if (_ready)
                _ = LoadAsync();
        }

        // Son 30 günün şarjlarını şarj seçme listesine ekler.
        private async Task LoadBatchesAsync()
        {
            var result = await _api.GetAsync<List<BatchListDto>>("api/batches");
            if (!result.IsSuccess || result.Data == null)
                return;

            foreach (var batch in result.Data.OrderByDescending(b => b.StartTime))
                Batches.Add(new Option(batch.Id, $"{batch.BatchCode} · {batch.RecipeName} · {batch.StartTime.ToString("dd.MM HH:mm", Tr)}"));
        }

        // Seçili ölçümleri seçili aralık ya da şarj için sunucudan okur ve serileri hazırlar.
        private async Task LoadAsync()
        {
            int request = ++_request;
            int rightGroup = SelectedRightGroup.Value;

            var defs = Tags
                .Where(t => t.Group == 0
                    ? TempTags.Any(item => item.Tag == t.Tag && item.IsChecked)
                    : t.Group == rightGroup)
                .ToList();

            if (defs.Count == 0)
            {
                ShowSeries(new List<TrendSeriesView>(), Array.Empty<double>(), rightGroup);
                Message = "En az bir ölçüm seçin.";
                return;
            }

            string tags = string.Join(",", defs.Select(d => d.Tag));
            string path;

            if (SelectedBatch.Value > 0)
            {
                path = $"api/trends/batch/{SelectedBatch.Value}?tags={tags}&points={Points}";
            }
            else
            {
                DateTime end = DateTime.Now;
                DateTime start = end.AddHours(-SelectedRange.Value);
                path = $"api/trends?tags={tags}&start={Uri.EscapeDataString(start.ToString("s"))}&end={Uri.EscapeDataString(end.ToString("s"))}&points={Points}";
            }

            _loading = true;
            var result = await _api.GetAsync<TrendDataDto>(path);
            _loading = false;

            if (request != _request)
                return;

            if (!result.IsSuccess || result.Data == null)
            {
                Message = result.Message ?? "Trend verisi alınamadı.";
                return;
            }

            var data = result.Data;

            // Bütün diziler aynı uzunluğa indirilir.
            int count = data.Timestamps.Count;
            foreach (var series in data.Series)
                count = Math.Min(count, Math.Min(series.Values.Count, series.Max.Count));

            double[] times = data.Timestamps.Take(count).Select(t => t.ToOADate()).ToArray();
            var views = new List<TrendSeriesView>();

            foreach (var def in defs)
            {
                var series = data.Series.FirstOrDefault(s => string.Equals(s.Tag, def.Tag, StringComparison.OrdinalIgnoreCase));
                if (series == null)
                    continue;

                // Adım numarasında ortalama küsuratlı çıkar; dilimin en yüksek değeri kullanılır.
                var values = def.Tag == "ActiveStep" ? series.Max : series.Values;

                views.Add(new TrendSeriesView
                {
                    Tag = def.Tag,
                    Name = def.Name,
                    Unit = def.Unit,
                    ColorHex = def.Color,
                    Swatch = ToBrush(def.Color),
                    IsRight = def.Group != 0,
                    IsDashed = def.Dashed,
                    Format = def.Format,
                    X = times,
                    Y = values.Take(count).Select(v => (double)v).ToArray()
                });
            }

            ShowSeries(views, times, rightGroup);

            Message = count == 0
                ? "Bu aralıkta kayıt yok."
                : $"{count} nokta · her nokta {data.BucketSeconds} saniyenin ortalaması · {DateTime.Now:HH:mm:ss}";
        }

        // Hazırlanan serileri yayınlar ve görünüme grafiği yeniden çizdirir.
        private void ShowSeries(List<TrendSeriesView> views, double[] times, int rightGroup)
        {
            _times = times;

            Series.Clear();
            foreach (var view in views)
                Series.Add(view);

            RightAxisLabel = rightGroup == 0 ? string.Empty : RightGroups.First(g => g.Value == rightGroup).Text;
            CursorText = string.Empty;
            DataChanged?.Invoke();
        }

        // Renk kodundan dondurulmuş fırça üretir.
        private static Brush ToBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        // Bir ölçümün sunucudaki adı, ekrandaki adı, birimi, rengi, çizgi tipi, grubu ve sayı biçimi
        private sealed record TagDef(string Tag, string Name, string Unit, string Color, bool Dashed, int Group, string Format);
    }
}