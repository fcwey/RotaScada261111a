using Core.Entities;
using System;

namespace Entities.Concrete
{
    // Bir şarjın reçetesinin belirli bir andaki kopyası (başlangıçta ve çalışırken yapılan her değişiklikte)
    public class BatchRecipeRevision : IEntity
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int UserId { get; set; }                         // Başlatan ya da değiştiren kullanıcı
        public string Reason { get; set; } = "START";           // START: program başlangıcı, EDIT: çalışırken değişiklik
        public string RecipeJson { get; set; } = string.Empty;  // Reçetenin o anki hali (başlık ve adımlar)
    }
}