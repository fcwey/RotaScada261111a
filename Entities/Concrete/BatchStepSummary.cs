using Core.Entities;

namespace Entities.Concrete
{
    // Bir şarjın tek bir reçete adımında gerçekleşen değerlerin özeti (PLC DB205'ten alınır)
    public class BatchStepSummary : IEntity
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public int StepNo { get; set; }             // Reçetedeki adım numarası
        public int StepType { get; set; }           // 1 vakum, 2 ısıtma, 3 soğutma
        public int Flags { get; set; }              // Bit 0 tamamlandı, 1 CO2 verildi (Kn yaklaşık), 2 H2 otomatik çalıştı, 3 sürüyor

        public int DurationMin { get; set; }        // Adımda geçen gerçek süre, duraklatmalar dahil
        public int GsoakWaitMin { get; set; }       // Gsoak yüzünden beklenen süre
        public int HoldMin { get; set; }            // Duraklatılmış geçen süre
        public int H2InBandMin { get; set; }        // H2'nin set bandında kaldığı süre

        public float TempAvg { get; set; }          // Pota sıcaklığı (°C)
        public float TempMin { get; set; }
        public float TempMax { get; set; }
        public float KnAvg { get; set; }            // Nitrasyon potansiyeli
        public float KnMin { get; set; }
        public float KnMax { get; set; }
        public float H2Avg { get; set; }            // %

        public float N2L { get; set; }              // Adımda MFC'den geçen azot (L)
        public float NH3L { get; set; }             // Amonyak (L)
        public float CO2L { get; set; }             // CO2 (L, hesap)
        public float EnergyKwh { get; set; }        // Adımın enerjisi
    }
}