using DataAccess.Abstract;
using S7.Net;
using System;

namespace DataAccess.Concrete.S7Net
{
    public class S7DriverDal : IS7DriverDal, IDisposable
    {
        private const int TimeoutMs = 3000;   // Bağlanma, okuma ve yazma için en uzun bekleme

        private Plc? _plc;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private string _ipAddress = "192.168.0.1";
        private CpuType _cpuType = CpuType.S71200;
        private short _rack = 0;
        private short _slot = 1;
        private bool _disposed = false;

        // PLC bağlantısının aktif olup olmadığını döner.
        public bool IsConnected => _plc != null && _plc.IsConnected;

        // Verilen IP ve CPU bilgileriyle PLC'ye bağlanır, başarı durumunu döner.
        public async Task<bool> ConnectAsync(string ipAddress, CpuType cpuType = CpuType.S71200, short rack = 0, short slot = 1)
        {
            _ipAddress = ipAddress;
            _cpuType = cpuType;
            _rack = rack;
            _slot = slot;

            await _semaphore.WaitAsync();
            try
            {
                CloseConnection();
                await OpenConnectionAsync();
                return true;
            }
            catch (Exception)
            {
                CloseConnection();
                return false;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // PLC bağlantısını kapatır.
        public void Disconnect()
        {
            _semaphore.Wait();
            try
            {
                CloseConnection();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Bir DB'nin verilen adresinden istenen sayıda ham baytı tek seferde okur.
        public async Task<byte[]> ReadBytesAsync(int db, int startByte, int count)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Okunacak bayt sayısı 0'dan büyük olmalıdır.");

            await _semaphore.WaitAsync();
            try
            {
                await EnsureConnectedAsync();

                using var timeout = new CancellationTokenSource(TimeoutMs);
                var buffer = await _plc!.ReadBytesAsync(DataType.DataBlock, db, startByte, count, timeout.Token);

                // Eksik gelen veri yanlış çözülmesin diye uzunluk kontrol edilir.
                if (buffer == null || buffer.Length != count)
                    throw new InvalidOperationException(
                        $"DB{db}.DBB{startByte} adresinden {count} bayt okunamadı (gelen: {buffer?.Length ?? 0}).");

                return buffer;
            }
            catch (OperationCanceledException)
            {
                CloseConnection();
                throw new TimeoutException($"PLC {TimeoutMs} ms içinde cevap vermedi (DB{db} okuma).");
            }
            catch (Exception)
            {
                // Hata sonrası bağlantının durumu belirsizdir; kapatılır ve bir sonraki işlemde yeniden kurulur.
                CloseConnection();
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Verilen ham baytları bir DB'nin belirtilen adresine tek seferde yazar.
        public async Task WriteBytesAsync(int db, int startByte, byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Yazılacak veri boş olamaz.", nameof(data));

            await _semaphore.WaitAsync();
            try
            {
                await EnsureConnectedAsync();

                using var timeout = new CancellationTokenSource(TimeoutMs);
                await _plc!.WriteBytesAsync(DataType.DataBlock, db, startByte, data, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                CloseConnection();
                throw new TimeoutException($"PLC {TimeoutMs} ms içinde cevap vermedi (DB{db} yazma).");
            }
            catch (Exception)
            {
                CloseConnection();
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Bağlantı kopuksa PLC'ye yeniden bağlanır.
        private async Task EnsureConnectedAsync()
        {
            if (_plc == null || !_plc.IsConnected)
            {
                CloseConnection();
                await OpenConnectionAsync();
            }
        }

        // Yeni bir PLC bağlantısı açar; süresinde açılamazsa hata fırlatır.
        private async Task OpenConnectionAsync()
        {
            var plc = new Plc(_cpuType, _ipAddress, _rack, _slot);

            try
            {
                using var timeout = new CancellationTokenSource(TimeoutMs);
                await plc.OpenAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                plc.Close();
                throw new TimeoutException($"PLC'ye {TimeoutMs} ms içinde bağlanılamadı. IP: {_ipAddress}");
            }
            catch (Exception)
            {
                plc.Close();
                throw;
            }

            if (!plc.IsConnected)
            {
                plc.Close();
                throw new InvalidOperationException($"PLC ile iletişim kurulamadı. IP: {_ipAddress}");
            }

            _plc = plc;
        }

        // Açık bağlantı varsa kapatır; kapatma hatası yok sayılır.
        private void CloseConnection()
        {
            try
            {
                _plc?.Close();
            }
            catch
            {
                // Zaten kopmuş bir bağlantıyı kapatırken oluşan hata önemsizdir.
            }
            _plc = null;
        }

        // PLC bağlantısını kapatır ve semaphore'u serbest bırakır.
        public void Dispose()
        {
            if (!_disposed)
            {
                CloseConnection();
                _semaphore.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}