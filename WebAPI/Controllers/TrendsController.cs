using Business.Abstract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class TrendsController : ControllerBase
    {
        private const string DefaultTags = "TempPota,TempSet";
        private const int DefaultRangeHours = 24;

        private readonly IHistorianService _historianService;

        // Historian servis bağımlılığını alır.
        public TrendsController(IHistorianService historianService)
        {
            _historianService = historianService;
        }

        // Grafiği çizilebilecek ölçümleri birim ve açıklamalarıyla listeler.
        [HttpGet("tags")]
        public IActionResult GetTags()
        {
            return Ok(_historianService.GetTrendTags());
        }

        // Verilen aralığın trendini döner; aralık verilmezse son 24 saat, ölçüm verilmezse pota sıcaklığı ve seti gelir.
        [HttpGet]
        public async Task<IActionResult> GetTrend(
            [FromQuery] string? tags,
            [FromQuery] DateTime? start,
            [FromQuery] DateTime? end,
            [FromQuery] int points = 1000)
        {
            DateTime rangeEnd = end ?? DateTime.Now;
            DateTime rangeStart = start ?? rangeEnd.AddHours(-DefaultRangeHours);

            var result = await _historianService.GetTrendSeriesAsync(SplitTags(tags), rangeStart, rangeEnd, points);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Tek bir şarjın başından sonuna trendini döner.
        [HttpGet("batch/{batchId:int}")]
        public async Task<IActionResult> GetBatchTrend(int batchId, [FromQuery] string? tags, [FromQuery] int points = 1000)
        {
            var result = await _historianService.GetBatchTrendAsync(batchId, SplitTags(tags), points);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Virgülle ayrılmış ölçüm adlarını listeye çevirir; boşsa varsayılan ölçümleri kullanır.
        private static string[] SplitTags(string? tags)
        {
            string value = string.IsNullOrWhiteSpace(tags) ? DefaultTags : tags;
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}