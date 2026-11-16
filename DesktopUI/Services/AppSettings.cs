using System.Text.Json;

namespace DesktopUI.Services
{
    // Ekran programının appsettings.json dosyasındaki ayarları
    public class AppSettings
    {
        public string ServerUrl { get; set; } = "https://localhost:5001";   // SCADA sunucusunun adresi
        public string FurnaceName { get; set; } = "Fırın";                  // Üst şeritte görünen fırın adı
        public int StaleSeconds { get; set; } = 5;                          // Bu kadar saniye veri gelmezse "veri gelmiyor" uyarısı çıkar

        // Ayarları programın yanındaki appsettings.json dosyasından okur; dosya yoksa ya da bozuksa varsayılanları kullanır.
        public static AppSettings Load()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(path))
                    return new AppSettings();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), options) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }
}