using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Constants;
using DesktopUI.Services;
using Entities.Dtos;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace DesktopUI.ViewModels
{
    // Kullanıcı listesinin bir satırı
    public class UserRow
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public string RolesText { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    // İşaretlenebilen bir rol ve ne işe yaradığı
    public class RoleItem : ObservableObject
    {
        private bool _isChecked;

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
    }

    // Kullanıcılar sekmesi: kullanıcı listesi, rol atama, hesap açma ve kapatma, şifre sıfırlama, yeni kullanıcı
    public class UsersViewModel : ObservableObject
    {
        private const int MinPasswordLength = 6;

        // Rol adlarının operatöre gösterilen açıklamaları
        private static readonly Dictionary<string, string> RoleDescriptions = new()
        {
            [PlcPermissions.Admin] = "Her şeye yetkili; kullanıcı yönetimi ve işlem kayıtları yalnızca bu rolde",
            [PlcPermissions.Operate] = "Program başlat, durdur, beklet, devam; alarm reset; kaçak testi; şarj ağırlığı ve notlar",
            [PlcPermissions.ProgramEdit] = "Çalışan programı değiştir, adıma git, soğutmaya geç",
            [PlcPermissions.Manual] = "Manuel modda cihazları elle çalıştır",
            [PlcPermissions.Override] = "Aşma komutları: yıkama onayı, korumalı beklemeyi bitirme, kapak kilidini aşma",
            [PlcPermissions.Maintenance] = "PID otomatik ayar, çalışma saati sıfırlama",
            [ParameterPermissions.CalibrationWrite] = "Güvenlik dışı PLC parametrelerini değiştir, parametre yedeği al",
            [ParameterPermissions.SafetyLimitsWrite] = "Güvenlik parametrelerini değiştir, yedeği PLC'ye geri yükle",
            [ParameterPermissions.PlcIpConfigWrite] = "PLC bağlantı adresini değiştir",
            [ParameterPermissions.SetpointBasicWrite] = "Şimdilik kullanılmıyor",
            [ParameterPermissions.GasFlowLimitsWrite] = "Şimdilik kullanılmıyor"
        };

        private readonly ApiClient _api;
        private readonly int _currentUserId;

        private List<string> _roleNames = new();
        private UserRow? _selectedUser;
        private string _message = string.Empty;
        private string _newEmail = string.Empty;
        private string _newFirstName = string.Empty;
        private string _newLastName = string.Empty;
        private string _newMessage = string.Empty;

        public ObservableCollection<UserRow> Users { get; } = new();
        public ObservableCollection<RoleItem> SelectedRoles { get; } = new();
        public ObservableCollection<RoleItem> NewRoles { get; } = new();

        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand SaveRolesCommand { get; }
        public IAsyncRelayCommand ToggleActiveCommand { get; }

        // Listede seçili kullanıcı; değişince rol kutucukları onun rollerine göre işaretlenir.
        public UserRow? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (!SetProperty(ref _selectedUser, value))
                    return;

                FillRoles(SelectedRoles, value?.Roles ?? new List<string>());
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedTitle));
                OnPropertyChanged(nameof(ToggleActiveText));
                SaveRolesCommand.NotifyCanExecuteChanged();
                ToggleActiveCommand.NotifyCanExecuteChanged();
            }
        }

        public bool HasSelection => SelectedUser != null;
        public string SelectedTitle => SelectedUser == null ? "Listeden bir kullanıcı seçin" : $"{SelectedUser.FullName} · {SelectedUser.Email}";
        public string ToggleActiveText => SelectedUser != null && !SelectedUser.IsActive ? "Hesabı Aç" : "Hesabı Kapat";

        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string NewEmail { get => _newEmail; set => SetProperty(ref _newEmail, value); }
        public string NewFirstName { get => _newFirstName; set => SetProperty(ref _newFirstName, value); }
        public string NewLastName { get => _newLastName; set => SetProperty(ref _newLastName, value); }
        public string NewMessage { get => _newMessage; private set => SetProperty(ref _newMessage, value); }

        // Bağımlılıkları alır, komutları kurar, rol adlarını ve kullanıcı listesini ister.
        public UsersViewModel(ApiClient api, SessionService session)
        {
            _api = api;
            _currentUserId = session.User?.Id ?? 0;

            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            SaveRolesCommand = new AsyncRelayCommand(SaveRolesAsync, () => SelectedUser != null);
            ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => SelectedUser != null);

            _ = LoadAsync();
        }

        // Rol adlarını ve kullanıcıları sunucudan okur; seçili kullanıcı korunur.
        private async Task LoadAsync()
        {
            var roles = await _api.GetAsync<List<string>>("api/users/roles");
            if (roles.IsSuccess && roles.Data != null)
            {
                _roleNames = roles.Data;
                if (NewRoles.Count == 0)
                    FillRoles(NewRoles, new List<string>());
            }

            var result = await _api.GetAsync<List<UserListDto>>("api/users");
            if (!result.IsSuccess || result.Data == null)
            {
                Message = result.Message ?? "Kullanıcı listesi alınamadı.";
                return;
            }

            int? selectedId = SelectedUser?.Id;
            Users.Clear();

            foreach (var user in result.Data.OrderBy(u => u.Email))
            {
                Users.Add(new UserRow
                {
                    Id = user.Id,
                    Email = user.Email,
                    FullName = $"{user.FirstName} {user.LastName}".Trim(),
                    IsActive = user.IsActive,
                    StatusText = user.IsActive ? string.Empty : "kapalı",
                    RolesText = user.Roles.Count == 0 ? "rol yok" : string.Join(", ", user.Roles.OrderBy(r => r)),
                    Roles = user.Roles
                });
            }

            SelectedUser = Users.FirstOrDefault(u => u.Id == selectedId) ?? Users.FirstOrDefault();
        }

        // Rol kutucuklarını bütün rol adlarıyla doldurur ve verilen rolleri işaretler.
        private void FillRoles(ObservableCollection<RoleItem> target, List<string> checkedRoles)
        {
            target.Clear();

            foreach (string name in _roleNames)
            {
                target.Add(new RoleItem
                {
                    Name = name,
                    Description = RoleDescriptions.TryGetValue(name, out var description) ? description : string.Empty,
                    IsChecked = checkedRoles.Contains(name)
                });
            }
        }

        // Seçili kullanıcının rollerini işaretli kutucuklara göre değiştirir.
        private async Task SaveRolesAsync()
        {
            var user = SelectedUser;
            if (user == null)
                return;

            var roles = SelectedRoles.Where(r => r.IsChecked).Select(r => r.Name).ToList();

            if (user.Id == _currentUserId && !roles.Contains(PlcPermissions.Admin))
            {
                Message = "Kendi Admin rolünüzü kaldıramazsınız; başka bir yönetici yapmalı.";
                return;
            }

            var result = await _api.PutAsync<JsonElement>($"api/users/{user.Id}/roles", roles);
            Message = result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  {user.Email} rolleri kaydedildi. Değişiklik kullanıcının sonraki isteğinde geçerli olur."
                : result.Message ?? "Roller kaydedilemedi.";

            if (result.IsSuccess)
                await LoadAsync();
        }

        // Onay alıp seçili kullanıcının hesabını kapatır ya da açar.
        private async Task ToggleActiveAsync()
        {
            var user = SelectedUser;
            if (user == null)
                return;

            bool activate = !user.IsActive;

            if (!activate && user.Id == _currentUserId)
            {
                Message = "Kendi hesabınızı kapatamazsınız.";
                return;
            }

            if (!activate)
            {
                var answer = MessageBox.Show(
                    $"{user.Email} hesabı kapatılacak; kullanıcı giriş yapamaz ve açık oturumu da çalışmaz.\n\nHesabı kapatmak istiyor musunuz?",
                    "Hesabı kapat", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var result = await _api.PutAsync<JsonElement>($"api/users/{user.Id}/status?active={(activate ? "true" : "false")}");
            Message = result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  {user.Email} hesabı {(activate ? "açıldı" : "kapatıldı")}."
                : result.Message ?? "Hesap durumu değiştirilemedi.";

            if (result.IsSuccess)
                await LoadAsync();
        }

        // Seçili kullanıcının şifresini verilen şifreyle değiştirir; başarılıysa true döner.
        public async Task<bool> ResetPasswordAsync(string password)
        {
            var user = SelectedUser;
            if (user == null)
                return false;

            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            {
                Message = $"Şifre en az {MinPasswordLength} karakter olmalı.";
                return false;
            }

            var result = await _api.PutAsync<JsonElement>($"api/users/{user.Id}/password", new UserPasswordDto { NewPassword = password });
            Message = result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  {user.Email} şifresi değiştirildi."
                : result.Message ?? "Şifre değiştirilemedi.";

            return result.IsSuccess;
        }

        // Formdaki bilgilerle ve verilen şifreyle yeni kullanıcı açar; başarılıysa true döner.
        public async Task<bool> CreateUserAsync(string password)
        {
            string email = (NewEmail ?? string.Empty).Trim();

            if (email.Length == 0 || !email.Contains('@'))
            {
                NewMessage = "Geçerli bir e-posta girin.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(NewFirstName) || string.IsNullOrWhiteSpace(NewLastName))
            {
                NewMessage = "Ad ve soyad girilmeli.";
                return false;
            }

            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            {
                NewMessage = $"Şifre en az {MinPasswordLength} karakter olmalı.";
                return false;
            }

            var dto = new UserCreateDto
            {
                Email = email,
                FirstName = NewFirstName.Trim(),
                LastName = NewLastName.Trim(),
                Password = password,
                Roles = NewRoles.Where(r => r.IsChecked).Select(r => r.Name).ToList()
            };

            var result = await _api.PostAsync<JsonElement>("api/users", dto);
            if (!result.IsSuccess)
            {
                NewMessage = result.Message ?? "Kullanıcı açılamadı.";
                return false;
            }

            NewMessage = $"{DateTime.Now:HH:mm:ss}  {email} açıldı.";
            NewEmail = string.Empty;
            NewFirstName = string.Empty;
            NewLastName = string.Empty;
            FillRoles(NewRoles, new List<string>());

            await LoadAsync();
            return true;
        }
    }
}