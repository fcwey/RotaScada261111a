using Core.Entities;
using System;

namespace Entities.Concrete
{
    // Bir şarja iliştirilen serbest not (başlangıç notu, ara not, sonuç bilgisi)
    public class BatchNote : IEntity
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int UserId { get; set; }
        public string NoteText { get; set; } = string.Empty;    // En fazla 2000 karakter
    }
}