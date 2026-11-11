using Core.Constants;
using Core.Entities.Concrete;
using Core.Utilities.Security.Hashing;
using DataAccess.Concrete.EntityFramework.Contexts;

namespace DataAccess.Concrete.EntityFramework
{
    // Uygulama açılışında veritabanını, tabloları, rolleri ve ilk yönetici kullanıcısını hazırlar
    public static class DbInitializer
    {
        // OperationClaims tablosunda bulunması gereken roller
        private static readonly string[] RoleNames =
        {
            PlcPermissions.Admin,
            PlcPermissions.Operate,
            PlcPermissions.ProgramEdit,
            PlcPermissions.Manual,
            ParameterPermissions.SetpointBasicWrite,
            ParameterPermissions.CalibrationWrite,
            ParameterPermissions.GasFlowLimitsWrite,
            ParameterPermissions.SafetyLimitsWrite,
            ParameterPermissions.PlcIpConfigWrite
        };

        // Veritabanı ve tablolar yoksa oluşturur, eksik rolleri ve ilk yönetici kullanıcısını ekler.
        public static void Initialize(string? adminEmail, string? adminPassword)
        {
            using var context = new NorthwindContext();

            context.Database.EnsureCreated();
            SeedRoles(context);
            SeedAdminUser(context, adminEmail, adminPassword);
        }

        // OperationClaims tablosunda eksik olan rolleri ekler.
        private static void SeedRoles(NorthwindContext context)
        {
            var existing = context.OperationClaims.Select(c => c.Name).ToList();

            foreach (var roleName in RoleNames)
            {
                if (!existing.Contains(roleName))
                    context.OperationClaims.Add(new OperationClaim { Name = roleName });
            }

            context.SaveChanges();
        }

        // Hiç kullanıcı yoksa ayarlardaki e-posta ve şifreyle Admin rolünde ilk kullanıcıyı oluşturur.
        private static void SeedAdminUser(NorthwindContext context, string? adminEmail, string? adminPassword)
        {
            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
                return;

            if (context.Users.Any())
                return;

            HashingHelper.CreatePasswordHash(adminPassword, out byte[] passwordHash, out byte[] passwordSalt);

            var admin = new User
            {
                FirstName = "Sistem",
                LastName = "Yöneticisi",
                Email = adminEmail,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Status = true
            };
            context.Users.Add(admin);
            context.SaveChanges();

            var adminRole = context.OperationClaims.First(c => c.Name == PlcPermissions.Admin);
            context.UserOperationClaims.Add(new UserOperationClaim
            {
                UserId = admin.Id,
                OperationClaimId = adminRole.Id
            });
            context.SaveChanges();
        }
    }
}