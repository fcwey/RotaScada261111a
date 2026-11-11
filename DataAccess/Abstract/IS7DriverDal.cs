using Entities.Enums;
using S7.Net;
using S7.Net.Types;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataAccess.Abstract
{
    public interface IS7DriverDal
    {
        // Bağlantı & Durum Metodları
        bool IsConnected { get; }
        Task<bool> ConnectAsync(string ipAddress, CpuType cpuType = CpuType.S71200, short rack = 0, short slot = 1);
        void Disconnect();
        Task<bool> PingAsync(string? ipAddress = null);
        Task<CpuState> GetCpuStatusAsync();

        // Ham Bayt Okuma ve Yazma (DB200–204 arayüzü bunları kullanır)
        Task<byte[]> ReadBytesAsync(int db, int startByte, int count);
        Task WriteBytesAsync(int db, int startByte, byte[] data);

        // Bulk / Class Okuma ve Yazma
        Task<T?> ReadClassAsync<T>(T instance, int dbNumber, int startByte = 0) where T : class, new();
        Task WriteClassAsync<T>(T instance, int dbNumber, int startByte = 0) where T : class;

        // Tag ve Bit Düzeyinde İşlemler
        Task<object?> ReadTagAsync(DataType dataType, int db, int startByte, VarType varType, int varCount = 1);
        Task WriteTagAsync(DataType dataType, int db, int startByte, object value);
        Task WriteSetpointAsync(int db, int startByte, float value);
        Task<bool> ReadBitAsync(DataType dataType, int db, int startByte, int bitNumber);
        Task WriteBitAsync(DataType dataType, int db, int startByte, int bitNumber, bool value);
        Task PulseBitAsync(DataType dataType, int db, int startByte, int bitNumber, int durationMs = 500);

        // Güvenli Komut El Sıkışma (Handshake / Ack)
        Task<bool> ExecuteCommandWithAckAsync(
            int cmdDb, int cmdByte, int cmdBit,
            int ackDb, int ackByte, int ackBit,
            int timeoutMs = 3000);

        // Çoklu Tag Okuma (Multi-Var Read)
        Task<List<DataItem>> ReadMultipleTagsAsync(List<DataItem> dataItems);
    }
}