using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Dtos;
using Entities.Structures;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class ProfileManager : IProfileService
    {
        private const short SeqHeating = 30;        // PLC sekans durumu: ısıtma
        private const float DefaultStartTemp = 20f; // Başlangıç sıcaklığı bilinmiyorsa kullanılır

        // Eğrinin bir parçası: belirli bir andan itibaren, belirli bir adımın belirli dakikasından başlayan plan
        private class Segment
        {
            public double StartMinute { get; set; }     // Parçanın başladığı an (ısıtma başlangıcından dakika)
            public double? EndMinute { get; set; }      // Bir atlamayla kesildiyse kesildiği an
            public int StartStep { get; set; }          // Parçanın başladığı adım
            public double StartElapsed { get; set; }    // O adımda geçmiş sayılan dakika
            public Recipe Plan { get; set; } = new Recipe();
        }

        // Kayıtlı reçetenin teorik eğrisini verilen başlangıç sıcaklığından hesaplar.
        public async Task<IDataResult<RecipeProfileDto>> GetRecipeProfileAsync(int recipeId, float startTemp)
        {
            try
            {
                await using var context = new NorthwindContext();

                var recipe = await context.Recipes
                    .AsNoTracking()
                    .Include(r => r.Steps)
                    .FirstOrDefaultAsync(r => r.Id == recipeId && r.IsActive);

                if (recipe == null)
                    return new ErrorDataResult<RecipeProfileDto>("Reçete bulunamadı.");

                var segments = new List<Segment> { CreateFirstSegment(recipe) };
                return new SuccessDataResult<RecipeProfileDto>(Build(recipe, segments, startTemp, null));
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<RecipeProfileDto>($"Teorik eğri hesaplanamadı: {ex.Message}");
            }
        }

        // O an çalışan (RUNNING) şarjı bulur ve geçerli teorik eğrisini döner.
        public async Task<IDataResult<RecipeProfileDto>> GetActiveBatchProfileAsync()
        {
            try
            {
                int? batchId;
                await using (var context = new NorthwindContext())
                {
                    batchId = await context.Batches
                        .AsNoTracking()
                        .Where(b => b.Status == "RUNNING")
                        .OrderByDescending(b => b.StartTime)
                        .Select(b => (int?)b.Id)
                        .FirstOrDefaultAsync();
                }

                if (batchId == null)
                    return new ErrorDataResult<RecipeProfileDto>("Çalışan şarj yok.");

                return await GetBatchProfileAsync(batchId.Value, false);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<RecipeProfileDto>($"Çalışan şarjın teorik eğrisi hesaplanamadı: {ex.Message}");
            }
        }

        // Şarjın teorik eğrisini hesaplar: değişiklikleri ve adıma atlamaları uygular, ısıtmanın başladığı ana oturtur.
        public async Task<IDataResult<RecipeProfileDto>> GetBatchProfileAsync(int batchId, bool original = false)
        {
            try
            {
                await using var context = new NorthwindContext();

                var batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId);
                if (batch == null)
                    return new ErrorDataResult<RecipeProfileDto>("Şarj bulunamadı.");

                var revisions = await context.BatchRecipeRevisions
                    .AsNoTracking()
                    .Where(r => r.BatchId == batchId)
                    .OrderBy(r => r.Id)
                    .ToListAsync();

                var startRecipe = await LoadStartRecipeAsync(context, batch, revisions);
                if (startRecipe == null)
                    return new ErrorDataResult<RecipeProfileDto>("Şarjın reçetesi bulunamadı.");

                // Isıtmaya geçilen ilk ölçüm kaydı, eğrinin başladığı anı ve başlangıç sıcaklığını verir.
                var firstHeatingLog = await context.ProcessDataLogs
                    .AsNoTracking()
                    .Where(p => p.BatchId == batchId && p.SeqState == SeqHeating)
                    .OrderBy(p => p.Timestamp)
                    .FirstOrDefaultAsync();

                DateTime? heatingStart = firstHeatingLog?.Timestamp;
                float startTemp;

                if (firstHeatingLog != null)
                {
                    startTemp = firstHeatingLog.TempSet > 0 ? firstHeatingLog.TempSet : firstHeatingLog.TempPota;
                }
                else
                {
                    // Isıtma henüz başlamadı (vakum sürüyor): eğri yalnızca dakika cinsinden verilir.
                    var lastLog = await context.ProcessDataLogs
                        .AsNoTracking()
                        .Where(p => p.BatchId == batchId)
                        .OrderByDescending(p => p.Timestamp)
                        .FirstOrDefaultAsync();

                    startTemp = lastLog != null && lastLog.TempPota > 0 ? lastLog.TempPota : DefaultStartTemp;
                }

                var segments = new List<Segment> { CreateFirstSegment(startRecipe) };
                Recipe recipeInForce = startRecipe;

                if (!original)
                    recipeInForce = await ApplyEventsAsync(context, batch.Id, revisions, segments, startRecipe, heatingStart);

                var profile = Build(recipeInForce, segments, startTemp, heatingStart);
                profile.BatchId = batchId;
                profile.IsOriginal = original;
                profile.EditTimes = revisions.Where(r => r.Reason == "EDIT").Select(r => r.Timestamp).ToList();
                profile.JumpTimes = revisions.Where(r => r.Reason == "GOTO" || r.Reason == "SKIP").Select(r => r.Timestamp).ToList();

                return new SuccessDataResult<RecipeProfileDto>(profile);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<RecipeProfileDto>($"Şarjın teorik eğrisi hesaplanamadı: {ex.Message}");
            }
        }

        // Şarjın başlangıçtaki reçete kopyasını döner; kopya yoksa (eski şarj) reçetenin şimdiki halini kullanır.
        private static async Task<Recipe?> LoadStartRecipeAsync(NorthwindContext context, Batch batch, List<BatchRecipeRevision> revisions)
        {
            var startRevision = revisions.FirstOrDefault(r => r.Reason == "START");
            var snapshot = RecipeSnapshot.Deserialize(startRevision?.RecipeJson);
            if (snapshot != null)
                return snapshot;

            return await context.Recipes
                .AsNoTracking()
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == batch.RecipeId);
        }

        // Plan olaylarını sırayla uygular: değişiklik mevcut parçayı günceller, atlama parçayı kesip yenisini başlatır. Yürürlükteki reçeteyi döner.
        private static async Task<Recipe> ApplyEventsAsync(
            NorthwindContext context, int batchId, List<BatchRecipeRevision> revisions,
            List<Segment> segments, Recipe startRecipe, DateTime? heatingStart)
        {
            Recipe recipeInForce = startRecipe;
            bool planEnded = false;

            foreach (var revision in revisions.Where(r => r.Reason != "START"))
            {
                if (planEnded)
                    break;

                var current = segments[segments.Count - 1];

                if (revision.Reason == "EDIT")
                {
                    var edited = RecipeSnapshot.Deserialize(revision.RecipeJson);
                    if (edited == null)
                        continue;

                    // Değişiklik anında çalışan adım, o andan önceki son ölçüm kaydından alınır.
                    int activeStep = await context.ProcessDataLogs
                        .AsNoTracking()
                        .Where(p => p.BatchId == batchId && p.Timestamp <= revision.Timestamp)
                        .OrderByDescending(p => p.Timestamp)
                        .Select(p => (int?)p.ActiveStep)
                        .FirstOrDefaultAsync() ?? 0;

                    // Çizilmekte olan parçada geçmiş adımlar değişmez; PLC'de ise reçetenin tamamı değişmiştir.
                    current.Plan = MergePlan(current.Plan, edited, activeStep);
                    recipeInForce = edited;
                }
                else if ((revision.Reason == "GOTO" || revision.Reason == "SKIP") && heatingStart.HasValue)
                {
                    double jumpMinute = Math.Max(current.StartMinute, (revision.Timestamp - heatingStart.Value).TotalMinutes);
                    current.EndMinute = jumpMinute;

                    int targetStep = revision.StepNo ?? 0;
                    bool goesToHeating = revision.Reason == "GOTO" && RecipeProfile.IsHeatingStep(recipeInForce, targetStep);

                    if (goesToHeating)
                    {
                        // Gidilen adım, PLC'de yürürlükte olan (değiştirilmiş) reçetenin değerleriyle baştan planlanır.
                        segments.Add(new Segment
                        {
                            StartMinute = jumpMinute,
                            StartStep = targetStep,
                            StartElapsed = revision.StepMinute ?? 0,
                            Plan = recipeInForce
                        });
                    }
                    else
                    {
                        // Soğutmaya geçildi: ısıtma planı burada biter.
                        planEnded = true;
                    }
                }
            }

            return recipeInForce;
        }

        // Reçetenin ilk ısıtma adımının başından başlayan ilk eğri parçasını üretir.
        private static Segment CreateFirstSegment(Recipe recipe)
        {
            return new Segment
            {
                StartMinute = 0,
                StartStep = RecipeProfile.GetFirstHeatingStepNo(recipe) ?? 0,
                StartElapsed = 0,
                Plan = recipe
            };
        }

        // Değişikliği parçaya uygular: çalışan adımdan önceki adımlar eski plandan, çalışan adım ve sonrası yeni reçeteden alınır.
        private static Recipe MergePlan(Recipe current, Recipe edited, int activeStep)
        {
            var steps = new List<RecipeStep>();
            steps.AddRange(current.Steps.Where(s => s.StepNo < activeStep));
            steps.AddRange(edited.Steps.Where(s => s.StepNo >= activeStep));

            return new Recipe
            {
                Id = edited.Id,
                RecipeType = edited.RecipeType,
                RecipeName = edited.RecipeName,
                TotalStepCount = steps.Count,
                Steps = steps.OrderBy(s => s.StepNo).ToList()
            };
        }

        // Parçalardan eğrinin noktalarını üretir; ısıtma başlangıcı biliniyorsa noktalara gerçek saatleri de yazar.
        private static RecipeProfileDto Build(Recipe recipe, List<Segment> segments, float startTemp, DateTime? heatingStart)
        {
            var points = new List<ProfilePointDto>();

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var segmentPoints = RecipeProfile.CalculateFrom(segment.Plan, segment.StartStep, segment.StartElapsed, startTemp);

                foreach (var point in segmentPoints)
                {
                    point.Segment = i;
                    point.Minute += segment.StartMinute;
                }

                if (segment.EndMinute.HasValue)
                    segmentPoints = Truncate(segmentPoints, segment.EndMinute.Value, i);

                points.AddRange(segmentPoints);
            }

            if (heatingStart.HasValue)
            {
                foreach (var point in points)
                    point.Timestamp = heatingStart.Value.AddMinutes(point.Minute);
            }

            double totalMinutes = points.Count > 0 ? points.Max(p => p.Minute) : 0;

            return new RecipeProfileDto
            {
                RecipeId = recipe.Id,
                RecipeName = recipe.RecipeName,
                StartTemp = startTemp,
                TotalMinutes = totalMinutes,
                IsAnchored = heatingStart.HasValue,
                HeatingStart = heatingStart,
                PlannedEnd = heatingStart?.AddMinutes(totalMinutes),
                CoolingEndTemp = RecipeProfile.GetCoolingEndTemp(recipe),
                Points = points
            };
        }

        // Parçayı verilen dakikada keser: sonraki noktaları atar ve kesim anına aradaki sıcaklıkla bir nokta ekler.
        private static List<ProfilePointDto> Truncate(List<ProfilePointDto> points, double endMinute, int segmentIndex)
        {
            var result = points.Where(p => p.Minute <= endMinute).ToList();

            var next = points.FirstOrDefault(p => p.Minute > endMinute);
            var previous = result.LastOrDefault();

            if (next != null && previous != null && previous.Minute < endMinute)
            {
                double fraction = (endMinute - previous.Minute) / (next.Minute - previous.Minute);

                result.Add(new ProfilePointDto
                {
                    Segment = segmentIndex,
                    Minute = endMinute,
                    Temp = (float)(previous.Temp + (next.Temp - previous.Temp) * fraction),
                    StepNo = next.StepNo
                });
            }

            return result;
        }
    }
}