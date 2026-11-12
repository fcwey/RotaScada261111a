using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Structures;
using Xunit;

namespace Tests
{
    // Reçete kaydetme, güncelleme, silme ve PLC'ye gönderip başlatma testleri (MySQL + sahte PLC)
    [Collection("Database")]
    public class RecipeManagerDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public RecipeManagerDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // DB204'te doğrulama sınırları girilmiş sahte PLC üretir.
        private static FakeS7Driver CreateDriver()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(204, 2, 750);     // MaxPotaTemp
            driver.SetInt(204, 50, 30);     // FanMinSpeed
            driver.SetInt(204, 58, 2000);   // N2_MfcMaxLph
            driver.SetInt(204, 60, 1500);   // NH3_MfcMaxLph
            return driver;
        }

        // Sahte PLC'ye bağlı reçete yöneticisi üretir.
        private static RecipeManager CreateManager(FakeS7Driver driver)
        {
            return new RecipeManager(driver, new PlcManager(driver));
        }

        // Dört adımlı geçerli bir nitrasyon reçetesi üretir.
        private static Recipe BuildRecipe(string name = "Nitrasyon Şaft")
        {
            return new Recipe
            {
                RecipeType = 2,
                RecipeName = name,
                PressureSetPoint = 20,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = 520, StepTime = 120, N2_Flow = 500 },
                    new RecipeStep { StepNo = 3, StepType = 2, TargetTemp = 520, StepTime = 240, N2_Flow = 100, NH3_Flow = 800 },
                    new RecipeStep { StepNo = 4, StepType = 3, TargetTemp = 150, N2_Flow = 500 }
                }
            };
        }

        // Eklenen reçetenin adımlarıyla birlikte kaydedildiğini ve sıralı okunduğunu doğrular.
        [Fact]
        public async Task Add_AdimlariylaKaydeder()
        {
            var manager = CreateManager(CreateDriver());

            var added = await manager.AddRecipeAsync(BuildRecipe());
            var loaded = await manager.GetRecipeByIdAsync(added.Data);

            Assert.True(added.IsSuccess);
            Assert.True(loaded.IsSuccess);
            Assert.Equal("Nitrasyon Şaft", loaded.Data.RecipeName);
            Assert.Equal(4, loaded.Data.TotalStepCount);
            Assert.Equal(new[] { 1, 2, 3, 4 }, loaded.Data.Steps.Select(s => s.StepNo).ToArray());
            Assert.Equal(800, loaded.Data.Steps.Single(s => s.StepNo == 3).NH3_Flow);
        }

        // Kurallara uymayan reçetenin veritabanına yazılmadığını doğrular.
        [Fact]
        public async Task Add_GecersizReceteKaydedilmez()
        {
            var manager = CreateManager(CreateDriver());
            var recipe = BuildRecipe();
            recipe.Steps.Single(s => s.StepNo == 4).StepType = 2;   // Son adım soğutma değil

            var result = await manager.AddRecipeAsync(recipe);

            using var context = new NorthwindContext();
            Assert.False(result.IsSuccess);
            Assert.Equal(0, context.Recipes.Count());
            Assert.Equal(0, context.RecipeSteps.Count());
        }

        // Güncellemede eski adımların silinip yenilerinin yazıldığını doğrular.
        [Fact]
        public async Task Update_AdimlariDegistirir()
        {
            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            var changed = new Recipe
            {
                Id = id,
                RecipeType = 2,
                RecipeName = "Nitrasyon Kısa",
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = 500, StepTime = 60, N2_Flow = 100, NH3_Flow = 600 },
                    new RecipeStep { StepNo = 3, StepType = 3, TargetTemp = 150, N2_Flow = 500 }
                }
            };

            var result = await manager.UpdateRecipeAsync(changed);
            var loaded = await manager.GetRecipeByIdAsync(id);

            using var context = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.Equal("Nitrasyon Kısa", loaded.Data.RecipeName);
            Assert.Equal(3, loaded.Data.Steps.Count);
            Assert.Equal(3, context.RecipeSteps.Count());   // Eski dört adım silinmiş
        }

        // Hiç kullanılmamış reçetenin tamamen silindiğini doğrular.
        [Fact]
        public async Task Delete_KullanilmayanReceteSilinir()
        {
            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            var result = await manager.DeleteRecipeAsync(id);

            using var context = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.Equal(0, context.Recipes.Count());
        }

        // Bir şarjda kullanılmış reçetenin silinmek yerine kullanımdan kaldırıldığını doğrular.
        [Fact]
        public async Task Delete_SarjdaKullanilanReceteGizlenir()
        {
            var manager = CreateManager(CreateDriver());
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            using (var context = new NorthwindContext())
            {
                context.Batches.Add(new Batch
                {
                    BatchCode = "TEST-ESKI",
                    RecipeId = id,
                    OperatorUserId = 1,
                    StartTime = DateTime.Now.AddDays(-1),
                    EndTime = DateTime.Now.AddDays(-1).AddHours(8),
                    Status = "COMPLETED"
                });
                context.SaveChanges();
            }

            var result = await manager.DeleteRecipeAsync(id);
            var list = await manager.GetAllRecipesAsync();

            using var check = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.False(check.Recipes.Single().IsActive);   // Kayıt duruyor ama pasif
            Assert.Equal(1, check.Batches.Count());          // Şarj kaydı korunmuş
            Assert.Empty(list.Data);                         // Listede görünmüyor
        }

        // Start kabul edilince reçetenin DB202'ye yazıldığını ve şarj kaydının açıldığını doğrular.
        [Fact]
        public async Task Start_KabulEdilinceSarjAcar()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            var result = await manager.StartRecipeAsync(id, 5);

            var inPlc = RecipeCodec.Decode(await driver.ReadBytesAsync(202, 0, RecipeCodec.TotalSize));
            using var context = new NorthwindContext();
            var batch = context.Batches.Single();

            Assert.True(result.IsSuccess);
            Assert.Equal(1, driver.GetInt(200, 2));          // CmdCode = Start
            Assert.Equal(id, inPlc.Id);
            Assert.Equal("Nitrasyon Şaft", inPlc.RecipeName);
            Assert.Equal(4, inPlc.Steps.Count);
            Assert.Equal("RUNNING", batch.Status);
            Assert.Equal(id, batch.RecipeId);
            Assert.Equal(5, batch.OperatorUserId);
        }

        // Yeni program başlarken kapanmamış eski şarjın kapatıldığını doğrular.
        [Fact]
        public async Task Start_EskiAcikSarjiKapatir()
        {
            var manager = CreateManager(CreateDriver());
            int oldBatchId = _database.InsertBatch("RUNNING");
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            await manager.StartRecipeAsync(id, 5);

            using var context = new NorthwindContext();
            Assert.Equal("ABORTED", context.Batches.Single(b => b.Id == oldBatchId).Status);
            Assert.Equal(1, context.Batches.Count(b => b.Status == "RUNNING"));
        }

        // PLC Start komutunu reddederse şarj kaydı açılmadığını doğrular.
        [Fact]
        public async Task Start_ReddedilinceSarjAcmaz()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            driver.AckResult = 101;   // Aktif alarm var

            var result = await manager.StartRecipeAsync(id, 5);

            using var context = new NorthwindContext();
            Assert.False(result.IsSuccess);
            Assert.Equal(101, (int)result.Data.ResultCode);
            Assert.Equal(0, context.Batches.Count());
        }

        // Olmayan reçete başlatılmak istenirse PLC'ye hiçbir şey yazılmadığını doğrular.
        [Fact]
        public async Task Start_OlmayanRecetedePlcyeYazilmaz()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);

            var result = await manager.StartRecipeAsync(999, 5);

            Assert.False(result.IsSuccess);
            Assert.Equal(0, driver.WriteCount);
        }

        // PLC sınırını aşan reçetenin SCADA'da reddedildiğini ve PLC'ye yazılmadığını doğrular.
        [Fact]
        public async Task Validate_SinirDisiRecetePlcyeYazilmaz()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            int id = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            driver.SetInt(204, 60, 0);   // Amonyak MFC tam skalası sonradan 0 yapıldı

            var result = await manager.ValidateOnPlcAsync(id);

            Assert.False(result.IsSuccess);
            Assert.Equal(118, (int)result.Data.ResultCode);
            Assert.Equal(3, (int)result.Data.Detail);
            Assert.Equal(0, driver.WriteCount);
        }

        // Çalışan reçetenin DB203'ten okunup çözüldüğünü doğrular.
        [Fact]
        public async Task GetActive_Db203tenOkur()
        {
            var driver = CreateDriver();
            var manager = CreateManager(driver);
            var recipe = BuildRecipe("Çalışan Reçete");
            recipe.Id = 12;
            await driver.WriteBytesAsync(203, 0, RecipeCodec.Encode(recipe));

            var result = await manager.GetActiveRecipeAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(12, result.Data.Id);
            Assert.Equal("Çalışan Reçete", result.Data.RecipeName);
            Assert.Equal(4, result.Data.Steps.Count);
        }
    }
}