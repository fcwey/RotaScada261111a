using Business.Concrete;
using Core.Entities.Concrete;
using Core.Utilities.Security.Hashing;
using DataAccess.Concrete.EntityFramework;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Dtos;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Tests
{
    // Giriş doğrulaması ve eski biçimli şifrenin ilk girişte yenilenmesi testleri
    [Collection("Database")]
    public class AuthManagerDbTests
    {
        // Her testten önce tabloları boşaltır.
        public AuthManagerDbTests(DatabaseFixture database)
        {
            database.ClearData();
        }

        // Gerçek kullanıcı servisiyle giriş yöneticisi üretir (token üretimi bu testlerde kullanılmaz).
        private static AuthManager CreateManager()
        {
            return new AuthManager(new UserManager(new EfUserDal()), null!);
        }

        // Giriş isteği üretir.
        private static UserForLoginDto Login(string email, string password)
        {
            return new UserForLoginDto { Email = email, Password = password };
        }

        // Şifresi eski biçimde (HMAC-SHA512) saklanan bir kullanıcıyı doğrudan veritabanına ekler.
        private static void InsertLegacyUser(string email, string password, bool isActive = true)
        {
            using var hmac = new HMACSHA512();
            using var context = new NorthwindContext();

            context.Users.Add(new User
            {
                Email = email,
                FirstName = "Eski",
                LastName = "Kullanıcı",
                PasswordSalt = hmac.Key,
                PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password)),
                Status = isActive
            });
            context.SaveChanges();
        }

        // Eski biçimli şifreyle girişin başarılı olduğunu ve şifrenin yeni biçime çevrildiğini doğrular.
        [Fact]
        public void Login_EskiBicimliSifreYenilenir()
        {
            InsertLegacyUser("eski@test", "eskiSifre1");
            var manager = CreateManager();

            var first = manager.Login(Login("eski@test", "eskiSifre1"));

            using var context = new NorthwindContext();
            var user = context.Users.Single();
            Assert.True(first.IsSuccess);
            Assert.False(HashingHelper.NeedsUpgrade(user.PasswordSalt));   // Artık yeni biçimde
            Assert.Equal(16, user.PasswordSalt.Length);

            var second = manager.Login(Login("eski@test", "eskiSifre1"));
            Assert.True(second.IsSuccess);                                 // Aynı şifre hâlâ geçerli
        }

        // Yeni biçimde oluşturulan kullanıcının giriş yapabildiğini doğrular.
        [Fact]
        public void Login_YeniKullaniciGirisYapar()
        {
            new UserManager(new EfUserDal()).CreateUser(new UserCreateDto
            {
                Email = "yeni@test",
                FirstName = "Yeni",
                LastName = "Kullanıcı",
                Password = "sifre123"
            });

            var result = CreateManager().Login(Login("yeni@test", "sifre123"));

            Assert.True(result.IsSuccess);
            Assert.Equal("yeni@test", result.Data.Email);
        }

        // Yanlış şifre ve olmayan kullanıcı için aynı mesajın döndüğünü doğrular.
        [Fact]
        public void Login_YanlisSifreVeOlmayanKullaniciAyniMesaj()
        {
            InsertLegacyUser("eski@test", "eskiSifre1");
            var manager = CreateManager();

            var wrongPassword = manager.Login(Login("eski@test", "yanlis"));
            var unknownUser = manager.Login(Login("yok@test", "eskiSifre1"));

            Assert.False(wrongPassword.IsSuccess);
            Assert.False(unknownUser.IsSuccess);
            Assert.Equal(AuthManager.InvalidLoginMessage, wrongPassword.Message);
            Assert.Equal(AuthManager.InvalidLoginMessage, unknownUser.Message);
        }

        // Yanlış şifreyle denemenin saklanan şifreyi değiştirmediğini doğrular.
        [Fact]
        public void Login_YanlisSifreOzetiDegistirmez()
        {
            InsertLegacyUser("eski@test", "eskiSifre1");

            CreateManager().Login(Login("eski@test", "yanlis"));

            using var context = new NorthwindContext();
            Assert.True(HashingHelper.NeedsUpgrade(context.Users.Single().PasswordSalt));   // Hâlâ eski biçimde
        }

        // Kapatılmış hesabın doğru şifreyle bile giriş yapamadığını doğrular.
        [Fact]
        public void Login_KapaliHesapGiremez()
        {
            InsertLegacyUser("kapali@test", "eskiSifre1", isActive: false);

            var result = CreateManager().Login(Login("kapali@test", "eskiSifre1"));

            Assert.False(result.IsSuccess);
            Assert.Contains("kapatılmış", result.Message);
        }
    }
}