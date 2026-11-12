using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Structures;
using Xunit;

namespace Tests
{
    // Şarj kaydına reçete kopyasının yazılması ve şarj sorgularının testleri
    [Collection("Database")]
    public class BatchRecipeRevisionDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public BatchRecipeRevisionDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // DB204'te doğrulama sınırları girilmiş sahte PLC üretir.
        private static FakeS7Driver CreateDriver()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(204, 2, 750);
            driver.SetInt(204, 50, 30);
            driver.SetInt(204, 58, 2000);
            driver.SetInt(204, 60, 1500);
            return driver;
        }

        // Sahte PLC'ye bağlı reçete yöneticisi üretir.
        private static RecipeManager CreateManager(FakeS7Driver driver)
        {
            return new RecipeManager(driver, new PlcManager(driver));
        }

        // Üç adımlı geçerli bir nitrasyon reçetesi üretir.
        private static Recipe BuildRecipe(string name = "Nitrasyon Şaft", int soakTemp = 520)
        {
            return new Recipe
            {
                RecipeType = 2,
                RecipeName = name,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = soakTemp, StepTime = 240, N2_Flow = 100, NH3_Flow = 800 },
                    new RecipeStep { StepNo = 3, StepType = 3, TargetTemp = 150, N2_Flow = 500 }
                }
            };
        }

        // Reçete kopyasının metne çevrilip geri çözülünce aynı kaldığını doğrular (Türkçe karakterler dahil).
        [Fact]
        public void Snapshot_GeriCozulunceAyni()
        {
            var recipe = BuildRecipe("Meneviş Çelik Şaft Ölçü");
            recipe.Id = 7;

            var restored = RecipeSnapshot.Deserialize(RecipeSnapshot.Serialize(recipe));

            Assert.NotNull(restored);
            Assert.Equal(7, restored!.Id);
            Assert.Equal("Meneviş Çelik Şaft Ölçü", restored.RecipeName);
            Assert.Equal(new[] { 1, 2, 3 }, restored.Steps.Select(s => s.StepNo).ToArray());
            Assert.Equal(800, restored.Steps.Single(s => s.StepNo == 2).NH3_Flow);
        }

        // Bozuk ya da boş metinde hata fırlatmadan null döndüğünü doğrular.
        [Fact]
        public void Snapshot_BozukMetindeNull()
        {
            Assert.Null(RecipeSnapshot.Deserialize("bu json değil"));
            Assert.Null(RecipeSnapshot.Deserialize(""));
            Assert.Null(RecipeSnapshot.Deserialize(null));
        }

        // Program başlayınca reçetenin kopyasının şarja START olarak yazıldığını doğrular.
        [Fact]
        public async Task Start_BaslangicKopyasiYazilir()
        {
            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            await manager.StartRecipeAsync(id, 5);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();
            var revision = context.BatchRecipeRevisions.Single();
            var snapshot = RecipeSnapshot.Deserialize(revision.RecipeJson);

            Assert.Equal(batch.Id, revision.BatchId);
            Assert.Equal("START", revision.Reason);
            Assert.Equal(5, revision.UserId);
            Assert.Equal("Nitrasyon Şaft", snapshot!.RecipeName);
            Assert.Equal(520, snapshot.Steps.Single(s => s.StepNo == 2).TargetTemp);
        }

        // Reçete sonradan değiştirilse de şarjdaki kopyanın eski değerleri koruduğunu doğrular.
        [Fact]
        public async Task ReceteDegisse_SarjKopyasiDegismez()
        {
            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            await manager.StartRecipeAsync(id, 5);

            var changed = BuildRecipe("Değişmiş Ad", soakTemp: 560);
            changed.Id = id;
            await manager.UpdateRecipeAsync(changed);

            using var context = new NorthwindContext();
            var snapshot = RecipeSnapshot.Deserialize(context.BatchRecipeRevisions.Single().RecipeJson);

            Assert.Equal("Değişmiş Ad", context.Recipes.Single().RecipeName);   // Reçete değişti
            Assert.Equal("Nitrasyon Şaft", snapshot!.RecipeName);               // Şarjın kopyası aynı
            Assert.Equal(520, snapshot.Steps.Single(s => s.StepNo == 2).TargetTemp);
        }

        // Reçete kopyasının işlem kaydı (audit) tablosuna ikinci kez yazılmadığını doğrular.
        [Fact]
        public async Task Kopya_AuditTablosunaYazilmaz()
        {
            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            await manager.StartRecipeAsync(id, 5);

            using var context = new NorthwindContext();
            Assert.Equal(0, context.AuditLogs.Count(a => a.ModuleName == "BatchRecipeRevision"));
        }

        // Çalışırken kabul edilen değişikliğin şarja EDIT olarak, değiştiren kullanıcıyla eklendiğini doğrular.
        [Fact]
        public async Task ApplyEdit_KabulEdilinceSarjaIslenir()
        {
            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            await manager.StartRecipeAsync(id, 5);

            var edited = BuildRecipe(soakTemp: 540);
            edited.Id = id;
            var result = await manager.ApplyEditAsync(edited, 9);

            using var context = new NorthwindContext();
            var revisions = context.BatchRecipeRevisions.OrderBy(r => r.Id).ToList();

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "START", "EDIT" }, revisions.Select(r => r.Reason).ToArray());
            Assert.Equal(9, revisions[1].UserId);
            Assert.Equal(540, RecipeSnapshot.Deserialize(revisions[1].RecipeJson)!.Steps.Single(s => s.StepNo == 2).TargetTemp);
            Assert.Equal(520, RecipeSnapshot.Deserialize(revisions[0].RecipeJson)!.Steps.Single(s => s.StepNo == 2).TargetTemp);
        }

        // PLC değişikliği reddederse şarja hiçbir şey eklenmediğini doğrular.
        [Fact]
        public async Task ApplyEdit_ReddedilinceSarjaIslenmez()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            await manager.StartRecipeAsync(id, 5);
            driver.AckResult = 132;   // Online edit: aktif adımın tipi değiştirilemez

            var edited = BuildRecipe(soakTemp: 540);
            edited.Id = id;
            var result = await manager.ApplyEditAsync(edited, 9);

            using var context = new NorthwindContext();
            Assert.False(result.IsSuccess);
            Assert.Equal(1, context.BatchRecipeRevisions.Count());   // Yalnızca START
        }

        // Şarj ayrıntısının reçete geçmişini, kullanıcıyı ve şarj sırasındaki alarmları getirdiğini doğrular.
        [Fact]
        public async Task SarjAyrintisi_GecmisVeAlarmlarGelir()
        {
            int userId = new UserManager(new DataAccess.Concrete.EntityFramework.EfUserDal())
                .CreateUser(new Entities.Dtos.UserCreateDto { Email = "operator@test", FirstName = "A", LastName = "B", Password = "sifre123" })
                .Data;

            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            await manager.StartRecipeAsync(id, userId);

            int batchId;
            using (var context = new NorthwindContext())
            {
                batchId = context.Batches.Single().Id;
                context.Alarms.Add(new Alarm { TagName = "ALM_022", Description = "Şarj sırasında", TriggeredAt = DateTime.Now, IsActive = true });
                context.Alarms.Add(new Alarm { TagName = "ALM_001", Description = "Şarjdan önce", TriggeredAt = DateTime.Now.AddDays(-2), IsActive = false });
                context.SaveChanges();
            }

            var result = await new BatchManager().GetBatchDetailAsync(batchId);

            Assert.True(result.IsSuccess);
            Assert.Equal("Nitrasyon Şaft", result.Data.Batch.RecipeName);
            Assert.Equal("operator@test", result.Data.Batch.OperatorEmail);
            Assert.Equal("RUNNING", result.Data.Batch.Status);
            Assert.Single(result.Data.Revisions);
            Assert.Equal("operator@test", result.Data.Revisions[0].UserEmail);
            Assert.Equal(3, result.Data.Revisions[0].Recipe!.Steps.Count);
            Assert.Single(result.Data.Alarms);
            Assert.Equal("ALM_022", result.Data.Alarms[0].TagName);
        }

        // Şarj listesinin yalnızca istenen tarih aralığını, yeniden eskiye getirdiğini doğrular.
        [Fact]
        public async Task SarjListesi_AraligaGoreSuzer()
        {
            using (var context = new NorthwindContext())
            {
                var recipe = new Recipe { RecipeType = 1, RecipeName = "R" };
                context.Recipes.Add(recipe);
                context.SaveChanges();

                foreach (int daysAgo in new[] { 1, 5, 40 })
                {
                    context.Batches.Add(new Batch
                    {
                        BatchCode = $"TEST-{daysAgo}",
                        RecipeId = recipe.Id,
                        OperatorUserId = 1,
                        StartTime = DateTime.Now.AddDays(-daysAgo),
                        Status = "COMPLETED"
                    });
                }
                context.SaveChanges();
            }

            var result = await new BatchManager().GetBatchesAsync(DateTime.Now.AddDays(-30), DateTime.Now);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "TEST-1", "TEST-5" }, result.Data.Select(b => b.BatchCode).ToArray());
            Assert.Equal("R", result.Data[0].RecipeName);   // Kopyası olmayan eski şarjda reçetenin şimdiki adı
        }

        // Olmayan şarj istenirse anlaşılır bir hata döndüğünü doğrular.
        [Fact]
        public async Task SarjAyrintisi_OlmayanSarj()
        {
            var result = await new BatchManager().GetBatchDetailAsync(987654);

            Assert.False(result.IsSuccess);
        }
    }
}