using Business.Abstract;
using Entities.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : Controller
    {
        private IAuthService _authService;
        private readonly ILoginAttemptTracker _loginAttemptTracker;
        private readonly ILogger<AuthController> _logger;

        // Kimlik doğrulama, deneme sayacı ve log bağımlılıklarını alır.
        public AuthController(IAuthService authService, ILoginAttemptTracker loginAttemptTracker, ILogger<AuthController> logger)
        {
            _authService = authService;
            _loginAttemptTracker = loginAttemptTracker;
            _logger = logger;
        }

        // E-posta ve şifreyle giriş yapar; hesap ya da bilgisayar bekletiliyorsa doğru şifreyi de kabul etmez.
        [HttpPost("login")]
        public ActionResult Login(UserForLoginDto userForLoginDto)
        {
            string clientAddress = GetClientAddress();
            string email = (userForLoginDto.Email ?? string.Empty).Trim().ToLowerInvariant();

            // İki sayaç tutulur: biri bu hesap için, biri bu bilgisayardan gelen bütün denemeler için.
            string accountKey = $"hesap|{email}|{clientAddress}";
            string addressKey = $"adres|{clientAddress}";

            int blockedSeconds = Math.Max(
                _loginAttemptTracker.GetBlockedSeconds(accountKey),
                _loginAttemptTracker.GetBlockedSeconds(addressKey));

            if (blockedSeconds > 0)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    $"Çok fazla hatalı giriş denemesi. {blockedSeconds} saniye sonra yeniden deneyin.");
            }

            var userToLogin = _authService.Login(userForLoginDto);
            if (!userToLogin.IsSuccess)
            {
                _loginAttemptTracker.RegisterFailure(accountKey);
                _loginAttemptTracker.RegisterFailure(addressKey);
                _logger.LogWarning("Hatalı giriş denemesi: {Email} ({ClientAddress})", email, clientAddress);
                return BadRequest(userToLogin.Message);
            }

            _loginAttemptTracker.Reset(accountKey);
            _loginAttemptTracker.Reset(addressKey);

            var result = _authService.CreateAccessToken(userToLogin.Data);
            if (result.IsSuccess)
            {
                return Ok(result.Data);
            }
            return BadRequest(result.Message);
        }

        // İsteği yapan bilgisayarın adresini döner; aynı bilgisayarın farklı yazımlarını (::1, 127.0.0.1) tek adrese indirir.
        private string GetClientAddress()
        {
            IPAddress? address = HttpContext.Connection.RemoteIpAddress;
            if (address == null)
                return "bilinmiyor";

            if (IPAddress.IsLoopback(address))
                return "yerel";

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            return address.ToString();
        }
    }
}