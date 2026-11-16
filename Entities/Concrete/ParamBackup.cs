using Core.Entities;
using System;

namespace Entities.Concrete
{
    // DB204 parametrelerinin belirli bir andaki yedeği
    public class ParamBackup : IEntity
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int UserId { get; set; }                         // Yedeği alan (ya da son değişikliği yapan) kullanıcı
        public string Note { get; set; } = string.Empty;
        public bool IsAuto { get; set; }                        // true: her parametre yazımında güncellenen tek otomatik yedek
        public string ValuesJson { get; set; } = string.Empty;  // Adres -> değer eşlemesi
    }
}