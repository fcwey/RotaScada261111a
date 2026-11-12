using Entities.Structures;
using Xunit;

namespace Tests
{
    // Alarm listesinin, sınıflarının ve sınıf ezme ayarının testleri
    public class AlarmCatalogTests
    {
        // Alarm etiketinin üç haneli numarayla üretildiğini doğrular.
        [Fact]
        public void GetTag_UcHaneliNumaraUretir()
        {
            Assert.Equal("ALM_001", AlarmCatalog.GetTag(1));
            Assert.Equal("ALM_022", AlarmCatalog.GetTag(22));
        }

        // Kılavuzdaki 47 alarmın hepsinin tanımlı bir metni olduğunu doğrular.
        [Fact]
        public void GetText_Ilk47AlarmTanimli()
        {
            for (int alarmNo = 1; alarmNo <= 47; alarmNo++)
                Assert.DoesNotContain("Tanımsız", AlarmCatalog.GetText(alarmNo));
        }

        // Listede olmayan alarm numarasının "Tanımsız" metniyle döndüğünü doğrular.
        [Fact]
        public void GetText_TanimsizAlarm()
        {
            Assert.Contains("Tanımsız", AlarmCatalog.GetText(60));
        }

        // Varsayılan sınıfların kılavuzla aynı olduğunu sınır numaralarında doğrular.
        [Theory]
        [InlineData(1, 2)]    // Acil stop: güvenli durdur
        [InlineData(8, 2)]    // Son güvenli durdur alarmı
        [InlineData(9, 1)]    // İlk duraklat alarmı
        [InlineData(22, 1)]   // Son duraklat alarmı
        [InlineData(23, 0)]   // İlk uyarı
        [InlineData(47, 0)]   // Son uyarı
        public void GetClass_VarsayilanSiniflar(int alarmNo, int expectedClass)
        {
            AlarmCatalog.SetClassOverrides(new Dictionary<int, int>());

            Assert.Equal(expectedClass, AlarmCatalog.GetClass(alarmNo));
            Assert.Equal(expectedClass + 1, AlarmCatalog.GetPriority(alarmNo));
        }

        // Ayarlardan gelen sınıfın varsayılanı ezdiğini ve temizlenince geri döndüğünü doğrular.
        [Fact]
        public void SetClassOverrides_VarsayilaniEzer()
        {
            try
            {
                AlarmCatalog.SetClassOverrides(new Dictionary<int, int> { { 22, 2 } });

                Assert.Equal(2, AlarmCatalog.GetClass(22));
                Assert.Equal(1, AlarmCatalog.GetClass(21));
            }
            finally
            {
                AlarmCatalog.SetClassOverrides(new Dictionary<int, int>());
            }

            Assert.Equal(1, AlarmCatalog.GetClass(22));
        }

        // Aralık dışı alarm numarası ve sınıf değerlerinin yok sayıldığını doğrular.
        [Fact]
        public void SetClassOverrides_GecersizDegerleriYokSayar()
        {
            try
            {
                AlarmCatalog.SetClassOverrides(new Dictionary<int, int> { { 22, 5 }, { 0, 1 }, { 99, 1 } });

                Assert.Equal(1, AlarmCatalog.GetClass(22));
            }
            finally
            {
                AlarmCatalog.SetClassOverrides(new Dictionary<int, int>());
            }
        }
    }
}