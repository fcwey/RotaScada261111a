using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Entities.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Business.Concrete
{
    public class HistorianManager : IHistorianService
    {
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

        // Trend grafiği için belirtilen aralıktaki veriyi seyrekleştirerek (downsampling) döner.
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