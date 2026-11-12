using Business.Abstract;
using Core.Constants;
using Core.Extensions;
using Core.Utilities.Results;
using Entities.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Linq;
using System.Threading.Tasks;
using WebAPI.Hubs.WebAPI.Hubs;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class PlcController : ControllerBase
    {
        private readonly IPlcService _plcService;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IBatchService _batchService;
        private readonly IHubContext<ScadaHub> _scadaHubContext;

        // PLC, işlem kaydı, şarj ve ekran bildirimi bağımlılıklarını alır.
        public PlcController(
            IPlcService plcService,
            IAuditTrailService auditTrailService,
            IBatchService batchService,
            IHubContext<ScadaHub> scadaHubContext)
        {
            _plcService = plcService;
            _auditTrailService = auditTrailService;
            _batchService = batchService;
            _scadaHubContext = scadaHubContext;
        }

        // Verilen IP adresindeki PLC'ye bağlanır; Admin ya da PLC ayar yetkisi gerekir.
        [HttpPost("connect")]
        public async Task<IActionResult> Connect([FromQuery] string ipAddress = "192.168.0.1")
        {
            if (!HasAnyRole(PlcPermissions.Admin, ParameterPermissions.PlcIpConfigWrite))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new ErrorResult($"YETKİSİZ ERİŞİM: PLC bağlantısı için '{ParameterPermissions.PlcIpConfigWrite}' yetkisi gereklidir."));
            }

            var result = await _plcService.ConnectPlcAsync(ipAddress);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // DB201'i okuyup anlık durumu döner.
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var result = await _plcService.ReadStatusAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Komutun gerektirdiği rolü kontrol eder, PLC'ye gönderir, sonucu kaydeder; planı değiştiren komutu şarja işler.
        [HttpPost("command")]
        public async Task<IActionResult> SendCommand(
            [FromQuery] short code,
            [FromQuery] short param1 = 0,
            [FromQuery] short param2 = 0,
            [FromQuery] short param3 = 0)
        {
            if (!Enum.IsDefined(typeof(ScadaCommandCode), code))
                return BadRequest(new ErrorResult($"Bilinmeyen komut kodu: {code}"));

            var commandCode = (ScadaCommandCode)code;
            string? requiredRole = GetRequiredRole(commandCode);

            if (requiredRole != null && !HasAnyRole(PlcPermissions.Admin, requiredRole))
            {
                await LogCommandAsync("PLC_COMMAND_DENIED", commandCode, param1, param2, param3,
                    $"Yetkisiz deneme. Gerekli yetki: {requiredRole}");

                return StatusCode(StatusCodes.Status403Forbidden,
                    new ErrorResult($"YETKİSİZ ERİŞİM: {commandCode} komutu için '{requiredRole}' yetkisi gereklidir."));
            }

            var result = await _plcService.SendCommandAsync(commandCode, param1, param2, param3);

            await LogCommandAsync(result.IsSuccess ? "PLC_COMMAND" : "PLC_COMMAND_REJECTED",
                commandCode, param1, param2, param3, result.Message);

            if (result.IsSuccess)
            {
                await RecordPlanEventAsync(commandCode, param1, param2);
                return Ok(result);
            }
            return BadRequest(result);
        }

        // Adıma atlama ve soğutmaya geçiş komutlarını şarjın plan geçmişine işler ve ekranlara plan bildirimi gönderir.
        private async Task RecordPlanEventAsync(ScadaCommandCode code, short param1, short param2)
        {
            string? reason = code switch
            {
                ScadaCommandCode.GotoStep => "GOTO",
                ScadaCommandCode.SkipToCooling => "SKIP",
                _ => null
            };

            if (reason == null)
                return;

            try
            {
                await _batchService.RecordPlanEventAsync(reason, param1, param2, User.GetUserId());
                await _scadaHubContext.Clients.All.SendAsync("ReceiveProfileChanged");
            }
            catch
            {
                // Plan kaydı ya da bildirim başarısız olsa da komutun sonucu operatöre dönmelidir.
            }
        }

        // Komut kodunun gerektirdiği rolü döner; null ise giriş yapmış olmak yeterlidir.
        private static string? GetRequiredRole(ScadaCommandCode code)
        {
            return code switch
            {
                ScadaCommandCode.Start => PlcPermissions.Operate,
                ScadaCommandCode.Stop => PlcPermissions.Operate,
                ScadaCommandCode.Hold => PlcPermissions.Operate,
                ScadaCommandCode.Resume => PlcPermissions.Operate,
                ScadaCommandCode.VacuumOk => PlcPermissions.Operate,
                ScadaCommandCode.AlarmReset => PlcPermissions.Operate,

                ScadaCommandCode.SkipToCooling => PlcPermissions.ProgramEdit,
                ScadaCommandCode.GotoStep => PlcPermissions.ProgramEdit,
                ScadaCommandCode.ApplyEdit => PlcPermissions.ProgramEdit,

                ScadaCommandCode.Manual => PlcPermissions.Manual,

                ScadaCommandCode.BuzzerMute => null,
                ScadaCommandCode.Validate => null,

                // Listeye sonradan eklenen komutlar varsayılan olarak yalnızca Admin'e açıktır.
                _ => PlcPermissions.Admin
            };
        }

        // Kullanıcının verilen rollerden en az birine sahip olup olmadığını döner.
        private bool HasAnyRole(params string[] roles)
        {
            var userRoles = User.ClaimRoles();
            return roles.Any(role => userRoles.Contains(role));
        }

        // Komutu kullanıcı, parametre ve sonuç bilgisiyle işlem kaydına yazar; kayıt hatası komutu etkilemez.
        private async Task LogCommandAsync(string actionType, ScadaCommandCode code, short param1, short param2, short param3, string? resultMessage)
        {
            try
            {
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

                await _auditTrailService.LogActionAsync(
                    User.GetUserId(),
                    actionType,
                    $"{User.GetUserName()}: {code} ({(short)code}) P1={param1} P2={param2} P3={param3}. Sonuç: {resultMessage}",
                    null,
                    null,
                    clientIp);
            }
            catch
            {
                // İşlem kaydı yazılamasa da komutun sonucu operatöre dönmelidir.
            }
        }
    }
}