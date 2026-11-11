using DataAccess.Abstract;
using Entities.Enums;
using S7.Net;
using S7.Net.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;

namespace DataAccess.Concrete.S7Net
{
    public class S7DriverDal : IS7DriverDal, IDisposable
    {
        private Plc? _plc;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private string _ipAddress = "192.168.0.1";
        private CpuType _cpuType = CpuType.S71200;
        private short _rack = 0;
        private short _slot = 1;
        private bool _disposed = false;

        // Aynı bit üzerinde aynı anda birden fazla darbe başlatılmasını engelleyen kayıt.
        private readonly ConcurrentDictionary<string, byte> _activePulses = new();

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
                if (_plc != null && _plc.IsConnected)
                    _plc.Close();

                _plc = new Plc(_cpuType, _ipAddress, _rack, _slot);
                await _plc.OpenAsync();
                return _plc.IsConnected;
            }
            catch (Exception)
            {
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
                if (_plc != null && _plc.IsConnected)
                {
                    _plc.Close();
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // PLC IP adresine ping atar, yanıt gelirse true döner.
        public async Task<bool> PingAsync(string? ipAddress = null)
        {
            string targetIp = ipAddress ?? _ipAddress;
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(targetIp, 1000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
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
                EnsureConnected();
                var buffer = await _plc!.ReadBytesAsync(DataType.DataBlock, db, startByte, count);

                // Eksik gelen veri yanlış çözülmesin diye uzunluk kontrol edilir.
                if (buffer == null || buffer.Length != count)
                    throw new InvalidOperationException(
                        $"DB{db}.DBB{startByte} adresinden {count} bayt okunamadı (gelen: {buffer?.Length ?? 0}).");

                return buffer;
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
                EnsureConnected();
                await _plc!.WriteBytesAsync(DataType.DataBlock, db, startByte, data);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Bir DB bloğunu tek seferde okuyup verilen sınıf nesnesine doldurur.
        public async Task<T?> ReadClassAsync<T>(T instance, int dbNumber, int startByte = 0) where T : class, new()
        {
            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();
                await _plc!.ReadClassAsync(instance, dbNumber, startByte);
                return instance;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Verilen sınıf nesnesini tek seferde PLC DB bloğuna yazar.
        public async Task WriteClassAsync<T>(T instance, int dbNumber, int startByte = 0) where T : class
        {
            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();
                await _plc!.WriteClassAsync(instance, dbNumber, startByte);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Belirtilen adresten tek bir değişkeni (tag) okur.
        public async Task<object?> ReadTagAsync(DataType dataType, int db, int startByte, VarType varType, int varCount = 1)
        {
            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();
                return await _plc!.ReadAsync(dataType, db, startByte, varType, varCount);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Belirtilen adrese tek bir değişken (tag) değeri yazar.
        public async Task WriteTagAsync(DataType dataType, int db, int startByte, object value)
        {
            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();
                await _plc!.WriteAsync(dataType, db, startByte, value);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Belirtilen adresteki bir biti okur (bit numarası S7.Net'e iletilir).
        public async Task<bool> ReadBitAsync(DataType dataType, int db, int startByte, int bitNumber)
        {
            if (bitNumber < 0 || bitNumber > 7)
                throw new ArgumentOutOfRangeException(nameof(bitNumber), "Bit numarası 0-7 aralığında olmalıdır.");

            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();

                // DÜZELTİLDİ (Madde 2): bitNumber, ReadAsync'in bitAdr parametresi olarak iletiliyor.
                var result = await _plc!.ReadAsync(dataType, db, startByte, VarType.Bit, 1, (byte)bitNumber);

                if (result is bool boolValue)
                    return boolValue;

                // Okuma beklenen tipte dönmediyse sessizce false dönmek yerine hata fırlatılır (ACK'nin yanlış okunmasını önler).
                throw new InvalidOperationException(
                    $"Bit okunamadı. Adres: {dataType} DB{db}.DBX{startByte}.{bitNumber}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Belirtilen adresteki bire true/false değeri yazar (S7.Net'in WriteBitAsync metoduyla).
        public async Task WriteBitAsync(DataType dataType, int db, int startByte, int bitNumber, bool value)
        {
            if (bitNumber < 0 || bitNumber > 7)
                throw new ArgumentOutOfRangeException(nameof(bitNumber), "Bit numarası 0-7 aralığında olmalıdır.");

            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();
                await _plc!.WriteBitAsync(dataType, db, startByte, bitNumber, value);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Biti true yapar, belirtilen süre bekler ve hata olsa bile mutlaka false'a çeker (start darbesi).
        public async Task PulseBitAsync(DataType dataType, int db, int startByte, int bitNumber, int durationMs = 500)
        {
            if (durationMs <= 0 || durationMs > 10000)
                throw new ArgumentOutOfRangeException(nameof(durationMs), "Darbe süresi 1-10000 ms aralığında olmalıdır.");

            string pulseKey = $"{dataType}:{db}:{startByte}:{bitNumber}";

            // Aynı bit için başka bir darbe sürüyorsa yenisi başlatılmaz.
            if (!_activePulses.TryAdd(pulseKey, 0))
                throw new InvalidOperationException($"Bu bit için darbe zaten devam ediyor: DB{db}.DBX{startByte}.{bitNumber}");

            bool writeAttempted = false;
            Exception? pulseError = null;

            try
            {
                // true yazımı PLC'ye ulaşıp yanıtı kaybolmuş olabilir; bu yüzden denendiği anda sıfırlama zorunlu sayılır.
                writeAttempted = true;
                await WriteBitAsync(dataType, db, startByte, bitNumber, true);

                await Task.Delay(durationMs);
            }
            catch (Exception ex)
            {
                pulseError = ex;
                throw;
            }
            finally
            {
                try
                {
                    if (writeAttempted)
                    {
                        bool resetOk = await ResetBitWithRetryAsync(dataType, db, startByte, bitNumber);
                        if (!resetOk)
                        {
                            throw new InvalidOperationException(
                                $"KRİTİK: DB{db}.DBX{startByte}.{bitNumber} biti false'a çekilemedi, PLC'de TRUE kalmış olabilir. PLC'yi kontrol edin.",
                                pulseError);
                        }
                    }
                }
                finally
                {
                    _activePulses.TryRemove(pulseKey, out _);
                }
            }
        }

        // Biti false'a çekmeyi birkaç kez dener, başarılı olursa true döner.
        private async Task<bool> ResetBitWithRetryAsync(DataType dataType, int db, int startByte, int bitNumber, int maxAttempts = 3)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await WriteBitAsync(dataType, db, startByte, bitNumber, false);
                    return true;
                }
                catch (Exception)
                {
                    if (attempt < maxAttempts)
                        await Task.Delay(100);
                }
            }
            return false;
        }

        // Komut bitini gönderir, onay (ACK) bitini bekler ve komut bitini sıfırlar.
        public async Task<bool> ExecuteCommandWithAckAsync(
            int cmdDb, int cmdByte, int cmdBit,
            int ackDb, int ackByte, int ackBit,
            int timeoutMs = 3000)
        {
            // 1. Komut Biti Gönderilir (TRUE)
            await WriteBitAsync(DataType.DataBlock, cmdDb, cmdByte, cmdBit, true);

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    bool ack = await ReadBitAsync(DataType.DataBlock, ackDb, ackByte, ackBit);
                    if (ack)
                    {
                        // 2. Onay Alındı, Komut Biti Sıfırlanır (FALSE)
                        await WriteBitAsync(DataType.DataBlock, cmdDb, cmdByte, cmdBit, false);
                        return true;
                    }
                    await Task.Delay(50, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Zaman aşımı: Komut biti güvenlik amacıyla sıfırlanır
                await WriteBitAsync(DataType.DataBlock, cmdDb, cmdByte, cmdBit, false);
            }
            return false;
        }

        // Birden fazla değişkeni tek istekte okur.
        public async Task<List<DataItem>> ReadMultipleTagsAsync(List<DataItem> dataItems)
        {
            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();
                await _plc!.ReadMultipleVarsAsync(dataItems);
                return dataItems;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Bağlantı kopuksa PLC'ye yeniden bağlanır, bağlanamazsa hata fırlatır.
        private void EnsureConnected()
        {
            if (_plc == null || !_plc.IsConnected)
            {
                _plc = new Plc(_cpuType, _ipAddress, _rack, _slot);
                _plc.Open();
                if (!_plc.IsConnected)
                {
                    throw new InvalidOperationException($"PLC ile iletişim kurulamadı. IP: {_ipAddress}");
                }
            }
        }

        // PLC'nin çalışma durumunu (Run/Stop/Error/Unknown) ping ile tahmin eder.
        public async Task<CpuState> GetCpuStatusAsync()
        {
            if (!IsConnected)
                return CpuState.Unknown;

            try
            {
                // PLC bağlantısı aktifse ve yanıt alınıyorsa RUN kabul edilir
                // S7-1200 / S7-1500 için SZL okuma yapılarak STOP/RUN ayrımı derinleştirilebilir
                bool pingOk = await PingAsync();
                return pingOk ? CpuState.Run : CpuState.Stop;
            }
            catch
            {
                return CpuState.Error;
            }
        }

        // PLC bağlantısını kapatır ve semaphore'u serbest bırakır.
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_plc != null && _plc.IsConnected)
                {
                    _plc.Close();
                }
                _semaphore.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        // Float (Real) setpoint değerini belirtilen DB adresine yazar.
        public async Task WriteSetpointAsync(int db, int startByte, float value)
        {
            await _semaphore.WaitAsync();
            try
            {
                EnsureConnected();
                await _plc!.WriteAsync(DataType.DataBlock, db, startByte, value);
            }
            catch (Exception ex)
            {
                throw new Exception($"PLC DB{db}.DBD{startByte} adresine {value} setpoint değeri yazılırken hata oluştu: {ex.Message}", ex);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}