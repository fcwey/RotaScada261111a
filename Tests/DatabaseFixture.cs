using DataAccess.Concrete.EntityFramework;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Text.Json;
using Xunit;

// Ortak durum (veritabanı bağlantısı, alarm sınıfı ayarı) paylaşıldığı için testler sırayla çalıştırılır.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Tests
{
    // Veritabanı testleri için ayrı bir test veritabanı hazırlar; asıl veritabanına hiç dokunmaz
    public class DatabaseFixture
    {
        public const string TestDatabaseName = "rotascada_test";

        // Bağlantıyı test veritabanına çevirir, veritabanını silip baştan oluşturur ve rolleri ekler.
        public DatabaseFixture()
        {
            // Veritabanı adı her durumda test adıyla değiştirilir; gerçek veriye bağlanılamaz.
            var builder = new MySqlConnectionStringBuilder(ReadConnectionString())
            {
                Database = TestDatabaseName
            };
            NorthwindContext.ConnectionString = builder.ConnectionString;

            using (var context = new NorthwindContext())
            {
                context.Database.EnsureDeleted();
            }

            DbInitializer.Initialize(null, null);
        }

        // Roller dışındaki bütün tabloları boşaltır; her test temiz veriyle başlar.
        public void ClearData()
        {
            using var context = new NorthwindContext();

            context.ProcessDataLogs.ExecuteDelete();
            context.Batches.ExecuteDelete();
            context.RecipeSteps.ExecuteDelete();
            context.Recipes.ExecuteDelete();
            context.Alarms.ExecuteDelete();
            context.AuditLogs.ExecuteDelete();
            context.UserOperationClaims.ExecuteDelete();
            context.Users.ExecuteDelete();
        }

        // Test için bir reçete ve ona bağlı, verilen durumda bir şarj kaydı ekler; şarjın id'sini döner.
        public int InsertBatch(string status)
        {
            using var context = new NorthwindContext();

            var recipe = new Recipe { RecipeType = 1, RecipeName = "Test Reçetesi", TotalStepCount = 0 };
            context.Recipes.Add(recipe);
            context.SaveChanges();

            var batch = new Batch
            {
                BatchCode = $"TEST-{Guid.NewGuid():N}",
                RecipeId = recipe.Id,
                OperatorUserId = 1,
                StartTime = DateTime.Now.AddHours(-1),
                Status = status
            };
            context.Batches.Add(batch);
            context.SaveChanges();

            return batch.Id;
        }

        // Bağlantı metnini ortam değişkeninden ya da WebAPI/appsettings.Local.json dosyasından okur.
        private static string ReadConnectionString()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("ROTASCADA_TEST_DB");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return fromEnvironment;

            // Test çıktı klasöründen yukarı çıkarak çözüm klasöründeki ayar dosyası aranır.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, "WebAPI", "appsettings.Local.json");
                if (File.Exists(path))
                {
                    var options = new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    using var document = JsonDocument.Parse(File.ReadAllText(path), options);
                    string? value = document.RootElement
                        .GetProperty("ConnectionStrings")
                        .GetProperty("Default")
                        .GetString();

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Test veritabanı bağlantısı bulunamadı. WebAPI/appsettings.Local.json içinde ConnectionStrings:Default olmalı.");
        }
    }

    // Veritabanı kullanan test sınıflarının aynı hazırlığı paylaşmasını sağlar
    [CollectionDefinition("Database")]
    public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
    {
    }
}