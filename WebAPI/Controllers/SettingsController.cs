using Business.Abstract;
using Core.Constants;
using Core.Extensions;
using Core.Utilities.Results;
using Entities.Structures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class SettingsController : ControllerBase
    {
        private readonly ISystemSettingService _settingService;
        private readonly IAuditTrailService _auditTrailService;

        // Ayar ve işlem kaydı servis bağımlılıklarını alır.
        public SettingsController(ISystemSettingService settingService, IAuditTrailService auditTrailService)
        {
            _settingService = settingService;
            _auditTrailService = auditTrailService;
        }

        // DB204'teki bütün parametreleri tanımları ve güncel değerleriyle listeler.
        [HttpGet("parameters")]
        public async Task<IActionResult> GetParameters()
        {
            var result = await _settingService.GetParametersAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Tek bir parametreyi değiştirir; güvenlik parametreleri ayrı yetki ister ve her deneme kaydedilir.
        [HttpPut("parameters/{address:int}")]
        public async Task<IActionResult> UpdateParameter(int address, [FromQuery] int value)
        {
            var definition = ParamCatalog.Find(address);
            if (definition == null)
                return BadRequest(new ErrorResult($"Adres {address} yazılabilir bir parametre değil."));

            string requiredRole = definition.IsSafety
                ? ParameterPermissions.SafetyLimitsWrite
                : ParameterPermissions.CalibrationWrite;

            var userRoles = User.ClaimRoles();
            if (!userRoles.Contains(PlcPermissions.Admin) && !userRoles.Contains(requiredRole))
            {
                await LogAsync("PARAMETER_UPDATE_DENIED",
                    $"'{definition.Name}' için yetkisiz deneme. Gerekli yetki: {requiredRole}", null, value.ToString());

                return StatusCode(StatusCodes.Status403Forbidden,
                    new ErrorResult($"YETKİSİZ ERİŞİM: '{definition.Name}' parametresi için '{requiredRole}' yetkisi gereklidir."));
            }

            var result = await _settingService.UpdateParameterAsync(address, value);

            await LogAsync(result.IsSuccess ? "PARAMETER_UPDATE" : "PARAMETER_UPDATE_FAILED",
                $"'{definition.Name}' (DB204 adres {address}). Sonuç: {result.Message}",
                result.IsSuccess ? result.Data.ToString() : null,
                value.ToString());

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Parametre işlemini kullanıcı, eski ve yeni değerle işlem kaydına yazar; kayıt hatası işlemi etkilemez.
        private async Task LogAsync(string actionType, string description, string? oldValue, string? newValue)
        {
            try
            {
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                await _auditTrailService.LogActionAsync(User.GetUserId(), actionType, $"{User.GetUserName()}: {description}", oldValue, newValue, clientIp);
            }
            catch
            {
                // İşlem kaydı yazılamasa da sonuç kullanıcıya dönmelidir.
            }
        }
    }
}