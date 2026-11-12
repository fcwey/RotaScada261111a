using Business.Concrete;
using Entities.Concrete;
using Xunit;

namespace Tests
{
    // Trend sorgusunun (zaman dilimi, ortalama, en düşük, en yüksek) testleri
    [Collection("Database")]
    public class TrendSeriesDbTests
    {
        private static readonly DateTime BaseTime = new DateTime(2026, 1, 1, 10, 0, 0);

        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public TrendSeriesDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Verilen aralıkla, pota sıcaklığı sıra numarasına ve basıncı on katına eşit ölçüm kayıtları ekler.
        private static async Task InsertLogsAsync(int count, int intervalSeconds, int? batchId = null)
        {
            var manager = new HistorianManager();
            for (int i = 0; i < count; i++)
            {
                await manager.LogProcessDataAsync(new ProcessDataLog
                {
                    Timestamp = BaseTime.AddSeconds(i * intervalSeconds),
                    BatchId = batchId,
                    TempPota = i,
                    Pressure = i * 10
                });
            }
        }

        // Ölçüm listesinin birimleriyle birlikte geldiğini doğrular.
        [Fact]
        public void Etiketler_BirimleriyleGelir()
        {
            var tags = new HistorianManager().GetTrendTags().Data;

            Assert.Contains(tags, t => t.Tag == "TempPota" && t.Unit == "°C");
            Assert.Contains(tags, t => t.Tag == "Pressure" && t.Unit == "mbar");
            Assert.Equal(16, tags.Count);
        }

        // Nokta sayısı yeterliyken her kaydın ayrı nokta olarak ve değeri değişmeden geldiğini doğrular.
        [Fact]
        public async Task KisaAralik_HerKayitAyriNokta()
        {
            await InsertLogsAsync(10, 60);

            var result = await new HistorianManager().GetTrendSeriesAsync(
                new[] { "TempPota" }, BaseTime, BaseTime.AddMinutes(9), 1000);

            Assert.True(result.IsSuccess);
            Assert.Equal(10, result.Data.Timestamps.Count);
            Assert.Equal(BaseTime, result.Data.Timestamps.First());

            var series = result.Data.Series.Single();
            Assert.Equal("TempPota", series.Tag);
            Assert.Equal("°C", series.Unit);
            Assert.Equal(Enumerable.Range(0, 10).Select(i => (float)i).ToArray(), series.Values.ToArray());
            Assert.Equal(series.Values, series.Min);   // Dilimde tek kayıt varken en düşük, en yüksek ve ortalama aynı
            Assert.Equal(series.Values, series.Max);
        }

        // Birden fazla ölçümün istenen sırayla ve doğru değerlerle geldiğini doğrular.
        [Fact]
        public async Task IkiOlcum_IstenenSiraylaGelir()
        {
            await InsertLogsAsync(5, 60);

            var result = await new HistorianManager().GetTrendSeriesAsync(
                new[] { "Pressure", "TempPota" }, BaseTime, BaseTime.AddMinutes(4), 1000);

            Assert.Equal(new[] { "Pressure", "TempPota" }, result.Data.Series.Select(s => s.Tag).ToArray());
            Assert.Equal(new[] { 0f, 10f, 20f, 30f, 40f }, result.Data.Series[0].Values.ToArray());
            Assert.Equal(new[] { 0f, 1f, 2f, 3f, 4f }, result.Data.Series[1].Values.ToArray());
        }

        // Ölçüm adının büyük küçük harfe bakmadan eşlendiğini doğrular.
        [Fact]
        public async Task OlcumAdi_BuyukKucukHarfeBakmaz()
        {
            await InsertLogsAsync(3, 60);

            var result = await new HistorianManager().GetTrendSeriesAsync(
                new[] { "temppota" }, BaseTime, BaseTime.AddMinutes(2), 1000);

            Assert.True(result.IsSuccess);
            Assert.Equal("TempPota", result.Data.Series.Single().Tag);
        }

