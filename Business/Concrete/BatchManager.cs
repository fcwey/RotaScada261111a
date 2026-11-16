using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Abstract;
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
        private const int RetrySeconds = 30;      // Hatadan sonra yeniden deneme aralığı

        private readonly IS7DriverDal? _s7DriverDal;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private bool _initialized;                // İlk durum işlendi mi
        private short _lastSeqState;              // Son işlenen sekans durumu
        private DateTime _retryAfter = DateTime.MinValue;

        // PLC bağlantısı olmadan oluşturur; yalnızca veritabanı sorguları ve plan olayları kullanılabilir.
        public BatchManager()
        {
        }

        // PLC sürücüsüyle oluşturur; şarj bitişini ve adım özetlerini PLC'den okuyabilir.
        public BatchManager(IS7DriverDal s7DriverDal)
        {
            _s7DriverDal = s7DriverDal;
        }

        // Sekans durumu değiştiğinde DB201'i taze okur; program bittiyse bitiş bilgisini kaydeder, fırın beklemeye döndüyse şarjı kapatır.
        public async Task<IResult> TrackSequenceAsync(short seqState)
        {
            await _lock.WaitAsync();
            try
            {
                bool isFirst = !_initialized;
                if (!isFirst && seqState == _lastSeqState)
                    return new SuccessResult();

                // Hata alındıysa canlı veri döngüsü tıkanmasın diye bir süre beklenir.
                if (DateTime.Now < _retryAfter)
                    return new SuccessResult();

                if (_s7DriverDal == null)
                    return new ErrorResult("Şarj takibi için PLC sürücüsü tanımlı değil.");

                // Karar, tetikleyen değere değil o anda PLC'den okunan duruma göre verilir.
                var status = ScadaStatus.Parse(
                    await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbStatus, 0, ScadaLayout.StatusSize));

                var messages = new List<string>();

                // 1. Program bittiyse (EndReason doldu) bitiş bilgisi ve adım özetleri kaydedilir; şarj açık kalır.
                if (status.EndReason != 0)
                {
                    string? endMessage = await RecordProgramEndAsync(status);
                    if (endMessage != null)
                        messages.Add(endMessage);
                }

                // 2. Fırın beklemeye döndüyse son toplamlar yazılır ve şarj kapatılır.
                if (status.SeqState == SeqIdle)
                {
                    string? closeMessage = await CloseRunningBatchesAsync(status);
                    if (closeMessage != null)
                        messages.Add(closeMessage);
                }

                // Durum yalnızca işlem başarılıysa saklanır; hata olursa aynı geçiş yeniden denenir.
                _initialized = true;
                _lastSeqState = seqState;

                return new SuccessResult(string.Join(" ", messages));
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

        // Tek şarjın özetini, plan geçmişini, adım özetlerini ve şarj sırasında oluşan alarmları getirir.
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

                var steps = await context.BatchStepSummaries
                    .AsNoTracking()
                    .Where(s => s.BatchId == batchId)
                    .OrderBy(s => s.StepNo)
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
                        Recipe = RecipeSnapshot.Deserialize(r.RecipeJson),
                        StepNo = r.StepNo,
                        StepMinute = r.StepMinute
                    }).ToList(),
                    Steps = steps,
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

        // Çalışan şarjın plan geçmişine adıma atlama (GOTO) ya da soğutmaya geçiş (SKIP) olayını ekler.
        public async Task<IResult> RecordPlanEventAsync(string reason, int stepNo, int stepMinute, int userId)
        {
            if (reason != "GOTO" && reason != "SKIP")
                return new ErrorResult($"Bilinmeyen plan olayı: {reason}");

            try
            {
                await using var context = new NorthwindContext();

                var batch = await context.Batches
                    .Where(b => b.Status == "RUNNING")
                    .OrderByDescending(b => b.StartTime)
                    .FirstOrDefaultAsync();

                if (batch == null)
                    return new ErrorResult("Çalışan şarj yok; plan olayı kaydedilmedi.");

                await context.BatchRecipeRevisions.AddAsync(new BatchRecipeRevision
                {
                    BatchId = batch.Id,
                    Timestamp = DateTime.Now,
                    UserId = userId,
                    Reason = reason,
                    RecipeJson = string.Empty,
                    StepNo = reason == "GOTO" ? stepNo : null,
                    StepMinute = reason == "GOTO" ? stepMinute : null
                });
                await context.SaveChangesAsync();

                return new SuccessResult("Plan olayı şarj kaydına işlendi.");
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Plan olayı kaydedilemedi: {ex.Message}");
            }
        }

        // PLC'deki şarj numarası açık şarjla aynıysa bitiş sebebini, enerjiyi ve adım özetlerini bir kez kaydeder.
        private async Task<string?> RecordProgramEndAsync(ScadaStatus status)
        {
            await using var context = new NorthwindContext();

            var batch = await context.Batches
                .FirstOrDefaultAsync(b => b.Status == "RUNNING" && b.Id == status.BatchId);

            // Açık şarj bu program değilse ya da bitişi zaten kaydedildiyse yapılacak bir şey yoktur.
            if (batch == null || batch.ProgramEndTime != null)
                return null;

            batch.EndReason = status.EndReason;
            batch.ProgramEndTime = DateTime.Now;
            CopyTotals(batch, status);
            await context.SaveChangesAsync();

            int stepCount = await SaveStepSummariesAsync(context, batch.Id);

            string stepText = stepCount >= 0
                ? $"{stepCount} adım özeti alındı."
                : "Adım özetleri alınamadı (DB205 okunamadı ya da başka şarja ait).";

            return $"Şarj {batch.BatchCode}: program bitti (bitiş sebebi {status.EndReason}). {stepText}";
        }

        // DB205'i okur; tablo bu şarja aitse adım özetlerini kaydeder. Kaydedilen satır sayısını, alınamadıysa -1 döner.
        private async Task<int> SaveStepSummariesAsync(NorthwindContext context, int batchId)
        {
            StepLogData data;
            try
            {
                data = StepLogCodec.Decode(
                    await _s7DriverDal!.ReadBytesAsync(ScadaLayout.DbStepLog, 0, ScadaLayout.StepLogSize));
            }
            catch
            {
                // Adım özeti okunamasa da bitiş bilgisi kaydedilmiş kalır.
                return -1;
            }

            if (data.LayoutVersion != ScadaLayout.StepLogVersion || data.BatchId != batchId)
                return -1;

            // Aynı şarj için daha önce alınmış özet varsa yenisiyle değiştirilir.
            await context.BatchStepSummaries.Where(s => s.BatchId == batchId).ExecuteDeleteAsync();

            foreach (var row in data.Rows)
            {
                row.BatchId = batchId;
                await context.BatchStepSummaries.AddAsync(row);
            }
            await context.SaveChangesAsync();

            return data.Rows.Count;
        }

        // Açık şarjları son toplamlarla ve PLC'den alınan bitiş sebebine göre kapatır.
        private static async Task<string?> CloseRunningBatchesAsync(ScadaStatus status)
        {
            await using var context = new NorthwindContext();

            var running = await context.Batches.Where(b => b.Status == "RUNNING").ToListAsync();
            if (running.Count == 0)
                return null;

            DateTime now = DateTime.Now;
            var closed = new List<string>();

            foreach (var batch in running)
            {
                // PLC toplamları bir sonraki Start'a kadar durduğu için beklemede de bu şarja aittir.
                if (batch.Id == status.BatchId)
                    CopyTotals(batch, status);

                switch (batch.EndReason)
                {
                    case 1:
                        batch.Status = "COMPLETED";
                        batch.CompletionNote = "Program tamamlandı.";
                        break;
                    case 2:
                        batch.Status = "ABORTED";
                        batch.CompletionNote = "Stop komutuyla bitirildi.";
                        break;
                    case 3:
                        batch.Status = "ABORTED";
                        batch.CompletionNote = "Güvenli durdurma alarmıyla bitti.";
                        break;
                    default:
                        batch.Status = "UNKNOWN";
                        batch.CompletionNote = "Bitiş bilgisi PLC'den alınamadı (şarj numarası PLC'dekiyle uyuşmuyor).";
                        break;
                }

                batch.EndTime = now;
                closed.Add($"{batch.BatchCode} ({batch.Status})");
            }

            await context.SaveChangesAsync();
            return $"Şarj kaydı kapatıldı: {string.Join(", ", closed)}";
        }

        // PLC'deki şarj toplamlarını ve enerjiyi şarj kaydına kopyalar.
        private static void CopyTotals(Batch batch, ScadaStatus status)
        {
            batch.EnergyKwh = status.BatchEnergyKwh;
            batch.N2TotalL = status.N2Total;
            batch.NH3TotalL = status.NH3Total;
            batch.CO2TotalL = status.CO2Total;
            batch.WaterTotalL = status.WaterTotal;
            batch.FillN2TotalL = status.FillN2Total;
            batch.EmgN2TotalL = status.EmgN2Total;
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
                    CompletionNote = b.CompletionNote,
                    EndReason = b.EndReason,
                    ProgramEndTime = b.ProgramEndTime,
                    EnergyKwh = b.EnergyKwh,
                    N2TotalL = b.N2TotalL,
                    NH3TotalL = b.NH3TotalL,
                    CO2TotalL = b.CO2TotalL,
                    WaterTotalL = b.WaterTotalL,
                    FillN2TotalL = b.FillN2TotalL,
                    EmgN2TotalL = b.EmgN2TotalL
                };
            }).ToList();
        }
    }
}