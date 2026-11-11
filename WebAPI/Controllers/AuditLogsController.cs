using Business.Abstract;
using Core.Constants;
using Core.Extensions;
using Core.Utilities.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class AuditLogsController : ControllerBase
    {
        private readonly IAuditTrailService _auditTrailService;

        // İşlem kaydı servis bağımlılığını alır.
        public AuditLogsController(IAuditTrailService auditTrailService)
        {
            _auditTrailService = auditTrailService;
        }

        // İşlem kayıtlarını tarih, kullanıcı ve işlem tipine göre listeler; yalnızca Admin erişebilir.
        [HttpGet]
        public async Task<IActionResult> GetLogs([FromQuery] DateTime start, [FromQuery] DateTime end, [FromQuery] int? userId, [FromQuery] string? actionType)
        {
            if (!User.ClaimRoles().Contains(PlcPermissions.Admin))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new ErrorResult("YETKİSİZ ERİŞİM: İşlem kayıtları için 'Admin' yetkisi gereklidir."));
            }

            var result = await _auditTrailService.GetAuditLogsAsync(start, end, userId, actionType);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }
    }
}