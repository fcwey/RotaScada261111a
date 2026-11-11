namespace Entities.Structures
{
    // DB204'teki tek bir parametrenin tanımı
    public class ParamDefinition
    {
        public int Address { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Default { get; set; }
        public string Unit { get; set; } = string.Empty;
        public int Min { get; set; }
        public int Max { get; set; }
        public bool IsSafety { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    // DB204 (DB_ScadaParam) parametre listesi (kılavuz bölüm 7). SCADA yalnızca bu adreslere yazabilir.
    public static class ParamCatalog
    {
        public const int BlockSize = 100;   // DB204 toplam boyut

        public static readonly IReadOnlyList<ParamDefinition> All = new List<ParamDefinition>
        {
            P(2,  "MaxPotaTemp",        750,  "°C",     100, 800,   true,  "Reçetede girilebilecek en yüksek sıcaklık"),
            P(4,  "MaxChTemp",          850,  "°C",     100, 950,   true,  "Cehennemlik sınırı; aşan grubun gücü kesilir"),
            P(6,  "MaxPressure",        400,  "mbar",   10,  1000,  true,  "Aşılırsa güvenli durdurma"),
            P(8,  "MinPressureNit",     1,    "mbar",   0,   100,   true,  "Nitrasyonda altına inilirse acil azot açılır, amonyak kesilir"),
            P(10, "NH3_MinTemp",        450,  "°C",     350, 700,   true,  "Altında amonyak ve CO2 verilmez, H2 ölçülmez"),
            P(12, "H2_CtrlMode",        1,    "",       1,   1,     false, "H2 kontrol yöntemi. Şimdilik sadece 1 (amonyak debisi)"),
            P(14, "H2_NH3BandPerc",     30,   "%",      0,   100,   false, "Amonyak debisinin reçete değerinden sapabileceği en büyük oran"),
            P(16, "H2_AdjustPeriodSec", 120,  "sn",     10,  3600,  false, "H2 düzeltme aralığı"),
            P(18, "H2_AdjustStepLph",   10,   "L/saat", 1,   1000,  false, "Her düzeltmede debi adımı"),
            P(20, "H2_Deadband",        5,    "% x10",  0,   200,   false, "Bu kadar sapmaya kadar düzeltme yapılmaz"),
            P(22, "PurgeVolumeL",       2000, "L",      100, 32767, true,  "Atmosferin temiz sayılması için gereken azot hacmi"),
            P(24, "PurgeN2Flow",        1000, "L/saat", 100, 32767, true,  "Yıkama azot debisi"),
            P(26, "PurgeH2Limit",       10,   "% x10",  0,   100,   true,  "Yıkama sonunda izin verilen en yüksek H2"),
            P(28, "LidOpenMaxTemp",     150,  "°C",     20,  300,   true,  "Üstünde kapak açılamaz"),
            P(30, "VacValveDelaySec",   10,   "sn",     0,   600,   false, "Vakum motoru çalıştıktan sonra vana gecikmesi"),
            P(32, "VacFillTimeoutMin",  15,   "dk",     1,   240,   false, "Azot dolumu zaman aşımı"),
            P(34, "VacFillTargetMbar",  1,    "mbar",   0,   1000,  false, "Azot dolumunun biteceği basınç"),
            P(36, "KlepeTimeoutSec",    30,   "sn",     1,   600,   false, "Klepenin açılması ve kapanması için süre"),
            P(38, "WaterCoolEnable",    0,    "0/1",    0,   1,     false, "Su soğutma kullanılsın"),
            P(40, "WaterCoolStartTemp", 300,  "°C",     50,  750,   false, "Altında su soğutma devreye girer"),
            P(42, "WaterFeedTimeSec",   90,   "sn",     1,   3600,  false, "Eşanjör ilk dolum süresi"),
            P(44, "WaterPumpTimeSec",   1200, "sn",     1,   7200,  false, "Eşanjör boşaltma için en uzun süre"),
            P(46, "WaterPumpMaxTemp",   100,  "°C",     20,  300,   false, "Altında eşanjör boşaltılabilir"),
            P(48, "FanDefaultSpeed",    100,  "%",      1,   100,   false, "Adımda hız girilmemişse kullanılır"),
            P(50, "FanMinSpeed",        30,   "%",      0,   100,   false, "İzin verilen en düşük fan hızı"),
            P(52, "DengeRange",         2,    "°C",     0,   100,   false, "Sete bu kadar yaklaşınca alt-üst dengeleme başlar"),
            P(54, "MaxTempOffset",      20,   "°C",     1,   200,   true,  "Pota seti bu kadar aşarsa alarm 21"),
            P(56, "GsoakTimeoutMin",    60,   "dk",     0,   1440,  false, "Gsoak beklemesi bunu aşarsa alarm 31. 0 = kapalı"),
            P(58, "N2_MfcMaxLph",       0,    "L/saat", 0,   32767, false, "Azot MFC tam skala. Girilmezse azot verilmez"),
            P(60, "NH3_MfcMaxLph",      0,    "L/saat", 0,   32767, false, "Amonyak MFC tam skala. Girilmezse amonyak verilmez"),
            P(62, "MfcDevPerc",         10,   "%",      0,   100,   false, "İzinli debi sapması, tam skalanın yüzdesi. 0 = kapalı"),
            P(64, "MfcDevDelaySec",     30,   "sn",     0,   3600,  false, "Sapma alarmı gecikmesi"),
            P(66, "PressureRange",      5,    "mbar",   0,   100,   false, "Basınç kontrol histerezisi"),
            P(68, "ScadaWatchdogSec",   10,   "sn",     0,   600,   false, "SCADA yaşam sinyali zaman aşımı. 0 = kapalı"),
            P(70, "PowerLossAdvance",   0,    "0/1",    0,   1,     false, "1: menevişte enerji kesintisi süresi adıma eklenir"),
            P(72, "CO2_FlowLph",        0,    "L/saat", 0,   32767, false, "CO2 rotometresinde ayarlı debi (Kn hesabı için)"),
            P(74, "RampStartMode",      0,    "0/1",    0,   1,     false, "İlk ısıtma rampası: 0 o anki sıcaklıktan, 1 RampStartTemp değerinden başlar"),
            P(76, "RampStartTemp",      50,   "°C",     0,   750,   false, "RampStartMode = 1 iken başlangıç sıcaklığı")
        };

        // Verilen adresteki parametrenin tanımını döner; adres katalogda yoksa null döner.
        public static ParamDefinition? Find(int address)
        {
            return All.FirstOrDefault(p => p.Address == address);
        }

        // Katalog satırlarını kısa yazabilmek için parametre tanımı üretir.
        private static ParamDefinition P(int address, string name, int defaultValue, string unit, int min, int max, bool isSafety, string description)
        {
            return new ParamDefinition
            {
                Address = address,
                Name = name,
                Default = defaultValue,
                Unit = unit,
                Min = min,
                Max = max,
                IsSafety = isSafety,
                Description = description
            };
        }
    }
}