using Core.Entities;
using Entities.Structures;

namespace Entities.Dtos
{
    // SignalR ile ekranlara gönderilen canlı veri paketi
    public class ScadaLiveDto : IDto
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsConnected { get; set; }          // PLC'den okuma yapılabiliyor mu
        public bool IsStale { get; set; }              // PLC yaşam sinyali 3 sn'dir değişmedi, veriler eski
        public string? Error { get; set; }             // Bağlantı ya da sürüm uyuşmazlığı mesajı
        public ScadaStatus? Status { get; set; }       // DB201'in çözülmüş hali
    }
}