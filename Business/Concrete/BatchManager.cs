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
    public class BatchManager : IBatchService
    {
        private const short SeqIdle = 0;          // Bekleme
        private const short SeqFinished = 60;     // Program bitti
        private const short SeqSafeStop = 80;     // Güvenli durdurma
        private const int RetrySeconds = 30;      // Veritabanı hatasından sonra yeniden deneme aralığı

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private bool _initialized;                // İlk durum işlendi mi
        private short _lastSeqState;              // Son işlenen sekans durumu
        private bool _sawSafeStop;                // Bu programda güvenli durdurma görüldü mü
        private DateTime _retryAfter = DateTime.MinValue;

        // Sekans durumu değiştiğinde açık şarjı uygun sonuçla kapatır; değişiklik yoksa veritabanına gitmez.
        public async Task<IResult> TrackSequenceAsync(short seqState)
        {
            await _lock.WaitAsync();
            try
            {
                if (seqState == SeqSafeStop)
                    _sawSafeStop = true;

                bool isFirst = !_initialized;
                if (!isFirst && seqState == _lastSeqState)
                    return new SuccessResult();

                // Veritabanı hata verdiyse canlı veri döngüsü tıkanmasın diye bir süre beklenir.
                if (DateTime.Now < _retryAfter)
                    return new SuccessResult();

                string? status = null;
                string? note = null;

                if (seqState == SeqFinished)
                {
                    status = "COMPLETED";
                    note = "Program tamamlandı.";
                }
                else if (seqState == SeqIdle)
                {
                    if (isFirst)
                    {
                        status = "UNKNOWN";
                        note = "SCADA kapalıyken program sona erdi; sonuç bilinmiyor.";
                    }
                    else
                    {
                        status = "ABORTED";
                        note = _sawSafeStop
                            ? "Güvenli durdurma (ciddi alarm) ile sonlandı."
                            : "Program tamamlanmadan durduruldu.";
                    }
                }

                int closedCount = 0;
                if (status != null)
                {
                    closedCount = await CloseRunningBatchesAsync(status, note!);
                    _sawSafeStop = false;
                }

                // Durum yalnızca kayıt başarılıysa saklanır; hata olursa aynı geçiş yeniden denenir.
                _initialized = true;
                _lastSeqState = seqState;

                return new SuccessResult(closedCount > 0 ? $"Şarj kaydı kapatıldı: {status}" : string.Empty);
            }
            catch (Exception ex)
            {
                _retryAfter = DateTime.Now.AddSeconds(RetrySeconds);
                return new ErrorResult($"Şarj kaydı güncellenemedi: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        // Başlangıç zamanı verilen aralıkta olan şarjları yeniden eskiye listeler.
        public async Task<IDataResult<List<BatchListDto>>> GetBatchesAsync(DateTime start, DateTime end)
        {
            try
            {
                await using var context = new NorthwindContext();

                var batches = await context.Batches
                    .AsNoTracking()
                    .Where(b => b.StartTime >= start && b.StartTime <= end)
                    .OrderByDescending(b => b.StartTime)
                    .ToListAsync();

                var list = await BuildListAsync(context, batches);
                return new SuccessDataResult<List<BatchListDto>>(list);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<List<BatchListDto>>($"Şarjlar okunamadı: {ex.Message}");
            }
        }

        // Tek şarjın özetini, reçete geçmişini ve şarj sırasında oluşan alarmları getirir.
        public async Task<IDataResult<BatchDetailDto>> GetBatchDetailAsync(int batchId)
        {
            try
            {
                await using var context = new NorthwindContext();

                var batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId);
                if (batch == null)
                    return new ErrorDataResult<BatchDetailDto>("Şarj bulunamadı.");

                var revisions = await context.BatchRecipeRevisions
                    .AsNoTracking()
                    .Where(r => r.BatchId == batchId)
                    .OrderBy(r => r.Timestamp)
                    .ThenBy(r => r.Id)
                    .ToListAsync();

                var userIds = revisions.Select(r => r.UserId).Distinct().ToList();
                var users = await context.Users
                    .AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Email);

                // Şarj hâlâ sürüyorsa alarmlar şu ana kadar alınır.
                DateTime alarmEnd = batch.EndTime ?? DateTime.Now;
                var alarms = await context.Alarms
                    .AsNoTracking()
                    .Where(a => a.TriggeredAt >= batch.StartTime && a.TriggeredAt <= alarmEnd)
                    .OrderBy(a => a.TriggeredAt)
                    .ToListAsync();

                var detail = new BatchDetailDto
                {
                    Batch = (await BuildListAsync(context, new List<Batch> { batch })).Single(),
                    Revisions = revisions.Select(r => new BatchRevisionDto
                    {
                        Timestamp = r.Timestamp,
                        UserId = r.UserId,
                        UserEmail = users.TryGetValue(r.UserId, out var email) ? email : string.Empty,
                        Reason = r.Reason,
                        Recipe = RecipeSnapshot.Deserialize(r.RecipeJson)
                    }).ToList(),
                    Alarms = alarms.Select(a => new AlarmHistoryDto
                    {
                        Id = a.Id,
                        TagName = a.TagName,
                        Description = a.Description,
                        PriorityLevel = a.PriorityLevel,
                        TriggeredAt = a.TriggeredAt,
                        AcknowledgedAt = a.AcknowledgedAt,
                        ResolvedAt = a.ResolvedAt,
                        UserNote = a.UserNote
                    }).ToList()
                };

                return new SuccessDataResult<BatchDetailDto>(detail);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<BatchDetailDto>($"Şarj ayrıntısı okunamadı: {ex.Message}");
            }
        }

        // Şarj kayıtlarını liste satırlarına çevirir; reçete adını şarjın başlangıç kopyasından, operatörü kullanıcı tablosundan alır.
        private static async Task<List<BatchListDto>> BuildListAsync(NorthwindContext context, List<Batch> batches)
        {
            var batchIds = batches.Select(b => b.Id).ToList();
            var operatorIds = batches.Select(b => b.OperatorUserId).Distinct().ToList();
            var recipeIds = batches.Select(b => b.RecipeId).Distinct().ToList();

            var startRevisions = await context.BatchRecipeRevisions
                .AsNoTracking()
                .Where(r => batchIds.Contains(r.BatchId) && r.Reason == "START")
                .ToListAsync();

            var operators = await context.Users
                .AsNoTracking()
                .Where(u => operatorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email);

            // Kopyası olmayan eski şarjlar için reçetenin şimdiki adı kullanılır.
            var currentNames = await context.Recipes
                .AsNoTracking()
                .Where(r => recipeIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.RecipeName);

            return batches.Select(b =>
            {
                var startRevision = startRevisions.FirstOrDefault(r => r.BatchId == b.Id);
                string? snapshotName = RecipeSnapshot.Deserialize(startRevision?.RecipeJson)?.RecipeName;

                return new BatchListDto
                {
                    Id = b.Id,
                    BatchCode = b.BatchCode,
                    RecipeId = b.RecipeId,
                    RecipeName = snapshotName ?? (currentNames.TryGetValue(b.RecipeId, out var name) ? name : string.Empty),
                    OperatorUserId = b.OperatorUserId,
                    OperatorEmail = operators.TryGetValue(b.OperatorUserId, out var email) ? email : string.Empty,
                    StartTime = b.StartTime,
                    EndTime = b.EndTime,
                    Status = b.Status,
                    CompletionNote = b.CompletionNote
                };
            }).ToList();
        }

        // Açık (RUNNING) şarj kayıtlarını verilen durum ve notla kapatır; kapatılan kayıt sayısını döner.
        private static async Task<int> CloseRunningBatchesAsync(string status, string note)
        {
            await using var context = new NorthwindContext();

            var running = await context.Batches.Where(b => b.Status == "RUNNING").ToListAsync();
            if (running.Count == 0)
                return 0;

            DateTime now = DateTime.Now;
            foreach (var batch in running)
            {
                batch.Status = status;
                batch.EndTime = now;
                batch.CompletionNote = note;
            }

            await context.SaveChangesAsync();
            return running.Count;
        }
    }
}