namespace Entities.Enums
{
    // DB200 CmdCode alanına yazılan komut kodları (kılavuz bölüm 3)
    public enum ScadaCommandCode : short
    {
        Start = 1,
        Stop = 2,
        Hold = 3,
        Resume = 4,
        SkipToCooling = 5,
        GotoStep = 6,
        ApplyEdit = 7,
        VacuumOk = 8,
        AlarmReset = 9,
        BuzzerMute = 10,
        Validate = 11,
        SetClock = 12,          // PLC saatini ayarla
        TuneStart = 13,         // PID otomatik ayar başlat (1 ön ayar, 2 ince ayar)
        TuneCancel = 14,        // PID otomatik ayarı iptal et
        PurgeOverride = 15,     // Yıkamayı elle onayla
        RunHoursReset = 16,     // Çalışma saati sayacını sıfırla (1..5)
        HoldExit = 17,          // Korumalı beklemeyi sonlandır
        LidOverride = 18,       // Kapak kilidini aş (1 başlat, 0 iptal)
        LeakTest = 19,          // Kaçak testi (1 başlat, 0 iptal)
        Manual = 20
    }
}