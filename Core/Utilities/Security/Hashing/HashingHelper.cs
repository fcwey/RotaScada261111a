using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Core.Utilities.Security.Hashing
{
    public class HashingHelper
    {
        private const int SaltSize = 16;          // Yeni biçimde tuz uzunluğu (bayt)
        private const int HashSize = 64;          // Özet uzunluğu (bayt)
        private const int Iterations = 210000;    // PBKDF2 tur sayısı

        // Şifreyi rastgele tuzla PBKDF2-SHA512 kullanarak özetler.
        public static void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            passwordSalt = RandomNumberGenerator.GetBytes(SaltSize);
            passwordHash = Rfc2898DeriveBytes.Pbkdf2(password, passwordSalt, Iterations, HashAlgorithmName.SHA512, HashSize);
        }

        // Şifrenin saklanan özetle eşleşip eşleşmediğini döner; eski biçimdeki özetleri de doğrular.
        public static bool VerifyPasswordHash(string password, byte[] passwrdHash, byte[] paswordSalt)
        {
            if (password == null || passwrdHash == null || paswordSalt == null || paswordSalt.Length == 0)
                return false;

            byte[] computedHash = NeedsUpgrade(paswordSalt)
                ? ComputeLegacyHash(password, paswordSalt)
                : Rfc2898DeriveBytes.Pbkdf2(password, paswordSalt, Iterations, HashAlgorithmName.SHA512, HashSize);

            // Karşılaştırma süresi eşleşen bayt sayısına göre değişmesin diye sabit süreli karşılaştırma kullanılır.
            return CryptographicOperations.FixedTimeEquals(computedHash, passwrdHash);
        }

        // Özetin eski biçimde (tek geçişli HMAC) olup olmadığını döner; öyleyse ilk girişte yenilenmelidir.
        public static bool NeedsUpgrade(byte[] passwordSalt)
        {
            return passwordSalt != null && passwordSalt.Length != SaltSize;
        }

        // Eski biçimdeki özeti hesaplar (HMAC-SHA512, tuz anahtar olarak kullanılır).
        private static byte[] ComputeLegacyHash(string password, byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512(passwordSalt))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            }
        }
    }
}