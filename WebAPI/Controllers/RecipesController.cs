using Business.Abstract;
using Core.Constants;
using Core.Extensions;
using Core.Utilities.Results;
using Entities.Concrete;
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
    public class RecipesController : ControllerBase
    {
        private readonly IRecipeService _recipeService;
        private readonly IAuditTrailService _auditTrailService;

        // Reçete ve işlem kaydı servis bağımlılıklarını alır.
        public RecipesController(IRecipeService recipeService, IAuditTrailService auditTrailService)
        {
            _recipeService = recipeService;
            _auditTrailService = auditTrailService;
        }

        // Kullanımdaki reçeteleri listeler.
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _recipeService.GetAllRecipesAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Verilen id'ye ait reçeteyi getirir.
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _recipeService.GetRecipeByIdAsync(id);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Yeni reçete ekler; Plc.ProgramEdit ya da Admin yetkisi gerekir.
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] Recipe recipe)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.ProgramEdit))
                return Forbidden(PlcPermissions.ProgramEdit);

            var result = await _recipeService.AddRecipeAsync(recipe);
            await LogAsync("RECIPE_ADD", $"'{recipe.RecipeName}'. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Mevcut reçeteyi günceller; Plc.ProgramEdit ya da Admin yetkisi gerekir.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Recipe recipe)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.ProgramEdit))
                return Forbidden(PlcPermissions.ProgramEdit);

            recipe.Id = id;
            var result = await _recipeService.UpdateRecipeAsync(recipe);
            await LogAsync("RECIPE_UPDATE", $"Reçete id {id}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Reçeteyi siler ya da kullanımdan kaldırır; Plc.ProgramEdit ya da Admin yetkisi gerekir.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.ProgramEdit))
                return Forbidden(PlcPermissions.ProgramEdit);

            var result = await _recipeService.DeleteRecipeAsync(id);
            await LogAsync("RECIPE_DELETE", $"Reçete id {id}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Reçeteyi PLC'ye yazıp yalnızca kontrol ettirir; Plc.Operate ya da Admin yetkisi gerekir.
        [HttpPost("{id:int}/validate")]
        public async Task<IActionResult> ValidateOnPlc(int id)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.Operate))
                return Forbidden(PlcPermissions.Operate);

            var result = await _recipeService.ValidateOnPlcAsync(id);
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Reçeteyi PLC'ye yazıp programı başlatır; Plc.Operate ya da Admin yetkisi gerekir.
        [HttpPost("{id:int}/start")]
        public async Task<IActionResult> Start(int id)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.Operate))
                return Forbidden(PlcPermissions.Operate);

            var result = await _recipeService.StartRecipeAsync(id, User.GetUserId());
            await LogAsync(result.IsSuccess ? "RECIPE_START" : "RECIPE_START_REJECTED", $"Reçete id {id}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // PLC'de çalışan reçeteyi (DB203) döner.
        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            var result = await _recipeService.GetActiveRecipeAsync();
            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Çalışan programı gönderilen reçeteyle değiştirir; Plc.ProgramEdit ya da Admin yetkisi gerekir.
        [HttpPost("apply-edit")]
        public async Task<IActionResult> ApplyEdit([FromBody] Recipe editedRecipe)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.ProgramEdit))
                return Forbidden(PlcPermissions.ProgramEdit);

            var result = await _recipeService.ApplyEditAsync(editedRecipe);
            await LogAsync(result.IsSuccess ? "RECIPE_APPLY_EDIT" : "RECIPE_APPLY_EDIT_REJECTED",
                $"Çalışan reçete id {editedRecipe.Id}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Kullanıcının verilen rollerden en az birine sahip olup olmadığını döner.
        private bool HasAnyRole(params string[] roles)
        {
            var userRoles = User.ClaimRoles();
            return roles.Any(role => userRoles.Contains(role));
        }

        // Yetkisiz erişim cevabını gerekli rol adıyla birlikte üretir.
        private IActionResult Forbidden(string requiredRole)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ErrorResult($"YETKİSİZ ERİŞİM: Bu işlem için '{requiredRole}' yetkisi gereklidir."));
        }

        // Yapılan reçete işlemini işlem kaydına yazar; kayıt hatası işlemi etkilemez.
        private async Task LogAsync(string actionType, string description)
        {
            try
            {
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                await _auditTrailService.LogActionAsync(User.GetUserId(), actionType, $"{User.GetUserName()}: {description}", null, null, clientIp);
            }
            catch
            {
                // İşlem kaydı yazılamasa da sonuç operatöre dönmelidir.
            }
        }
    }
}