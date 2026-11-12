using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Core.Extensions
{
    public class ExceptionMiddleware
    {
        private RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        // Sıradaki işleyiciyi ve logger'ı alır.
        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        // İsteği işler; yakalanmamış bir hata çıkarsa kaydeder ve düzgün bir cevap döner.
        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception e)
            {

                await HandleExceptionAsync(httpContext, e);
            }

        }

        // Hatayı hata koduyla birlikte log dosyasına yazar ve kullanıcıya aynı kodu içeren cevabı döner.
        private Task HandleExceptionAsync(HttpContext httpContext, Exception e)
        {
            bool isValidation = e.GetType() == typeof(ValidationException);
            string errorId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string message;

            if (isValidation)
            {
                // Doğrulama hatası kullanıcının düzeltebileceği bir hatadır; metni aynen döner.
                message = e.Message;
            }
            else
            {
                string user = httpContext.User?.Identity?.Name ?? "anonim";

                _logger.LogError(e, "Beklenmeyen hata [{ErrorId}] {Method} {Path} (kullanıcı: {User})",
                    errorId, httpContext.Request.Method, httpContext.Request.Path, user);

                message = $"Beklenmeyen bir hata oluştu. Hata kodu: {errorId}";
            }

            // Cevap gönderilmeye başlandıysa artık değiştirilemez.
            if (httpContext.Response.HasStarted)
                return Task.CompletedTask;

            httpContext.Response.ContentType = "application/json";
            httpContext.Response.StatusCode = isValidation
                ? (int)HttpStatusCode.BadRequest
                : (int)HttpStatusCode.InternalServerError;

            return httpContext.Response.WriteAsync(new ErrorDetails
            {
                StatusCode = httpContext.Response.StatusCode,
                Message = message
            }.ToString());
        }
    }
}