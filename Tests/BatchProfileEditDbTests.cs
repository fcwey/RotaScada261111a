using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Xunit;

namespace Tests
{
    // Çalışırken yapılan değişikliğin teorik eğriye yansımasını doğrulayan testler
    [Collection("Database")]
    public class BatchProfileEditDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public BatchProfileEditDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
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

        // Vakum, rampa, bekleme ve soğutmadan oluşan reçete üretir; rampa ve bekleme süreleri verilebilir.
        private static Recipe BuildRecipe(int rampMinutes = 120, int soakMinutes = 240)
        {
            return new Recipe
            {
                RecipeType = 2,
                RecipeName = "Nitrasyon Şaft",
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = 520, StepTime = rampMinutes, N2_Flow = 500 },
                    new RecipeStep { StepNo = 3, StepType = 2, TargetTemp = 520, StepTime = soakMinutes, N2_Flow = 100, NH3_Flow = 800 },
                    new RecipeStep { StepNo = 4, StepType = 3, TargetTemp = 150, N2_Flow = 500 }
                }
            };
        }

        // Üç saat önce ısıtmaya başlamış bir şarj hazırlar; reçete yöneticisini, reçete ve şarj numaralarını döner.
        private static async Task<(RecipeManager Manager, int RecipeId, int BatchId, DateTime HeatingStart)> StartBatchAsync()
        {
            var manager = CreateRecipeManager();
            int recipeId = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            await manager.StartRecipeAsync(recipeId, 5);

            DateTime now = DateTime.Now;
            DateTime heatingStart = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond).AddHours(-3);

            await new HistorianManager().LogProcessDataAsync(new ProcessDataLog
            {
                Timestamp = heatingStart,
                SeqState = 30,
                ActiveStep = 2,
                TempPota = 33,
                TempSet = 35
            });

            int batchId;
            using (var context = new NorthwindContext())
            {
                batchId = context.Batches.Single().Id;
            }

            return (manager, recipeId, batchId, heatingStart);
        }

        // Çalışan adımdaki değişikliğin eğriye yansıdığını, geçmiş adımdaki değişikliğin yok sayıldığını doğrular.
        [Fact]
        public async Task Degisiklik_CalisanAdimdanItibarenYansir()
        {
            var (manager, recipeId, batchId, heatingStart) = await StartBatchAsync();

            // Isıtma başladıktan 130 dakika sonra 3. adım (bekleme) çalışıyor.
            await new HistorianManager().LogProcessDataAsync(new ProcessDataLog
            {
                Timestamp = heatingStart.AddMinutes(130),
                SeqState = 30,
                ActiveStep = 3,
                TempPota = 520,
                TempSet = 520
            });

            // Hem geçmişteki rampa (120 → 60) hem çalışan bekleme (240 → 300) değiştiriliyor.
            var edited = BuildRecipe(rampMinutes: 60, soakMinutes: 300);
            edited.Id = recipeId;
            var editResult = await manager.ApplyEditAsync(edited, 9);

            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.True(editResult.IsSuccess);
            Assert.True(result.IsSuccess);
            Assert.False(result.Data.IsOriginal);
            Assert.Equal(420, result.Data.TotalMinutes);                       // 120 (eski rampa) + 300 (yeni bekleme)
            Assert.Equal(120, result.Data.Points[1].Minute);                   // Geçmiş adım değişmedi
            Assert.Equal(heatingStart.AddMinutes(420), result.Data.PlannedEnd);
            Assert.Single(result.Data.EditTimes);
        }

        // original=true ile değişiklikten etkilenmeyen başlangıç planının geldiğini doğrular.
        [Fact]
        public async Task Original_BaslangicPlaniniVerir()
        {
            var (manager, recipeId, batchId, heatingStart) = await StartBatchAsync();

            await new HistorianManager().LogProcessDataAsync(new ProcessDataLog
            {
                Timestamp = heatingStart.AddMinutes(130),
                SeqState = 30,
                ActiveStep = 3
            });

            var edited = BuildRecipe(soakMinutes: 300);
            edited.Id = recipeId;
            await manager.ApplyEditAsync(edited, 9);

            var result = await new ProfileManager().GetBatchProfileAsync(batchId, original: true);

            Assert.True(result.Data.IsOriginal);
            Assert.Equal(360, result.Data.TotalMinutes);                       // 120 + 240
            Assert.Equal(heatingStart.AddMinutes(360), result.Data.PlannedEnd);
        }

        // Henüz çalışmamış bir adımın değişikliğinin eğriye yansıdığını doğrular.
        [Fact]
        public async Task GelecekAdimDegisikligi_Yansir()
        {
            var (manager, recipeId, batchId, _) = await StartBatchAsync();

            // Son ölçüm kaydında hâlâ 2. adım (rampa) çalışıyor; 3. adım henüz gelmedi.
            var edited = BuildRecipe(soakMinutes: 180);
            edited.Id = recipeId;
            await manager.ApplyEditAsync(edited, 9);

            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.Equal(300, result.Data.TotalMinutes);                       // 120 + 180
        }

        // Art arda iki değişiklikten sonra eğrinin en son planı gösterdiğini doğrular.
        [Fact]
        public async Task IkiDegisiklik_SonPlanGecerli()
        {
            var (manager, recipeId, batchId, _) = await StartBatchAsync();

            var first = BuildRecipe(soakMinutes: 300);
            first.Id = recipeId;
            await manager.ApplyEditAsync(first, 9);

            var second = BuildRecipe(soakMinutes: 200);
            second.Id = recipeId;
            await manager.ApplyEditAsync(second, 9);

            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.Equal(320, result.Data.TotalMinutes);                       // 120 + 200
            Assert.Equal(2, result.Data.EditTimes.Count);
        }

        // Çalışan şarjın eğrisinin şarj numarası verilmeden alınabildiğini doğrular.
        [Fact]
        public async Task AktifProfil_CalisanSarjiBulur()
        {
            var (_, _, batchId, heatingStart) = await StartBatchAsync();

            var result = await new ProfileManager().GetActiveBatchProfileAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(batchId, result.Data.BatchId);
            Assert.True(result.Data.IsAnchored);
            Assert.Equal(heatingStart, result.Data.HeatingStart);
        }

        // Çalışan şarj yokken anlaşılır bir hata döndüğünü doğrular.
        [Fact]
        public async Task AktifProfil_SarjYokkenHata()
        {
            var result = await new ProfileManager().GetActiveBatchProfileAsync();

            Assert.False(result.IsSuccess);
            Assert.Contains("Çalışan şarj yok", result.Message);
        }
    }
}