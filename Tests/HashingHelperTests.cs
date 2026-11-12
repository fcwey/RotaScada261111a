using Core.Utilities.Security.Hashing;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Tests
{
    // Şifre özetleme ve doğrulama testleri (yeni PBKDF2 biçimi ve eski HMAC biçimi)
    public class HashingHelperTests
    {
        // Eski biçimde (tek geçişli HMAC-SHA512) özet üretir.
        private static void CreateLegacyHash(string password, out byte[] hash, out byte[] salt)
        {
            using var hmac = new HMACSHA512();
            salt = hmac.Key;
            hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        }

        // Yeni özetin beklenen uzunluklarda üretildiğini doğrular.
        [Fact]
        public void Create_YeniBicimUzunluklari()
        {
            HashingHelper.CreatePasswordHash("sifre123", out var hash, out var salt);

            Assert.Equal(16, salt.Length);
            Assert.Equal(64, hash.Length);
            Assert.False(HashingHelper.NeedsUpgrade(salt));
        }

        // Doğru şifrenin kabul, yanlış şifrenin ret edildiğini doğrular.
        [Fact]
        public void Verify_DogruVeYanlisSifre()
        {
            HashingHelper.CreatePasswordHash("sifre123", out var hash, out var salt);

            Assert.True(HashingHelper.VerifyPasswordHash("sifre123", hash, salt));
            Assert.False(HashingHelper.VerifyPasswordHash("sifre124", hash, salt));
            Assert.False(HashingHelper.VerifyPasswordHash("", hash, salt));
        }

        // Aynı şifrenin her seferinde farklı tuz ve özet ürettiğini doğrular.
        [Fact]
        public void Create_AyniSifreFarkliOzet()
        {
            HashingHelper.CreatePasswordHash("sifre123", out var hash1, out var salt1);
            HashingHelper.CreatePasswordHash("sifre123", out var hash2, out var salt2);

            Assert.NotEqual(salt1, salt2);
            Assert.NotEqual(hash1, hash2);
        }

        // Türkçe karakterli şifrenin doğru özetlenip doğrulandığını doğrular.
        [Fact]
        public void Verify_TurkceKarakterliSifre()
        {
            HashingHelper.CreatePasswordHash("Çelik-Şaft-Ölçü1", out var hash, out var salt);

            Assert.True(HashingHelper.VerifyPasswordHash("Çelik-Şaft-Ölçü1", hash, salt));
            Assert.False(HashingHelper.VerifyPasswordHash("Celik-Saft-Olcu1", hash, salt));
        }

        // Eski biçimde saklanan şifrenin hâlâ doğrulanabildiğini doğrular.
        [Fact]
        public void Verify_EskiBicimDogrulanir()
        {
            CreateLegacyHash("eskiSifre", out var hash, out var salt);

            Assert.True(HashingHelper.VerifyPasswordHash("eskiSifre", hash, salt));
            Assert.False(HashingHelper.VerifyPasswordHash("yanlis", hash, salt));
        }

        // Eski biçimdeki özetin "yenilenmeli" olarak işaretlendiğini doğrular.
        [Fact]
        public void NeedsUpgrade_EskiBicimdeTrue()
        {
            CreateLegacyHash("eskiSifre", out _, out var salt);

            Assert.True(HashingHelper.NeedsUpgrade(salt));
        }

        // Boş ya da eksik veriyle hata fırlatmadan false döndüğünü doğrular.
        [Fact]
        public void Verify_EksikVerideFalse()
        {
            HashingHelper.CreatePasswordHash("sifre123", out var hash, out var salt);

            Assert.False(HashingHelper.VerifyPasswordHash("sifre123", null!, salt));
            Assert.False(HashingHelper.VerifyPasswordHash("sifre123", hash, null!));
            Assert.False(HashingHelper.VerifyPasswordHash("sifre123", hash, Array.Empty<byte>()));
            Assert.False(HashingHelper.VerifyPasswordHash(null!, hash, salt));
        }
    }
}