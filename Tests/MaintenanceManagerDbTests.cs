using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Dtos;
using Xunit;

namespace Tests
{
    // Eski verinin temizlenmesi kurallarını doğrulayan testler
    [Collection("Database")]
    public class MaintenanceManagerDbTests
    {
        private static readonly DateTime Now = new DateTime(2026, 6, 1, 4, 0, 0);

        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public MaintenanceManagerDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Hiçbir şeyi silmeyen (bütün süreler 0) bakım ayarı üretir.
        private static MaintenanceOptions KeepEverything()
        {
            return new MaintenanceOptions
            {
                IdleLogKeepDays = 0,
                BatchLogKeepDays = 0,
                AlarmKeepDays = 0,
                AuditKeepDays = 0
            };
        }

        // Verilen gün kadar eski bir ölçüm kaydı ekler; batchId boşsa şarj dışı ölçümdür.
        private static void InsertLog(int daysOld, int? batchId)
        {
            using var context = new NorthwindContext();
            context.ProcessDataLogs.Add(new ProcessDataLog
            {
                Timestamp = Now.AddDays(-daysOld),
                BatchId = batchId,
                TempPota = 100
            });
            context.SaveChanges();
        }

        // Süresi dolan şarj dışı ölçümlerin silindiğini, yenilerinin kaldığını doğrular.
        [Fact]
        public async Task SarjDisiOlcumler_SuresiDolunca_Silinir()
        {
            InsertLog(100, null);
            InsertLog(10, null);
            var options = KeepEverything();
            options.IdleLogKeepDays = 90;

            var result = await new MaintenanceManager().CleanupAsync(options, Now);

            using var context = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Data);
            Assert.Equal(1, context.ProcessDataLogs.Count());
            Assert.Equal(Now.AddDays(-10), context.ProcessDataLogs.Single().Timestamp);
        }

        // Şarj dışı ölçümler silinirken şarja ait eski ölçümlere dokunulmadığını doğrular.
        [Fact]
        public async Task SarjOlcumleri_VarsayilandaSilinmez()
        {
            int batchId = _database.InsertBatch("COMPLETED");
            InsertLog(1000, batchId);
            InsertLog(1000, null);
            var options = KeepEverything();
            options.IdleLogKeepDays = 90;

            await new MaintenanceManager().CleanupAsync(options, Now);

            using var context = new NorthwindContext();
            Assert.Equal(1, context.ProcessDataLogs.Count());
            Assert.Equal(batchId, context.ProcessDataLogs.Single().BatchId);
        }

        // Şarj ölçümleri için süre verilirse yalnızca süresi dolanların silindiğini doğrular.
        [Fact]
        public async Task SarjOlcumleri_SureVerilirse_Silinir()
        {
            int batchId = _database.InsertBatch("COMPLETED");
            InsertLog(800, batchId);
            InsertLog(100, batchId);
            var options = KeepEverything();
            options.BatchLogKeepDays = 730;

            var result = await new MaintenanceManager().CleanupAsync(options, Now);

            using var context = new NorthwindContext();
            Assert.Equal(1, result.Data);
            Assert.Equal(1, context.ProcessDataLogs.Count());
        }

        // Bütün süreler 0 iken hiçbir kaydın silinmediğini doğrular.
        [Fact]
        public async Task SurelerSifirken_HicbirSeySilinmez()
        {
            InsertLog(5000, null);

            var result = await new MaintenanceManager().CleanupAsync(KeepEverything(), Now);

            using var context = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.Data);
            Assert.Equal(1, context.ProcessDataLogs.Count());
        }

        // Eski alarmlardan yalnızca kapanmış olanların silindiğini, süren alarmın kaldığını doğrular.
        [Fact]
        public async Task Alarmlar_YalnizcaKapanmisEskilerSilinir()
        {
            using (var context = new NorthwindContext())
            {
                context.Alarms.Add(new Alarm { TagName = "ALM_001", TriggeredAt = Now.AddDays(-500), IsActive = false });
                context.Alarms.Add(new Alarm { TagName = "ALM_002", TriggeredAt = Now.AddDays(-500), IsActive = true });
                context.Alarms.Add(new Alarm { TagName = "ALM_003", TriggeredAt = Now.AddDays(-10), IsActive = false });
                context.SaveChanges();
            }
            var options = KeepEverything();
            options.AlarmKeepDays = 365;

            await new MaintenanceManager().CleanupAsync(options, Now);

            using var check = new NorthwindContext();
            var tags = check.Alarms.Select(a => a.TagName).OrderBy(t => t).ToList();
            Assert.Equal(new[] { "ALM_002", "ALM_003" }, tags);
        }

        // Süresi dolan işlem kayıtlarının silindiğini doğrular.
        [Fact]
        public async Task IslemKayitlari_SuresiDolunca_Silinir()
        {
            using (var context = new NorthwindContext())
            {
                context.AuditLogs.Add(new AuditLog { ActionType = "ESKI", Description = "eski", Timestamp = Now.AddDays(-400) });
                context.AuditLogs.Add(new AuditLog { ActionType = "YENI", Description = "yeni", Timestamp = Now.AddDays(-5) });
                context.SaveChanges();
            }
            var options = KeepEverything();
            options.AuditKeepDays = 365;

            await new MaintenanceManager().CleanupAsync(options, Now);

            using var check = new NorthwindContext();
            Assert.Equal("YENI", check.AuditLogs.Single().ActionType);
        }

        // Yedekleme kapalıyken hata vermeden "yedek alınmadı" döndüğünü doğrular.
        [Fact]
        public async Task Yedekleme_KapaliykenBirSeyYapmaz()
        {
            var options = KeepEverything();
            options.BackupEnabled = false;

            var result = await new MaintenanceManager().BackupAsync(options, Now);

            Assert.True(result.IsSuccess);
            Assert.False(result.Data);
        }

        // Yedek klasörü tanımlı değilken anlaşılır bir hata döndüğünü doğrular.
        [Fact]
        public async Task Yedekleme_KlasorYoksaHataDoner()
        {
            var options = KeepEverything();
            options.BackupEnabled = true;
            options.BackupFolder = "";

            var result = await new MaintenanceManager().BackupAsync(options, Now);

            Assert.False(result.IsSuccess);
            Assert.Contains("BackupFolder", result.Message);
        }
    }
}