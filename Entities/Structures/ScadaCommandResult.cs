namespace Entities.Structures
{
    // PLC'nin bir komuta verdiği cevap (AckResult ve AckDetail) ve operatöre gösterilecek metni
    public class ScadaCommandResult
    {
        public short CmdId { get; set; }
        public short ResultCode { get; set; }
        public short Detail { get; set; }
        public bool Accepted => ResultCode == 1;
        public string Message { get; set; } = string.Empty;

        // AckResult kodunu operatöre gösterilecek Türkçe metne çevirir.
        public static string Describe(short resultCode, short detail)
        {
            if (resultCode >= 110 && resultCode <= 121)
                return $"Reçete hatası ({resultCode}), adım {detail}: {DescribeRecipeError(resultCode)}";

            return resultCode switch
            {
                0 => "Komut işleniyor",
                1 => "Komut kabul edildi",
                100 => "Otomatik mod seçili değil",
                101 => "Aktif alarm var",
                102 => "Başlatma izni yok (kapak, eşanjörde su, güç)",
                103 => "Komut bu durumda geçersiz",
                104 => "Program zaten aktif",
                106 => "Duraklatılmış değil",
                107 => "Adım numarası geçersiz",
                108 => "Vakum adımına atlanamaz",
                109 => "Süre geçersiz (adım süresinden büyük ya da negatif)",
                130 => "Online edit: reçete tipi değiştirilemez",
                131 => "Online edit: adım sayısı aktif adımdan küçük",
                132 => "Online edit: aktif adımın tipi değiştirilemez",
                140 => "Manuel mod aktif değil",
                141 => "Cihaz numarası geçersiz",
                142 => "Değer limit dışı",
                190 => "Bilinmeyen komut kodu",
                191 => "Komut iletildi ama hiçbir blok cevap vermedi",
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
                117 => "Gaz reçete tipine uygun olmalı",
                118 => "Değer limit dışında",
                119 => "dNH3_Flow 0 olmalı",
                120 => "H2_Auto için adım ısıtma olmalı, NH3_Flow ≥ 1 ve H2_Set 1–1000 olmalı",
                121 => "Nitrasyon reçetesinde en az bir vakum adımı olmalı",
                _ => "Reçete doğrulama hatası"
            };
        }
    }
}