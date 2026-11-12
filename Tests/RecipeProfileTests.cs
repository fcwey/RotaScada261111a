using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Structures;
using Xunit;

namespace Tests
{
    // Teorik sıcaklık eğrisinin hesaplanması ve şarjın gerçek saatlerine oturtulması testleri
    [Collection("Database")]
    public class RecipeProfileTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public RecipeProfileTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Vakum, 120 dk rampa, 240 dk bekleme ve soğutmadan oluşan nitrasyon reçetesi üretir.
        private static Recipe BuildRecipe()
        {
            return new Recipe
            {
                RecipeType = 2,
                RecipeName = "Nitrasyon Şaft",
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = 520, StepTime = 120, N2_Flow = 500 },
                    new RecipeStep { StepNo = 3, StepType = 2, TargetTemp = 520, StepTime = 240, N2_Flow = 100, NH3_Flow = 800 },
                    new RecipeStep { StepNo = 4, StepType = 3, TargetTemp = 150, N2_Flow = 500 }
                }
            };
        }

        // DB204'te doğrulama sınırları girilmiş sahte PLC'ye bağlı reçete yöneticisi üretir.
        private static RecipeManager CreateRecipeManager()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(204, 2, 750);
            driver.SetInt(204, 50, 30);
            driver.SetInt(204, 58, 2000);
            driver.SetInt(204, 60, 1500);
            return new RecipeManager(driver, new PlcManager(driver));
        }

        // Rampa ve bekleme adımlarının doğru köşe noktalarını ürettiğini doğrular.
        [Fact]
        public void Calculate_RampaVeBekleme()
        {
            var points = RecipeProfile.Calculate(BuildRecipe(), 25f);

            Assert.Equal(3, points.Count);

            Assert.Equal(0, points[0].Minute);      // Başlangıç
            Assert.Equal(25f, points[0].Temp);
            Assert.Equal(2, points[0].StepNo);

            Assert.Equal(120, points[1].Minute);    // Rampa sonu
            Assert.Equal(520f, points[1].Temp);
            Assert.Equal(2, points[1].StepNo);

            Assert.Equal(360, points[2].Minute);    // Bekleme sonu (sıcaklık aynı, yatay çizgi)
            Assert.Equal(520f, points[2].Temp);
            Assert.Equal(3, points[2].StepNo);
        }

        // Vakum ve soğutma adımlarının süreye katılmadığını doğrular.
        [Fact]
        public void Calculate_VakumVeSogutmaSureyeGirmez()
        {
            var points = RecipeProfile.Calculate(BuildRecipe(), 25f);

            Assert.Equal(360, points.Last().Minute);   // Yalnızca 120 + 240
            Assert.DoesNotContain(points, p => p.StepNo == 1 || p.StepNo == 4);
        }

        // Sıcaklığı düşüren ısıtma adımının (kademeli iniş) da doğru hesaplandığını doğrular.
        [Fact]
        public void Calculate_InisRampasi()
        {
            var recipe = BuildRecipe();
            recipe.Steps.Add(new RecipeStep { StepNo = 5, StepType = 2, TargetTemp = 480, StepTime = 60 });

            var points = RecipeProfile.Calculate(recipe, 25f);

            Assert.Equal(420, points.Last().Minute);
            Assert.Equal(480f, points.Last().Temp);
        }

        // Isıtma adımı olmayan reçetede boş eğri döndüğünü doğrular.
        [Fact]
        public void Calculate_IsitmaYoksaBos()
        {
            var recipe = BuildRecipe();
            recipe.Steps = recipe.Steps.Where(s => s.StepType != 2).ToList();

            Assert.Empty(RecipeProfile.Calculate(recipe, 25f));
        }

        // Soğutma bitiş sıcaklığının reçeteden alındığını doğrular.
        [Fact]
        public void CoolingEndTemp_RecetedenAlinir()
        {
            Assert.Equal(150, RecipeProfile.GetCoolingEndTemp(BuildRecipe()));
        }

        // Kayıtlı reçetenin ön izleme eğrisinin dakika cinsinden, saatsiz geldiğini doğrular.
        [Fact]
        public async Task ReceteProfili_DakikaCinsindenGelir()
        {
            int id = (await CreateRecipeManager().AddRecipeAsync(BuildRecipe())).Data;

            var result = await new ProfileManager().GetRecipeProfileAsync(id, 30f);

            Assert.True(result.IsSuccess);
            Assert.False(result.Data.IsAnchored);
            Assert.Equal(30f, result.Data.StartTemp);
            Assert.Equal(360, result.Data.TotalMinutes);
            Assert.Equal(150, result.Data.CoolingEndTemp);
            Assert.All(result.Data.Points, p => Assert.Null(p.Timestamp));
        }

        // Şarj eğrisinin vakumdan sonra, ısıtmanın gerçekten başladığı ana oturtulduğunu doğrular.
        [Fact]
        public async Task SarjProfili_IsitmaBaslangicinaOturur()
        {
            var recipeManager = CreateRecipeManager();
            int id = (await recipeManager.AddRecipeAsync(BuildRecipe())).Data;
            await recipeManager.StartRecipeAsync(id, 5);

            // 10:00'da vakum sürüyor, 10:15'te ısıtmaya geçiliyor (o andaki set 35 °C).
            var vacuumTime = new DateTime(2026, 3, 1, 10, 0, 0);
            var heatingTime = new DateTime(2026, 3, 1, 10, 15, 0);
            var historian = new HistorianManager();
            await historian.LogProcessDataAsync(new ProcessDataLog { Timestamp = vacuumTime, SeqState = 20, TempPota = 30, TempSet = 0 });
            await historian.LogProcessDataAsync(new ProcessDataLog { Timestamp = heatingTime, SeqState = 30, TempPota = 33, TempSet = 35 });
            await historian.LogProcessDataAsync(new ProcessDataLog { Timestamp = heatingTime.AddMinutes(10), SeqState = 30, TempPota = 70, TempSet = 75 });

            int batchId;
            using (var context = new NorthwindContext())
            {
                batchId = context.Batches.Single().Id;
            }

            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.True(result.IsSuccess);
            Assert.True(result.Data.IsAnchored);
            Assert.Equal(heatingTime, result.Data.HeatingStart);
            Assert.Equal(35f, result.Data.StartTemp);
            Assert.Equal(heatingTime.AddMinutes(360), result.Data.PlannedEnd);

            Assert.Equal(heatingTime, result.Data.Points[0].Timestamp);
            Assert.Equal(heatingTime.AddMinutes(120), result.Data.Points[1].Timestamp);
            Assert.Equal(520f, result.Data.Points[1].Temp);
            Assert.Equal(heatingTime.AddMinutes(360), result.Data.Points[2].Timestamp);
        }

        // Isıtma henüz başlamamışken eğrinin saatsiz, son ölçülen sıcaklıktan başlayarak geldiğini doğrular.
        [Fact]
        public async Task SarjProfili_VakumdaykenSaatsiz()
        {
            var recipeManager = CreateRecipeManager();
            int id = (await recipeManager.AddRecipeAsync(BuildRecipe())).Data;
            await recipeManager.StartRecipeAsync(id, 5);

            await new HistorianManager().LogProcessDataAsync(new ProcessDataLog
            {
                Timestamp = new DateTime(2026, 3, 1, 10, 0, 0),
                SeqState = 20,
                TempPota = 42
            });

            int batchId;
            using (var context = new NorthwindContext())
            {
                batchId = context.Batches.Single().Id;
            }

            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.True(result.IsSuccess);
            Assert.False(result.Data.IsAnchored);
            Assert.Null(result.Data.PlannedEnd);
            Assert.Equal(42f, result.Data.StartTemp);
        }

        // Reçete sonradan değişse de şarj eğrisinin başlangıçtaki reçeteden hesaplandığını doğrular.
        [Fact]
        public async Task SarjProfili_BaslangicRecetesiniKullanir()
        {
            var recipeManager = CreateRecipeManager();
            int id = (await recipeManager.AddRecipeAsync(BuildRecipe())).Data;
            await recipeManager.StartRecipeAsync(id, 5);

            var changed = BuildRecipe();
            changed.Id = id;
            changed.Steps.Single(s => s.StepNo == 3).StepTime = 600;
            await recipeManager.UpdateRecipeAsync(changed);

            int batchId;
            using (var context = new NorthwindContext())
            {
                batchId = context.Batches.Single().Id;
            }

            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.Equal(360, result.Data.TotalMinutes);   // 120 + 240; sonradan girilen 600 değil
        }

        // Olmayan şarj ve reçete için hata döndüğünü doğrular.
        [Fact]
        public async Task OlmayanKayit_HataDoner()
        {
            var manager = new ProfileManager();

            Assert.False((await manager.GetBatchProfileAsync(987654)).IsSuccess);
            Assert.False((await manager.GetRecipeProfileAsync(987654, 20f)).IsSuccess);
        }
    }
}