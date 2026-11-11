namespace Entities.Structures
{
    // Reçete doğrulamasında kullanılan sınırlar (PLC bağlıyken DB204'ten okunur)
    public class RecipeLimits
    {
        public int MaxPotaTemp { get; set; } = 750;         // DB204 adres 2
        public int FanMinSpeed { get; set; } = 30;          // DB204 adres 50
        public int N2MfcMaxLph { get; set; } = 32767;       // DB204 adres 58
        public int NH3MfcMaxLph { get; set; } = 32767;      // DB204 adres 60
    }
}