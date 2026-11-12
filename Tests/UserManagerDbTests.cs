using Business.Concrete;
using Core.Constants;
using Core.Utilities.Security.Hashing;
using DataAccess.Concrete.EntityFramework;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Dtos;
using Xunit;

namespace Tests
{
    // Kullanıcı açma, rol atama, hesap kapatma ve son yönetici koruması testleri
    [Collection("Database")]
    public class UserManagerDbTests
    {
        // Her testten önce tabloları boşaltır (roller kalır).
        public UserManagerDbTests(DatabaseFixture database)
        {
            database.ClearData();
        }

        // Gerçek veri erişimiyle kullanıcı yöneticisi üretir.
        private static UserManager CreateManager()
        {
            return new UserManager(new EfUserDal());
        }

        // Verilen e-posta ve rollerle kullanıcı açma isteği üretir.
        private static UserCreateDto NewUser(string email, params string[] roles)
        {
            return new UserCreateDto
            {
                Email = email,
                FirstName = "Test",
                LastName = "Kullanıcı",
                Password = "sifre123",
                Roles = roles.ToList()
            };
        }

        // Kullanıcının rolleriyle birlikte oluşturulduğunu doğrular.
        [Fact]
        public void Create_RolleriyleOlusturur()
        {
            var manager = CreateManager();

            var result = manager.CreateUser(NewUser("operator@test", PlcPermissions.Operate, PlcPermissions.Manual));
            var users = manager.GetUsers().Data;

            Assert.True(result.IsSuccess);
            Assert.Single(users);
            Assert.Equal("operator@test", users[0].Email);
            Assert.True(users[0].IsActive);
            Assert.Equal(new[] { PlcPermissions.Manual, PlcPermissions.Operate }, users[0].Roles.ToArray());
        }

        // Şifrenin düz metin olarak saklanmadığını ve doğrulanabildiğini doğrular.
        [Fact]
        public void Create_SifreHashlenir()
        {
            var manager = CreateManager();
            manager.CreateUser(NewUser("operator@test"));

            using var context = new NorthwindContext();
            var user = context.Users.Single();

            Assert.True(HashingHelper.VerifyPasswordHash("sifre123", user.PasswordHash, user.PasswordSalt));
            Assert.False(HashingHelper.VerifyPasswordHash("yanlis", user.PasswordHash, user.PasswordSalt));
        }

        // Aynı e-posta ile ikinci kullanıcı açılamadığını doğrular.
        [Fact]
        public void Create_AyniEpostaReddedilir()
        {
            var manager = CreateManager();
            manager.CreateUser(NewUser("operator@test"));

            var second = manager.CreateUser(NewUser("operator@test"));

            Assert.False(second.IsSuccess);
            Assert.Single(manager.GetUsers().Data);
        }

        // Bilinmeyen rol verilirse kullanıcının hiç oluşturulmadığını doğrular.
        [Fact]
        public void Create_BilinmeyenRolReddedilir()
        {
            var manager = CreateManager();

            var result = manager.CreateUser(NewUser("operator@test", "Olmayan.Rol"));

            Assert.False(result.IsSuccess);
            Assert.Empty(manager.GetUsers().Data);
        }

        // Kısa şifrenin reddedildiğini doğrular.
        [Fact]
        public void Create_KisaSifreReddedilir()
        {
            var manager = CreateManager();
            var dto = NewUser("operator@test");
            dto.Password = "123";

            Assert.False(manager.CreateUser(dto).IsSuccess);
        }

        // Rol değişikliğinin beklemeden (önbelleğe takılmadan) geçerli olduğunu doğrular.
        [Fact]
        public void SetRoles_HemenGecerliOlur()
        {
            var manager = CreateManager();
            int id = manager.CreateUser(NewUser("operator@test", PlcPermissions.Operate)).Data;
            var before = manager.GetCurrentAccess(id);

            var result = manager.SetRoles(id, new List<string> { PlcPermissions.Manual });
            var after = manager.GetCurrentAccess(id);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { PlcPermissions.Operate }, before!.Roles.ToArray());
            Assert.Equal(new[] { PlcPermissions.Manual }, after!.Roles.ToArray());
        }

