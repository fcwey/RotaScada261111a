using Entities.Structures;

namespace DesktopUI.Helpers
{
    // PLC'nin durum kodlarını ekranda gösterilen metinlere çevirir (kılavuzdaki kod sözlüğü).
    public static class StatusTexts
    {
        // Sekans durum kodunu metne çevirir.
        public static string DescribeSeqState(short seqState)
        {
            return seqState switch
            {
                0 => "BEKLEME",
                20 => "VAKUM",
                30 => "ISITMA",
                40 => "YIKAMA",
                45 => "KORUMALI BEKLEME",
                50 => "SOĞUTMA",
                60 => "BİTTİ",
                70 => "DURAKLATILDI",
                80 => "GÜVENLİ DURDURMA",
                _ => $"DURUM {seqState}"
            };
        }

        // Çalışma modunu metne çevirir; simülasyon ve kısıtlı manuel öne alınır.
        public static string DescribeMode(ScadaStatus status)
        {
            if (status.GetDeviceBit(1, 11)) return "SİMÜLASYON";
            if (status.GetDeviceBit(1, 10)) return "KISITLI MANUEL";

            return status.Mode switch
            {
                1 => "MANUEL",
                2 => "OTOMATİK",
                _ => "KAPALI"
            };
        }

        // Dakikayı saat:dakika biçiminde yazar.
        public static string FormatMinutes(int minutes)
        {
            if (minutes < 0) minutes = 0;
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }
}