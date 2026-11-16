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

            if (!HasAnyRole(PlcPermissions.Admin, requiredRole))
            {
                await LogAsync("PARAMETER_UPDATE_DENIED",
                    $"'{definition.Name}' için yetkisiz deneme. Gerekli yetki: {requiredRole}", null, value.ToString());

                return Forbidden($"'{definition.Name}' parametresi için '{requiredRole}' yetkisi gereklidir.");
            }

            var result = await _settingService.UpdateParameterAsync(address, value, User.GetUserId());

            await LogAsync(result.IsSuccess ? "PARAMETER_UPDATE" : "PARAMETER_UPDATE_FAILED",
                $"'{definition.Name}' (DB204 adres {address}). Sonuç: {result.Message}",
                result.IsSuccess ? result.Data.ToString() : null,
                value.ToString());

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Parametre yedeklerini listeler.
        [HttpGet("backups")]
        public async Task<IActionResult> GetBackups()
        {
            var result = await _settingService.GetBackupsAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // PLC'deki parametrelerin o anki halini notuyla yedekler; parametre yazma yetkilerinden biri gerekir.
        [HttpPost("backups")]
        public async Task<IActionResult> CreateBackup([FromQuery] string? note)
        {
            if (!HasAnyRole(PlcPermissions.Admin, ParameterPermissions.CalibrationWrite, ParameterPermissions.SafetyLimitsWrite))
                return Forbidden($"Yedek almak için '{ParameterPermissions.CalibrationWrite}' yetkisi gereklidir.");

            var result = await _settingService.CreateBackupAsync(note, User.GetUserId());
            await LogAsync("PARAMETER_BACKUP", $"Not: {note}. Sonuç: {result.Message}", null, null);

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Yedekteki değerlerle PLC'deki değerlerin farklarını döner.
        [HttpGet("backups/{id:int}/compare")]
        public async Task<IActionResult> CompareBackup(int id)
        {
            var result = await _settingService.CompareBackupAsync(id);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Yedeği PLC'ye geri yükler; güvenlik parametrelerini de yazdığı için güvenlik yetkisi gerekir.
        [HttpPost("backups/{id:int}/restore")]
        public async Task<IActionResult> RestoreBackup(int id)
        {
            if (!HasAnyRole(PlcPermissions.Admin, ParameterPermissions.SafetyLimitsWrite))
            {
                await LogAsync("PARAMETER_RESTORE_DENIED", $"Yedek {id} için yetkisiz geri yükleme denemesi.", null, null);
                return Forbidden($"Geri yükleme için '{ParameterPermissions.SafetyLimitsWrite}' yetkisi gereklidir.");
            }

            var result = await _settingService.RestoreBackupAsync(id, User.GetUserId());

            string detail = result.Data == null
                ? string.Empty
                : string.Join("; ", result.Data.Select(i => $"{i.Name}: {i.PreviousValue} → {i.AppliedValue} ({i.Status})"));

            await LogAsync(result.IsSuccess ? "PARAMETER_RESTORE" : "PARAMETER_RESTORE_FAILED",
                $"Yedek {id}. Sonuç: {result.Message}", null, detail);

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Kullanıcının verilen rollerden en az birine sahip olup olmadığını döner.
        private bool HasAnyRole(params string[] roles)
        {
            var userRoles = User.ClaimRoles();
            return roles.Any(role => userRoles.Contains(role));
        }

        // Yetkisiz erişim cevabını verilen açıklamayla üretir.
        private IActionResult Forbidden(string detail)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResult($"YETKİSİZ ERİŞİM: {detail}"));
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