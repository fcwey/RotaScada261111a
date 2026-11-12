using Business.Concrete;
using DataAccess.Concrete.EntityFramework;
using DataAccess.Concrete.EntityFramework.Contexts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests
{
    // PLC alarm bitlerinin veritabanı kayıtlarına dönüşmesini doğrulayan testler
    [Collection("Database")]
    public class AlarmManagerDbTests
    {
        // Her testten önce tabloları boşaltır.
        public AlarmManagerDbTests(DatabaseFixture database)
        {
            database.ClearData();
        }

        // Boş ayarla (alarm sınıfı ezme yok) alarm yöneticisi üretir.
        private static AlarmManager CreateManager()
        {
            return new AlarmManager(new EfAlarmDal(), new ConfigurationBuilder().Build());
        }

        // Verilen alarm numaralarının bitleri dolu olan 8 kelimelik dizi üretir.
        private static short[] Words(params int[] alarmNumbers)
        {
            var words = new short[8];
            foreach (int alarmNo in alarmNumbers)
            {
                int word = (alarmNo - 1) / 16;
                int bit = (alarmNo - 1) % 16;
                words[word] = (short)(words[word] | (1 << bit));
            }
            return words;
        }

        // Yeni gelen alarm için doğru etiket, metin ve öncelikle kayıt açıldığını doğrular.
        [Fact]
        public async Task YeniAlarm_KayitAcar()
        {
            var manager = CreateManager();

            var result = await manager.ProcessAlarmWordsAsync(Words(22), Words(22));

            using var context = new NorthwindContext();
            var alarm = context.Alarms.Single();

            Assert.True(result.IsSuccess);
            Assert.True(result.Data);                       // Değişiklik oldu
            Assert.Equal("ALM_022", alarm.TagName);
            Assert.Contains("brülör", alarm.Description);
            Assert.Equal(2, alarm.PriorityLevel);           // Sınıf 1 (duraklat) → öncelik 2
            Assert.True(alarm.IsActive);
            Assert.False(alarm.IsAcknowledged);
            Assert.Null(alarm.ResolvedAt);
        }

        // Bitler değişmediğinde yeni kayıt açılmadığını ve "değişiklik yok" döndüğünü doğrular.
        [Fact]
        public async Task AyniBitler_DegisiklikYok()
        {
            var manager = CreateManager();
            await manager.ProcessAlarmWordsAsync(Words(22), Words(22));

            var second = await manager.ProcessAlarmWordsAsync(Words(22), Words(22));

            using var context = new NorthwindContext();
            Assert.True(second.IsSuccess);
            Assert.False(second.Data);
            Assert.Equal(1, context.Alarms.Count());
        }

        // Alarm biti düşünce kaydın kapatıldığını ve bitiş zamanının yazıldığını doğrular.
        [Fact]
        public async Task AlarmGidince_KayitKapanir()
        {
            var manager = CreateManager();
            await manager.ProcessAlarmWordsAsync(Words(22), Words(22));

            await manager.ProcessAlarmWordsAsync(Words(), Words(22));

            using var context = new NorthwindContext();
            var alarm = context.Alarms.Single();
            Assert.False(alarm.IsActive);
            Assert.NotNull(alarm.ResolvedAt);
            Assert.False(alarm.IsAcknowledged);   // Reset henüz verilmedi
        }

        // PLC'de reset verilince (görülmemiş biti düşünce) kaydın görüldü olarak işaretlendiğini doğrular.
        [Fact]
        public async Task ResetGelince_GorulduOlur()
        {
            var manager = CreateManager();
            await manager.ProcessAlarmWordsAsync(Words(22), Words(22));

            await manager.ProcessAlarmWordsAsync(Words(22), Words());

            using var context = new NorthwindContext();
            var alarm = context.Alarms.Single();
            Assert.True(alarm.IsActive);          // Koşul sürüyor
            Assert.True(alarm.IsAcknowledged);
            Assert.NotNull(alarm.AcknowledgedAt);
        }

        // SCADA yeniden başlatıldığında süren alarm için ikinci kayıt açılmadığını doğrular.
        [Fact]
        public async Task YenidenBaslatma_KayitCiftlenmez()
        {
            await CreateManager().ProcessAlarmWordsAsync(Words(22), Words(22));

            await CreateManager().ProcessAlarmWordsAsync(Words(22), Words(22));

            using var context = new NorthwindContext();
            Assert.Equal(1, context.Alarms.Count());
        }

        // Aynı anda gelen birden fazla alarmın (kelime sınırları dahil) ayrı kayıtlar olduğunu doğrular.
        [Fact]
        public async Task BirdenFazlaAlarm_AyriKayitlar()
        {
            var manager = CreateManager();

            await manager.ProcessAlarmWordsAsync(Words(1, 16, 17, 47), Words(1, 16, 17, 47));

            using var context = new NorthwindContext();
            var tags = context.Alarms.Select(a => a.TagName).OrderBy(t => t).ToList();
            Assert.Equal(new[] { "ALM_001", "ALM_016", "ALM_017", "ALM_047" }, tags);
        }

        // Aynı alarm gidip yeniden gelirse ikinci bir kayıt açıldığını doğrular.
        [Fact]
        public async Task AlarmTekrarGelirse_YeniKayitAcilir()
        {
            var manager = CreateManager();
            await manager.ProcessAlarmWordsAsync(Words(22), Words(22));
            await manager.ProcessAlarmWordsAsync(Words(), Words());

            await manager.ProcessAlarmWordsAsync(Words(22), Words(22));

            using var context = new NorthwindContext();
            Assert.Equal(2, context.Alarms.Count());
            Assert.Equal(1, context.Alarms.Count(a => a.IsActive));
        }

        // Aktif alarm listesinin yalnızca süren alarmları içerdiğini doğrular.
        [Fact]
        public async Task GetActiveAlarms_YalnizcaSurenleriGetirir()
        {
            var manager = CreateManager();
            await manager.ProcessAlarmWordsAsync(Words(1, 22), Words(1, 22));
            await manager.ProcessAlarmWordsAsync(Words(1), Words(1, 22));

            var result = await manager.GetActiveAlarmsAsync();

            Assert.True(result.IsSuccess);
            Assert.Single(result.Data);
            Assert.Equal("ALM_001", result.Data[0].TagName);
            Assert.Equal(3, result.Data[0].PriorityLevel);   // Acil stop: sınıf 2 → öncelik 3
        }
    }
}