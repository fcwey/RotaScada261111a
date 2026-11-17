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

        // Durum mesajı kodunu operatöre gösterilen cümleye çevirir.
        public static string DescribeSeqMessage(short seqMessage)
        {
            return seqMessage switch
            {
                0 => "Görev bekliyor",
                10 => "Vakum yapılıyor",
                11 => "Azot dolduruluyor",
                20 => "Isıtma",
                21 => "Gsoak bekleniyor",
                22 => "Amonyak izni bekleniyor",
                30 => "Soğutma",
                31 => "Soğutma ve yıkama",
                40 => "Yıkama",
                45 => "Korumalı bekleme (kapak açma sıcaklığı bekleniyor)",
                60 => "Program bitti",
                >= 71 and <= 77 => "Duraklatıldı",
                80 => "Güvenli durdurma",
                _ => $"Mesaj {seqMessage}"
            };
        }

        // Duraklatma sebebi kodunu metne çevirir.
        public static string DescribeHoldReason(short holdReason)
        {
            return holdReason switch
            {
                0 => string.Empty,
                1 => "Operatör bekletti: yalnızca süre durdu, sıcaklık ve gazlar sürüyor",
                2 => "Gsoak beklemesi",
                3 => "Alarm: ısıtma ve proses gazları kesildi",
                4 => "Otomatik mod kapatıldı",
                5 => "Enerji geri geldi: ısıtma ve proses gazları kesik",
                6 => "Vakum zaman aşımı: Devam yeniden dener, Vakum Onay geçer",
                _ => $"Duraklatma sebebi {holdReason}"
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