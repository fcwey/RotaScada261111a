using Core.Entities;
using System;

namespace Entities.Concrete
{
    // Sonuçlanmış bir kaçak testinin kaydı
    public class LeakTestRecord : IEntity
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;   // Testin sonuçlandığı an
        public int Result { get; set; }                           // 1 geçti, 2 kaldı, 3..7 tamamlanamadı
        public string ResultText { get; set; } = string.Empty;
        public float RiseMbar { get; set; }                       // Ölçüm süresince basınç artışı
        public float RiseRateMbarPerMin { get; set; }             // Artış hızı (artış / ölçüm süresi)
        public int TestMin { get; set; }                          // Ölçüm süresi (test anındaki parametre)
        public float MaxRiseMbar { get; set; }                    // İzin verilen artış (test anındaki parametre)
        public int VacTargetMbar { get; set; }                    // İnilecek vakum (test anındaki parametre)
        public float TempPota { get; set; }                       // Test sonuçlandığında pota sıcaklığı
    }
}