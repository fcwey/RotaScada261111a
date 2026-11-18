using Core.Entities;

namespace Entities.Dtos
{
    // Program başlatılırken operatörün girdiği şarj bilgileri
    public class StartRequestDto : IDto
    {
        public float? LoadKg { get; set; }
        public string? Note { get; set; }
    }

    // Şarja not eklerken gönderilen metin
    public class BatchNoteRequestDto : IDto
    {
        public string? Text { get; set; }
    }

    // Şarja iliştirilmiş bir not
    public class BatchNoteDto : IDto
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public int UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    // Şarjın operatör tarafından girilen bilgileri: ağırlık ve notlar
    public class BatchInfoDto : IDto
    {
        public int BatchId { get; set; }
        public string BatchCode { get; set; } = string.Empty;
        public float? LoadKg { get; set; }
        public List<BatchNoteDto> Notes { get; set; } = new();   // Eskiden yeniye
    }
}