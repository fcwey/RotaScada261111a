using Core.Entities;
using Entities.Concrete;

namespace Entities.Dtos
{
    // Şarj listesinde gösterilen satır
    public class BatchListDto : IDto
    {
        public int Id { get; set; }
        public string BatchCode { get; set; } = string.Empty;
        public int RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;   // Şarj başladığı andaki reçete adı
        public int OperatorUserId { get; set; }
        public string OperatorEmail { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = string.Empty;       // RUNNING, COMPLETED, ABORTED, UNKNOWN
        public string? CompletionNote { get; set; }
    }

    // Şarjın planını değiştiren bir olay
    public class BatchRevisionDto : IDto
    {
        public DateTime Timestamp { get; set; }
        public int UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;       // START, EDIT, GOTO, SKIP
        public Recipe? Recipe { get; set; }                      // START ve EDIT için reçetenin o anki hali
        public int? StepNo { get; set; }                         // GOTO için gidilen adım
        public int? StepMinute { get; set; }                     // GOTO için o adımda geçmiş sayılan dakika
    }

    // Tek bir şarjın ayrıntısı: özet, plan geçmişi ve şarj sırasındaki alarmlar
    public class BatchDetailDto : IDto
    {
        public BatchListDto Batch { get; set; } = new BatchListDto();
        public List<BatchRevisionDto> Revisions { get; set; } = new();
        public List<AlarmHistoryDto> Alarms { get; set; } = new();
    }
}