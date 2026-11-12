using Core.Entities;
using System;

namespace Entities.Concrete
{
    // Bir şarjın planını değiştiren olay: başlangıç reçetesi, çalışırken değişiklik, adıma atlama ya da soğutmaya geçiş
    public class BatchRecipeRevision : IEntity
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int UserId { get; set; }                         // İşlemi yapan kullanıcı
        public string Reason { get; set; } = "START";           // START, EDIT, GOTO, SKIP
        public string RecipeJson { get; set; } = string.Empty;  // START ve EDIT için reçetenin o anki hali
        public int? StepNo { get; set; }                        // GOTO için gidilen adım
        public int? StepMinute { get; set; }                    // GOTO için o adımda geçmiş sayılan dakika
    }
}