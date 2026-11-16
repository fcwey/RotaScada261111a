using Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Concrete
{
    // Şarj kaydı. Id, Start komutuyla PLC'ye şarj numarası (BatchId) olarak gönderilir.
    public class Batch : IEntity
    {
        public int Id { get; set; }
        public string BatchCode { get; set; } = string.Empty;   // Örn: "BATCH-20261116-174500"
        public int RecipeId { get; set; }
        public int OperatorUserId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }                  // Şarjın kapandığı an (fırın beklemeye döndü)
        public string Status { get; set; } = "RUNNING";         // STARTING, RUNNING, COMPLETED, ABORTED, UNKNOWN
        public string? CompletionNote { get; set; }

        // PLC'den alınan bitiş bilgisi
        public int? EndReason { get; set; }                     // 1 tamamlandı, 2 stop, 3 güvenli durdurma
        public DateTime? ProgramEndTime { get; set; }           // Programın bittiği an (yıkama ve korumalı bekleme öncesi)

        // Şarj toplamları (PLC sayaçlarından)
        public float EnergyKwh { get; set; }                    // Program bitince dondurulan enerji
        public float N2TotalL { get; set; }                     // MFC'den geçen azot
        public float NH3TotalL { get; set; }                    // Amonyak
        public float CO2TotalL { get; set; }                    // CO2 (hesap)
        public float WaterTotalL { get; set; }                  // Su (hesap)
        public float FillN2TotalL { get; set; }                 // Vakum dolumunda harcanan azot (hesap)
        public float EmgN2TotalL { get; set; }                  // Acil azot hattından giden azot (hesap)

        // Navigation
        public virtual Recipe? Recipe { get; set; }
    }
}