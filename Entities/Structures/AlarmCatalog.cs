namespace Entities.Structures
{
    // PLC alarm listesi (kılavuz bölüm 10): numara, başlangıç sınıfı ve operatöre gösterilecek metin
    public static class AlarmCatalog
    {
        public const int MaxAlarmNo = 64;   // DB201'de kullanılan alarm biti sayısı (1..59 tanımlı)

        // Sınıf: 0 uyarı, 1 duraklat, 2 güvenli durdur
        private static readonly Dictionary<int, (int Class, string Text)> Alarms = new()
        {
            { 1,  (2, "Acil stop basılı") },
            { 2,  (2, "Güç yok (acil durum kontaktörü çekmedi)") },
            { 3,  (2, "E5DC yüksek sıcaklık alarmı") },
            { 4,  (2, "Harici basınç alarmı (Enda)") },
            { 5,  (2, "Basınç en yüksek değeri aştı") },
            { 6,  (2, "Pota sıcaklığı en yüksek değeri aştı") },
            { 7,  (2, "Basınç sensörü arızası") },
            { 8,  (2, "Program çalışırken kapak kapalı değil") },
            { 9,  (1, "Pota termokuplu arızası") },
            { 10, (1, "Alt cehennemlik termokuplu arızası") },
            { 11, (1, "Üst cehennemlik termokuplu arızası") },
            { 12, (1, "Azot MFC arızası") },
            { 13, (1, "Amonyak MFC arızası") },
            { 14, (1, "Azot hattında basınç yok") },
            { 15, (1, "Amonyak hattında basınç yok") },
            { 16, (1, "Sirkülasyon fanı arızası (termik ya da sürücü)") },
            { 17, (1, "Vakum motoru termik arızası") },
            { 18, (1, "Soğutma klepesi kapanmadı") },
            { 19, (1, "Alt rezistans güç ünitesi (DPU) arızası") },
            { 20, (1, "Üst rezistans güç ünitesi (DPU) arızası") },
            { 21, (1, "Pota sıcaklığı seti aştı") },
            { 22, (1, "Atık gaz brülörü yanmıyor") },
            { 23, (0, "Azot debisi sete uymuyor") },
            { 24, (0, "Amonyak debisi sete uymuyor") },
            { 25, (0, "CO2 hattında basınç yok") },
            { 26, (0, "Soğutma fanı termik arızası") },
            { 27, (0, "Hidrolik motoru termik arızası") },
            { 28, (0, "Soğutma klepesi açılmadı") },
            { 29, (0, "Cehennemlik sıcaklığı sınırı aştı, güç kesildi") },
            { 30, (0, "Acil azot devrede (basınç düşük)") },
            { 31, (0, "Gsoak bekleme süresi aşıldı") },
            { 32, (0, "H2 sete getirilemiyor (amonyak düzeltmesi sınırda)") },
            { 33, (0, "Seçilen H2 kontrol yöntemi desteklenmiyor") },
            { 34, (0, "H2 sensörü arızası") },
            { 35, (0, "SCADA ile haberleşme yok") },
            { 36, (0, "Süre sayacı ile PLC saati uyuşmuyor") },
            { 37, (0, "Vakum süresinde tamamlanmadı, operatör bekleniyor") },
            { 38, (0, "Azot dolumu süresinde tamamlanmadı, operatör bekleniyor") },
            { 39, (0, "Enerji kesintisinden dönüldü, operatör bekleniyor") },
            { 40, (0, "Su soğutma: boşaltmada pompaya su gelmedi") },
            { 41, (0, "Su soğutma: boşaltma süresinde bitmedi") },
            { 42, (0, "Eşanjör su vanası konuma gelmedi") },
            { 43, (0, "Alt DPU ile haberleşme yok") },
            { 44, (0, "Üst DPU ile haberleşme yok") },
            { 45, (0, "Enerji analizörü ile haberleşme yok") },
            { 46, (0, "Sürücü ile haberleşme yok") },
            { 47, (0, "RS485 portu ayarlanamadı") },
            { 48, (0, "Program uzun süredir operatör tarafından duraklatılmış") },
            { 49, (0, "Giriş/çıkış modülü arızası") },
            { 50, (0, "Atmosfer temizliği (yıkama) operatör tarafından elle onaylandı") },
            { 51, (0, "Simülasyon modu aktif (girişler modelden geliyor, fiziksel çıkışlar kapalı)") },
            { 52, (0, "Korumalı bekleme operatör tarafından sonlandırıldı") },
            { 53, (0, "Kapak kilidi operatör tarafından aşıldı") },
            { 54, (0, "Tahliye açık ama basınç boşalmıyor (tahliye vanası açmıyor ya da hat tıkalı olabilir)") },
            { 55, (0, "Basınç üst emniyeti devreye girdi (basınç kontrol vanası ya da disko çek valf boşaltamıyor olabilir)") },
            { 56, (0, "Vanalar kapalı ve gaz veriliyor ama basınç oluşmuyor (vana kapatmıyor ya da kaçak olabilir)") },
            { 57, (0, "Acil azot açık ama basınç yükselmiyor (azot yok ya da vana açmıyor olabilir)") },
            { 58, (0, "Dengeleme yapılırken alt ve üst cehennemlik sıcaklıkları ayrışıyor") },
            { 59, (0, "Kaçak testi sonunda azot dolumu süresinde tamamlanamadı") }
        };

        // Ayarlardan gelen, başlangıç değerini ezen sınıflar (alarm no -> sınıf)
        private static Dictionary<int, int> _classOverrides = new();

        // Sahada değiştirilen alarm sınıflarını yükler; aralık dışı numara ve sınıflar yok sayılır.
        public static void SetClassOverrides(IReadOnlyDictionary<int, int> overrides)
        {
            var valid = new Dictionary<int, int>();
            foreach (var item in overrides)
            {
                if (item.Key >= 1 && item.Key <= MaxAlarmNo && item.Value >= 0 && item.Value <= 2)
                    valid[item.Key] = item.Value;
            }
            _classOverrides = valid;
        }

        // Alarm numarasından veritabanında kullanılan etiket adını üretir (örnek: ALM_022).
        public static string GetTag(int alarmNo)
        {
            return $"ALM_{alarmNo:000}";
        }

        // Alarm numarasının operatöre gösterilecek metnini döner.
        public static string GetText(int alarmNo)
        {
            return Alarms.TryGetValue(alarmNo, out var alarm)
                ? alarm.Text
                : $"Tanımsız alarm ({alarmNo})";
        }

        // Alarm numarasının sınıfını döner; ayarlarda ezilmişse onu, yoksa başlangıç değerini kullanır.
        public static int GetClass(int alarmNo)
        {
            if (_classOverrides.TryGetValue(alarmNo, out int overrideClass))
                return overrideClass;

            return Alarms.TryGetValue(alarmNo, out var alarm) ? alarm.Class : 0;
        }

        // Alarm sınıfını mevcut öncelik seviyesine çevirir (1 bilgi, 2 uyarı, 3 kritik).
        public static int GetPriority(int alarmNo)
        {
            return GetClass(alarmNo) + 1;
        }
    }
}