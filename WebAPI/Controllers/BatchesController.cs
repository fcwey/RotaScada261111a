using Business.Abstract;
using Core.Constants;
using Core.Extensions;
using Core.Utilities.Results;
using Entities.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
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
        private readonly IBatchInfoService _batchInfoService;

        // Şarj ve şarj bilgisi servis bağımlılıklarını alır.
        public BatchesController(IBatchService batchService, IBatchInfoService batchInfoService)
        {
            _batchService = batchService;
            _batchInfoService = batchInfoService;
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

        // Şarjın operatör tarafından girilen ağırlığını ve notlarını getirir.
        [HttpGet("{id:int}/info")]
        public async Task<IActionResult> GetInfo(int id)
        {
            var result = await _batchInfoService.GetInfoAsync(id);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Şarj ağırlığını yazar ya da düzeltir (çalışan ya da geçmiş şarjda); Plc.Operate ya da Admin yetkisi gerekir.
        [HttpPut("{id:int}/load")]
        public async Task<IActionResult> SetLoad(int id, [FromQuery] float? kg)
        {
            if (!CanOperate())
                return Forbidden();

            var result = await _batchInfoService.SetLoadAsync(id, kg);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Şarja not ekler (çalışan ya da geçmiş şarjda); Plc.Operate ya da Admin yetkisi gerekir.
        [HttpPost("{id:int}/notes")]
        public async Task<IActionResult> AddNote(int id, [FromBody] BatchNoteRequestDto request)
        {
            if (!CanOperate())
                return Forbidden();

            var result = await _batchInfoService.AddNoteAsync(id, User.GetUserId(), request.Text);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Kullanıcının şarj bilgilerini değiştirme yetkisi olup olmadığını döner.
        private bool CanOperate()
        {
            var roles = User.ClaimRoles();
            return roles.Contains(PlcPermissions.Admin) || roles.Contains(PlcPermissions.Operate);
        }

        // Yetkisiz erişim cevabını üretir.
        private IActionResult Forbidden()
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ErrorResult($"YETKİSİZ ERİŞİM: Bu işlem için '{PlcPermissions.Operate}' yetkisi gereklidir."));
        }
    }
}