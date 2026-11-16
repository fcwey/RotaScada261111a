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
        public bool IdleOnly { get; set; }          // Yalnızca program çalışmıyorken değiştirilebilir
        public string Description { get; set; } = string.Empty;
    }

    // DB204 (DB_ScadaParam, 112 bayt) parametre listesi (kılavuz bölüm 8). SCADA yalnızca bu adreslere yazabilir.
    public static class ParamCatalog
    {
        public const int BlockSize = 112;   // DB204 toplam boyut

        public static readonly IReadOnlyList<ParamDefinition> All = new List<ParamDefinition>
        {
            // Güvenlik sınırları (PLC bu değerleri kendi sabit sınırlarına çeker)
            P(2,   "MaxPotaTemp",           750,  "°C",      50,  800,   true,  "En yüksek pota sıcaklığı; reçete hedefleri bunu aşamaz, aşılırsa alarm 6"),
            P(4,   "MaxChTemp",             850,  "°C",      50,  900,   true,  "En yüksek cehennemlik sıcaklığı"),
            P(6,   "MaxPressure",           400,  "mbar",    10,  500,   true,  "En yüksek basınç; aşılırsa alarm 5"),
            P(8,   "MinPressureNit",        1,    "mbar",    1,   50,    true,  "Nitrasyonda alt basınç; altında acil azot açılır"),
            P(10,  "NH3_MinTemp",           450,  "°C",      300, 700,   true,  "Bu sıcaklığın altında amonyak verilmez, H2 ölçülmez"),
            P(54,  "MaxTempOffset",         20,   "°C",      5,   50,    true,  "Pota seti bu kadar aşarsa alarm 21"),
            P(78,  "ManVacMaxTemp",         200,  "°C",      50,  400,   true,  "Manuel vakum ancak bu sıcaklığın altında yapılır"),

            // H2 kontrolü
            P(12,  "H2_CtrlMode",           1,    "",        1,   1,     false, "H2 kontrol yöntemi. Şimdilik sadece 1 (amonyak debisi)"),
            P(14,  "H2_NH3BandPerc",        30,   "%",       0,   50,    false, "Reçete debisi etrafında izinli oynama"),
            P(16,  "H2_AdjustPeriodSec",    120,  "sn",      10,  3600,  false, "H2 düzeltme aralığı"),
            P(18,  "H2_AdjustStepLph",      10,   "L/saat",  1,   1000,  false, "Her düzeltmedeki debi adımı"),
            P(20,  "H2_Deadband",           5,    "% x10",   0,   200,   false, "Ölü bant (5 = %0,5)"),

            // Yıkama, kapak ve korumalı bekleme
            P(22,  "PurgeVolumeL",          2000, "L",       100, 32000, true,  "Atmosferin temiz sayılması için gereken azot hacmi"),
            P(24,  "PurgeN2Flow",           1000, "L/saat",  100, 32000, true,  "Yıkama azot debisi"),
            P(26,  "PurgeH2Limit",          10,   "% x10",   1,   20,    true,  "Yıkama sonu H2 sınırı (10 = %1,0)"),
            P(28,  "LidOpenMaxTemp",        150,  "°C",      30,  200,   true,  "Nitrasyonda (ve atmosfer kirliyken) kapak açma sıcaklığı"),
            P(94,  "LidOpenMaxTempTemper",  150,  "°C",      30,  400,   true,  "Meneviş ve oksidasyonda kapak açma sıcaklığı"),
            P(90,  "LidOpenMaxPress",       5,    "mbar",    1,   20,    true,  "Kapak ancak basınç bunun altındayken açılır"),
            P(88,  "HoldN2Flow",            300,  "L/saat",  0,   32000, false, "Korumalı beklemede azot debisi"),
            P(92,  "HoldExitMode",          1,    "0/1",     0,   1,     true,  "0 yalnızca sıcaklık düşünce, 1 komutla da korumalı beklemeden çıkılabilir"),

            // Vakum ve basınç
            P(30,  "VacValveDelaySec",      10,   "sn",      0,   600,   false, "Motor çalıştıktan sonra vakum vanasının açılma gecikmesi"),
            P(32,  "VacFillTimeoutMin",     15,   "dk",      1,   240,   false, "Azot dolum süre sınırı"),
            P(34,  "VacFillTargetMbar",     1,    "mbar",    0,   1000,  false, "Azot dolum hedefi (atmosfere göre)"),
            P(66,  "PressureRange",         5,    "mbar",    0,   100,   false, "Basınç kontrolü histerezisi"),

            // Soğutma ve su soğutma
            P(36,  "KlepeTimeoutSec",       30,   "sn",      1,   600,   false, "Klepenin konuma gelme süresi"),
            P(38,  "WaterCoolEnable",       0,    "0/1",     0,   1,     false, "Eşanjörlü su soğutma kullanılsın mı"),
            P(40,  "WaterCoolStartTemp",    300,  "°C",      50,  750,   false, "Su soğutmanın başlama sıcaklığı"),
            P(42,  "WaterFeedTimeSec",      90,   "sn",      1,   3600,  false, "Besleme süresi"),
            P(44,  "WaterPumpTimeSec",      1200, "sn",      1,   7200,  false, "Boşaltma pompası süresi"),
            P(46,  "WaterPumpMaxTemp",      100,  "°C",      20,  300,   false, "Pompanın çalışabileceği en yüksek sıcaklık"),

            // Isıtma ve sirkülasyon
            P(48,  "FanDefaultSpeed",       100,  "%",       1,   100,   false, "Reçetede FanSpeed 0 ise kullanılan hız"),
            P(50,  "FanMinSpeed",           30,   "%",       0,   100,   false, "İzinli en düşük hız"),
            P(52,  "DengeRange",            2,    "°C",      0,   100,   false, "Cehennemlik dengeleme bandı"),
            P(56,  "GsoakTimeoutMin",       60,   "dk",      0,   1440,  false, "Gsoak bu kadar sürerse alarm 31"),
            P(74,  "RampStartMode",         0,    "0/1",     0,   1,     false, "İlk ısıtma adımında rampa başlangıcı: 0 o anki sıcaklık, 1 RampStartTemp"),
            P(76,  "RampStartTemp",         50,   "°C",      0,   750,   false, "RampStartMode = 1 iken başlangıç sıcaklığı"),
            P(70,  "PowerLossAdvance",      0,    "0/1",     0,   1,     false, "1: menevişte enerji kesintisi süresi adıma eklenir"),

            // Gaz hatları ve debiler
            P(58,  "N2_MfcMaxLph",          0,    "L/saat",  0,   32767, false, "Azot MFC tam skalası. Girilmeli; 0 iken MFC ölçeklenemez"),
            P(60,  "NH3_MfcMaxLph",         0,    "L/saat",  0,   32767, false, "Amonyak MFC tam skalası. Girilmeli"),
            P(62,  "MfcDevPerc",            10,   "%",       0,   100,   false, "Debi sapma alarm eşiği (alarm 23, 24)"),
            P(64,  "MfcDevDelaySec",        30,   "sn",      0,   3600,  false, "Sapma alarm gecikmesi"),
            P(72,  "CO2_FlowLph",           0,    "L/saat",  0,   32767, false, "CO2 rotometresinde ayarlı debi (Kn hesabı ve CO2 toplamı için). Girilmeli"),
            P(82,  "FillN2_FlowLph",        0,    "L/saat",  0,   32767, false, "Harici azot (dolum) hattı rotometre debisi. Girilmeli"),
            P(84,  "WaterDose_mlh",         0,    "ml/saat", 0,   32767, false, "Su dozaj pompasında ayarlı debi. Girilmeli"),
            P(86,  "EmgN2_FlowLph",         0,    "L/saat",  0,   32767, false, "Acil azot hattı rotometre debisi. Girilmeli"),

            // Haberleşme ve termokupl kanalları
            P(68,  "ScadaWatchdogSec",      10,   "sn",      0,   600,   false, "SCADA yaşam sinyali zaman aşımı; 0 = izleme kapalı"),
            P(80,  "TcEnableMask",          63,   "bit",     0,   255,   false, "Bit n = kanal n açık. PLC kanal 0, 1, 2'yi her zaman açık tutar", idleOnly: true),

            // Kaçak testi
            P(96,  "LeakMaxTemp",           60,   "°C",      30,  200,   false, "Test ancak fırın bu sıcaklığın altındayken yapılır"),
            P(98,  "LeakVacTarget",         50,   "mbar",    1,   900,   false, "İnilecek vakum (mutlak)"),
            P(100, "LeakPumpTimeoutMin",    15,   "dk",      1,   120,   false, "Hedef vakuma inme süre sınırı"),
            P(102, "LeakSettleSec",         120,  "sn",      0,   3600,  false, "Ölçümden önce dinlenme süresi"),
            P(104, "LeakTestMin",           10,   "dk",      1,   240,   false, "Ölçüm süresi"),
            P(106, "LeakMaxRise",           50,   "mbar x10", 0,  10000, false, "İzin verilen artış (50 = 5,0 mbar)")
        };

        // Verilen adresteki parametrenin tanımını döner; adres katalogda yoksa null döner.
        public static ParamDefinition? Find(int address)
        {
            return All.FirstOrDefault(p => p.Address == address);
        }

        // Katalog satırlarını kısa yazabilmek için parametre tanımı üretir.
        private static ParamDefinition P(int address, string name, int defaultValue, string unit, int min, int max, bool isSafety, string description, bool idleOnly = false)
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
                IdleOnly = idleOnly,
                Description = description
            };
        }
    }
}