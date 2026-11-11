using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class ReportManager : IReportService
    {
        // Şarjın özet bilgilerini ve pota sıcaklığı istatistiklerini içeren raporu üretir.
        public async Task<IDataResult<byte[]>> GenerateBatchPdfReportAsync(int batchId)
        {
            await using var context = new NorthwindContext();

            var batch = await context.Batches
                .Include(b => b.Recipe)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
                return new ErrorDataResult<byte[]>("Raporlanacak şarj/parti kaydı bulunamadı.");

            var logs = await context.ProcessDataLogs
                .AsNoTracking()
                .Where(p => p.BatchId == batchId)
                .OrderBy(p => p.Timestamp)
                .ToListAsync();

            float maxTemp = logs.Any() ? logs.Max(l => l.TempPota) : 0f;
            float avgTemp = logs.Any() ? logs.Average(l => l.TempPota) : 0f;

            var sb = new StringBuilder();
            sb.AppendLine("=================================================");
            sb.AppendLine("          ISIL İŞLEM ŞARJ VE PROSES RAPORU       ");
            sb.AppendLine("=================================================");
            sb.AppendLine($"Şarj Kodu      : {batch.BatchCode}");
            sb.AppendLine($"Reçete Adı     : {batch.Recipe?.RecipeName ?? "N/A"}");
            sb.AppendLine($"Başlangıç      : {batch.StartTime:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine($"Bitiş          : {(batch.EndTime.HasValue ? batch.EndTime.Value.ToString("dd.MM.yyyy HH:mm:ss") : "Devam Ediyor")}");
            sb.AppendLine($"Durum          : {batch.Status}");
            sb.AppendLine("-------------------------------------------------");
            sb.AppendLine($"Maksimum Pota Sıcaklığı : {maxTemp:F1} °C");
            sb.AppendLine($"Ortalama Pota Sıcaklığı : {avgTemp:F1} °C");
            sb.AppendLine($"Toplam Log Sayısı       : {logs.Count}");
            sb.AppendLine("=================================================");

            byte[] reportBytes = Encoding.UTF8.GetBytes(sb.ToString());
            return new SuccessDataResult<byte[]>(reportBytes, "Şarj raporu başarıyla üretildi.");
        }

        // Belirtilen tarih aralığındaki telemetri kayıtlarını CSV olarak dışa aktarır.
        public async Task<IDataResult<byte[]>> ExportTrendToCsvAsync(DateTime start, DateTime end, string[] tagNames)
        {
            await using var context = new NorthwindContext();

            var logs = await context.ProcessDataLogs
                .AsNoTracking()
                .Where(p => p.Timestamp >= start && p.Timestamp <= end)
                .OrderBy(p => p.Timestamp)
                .ToListAsync();

            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("Timestamp;SeqState;ActiveStep;TempPota;TempAlt;TempUst;TempPotaAlt;TempPotaUst;TempMalzeme;TempSet;Pressure;PressureSet;N2FlowAct;NH3FlowAct;H2Act;Kn;HeatOutAlt;HeatOutUst;FanSpeedAct");

            foreach (var item in logs)
            {
                csvBuilder.AppendLine(
                    $"{item.Timestamp:yyyy-MM-dd HH:mm:ss};{item.SeqState};{item.ActiveStep};" +
                    $"{item.TempPota};{item.TempAlt};{item.TempUst};{item.TempPotaAlt};{item.TempPotaUst};{item.TempMalzeme};{item.TempSet};" +
                    $"{item.Pressure};{item.PressureSet};{item.N2FlowAct};{item.NH3FlowAct};{item.H2Act};{item.Kn};" +
                    $"{item.HeatOutAlt};{item.HeatOutUst};{item.FanSpeedAct}");
            }

            byte[] csvBytes = Encoding.UTF8.GetBytes(csvBuilder.ToString());
            return new SuccessDataResult<byte[]>(csvBytes, "Trend CSV dışa aktarımı tamamlandı.");
        }
    }
}