using Business.Abstract;
using Core.Aspects.Autofac.Logging;
using Core.CrossCuttingConcerns.Logging.Log4Net.Loggers;
using Core.Utilities.Results;
using DataAccess.Abstract;
using Entities.Enums;
using Entities.Structures;
using S7.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Business.Concrete
{
    public class PlcManager : IPlcService
    {
        private readonly IS7DriverDal _s7DriverDal;

        // Aynı anda tek komut gönderilmesini sağlar (önceki cevap gelmeden yenisi gitmez).
        private readonly SemaphoreSlim _commandLock = new SemaphoreSlim(1, 1);

        private short _cmdId;                    // Son gönderilen komut numarası
        private bool _cmdIdLoaded;               // CmdId PLC'den okundu mu
        private short _heartbeat;                // SCADA yaşam sinyali sayacı
        private volatile bool _layoutVerified;   // Şema sürümü doğrulandı mı

        // Sürücü bağımlılığını alır.
        public PlcManager(IS7DriverDal s7DriverDal)
        {
            _s7DriverDal = s7DriverDal;
        }

        // PLC'ye bağlanır ve şema sürümünü doğrular.
        public async Task<IResult> ConnectPlcAsync(string ipAddress)
        {
            _layoutVerified = false;
            _cmdIdLoaded = false;

            var result = await _s7DriverDal.ConnectAsync(ipAddress, CpuType.S71200, 0, 1);
            if (!result)
                return new ErrorResult("PLC'ye Bağlanılamadı");

            var layout = await VerifyLayoutAsync();
            if (!layout.IsSuccess)
                return new ErrorResult($"PLC'ye bağlanıldı ama yazma kapalı: {layout.Message}");

            return new SuccessResult("PLC Bağlantısı Başarılı");
        }

        // PLC bağlantısını keser.
        public IResult DisconnectPlc()
        {
            _s7DriverDal.Disconnect();
            _layoutVerified = false;
            _cmdIdLoaded = false;
            return new SuccessResult("PLC Bağlantısı Kesildi");
        }

        // DB201 ve DB204'teki şema sürümünü okur; 3 değilse yazmaya izin vermez.
        public async Task<IResult> VerifyLayoutAsync()
        {
            try
            {
                var statusVer = S7Bytes.GetInt(
                    await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbStatus, ScadaLayout.StatusVersionOffset, 2), 0);
                var paramVer = S7Bytes.GetInt(
                    await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, ScadaLayout.ParamVersionOffset, 2), 0);

                if (statusVer != ScadaLayout.LayoutVersion || paramVer != ScadaLayout.LayoutVersion)
                {
                    _layoutVerified = false;
                    return new ErrorResult(
                        $"Şema sürümü uyuşmuyor. Beklenen {ScadaLayout.LayoutVersion}, DB201: {statusVer}, DB204: {paramVer}. PLC'ye yazılmayacak.");
                }

                _layoutVerified = true;
                return new SuccessResult("Şema sürümü doğrulandı.");
            }
            catch (Exception ex)
            {
                _layoutVerified = false;
                return new ErrorResult($"Şema sürümü okunamadı: {ex.Message}");
            }
        }

        // DB201'in tamamını tek blok okuyup çözülmüş durum nesnesini döner.
        public async Task<IDataResult<ScadaStatus>> ReadStatusAsync()
        {
            try
            {
                var buffer = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbStatus, 0, ScadaLayout.StatusSize);
                var status = ScadaStatus.Parse(buffer);

                if (status.LayoutVersion != ScadaLayout.LayoutVersion)
                {
                    _layoutVerified = false;
                    return new ErrorDataResult<ScadaStatus>(status,
                        $"Şema sürümü uyuşmuyor (DB201: {status.LayoutVersion}, beklenen: {ScadaLayout.LayoutVersion}).");
                }

                return new SuccessDataResult<ScadaStatus>(status);
            }
            catch (Exception ex)
            {
                // Bağlantı koptuysa yeniden bağlanınca CmdId ve sürüm baştan okunur.
                _layoutVerified = false;
                _cmdIdLoaded = false;
                return new ErrorDataResult<ScadaStatus>($"Durum okunamadı: {ex.Message}");
            }
        }

        // SCADA yaşam sinyalini bir artırıp DB200 adres 10'a yazar (saniyede bir çağrılır).
        public async Task<IResult> WriteHeartbeatAsync()
        {
            try
            {
                if (!_layoutVerified)
                {
                    var layout = await VerifyLayoutAsync();
                    if (!layout.IsSuccess)
                        return layout;
                }

                _heartbeat = _heartbeat >= short.MaxValue ? (short)1 : (short)(_heartbeat + 1);

                var data = new byte[2];
                S7Bytes.SetInt(data, 0, _heartbeat);
                await _s7DriverDal.WriteBytesAsync(ScadaLayout.DbCmd, ScadaLayout.CmdHeartbeatOffset, data);

                return new SuccessResult();
            }
            catch (Exception ex)
            {
                _layoutVerified = false;
                return new ErrorResult($"Yaşam sinyali yazılamadı: {ex.Message}");
            }
        }

        // Komutu DB200'e yazar, DB201'den AckCmdId ve AckResult cevabını bekleyip sonucu döner.
        public async Task<IDataResult<ScadaCommandResult>> SendCommandAsync(ScadaCommandCode code, short param1 = 0, short param2 = 0, short param3 = 0)
        {
            await _commandLock.WaitAsync();
            try
            {
                // Her komuttan önce sürüm yeniden doğrulanır; uyuşmuyorsa hiçbir şey yazılmaz.
                var layout = await VerifyLayoutAsync();
                if (!layout.IsSuccess)
                    return new ErrorDataResult<ScadaCommandResult>(layout.Message);

                // Açılışta PLC'de duran CmdId okunur ve oradan devam edilir.
                if (!_cmdIdLoaded)
                {
                    _cmdId = S7Bytes.GetInt(await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbCmd, 0, 2), 0);
                    _cmdIdLoaded = true;
                }

                short cmdId = NextCmdId(_cmdId);

                // PLC'de eski bir cevap aynı numarayla duruyorsa karışmaması için bir numara daha atlanır.
                var oldAck = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbStatus, ScadaLayout.StatusAckOffset, 2);
                if (S7Bytes.GetInt(oldAck, 0) == cmdId)
                    cmdId = NextCmdId(cmdId);

                _cmdId = cmdId;

                // Adres 0–9 arasındaki 10 bayt tek yazma işlemiyle gönderilir.
                var frame = new byte[ScadaLayout.CmdFrameSize];
                S7Bytes.SetInt(frame, 0, cmdId);
                S7Bytes.SetInt(frame, 2, (short)code);
                S7Bytes.SetInt(frame, 4, param1);
                S7Bytes.SetInt(frame, 6, param2);
                S7Bytes.SetInt(frame, 8, param3);
                await _s7DriverDal.WriteBytesAsync(ScadaLayout.DbCmd, 0, frame);

                // AckCmdId kendi numaramıza eşit ve AckResult 0'dan farklı olana kadar beklenir.
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < ScadaLayout.CommandTimeoutMs)
                {
                    await Task.Delay(ScadaLayout.CommandPollMs);

                    var ack = await _s7DriverDal.ReadBytesAsync(
                        ScadaLayout.DbStatus, ScadaLayout.StatusAckOffset, ScadaLayout.StatusAckSize);

                    short ackCmdId = S7Bytes.GetInt(ack, 0);
                    short ackResult = S7Bytes.GetInt(ack, 2);
                    short ackDetail = S7Bytes.GetInt(ack, 4);

                    if (ackCmdId == cmdId && ackResult != 0)
                    {
                        var commandResult = new ScadaCommandResult
                        {
                            CmdId = cmdId,
                            ResultCode = ackResult,
                            Detail = ackDetail,
                            Message = ScadaCommandResult.Describe(ackResult, ackDetail)
                        };

                        if (commandResult.Accepted)
                            return new SuccessDataResult<ScadaCommandResult>(commandResult, commandResult.Message);

                        return new ErrorDataResult<ScadaCommandResult>(commandResult, commandResult.Message);
                    }
                }

                return new ErrorDataResult<ScadaCommandResult>(
                    $"Haberleşme hatası: {code} komutuna {ScadaLayout.CommandTimeoutMs} ms içinde cevap gelmedi.");
            }
            catch (Exception ex)
            {
                _cmdIdLoaded = false;
                return new ErrorDataResult<ScadaCommandResult>($"Komut gönderilemedi: {ex.Message}");
            }
            finally
            {
                _commandLock.Release();
            }
        }

        // Bir sonraki komut numarasını üretir; 32767'den sonra 1'e döner.
        private static short NextCmdId(short current)
        {
            if (current < 0 || current >= short.MaxValue)
                return 1;

            return (short)(current + 1);
        }

        // Eski arayüz: verilen adresten Real sıcaklık değeri okur.
        [LogAspect(typeof(FileLogger))]
        public async Task<IDataResult<float>> ReadTemperatureAsync(int dbNumber, int startByte)
        {
            try
            {
                var rawValue = await _s7DriverDal.ReadTagAsync(DataType.DataBlock, dbNumber, startByte, VarType.Real);
                if (rawValue == null)
                    return new ErrorDataResult<float>("Veri okunamadı");

                float temp = Convert.ToSingle(rawValue);
                return new SuccessDataResult<float>(temp);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<float>($"Hata: {ex.Message}");
            }
        }

        // Eski arayüz: setpoint değerini senkron olarak yazar.
        public IResult WriteSetpoint(int dbNumber, int startByte, float value)
        {
            if (value < 0 || value > 1200)
            {
                return new ErrorResult("Setpoint değeri 0 - 1200 °C sınırları dışında.");
            }

            try
            {
                _s7DriverDal.WriteTagAsync(DataType.DataBlock, dbNumber, startByte, value).GetAwaiter().GetResult();
                return new SuccessResult("Setpoint PLC'ye yazıldı.");
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Yazma Hatası: {ex.Message}");
            }
        }

        // Eski arayüz: setpoint değerini sınır kontrolüyle yazar.
        public async Task<IResult> WriteSetpointAsync(int dbNumber, int startByte, float value)
        {
            if (value < 0 || value > 1200)
            {
                return new ErrorResult("Setpoint değeri 0 - 1200 °C sınırları dışında.");
            }

            try
            {
                await _s7DriverDal.WriteTagAsync(DataType.DataBlock, dbNumber, startByte, value);
                return new SuccessResult("Setpoint PLC'ye yazıldı.");
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Yazma Hatası: {ex.Message}");
            }
        }

        // Eski arayüz: verilen bite darbe gönderir.
        public async Task<IResult> PulseBitAsync(DataType dataType, int dbNumber, int startByte, int bitNumber, int durationMs = 500)
        {
            try
            {
                await _s7DriverDal.PulseBitAsync(dataType, dbNumber, startByte, bitNumber, durationMs);
                return new SuccessResult("Bit tetikleme (Pulse) başarıyla gönderildi.");
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Pulse Hatası: {ex.Message}");
            }
        }
    }
}