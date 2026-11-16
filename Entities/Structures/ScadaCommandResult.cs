namespace Entities.Structures
{
    // PLC'nin bir komuta verdiği cevap (AckResult ve AckDetail) ve operatöre gösterilecek metni
    public class ScadaCommandResult
    {
        // Kabul edilen komutta AckDetail bu değerse: program sonunda korumalı bekleme olacak uyarısı
        public const short HotEndWarning = 901;

        public short CmdId { get; set; }
        public short ResultCode { get; set; }
        public short Detail { get; set; }
        public bool Accepted => ResultCode == 1;
        public bool HasWarning => Accepted && Detail == HotEndWarning;
        public string Message { get; set; } = string.Empty;

        // AckResult kodunu operatöre gösterilecek Türkçe metne çevirir.
        public static string Describe(short resultCode, short detail)
        {
            if (resultCode >= 110 && resultCode <= 121)
            {
                string where = detail > 0 ? $"adım {detail}" : "reçete başlığı ya da bütünü";
                return $"Reçete hatası ({resultCode}), {where}: {DescribeRecipeError(resultCode)}";
            }

            if (resultCode == 1 && detail == HotEndWarning)
                return "Komut kabul edildi. UYARI: soğutma bitiş sıcaklığı kapak açma sınırından yüksek; program sonunda korumalı bekleme olacak.";

            return resultCode switch
            {
                0 => "Komut işleniyor",
                1 => "Komut kabul edildi",
                100 => "Otomatik mod seçili değil",
                101 => "Aktif alarm var",
                102 => "Başlatma izni yok (kapak kapalı ve kilitli değil, acil stop, eşanjörde su var ya da kaçak testi sürüyor)",
                103 => "Komut bu durumda geçersiz",
                104 => "Program zaten aktif",
                106 => "Program duraklatılmış değil",
                107 => "Adım numarası geçersiz",
                108 => "Vakum adımına atlanamaz",
                109 => "Süre geçersiz (adım süresinden büyük ya da negatif)",
                130 => "Reçete tipi çalışırken değiştirilemez",
                131 => "Adım sayısı aktif adımdan küçük",
                132 => "Aktif adımın tipi değiştirilemez",
                140 => "Manuel mod aktif değil",
                141 => "Cihaz numarası geçersiz",
                142 => "Değer sınır dışında ya da parametre geçersiz",
                143 => "Kısıtlı manuelde bu cihaz otomatik kontrol ediliyor",
                144 => "Basınç modu \"elle vana\" değil",
                145 => "Saat ayarlanamadı (değer geçersiz ya da yazma hatası)",
                150 => "Isıtma aktif değil",
                151 => "PID ayarı zaten sürüyor",
                152 => "Set sabit değil (manuelde ya da Hold'da başlatılmalı)",
                153 => "Süren bir PID ayarı yok",
                160 => "Yıkama onayı ısıtma adımında verilemez",
                161 => "Atmosfer zaten temiz",
                162 => "Korumalı beklemeden komutla çıkış kapalı (HoldExitMode = 0)",
                163 => "Aşma komutları PLC ayarıyla kapatılmış",
                164 => "Program çalışıyor, atmosfer kirli ya da vakum var",
                170 => "Program çalışıyor ya da içeride amonyak atmosferi var",
                171 => "Fırın sıcak (kaçak testi sıcaklık sınırının altında değil ya da sağlam termokupl yok)",
                172 => "Vakum izni yok ya da vakum motoru arızalı",
                173 => "Basınç sensörü arızalı",
                174 => "Durum uygun değil (test zaten sürüyor ya da iptal edilecek test yok)",
                175 => "Parametre geçersiz (0 ya da 1 olmalı)",
                190 => "Tanımsız komut kodu",
                191 => "Komutu hiçbir blok sahiplenmedi (PLC tarafı hata)",
                _ => $"Bilinmeyen sonuç kodu ({resultCode})"
            };
        }

        // Reçete doğrulama hata kodunu (110–121) kural metnine çevirir.
        private static string DescribeRecipeError(short resultCode)
        {
            return resultCode switch
            {
                110 => "Reçete tipi 1, 2 ya da 3 olmalı",
                111 => "Adım sayısı 1–20 arasında olmalı",
                112 => "Her adımın tipi 1, 2 ya da 3 olmalı (boş adım olamaz)",
                113 => "Vakum adımları en başta ve ardışık olmalı",
                114 => "Soğutma tek olmalı ve ondan sonra adım gelmemeli",
                115 => "Son adım soğutma olmalı",
                116 => "En az bir ısıtma adımı olmalı",
                117 => "Adımda reçete tipine uymayan gaz var (soğutmada amonyak da verilemez)",
                118 => "Bir değer sınır dışında (sıcaklık, süre, debi, yüzde, fan hızı)",
                119 => "dNH3_Flow 0 olmalı (donanım yok)",
                120 => "H2_Auto için adım ısıtma olmalı, NH3_Flow ≥ 1 ve H2_Set 1–1000 olmalı",
                121 => "Nitrasyon reçetesinde en az bir vakum adımı olmalı",
                _ => "Reçete doğrulama hatası"
            };
        }
    }
}