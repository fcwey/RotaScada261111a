using S7.Net;
using System.Threading.Tasks;

namespace DataAccess.Abstract
{
    public interface IS7DriverDal
    {
        // Bağlantı
        bool IsConnected { get; }
        Task<bool> ConnectAsync(string ipAddress, CpuType cpuType = CpuType.S71200, short rack = 0, short slot = 1);
        void Disconnect();

        // Ham bayt okuma ve yazma (DB200–204 arayüzü yalnızca bunları kullanır)
        Task<byte[]> ReadBytesAsync(int db, int startByte, int count);
        Task WriteBytesAsync(int db, int startByte, byte[] data);
    }
}