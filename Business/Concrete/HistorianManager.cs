using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Entities.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Business.Concrete
{
    public class HistorianManager : IHistorianService
    {
        private const int MinPoints = 10;
        private const int MaxPoints = 5000;
        private const int MaxTagsPerQuery = 16;

        // Grafiği çizilebilecek ölçümler. Tag adı, ProcessDataLog sınıfındaki alan adıyla aynıdır.
        private static readonly List<TrendTagDto> TrendTags = new List<TrendTagDto>
        {
            new TrendTagDto { Tag = "TempPota",    Unit = "°C",     Description = "Pota sıcaklığı (kontrol)" },
            new TrendTagDto { Tag = "TempSet",     Unit = "°C",     Description = "Sıcaklık seti (rampa üzerindeki nokta)" },
            new TrendTagDto { Tag = "TempAlt",     Unit = "°C",     Description = "Alt cehennemlik sıcaklığı" },
            new TrendTagDto { Tag = "TempUst",     Unit = "°C",     Description = "Üst cehennemlik sıcaklığı" },
            new TrendTagDto { Tag = "TempPotaAlt", Unit = "°C",     Description = "Pota alt sıcaklığı" },
            new TrendTagDto { Tag = "TempPotaUst", Unit = "°C",     Description = "Pota üst sıcaklığı" },
            new TrendTagDto { Tag = "TempMalzeme", Unit = "°C",     Description = "Malzeme sıcaklığı" },
            new TrendTagDto { Tag = "Pressure",    Unit = "mbar",   Description = "Basınç" },
            new TrendTagDto { Tag = "PressureSet", Unit = "mbar",   Description = "Basınç seti" },
            new TrendTagDto { Tag = "N2FlowAct",   Unit = "L/saat", Description = "Azot debisi" },
            new TrendTagDto { Tag = "NH3FlowAct",  Unit = "L/saat", Description = "Amonyak debisi" },
            new TrendTagDto { Tag = "H2Act",       Unit = "%",      Description = "Hidrojen ölçümü" },
            new TrendTagDto { Tag = "Kn",          Unit = "",       Description = "Nitrasyon potansiyeli" },
            new TrendTagDto { Tag = "HeatOutAlt",  Unit = "%",      Description = "Alt ısıtma çıkışı" },
            new TrendTagDto { Tag = "HeatOutUst",  Unit = "%",      Description = "Üst ısıtma çıkışı" },
            new TrendTagDto { Tag = "FanSpeedAct", Unit = "%",      Description = "Fan hızı" }
        };

        // Telemetri kaydını, BatchId boşsa aktif şarjın kimliğiyle etiketleyip veritabanına ekler.
        public async Task<IResult> LogProcessDataAsync(ProcessDataLog dataLog)
        {
            await using var context = new NorthwindContext();

            if (dataLog.BatchId == null)
            {
                dataLog.BatchId = await GetActiveBatchIdAsync(context);
            }

            await context.ProcessDataLogs.AddAsync(dataLog);
            await context.SaveChangesAsync();
            return new SuccessResult("Telemetri verisi kaydedildi.");
        }

        // Çalışan (RUNNING) şarjın id'sini döner, çalışan şarj yoksa null döner.
        private static async Task<int?> GetActiveBatchIdAsync(NorthwindContext context)
        {
            return await context.Batches
                .AsNoTracking()
                .Where(b => b.Status == "RUNNING")
                .Select(b => (int?)b.Id)
                .FirstOrDefaultAsync();
        }

        // Tek ölçüm için aralıktaki veriyi satır atlayarak seyreltip döner (eski yöntem).
        public async Task<IDataResult<List<TrendPointDto>>> GetTrendDataAsync(string tagName, DateTime start, DateTime end, int targetPointCount = 500)
        {
            await using var context = new NorthwindContext();

            var rawLogs = await context.ProcessDataLogs
                .AsNoTracking()
                .Where(p => p.Timestamp >= start && p.Timestamp <= end)
                .OrderBy(p => p.Timestamp)
                .ToListAsync();

            if (!rawLogs.Any())
                return new SuccessDataResult<List<TrendPointDto>>(new List<TrendPointDto>());

            int totalCount = rawLogs.Count;
            int step = Math.Max(1, totalCount / targetPointCount);

            var trendPoints = new List<TrendPointDto>();
            for (int i = 0; i < totalCount; i += step)
            {
                var item = rawLogs[i];
                trendPoints.Add(new TrendPointDto
                {
                    Timestamp = item.Timestamp,
                    Value = ExtractValueByTag(item, tagName)
                });
            }

            return new SuccessDataResult<List<TrendPointDto>>(trendPoints);
        }

        // Belirtilen tarihten eski telemetri kayıtlarını siler.
        public async Task<IResult> CompressOldLogsAsync(DateTime olderThan)
        {
            await using var context = new NorthwindContext();

            var oldLogs = await context.ProcessDataLogs
                .Where(p => p.Timestamp < olderThan)
                .ToListAsync();

            context.ProcessDataLogs.RemoveRange(oldLogs);
            await context.SaveChangesAsync();

            return new SuccessResult($"{oldLogs.Count} adet eski telemetri kaydı temizlendi.");
        }

        // Grafiği çizilebilecek ölçümleri birim ve açıklamalarıyla listeler.
        public IDataResult<List<TrendTagDto>> GetTrendTags()
        {
            return new SuccessDataResult<List<TrendTagDto>>(TrendTags.ToList());
        }

        // Verilen aralık için istenen ölçümlerin trendini döner.
        public async Task<IDataResult<TrendDataDto>> GetTrendSeriesAsync(IEnumerable<string> tags, DateTime start, DateTime end, int targetPointCount = 1000)
        {
            if (start >= end)
                return new ErrorDataResult<TrendDataDto>("Başlangıç zamanı bitiş zamanından önce olmalı.");

            var resolved = ResolveTags(tags, out string? tagError);
            if (resolved == null)
                return new ErrorDataResult<TrendDataDto>(tagError ?? "Ölçüm listesi geçersiz.");

            try
            {
                var data = await QueryTrendAsync(resolved, start, end, targetPointCount, null);
                return new SuccessDataResult<TrendDataDto>(data);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<TrendDataDto>($"Trend verisi okunamadı: {ex.Message}");
            }
        }

        // Tek bir şarjın başlangıcından bitişine (sürüyorsa şu ana) kadar olan trendini döner.
        public async Task<IDataResult<TrendDataDto>> GetBatchTrendAsync(int batchId, IEnumerable<string> tags, int targetPointCount = 1000)
        {
            var resolved = ResolveTags(tags, out string? tagError);
            if (resolved == null)
                return new ErrorDataResult<TrendDataDto>(tagError ?? "Ölçüm listesi geçersiz.");

            try
            {
                Batch? batch;
                await using (var context = new NorthwindContext())
                {
                    batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId);
                }

                if (batch == null)
                    return new ErrorDataResult<TrendDataDto>("Şarj bulunamadı.");

                DateTime end = batch.EndTime ?? DateTime.Now;
                if (end <= batch.StartTime)
                    end = batch.StartTime.AddSeconds(1);

                var data = await QueryTrendAsync(resolved, batch.StartTime, end, targetPointCount, batchId);
                return new SuccessDataResult<TrendDataDto>(data);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<TrendDataDto>($"Şarj trendi okunamadı: {ex.Message}");
            }
        }

        // İstenen ölçüm adlarını tanımlı listeyle eşler; bilinmeyen ad varsa null döner ve hatayı bildirir.
        private static List<TrendTagDto>? ResolveTags(IEnumerable<string>? tags, out string? error)
        {
            error = null;

            var requested = (tags ?? Enumerable.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requested.Count == 0)
            {
                error = "En az bir ölçüm adı verilmeli.";
                return null;
            }

            if (requested.Count > MaxTagsPerQuery)
            {
                error = $"Tek sorguda en fazla {MaxTagsPerQuery} ölçüm istenebilir.";
                return null;
            }

            var resolved = new List<TrendTagDto>();
            var unknown = new List<string>();

            foreach (var name in requested)
            {
                var match = TrendTags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    unknown.Add(name);
                else
                    resolved.Add(match);
            }

            if (unknown.Count > 0)
            {
                error = $"Bilinmeyen ölçüm: {string.Join(", ", unknown)}";
                return null;
            }

            return resolved;
        }

        // Aralığı eşit zaman dilimlerine böler ve her dilim için ortalama, en düşük ve en yüksek değeri veritabanında hesaplatır.
        private static async Task<TrendDataDto> QueryTrendAsync(List<TrendTagDto> tags, DateTime start, DateTime end, int targetPointCount, int? batchId)
        {
            int points = Math.Clamp(targetPointCount, MinPoints, MaxPoints);
            double rangeSeconds = Math.Max(1, (end - start).TotalSeconds);
            int bucketSeconds = Math.Max(1, (int)Math.Ceiling(rangeSeconds / points));

            var result = new TrendDataDto
            {
                Start = start,
                End = end,
                BucketSeconds = bucketSeconds,
                Series = tags.Select(t => new TrendSeriesDto { Tag = t.Tag, Unit = t.Unit }).ToList()
            };

            await using var context = new NorthwindContext();

            // Tablo ve sütun adları kullanıcıdan değil, veritabanı modelinden alınır.
            var entityType = context.Model.FindEntityType(typeof(ProcessDataLog))!;
            string table = entityType.GetTableName()!;
            string timeColumn = entityType.FindProperty(nameof(ProcessDataLog.Timestamp))!.GetColumnName();
            string batchColumn = entityType.FindProperty(nameof(ProcessDataLog.BatchId))!.GetColumnName();

            var sql = new StringBuilder();
            sql.Append($"SELECT MIN(`{timeColumn}`) AS t");
            foreach (var tag in tags)
            {
                string column = entityType.FindProperty(tag.Tag)!.GetColumnName();
                sql.Append($", AVG(`{column}`), MIN(`{column}`), MAX(`{column}`)");
            }
            sql.Append($" FROM `{table}` WHERE `{timeColumn}` >= @start AND `{timeColumn}` <= @end");
            if (batchId.HasValue)
                sql.Append($" AND `{batchColumn}` = @batchId");
            sql.Append($" GROUP BY FLOOR(UNIX_TIMESTAMP(`{timeColumn}`) / @bucket) ORDER BY t");

            DbConnection connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql.ToString();
            AddParameter(command, "@start", start);
            AddParameter(command, "@end", end);
            AddParameter(command, "@bucket", bucketSeconds);
            if (batchId.HasValue)
                AddParameter(command, "@batchId", batchId.Value);

            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Timestamps.Add(reader.GetDateTime(0));

                for (int i = 0; i < result.Series.Count; i++)
                {
                    int column = 1 + i * 3;
                    result.Series[i].Values.Add(ToFloat(reader.GetValue(column)));
                    result.Series[i].Min.Add(ToFloat(reader.GetValue(column + 1)));
                    result.Series[i].Max.Add(ToFloat(reader.GetValue(column + 2)));
                }
            }

            return result;
        }

        // Sorguya adlandırılmış bir parametre ekler.
        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        // Veritabanından gelen sayısal değeri float'a çevirir; boşsa 0 döner.
        private static float ToFloat(object value)
        {
            return value == null || value == DBNull.Value ? 0f : Convert.ToSingle(value);
        }

        // Tag adına karşılık gelen sayısal değeri telemetri kaydından seçer.
        private static float ExtractValueByTag(ProcessDataLog log, string tagName) => tagName switch
        {
            "TempPota" => log.TempPota,
            "TempAlt" => log.TempAlt,
            "TempUst" => log.TempUst,
            "TempPotaAlt" => log.TempPotaAlt,
            "TempPotaUst" => log.TempPotaUst,
            "TempMalzeme" => log.TempMalzeme,
            "TempSet" => log.TempSet,
            "Pressure" => log.Pressure,
            "PressureSet" => log.PressureSet,
            "N2FlowAct" => log.N2FlowAct,
            "NH3FlowAct" => log.NH3FlowAct,
            "H2Act" => log.H2Act,
            "Kn" => log.Kn,
            "HeatOutAlt" => log.HeatOutAlt,
            "HeatOutUst" => log.HeatOutUst,
            "FanSpeedAct" => log.FanSpeedAct,
            _ => 0f
        };
    }
}