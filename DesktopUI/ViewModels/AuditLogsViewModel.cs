using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopUI.Services;
using Entities.Dtos;
using System.Collections.ObjectModel;
using System.Globalization;

namespace DesktopUI.ViewModels
{
    // İşlem kayıtları tablosunun bir satırı
    public class AuditRow
    {
        public string Time { get; set; } = string.Empty;
        public string TypeText { get; set; } = string.Empty;
        public string Level { get; set; } = "Info";     // Info, Warn, Bad
        public string Description { get; set; } = string.Empty;
        public string Change { get; set; } = string.Empty;
        public string ClientIp { get; set; } = string.Empty;
    }

    // İşlem kayıtları sekmesi: kim, ne zaman, ne yaptı; tarih, tür ve metne göre süzülür
    public class AuditLogsViewModel : ObservableObject
    {
        private const int MaxRows = 3000;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // İşlem tiplerinin ekranda gösterilen adları
        private static readonly Dictionary<string, string> TypeNames = new()
        {
            ["PLC_COMMAND"] = "PLC komutu",
            ["PLC_COMMAND_REJECTED"] = "PLC komutu reddedildi",
            ["PLC_COMMAND_DENIED"] = "Yetkisiz komut denemesi",
            ["PLC_OVERRIDE"] = "Aşma komutu",
            ["PLC_OVERRIDE_REJECTED"] = "Aşma komutu reddedildi",
            ["PARAMETER_UPDATE"] = "Parametre değişti",
            ["PARAMETER_UPDATE_FAILED"] = "Parametre yazılamadı",
            ["PARAMETER_UPDATE_DENIED"] = "Yetkisiz parametre denemesi",
            ["PARAMETER_BACKUP"] = "Parametre yedeği",
            ["PARAMETER_RESTORE"] = "Yedek geri yüklendi",
            ["PARAMETER_RESTORE_FAILED"] = "Yedek geri yüklenemedi",
            ["PARAMETER_RESTORE_DENIED"] = "Yetkisiz geri yükleme denemesi",
            ["USER_CREATE"] = "Kullanıcı açıldı",
            ["USER_ROLES_UPDATE"] = "Roller değişti",
            ["USER_STATUS_UPDATE"] = "Hesap açıldı ya da kapatıldı",
            ["USER_PASSWORD_RESET"] = "Şifre değişti",
            ["PID_TUNE_OK"] = "PID ayarı başarılı",
            ["PID_TUNE_FAILED"] = "PID ayarı başarısız"
        };

        private readonly ApiClient _api;

        private List<AuditLogDto> _all = new();
        private bool _ready;
        private int _request;
        private Option _selectedRange;
        private Option _selectedGroup;
        private string _searchText = string.Empty;
        private string _summary = string.Empty;

        public ObservableCollection<AuditRow> Rows { get; } = new();
        public ObservableCollection<Option> Ranges { get; } = new();
        public ObservableCollection<Option> Groups { get; } = new();

        public IAsyncRelayCommand RefreshCommand { get; }

        // Gösterilen süre (saat); değişince kayıtlar yeniden okunur.
        public Option SelectedRange
        {
            get => _selectedRange;
            set
            {
                if (value != null && SetProperty(ref _selectedRange, value) && _ready)
                    _ = LoadAsync();
            }
        }

