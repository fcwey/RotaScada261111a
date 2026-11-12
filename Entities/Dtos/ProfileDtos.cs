using Core.Entities;

namespace Entities.Dtos
{
    // Teorik sıcaklık eğrisinin bir köşe noktası (aynı parçadaki noktalar düz çizgiyle birleştirilir)
    public class ProfilePointDto : IDto
    {
        public int Segment { get; set; }            // Eğri parçası; her adıma atlamada yeni parça başlar
        public double Minute { get; set; }          // Isıtmanın başlangıcından itibaren dakika
        public DateTime? Timestamp { get; set; }    // Gerçek saat; ısıtma henüz başlamadıysa boş
        public float Temp { get; set; }             // °C
        public int StepNo { get; set; }             // Bu noktada biten adım (parçanın ilk noktasında başlayan adım)
    }

    // Reçeteden hesaplanan teorik sıcaklık eğrisi
    public class RecipeProfileDto : IDto
    {
        public int? BatchId { get; set; }               // Şarja ait eğriyse şarjın numarası
        public int RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public float StartTemp { get; set; }            // İlk rampanın başladığı sıcaklık
        public double TotalMinutes { get; set; }        // Isıtma başlangıcından planlanan bitişe kadar geçen süre
        public bool IsAnchored { get; set; }            // Gerçek saatlere oturtuldu mu (ısıtma başladı mı)
        public DateTime? HeatingStart { get; set; }     // Isıtmanın gerçekten başladığı an
        public DateTime? PlannedEnd { get; set; }       // Bundan sonra gecikme olmazsa ısıtmanın biteceği an
        public int? CoolingEndTemp { get; set; }        // Soğutmanın biteceği sıcaklık (süresi belli değildir)
        public bool IsOriginal { get; set; }            // true: başlangıçtaki plan, false: şu an geçerli plan
        public List<DateTime> EditTimes { get; set; } = new();   // Çalışırken reçetenin değiştirildiği anlar
        public List<DateTime> JumpTimes { get; set; } = new();   // Adıma atlama ve soğutmaya geçiş anları
        public List<ProfilePointDto> Points { get; set; } = new();
    }
}