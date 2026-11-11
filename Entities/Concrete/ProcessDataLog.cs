using Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Concrete
{
    // Historian kaydı: DB201'den alınan ölçümlerin zaman damgalı kopyası
    public class ProcessDataLog : IEntity
    {
        public long Id { get; set; }
        public int? BatchId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // Sekans
        public short SeqState { get; set; }
        public short ActiveStep { get; set; }

        // Sıcaklıklar (°C)
        public float TempPota { get; set; }
        public float TempAlt { get; set; }
        public float TempUst { get; set; }
        public float TempPotaAlt { get; set; }
        public float TempPotaUst { get; set; }
        public float TempMalzeme { get; set; }
        public float TempSet { get; set; }

        // Basınç (mbar)
        public float Pressure { get; set; }
        public float PressureSet { get; set; }

        // Gazlar
        public float N2FlowAct { get; set; }
        public float NH3FlowAct { get; set; }
        public float H2Act { get; set; }
        public float Kn { get; set; }

        // Çıkışlar (%)
        public float HeatOutAlt { get; set; }
        public float HeatOutUst { get; set; }
        public float FanSpeedAct { get; set; }
    }
}