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
    public class LeakTestsController : ControllerBase
    {
        private const int DefaultRangeDays = 90;

        private readonly IPlcEventService _plcEventService;

        // PLC olay servis bağımlılığını alır.
        public LeakTestsController(IPlcEventService plcEventService)
        {
            _plcEventService = plcEventService;
        }

        // Verilen tarih aralığındaki kaçak testi kayıtlarını listeler; aralık verilmezse son 90 günü getirir.
        [HttpGet]
        public async Task<IActionResult> GetLeakTests([FromQuery] DateTime? start, [FromQuery] DateTime? end)
        {
            DateTime rangeEnd = end ?? DateTime.Now;
            DateTime rangeStart = start ?? rangeEnd.AddDays(-DefaultRangeDays);

            var result = await _plcEventService.GetLeakTestsAsync(rangeStart, rangeEnd);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }
    }
}