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
    public class BatchesController : ControllerBase
    {
        private const int DefaultRangeDays = 30;

        private readonly IBatchService _batchService;

        // Şarj servis bağımlılığını alır.
        public BatchesController(IBatchService batchService)
        {
            _batchService = batchService;
        }

        // Verilen tarih aralığında başlayan şarjları listeler; aralık verilmezse son 30 günü getirir.
        [HttpGet]
        public async Task<IActionResult> GetBatches([FromQuery] DateTime? start, [FromQuery] DateTime? end)
        {
            DateTime rangeEnd = end ?? DateTime.Now;
            DateTime rangeStart = start ?? rangeEnd.AddDays(-DefaultRangeDays);

            var result = await _batchService.GetBatchesAsync(rangeStart, rangeEnd);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Tek şarjın özetini, reçete geçmişini ve şarj sırasındaki alarmları getirir.
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetBatchDetail(int id)
        {
            var result = await _batchService.GetBatchDetailAsync(id);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }
    }
}