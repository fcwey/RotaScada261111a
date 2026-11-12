using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Xunit;

namespace Tests
{
    // Adıma atlama ve soğutmaya geçişin teorik eğriye yansımasını doğrulayan testler
    [Collection("Database")]
    public class BatchProfileJumpDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public BatchProfileJumpDbTests(DatabaseFixture database)
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

        // Vakum (1), rampa (2), bekleme (3) ve soğutmadan (4) oluşan reçete üretir.
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

        // Üç saat önce 35 °C'den ısıtmaya başlamış ve şu an 3. adımda olan bir şarj hazırlar.
        private static async Task<(RecipeManager Manager, int RecipeId, int BatchId)> StartBatchInStep3Async()
        {
            var manager = CreateRecipeManager();
            int recipeId = (await manager.AddRecipeAsync(BuildRecipe())).Data;
            await manager.StartRecipeAsync(recipeId, 5);

            DateTime now = DateTime.Now;
            DateTime heatingStart = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond).AddHours(-3);

            var historian = new HistorianManager();
            await historian.LogProcessDataAsync(new ProcessDataLog { Timestamp = heatingStart, SeqState = 30, ActiveStep = 2, TempPota = 33, TempSet = 35 });
            await historian.LogProcessDataAsync(new ProcessDataLog { Timestamp = heatingStart.AddMinutes(130), SeqState = 30, ActiveStep = 3, TempPota = 520, TempSet = 520 });

            int batchId;
            using (var context = new NorthwindContext())
            {
                batchId = context.Batches.Single().Id;
            }

            return (manager, recipeId, batchId);
        }

        // 3. adımdayken 2 ve 3 değiştirilip 2. adımın 10. dakikasına dönülünce 2. adımın yeni değerle planlandığını doğrular.
        [Fact]
        public async Task DegisiklikSonrasiGeriAlma_IkinciAdimYeniDegerleCalisir()
        {
            var (manager, recipeId, batchId) = await StartBatchInStep3Async();

            // 3. adımdayken rampa 120 → 150, bekleme 240 → 300 yapılıyor.
            var edited = BuildRecipe(rampMinutes: 150, soakMinutes: 300);
            edited.Id = recipeId;
            await manager.ApplyEditAsync(edited, 9);

            // Geri almadan önce: geçmişteki rampa eski (120), çalışan bekleme yeni (300).
            var beforeJump = await new ProfileManager().GetBatchProfileAsync(batchId);
            Assert.Equal(420, beforeJump.Data.TotalMinutes);

            // Isıtma başladıktan yaklaşık 180 dakika sonra 2. adımın 10. dakikasına dönülüyor.
            var jump = await new BatchManager().RecordPlanEventAsync("GOTO", 2, 10, 9);
            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.True(jump.IsSuccess);
            Assert.True(result.IsSuccess);
            Assert.Single(result.Data.JumpTimes);
            Assert.Equal(new[] { 0, 1 }, result.Data.Points.Select(p => p.Segment).Distinct().ToArray());

            // İlk parça atlama anında (yaklaşık 180. dakika) kesilmiş, o an bekleme sıcaklığında.
            var firstSegment = result.Data.Points.Where(p => p.Segment == 0).ToList();
            Assert.InRange(firstSegment.Last().Minute, 180, 180.5);
            Assert.Equal(520f, firstSegment.Last().Temp);

            // İkinci parça 2. adımın YENİ süresinin (150 dk) 10. dakikasından başlıyor: 35 + (520 − 35) × 10 / 150.
            var secondSegment = result.Data.Points.Where(p => p.Segment == 1).ToList();
            Assert.Equal(2, secondSegment.First().StepNo);
            Assert.InRange(secondSegment.First().Temp, 67.2f, 67.5f);

            // Kalan plan: 2. adımdan 140 dk + 3. adımın yeni süresi 300 dk; atlama anına eklenir.
            Assert.InRange(result.Data.TotalMinutes, 180 + 140 + 300, 180 + 140 + 300 + 0.5);
        }

        // Değişiklik olmadan geri alınınca gidilen adımın başlangıçtaki değerlerle planlandığını doğrular.
        [Fact]
        public async Task GeriAlma_DegisiklikYokkenIlkDegerlerle()
        {
            var (_, _, batchId) = await StartBatchInStep3Async();

            await new BatchManager().RecordPlanEventAsync("GOTO", 2, 60, 9);
            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            // 2. adımın 60. dakikası: 35 ile 520'nin tam ortası.
            var secondSegment = result.Data.Points.Where(p => p.Segment == 1).ToList();
            Assert.InRange(secondSegment.First().Temp, 277f, 278f);

            // Kalan plan: 60 dk rampa + 240 dk bekleme.
            Assert.InRange(result.Data.TotalMinutes, 180 + 60 + 240, 180 + 60 + 240 + 0.5);
        }

        // İleri atlamanın planı kısalttığını doğrular.
        [Fact]
        public async Task IleriAtlama_PlaniKisaltir()
        {
            var (_, _, batchId) = await StartBatchInStep3Async();

            // 3. adımın (240 dk bekleme) 200. dakikasına atlanıyor: geriye 40 dakika kalır.
            await new BatchManager().RecordPlanEventAsync("GOTO", 3, 200, 9);
            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.InRange(result.Data.TotalMinutes, 180 + 40, 180 + 40 + 0.5);
        }

        // Soğutmaya geçilince ısıtma planının o anda bittiğini doğrular.
        [Fact]
        public async Task SogutmayaGecis_PlanBiter()
        {
            var (_, _, batchId) = await StartBatchInStep3Async();

            await new BatchManager().RecordPlanEventAsync("SKIP", 0, 0, 9);
            var result = await new ProfileManager().GetBatchProfileAsync(batchId);

            Assert.InRange(result.Data.TotalMinutes, 180, 180.5);
            Assert.All(result.Data.Points, p => Assert.Equal(0, p.Segment));
            Assert.Single(result.Data.JumpTimes);
        }

        // Başlangıç planı istendiğinde atlamaların ve değişikliklerin yok sayıldığını doğrular.
        [Fact]
        public async Task Original_AtlamalariYokSayar()
        {
            var (_, _, batchId) = await StartBatchInStep3Async();
            await new BatchManager().RecordPlanEventAsync("GOTO", 2, 10, 9);

            var result = await new ProfileManager().GetBatchProfileAsync(batchId, original: true);

            Assert.Equal(360, result.Data.TotalMinutes);
            Assert.All(result.Data.Points, p => Assert.Equal(0, p.Segment));
        }

        // Atlama olayının şarj ayrıntısında adım ve dakikasıyla göründüğünü doğrular.
        [Fact]
        public async Task SarjAyrintisi_AtlamayiGosterir()
        {
            var (_, _, batchId) = await StartBatchInStep3Async();
            await new BatchManager().RecordPlanEventAsync("GOTO", 2, 10, 9);

            var detail = await new BatchManager().GetBatchDetailAsync(batchId);

            var jump = detail.Data.Revisions.Single(r => r.Reason == "GOTO");
            Assert.Equal(2, jump.StepNo);
            Assert.Equal(10, jump.StepMinute);
            Assert.Null(jump.Recipe);
        }

        // Çalışan şarj yokken plan olayının kaydedilmediğini ve hata döndüğünü doğrular.
        [Fact]
        public async Task SarjYokken_OlayKaydedilmez()
        {
            var result = await new BatchManager().RecordPlanEventAsync("GOTO", 2, 10, 9);

            using var context = new NorthwindContext();
            Assert.False(result.IsSuccess);
            Assert.Equal(0, context.BatchRecipeRevisions.Count());
        }
    }
}