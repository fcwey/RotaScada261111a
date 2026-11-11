namespace Core.Constants
{
    // PLC komutları için rol adları (OperationClaims tablosundaki Name alanıyla aynı olmalı)
    public static class PlcPermissions
    {
        public const string Admin = "Admin";

        // Program akışı: Start, Stop, Hold, Resume, VacuumOk, AlarmReset
        public const string Operate = "Plc.Operate";

        // Çalışan programa müdahale: SkipToCooling, GotoStep, ApplyEdit
        public const string ProgramEdit = "Plc.ProgramEdit";

        // Manuel cihaz komutları (kod 20)
        public const string Manual = "Plc.Manual";
    }
}