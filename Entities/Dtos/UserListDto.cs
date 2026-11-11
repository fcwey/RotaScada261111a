using Core.Entities;

namespace Entities.Dtos
{
    // Kullanıcı listesinde gösterilen bilgiler
    public class UserListDto : IDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    // Yeni kullanıcı açarken gönderilen bilgiler
    public class UserCreateDto : IDto
    {
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    // Şifre sıfırlarken gönderilen bilgi
    public class UserPasswordDto : IDto
    {
        public string NewPassword { get; set; } = string.Empty;
    }

    // Bir kullanıcının veritabanındaki güncel durumu ve rolleri (her istekte yetki kontrolü için)
    public class UserAccessDto : IDto
    {
        public bool Exists { get; set; }
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
    }
}