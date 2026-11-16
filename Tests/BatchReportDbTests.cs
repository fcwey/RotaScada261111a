using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using System.Text;
using Xunit;

namespace Tests
{
    // Şarj raporunun içeriğini ve mimik durumunun yeni bitlerini doğrulayan testler
    [Collection("Database")]
    public class BatchReportDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public BatchReportDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Raporun bitiş sebebini, toplamları, adım özetini, müdahaleyi ve alarmı içerdiğini doğrular.
        [Fact]
        public async Task Rapor_YeniVerileriIcerir()
        {
            int batchId = _database.InsertBatch("ABORTED");

            using (var context = new NorthwindContext())
            {
                var batch = context.Batches.Single();
                batch.EndReason = 2;
                batch.ProgramEndTime = batch.StartTime.AddMinutes(40);
                batch.EndTime = batch.StartTime.AddMinutes(50);
                batch.EnergyKwh = 84.5f;
                batch.NH3TotalL = 3100f;

                context.BatchStepSummaries.Add(new BatchStepSummary
                {
                    BatchId = batchId,
                    StepNo = 2,
                    StepType = 2,
                    Flags = 3,
                    DurationMin = 245,
                    TempAvg = 518.5f,
                    KnAvg = 3.25f,
                    NH3L = 3120f,
                    EnergyKwh = 62f
                });
                context.BatchRecipeRevisions.Add(new BatchRecipeRevision
                {
                    BatchId = batchId,
                    Reason = "GOTO",
                    StepNo = 2,
                    StepMinute = 10,
                    UserId = 1,
                    Timestamp = batch.StartTime.AddMinutes(20)
                });
                context.Alarms.Add(new Alarm
                {
                    TagName = "ALM_022",
                    Description = "Atık gaz brülörü yanmıyor",
                    TriggeredAt = batch.StartTime.AddMinutes(5),
                    IsActive = false
                });
                context.SaveChanges();
            }

            var result = await new ReportManager().GenerateBatchPdfReportAsync(batchId);
            string text = Encoding.UTF8.GetString(result.Data);

            Assert.True(result.IsSuccess);
            Assert.Contains("Stop komutuyla bitirildi", text);
            Assert.Contains("84.5 kWh", text);
            Assert.Contains("3100.0 L", text);
            Assert.Contains("518.5", text);
            Assert.Contains("CO2 verildi, Kn yaklaşık", text);
            Assert.Contains("Adım 2, 10. dakikaya atlandı", text);
            Assert.Contains("ALM_022", text);
        }

        // Adım özeti ve müdahalesi olmayan şarjda raporun bunu açıkça yazdığını doğrular.
        [Fact]
        public async Task Rapor_BosBolumleriAciklar()
        {
            int batchId = _database.InsertBatch("RUNNING");

            var result = await new ReportManager().GenerateBatchPdfReportAsync(batchId);
            string text = Encoding.UTF8.GetString(result.Data);

            Assert.True(result.IsSuccess);
            Assert.Contains("Adım özeti yok", text);
            Assert.Contains("müdahale olmadan", text);
            Assert.Contains("Alarm oluşmadı", text);
        }

        // Olmayan şarj için raporun hata döndüğünü doğrular.
        [Fact]
        public async Task Rapor_OlmayanSarj()
        {
            var result = await new ReportManager().GenerateBatchPdfReportAsync(987654);

            Assert.False(result.IsSuccess);
        }

        // Mimik durumunun kısıtlı manuel, basınç modu, kilitleme ve arıza bitlerini doğru aktardığını doğrular.
        [Fact]
        public async Task Mimik_YeniBitleriAktarir()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(201, 158, (1 << 8) | (1 << 10));   // Manuel mod + kısıtlı manuel
            driver.SetInt(201, 160, 2);                       // Manuel basınç modu: elle vana
            driver.SetInt(201, 280, 1 << 1);                  // Isıtma isteniyor ama izin yok
            driver.SetInt(201, 282, 1 << 4);                  // Azot MFC arızası
            driver.SetInt(201, 12, 45);                       // Korumalı bekleme

            var result = await new InterlockManager(new PlcManager(driver)).GetMimicStateAsync();
            var mimic = result.Data;

            Assert.True(mimic.IsConnected);
            Assert.True(mimic.ManualMode);
            Assert.True(mimic.RestrictedManual);
            Assert.False(mimic.SimulationMode);
            Assert.Equal(2, (int)mimic.ManualPressureMode);
            Assert.True(mimic.HeatingBlocked);
            Assert.False(mimic.VacuumBlocked);
            Assert.True(mimic.N2MfcFault);
            Assert.False(mimic.NH3MfcFault);
            Assert.Equal("KORUMALI BEKLEME", mimic.SafetyStatusText);
        }
    }
}