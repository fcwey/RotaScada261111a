using Core.Utilities.Results;
using Entities.Concrete;
using Entities.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Business.Abstract
{
    public interface IHistorianService
    {
        // Anlık telemetri nesnesini veritabanına ekler
        Task<IResult> LogProcessDataAsync(ProcessDataLog dataLog);

        // Tek ölçüm için seyreltilmiş noktaları döner (eski yöntem; yeni ekranlar GetTrendSeriesAsync kullanmalı)
        Task<IDataResult<List<TrendPointDto>>> GetTrendDataAsync(string tagName, DateTime start, DateTime end, int targetPointCount = 500);

        // Eski log verilerini temizler
        Task<IResult> CompressOldLogsAsync(DateTime olderThan);

        // Grafiği çizilebilecek ölçümleri listeler
        IDataResult<List<TrendTagDto>> GetTrendTags();

        // Verilen aralık için birden fazla ölçümün trendini ortalama, en düşük ve en yüksek değerlerle döner
        Task<IDataResult<TrendDataDto>> GetTrendSeriesAsync(IEnumerable<string> tags, DateTime start, DateTime end, int targetPointCount = 1000);

        // Tek bir şarjın başından sonuna trendini döner
        Task<IDataResult<TrendDataDto>> GetBatchTrendAsync(int batchId, IEnumerable<string> tags, int targetPointCount = 1000);
    }
}