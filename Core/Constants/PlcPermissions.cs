namespace Core.Constants
{
    // PLC komutları için rol adları (OperationClaims tablosundaki Name alanıyla aynı olmalı)
    public static class PlcPermissions
    {
        public const string Admin = "Admin";

        // Program akışı: Start, Stop, Hold, Resume, VacuumOk, AlarmReset, kaçak testi
        public const string Operate = "Plc.Operate";

        // Çalışan programa müdahale: SkipToCooling, GotoStep, ApplyEdit
        public const string ProgramEdit = "Plc.ProgramEdit";

        // Manuel cihaz komutları (kod 20)
        public const string Manual = "Plc.Manual";

        // Ölçümün yerine operatör kararını koyan aşma komutları: yıkama onayı, korumalı beklemeden çıkış, kapak kilidi aşma
        public const string Override = "Plc.Override";

        // Bakım komutları: saat ayarı, PID otomatik ayar, çalışma saati sayacı sıfırlama
        public const string Maintenance = "Plc.Maintenance";
    }
}