using Core.Entities;

namespace Entities.Dtos
{
    // Yedek listesinde gösterilen satır
    public class ParamBackupDto : IDto
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public int UserId { get; set; }
        public string Note { get; set; } = string.Empty;
        public bool IsAuto { get; set; }
    }

    // Yedekteki değer ile PLC'deki değer farklı olan bir parametre
    public class ParamDiffDto : IDto
    {
        public int Address { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public bool IsSafety { get; set; }
        public int BackupValue { get; set; }
        public int PlcValue { get; set; }
    }

    // Geri yüklemede işlem gören bir parametrenin sonucu
    public class ParamRestoreItemDto : IDto
    {
        public int Address { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int PreviousValue { get; set; }      // Geri yüklemeden önce PLC'deki değer
        public int BackupValue { get; set; }        // Yedekteki değer
        public int AppliedValue { get; set; }       // Geri yüklemeden sonra PLC'de geçerli olan değer
        public string Status { get; set; } = string.Empty;
    }
}