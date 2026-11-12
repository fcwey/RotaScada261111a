using Business.Abstract;
using Entities.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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

        // E-posta ve şifreyle giriş yapar; art arda hatalı denemede bir süre bekletir.
        [HttpPost("login")]
        public ActionResult Login(UserForLoginDto userForLoginDto)
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "bilinmiyor";
            string email = (userForLoginDto.Email ?? string.Empty).Trim().ToLowerInvariant();
            string key = $"{email}|{clientIp}";

            int blockedSeconds = _loginAttemptTracker.GetBlockedSeconds(key);
            if (blockedSeconds > 0)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    $"Çok fazla hatalı giriş denemesi. {blockedSeconds} saniye sonra yeniden deneyin.");
            }

            var userToLogin = _authService.Login(userForLoginDto);
            if (!userToLogin.IsSuccess)
            {
                _loginAttemptTracker.RegisterFailure(key);
                _logger.LogWarning("Hatalı giriş denemesi: {Email} ({ClientIp})", email, clientIp);
                return BadRequest(userToLogin.Message);
            }

            _loginAttemptTracker.Reset(key);

            var result = _authService.CreateAccessToken(userToLogin.Data);
            if (result.IsSuccess)
            {
                return Ok(result.Data);
            }
            return BadRequest(result.Message);
        }
    }
}