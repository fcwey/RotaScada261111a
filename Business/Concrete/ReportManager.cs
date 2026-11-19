using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Structures;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class ReportManager : IReportService
    {
        private const string Line = "========================================================================================";
        private const string Thin = "----------------------------------------------------------------------------------------";

        // Şarjın özetini, ağırlığını, toplamlarını, adım özetlerini, müdahalelerini, alarmlarını ve notlarını içeren metin raporunu üretir.
        public async Task<IDataResult<byte[]>> GenerateBatchPdfReportAsync(int batchId)
        {
            try
            {
                await using var context = new NorthwindContext();

                var batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId);
                if (batch == null)
                    return new ErrorDataResult<byte[]>("Raporlanacak şarj kaydı bulunamadı.");

                var revisions = await context.BatchRecipeRevisions
                    .AsNoTracking()
                    .Where(r => r.BatchId == batchId)
                    .OrderBy(r => r.Id)
                    .ToListAsync();

                var steps = await context.BatchStepSummaries
                    .AsNoTracking()
                    .Where(s => s.BatchId == batchId)
                    .OrderBy(s => s.StepNo)
                    .ToListAsync();

                var notes = await context.BatchNotes
                    .AsNoTracking()
                    .Where(n => n.BatchId == batchId)
                    .OrderBy(n => n.Timestamp)
                    .ToListAsync();

                DateTime periodEnd = batch.EndTime ?? DateTime.Now;
                var alarms = await context.Alarms
                    .AsNoTracking()
                    .Where(a => a.TriggeredAt >= batch.StartTime && a.TriggeredAt <= periodEnd)
                    .OrderBy(a => a.TriggeredAt)
                    .ToListAsync();

                var userIds = revisions.Select(r => r.UserId)
                    .Concat(notes.Select(n => n.UserId))
                    .Append(batch.OperatorUserId)
                    .Distinct()
                    .ToList();

                var users = await context.Users
                    .AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Email);

                int logCount = await context.ProcessDataLogs.CountAsync(p => p.BatchId == batchId);
                float maxTemp = logCount > 0
                    ? await context.ProcessDataLogs.Where(p => p.BatchId == batchId).MaxAsync(p => p.TempPota)
                    : 0f;

                // Reçete adı şarjın başlangıç kopyasından alınır; reçete sonradan değişmiş olabilir.
                var startRecipe = RecipeSnapshot.Deserialize(revisions.FirstOrDefault(r => r.Reason == "START")?.RecipeJson);
                string recipeName = startRecipe?.RecipeName ?? "(başlangıç kopyası yok)";

                var sb = new StringBuilder();
                sb.AppendLine(Line);
                sb.AppendLine("  ISIL İŞLEM ŞARJ RAPORU");
                sb.AppendLine(Line);
                sb.AppendLine($"Şarj kodu        : {batch.BatchCode}  (şarj no {batch.Id})");
                sb.AppendLine($"Reçete           : {recipeName}  ({DescribeRecipeType(startRecipe?.RecipeType)})");
                sb.AppendLine($"Şarj ağırlığı    : {FormatLoad(batch.LoadKg)}");
                sb.AppendLine($"Operatör         : {LookupUser(users, batch.OperatorUserId)}");
                sb.AppendLine($"Başlangıç        : {batch.StartTime:dd.MM.yyyy HH:mm:ss}");
                sb.AppendLine($"Program bitişi   : {FormatTime(batch.ProgramEndTime)}");
                sb.AppendLine($"Şarj kapanışı    : {FormatTime(batch.EndTime)}");
                sb.AppendLine($"Durum            : {batch.Status}");
                sb.AppendLine($"Bitiş sebebi     : {DescribeEndReason(batch.EndReason)}");
                sb.AppendLine($"Not              : {batch.CompletionNote}");
                sb.AppendLine();

                sb.AppendLine("ŞARJ TOPLAMLARI");
                sb.AppendLine(Thin);
                sb.AppendLine($"Enerji                    : {F(batch.EnergyKwh)} kWh");

                if (batch.LoadKg.HasValue && batch.LoadKg.Value > 0 && batch.EnergyKwh > 0)
                    sb.AppendLine($"Kilogram başına enerji    : {(batch.EnergyKwh / batch.LoadKg.Value).ToString("0.000", CultureInfo.InvariantCulture)} kWh/kg");

                sb.AppendLine($"Azot (MFC)                : {F(batch.N2TotalL)} L");
                sb.AppendLine($"Amonyak                   : {F(batch.NH3TotalL)} L");
                sb.AppendLine($"CO2 (hesap)               : {F(batch.CO2TotalL)} L");
                sb.AppendLine($"Su (hesap)                : {F(batch.WaterTotalL)} L");
                sb.AppendLine($"Vakum dolum azotu (hesap) : {F(batch.FillN2TotalL)} L");
                sb.AppendLine($"Acil azot (hesap)         : {F(batch.EmgN2TotalL)} L");
                sb.AppendLine($"En yüksek pota sıcaklığı  : {F(maxTemp)} °C  ({logCount} ölçüm kaydı)");
                sb.AppendLine();

                AppendSteps(sb, steps);
                AppendPlanEvents(sb, revisions, users);
                AppendAlarms(sb, alarms);
                AppendNotes(sb, notes, users);

                sb.AppendLine(Line);
                sb.AppendLine($"Rapor tarihi: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

                return new SuccessDataResult<byte[]>(Encoding.UTF8.GetBytes(sb.ToString()), "Şarj raporu üretildi.");
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<byte[]>($"Şarj raporu üretilemedi: {ex.Message}");
            }
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
            csvBuilder.AppendLine("Timestamp;BatchId;SeqState;ActiveStep;TempPota;TempAlt;TempUst;TempPotaAlt;TempPotaUst;TempMalzeme;TempSet;Pressure;PressureSet;N2FlowAct;NH3FlowAct;H2Act;Kn;KnApprox;HeatOutAlt;HeatOutUst;FanSpeedAct");

            foreach (var item in logs)
            {
                csvBuilder.AppendLine(
                    $"{item.Timestamp:yyyy-MM-dd HH:mm:ss};{item.BatchId};{item.SeqState};{item.ActiveStep};" +
                    $"{item.TempPota};{item.TempAlt};{item.TempUst};{item.TempPotaAlt};{item.TempPotaUst};{item.TempMalzeme};{item.TempSet};" +
                    $"{item.Pressure};{item.PressureSet};{item.N2FlowAct};{item.NH3FlowAct};{item.H2Act};{item.Kn};{(item.KnApprox ? 1 : 0)};" +
                    $"{item.HeatOutAlt};{item.HeatOutUst};{item.FanSpeedAct}");
            }

            byte[] csvBytes = Encoding.UTF8.GetBytes(csvBuilder.ToString());
            return new SuccessDataResult<byte[]>(csvBytes, "Trend CSV dışa aktarımı tamamlandı.");
        }

        // PLC'nin adım bazında gerçekleşen değerlerini tablo olarak rapora ekler.
        private static void AppendSteps(StringBuilder sb, List<BatchStepSummary> steps)
        {
            sb.AppendLine("ADIM ÖZETLERİ (PLC'de gerçekleşen değerler)");
            sb.AppendLine(Thin);

            if (steps.Count == 0)
            {
                sb.AppendLine("Adım özeti yok (program sürüyor ya da özet PLC'den alınamadı).");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("Adım Tip      Süre  Gsoak  Hold   T.ort  T.min  T.max   Kn.ort  H2.ort    N2(L)   NH3(L)  CO2(L)    kWh  Not");

            foreach (var s in steps)
            {
                var notes = new List<string>();
                if ((s.Flags & 1) == 0) notes.Add("tamamlanmadı");
                if ((s.Flags & 2) != 0) notes.Add("CO2 verildi, Kn yaklaşık");
                if ((s.Flags & 4) != 0) notes.Add($"H2 otomatik, bantta {s.H2InBandMin} dk");

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4} {1,-8} {2,4} {3,6} {4,5} {5,7:0.0} {6,6:0.0} {7,6:0.0} {8,8:0.00} {9,7:0.0} {10,8:0} {11,8:0} {12,7:0.0} {13,6:0.0}  {14}",
                    s.StepNo, DescribeStepType(s.StepType), s.DurationMin, s.GsoakWaitMin, s.HoldMin,
                    s.TempAvg, s.TempMin, s.TempMax, s.KnAvg, s.H2Avg, s.N2L, s.NH3L, s.CO2L, s.EnergyKwh,
                    string.Join("; ", notes)));
            }

            sb.AppendLine("Süreler dakikadır. Yıkama ve korumalı beklemedeki tüketim adımlara yazılmaz, şarj toplamında görünür.");
            sb.AppendLine();
        }

        // Program sırasındaki reçete değişikliklerini ve adım atlamalarını rapora ekler.
        private static void AppendPlanEvents(StringBuilder sb, List<BatchRecipeRevision> revisions, Dictionary<int, string> users)
        {
            var events = revisions.Where(r => r.Reason != "START").ToList();

            sb.AppendLine("PROGRAMA MÜDAHALELER");
            sb.AppendLine(Thin);

            if (events.Count == 0)
            {
                sb.AppendLine("Program başladığı reçeteyle, müdahale olmadan yürüdü.");
                sb.AppendLine();
                return;
            }

            foreach (var e in events)
            {
                string what = e.Reason switch
                {
                    "EDIT" => "Reçete çalışırken değiştirildi",
                    "GOTO" => $"Adım {e.StepNo}, {e.StepMinute}. dakikaya atlandı",
                    "SKIP" => "Soğutmaya geçildi",
                    _ => e.Reason
                };

                sb.AppendLine($"{e.Timestamp:dd.MM.yyyy HH:mm:ss}  {what}  ({LookupUser(users, e.UserId)})");
            }

            sb.AppendLine();
        }

        // Şarj süresince oluşan alarmları rapora ekler.
        private static void AppendAlarms(StringBuilder sb, List<Alarm> alarms)
        {
            sb.AppendLine("ŞARJ SIRASINDAKİ ALARMLAR");
            sb.AppendLine(Thin);

            if (alarms.Count == 0)
            {
                sb.AppendLine("Alarm oluşmadı.");
                sb.AppendLine();
                return;
            }

            foreach (var a in alarms)
            {
                string resolved = a.ResolvedAt.HasValue ? $"gitti {a.ResolvedAt:HH:mm:ss}" : "sürüyor";
                sb.AppendLine($"{a.TriggeredAt:dd.MM.yyyy HH:mm:ss}  {a.TagName}  {a.Description}  ({resolved})");
            }

            sb.AppendLine();
        }

        // Şarja iliştirilen notları (başlangıç notu, ara notlar, sonuçlar) eskiden yeniye rapora ekler.
        private static void AppendNotes(StringBuilder sb, List<BatchNote> notes, Dictionary<int, string> users)
        {
            sb.AppendLine("NOTLAR");
            sb.AppendLine(Thin);

            if (notes.Count == 0)
            {
                sb.AppendLine("Not eklenmemiş.");
                sb.AppendLine();
                return;
            }

            foreach (var note in notes)
            {
                sb.AppendLine($"{note.Timestamp:dd.MM.yyyy HH:mm}  ({LookupUser(users, note.UserId)})");

                // Çok satırlı notun her satırı girintili yazılır.
                foreach (string line in note.NoteText.Replace("\r", string.Empty).Split('\n'))
                    sb.AppendLine($"    {line}");

                sb.AppendLine();
            }
        }

        // Kullanıcı numarasını kullanıcı adına çevirir; bulunamazsa numarayı yazar.
        private static string LookupUser(Dictionary<int, string> users, int userId)
        {
            return users.TryGetValue(userId, out var name) ? name : $"kullanıcı {userId}";
        }

        // Şarj ağırlığını yazar; girilmemişse bunu belirtir.
        private static string FormatLoad(float? loadKg)
        {
            return loadKg.HasValue ? $"{loadKg.Value.ToString("0.#", CultureInfo.InvariantCulture)} kg" : "girilmemiş";
        }

        // Zamanı rapor biçiminde yazar; boşsa çizgi koyar.
        private static string FormatTime(DateTime? time)
        {
            return time.HasValue ? time.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-";
        }

        // Sayıyı bir ondalıkla, nokta ayırıcıyla yazar.
        private static string F(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        // PLC'nin bitiş sebebi kodunu metne çevirir.
        private static string DescribeEndReason(int? endReason)
        {
            return endReason switch
            {
                1 => "Normal tamamlandı",
                2 => "Stop komutuyla bitirildi",
                3 => "Güvenli durdurma alarmıyla bitti",
                null => "Yok (program sürüyor ya da bitiş bilgisi alınamadı)",
                _ => $"Bilinmeyen ({endReason})"
            };
        }

        // Reçete tipini metne çevirir.
        private static string DescribeRecipeType(int? recipeType)
        {
            return recipeType switch
            {
                1 => "meneviş",
                2 => "nitrasyon",
                3 => "oksidasyon",
                _ => "tip bilinmiyor"
            };
        }

        // Adım tipini metne çevirir.
        private static string DescribeStepType(int stepType)
        {
            return stepType switch
            {
                1 => "vakum",
                2 => "ısıtma",
                3 => "soğutma",
                _ => "?"
            };
        }
    }
}