        // Kapatılan hesabın beklemeden pasif göründüğünü doğrular.
        [Fact]
        public void SetActive_KapatilanHesapHemenPasif()
        {
            var manager = CreateManager();
            int id = manager.CreateUser(NewUser("operator@test", PlcPermissions.Operate)).Data;
            Assert.True(manager.GetCurrentAccess(id)!.IsActive);

            var result = manager.SetActive(id, false);

            Assert.True(result.IsSuccess);
            Assert.False(manager.GetCurrentAccess(id)!.IsActive);
        }

        // Son yöneticinin Admin rolünün alınamadığını doğrular.
        [Fact]
        public void SonYonetici_RoluAlinamaz()
        {
            var manager = CreateManager();
            int id = manager.CreateUser(NewUser("admin@test", PlcPermissions.Admin)).Data;

            var result = manager.SetRoles(id, new List<string> { PlcPermissions.Operate });

            Assert.False(result.IsSuccess);
            Assert.Contains(PlcPermissions.Admin, manager.GetCurrentAccess(id)!.Roles);
        }

        // Son yönetici hesabının kapatılamadığını doğrular.
        [Fact]
        public void SonYonetici_Kapatilamaz()
        {
            var manager = CreateManager();
            int id = manager.CreateUser(NewUser("admin@test", PlcPermissions.Admin)).Data;

            var result = manager.SetActive(id, false);

            Assert.False(result.IsSuccess);
            Assert.True(manager.GetCurrentAccess(id)!.IsActive);
        }

        // İkinci bir yönetici varken birincinin rolünün alınabildiğini doğrular.
        [Fact]
        public void IkinciYoneticiVarken_RolAlinabilir()
        {
            var manager = CreateManager();
            int first = manager.CreateUser(NewUser("admin1@test", PlcPermissions.Admin)).Data;
            manager.CreateUser(NewUser("admin2@test", PlcPermissions.Admin));

            var result = manager.SetRoles(first, new List<string>());

            Assert.True(result.IsSuccess);
            Assert.Empty(manager.GetCurrentAccess(first)!.Roles);
        }

        // Kapatılmış yöneticinin "son yönetici" sayımına girmediğini doğrular.
        [Fact]
        public void KapaliYonetici_SayimaGirmez()
        {
            var manager = CreateManager();
            int first = manager.CreateUser(NewUser("admin1@test", PlcPermissions.Admin)).Data;
            int second = manager.CreateUser(NewUser("admin2@test", PlcPermissions.Admin)).Data;
            manager.SetActive(second, false);

            var result = manager.SetActive(first, false);

            Assert.False(result.IsSuccess);   // Geriye aktif yönetici kalmayacaktı
        }

        // Şifre sıfırlamadan sonra yeni şifrenin geçerli, eskisinin geçersiz olduğunu doğrular.
        [Fact]
        public void ResetPassword_YeniSifreGecerli()
        {
            var manager = CreateManager();
            int id = manager.CreateUser(NewUser("operator@test")).Data;

            var result = manager.ResetPassword(id, "yeniSifre456");

            using var context = new NorthwindContext();
            var user = context.Users.Single();
            Assert.True(result.IsSuccess);
            Assert.True(HashingHelper.VerifyPasswordHash("yeniSifre456", user.PasswordHash, user.PasswordSalt));
            Assert.False(HashingHelper.VerifyPasswordHash("sifre123", user.PasswordHash, user.PasswordSalt));
        }

        // Olmayan kullanıcı için erişim sorgusunun "yok" döndüğünü doğrular.
        [Fact]
        public void GetCurrentAccess_OlmayanKullanici()
        {
            var access = CreateManager().GetCurrentAccess(987654);

            Assert.NotNull(access);
            Assert.False(access!.Exists);
            Assert.False(access.IsActive);
        }

        // Atanabilir rol listesinin açılışta eklenen rolleri içerdiğini doğrular.
        [Fact]
        public void GetRoleNames_AcilisRolleriniIcerir()
        {
            var roles = CreateManager().GetRoleNames().Data;

            Assert.Contains(PlcPermissions.Admin, roles);
            Assert.Contains(PlcPermissions.Operate, roles);
            Assert.Contains(PlcPermissions.ProgramEdit, roles);
            Assert.Contains(PlcPermissions.Manual, roles);
            Assert.Contains(ParameterPermissions.SafetyLimitsWrite, roles);
        }
    }
}