        // Uzun aralıkta noktaların azaltıldığını ama tepe değerin en yüksek dizisinde korunduğunu doğrular.
        [Fact]
        public async Task Seyreltme_TepeDegeriKorur()
        {
            await InsertLogsAsync(60, 10);

            // Araya kısa süren bir sıcaklık aşımı eklenir.
            await new HistorianManager().LogProcessDataAsync(new ProcessDataLog
            {
                Timestamp = BaseTime.AddSeconds(305),
                TempPota = 999
            });

            var result = await new HistorianManager().GetTrendSeriesAsync(
                new[] { "TempPota" }, BaseTime, BaseTime.AddSeconds(590), 10);

            var series = result.Data.Series.Single();

            Assert.True(result.IsSuccess);
            Assert.Equal(59, result.Data.BucketSeconds);
            Assert.InRange(result.Data.Timestamps.Count, 9, 12);    // 61 kayıt yaklaşık 10 noktaya indi
            Assert.Equal(999f, series.Max.Max());                    // Aşım en yüksek dizisinde duruyor
            Assert.True(series.Values.Max() < 999f);                 // Ortalamada ise yumuşamış
            Assert.Equal(0f, series.Min.Min());
        }

        // Sorgunun yalnızca istenen zaman aralığını döndüğünü doğrular.
        [Fact]
        public async Task YalnizcaIstenenAralik()
        {
            await InsertLogsAsync(10, 60);

            var result = await new HistorianManager().GetTrendSeriesAsync(
                new[] { "TempPota" }, BaseTime.AddMinutes(2), BaseTime.AddMinutes(4), 1000);

            Assert.Equal(new[] { 2f, 3f, 4f }, result.Data.Series.Single().Values.ToArray());
        }

        // Aralıkta kayıt yokken boş ama geçerli bir cevap döndüğünü doğrular.
        [Fact]
        public async Task KayitYokken_BosCevap()
        {
            var result = await new HistorianManager().GetTrendSeriesAsync(
                new[] { "TempPota" }, BaseTime, BaseTime.AddHours(1), 1000);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Data.Timestamps);
            Assert.Empty(result.Data.Series.Single().Values);
        }

        // Bilinmeyen ölçüm adında hangi adın bilinmediğini söyleyen bir hata döndüğünü doğrular.
        [Fact]
        public async Task BilinmeyenOlcum_HataDoner()
        {
            var result = await new HistorianManager().GetTrendSeriesAsync(
                new[] { "TempPota", "OlmayanOlcum" }, BaseTime, BaseTime.AddHours(1), 1000);

            Assert.False(result.IsSuccess);
            Assert.Contains("OlmayanOlcum", result.Message);
        }

        // Ölçüm adı verilmezse ve başlangıç bitişten sonraysa hata döndüğünü doğrular.
        [Fact]
        public async Task GecersizIstek_HataDoner()
        {
            var manager = new HistorianManager();

            var noTags = await manager.GetTrendSeriesAsync(Array.Empty<string>(), BaseTime, BaseTime.AddHours(1), 1000);
            var reversed = await manager.GetTrendSeriesAsync(new[] { "TempPota" }, BaseTime.AddHours(1), BaseTime, 1000);

            Assert.False(noTags.IsSuccess);
            Assert.False(reversed.IsSuccess);
        }

        // Şarj trendinin yalnızca o şarja ait ölçümleri getirdiğini doğrular.
        [Fact]
        public async Task SarjTrendi_YalnizcaOSarjinOlcumleri()
        {
            int batchId = _database.InsertBatch("COMPLETED");   // Başlangıcı bir saat önce, bitişi boş
            var manager = new HistorianManager();
            DateTime now = DateTime.Now;

            await manager.LogProcessDataAsync(new ProcessDataLog { Timestamp = now.AddMinutes(-40), BatchId = batchId, TempPota = 400 });
            await manager.LogProcessDataAsync(new ProcessDataLog { Timestamp = now.AddMinutes(-30), BatchId = batchId, TempPota = 500 });
            await manager.LogProcessDataAsync(new ProcessDataLog { Timestamp = now.AddMinutes(-35), BatchId = null, TempPota = 20 });

            var result = await manager.GetBatchTrendAsync(batchId, new[] { "TempPota" }, 5000);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { 400f, 500f }, result.Data.Series.Single().Values.ToArray());
        }

        // Olmayan şarjın trendi istenirse hata döndüğünü doğrular.
        [Fact]
        public async Task SarjTrendi_OlmayanSarj()
        {
            var result = await new HistorianManager().GetBatchTrendAsync(987654, new[] { "TempPota" }, 1000);

            Assert.False(result.IsSuccess);
        }
    }
}