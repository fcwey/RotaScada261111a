namespace Entities.Enums
{
    // DB200 CmdCode alanına yazılan komut kodları (kılavuz bölüm 3.3)
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
        Manual = 20
    }
}