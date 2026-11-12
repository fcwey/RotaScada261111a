using Business.Abstract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class ProfilesController : ControllerBase
    {
        private readonly IProfileService _profileService;

        // Teorik eğri servis bağımlılığını alır.
        public ProfilesController(IProfileService profileService)
        {
            _profileService = profileService;
        }

        // Kayıtlı reçetenin teorik sıcaklık eğrisini döner; başlangıç sıcaklığı verilmezse 20 °C alınır.
        [HttpGet("recipe/{recipeId:int}")]
        public async Task<IActionResult> GetRecipeProfile(int recipeId, [FromQuery] float startTemp = 20f)
        {
            var result = await _profileService.GetRecipeProfileAsync(recipeId, startTemp);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Şarjın teorik eğrisini döner: varsayılan olarak geçerli plan, original=true ile başlangıçtaki plan.
        [HttpGet("batch/{batchId:int}")]
        public async Task<IActionResult> GetBatchProfile(int batchId, [FromQuery] bool original = false)
        {
            var result = await _profileService.GetBatchProfileAsync(batchId, original);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // O an çalışan şarjın geçerli teorik eğrisini döner.
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveProfile()
        {
            var result = await _profileService.GetActiveBatchProfileAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }
    }
}