using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Xunit;

namespace Tests
{
    // Ölçüm kaydı, trend sorgusu ve eski veri temizliği testleri
    [Collection("Database")]
    public class HistorianManagerDbTests
    {
        private static readonly DateTime BaseTime = new DateTime(2026, 1, 1, 10, 0, 0);

        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public HistorianManagerDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Birer dakika arayla, pota sıcaklığı sıra numarasına eşit olan ölçüm kayıtları ekler.
        private static async Task InsertLogsAsync(HistorianManager manager, int count)
        {
            for (int i = 0; i < count; i++)
            {
                await manager.LogProcessDataAsync(new ProcessDataLog
                {
                    Timestamp = BaseTime.AddMinutes(i),
                    TempPota = i,
                    Pressure = i * 10
                });
            }
        }

        // Açık şarj varken yazılan ölçümün o şarjın kimliğiyle etiketlendiğini doğrular.
        [Fact]
        public async Task Log_AcikSarjlaEtiketlenir()
        {
            int batchId = _database.InsertBatch("RUNNING");

            var result = await new HistorianManager().LogProcessDataAsync(new ProcessDataLog { TempPota = 500 });

            using var context = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.Equal(batchId, context.ProcessDataLogs.Single().BatchId);
        }

        // Açık şarj yokken yazılan ölçümün şarj kimliğinin boş kaldığını doğrular.
        [Fact]
        public async Task Log_SarjYokkenEtiketsiz()
        {
            _database.InsertBatch("COMPLETED");

            await new HistorianManager().LogProcessDataAsync(new ProcessDataLog { TempPota = 500 });

            using var context = new NorthwindContext();
            Assert.Null(context.ProcessDataLogs.Single().BatchId);
        }

        // Ölçüm kayıtlarının işlem kaydı (audit) tablosunu doldurmadığını doğrular.
        [Fact]
        public async Task Log_AuditTablosunaYazmaz()
        {
            await InsertLogsAsync(new HistorianManager(), 3);

            using var context = new NorthwindContext();
            Assert.Equal(3, context.ProcessDataLogs.Count());
            Assert.Equal(0, context.AuditLogs.Count());
        }

        // Trend sorgusunun istenen etiketin değerlerini zaman sırasıyla döndüğünü doğrular.
        [Fact]
        public async Task Trend_DegerleriZamanSirasiylaDoner()
        {
            var manager = new HistorianManager();
            await InsertLogsAsync(manager, 10);

            var result = await manager.GetTrendDataAsync("TempPota", BaseTime, BaseTime.AddMinutes(9));

            Assert.True(result.IsSuccess);
            Assert.Equal(10, result.Data.Count);
            Assert.Equal(0f, result.Data.First().Value);
            Assert.Equal(9f, result.Data.Last().Value);
        }

        // Trend sorgusunun yalnızca istenen zaman aralığını döndüğünü doğrular.
        [Fact]
        public async Task Trend_YalnizcaIstenenAralik()
        {
            var manager = new HistorianManager();
            await InsertLogsAsync(manager, 10);

            var result = await manager.GetTrendDataAsync("Pressure", BaseTime.AddMinutes(2), BaseTime.AddMinutes(4));

            Assert.Equal(new[] { 20f, 30f, 40f }, result.Data.Select(p => p.Value).ToArray());
        }

        // Hedef nokta sayısı kayıt sayısından azsa verinin seyrekleştirildiğini doğrular.
        [Fact]
        public async Task Trend_Seyreklestirir()
        {
            var manager = new HistorianManager();
            await InsertLogsAsync(manager, 10);

            var result = await manager.GetTrendDataAsync("TempPota", BaseTime, BaseTime.AddMinutes(9), 5);

            Assert.Equal(5, result.Data.Count);
        }

        // Aralıkta kayıt yoksa boş liste döndüğünü doğrular.
        [Fact]
        public async Task Trend_KayitYoksaBosListe()
        {
            var result = await new HistorianManager().GetTrendDataAsync("TempPota", BaseTime, BaseTime.AddHours(1));

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Data);
        }

        // Temizliğin yalnızca verilen tarihten eski kayıtları sildiğini doğrular.
        [Fact]
        public async Task Temizlik_YalnizcaEskileriSiler()
        {
            var manager = new HistorianManager();
            await InsertLogsAsync(manager, 10);

            var result = await manager.CompressOldLogsAsync(BaseTime.AddMinutes(4));

            using var context = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.Equal(6, context.ProcessDataLogs.Count());   // 4., 5., ... 9. dakika kaldı
            Assert.Equal(BaseTime.AddMinutes(4), context.ProcessDataLogs.Min(p => p.Timestamp));
        }
    }
}