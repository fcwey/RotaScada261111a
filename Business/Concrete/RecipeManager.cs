using Business.Abstract;
using Business.ValidationRules;
using Core.Utilities.Results;
using DataAccess.Abstract;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Enums;
using Entities.Structures;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class RecipeManager : IRecipeService
    {
        private readonly IS7DriverDal _s7DriverDal;
        private readonly IPlcService _plcService;

        // DB202'ye aynı anda tek reçete yazılmasını sağlar (yazma ve komut birlikte kilitlenir).
        private readonly SemaphoreSlim _plcRecipeLock = new SemaphoreSlim(1, 1);

        // Aynı anda tek program başlatma işleminin yürümesini sağlar.
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);

        // Sürücü ve PLC servis bağımlılıklarını alır.
        public RecipeManager(IS7DriverDal s7DriverDal, IPlcService plcService)
        {
            _s7DriverDal = s7DriverDal;
            _plcService = plcService;
        }

        // Kullanımdaki reçeteleri adımlarıyla birlikte listeler.
        public async Task<IDataResult<List<Recipe>>> GetAllRecipesAsync()
        {
            await using var context = new NorthwindContext();

            var recipes = await context.Recipes
                .AsNoTracking()
                .Include(r => r.Steps)
                .Where(r => r.IsActive)
                .OrderBy(r => r.RecipeName)
                .ToListAsync();

            foreach (var recipe in recipes)
                recipe.Steps = recipe.Steps.OrderBy(s => s.StepNo).ToList();

            return new SuccessDataResult<List<Recipe>>(recipes, "Reçeteler listelendi.");
        }

        // Verilen id'ye ait reçeteyi adımlarıyla birlikte getirir.
        public async Task<IDataResult<Recipe>> GetRecipeByIdAsync(int id)
        {
            var recipe = await LoadRecipeAsync(id);
            if (recipe == null)
                return new ErrorDataResult<Recipe>("Reçete bulunamadı.");

            return new SuccessDataResult<Recipe>(recipe);
        }

        // Reçeteyi doğrulayıp veritabanına ekler; oluşan reçetenin id'sini döner.
        public async Task<IDataResult<int>> AddRecipeAsync(Recipe recipe)
        {
            RecipeRules.Normalize(recipe);

            var error = RecipeRules.Validate(recipe, await ReadLimitsAsync());
            if (error != null)
                return new ErrorDataResult<int>(error.Message);

            var entity = new Recipe
            {
                CreatedAt = DateTime.Now,
                IsActive = true,
                Steps = recipe.Steps.OrderBy(s => s.StepNo).Select(CloneStep).ToList()
            };
            CopyHeader(recipe, entity);

            await using var context = new NorthwindContext();
            await context.Recipes.AddAsync(entity);
            await context.SaveChangesAsync();

            return new SuccessDataResult<int>(entity.Id, $"'{entity.RecipeName}' reçetesi kaydedildi.");
        }

        // Mevcut reçeteyi doğrulayıp başlığını ve adımlarını günceller.
        public async Task<IResult> UpdateRecipeAsync(Recipe recipe)
        {
            RecipeRules.Normalize(recipe);

            var error = RecipeRules.Validate(recipe, await ReadLimitsAsync());
            if (error != null)
                return new ErrorResult(error.Message);

            await using var context = new NorthwindContext();

            var existing = await context.Recipes
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == recipe.Id && r.IsActive);

            if (existing == null)
                return new ErrorResult("Güncellenecek reçete bulunamadı.");

            CopyHeader(recipe, existing);

            // Eski adımlar silinip yenileri eklenir.
            context.RecipeSteps.RemoveRange(existing.Steps.ToList());
            foreach (var step in recipe.Steps.OrderBy(s => s.StepNo))
            {
                var newStep = CloneStep(step);
                newStep.RecipeId = existing.Id;
                await context.RecipeSteps.AddAsync(newStep);
            }

            await context.SaveChangesAsync();
            return new SuccessResult($"'{existing.RecipeName}' reçetesi güncellendi.");
        }

        // Reçeteyi siler; şarj kayıtlarında kullanılmışsa silmek yerine kullanımdan kaldırır.
        public async Task<IResult> DeleteRecipeAsync(int id)
        {
            await using var context = new NorthwindContext();

            var recipe = await context.Recipes.FirstOrDefaultAsync(r => r.Id == id && r.IsActive);
            if (recipe == null)
                return new ErrorResult("Silinecek reçete bulunamadı.");

            bool usedInBatch = await context.Batches.AnyAsync(b => b.RecipeId == id);
            if (usedInBatch)
            {
                // Geçmiş şarj raporları bozulmasın diye kayıt tutulur, listeden kaldırılır.
                recipe.IsActive = false;
                await context.SaveChangesAsync();
                return new SuccessResult($"'{recipe.RecipeName}' reçetesi kullanımdan kaldırıldı (geçmiş şarjlarda kullanıldığı için silinmedi).");
            }

            context.Recipes.Remove(recipe);
            await context.SaveChangesAsync();
            return new SuccessResult($"'{recipe.RecipeName}' reçetesi silindi.");
        }

        // Reçeteyi DB202'ye yazar ve PLC'nin yalnızca kontrol etmesini ister (Validate komutu).
        public async Task<IDataResult<ScadaCommandResult>> ValidateOnPlcAsync(int recipeId)
        {
            var recipe = await LoadRecipeAsync(recipeId);
            if (recipe == null)
                return new ErrorDataResult<ScadaCommandResult>("Reçete bulunamadı.");

            return await SendRecipeAsync(recipe, ScadaCommandCode.Validate);
        }

        // Şarj numarası ayırır, reçeteyi DB202'ye yazıp Start komutunu bu numarayla gönderir; kabul edilirse şarjı açar.
        public async Task<IDataResult<ScadaCommandResult>> StartRecipeAsync(int recipeId, int userId)
        {
            var recipe = await LoadRecipeAsync(recipeId);
            if (recipe == null)
                return new ErrorDataResult<ScadaCommandResult>("Başlatılacak reçete bulunamadı.");

            await _startLock.WaitAsync();
            try
            {
                // Şarj numarası Start komutundan önce gerekir; veritabanına ulaşılamazsa program 0 numarayla yine başlatılır.
                int? batchId = await ReserveBatchAsync(recipe, userId);
                int plcBatchId = batchId ?? 0;

                // Şarj numarası 32 bittir: Param1 düşük 16 bit, Param2 yüksek 16 bit.
                short param1 = unchecked((short)(plcBatchId & 0xFFFF));
                short param2 = unchecked((short)(plcBatchId >> 16));

                var result = await SendRecipeAsync(recipe, ScadaCommandCode.Start, param1, param2);
                if (!result.IsSuccess)
                {
                    if (batchId.HasValue)
                        await ReleaseBatchAsync(batchId.Value);

                    return result;
                }

                // Program PLC'de başladı; ayrılan şarj kaydı açılır.
                string? batchCode = batchId.HasValue
                    ? await ActivateBatchAsync(batchId.Value, recipe, userId)
                    : null;

                string message = batchCode != null
                    ? $"'{recipe.RecipeName}' programı başladı. Şarj kodu: {batchCode}"
                    : $"'{recipe.RecipeName}' programı başladı ama şarj kaydı açılamadı. Veritabanını kontrol edin.";

                if (result.Data != null && result.Data.HasWarning)
                    message += " UYARI: soğutma bitiş sıcaklığı kapak açma sınırından yüksek; program sonunda korumalı bekleme olacak.";

                return new SuccessDataResult<ScadaCommandResult>(result.Data, message);
            }
            finally
            {
                _startLock.Release();
            }
        }

        // PLC'de çalışan reçeteyi DB203'ten okuyup döner.
        public async Task<IDataResult<Recipe>> GetActiveRecipeAsync()
        {
            try
            {
                var buffer = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbActiveRecipe, 0, RecipeCodec.TotalSize);
                return new SuccessDataResult<Recipe>(RecipeCodec.Decode(buffer));
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<Recipe>($"Çalışan reçete okunamadı: {ex.Message}");
            }
        }

        // Çalışan programın değiştirilmiş halini PLC'ye gönderir; kabul edilirse değişikliği açık şarja işler.
        public async Task<IDataResult<ScadaCommandResult>> ApplyEditAsync(Recipe editedRecipe, int userId)
        {
            var result = await SendRecipeAsync(editedRecipe, ScadaCommandCode.ApplyEdit);
            if (!result.IsSuccess)
                return result;

            bool recorded = await AddEditRevisionAsync(editedRecipe, userId);

            string message = recorded
                ? "Değişiklik uygulandı ve şarj kaydına işlendi."
                : "Değişiklik uygulandı ama şarj kaydına işlenemedi (açık şarj bulunamadı ya da veritabanına ulaşılamadı).";

            if (result.Data != null && result.Data.HasWarning)
                message += " UYARI: soğutma bitiş sıcaklığı kapak açma sınırından yüksek; program sonunda korumalı bekleme olacak.";

            return new SuccessDataResult<ScadaCommandResult>(result.Data, message);
        }

        // Reçeteyi doğrular, DB202'ye yazar, geri okuyup karşılaştırır ve verilen komutu parametreleriyle gönderir.
        private async Task<IDataResult<ScadaCommandResult>> SendRecipeAsync(Recipe recipe, ScadaCommandCode command, short param1 = 0, short param2 = 0)
        {
            await _plcRecipeLock.WaitAsync();
            try
            {
                var layout = await _plcService.VerifyLayoutAsync();
                if (!layout.IsSuccess)
                    return new ErrorDataResult<ScadaCommandResult>(layout.Message);

                RecipeRules.Normalize(recipe);

                var error = RecipeRules.Validate(recipe, await ReadLimitsAsync());
                if (error != null)
                    return new ErrorDataResult<ScadaCommandResult>(error, error.Message);

                byte[] data = RecipeCodec.Encode(recipe);
                await _s7DriverDal.WriteBytesAsync(ScadaLayout.DbRecipe, 0, data);

                // Yazılan veri geri okunup karşılaştırılır; uyuşmuyorsa komut gönderilmez.
                byte[] readBack = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbRecipe, 0, data.Length);
                if (!readBack.SequenceEqual(data))
                    return new ErrorDataResult<ScadaCommandResult>("DB202'ye yazılan reçete geri okunanla uyuşmuyor. Komut gönderilmedi.");

                return await _plcService.SendCommandAsync(command, param1, param2);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<ScadaCommandResult>($"Reçete PLC'ye gönderilemedi: {ex.Message}");
            }
            finally
            {
                _plcRecipeLock.Release();
            }
        }

        // Doğrulama sınırlarını DB204'ten okur; PLC'ye ulaşılamazsa varsayılan sınırları döner.
        private async Task<RecipeLimits> ReadLimitsAsync()
        {
            try
            {
                var buffer = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, 0, 62);

                return new RecipeLimits
                {
                    MaxPotaTemp = S7Bytes.GetInt(buffer, 2),
                    FanMinSpeed = S7Bytes.GetInt(buffer, 50),
                    N2MfcMaxLph = S7Bytes.GetInt(buffer, 58),
                    NH3MfcMaxLph = S7Bytes.GetInt(buffer, 60)
                };
            }
            catch
            {
                return new RecipeLimits();
            }
        }

        // Kullanımdaki reçeteyi adımları sıralı olarak veritabanından yükler; yoksa null döner.
        private static async Task<Recipe?> LoadRecipeAsync(int id)
        {
            await using var context = new NorthwindContext();

            var recipe = await context.Recipes
                .AsNoTracking()
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);

            if (recipe != null)
                recipe.Steps = recipe.Steps.OrderBy(s => s.StepNo).ToList();

            return recipe;
        }

        // Start komutundan önce şarj kaydını "STARTING" durumunda ayırır ve numarasını döner; veritabanı hatasında null döner.
        private static async Task<int?> ReserveBatchAsync(Recipe recipe, int userId)
        {
            try
            {
                await using var context = new NorthwindContext();
                DateTime now = DateTime.Now;

                // Daha önce yarım kalmış ayırmalar temizlenir.
                await context.Batches.Where(b => b.Status == "STARTING").ExecuteDeleteAsync();

                var batch = new Batch
                {
                    BatchCode = $"BATCH-{now:yyyyMMdd-HHmmss}",
                    RecipeId = recipe.Id,
                    OperatorUserId = userId,
                    StartTime = now,
                    Status = "STARTING",
                    CompletionNote = "Start komutu gönderiliyor."
                };

                await context.Batches.AddAsync(batch);
                await context.SaveChangesAsync();
                return batch.Id;
            }
            catch
            {
                return null;
            }
        }

        // Start kabul edilince: kapanmamış eski şarjları kapatır, ayrılan şarjı açar ve reçetenin başlangıç kopyasını saklar; şarj kodunu döner.
        private static async Task<string?> ActivateBatchAsync(int batchId, Recipe recipe, int userId)
        {
            try
            {
                await using var context = new NorthwindContext();
                DateTime now = DateTime.Now;

                var staleBatches = await context.Batches.Where(b => b.Status == "RUNNING").ToListAsync();
                foreach (var stale in staleBatches)
                {
                    stale.Status = "ABORTED";
                    stale.EndTime = now;
                    stale.CompletionNote = "Bitişi kaydedilmemiş şarj; yeni program başlatılırken kapatıldı.";
                }

                var batch = await context.Batches.FirstOrDefaultAsync(b => b.Id == batchId);
                if (batch == null)
                    return null;

                batch.Status = "RUNNING";
                batch.StartTime = now;
                batch.CompletionNote = "Program başlatıldı.";
                await context.SaveChangesAsync();

                // Reçetenin PLC'ye gönderilen hali şarja kopyalanır; reçete sonradan değişse de bu kayıt değişmez.
                await context.BatchRecipeRevisions.AddAsync(new BatchRecipeRevision
                {
                    BatchId = batch.Id,
                    Timestamp = now,
                    UserId = userId,
                    Reason = "START",
                    RecipeJson = RecipeSnapshot.Serialize(recipe)
                });
                await context.SaveChangesAsync();

                return batch.BatchCode;
            }
            catch
            {
                return null;
            }
        }

        // Start reddedilince ayrılan şarj kaydını siler; silme hatası yok sayılır (bir sonraki Start'ta temizlenir).
        private static async Task ReleaseBatchAsync(int batchId)
        {
            try
            {
                await using var context = new NorthwindContext();
                await context.Batches.Where(b => b.Id == batchId && b.Status == "STARTING").ExecuteDeleteAsync();
            }
            catch
            {
                // Kayıt silinemese de komutun sonucu operatöre dönmelidir.
            }
        }

        // Çalışırken yapılan değişikliği açık şarjın plan geçmişine ekler; eklendiyse true döner.
        private static async Task<bool> AddEditRevisionAsync(Recipe editedRecipe, int userId)
        {
            try
            {
                await using var context = new NorthwindContext();

                var batch = await context.Batches
                    .Where(b => b.Status == "RUNNING")
                    .OrderByDescending(b => b.StartTime)
                    .FirstOrDefaultAsync();

                if (batch == null)
                    return false;

                await context.BatchRecipeRevisions.AddAsync(new BatchRecipeRevision
                {
                    BatchId = batch.Id,
                    Timestamp = DateTime.Now,
                    UserId = userId,
                    Reason = "EDIT",
                    RecipeJson = RecipeSnapshot.Serialize(editedRecipe)
                });
                await context.SaveChangesAsync();

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Reçete başlık alanlarını kaynak nesneden hedef nesneye kopyalar.
        private static void CopyHeader(Recipe source, Recipe target)
        {
            target.RecipeType = source.RecipeType;
            target.RecipeName = source.RecipeName;
            target.TotalStepCount = source.Steps.Count;
            target.PressureSetPoint = source.PressureSetPoint;
            target.PressureMode = source.PressureMode;
            target.PressureModeTime = source.PressureModeTime;
            target.PressureModeTemp = source.PressureModeTemp;
            target.GsoakTcId = source.GsoakTcId;
            target.CO2_Period = source.CO2_Period;
        }

        // Adımın veritabanı kimliği taşımayan temiz bir kopyasını üretir.
        private static RecipeStep CloneStep(RecipeStep step)
        {
            return new RecipeStep
            {
                StepNo = step.StepNo,
                StepType = step.StepType,
                Vac_SetPoint = step.Vac_SetPoint,
                Vac_Timeout = step.Vac_Timeout,
                TargetTemp = step.TargetTemp,
                StepTime = step.StepTime,
                GSoak = step.GSoak,
                N2_Flow = step.N2_Flow,
                NH3_Flow = step.NH3_Flow,
                dNH3_Flow = step.dNH3_Flow,
                CO2_Perc = step.CO2_Perc,
                Su_On = step.Su_On,
                H2_Auto = step.H2_Auto,
                H2_Set = step.H2_Set,
                FanSpeed = step.FanSpeed
            };
        }
    }
}