        // Gösterilen işlem grubu; değeri 0 ise hepsi gösterilir.
        public Option SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (value != null && SetProperty(ref _selectedGroup, value))
                    ShowRows();
            }
        }

        // Açıklamada, işlem tipinde ya da değerlerde aranan metin
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ShowRows();
            }
        }

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

        // Seçenek listelerini kurar ve son 24 saatin kayıtlarını ister.
        public AuditLogsViewModel(ApiClient api)
        {
            _api = api;

            Ranges.Add(new Option(24, "Son 24 saat"));
            Ranges.Add(new Option(168, "Son 7 gün"));
            Ranges.Add(new Option(720, "Son 30 gün"));
            _selectedRange = Ranges[0];

            Groups.Add(new Option(0, "Tüm işlemler"));
            Groups.Add(new Option(1, "PLC komutları"));
            Groups.Add(new Option(2, "Aşma komutları"));
            Groups.Add(new Option(3, "Parametreler ve yedekler"));
            Groups.Add(new Option(4, "Kullanıcı işlemleri"));
            Groups.Add(new Option(5, "Reddedilen ve yetkisiz denemeler"));
            _selectedGroup = Groups[0];

            RefreshCommand = new AsyncRelayCommand(LoadAsync);

            _ready = true;
            _ = LoadAsync();
        }

        // Seçili süredeki işlem kayıtlarını sunucudan okur.
        private async Task LoadAsync()
        {
            int request = ++_request;
            DateTime end = DateTime.Now.AddMinutes(1);
            DateTime start = end.AddHours(-SelectedRange.Value);

            Summary = "Yükleniyor…";
            var result = await _api.GetAsync<List<AuditLogDto>>(
                $"api/auditlogs?start={Uri.EscapeDataString(start.ToString("s"))}&end={Uri.EscapeDataString(end.ToString("s"))}");

            if (request != _request)
                return;

            if (!result.IsSuccess || result.Data == null)
            {
                Summary = result.Message ?? "İşlem kayıtları alınamadı.";
                return;
            }

            _all = result.Data.OrderByDescending(l => l.Timestamp).ToList();
            ShowRows();
        }

        // Gruba ve arama metnine uyan kayıtları en yenisi üstte olacak şekilde tabloya yerleştirir.
        private void ShowRows()
        {
            string search = (SearchText ?? string.Empty).Trim();
            int group = SelectedGroup.Value;
            int matched = 0;

            Rows.Clear();

            foreach (var log in _all)
            {
                if (!InGroup(log.ActionType, group))
                    continue;

                if (search.Length > 0 && !Contains(log.Description, search) && !Contains(log.ActionType, search)
                    && !Contains(log.OldValue, search) && !Contains(log.NewValue, search))
                    continue;

                matched++;
                if (Rows.Count >= MaxRows)
                    continue;

                Rows.Add(new AuditRow
                {
                    Time = log.Timestamp.ToString("dd.MM.yyyy HH:mm:ss", Tr),
                    TypeText = TypeNames.TryGetValue(log.ActionType, out var name) ? name : log.ActionType,
                    Level = DescribeLevel(log.ActionType),
                    Description = log.Description,
                    Change = DescribeChange(log.OldValue, log.NewValue),
                    ClientIp = log.ClientIp
                });
            }

            Summary = matched > MaxRows
                ? $"{matched} kayıt; en yeni {MaxRows} tanesi gösteriliyor. Süreyi daraltın ya da arama yapın."
                : $"{matched} kayıt";
        }

        // İşlem tipinin seçili gruba girip girmediğini döner.
        private static bool InGroup(string actionType, int group)
        {
            bool refused = actionType.EndsWith("_REJECTED") || actionType.EndsWith("_DENIED") || actionType.EndsWith("_FAILED");

            return group switch
            {
                1 => actionType.StartsWith("PLC_COMMAND"),
                2 => actionType.StartsWith("PLC_OVERRIDE"),
                3 => actionType.StartsWith("PARAMETER_") || actionType.StartsWith("PID_"),
                4 => actionType.StartsWith("USER_"),
                5 => refused,
                _ => true
            };
        }

        // İşlem tipini satırın renk seviyesine çevirir: aşma kırmızı, reddedilen ve yetkisiz sarı, diğerleri mavi.
        private static string DescribeLevel(string actionType)
        {
            if (actionType == "PLC_OVERRIDE")
                return "Bad";

            if (actionType.EndsWith("_REJECTED") || actionType.EndsWith("_DENIED") || actionType.EndsWith("_FAILED"))
                return "Warn";

            return "Info";
        }

        // Eski ve yeni değeri tek satırda yazar; ikisi de boşsa boş döner.
        private static string DescribeChange(string? oldValue, string? newValue)
        {
            bool hasOld = !string.IsNullOrWhiteSpace(oldValue);
            bool hasNew = !string.IsNullOrWhiteSpace(newValue);

            if (hasOld && hasNew) return $"{oldValue} → {newValue}";
            if (hasNew) return newValue!;
            if (hasOld) return $"önceki: {oldValue}";
            return string.Empty;
        }

        // Metnin içinde aranan ifadenin büyük-küçük harf ayırmadan geçip geçmediğini döner.
        private static bool Contains(string? text, string search)
        {
            return !string.IsNullOrEmpty(text) && Tr.CompareInfo.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0;
        }
    }
}