using Core.Entities;

namespace Entities.Dtos
{
    // Kullanıcı listesinde gösterilen bilgiler. Email alanı kullanıcı adını taşır.
    public class UserListDto : IDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    // Yeni kullanıcı açarken gönderilen bilgiler. Email alanı kullanıcı adını taşır; şifre boş bırakılabilir.
    public class UserCreateDto : IDto
    {
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Password { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    // Şifre değiştirirken gönderilen bilgi; boş gönderilirse hesap şifresiz olur.
    public class UserPasswordDto : IDto
    {
        public string? NewPassword { get; set; }
    }

    // Bir kullanıcının veritabanındaki güncel durumu ve rolleri (her istekte yetki kontrolü için)
    public class UserAccessDto : IDto
    {
        public bool Exists { get; set; }
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
    }
}