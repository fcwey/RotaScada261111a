using Core.Constants;
using Core.Extensions;
using Core.Utilities.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class RecipesController : ControllerBase
    {
        private const string NotReadyMessage =
            "Reçete gönderme yeni PLC arayüzüne (DB202) henüz taşınmadı. Eski akış yanlış DB'lere yazdığı için kapatıldı.";

        // Reçeteyi PLC'ye indirir; Plc.Operate ya da Admin yetkisi gerekir (yeni arayüze taşınana kadar kapalı).
        [HttpPost("download/{recipeId}")]
        public IActionResult DownloadToPlc(int recipeId)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.Operate))
                return Forbidden(PlcPermissions.Operate);

            return StatusCode(StatusCodes.Status501NotImplemented, new ErrorResult(NotReadyMessage));
        }

        // Reçeteyi indirip programı başlatır; Plc.Operate ya da Admin yetkisi gerekir (yeni arayüze taşınana kadar kapalı).
        [HttpPost("download-and-start/{recipeId}")]
        public IActionResult DownloadAndStart(int recipeId)
        {
            if (!HasAnyRole(PlcPermissions.Admin, PlcPermissions.Operate))
                return Forbidden(PlcPermissions.Operate);

            return StatusCode(StatusCodes.Status501NotImplemented, new ErrorResult(NotReadyMessage));
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
    }
}