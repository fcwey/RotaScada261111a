using Business.Abstract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class InterlocksController : ControllerBase
    {
        private readonly IInterlockService _interlockService;

        // İzin ve cihaz durumu servis bağımlılığını alır.
        public InterlocksController(IInterlockService interlockService)
        {
            _interlockService = interlockService;
        }

        // Mimik ekran için cihaz ve izin bitlerini döner.
        [HttpGet("mimic-state")]
        public async Task<IActionResult> GetMimicState()
        {
            var result = await _interlockService.GetMimicStateAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Verilen işlem için PLC'nin izin verip vermediğini döner.
        [HttpPost("validate-safety")]
        public async Task<IActionResult> ValidateSafety([FromQuery] string operationCode)
        {
            var result = await _interlockService.ValidateOperationSafetyAsync(operationCode);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }
    }
}