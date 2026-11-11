using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using System;
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