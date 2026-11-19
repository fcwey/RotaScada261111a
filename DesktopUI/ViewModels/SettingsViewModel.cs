using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Services;
using Entities.Dtos;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace DesktopUI.ViewModels
{
    // Parametre tablosunun bir satırı: tanım, PLC'deki değer ve düzenlenen metin
    public class ParamRow : ObservableObject
    {
        private int _value;
        private string _editText = string.Empty;

        public int Address { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string RangeText { get; set; } = string.Empty;
        public string DefaultText { get; set; } = string.Empty;
        public int Default { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public bool IsSafety { get; set; }
        public bool CanEdit { get; set; }

        // PLC'deki güncel değer
        public int Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(IsChanged));
                }
            }
        }

        // Kutuya yazılan, henüz PLC'ye gönderilmemiş değer
        public string EditText
        {
            get => _editText;
            set
            {
                if (SetProperty(ref _editText, value))
                    OnPropertyChanged(nameof(IsDirty));
            }
        }

        // Kutudaki değer PLC'dekinden farklıysa true döner.
        public bool IsDirty => (EditText ?? string.Empty).Trim() != Value.ToString();

        // PLC'deki değer varsayılandan farklıysa true döner.
        public bool IsChanged => Value != Default;
    }

    // Yedek listesinin bir satırı
    public class BackupRow
    {
        public int Id { get; set; }
        public string Time { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    // Ayarlar sayfası: PLC parametrelerinin (DB204) görüntülenmesi ve değiştirilmesi, parametre yedekleri
    public class SettingsViewModel : ObservableObject
    {
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Parametre gruplarının adları ve içerdikleri DB204 adresleri (kılavuzdaki sırayla)
        private static readonly (string Name, int[] Addresses)[] Groups =
        {
            ("Güvenlik sınırları", new[] { 2, 4, 6, 8, 10, 54, 78 }),
            ("H₂ kontrolü", new[] { 12, 14, 16, 18, 20 }),
            ("Yıkama, kapak ve korumalı bekleme", new[] { 22, 24, 26, 28, 94, 90, 88, 92 }),
            ("Vakum ve basınç", new[] { 30, 32, 34, 66 }),
            ("Soğutma ve su soğutma", new[] { 36, 38, 40, 42, 44, 46 }),
            ("Isıtma ve sirkülasyon", new[] { 48, 50, 52, 56, 74, 76, 70 }),
            ("Gaz hatları ve debiler", new[] { 58, 60, 62, 64, 72, 82, 84, 86 }),
            ("Haberleşme ve termokupl kanalları", new[] { 68, 80 }),
            ("Kaçak testi", new[] { 96, 98, 100, 102, 104, 106 })
        };

        private readonly ApiClient _api;
        private readonly bool _canCalibrate;
        private readonly bool _canSafety;
        private readonly List<ParamRow> _all = new();

        private string _searchText = string.Empty;
        private string _paramMessage = string.Empty;
        private string _backupNote = string.Empty;
        private string _backupMessage = string.Empty;
        private string _resultTitle = string.Empty;
        private BackupRow? _selectedBackup;

        public ObservableCollection<ParamRow> Rows { get; } = new();
        public ObservableCollection<BackupRow> Backups { get; } = new();
        public ObservableCollection<string> ResultLines { get; } = new();

        public IAsyncRelayCommand<ParamRow> SaveCommand { get; }
        public IRelayCommand<ParamRow> RevertCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand CreateBackupCommand { get; }
        public IAsyncRelayCommand CompareCommand { get; }
        public IAsyncRelayCommand RestoreCommand { get; }

        // Parametre adı ya da açıklamasında aranan metin
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        // Listede seçili yedek; karşılaştırma ve geri yükleme buna uygulanır.
        public BackupRow? SelectedBackup
        {
            get => _selectedBackup;
            set
            {
                if (SetProperty(ref _selectedBackup, value))
                {
                    CompareCommand.NotifyCanExecuteChanged();
                    RestoreCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string BackupNote { get => _backupNote; set => SetProperty(ref _backupNote, value); }
        public string ParamMessage { get => _paramMessage; private set => SetProperty(ref _paramMessage, value); }
        public string BackupMessage { get => _backupMessage; private set => SetProperty(ref _backupMessage, value); }
        public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }

        // Yetkileri okur, komutları kurar, parametreleri ve yedek listesini ister.
        public SettingsViewModel(ApiClient api, SessionService session)
        {
            _api = api;
            _canCalibrate = session.HasRole(ParameterPermissions.CalibrationWrite);
            _canSafety = session.HasRole(ParameterPermissions.SafetyLimitsWrite);

            SaveCommand = new AsyncRelayCommand<ParamRow>(SaveAsync);
            RevertCommand = new RelayCommand<ParamRow>(Revert);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => _canCalibrate || _canSafety);
            CompareCommand = new AsyncRelayCommand(CompareAsync, () => SelectedBackup != null);
            RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => SelectedBackup != null && _canSafety);

            if (!_canCalibrate && !_canSafety)
                ParamMessage = "Parametreleri değiştirme yetkiniz yok; değerler yalnızca görüntülenir.";

            _ = RefreshAsync();
        }

        // Parametreleri ve yedek listesini sunucudan yeniden okur.
        private async Task RefreshAsync()
        {
            await LoadParametersAsync();
            await LoadBackupsAsync();
        }

        // DB204 parametrelerini okur; satırlar varsa değerlerini günceller, yazılmakta olan kutulara dokunmaz.
        private async Task LoadParametersAsync()
        {
            var result = await _api.GetAsync<List<ParamValueDto>>("api/settings/parameters");
            if (!result.IsSuccess || result.Data == null)
            {
                ParamMessage = result.Message ?? "Parametreler okunamadı.";
                return;
            }

            foreach (var dto in result.Data)
            {
                var row = _all.FirstOrDefault(r => r.Address == dto.Address);
                if (row == null)
                {
                    _all.Add(CreateRow(dto));
                    continue;
                }

                bool wasDirty = row.IsDirty;
                row.Value = dto.Value;
                if (!wasDirty)
                    row.EditText = dto.Value.ToString();
            }

            ApplyFilter();
        }

        // Sunucudan gelen parametreyi tablo satırına çevirir.
        private ParamRow CreateRow(ParamValueDto dto)
        {
            return new ParamRow
            {
                Address = dto.Address,
                Name = dto.Name,
                Description = dto.Description,
                Unit = dto.Unit,
                GroupName = FindGroup(dto.Address),
                RangeText = $"{dto.Min} … {dto.Max}",
                DefaultText = dto.Default.ToString(),
                Default = dto.Default,
                Min = dto.Min,
                Max = dto.Max,
                IsSafety = dto.IsSafety,
                CanEdit = dto.IsSafety ? _canSafety : _canCalibrate,
                Value = dto.Value,
                EditText = dto.Value.ToString()
            };
        }

        // Arama metnine uyan satırları grup sırasıyla tabloya yerleştirir.
        private void ApplyFilter()
        {
            string search = (SearchText ?? string.Empty).Trim();
            Rows.Clear();

            foreach (var group in Groups)
            {
                foreach (int address in group.Addresses)
                {
                    var row = _all.FirstOrDefault(r => r.Address == address);
                    if (row != null && Matches(row, search))
                        Rows.Add(row);
                }
            }

            // Grup listesinde olmayan (sonradan eklenmiş) parametreler sona eklenir.
            foreach (var row in _all.Where(r => r.GroupName == "Diğer" && Matches(r, search)))
                Rows.Add(row);
        }

        // Kutudaki değeri denetler, güvenlik parametresinde onay alır ve PLC'ye yazar.
        private async Task SaveAsync(ParamRow? row)
        {
            if (row == null || !row.CanEdit || !row.IsDirty)
                return;

            if (!int.TryParse((row.EditText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                ParamMessage = $"{row.Name}: tam sayı girilmeli.";
                return;
            }

            if (value < row.Min || value > row.Max)
            {
                ParamMessage = $"{row.Name}: değer {row.Min} ile {row.Max} arasında olmalı.";
                return;
            }

            if (row.IsSafety)
            {
                var answer = MessageBox.Show(
                    $"{row.Name} bir güvenlik parametresidir.\n\n{row.Description}\n\nPLC'deki değer: {row.Value} {row.Unit}\nYeni değer: {value} {row.Unit}\n\nDeğiştirmek istiyor musunuz?",
                    "Güvenlik parametresi", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var result = await _api.PutAsync<JsonElement>($"api/settings/parameters/{row.Address}?value={value}");
            if (!result.IsSuccess)
            {
                ParamMessage = $"{row.Name} yazılamadı: {result.Message}";
                return;
            }

            // PLC değeri kendi sınırına çekebilir; kutu PLC'den okunan değerle yenilenir.
            row.EditText = row.Value.ToString();
            await LoadParametersAsync();
            ParamMessage = $"{DateTime.Now:HH:mm:ss}  {row.Name} = {row.Value} {row.Unit} yazıldı.";
        }

        // Kutudaki yazılmamış değişikliği atar.
        private void Revert(ParamRow? row)
        {
            if (row != null)
                row.EditText = row.Value.ToString();
        }

        // Yedek listesini en yenisi üstte olacak şekilde okur.
        private async Task LoadBackupsAsync()
        {
            var result = await _api.GetAsync<List<ParamBackupDto>>("api/settings/backups");
            if (!result.IsSuccess || result.Data == null)
            {
                BackupMessage = result.Message ?? "Yedek listesi alınamadı.";
                return;
            }

            int? selectedId = SelectedBackup?.Id;
            Backups.Clear();

            foreach (var backup in result.Data.OrderByDescending(b => b.Timestamp))
            {
                Backups.Add(new BackupRow
                {
                    Id = backup.Id,
                    Time = backup.Timestamp.ToString("dd.MM.yyyy HH:mm", Tr),
                    Note = backup.IsAuto ? $"otomatik · {backup.Note}".TrimEnd(' ', '·') : backup.Note
                });
            }

            SelectedBackup = Backups.FirstOrDefault(b => b.Id == selectedId);
        }

        // PLC'deki parametrelerin o anki halini, yazılan notla yedekler.
        private async Task CreateBackupAsync()
        {
            string note = (BackupNote ?? string.Empty).Trim();
            string path = note.Length > 0 ? $"api/settings/backups?note={Uri.EscapeDataString(note)}" : "api/settings/backups";

            var result = await _api.PostAsync<JsonElement>(path);
            BackupMessage = result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  Yedek alındı."
                : result.Message ?? "Yedek alınamadı.";

            if (result.IsSuccess)
            {
                BackupNote = string.Empty;
                await LoadBackupsAsync();
            }
        }

        // Seçili yedek ile PLC'deki değerler arasındaki farkları listeler.
        private async Task CompareAsync()
        {
            var backup = SelectedBackup;
            if (backup == null)
                return;

            var result = await _api.GetAsync<List<ParamDiffDto>>($"api/settings/backups/{backup.Id}/compare");
            ResultLines.Clear();

            if (!result.IsSuccess || result.Data == null)
            {
                ResultTitle = string.Empty;
                BackupMessage = result.Message ?? "Karşılaştırma yapılamadı.";
                return;
            }

            ResultTitle = result.Data.Count == 0
                ? $"{backup.Time} yedeği PLC ile aynı."
                : $"{backup.Time} yedeği ile PLC arasında {result.Data.Count} fark (yedek → PLC):";

            foreach (var diff in result.Data)
                ResultLines.Add($"{diff.Name}: {diff.BackupValue} → {diff.PlcValue} {diff.Unit}{(diff.IsSafety ? "  (güvenlik)" : string.Empty)}");

            BackupMessage = string.Empty;
        }

        // Onay alıp seçili yedeği PLC'ye geri yükler ve her parametrenin sonucunu listeler.
        private async Task RestoreAsync()
        {
            var backup = SelectedBackup;
            if (backup == null)
                return;

            var answer = MessageBox.Show(
                $"{backup.Time} tarihli yedek PLC'ye geri yüklenecek. Güvenlik parametreleri dahil bütün parametreler yedekteki değerlere döner.\n\nGeri yüklemek istiyor musunuz?",
                "Yedeği geri yükle", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
                return;

            var result = await _api.PostAsync<List<ParamRestoreItemDto>>($"api/settings/backups/{backup.Id}/restore");
            ResultLines.Clear();

            ResultTitle = result.IsSuccess ? "Geri yükleme sonucu (önce → sonra):" : "Geri yükleme tamamlanamadı:";
            BackupMessage = result.Message ?? string.Empty;

            if (result.Data != null)
            {
                foreach (var item in result.Data)
                    ResultLines.Add($"{item.Name}: {item.PreviousValue} → {item.AppliedValue} {item.Unit}  ({item.Status})");
            }

            await LoadParametersAsync();
        }

        // Parametre adresinin ait olduğu grubun adını döner.
        private static string FindGroup(int address)
        {
            foreach (var group in Groups)
            {
                if (group.Addresses.Contains(address))
                    return group.Name;
            }

            return "Diğer";
        }

        // Satırın adında ya da açıklamasında aranan metnin geçip geçmediğini döner; arama boşsa her satır uyar.
        private static bool Matches(ParamRow row, string search)
        {
            if (search.Length == 0)
                return true;

            return Tr.CompareInfo.IndexOf(row.Name, search, CompareOptions.IgnoreCase) >= 0
                || Tr.CompareInfo.IndexOf(row.Description, search, CompareOptions.IgnoreCase) >= 0;
        }
    }
}