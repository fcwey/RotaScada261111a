using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests
{
    // Parametre yedeği, karşılaştırma ve PLC'ye geri yükleme testleri
    [Collection("Database")]
    public class ParamBackupDbTests
    {
        // Her testten önce tabloları ve parametre yedeklerini boşaltır.
        public ParamBackupDbTests(DatabaseFixture database)
        {
            database.ClearData();

            using var context = new NorthwindContext();
            context.ParamBackups.ExecuteDelete();
        }

        // DB204'te birkaç parametresi sahada girilmiş gibi dolu olan sahte PLC üretir.
        private static FakeS7Driver CreateDriver()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(204, 2, 700);      // MaxPotaTemp
            driver.SetInt(204, 50, 30);      // FanMinSpeed
            driver.SetInt(204, 58, 2000);    // N2_MfcMaxLph
            driver.SetInt(204, 60, 1500);    // NH3_MfcMaxLph
            driver.SetInt(204, 104, 10);     // LeakTestMin
            return driver;
        }

        // Sahte PLC'ye bağlı ayar yöneticisi üretir.
        private static SystemSettingManager CreateManager(FakeS7Driver driver)
        {
            return new SystemSettingManager(driver, new PlcManager(driver));
        }

        // Elle alınan yedeğin notuyla kaydedildiğini ve listede göründüğünü doğrular.
        [Fact]
        public async Task Yedek_NotuylaKaydedilir()
        {
            var manager = CreateManager(CreateDriver());

            var created = await manager.CreateBackupAsync("Devreye alma sonu", 7);
            var list = await manager.GetBackupsAsync();

            Assert.True(created.IsSuccess);
            Assert.Single(list.Data);
            Assert.Equal("Devreye alma sonu", list.Data[0].Note);
            Assert.Equal(7, list.Data[0].UserId);
            Assert.False(list.Data[0].IsAuto);
        }

        // PLC yedekle aynıyken fark çıkmadığını, PLC sıfırlanınca farkların listelendiğini doğrular.
        [Fact]
        public async Task Karsilastirma_FarklariListeler()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            int backupId = (await manager.CreateBackupAsync(null, 7)).Data;

            var same = await manager.CompareBackupAsync(backupId);
            Assert.Empty(same.Data);

            // PLC'ye yeni program yüklendi: iki parametre başlangıç değerine döndü.
            driver.SetInt(204, 58, 0);
            driver.SetInt(204, 2, 750);

            var diff = await manager.CompareBackupAsync(backupId);

            Assert.Equal(new[] { 2, 58 }, diff.Data.Select(d => d.Address).OrderBy(a => a).ToArray());
            var n2 = diff.Data.Single(d => d.Address == 58);
            Assert.Equal(2000, n2.BackupValue);
            Assert.Equal(0, n2.PlcValue);
            Assert.True(diff.Data.Single(d => d.Address == 2).IsSafety);
        }

        // Geri yüklemenin yalnızca farklı parametreleri PLC'ye yazdığını doğrular.
        [Fact]
        public async Task GeriYukleme_FarkliParametreleriYazar()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            int backupId = (await manager.CreateBackupAsync(null, 7)).Data;

            driver.SetInt(204, 58, 0);
            driver.SetInt(204, 60, 0);

            var result = await manager.RestoreBackupAsync(backupId, 7);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Data.Count);
            Assert.All(result.Data, i => Assert.Equal("Geri yüklendi", i.Status));
            Assert.Equal(2000, driver.GetInt(204, 58));
            Assert.Equal(1500, driver.GetInt(204, 60));
            Assert.Equal(700, driver.GetInt(204, 2));     // Aynı olan parametreye dokunulmadı
            Assert.Equal(6, driver.GetInt(204, 0));       // Şema sürümü alanına yazılmadı
        }

        // Program çalışırken geri yüklemenin reddedildiğini ve PLC'ye hiçbir şey yazılmadığını doğrular.
        [Fact]
        public async Task GeriYukleme_ProgramCalisirkenReddedilir()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            int backupId = (await manager.CreateBackupAsync(null, 7)).Data;

            driver.SetInt(204, 58, 0);
            driver.SetInt(201, 12, 30);     // SeqState: ısıtma

            var result = await manager.RestoreBackupAsync(backupId, 7);

            Assert.False(result.IsSuccess);
            Assert.Equal(0, driver.GetInt(204, 58));
        }

        // Yedekteki değer bugünkü izinli aralığın dışındaysa yazılmadığını ve bunun bildirildiğini doğrular.
        [Fact]
        public async Task GeriYukleme_AralikDisiDegerYazilmaz()
        {
            var driver = CreateDriver();
            driver.SetInt(204, 2, 900);     // MaxPotaTemp izinli aralığın (50–800) dışında bir değerle yedeklenecek
            var manager = CreateManager(driver);
            int backupId = (await manager.CreateBackupAsync(null, 7)).Data;

            driver.SetInt(204, 2, 750);

            var result = await manager.RestoreBackupAsync(backupId, 7);

            var item = result.Data.Single();
            Assert.True(result.IsSuccess);
            Assert.StartsWith("Yazılmadı", item.Status);
            Assert.Equal(750, driver.GetInt(204, 2));
        }

        // Parametre yazımlarının tek bir otomatik yedeği güncellediğini ve son değerleri taşıdığını doğrular.
        [Fact]
        public async Task ParametreYazimi_OtomatikYedegiGunceller()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);

            await manager.UpdateParameterAsync(58, 2500, 7);
            await manager.UpdateParameterAsync(60, 1800, 9);

            var list = await manager.GetBackupsAsync();
            var auto = list.Data.Single();
            Assert.True(auto.IsAuto);
            Assert.Equal(9, auto.UserId);

            // PLC sıfırlandı: otomatik yedek son yazılan değerleri geri getirir.
            driver.SetInt(204, 58, 0);
            driver.SetInt(204, 60, 0);
            await manager.RestoreBackupAsync(auto.Id, 7);

            Assert.Equal(2500, driver.GetInt(204, 58));
            Assert.Equal(1800, driver.GetInt(204, 60));
        }

        // Olmayan yedek için karşılaştırma ve geri yüklemenin hata döndüğünü doğrular.
        [Fact]
        public async Task OlmayanYedek_HataDoner()
        {
            var manager = CreateManager(CreateDriver());

            Assert.False((await manager.CompareBackupAsync(987654)).IsSuccess);
            Assert.False((await manager.RestoreBackupAsync(987654, 7)).IsSuccess);
        }
    }
}