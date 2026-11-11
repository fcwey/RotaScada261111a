using Business.Abstract;
using Core.Constants;
using Core.Extensions;
using Core.Utilities.Results;
using Entities.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Sadece giriş yapmış kullanıcılar istek atabilir
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IAuditTrailService _auditTrailService;

        // Kullanıcı ve işlem kaydı servis bağımlılıklarını alır.
        public UsersController(IUserService userService, IAuditTrailService auditTrailService)
        {
            _userService = userService;
            _auditTrailService = auditTrailService;
        }

        // Bütün kullanıcıları rolleriyle birlikte listeler.
        [HttpGet]
        public IActionResult GetUsers()
        {
            if (!IsAdmin()) return Forbidden();

            return Ok(_userService.GetUsers());
        }

        // Atanabilecek rol adlarını listeler.
        [HttpGet("roles")]
        public IActionResult GetRoles()
        {
            if (!IsAdmin()) return Forbidden();

            return Ok(_userService.GetRoleNames());
        }

        // Yeni kullanıcı açar ve rollerini atar.
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] UserCreateDto dto)
        {
            if (!IsAdmin()) return Forbidden();

            var result = _userService.CreateUser(dto);
            await LogAsync("USER_CREATE", $"'{dto.Email}' kullanıcısı. Roller: {string.Join(", ", dto.Roles ?? new List<string>())}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Kullanıcının rollerini gönderilen listeyle değiştirir.
        [HttpPut("{id}/roles")]
        public async Task<IActionResult> SetRoles(int id, [FromBody] List<string> roles)
        {
            if (!IsAdmin()) return Forbidden();

            var result = _userService.SetRoles(id, roles);
            await LogAsync("USER_ROLES_UPDATE", $"Kullanıcı id {id}. Yeni roller: {string.Join(", ", roles ?? new List<string>())}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Kullanıcı hesabını açar ya da kapatır.
        [HttpPut("{id}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromQuery] bool active)
        {
            if (!IsAdmin()) return Forbidden();

            var result = _userService.SetActive(id, active);
            await LogAsync("USER_STATUS_UPDATE", $"Kullanıcı id {id}. Aktif: {active}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // Kullanıcının şifresini yenisiyle değiştirir.
        [HttpPut("{id}/password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] UserPasswordDto dto)
        {
            if (!IsAdmin()) return Forbidden();

            var result = _userService.ResetPassword(id, dto.NewPassword);
            await LogAsync("USER_PASSWORD_RESET", $"Kullanıcı id {id}. Sonuç: {result.Message}");

            if (result.IsSuccess) return Ok(result);
            return BadRequest(result);
        }

        // İsteği yapan kullanıcının Admin rolüne sahip olup olmadığını döner.
        private bool IsAdmin()
        {
            return User.ClaimRoles().Contains(PlcPermissions.Admin);
        }

        // Yetkisiz erişim cevabını üretir.
        private IActionResult Forbidden()
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ErrorResult("YETKİSİZ ERİŞİM: Kullanıcı yönetimi için 'Admin' yetkisi gereklidir."));
        }

        // Yapılan kullanıcı yönetimi işlemini işlem kaydına yazar; kayıt hatası işlemi etkilemez.
        private async Task LogAsync(string actionType, string description)
        {
            try
            {
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                await _auditTrailService.LogActionAsync(User.GetUserId(), actionType, $"{User.GetUserName()}: {description}", null, null, clientIp);
            }
            catch
            {
                // İşlem kaydı yazılamasa da sonuç yöneticiye dönmelidir.
            }
        }
    }
}