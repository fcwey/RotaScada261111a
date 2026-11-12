using Core.Entities;

namespace Entities.Dtos
{
    // Grafiği çizilebilecek bir ölçümün tanımı
    public class TrendTagDto : IDto
    {
        public string Tag { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    // Tek bir ölçümün zaman dilimlerine göre değerleri (dizilerin sırası Timestamps ile aynıdır)
    public class TrendSeriesDto : IDto
    {
        public string Tag { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public List<float> Values { get; set; } = new();   // Dilimin ortalaması
        public List<float> Min { get; set; } = new();      // Dilimdeki en düşük değer
        public List<float> Max { get; set; } = new();      // Dilimdeki en yüksek değer (tepe değerler burada korunur)
    }

    // Trend sorgusunun cevabı: ortak zaman ekseni ve her ölçüm için bir seri
    public class TrendDataDto : IDto
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int BucketSeconds { get; set; }             // Her noktanın temsil ettiği süre
        public List<DateTime> Timestamps { get; set; } = new();
        public List<TrendSeriesDto> Series { get; set; } = new();
    }
}