using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopUI.Services;
using Entities.Dtos;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace DesktopUI.ViewModels
{
    // Aktif alarm tablosunun bir satırı
    public class ActiveAlarmRow
    {
        public int Id { get; set; }
        public string Time { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ClassText { get; set; } = string.Empty;
        public string Level { get; set; } = "Warn";     // Info, Warn, Bad
        public bool IsAcknowledged { get; set; }
        public string StateText { get; set; } = string.Empty;
    }

    // Alarm geçmişi tablosunun bir satırı
    public class AlarmHistoryRow
    {
        public string Triggered { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ClassText { get; set; } = string.Empty;
        public string Level { get; set; } = "Warn";
        public string Acknowledged { get; set; } = string.Empty;
        public string Resolved { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    // Alarmlar sayfası: aktif alarmların izlenmesi ve onaylanması, geçmiş alarmların süzülerek listelenmesi
    public class AlarmsViewModel : ObservableObject, IDisposable
    {
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly ApiClient _api;
        private readonly LiveDataService _live;

        private List<AlarmHistoryDto> _history = new();
        private bool _ready;
        private int _historyRequest;
        private Option _selectedRange;
        private Option _selectedPriority;
        private string _searchText = string.Empty;
        private string _ackNote = string.Empty;
        private string _activeSummary = "yok";
        private string _historySummary = string.Empty;
        private string _message = string.Empty;

        public ObservableCollection<ActiveAlarmRow> ActiveAlarms { get; } = new();
        public ObservableCollection<AlarmHistoryRow> History { get; } = new();
        public ObservableCollection<Option> Ranges { get; } = new();
        public ObservableCollection<Option> Priorities { get; } = new();

        public IAsyncRelayCommand<ActiveAlarmRow> AcknowledgeCommand { get; }
        public IAsyncRelayCommand AcknowledgeAllCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }

        // Geçmişte gösterilen süre (saat); değişince geçmiş yeniden okunur.
        public Option SelectedRange
        {
            get => _selectedRange;
            set
            {
                if (value != null && SetProperty(ref _selectedRange, value) && _ready)
                    _ = LoadHistoryAsync();
            }
        }

        // Geçmişte gösterilen alarm sınıfı; değeri 0 ise hepsi gösterilir.
        public Option SelectedPriority
        {
            get => _selectedPriority;
            set
            {
                if (value != null && SetProperty(ref _selectedPriority, value) && _ready)
                    _ = LoadHistoryAsync();
            }
        }

        // Geçmişte alarm kodu ya da açıklamada aranan metin
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ShowHistory();
            }
        }

        // Onaylanan alarmlara eklenecek not; boş bırakılabilir.
        public string AckNote { get => _ackNote; set => SetProperty(ref _ackNote, value); }

        public string ActiveSummary { get => _activeSummary; private set => SetProperty(ref _activeSummary, value); }
        public string HistorySummary { get => _historySummary; private set => SetProperty(ref _historySummary, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }

        // Seçenek listelerini ve komutları kurar, canlı alarm olayına abone olur, aktif alarmları ve geçmişi ister.
        public AlarmsViewModel(ApiClient api, LiveDataService live)
        {
            _api = api;
            _live = live;

            Ranges.Add(new Option(24, "Son 24 saat"));
            Ranges.Add(new Option(168, "Son 7 gün"));
            Ranges.Add(new Option(720, "Son 30 gün"));
            Ranges.Add(new Option(2160, "Son 90 gün"));
            _selectedRange = Ranges[1];

            Priorities.Add(new Option(0, "Tüm sınıflar"));
            Priorities.Add(new Option(3, "Ciddi"));
            Priorities.Add(new Option(2, "Uyarı"));
            Priorities.Add(new Option(1, "Bilgi"));
            _selectedPriority = Priorities[0];

            AcknowledgeCommand = new AsyncRelayCommand<ActiveAlarmRow>(AcknowledgeAsync);
            AcknowledgeAllCommand = new AsyncRelayCommand(AcknowledgeAllAsync, () => ActiveAlarms.Any(a => !a.IsAcknowledged));
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);

            _live.AlarmsReceived += OnAlarmsReceived;

            _ready = true;
            _ = RefreshAsync();
        }

        // Canlı alarm olayı aboneliğini bırakır.
        public void Dispose()
        {
            _live.AlarmsReceived -= OnAlarmsReceived;
        }

        // Canlı gelen aktif alarm listesini tabloya yansıtır ve geçmişi tazeler (yeni oluşan ya da düzelen alarm geçmişte de görünsün).
        private void OnAlarmsReceived(List<ActiveAlarmDto> alarms)
        {
            ShowActive(alarms);
            _ = LoadHistoryAsync();
        }

        // Aktif alarmları ve geçmişi sunucudan yeniden okur.
        private async Task RefreshAsync()
        {
            await LoadActiveAsync();
            await LoadHistoryAsync();
        }

        // Aktif alarm listesini sunucudan okur (hub listeyi yalnızca değişiklikte gönderir).
        private async Task LoadActiveAsync()
        {
            var result = await _api.GetAsync<List<ActiveAlarmDto>>("api/alarms/active");
            if (result.IsSuccess && result.Data != null)
                ShowActive(result.Data);
            else
                Message = result.Message ?? "Aktif alarmlar alınamadı.";
        }

        // Aktif alarmları en yenisi üstte olacak şekilde tabloya yerleştirir.
        private void ShowActive(List<ActiveAlarmDto> alarms)
        {
            ActiveAlarms.Clear();

            foreach (var alarm in alarms.OrderByDescending(a => a.TriggeredAt))
            {
                ActiveAlarms.Add(new ActiveAlarmRow
                {
                    Id = alarm.Id,
                    Time = alarm.TriggeredAt.ToString("dd.MM.yyyy HH:mm:ss", Tr),
                    Tag = alarm.TagName,
                    Description = alarm.Description,
                    ClassText = DescribeClass(alarm.PriorityLevel),
                    Level = DescribeLevel(alarm.PriorityLevel),
                    IsAcknowledged = alarm.IsAcknowledged,
                    StateText = alarm.IsAcknowledged ? "onaylandı" : "onay bekliyor"
                });
            }

            int waiting = ActiveAlarms.Count(a => !a.IsAcknowledged);
            ActiveSummary = ActiveAlarms.Count == 0
                ? "yok"
                : waiting == 0 ? $"{ActiveAlarms.Count} aktif, hepsi onaylı" : $"{ActiveAlarms.Count} aktif, {waiting} onay bekliyor";

            AcknowledgeAllCommand.NotifyCanExecuteChanged();
        }

        // Seçili süre ve sınıfa göre geçmiş alarmları sunucudan okur.
        private async Task LoadHistoryAsync()
        {
            int request = ++_historyRequest;
            DateTime end = DateTime.Now.AddMinutes(1);
            DateTime start = end.AddHours(-SelectedRange.Value);

            string path = $"api/alarms/history?start={Uri.EscapeDataString(start.ToString("s"))}&end={Uri.EscapeDataString(end.ToString("s"))}";
            if (SelectedPriority.Value > 0)
                path += $"&priority={SelectedPriority.Value}";

            var result = await _api.GetAsync<List<AlarmHistoryDto>>(path);
            if (request != _historyRequest)
                return;

            if (!result.IsSuccess || result.Data == null)
            {
                HistorySummary = result.Message ?? "Alarm geçmişi alınamadı.";
                return;
            }

            _history = result.Data.OrderByDescending(a => a.TriggeredAt).ToList();
            ShowHistory();
        }

        // Arama metnine uyan geçmiş kayıtlarını tabloya yerleştirir.
        private void ShowHistory()
        {
            string search = (SearchText ?? string.Empty).Trim();
            History.Clear();

            foreach (var alarm in _history)
            {
                if (search.Length > 0 && !Contains(alarm.TagName, search) && !Contains(alarm.Description, search))
                    continue;

                History.Add(new AlarmHistoryRow
                {
                    Triggered = alarm.TriggeredAt.ToString("dd.MM.yyyy HH:mm:ss", Tr),
                    Tag = alarm.TagName,
                    Description = alarm.Description,
                    ClassText = DescribeClass(alarm.PriorityLevel),
                    Level = DescribeLevel(alarm.PriorityLevel),
                    Acknowledged = alarm.AcknowledgedAt.HasValue ? alarm.AcknowledgedAt.Value.ToString("dd.MM HH:mm:ss", Tr) : "—",
                    Resolved = alarm.ResolvedAt.HasValue ? alarm.ResolvedAt.Value.ToString("dd.MM HH:mm:ss", Tr) : "sürüyor",
                    Duration = alarm.ResolvedAt.HasValue ? FormatDuration(alarm.ResolvedAt.Value - alarm.TriggeredAt) : "—",
                    Note = alarm.UserNote ?? string.Empty
                });
            }

            HistorySummary = History.Count == _history.Count
                ? $"{History.Count} kayıt"
                : $"{History.Count} kayıt ({_history.Count} içinden)";
        }

        // Tek bir alarmı, yazılmışsa notuyla birlikte onaylar.
        private async Task AcknowledgeAsync(ActiveAlarmRow? row)
        {
            if (row == null || row.IsAcknowledged)
                return;

            string? error = await SendAcknowledgeAsync(row.Id);
            Message = error ?? $"{DateTime.Now:HH:mm:ss}  {row.Tag} onaylandı.";

            if (error == null)
                AckNote = string.Empty;

            await RefreshAsync();
        }

        // Onay bekleyen bütün aktif alarmları aynı notla onaylar.
        private async Task AcknowledgeAllAsync()
        {
            var waiting = ActiveAlarms.Where(a => !a.IsAcknowledged).ToList();
            int done = 0;
            string? lastError = null;

            foreach (var row in waiting)
            {
                string? error = await SendAcknowledgeAsync(row.Id);
                if (error == null)
                    done++;
                else
                    lastError = error;
            }

            Message = lastError == null
                ? $"{DateTime.Now:HH:mm:ss}  {done} alarm onaylandı."
                : $"{done} alarm onaylandı, {waiting.Count - done} alarm onaylanamadı: {lastError}";

            if (lastError == null)
                AckNote = string.Empty;

            await RefreshAsync();
        }

        // Onay isteğini sunucuya gönderir; başarılıysa null, değilse hata metnini döner.
        private async Task<string?> SendAcknowledgeAsync(int alarmId)
        {
            string path = $"api/alarms/acknowledge/{alarmId}";
            string note = (AckNote ?? string.Empty).Trim();
            if (note.Length > 0)
                path += $"?note={Uri.EscapeDataString(note)}";

            var result = await _api.PostAsync<JsonElement>(path);
            return result.IsSuccess ? null : result.Message ?? "Alarm onaylanamadı.";
        }

        // Öncelik seviyesini sınıf adına çevirir.
        private static string DescribeClass(int priority)
        {
            return priority >= 3 ? "Ciddi" : priority == 2 ? "Uyarı" : "Bilgi";
        }

        // Öncelik seviyesini satırın renk seviyesine çevirir.
        private static string DescribeLevel(int priority)
        {
            return priority >= 3 ? "Bad" : priority == 2 ? "Warn" : "Info";
        }

        // Süreyi "2 sa 5 dk" ya da "3 dk 10 sn" biçiminde yazar.
        private static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;

            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours} sa {span.Minutes} dk";

            return span.TotalMinutes >= 1 ? $"{span.Minutes} dk {span.Seconds} sn" : $"{span.Seconds} sn";
        }

        // Metnin içinde aranan ifadenin büyük-küçük harf ayırmadan geçip geçmediğini döner.
        private static bool Contains(string? text, string search)
        {
            return !string.IsNullOrEmpty(text) && Tr.CompareInfo.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0;
        }
    }
}