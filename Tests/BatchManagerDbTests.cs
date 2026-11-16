using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Xunit;

namespace Tests
{
    // PLC'nin bitiş bilgisine (EndReason, BatchId, DB205) göre şarj kaydının kapanmasını doğrulayan testler
    [Collection("Database")]
    public class BatchManagerDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public BatchManagerDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Sahte PLC'nin durumunu ayarlayıp şarj yöneticisine sekans durumunu bildirir.
        private static async Task TrackAsync(BatchManager manager, FakeS7Driver driver, short seqState, int batchId, short endReason)
        {
            FakePlcData.SetStatus(driver, seqState, batchId, endReason);

            var result = await manager.TrackSequenceAsync(seqState);
            Assert.True(result.IsSuccess);
        }

        // Program sürerken şarjın açık kaldığını ve bitiş bilgisinin boş olduğunu doğrular.
        [Fact]
        public async Task ProgramSurerken_SarjAcik()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);

            await TrackAsync(manager, driver, 20, batchId, 0);
            await TrackAsync(manager, driver, 30, batchId, 0);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();
            Assert.Equal("RUNNING", batch.Status);
            Assert.Null(batch.EndReason);
            Assert.Null(batch.ProgramEndTime);
        }

        // Normal bitişte önce bitiş bilgisinin kaydedildiğini, şarjın ise fırın beklemeye dönünce kapandığını doğrular.
        [Fact]
        public async Task NormalBitis_IkiAdimdaKapanir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);
            await TrackAsync(manager, driver, 50, batchId, 0);

            // Program bitti, reset bekleniyor.
            FakePlcData.PutReal(driver, 201, 256, 84.5f);    // BatchEnergyKwh
            await TrackAsync(manager, driver, 60, batchId, 1);

            using (var context = new NorthwindContext())
            {
                var batch = context.Batches.Single();
                Assert.Equal("RUNNING", batch.Status);       // Henüz kapanmadı
                Assert.Equal(1, batch.EndReason);
                Assert.NotNull(batch.ProgramEndTime);
                Assert.Null(batch.EndTime);
                Assert.Equal(84.5f, batch.EnergyKwh);
            }

            // Reset verildi, fırın beklemeye döndü.
            await TrackAsync(manager, driver, 0, batchId, 1);

            using (var context = new NorthwindContext())
            {
                var batch = context.Batches.Single();
                Assert.Equal("COMPLETED", batch.Status);
                Assert.NotNull(batch.EndTime);
            }
        }

        // Stop sonrası korumalı bekleme sürerken şarjın açık kaldığını, beklemeye dönünce "Stop" notuyla kapandığını doğrular.
        [Fact]
        public async Task Stop_KorumaliBeklemedenSonraKapanir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);
            await TrackAsync(manager, driver, 30, batchId, 0);

            await TrackAsync(manager, driver, 40, batchId, 2);   // Yıkama
            await TrackAsync(manager, driver, 45, batchId, 2);   // Korumalı bekleme

            using (var context = new NorthwindContext())
            {
                Assert.Equal("RUNNING", context.Batches.Single().Status);
            }

            await TrackAsync(manager, driver, 0, batchId, 2);

            using (var context = new NorthwindContext())
            {
                var batch = context.Batches.Single();
                Assert.Equal("ABORTED", batch.Status);
                Assert.Equal(2, batch.EndReason);
                Assert.Contains("Stop", batch.CompletionNote);
            }
        }

        // Güvenli durdurmayla biten şarjın notunda bunun yazdığını doğrular.
        [Fact]
        public async Task GuvenliDurdurma_NottaBelirtilir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);
            await TrackAsync(manager, driver, 30, batchId, 0);

            await TrackAsync(manager, driver, 80, batchId, 3);
            await TrackAsync(manager, driver, 0, batchId, 3);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();
            Assert.Equal("ABORTED", batch.Status);
            Assert.Contains("Güvenli durdurma", batch.CompletionNote);
        }

        // Program bitince DB205'teki adım özetlerinin şarja kaydedildiğini doğrular.
        [Fact]
        public async Task ProgramBitince_AdimOzetleriKaydedilir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);
            await TrackAsync(manager, driver, 30, batchId, 0);

            FakePlcData.SetStepLogHeader(driver, batchId, 5);
            FakePlcData.SetStepRow(driver, 1, 1, 1, 14, 31f, 0f, 0f, 0.5f);
            FakePlcData.SetStepRow(driver, 2, 2, 5, 245, 518.5f, 3.25f, 3120f, 62f);
            FakePlcData.SetStepRow(driver, 3, 3, 1, 180, 310f, 0f, 0f, 4f);

            await TrackAsync(manager, driver, 60, batchId, 1);

            var detail = await manager.GetBatchDetailAsync(batchId);

            Assert.Equal(new[] { 1, 2, 3 }, detail.Data.Steps.Select(s => s.StepNo).ToArray());

            var heating = detail.Data.Steps.Single(s => s.StepNo == 2);
            Assert.Equal(2, heating.StepType);
            Assert.Equal(5, heating.Flags);
            Assert.Equal(245, heating.DurationMin);
            Assert.Equal(518.5f, heating.TempAvg);
            Assert.Equal(3.25f, heating.KnAvg);
            Assert.Equal(3120f, heating.NH3L);
            Assert.Equal(62f, heating.EnergyKwh);
        }

        // DB205 başka bir şarja aitse adım özetlerinin bu şarja yazılmadığını doğrular.
        [Fact]
        public async Task AdimOzetiBaskaSarjaAitse_Kaydedilmez()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);
            await TrackAsync(manager, driver, 30, batchId, 0);

            FakePlcData.SetStepLogHeader(driver, batchId + 100, 5);
            FakePlcData.SetStepRow(driver, 1, 2, 1, 60, 400f, 0f, 0f, 10f);

            await TrackAsync(manager, driver, 60, batchId, 1);

            using var context = new NorthwindContext();
            Assert.Equal(0, context.BatchStepSummaries.Count());
            Assert.Equal(1, context.Batches.Single().EndReason);   // Bitiş bilgisi yine de kaydedildi
        }

        // Gaz toplamlarının kapanışta son değerlerle yazıldığını (yıkama tüketimi dahil), enerjinin de kaydedildiğini doğrular.
        [Fact]
        public async Task Kapanista_SonToplamlarYazilir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);
            await TrackAsync(manager, driver, 30, batchId, 0);

            FakePlcData.PutReal(driver, 201, 132, 1000f);    // N2Total, program bittiği an
            FakePlcData.PutReal(driver, 201, 136, 3100f);    // NH3Total
            await TrackAsync(manager, driver, 45, batchId, 2);

            // Korumalı beklemede azot tüketimi sürdü.
            FakePlcData.PutReal(driver, 201, 132, 1450f);
            FakePlcData.PutReal(driver, 201, 288, 12.5f);    // CO2Total
            FakePlcData.PutReal(driver, 201, 292, 0.5f);     // WaterTotal
            FakePlcData.PutReal(driver, 201, 296, 800f);     // FillN2Total
            FakePlcData.PutReal(driver, 201, 320, 25f);      // EmgN2Total
            await TrackAsync(manager, driver, 0, batchId, 2);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();
            Assert.Equal(1450f, batch.N2TotalL);
            Assert.Equal(3100f, batch.NH3TotalL);
            Assert.Equal(12.5f, batch.CO2TotalL);
            Assert.Equal(0.5f, batch.WaterTotalL);
            Assert.Equal(800f, batch.FillN2TotalL);
            Assert.Equal(25f, batch.EmgN2TotalL);
        }

        // SCADA kapalıyken biten şarjın açılışta bitiş bilgisi ve adım özetleriyle birlikte kapatıldığını doğrular.
        [Fact]
        public async Task ScadaKapaliykenBiten_AcilistaAlinir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();

            FakePlcData.SetStepLogHeader(driver, batchId, 5);
            FakePlcData.SetStepRow(driver, 1, 2, 1, 120, 400f, 0f, 0f, 30f);
            FakePlcData.PutReal(driver, 201, 256, 33f);

            // SCADA yeni açıldı: PLC beklemede, son programın şarj numarası açık şarjla aynı.
            await TrackAsync(new BatchManager(driver), driver, 0, batchId, 1);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();
            Assert.Equal("COMPLETED", batch.Status);
            Assert.Equal(33f, batch.EnergyKwh);
            Assert.Equal(1, context.BatchStepSummaries.Count());
        }

        // Açılışta PLC'deki şarj numarası açık şarjla uyuşmuyorsa sonucun bilinmiyor olarak kapandığını doğrular.
        [Fact]
        public async Task SarjNumarasiUyusmazsa_SonucBilinmiyor()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            FakePlcData.PutReal(driver, 201, 132, 999f);     // Başka şarjın azot toplamı

            await TrackAsync(new BatchManager(driver), driver, 0, batchId + 7, 1);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();
            Assert.Equal("UNKNOWN", batch.Status);
            Assert.Null(batch.EndReason);
            Assert.Equal(0f, batch.N2TotalL);                // Başka şarjın toplamı yazılmadı
        }

        // Daha önce kapanmış şarjın yeniden değiştirilmediğini doğrular.
        [Fact]
        public async Task KapanmisSarja_Dokunulmaz()
        {
            int batchId = _database.InsertBatch("COMPLETED");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);

            await TrackAsync(manager, driver, 30, batchId, 0);
            await TrackAsync(manager, driver, 0, batchId, 2);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();
            Assert.Equal("COMPLETED", batch.Status);
            Assert.Null(batch.EndReason);
        }

        // Sekans durumu değişmediğinde PLC'nin yeniden okunmadığını (şarjın değişmediğini) doğrular.
        [Fact]
        public async Task DurumDegismezse_IslemYapilmaz()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var driver = new FakeS7Driver();
            var manager = new BatchManager(driver);
            await TrackAsync(manager, driver, 30, batchId, 0);

            // PLC'de bitiş sebebi doldu ama sekans durumu aynı bildirildi: tetikleme olmaz.
            await TrackAsync(manager, driver, 30, batchId, 1);

            using var context = new NorthwindContext();
            Assert.Null(context.Batches.Single().EndReason);
        }

        // PLC sürücüsü olmadan şarj takibinin anlaşılır bir hata döndüğünü doğrular.
        [Fact]
        public async Task SurucuYokken_HataDoner()
        {
            var result = await new BatchManager().TrackSequenceAsync(30);

            Assert.False(result.IsSuccess);
        }
    }
}