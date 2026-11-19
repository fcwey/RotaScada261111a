using Core.Entities;

namespace Entities.Dtos
{
    // Giriş isteği. Email alanı kullanıcı adını taşır (alan adı eski sürümle uyum için değişmedi).
    public class UserForLoginDto : IDto
    {
        public string Email { get; set; } = string.Empty;

        // Şifre boş bırakılabilir; şifresiz hesaplar için.
        public string? Password { get; set; }
    }
}