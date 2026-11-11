namespace Entities.Structures
{
    // PLC alarm listesi (kılavuz bölüm 5.4): numara, varsayılan sınıf ve operatöre gösterilecek metin
    public static class AlarmCatalog
    {
        public const int MaxAlarmNo = 64;   // DB201'de ayrılan alarm biti sayısı

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
            { 47, (0, "RS485 portu ayarlanamadı") }
        };

        // Ayarlardan gelen, varsayılanı ezen sınıflar (alarm no -> sınıf)
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

        // Alarm numarasının sınıfını döner; ayarlarda ezilmişse onu, yoksa varsayılanı kullanır.
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