namespace Entities.Dtos
{
    // Günlük bakım ayarları (appsettings içindeki "Maintenance" bölümü)
    public class MaintenanceOptions
    {
        public int RunHour { get; set; } = 3;               // Bakımın yapılacağı saat (0–23); bu saatten sonra günde bir kez

        // Saklama süreleri (gün). 0 = hiç silme
        public int IdleLogKeepDays { get; set; } = 90;      // Şarj dışı (beklemedeki) ölçümler
        public int BatchLogKeepDays { get; set; } = 0;      // Şarja ait ölçümler
        public int AlarmKeepDays { get; set; } = 0;         // Kapanmış alarmlar
        public int AuditKeepDays { get; set; } = 0;         // İşlem kayıtları

        // Yedekleme
        public bool BackupEnabled { get; set; } = false;
        public string MysqldumpPath { get; set; } = @"C:\Program Files\MySQL\MySQL Server 8.4\bin\mysqldump.exe";
        public string BackupFolder { get; set; } = string.Empty;
        public int BackupKeepCount { get; set; } = 30;      // Saklanacak yedek dosyası sayısı
    }
}