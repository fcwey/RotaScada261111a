using Core.Entities;

namespace Entities.Dtos
{
    // Giriş yapmış kullanıcının ekranda kullanılan bilgileri
    public class CurrentUserDto : IDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }
}