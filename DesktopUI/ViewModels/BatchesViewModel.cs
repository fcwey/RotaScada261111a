using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Controls;
using DesktopUI.Services;
using Entities.Concrete;
using Entities.Dtos;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace DesktopUI.ViewModels
{
    // Şarj listesinin bir satırı
    public class BatchRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Recipe { get; set; } = string.Empty;
        public string TimeText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string Level { get; set; } = "Off";      // On, Warn, Fault, Info, Off
    }

    // Adım özetleri tablosunun bir satırı
    public class StepSummaryRow
    {
        public string StepNo { get; set; } = string.Empty;
        public string TypeText { get; set; } = string.Empty;
        public string StateText { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string GsoakWait { get; set; } = string.Empty;
        public string Hold { get; set; } = string.Empty;
        public string Temp { get; set; } = string.Empty;
        public string Kn { get; set; } = string.Empty;
        public string H2 { get; set; } = string.Empty;
        public string N2 { get; set; } = string.Empty;
        public string NH3 { get; set; } = string.Empty;
        public string CO2 { get; set; } = string.Empty;
        public string Energy { get; set; } = string.Empty;
    }

    // Şarjın planını değiştiren bir olayın satırı
    public class EventRow
    {
        public string Time { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    // Şarjlar sayfası: geçmiş şarjların listesi ve seçilen şarjın özeti, grafiği, adım özetleri, notları, olayları ve alarmları
    public class BatchesViewModel : ObservableObject
    {
        private const float MaxLoadKg = 100000f;
        private const int ChartPoints = 500;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly bool _canOperate;
        private readonly Dictionary<string, string> _values = new();

        private List<BatchListDto> _all = new();
        private bool _fillingList;
        private int _listRequest;
        private Option _selectedRange;
        private BatchRow? _selectedBatch;
        private string _searchText = string.Empty;
        private string _listMessage = string.Empty;
        private bool _hasDetail;
        private string _loadKgText = string.Empty;
        private string _newNote = string.Empty;
        private string _infoMessage = string.Empty;
        private ProfileChartData? _chartData;

        public ObservableCollection<Option> Ranges { get; } = new();
        public ObservableCollection<BatchRow> Batches { get; } = new();
        public ObservableCollection<StepSummaryRow> Steps { get; } = new();
        public ObservableCollection<EventRow> Events { get; } = new();
        public ObservableCollection<AlarmRow> Alarms { get; } = new();
        public ObservableCollection<NoteRow> Notes { get; } = new();

        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand SaveLoadCommand { get; }
        public IAsyncRelayCommand AddNoteCommand { get; }

        // Ekranın adla okuduğu özet metinleri; olmayan ad boş döner.
        public string this[string key] => _values.TryGetValue(key, out var value) ? value : string.Empty;

        // Listelenen tarih aralığı; değişince liste yeniden okunur.
        public Option SelectedRange
        {
            get => _selectedRange;
            set
            {
                if (value != null && SetProperty(ref _selectedRange, value))
                    _ = LoadListAsync();
            }
        }

        // Listede seçili şarj; değişince ayrıntısı okunur.
        public BatchRow? SelectedBatch
        {
            get => _selectedBatch;
            set
            {
                if (_fillingList)
                    return;

                if (SetProperty(ref _selectedBatch, value))
                    _ = LoadDetailAsync();
            }
        }

        // Şarj kodu ya da reçete adında aranan metin
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        public string ListMessage { get => _listMessage; private set => SetProperty(ref _listMessage, value); }
        public bool HasDetail { get => _hasDetail; private set => SetProperty(ref _hasDetail, value); }
        public string LoadKgText { get => _loadKgText; set => SetProperty(ref _loadKgText, value); }
        public string InfoMessage { get => _infoMessage; private set => SetProperty(ref _infoMessage, value); }
        public ProfileChartData? ChartData { get => _chartData; private set => SetProperty(ref _chartData, value); }

        // Yazılmakta olan not; boşken Ekle düğmesi kapalıdır.
        public string NewNote
        {
            get => _newNote;
            set
            {
                if (SetProperty(ref _newNote, value))
                    AddNoteCommand.NotifyCanExecuteChanged();
            }
        }

        // Bağımlılıkları alır, komutları kurar ve son 30 günün şarjlarını ister.
        public BatchesViewModel(ApiClient api, SessionService session)
        {
            _api = api;
            _canOperate = session.HasRole(PlcPermissions.Operate);

            Ranges.Add(new Option(30, "Son 30 gün"));
            Ranges.Add(new Option(90, "Son 90 gün"));
            Ranges.Add(new Option(365, "Son 1 yıl"));
            _selectedRange = Ranges[0];

            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            SaveLoadCommand = new AsyncRelayCommand(SaveLoadAsync, () => _canOperate && HasDetail);
            AddNoteCommand = new AsyncRelayCommand(AddNoteAsync, () => _canOperate && HasDetail && !string.IsNullOrWhiteSpace(NewNote));

            Set("Empty", "Soldaki listeden bir şarj seçin.");
            _ = LoadListAsync();
        }

        // Listeyi ve seçili şarjın ayrıntısını yeniden okur.
        private async Task RefreshAsync()
        {
            await LoadListAsync();
            await LoadDetailAsync();
        }

        // Seçili tarih aralığında başlamış şarjları sunucudan okur.
        private async Task LoadListAsync()
        {
            int request = ++_listRequest;
            DateTime end = DateTime.Now.AddMinutes(1);
            DateTime start = end.AddDays(-SelectedRange.Value);

            ListMessage = "Yükleniyor…";
            var result = await _api.GetAsync<List<BatchListDto>>(
                $"api/batches?start={Uri.EscapeDataString(start.ToString("s"))}&end={Uri.EscapeDataString(end.ToString("s"))}");

            if (request != _listRequest)
                return;

            if (!result.IsSuccess || result.Data == null)
            {
                ListMessage = result.Message ?? "Şarj listesi alınamadı.";
                return;
            }

            _all = result.Data.OrderByDescending(b => b.StartTime).ToList();
            ApplyFilter();
        }

        // Arama metnine uyan şarjları listeye yerleştirir; seçim korunur, seçili şarj listede kalmadıysa ilk satır seçilir.
        private void ApplyFilter()
        {
            int? selectedId = _selectedBatch?.Id;
            string search = (SearchText ?? string.Empty).Trim();

            _fillingList = true;
            Batches.Clear();

            foreach (var batch in _all)
            {
                if (search.Length == 0 || Contains(batch.BatchCode, search) || Contains(batch.RecipeName, search))
                    Batches.Add(ToRow(batch));
            }

            _fillingList = false;

            _selectedBatch = Batches.FirstOrDefault(b => b.Id == selectedId) ?? Batches.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedBatch));

            ListMessage = Batches.Count == 0
                ? (_all.Count == 0 ? "Bu aralıkta şarj yok." : "Aramaya uyan şarj yok.")
                : $"{Batches.Count} şarj";

            if (_selectedBatch?.Id != selectedId)
                _ = LoadDetailAsync();
        }

        // Seçili şarjın özetini, ağırlık ve notlarını, sıcaklık trendini okuyup ekrana yerleştirir.
        private async Task LoadDetailAsync()
        {
            var row = _selectedBatch;
            if (row == null)
            {
                ClearDetail("Soldaki listeden bir şarj seçin.");
                return;
            }

            int id = row.Id;
            var detailTask = _api.GetAsync<BatchDetailDto>($"api/batches/{id}");
            var infoTask = _api.GetAsync<BatchInfoDto>($"api/batches/{id}/info");
            var trendTask = _api.GetAsync<TrendDataDto>($"api/trends/batch/{id}?tags=TempPota,TempSet&points={ChartPoints}");
            await Task.WhenAll(detailTask, infoTask, trendTask);

            if (_selectedBatch?.Id != id)
                return;

            var detail = detailTask.Result;
            if (!detail.IsSuccess || detail.Data == null)
            {
                ClearDetail(detail.Message ?? "Şarj ayrıntısı alınamadı.");
                return;
            }

            ShowSummary(detail.Data.Batch);
            ShowSteps(detail.Data.Steps);
            ShowEvents(detail.Data.Revisions);
            ShowAlarms(detail.Data.Alarms);
            ShowInfo(infoTask.Result);

            ChartData = BuildChart(trendTask.Result.Data);
            Set("ChartNote", ChartData == null ? "Bu şarj için kayıtlı ölçüm yok." : "Yatay eksen: şarjın başlangıcından itibaren saat");

            NewNote = string.Empty;
            HasDetail = true;
            Publish();
            RefreshCommands();
        }

        // Ayrıntı alanını boşaltır ve yerine verilen açıklamayı gösterir.
        private void ClearDetail(string message)
        {
            HasDetail = false;
            Steps.Clear();
            Events.Clear();
            Alarms.Clear();
            Notes.Clear();
            ChartData = null;
            Set("Empty", message);
            Publish();
            RefreshCommands();
        }

        // Şarjın özet değerlerini (zamanlar, sonuç, operatör, toplamlar) ekran metinlerine çevirir.
        private void ShowSummary(BatchListDto b)
        {
            var result = DescribeResult(b);
            DateTime? end = b.ProgramEndTime ?? b.EndTime;

            Set("Code", b.BatchCode);
            Set("Recipe", b.RecipeName);
            Set("Result", result.Text);
            Set("ResultLevel", result.Level);
            Set("Start", b.StartTime.ToString("dd.MM.yyyy HH:mm", Tr));
            Set("End", end.HasValue ? end.Value.ToString("dd.MM.yyyy HH:mm", Tr) : "sürüyor");
            Set("Duration", FormatDuration((end ?? DateTime.Now) - b.StartTime));
            Set("Operator", string.IsNullOrEmpty(b.OperatorEmail) ? "—" : b.OperatorEmail);
            Set("Energy", $"{b.EnergyKwh.ToString("0.0", Tr)} kWh");
            Set("N2", $"{b.N2TotalL.ToString("0", Tr)} L");
            Set("NH3", $"{b.NH3TotalL.ToString("0", Tr)} L");
            Set("CO2", $"{b.CO2TotalL.ToString("0", Tr)} L");
            Set("Water", $"{b.WaterTotalL.ToString("0.0", Tr)} L");
            Set("FillN2", $"{b.FillN2TotalL.ToString("0", Tr)} L");
            Set("EmgN2", $"{b.EmgN2TotalL.ToString("0", Tr)} L");
            Set("Note", b.CompletionNote ?? string.Empty);
        }

        // PLC'nin adım bazında tuttuğu gerçekleşen değerleri tabloya yerleştirir.
        private void ShowSteps(List<BatchStepSummary> steps)
        {
            Steps.Clear();

            foreach (var s in steps.OrderBy(s => s.Id))
            {
                bool heating = s.StepType == 2;
                bool knApprox = (s.Flags & 2) != 0;

                Steps.Add(new StepSummaryRow
                {
                    StepNo = s.StepNo.ToString(),
                    TypeText = s.StepType switch { 1 => "Vakum", 2 => "Isıtma", 3 => "Soğutma", _ => "—" },
                    StateText = (s.Flags & 8) != 0 ? "sürüyor" : (s.Flags & 1) != 0 ? "tamamlandı" : "yarıda kaldı",
                    Duration = s.DurationMin.ToString(),
                    GsoakWait = heating ? s.GsoakWaitMin.ToString() : "—",
                    Hold = s.HoldMin.ToString(),
                    Temp = $"{s.TempAvg.ToString("0", Tr)} ({s.TempMin.ToString("0", Tr)}–{s.TempMax.ToString("0", Tr)})",
                    Kn = heating && s.KnMax > 0 ? (knApprox ? "≈ " : string.Empty) + s.KnAvg.ToString("0.00", Tr) : "—",
                    H2 = heating ? "%" + s.H2Avg.ToString("0.0", Tr) : "—",
                    N2 = s.N2L.ToString("0", Tr),
                    NH3 = s.NH3L.ToString("0", Tr),
                    CO2 = s.CO2L.ToString("0", Tr),
                    Energy = s.EnergyKwh.ToString("0.0", Tr)
                });
            }

            Set("StepsNote", Steps.Count == 0 ? "Bu şarj için adım özeti kaydedilmemiş." : string.Empty);
        }

        // Şarjın planını değiştiren olayları (başlatma, değişiklik, adıma gitme, soğutmaya geçme) listeler.
        private void ShowEvents(List<BatchRevisionDto> revisions)
        {
            Events.Clear();

            foreach (var r in revisions.OrderBy(r => r.Timestamp))
            {
                string text = r.Reason switch
                {
                    "START" => "Program başlatıldı",
                    "EDIT" => "Reçete çalışırken değiştirildi",
                    "GOTO" => $"Adım {r.StepNo}, {r.StepMinute}. dakikaya gidildi",
                    "SKIP" => "Soğutmaya geçildi",
                    _ => r.Reason
                };

                if (!string.IsNullOrEmpty(r.UserEmail))
                    text += $" · {r.UserEmail}";

                Events.Add(new EventRow { Time = r.Timestamp.ToString("dd.MM HH:mm", Tr), Text = text });
            }
        }

        // Şarj sırasında oluşan alarmları eskiden yeniye listeler.
        private void ShowAlarms(List<AlarmHistoryDto> alarms)
        {
            Alarms.Clear();

            foreach (var a in alarms.OrderBy(a => a.TriggeredAt))
            {
                Alarms.Add(new AlarmRow
                {
                    Time = a.TriggeredAt.ToString("dd.MM HH:mm:ss", Tr),
                    Tag = a.TagName,
                    Description = a.Description,
                    ClassText = a.PriorityLevel >= 3 ? "Ciddi" : a.PriorityLevel == 2 ? "Uyarı" : "Bilgi",
                    Level = a.PriorityLevel >= 3 ? "Bad" : a.PriorityLevel == 2 ? "Warn" : "Info"
                });
            }

            Set("AlarmsNote", Alarms.Count == 0 ? "Şarj sırasında alarm oluşmamış." : string.Empty);
        }

        // Şarj ağırlığını ve notları ekrana yerleştirir; okunamadıysa sebebini gösterir.
        private void ShowInfo(ApiResult<BatchInfoDto> result)
        {
            Notes.Clear();

            if (!result.IsSuccess || result.Data == null)
            {
                LoadKgText = string.Empty;
                InfoMessage = result.Message ?? "Ağırlık ve notlar alınamadı.";
                return;
            }

            LoadKgText = result.Data.LoadKg.HasValue ? result.Data.LoadKg.Value.ToString("0.#", Tr) : string.Empty;

            foreach (var note in result.Data.Notes.OrderByDescending(n => n.Timestamp))
                Notes.Add(new NoteRow { Header = $"{note.Timestamp:dd.MM.yyyy HH:mm} · {note.UserEmail}", Text = note.Text });

            InfoMessage = _canOperate ? string.Empty : "Ağırlık ve not değiştirmek için operatör yetkisi gerekir.";
        }

        // Girilen şarj ağırlığını denetleyip sunucuya yazar.
        private async Task SaveLoadAsync()
        {
            var row = _selectedBatch;
            if (row == null)
                return;

            string text = (LoadKgText ?? string.Empty).Trim().Replace('.', ',');

            if (!float.TryParse(text, NumberStyles.Float, Tr, out float kg) || kg <= 0 || kg > MaxLoadKg)
            {
                InfoMessage = $"Şarj ağırlığı 0'dan büyük, en fazla {MaxLoadKg:0} kg olan bir sayı olmalı.";
                return;
            }

            var result = await _api.PutAsync<JsonElement>($"api/batches/{row.Id}/load?kg={kg.ToString(CultureInfo.InvariantCulture)}");
            InfoMessage = result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  Ağırlık kaydedildi."
                : result.Message ?? "Ağırlık kaydedilemedi.";
        }

        // Yazılan notu seçili şarja ekler ve not listesini yeniler.
        private async Task AddNoteAsync()
        {
            var row = _selectedBatch;
            if (row == null)
                return;

            var result = await _api.PostAsync<BatchNoteDto>($"api/batches/{row.Id}/notes", new BatchNoteRequestDto { Text = NewNote });
            if (!result.IsSuccess)
            {
                InfoMessage = result.Message ?? "Not eklenemedi.";
                return;
            }

            NewNote = string.Empty;

            var info = await _api.GetAsync<BatchInfoDto>($"api/batches/{row.Id}/info");
            if (_selectedBatch?.Id == row.Id)
                ShowInfo(info);
        }

        // Şarjın saat zamanına göre sıcaklık grafiğini üretir: set kesikli, ölçülen dolu çizgi. Veri yoksa null döner.
        private static ProfileChartData? BuildChart(TrendDataDto? trend)
        {
            if (trend == null || trend.Timestamps.Count < 2)
                return null;

            var measured = trend.Series.FirstOrDefault(s => s.Tag == "TempPota");
            var set = trend.Series.FirstOrDefault(s => s.Tag == "TempSet");

            var actual = new List<Point>();
            var theory = new List<Point>();
            double maxTemp = 0;
            double maxX = 0;

            for (int i = 0; i < trend.Timestamps.Count; i++)
            {
                double x = Math.Max(0, (trend.Timestamps[i] - trend.Start).TotalHours);
                maxX = Math.Max(maxX, x);

                if (measured != null && i < measured.Values.Count)
                {
                    actual.Add(new Point(x, measured.Values[i]));
                    maxTemp = Math.Max(maxTemp, measured.Values[i]);
                }

                if (set != null && i < set.Values.Count)
                {
                    theory.Add(new Point(x, set.Values[i]));
                    maxTemp = Math.Max(maxTemp, set.Values[i]);
                }
            }

            var data = new ProfileChartData
            {
                Actual = actual,
                XMax = Math.Max(maxX, 0.05),
                YMax = Math.Max(150, Math.Ceiling(maxTemp * 1.15 / 150.0) * 150)
            };
            data.TickEnd = data.XMax;

            if (theory.Count > 1)
                data.Theory.Add(theory);

            return data;
        }

        // Şarj kaydını liste satırına çevirir.
        private static BatchRow ToRow(BatchListDto b)
        {
            var result = DescribeResult(b);

            return new BatchRow
            {
                Id = b.Id,
                Code = b.BatchCode,
                Recipe = b.RecipeName,
                TimeText = b.StartTime.ToString("dd.MM.yyyy HH:mm", Tr),
                StatusText = result.Text,
                Level = result.Level
            };
        }

        // Şarjın nasıl bittiğini metne ve renk seviyesine çevirir.
        private static (string Text, string Level) DescribeResult(BatchListDto b)
        {
            return b.EndReason switch
            {
                1 => ("Tamamlandı", "On"),
                2 => ("Stop", "Warn"),
                3 => ("Güvenli durdurma", "Fault"),
                _ => b.Status switch
                {
                    "RUNNING" or "STARTING" => ("Sürüyor", "Info"),
                    "COMPLETED" => ("Tamamlandı", "On"),
                    "ABORTED" => ("Yarıda kesildi", "Warn"),
                    _ => ("Bilinmiyor", "Off")
                }
            };
        }

        // Süreyi "5 sa 12 dk" biçiminde yazar.
        private static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;

            int hours = (int)span.TotalHours;
            return hours > 0 ? $"{hours} sa {span.Minutes} dk" : $"{span.Minutes} dk";
        }

        // Metnin içinde aranan ifadenin büyük-küçük harf ayırmadan geçip geçmediğini döner.
        private static bool Contains(string? text, string search)
        {
            return !string.IsNullOrEmpty(text) && Tr.CompareInfo.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0;
        }

        // Düğmelerin etkin/pasif durumunu yeniden hesaplatır.
        private void RefreshCommands()
        {
            SaveLoadCommand.NotifyCanExecuteChanged();
            AddNoteCommand.NotifyCanExecuteChanged();
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