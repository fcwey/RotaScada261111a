using Entities.Dtos;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DesktopUI.Services
{
    // Sunucunun bütün cevaplarında kullandığı sonuç zarfı
    public class ApiResult<T>
    {
        public bool IsSuccess { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    // Giriş ucunun döndürdüğü token
    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
    }

    // Sunucuya yapılan bütün HTTP isteklerinin geçtiği sınıf; hata durumunda istisna atmaz, başarısız sonuç döner.
    public class ApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;
        private string? _token;

        // Sunucu token'ı kabul etmediğinde (401) tetiklenir; ekran giriş sayfasına döner.
        public event Action? Unauthorized;

        // Sunucu adresiyle HTTP istemcisini kurar.
        public ApiClient(string serverUrl)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        // Sonraki isteklerde kullanılacak token'ı ayarlar; null verilirse token kaldırılır.
        public void SetToken(string? token)
        {
            _token = token;
        }

        // E-posta ve şifreyle giriş yapıp token döner.
        public Task<ApiResult<LoginResponse>> LoginAsync(string email, string password)
        {
            return SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", new UserForLoginDto { Email = email, Password = password });
        }

        // Token'ın sahibi kullanıcıyı ve güncel rollerini döner.
        public Task<ApiResult<CurrentUserDto>> GetCurrentUserAsync()
        {
            return SendAsync<CurrentUserDto>(HttpMethod.Get, "api/auth/me", null);
        }

        // Verilen adrese GET isteği yapar.
        public Task<ApiResult<T>> GetAsync<T>(string path)
        {
            return SendAsync<T>(HttpMethod.Get, path, null);
        }

        // Verilen adrese POST isteği yapar; gövde verilmeyebilir.
        public Task<ApiResult<T>> PostAsync<T>(string path, object? body = null)
        {
            return SendAsync<T>(HttpMethod.Post, path, body);
        }

        // Verilen adrese PUT isteği yapar; gövde verilmeyebilir.
        public Task<ApiResult<T>> PutAsync<T>(string path, object? body = null)
        {
            return SendAsync<T>(HttpMethod.Put, path, body);
        }

        // Verilen adrese DELETE isteği yapar.
        public Task<ApiResult<T>> DeleteAsync<T>(string path)
        {
            return SendAsync<T>(HttpMethod.Delete, path, null);
        }

        // PLC'ye komut gönderir (api/plc/command).
        public Task<ApiResult<JsonElement>> SendCommandAsync(int code, int param1 = 0, int param2 = 0, int param3 = 0)
        {
            return PostAsync<JsonElement>($"api/plc/command?code={code}&param1={param1}&param2={param2}&param3={param3}");
        }

        // İsteği gönderir, cevabı sonuç zarfına çevirir; ağ hatası ve yetki hatası da başarısız sonuç olarak döner.
        private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body)
        {
            try
            {
                using var request = new HttpRequestMessage(method, path);

                if (!string.IsNullOrEmpty(_token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

                if (body != null)
                    request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);

                using var response = await _http.SendAsync(request);
                string text = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Unauthorized?.Invoke();
                    return Fail<T>("Oturum geçersiz. Yeniden giriş yapın.");
                }

                // Olağan durum: sunucu { isSuccess, message, data } zarfı döner.
                var wrapped = TryParse<ApiResult<T>>(text);
                if (wrapped != null && (wrapped.IsSuccess || !string.IsNullOrEmpty(wrapped.Message)))
                {
                    if (!response.IsSuccessStatusCode)
                        wrapped.IsSuccess = false;
                    return wrapped;
                }

                // Giriş ucu gibi zarfsız cevap veren uçlar: veri doğrudan gövdededir.
                if (response.IsSuccessStatusCode)
                {
                    var raw = TryParse<T>(text);
                    if (raw != null)
                        return new ApiResult<T> { IsSuccess = true, Data = raw };
                }

                return Fail<T>(ReadMessage(text, response.StatusCode));
            }
            catch (HttpRequestException ex)
            {
                return Fail<T>($"Sunucuya ulaşılamıyor: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return Fail<T>("Sunucu zamanında cevap vermedi.");
            }
        }

        // Verilen mesajla başarısız sonuç üretir.
        private static ApiResult<T> Fail<T>(string message)
        {
            return new ApiResult<T> { IsSuccess = false, Message = message };
        }

        // Metni verilen tipe çevirmeyi dener; olmazsa varsayılan değeri döner.
        private static T? TryParse<T>(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(text, JsonOptions);
            }
            catch (JsonException)
            {
                return default;
            }
        }

        // Hata cevabının gövdesinden kullanıcıya gösterilecek mesajı çıkarır (düz metin ya da JSON olabilir).
        private static string ReadMessage(string text, HttpStatusCode status)
        {
            string fallback = $"Sunucu hatası ({(int)status}).";
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            string trimmed = text.Trim();

            try
            {
                if (trimmed.StartsWith('"'))
                    return JsonSerializer.Deserialize<string>(trimmed) ?? fallback;

                if (trimmed.StartsWith('{'))
                {
                    using var document = JsonDocument.Parse(trimmed);
                    foreach (string name in new[] { "message", "Message", "title", "detail" })
                    {
                        if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                            return value.GetString() ?? fallback;
                    }
                    return fallback;
                }
            }
            catch (JsonException)
            {
                return fallback;
            }

            return trimmed;
        }
    }
}