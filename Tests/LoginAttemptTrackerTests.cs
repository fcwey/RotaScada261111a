using Business.Concrete;
using Xunit;

namespace Tests
{
    // Hatalı giriş sayacı ve bekletme süresi testleri
    public class LoginAttemptTrackerTests
    {
        private DateTime _now = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // Zamanı testin ilerletebildiği bir sayaç üretir.
        private LoginAttemptTracker CreateTracker()
        {
            return new LoginAttemptTracker { Clock = () => _now };
        }

        // Verilen sayıda hatalı denemeyi art arda kaydeder.
        private static void Fail(LoginAttemptTracker tracker, string key, int count)
        {
            for (int i = 0; i < count; i++)
                tracker.RegisterFailure(key);
        }

        // Dört hatalı denemeden sonra henüz bekletme olmadığını doğrular.
        [Fact]
        public void DortHata_Bekletmez()
        {
            var tracker = CreateTracker();

            Fail(tracker, "admin|1.1.1.1", 4);

            Assert.Equal(0, tracker.GetBlockedSeconds("admin|1.1.1.1"));
        }

        // Beşinci hatalı denemede 60 saniyelik bekletmenin başladığını doğrular.
        [Fact]
        public void BesinciHata_AltmisSaniyeBekletir()
        {
            var tracker = CreateTracker();

            Fail(tracker, "admin|1.1.1.1", 5);

            Assert.Equal(60, tracker.GetBlockedSeconds("admin|1.1.1.1"));
        }

        // Bekletme süresinin zamanla azaldığını ve dolunca kalktığını doğrular.
        [Fact]
        public void SureDolunca_BekletmeKalkar()
        {
            var tracker = CreateTracker();
            Fail(tracker, "admin|1.1.1.1", 5);

            _now = _now.AddSeconds(20);
            Assert.Equal(40, tracker.GetBlockedSeconds("admin|1.1.1.1"));

            _now = _now.AddSeconds(41);
            Assert.Equal(0, tracker.GetBlockedSeconds("admin|1.1.1.1"));
        }

        // Bekletme bittikten sonraki ilk hatalı denemenin yeniden beklettiğini doğrular.
        [Fact]
        public void SuredenSonraYineHata_HemenBekletir()
        {
            var tracker = CreateTracker();
            Fail(tracker, "admin|1.1.1.1", 5);
            _now = _now.AddSeconds(61);

            Fail(tracker, "admin|1.1.1.1", 1);

            Assert.Equal(60, tracker.GetBlockedSeconds("admin|1.1.1.1"));
        }

        // Başarılı girişin sayacı sıfırladığını doğrular.
        [Fact]
        public void Reset_SayaciSifirlar()
        {
            var tracker = CreateTracker();
            Fail(tracker, "admin|1.1.1.1", 4);

            tracker.Reset("admin|1.1.1.1");
            Fail(tracker, "admin|1.1.1.1", 4);

            Assert.Equal(0, tracker.GetBlockedSeconds("admin|1.1.1.1"));
        }

        // Farklı bilgisayardan (IP) yapılan hatalı denemelerin başka anahtarı bekletmediğini doğrular.
        [Fact]
        public void FarkliAnahtarlar_BirbiriniEtkilemez()
        {
            var tracker = CreateTracker();

            Fail(tracker, "admin|9.9.9.9", 10);

            Assert.True(tracker.GetBlockedSeconds("admin|9.9.9.9") > 0);
            Assert.Equal(0, tracker.GetBlockedSeconds("admin|1.1.1.1"));
        }

        // 15 dakikadan uzun süre deneme olmazsa eski hataların unutulduğunu doğrular.
        [Fact]
        public void UzunAradanSonra_EskiHatalarUnutulur()
        {
            var tracker = CreateTracker();
            Fail(tracker, "admin|1.1.1.1", 4);
            _now = _now.AddMinutes(16);

            Fail(tracker, "admin|1.1.1.1", 1);

            Assert.Equal(0, tracker.GetBlockedSeconds("admin|1.1.1.1"));
        }
    }
}