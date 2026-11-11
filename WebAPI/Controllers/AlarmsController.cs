using Business.Abstract;
using Core.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class AlarmsController : ControllerBase
    {
        private readonly IAlarmService _alarmService;

        // Alarm servis bağımlılığını alır.
        public AlarmsController(IAlarmService alarmService)
        {
            _alarmService = alarmService;
        }

        // Aktif alarmları listeler.
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveAlarms()
        {
            var result = await _alarmService.GetActiveAlarmsAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Alarmı, giriş yapmış kullanıcının adına ve notuyla birlikte onaylar.
        [HttpPost("acknowledge/{alarmId}")]
        public async Task<IActionResult> AcknowledgeAlarm(int alarmId, [FromQuery] string? note)
        {
            var result = await _alarmService.AcknowledgeAlarmAsync(alarmId, User.GetUserId(), note);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Tarih aralığı ve önceliğe göre geçmiş alarmları listeler.
        [HttpGet("history")]
        public async Task<IActionResult> GetAlarmHistory([FromQuery] DateTime start, [FromQuery] DateTime end, [FromQuery] int? priority)
        {
            var result = await _alarmService.GetAlarmHistoryAsync(start, end, priority);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }
    }
}