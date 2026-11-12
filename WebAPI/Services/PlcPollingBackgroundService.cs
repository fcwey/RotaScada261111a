namespace WebAPI.Services
{
    using Business.Abstract;
    using Entities.Concrete;
    using Entities.Dtos;
    using Entities.Structures;
    using global::WebAPI.Hubs;
    using global::WebAPI.Hubs.WebAPI.Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    namespace WebAPI.Services
    {
        public class PlcPollingBackgroundService : BackgroundService
        {
            private const int PollMs = 500;             // DB201 okuma aralığı
            private const int HeartbeatMs = 1000;       // SCADA yaşam sinyali yazma aralığı
            private const int StaleMs = 3000;           // PLC yaşam sinyali bu kadar değişmezse veri eski sayılır
            private const int ReconnectMs = 2000;       // Okuma hatasından sonra bekleme
            private const int ErrorLogSeconds = 60;     // Aynı türden hata en fazla bu sıklıkta log dosyasına yazılır

            private const int DefaultLogActiveSeconds = 10;   // Ayarda yoksa: program çalışırken kayıt aralığı
            private const int DefaultLogIdleSeconds = 60;     // Ayarda yoksa: beklemede kayıt aralığı

            private readonly IPlcService _plcService;
            private readonly IAlarmService _alarmService;
            private readonly IHistorianService _historianService;
            private readonly IBatchService _batchService;
            private readonly IHubContext<ScadaHub> _scadaHubContext;
            private readonly IHubContext<AlarmHub> _alarmHubContext;
            private readonly IConfiguration _configuration;
            private readonly ILogger<PlcPollingBackgroundService> _logger;
            private readonly string _plcIp;

            private readonly Stopwatch _sincePlcHeartbeatChange = new Stopwatch();
            private readonly Stopwatch _sinceScadaHeartbeat = Stopwatch.StartNew();
            private readonly Stopwatch _sinceLog = new Stopwatch();
            private short _lastPlcHeartbeat;
            private bool _hasPlcHeartbeat;

            // PLC döngüsünün okuduğu son geçerli durum; veritabanı döngüsü bunu işler. Geçerli veri yoksa null.
            private volatile ScadaStatus? _latestStatus;
            private volatile bool _alarmsPublished;

            // Log dosyası için: son yazılan bağlantı durumu ve hata türlerinin son yazılma zamanları
            private string _lastLinkState = string.Empty;
            private readonly ConcurrentDictionary<string, DateTime> _lastErrorLog = new();

            // Servis bağımlılıklarını alır ve PLC IP adresini ayarlardan okur.
            public PlcPollingBackgroundService(
                IPlcService plcService,
                IAlarmService alarmService,
                IHistorianService historianService,
                IBatchService batchService,
                IHubContext<ScadaHub> scadaHubContext,
                IHubContext<AlarmHub> alarmHubContext,
                IConfiguration configuration,
                ILogger<PlcPollingBackgroundService> logger)
            {
                _plcService = plcService;
                _alarmService = alarmService;
                _historianService = historianService;
                _batchService = batchService;
                _scadaHubContext = scadaHubContext;
                _alarmHubContext = alarmHubContext;
                _configuration = configuration;
                _logger = logger;
                _plcIp = configuration["Plc:IpAddress"] ?? "192.168.0.1";
            }

            // PLC döngüsünü ve veritabanı döngüsünü birbirinden bağımsız olarak çalıştırır.
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _logger.LogInformation("PLC izleme başladı. Hedef adres: {PlcIp}", _plcIp);

                await Task.WhenAll(
                    RunPlcLoopAsync(stoppingToken),
                    RunDatabaseLoopAsync(stoppingToken));
            }

            // PLC döngüsü: DB201'i okur, ekrana yayınlar ve yaşam sinyalini yazar; veritabanına hiç dokunmaz.
            private async Task RunPlcLoopAsync(CancellationToken stoppingToken)
            {
                await _plcService.ConnectPlcAsync(_plcIp);

                while (!stoppingToken.IsCancellationRequested)
                {
                    int delayMs = PollMs;

                    try
                    {
                        var statusResult = await _plcService.ReadStatusAsync();

                        if (statusResult.Data == null)
                        {
                            // Okuma yapılamadı: bağlantı yok sayılır, sürücü bir sonraki okumada yeniden bağlanmayı dener.
                            _latestStatus = null;
                            _hasPlcHeartbeat = false;
                            _alarmsPublished = false;
                            delayMs = ReconnectMs;

                            LogLinkState("KOPUK", statusResult.Message);

                            await PublishAsync(new ScadaLiveDto
                            {
                                IsConnected = false,
                                IsStale = true,
                                Error = statusResult.Message
                            }, stoppingToken);
                        }
                        else
                        {
                            var status = statusResult.Data;
                            bool versionOk = statusResult.IsSuccess;
                            bool isStale = UpdateStaleState(status.PlcHeartbeat);

                            if (!versionOk)
                                LogLinkState("SÜRÜM UYUŞMUYOR", statusResult.Message);
                            else if (isStale)
                                LogLinkState("VERİ ESKİ", "PLC yaşam sinyali 3 saniyedir değişmiyor; PLC STOP'ta olabilir.");
                            else
                                LogLinkState("BAĞLI", null);

                            await PublishAsync(new ScadaLiveDto
                            {
                                IsConnected = true,
                                IsStale = isStale,
                                Error = versionOk ? null : statusResult.Message,
                                Status = status
                            }, stoppingToken);

                            // Sürüm uyuşmuyorsa ya da veri eskiyse veritabanı döngüsüne veri verilmez.
                            _latestStatus = (versionOk && !isStale) ? status : null;

                            if (versionOk)
                                await SendHeartbeatIfDueAsync();
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await ReportErrorAsync("plc", $"PLC döngüsü hatası: {ex.Message}", ex, stoppingToken);
                    }

                    await DelayAsync(delayMs, stoppingToken);
                }
            }

            // Veritabanı döngüsü: son geçerli durumdan alarmları, şarj kaydını ve ölçümleri işler.
            private async Task RunDatabaseLoopAsync(CancellationToken stoppingToken)
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var status = _latestStatus;

                    if (status != null)
                    {
                        try
                        {
                            await ProcessAlarmsAsync(status, stoppingToken);
                            await TrackBatchAsync(status, stoppingToken);
                            await LogHistoryIfDueAsync(status, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Veritabanı hatası yalnızca bu döngüyü etkiler; canlı veri akmaya devam eder.
                            await ReportErrorAsync("db", $"Veritabanı kaydı yapılamadı: {ex.Message}", ex, stoppingToken);
                        }
                    }

                    await DelayAsync(PollMs, stoppingToken);
                }
            }

            // Bağlantı durumu değiştiyse log dosyasına bir satır yazar; aynı durum sürerken tekrar yazmaz.
            private void LogLinkState(string state, string? detail)
            {
                if (state == _lastLinkState)
                    return;

                _lastLinkState = state;

                if (state == "BAĞLI")
                    _logger.LogInformation("PLC durumu: {State}", state);
                else
                    _logger.LogWarning("PLC durumu: {State} ({Detail})", state, detail);
            }

            // Hatayı ekranlara gönderir ve aynı türden hatayı dakikada en fazla bir kez log dosyasına yazar.
            private async Task ReportErrorAsync(string key, string message, Exception? exception, CancellationToken token)
            {
                DateTime now = DateTime.Now;
                bool shouldLog = !_lastErrorLog.TryGetValue(key, out var last) || (now - last).TotalSeconds >= ErrorLogSeconds;

                if (shouldLog)
                {
                    _lastErrorLog[key] = now;
                    _logger.LogError(exception, "{Message}", message);
                }

                try
                {
                    await _scadaHubContext.Clients.All.SendAsync("ReceiveScadaError", message, token);
                }
                catch
                {
                    // Hata bildirimi gönderilemese de döngü çalışmaya devam etmelidir.
                }
            }

            // Verilen süre kadar bekler; uygulama kapanırken beklemeyi sessizce keser.
            private static async Task DelayAsync(int delayMs, CancellationToken token)
            {
                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    // Uygulama kapanıyor.
                }
            }

            // Canlı veri paketini ScadaHub üzerinden bütün ekranlara gönderir.
            private Task PublishAsync(ScadaLiveDto dto, CancellationToken token)
            {
                return _scadaHubContext.Clients.All.SendAsync("ReceiveScadaData", dto, token);
            }

            // PLC yaşam sinyalinin değişimini izler; 3 sn değişmediyse verinin eski olduğunu döner.
            private bool UpdateStaleState(short plcHeartbeat)
            {
                if (!_hasPlcHeartbeat || plcHeartbeat != _lastPlcHeartbeat)
                {
                    _lastPlcHeartbeat = plcHeartbeat;
                    _hasPlcHeartbeat = true;
                    _sincePlcHeartbeatChange.Restart();
                    return false;
                }

                return _sincePlcHeartbeatChange.ElapsedMilliseconds >= StaleMs;
            }

            // Son yazmadan beri 1 sn geçtiyse SCADA yaşam sinyalini DB200'e yazar.
            private async Task SendHeartbeatIfDueAsync()
            {
                if (_sinceScadaHeartbeat.ElapsedMilliseconds < HeartbeatMs)
                    return;

                _sinceScadaHeartbeat.Restart();
                await _plcService.WriteHeartbeatAsync();
            }

            // Alarm bitlerini işler; değişiklik olduysa aktif alarm listesini AlarmHub'dan yayınlar.
            private async Task ProcessAlarmsAsync(ScadaStatus status, CancellationToken token)
            {
                var result = await _alarmService.ProcessAlarmWordsAsync(status.AlarmActive, status.AlarmUnack);
                if (!result.IsSuccess)
                {
                    await ReportErrorAsync("alarm", result.Message, null, token);
                    return;
                }

                // İlk başarılı okumada ve her değişiklikte liste yeniden gönderilir.
                if (result.Data || !_alarmsPublished)
                {
                    var activeAlarms = await _alarmService.GetActiveAlarmsAsync();
                    if (activeAlarms.IsSuccess && activeAlarms.Data != null)
                    {
                        await _alarmHubContext.Clients.All.SendAsync("ReceiveActiveAlarms", activeAlarms.Data, token);
                        _alarmsPublished = true;
                    }
                }
            }

            // Sekans durumunu şarj servisine bildirir; şarj kapandıysa bunu log dosyasına yazar.
            private async Task TrackBatchAsync(ScadaStatus status, CancellationToken token)
            {
                var result = await _batchService.TrackSequenceAsync(status.SeqState);
                if (!result.IsSuccess)
                {
                    await ReportErrorAsync("batch", result.Message, null, token);
                }
                else if (!string.IsNullOrEmpty(result.Message))
                {
                    _logger.LogInformation("{Message}", result.Message);
                }
            }

            // Ayardaki aralığa göre ölçümleri historian tablosuna yazar; beklemede aralık 0 ise hiç yazmaz.
            private async Task LogHistoryIfDueAsync(ScadaStatus status, CancellationToken token)
            {
                bool isIdle = status.SeqState == 0;

                // Aralıklar her seferinde ayardan okunur; appsettings değişince yeniden başlatmadan geçerli olur.
                int intervalSeconds = isIdle
                    ? ReadSeconds("Historian:IdleSeconds", DefaultLogIdleSeconds, 0, 86400)
                    : ReadSeconds("Historian:ActiveSeconds", DefaultLogActiveSeconds, 1, 3600);

                if (intervalSeconds == 0)
                    return;

                if (_sinceLog.IsRunning && _sinceLog.ElapsedMilliseconds < intervalSeconds * 1000L)
                    return;

                _sinceLog.Restart();

                try
                {
                    await _historianService.LogProcessDataAsync(new ProcessDataLog
                    {
                        Timestamp = DateTime.Now,
                        SeqState = status.SeqState,
                        ActiveStep = status.ActiveStep,
                        TempPota = status.TempPota,
                        TempAlt = status.TempAlt,
                        TempUst = status.TempUst,
                        TempPotaAlt = status.TempPotaAlt,
                        TempPotaUst = status.TempPotaUst,
                        TempMalzeme = status.TempMalzeme,
                        TempSet = status.TempSet,
                        Pressure = status.Pressure,
                        PressureSet = status.PressureSet,
                        N2FlowAct = status.N2FlowAct,
                        NH3FlowAct = status.NH3FlowAct,
                        H2Act = status.H2Act,
                        Kn = status.Kn,
                        HeatOutAlt = status.HeatOutAlt,
                        HeatOutUst = status.HeatOutUst,
                        FanSpeedAct = status.FanSpeedAct
                    });
                }
                catch (Exception ex)
                {
                    await ReportErrorAsync("historian", $"Historian kaydı yazılamadı: {ex.Message}", ex, token);
                }
            }

            // Ayardan saniye değeri okur; yoksa ya da sayı değilse varsayılanı kullanır, sınırların dışındaysa sınıra çeker.
            private int ReadSeconds(string key, int defaultValue, int min, int max)
            {
                if (!int.TryParse(_configuration[key], out int value))
                    return defaultValue;

                return Math.Clamp(value, min, max);
            }
        }
    }
}