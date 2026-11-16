using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Xunit;

namespace Tests
{
    // Start komutunun şarj numarasıyla gönderilmesini doğrulayan testler
    [Collection("Database")]
    public class RecipeStartBatchIdDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public RecipeStartBatchIdDbTests(DatabaseFixture database)
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

        // Rampa ve soğutmadan oluşan geçerli bir meneviş reçetesi üretir.
        private static Recipe BuildRecipe()
        {
            return new Recipe
            {
                RecipeType = 1,
                RecipeName = "Meneviş",
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 2, TargetTemp = 400, StepTime = 60, N2_Flow = 200 },
                    new RecipeStep { StepNo = 2, StepType = 3, TargetTemp = 150, N2_Flow = 200 }
                }
            };
        }

        // Start komutunun Param1 ve Param2 alanlarında açılan şarjın numarasının gittiğini doğrular.
        [Fact]
        public async Task Start_SarjNumarasiniGonderir()
        {
            var driver = CreateDriver();
            var manager = new RecipeManager(driver, new PlcManager(driver));
            int recipeId = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            var result = await manager.StartRecipeAsync(recipeId, 5);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single();

            Assert.True(result.IsSuccess);
            Assert.Equal("RUNNING", batch.Status);
            Assert.Equal(1, driver.GetInt(200, 2));                          // CmdCode = Start
            Assert.Equal(batch.Id & 0xFFFF, (ushort)driver.GetInt(200, 4));  // Param1: düşük 16 bit
            Assert.Equal(batch.Id >> 16, (int)driver.GetInt(200, 6));        // Param2: yüksek 16 bit
        }

        // PLC 901 uyarısıyla kabul ederse programın başladığını ve mesajda uyarının geçtiğini doğrular.
        [Fact]
        public async Task Start_901UyarisiMesajdaGosterilir()
        {
            var driver = CreateDriver();
            driver.AckDetail = 901;
            var manager = new RecipeManager(driver, new PlcManager(driver));
            int recipeId = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            var result = await manager.StartRecipeAsync(recipeId, 5);

            using var context = new NorthwindContext();
            Assert.True(result.IsSuccess);
            Assert.True(result.Data.HasWarning);
            Assert.Contains("korumalı bekleme", result.Message);
            Assert.Equal("RUNNING", context.Batches.Single().Status);
        }

        // Yarım kalmış eski bir ayırma (STARTING) kaydının yeni Start'ta temizlendiğini doğrular.
        [Fact]
        public async Task Start_YarimKalmisAyirmayiTemizler()
        {
            int staleId = _database.InsertBatch("STARTING");
            var driver = CreateDriver();
            var manager = new RecipeManager(driver, new PlcManager(driver));
            int recipeId = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            await manager.StartRecipeAsync(recipeId, 5);

            using var context = new NorthwindContext();
            Assert.DoesNotContain(context.Batches.ToList(), b => b.Id == staleId);
            Assert.Equal("RUNNING", context.Batches.Single().Status);
        }

        // Start reddedilince ayrılan şarj kaydının silindiğini doğrular.
        [Fact]
        public async Task Start_ReddedilinceAyirmaSilinir()
        {
            var driver = CreateDriver();
            driver.AckResult = 102;
            var manager = new RecipeManager(driver, new PlcManager(driver));
            int recipeId = (await manager.AddRecipeAsync(BuildRecipe())).Data;

            var result = await manager.StartRecipeAsync(recipeId, 5);

            using var context = new NorthwindContext();
            Assert.False(result.IsSuccess);
            Assert.Equal(0, context.Batches.Count());
        }
    }
}