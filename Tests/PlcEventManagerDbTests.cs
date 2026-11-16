using Business.Concrete;
using DataAccess.Concrete.EntityFramework;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Structures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests
{
    // Saat eşitleme, kaçak testi kaydı ve PID ayar sonucu izleme testleri
    [Collection("Database")]
    public class PlcEventManagerDbTests
    {
        private DateTime _now = new DateTime(2026, 11, 16, 18, 0, 0);

        // Her testten önce tabloları ve kaçak testi kayıtlarını boşaltır.
        public PlcEventManagerDbTests(DatabaseFixture database)
        {
            database.ClearData();

            using var context = new NorthwindContext();
            context.LeakTestRecords.ExecuteDelete();
        }

        // Sahte PLC'ye bağlı, zamanı testin ilerletebildiği olay yöneticisi üretir.
        private PlcEventManager CreateManager(FakeS7Driver driver, bool clockSync)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Plc:ClockSync", clockSync ? "true" : "false" } })
                .Build();

            return new PlcEventManager(driver, new PlcManager(driver), new AuditTrailManager(new EfAuditLogDal()), configuration)
            {
                Clock = () => _now
            };
        }

        // Sahte PLC'nin o anki durumunu okuyup çözer.
        private static ScadaStatus ReadStatus(FakeS7Driver driver)
        {
            return ScadaStatus.Parse(driver.ReadBytesAsync(201, 0, 336).Result);
        }

        // DB200'e yazılmış saat komutunun parametrelerinden 32 bit zaman değerini geri kurar.
        private static int ReadClockParam(FakeS7Driver driver)
        {
            int low = (ushort)driver.GetInt(200, 4);
            int high = driver.GetInt(200, 6);
            return (high << 16) | low;
        }

        // İlk izlemede PLC saatinin bilgisayarın yerel saatiyle ayarlandığını doğrular.
        [Fact]
        public async Task IlkIzleme_SaatiEsitler()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: true);

            var result = await manager.TrackAsync(ReadStatus(driver));

            int expected = (int)(_now - new DateTime(1970, 1, 1)).TotalSeconds;
            Assert.True(result.IsSuccess);
            Assert.Equal(12, driver.GetInt(200, 2));          // CmdCode = SetClock
            Assert.Equal(expected, ReadClockParam(driver));
            Assert.Contains("saati eşitlendi", result.Message);
        }

        // Eşitleme yapıldıktan sonra 24 saat dolmadan yeniden komut gönderilmediğini, dolunca gönderildiğini doğrular.
        [Fact]
        public async Task SaatEsitleme_GundeBirKez()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: true);
            await manager.TrackAsync(ReadStatus(driver));
            short firstCmdId = driver.GetInt(200, 0);

            _now = _now.AddHours(23);
            await manager.TrackAsync(ReadStatus(driver));
            Assert.Equal(firstCmdId, driver.GetInt(200, 0));

            _now = _now.AddHours(2);
            await manager.TrackAsync(ReadStatus(driver));
            Assert.Equal(firstCmdId + 1, driver.GetInt(200, 0));
        }

        // Yeniden bağlanınca 24 saat beklenmeden saatin yeniden eşitlendiğini doğrular.
        [Fact]
        public async Task YenidenBaglaninca_SaatYenidenEsitlenir()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: true);
            await manager.TrackAsync(ReadStatus(driver));
            short firstCmdId = driver.GetInt(200, 0);

            manager.OnConnected();
            await manager.TrackAsync(ReadStatus(driver));

            Assert.Equal(firstCmdId + 1, driver.GetInt(200, 0));
        }

        // Ayardan kapatıldığında saat komutunun hiç gönderilmediğini doğrular.
        [Fact]
        public async Task SaatEsitleme_KapaliysaGonderilmez()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: false);

            await manager.TrackAsync(ReadStatus(driver));

            Assert.Equal(0, driver.GetInt(200, 0));
            Assert.Equal(0, driver.GetInt(200, 2));
        }

        // PLC saati reddederse hata döndüğünü ve hemen yeniden denenmediğini doğrular.
        [Fact]
        public async Task SaatReddedilirse_BesDakikaSonraYenidenDenenir()
        {
            var driver = new FakeS7Driver { AckResult = 145 };
            var manager = CreateManager(driver, clockSync: true);

            var first = await manager.TrackAsync(ReadStatus(driver));
            short cmdIdAfterFirst = driver.GetInt(200, 0);

            await manager.TrackAsync(ReadStatus(driver));
            Assert.False(first.IsSuccess);
            Assert.Equal(cmdIdAfterFirst, driver.GetInt(200, 0));     // Hemen yeniden denenmedi

            _now = _now.AddMinutes(6);
            await manager.TrackAsync(ReadStatus(driver));
            Assert.Equal(cmdIdAfterFirst + 1, driver.GetInt(200, 0)); // Süre dolunca denendi
        }

        // İlk okumadaki sayaç değerinin kayıt üretmediğini, sayaç artınca testin parametreleriyle kaydedildiğini doğrular.
        [Fact]
        public async Task KacakTesti_SayacArtincaKaydedilir()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: false);

            driver.SetInt(201, 334, 3);                       // LeakCount: SCADA açılmadan önceki testler
            await manager.TrackAsync(ReadStatus(driver));

            using (var context = new NorthwindContext())
            {
                Assert.Equal(0, context.LeakTestRecords.Count());
            }

            // Yeni test sonuçlandı: geçti, 2,5 mbar artış.
            driver.SetInt(204, 98, 50);                       // LeakVacTarget
            driver.SetInt(204, 104, 10);                      // LeakTestMin
            driver.SetInt(204, 106, 50);                      // LeakMaxRise (5,0 mbar)
            driver.SetInt(201, 326, 1);                       // LeakResult
            FakePlcData.PutReal(driver, 201, 328, 2.5f);      // LeakRise
            FakePlcData.PutReal(driver, 201, 48, 41f);        // TempPota
            driver.SetInt(201, 334, 4);

            var result = await manager.TrackAsync(ReadStatus(driver));

            using (var context = new NorthwindContext())
            {
                var record = context.LeakTestRecords.Single();
                Assert.True(result.IsSuccess);
                Assert.Equal(1, record.Result);
                Assert.Equal("Geçti", record.ResultText);
                Assert.Equal(2.5f, record.RiseMbar);
                Assert.Equal(0.25f, record.RiseRateMbarPerMin);
                Assert.Equal(10, record.TestMin);
                Assert.Equal(5f, record.MaxRiseMbar);
                Assert.Equal(50, record.VacTargetMbar);
                Assert.Equal(41f, record.TempPota);
                Assert.Equal(_now, record.Timestamp);
            }
        }

        // Aynı sayaç değeri tekrar okunduğunda ikinci kayıt açılmadığını ve geçmiş sorgusunun kaydı getirdiğini doğrular.
        [Fact]
        public async Task KacakTesti_AyniSayacTekrarKaydedilmez()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: false);
            await manager.TrackAsync(ReadStatus(driver));

            driver.SetInt(201, 326, 2);
            driver.SetInt(201, 334, 1);
            await manager.TrackAsync(ReadStatus(driver));
            await manager.TrackAsync(ReadStatus(driver));

            var history = await manager.GetLeakTestsAsync(_now.AddDays(-1), _now.AddDays(1));

            Assert.True(history.IsSuccess);
            Assert.Single(history.Data);
            Assert.Contains("Kaldı", history.Data[0].ResultText);
        }

        // PID ayarı başarıyla bitince eski ve yeni değerlerin işlem kaydına yazıldığını doğrular.
        [Fact]
        public async Task PidAyar_BasariliBitinceKaydedilir()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: false);

            FakePlcData.PutReal(driver, 201, 268, 2f);        // PidGain
            FakePlcData.PutReal(driver, 201, 272, 300f);      // PidTi
            FakePlcData.PutReal(driver, 201, 276, 40f);       // PidTd
            await manager.TrackAsync(ReadStatus(driver));     // Ayar yok

            driver.SetInt(201, 262, 2);                       // İnce ayar sürüyor
            await manager.TrackAsync(ReadStatus(driver));

            FakePlcData.PutReal(driver, 201, 268, 3.5f);      // Ayar yeni değerleri yazdı
            FakePlcData.PutReal(driver, 201, 272, 250f);
            driver.SetInt(201, 262, 3);                       // Başarılı
            var result = await manager.TrackAsync(ReadStatus(driver));

            using var context = new NorthwindContext();
            var log = context.AuditLogs.Single(a => a.ActionType == "PID_TUNE_OK");

            Assert.True(result.IsSuccess);
            Assert.Contains("Kp=2", log.OldValues);
            Assert.Contains("Ti=300", log.OldValues);
            Assert.Contains("Kp=3.5", log.NewValues);
            Assert.Contains("Ti=250", log.NewValues);
        }

        // PID ayarı başarısız bitince bunun ayrı bir işlem tipiyle kaydedildiğini doğrular.
        [Fact]
        public async Task PidAyar_BasarisizBitinceKaydedilir()
        {
            var driver = new FakeS7Driver();
            var manager = CreateManager(driver, clockSync: false);
            await manager.TrackAsync(ReadStatus(driver));

            driver.SetInt(201, 262, 1);                       // Ön ayar sürüyor
            await manager.TrackAsync(ReadStatus(driver));

            driver.SetInt(201, 262, 4);                       // Başarısız ya da iptal
            await manager.TrackAsync(ReadStatus(driver));

            using var context = new NorthwindContext();
            Assert.Equal(1, context.AuditLogs.Count(a => a.ActionType == "PID_TUNE_FAILED"));
            Assert.Equal(0, context.AuditLogs.Count(a => a.ActionType == "PID_TUNE_OK"));
        }
    }
}