using Entities.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System.Windows;

namespace DesktopUI.Services
{
    // Sunucudaki SignalR hub'larına bağlanır, canlı veriyi ve alarm listesini ekrana aktarır; bağlantı koparsa kendiliğinden yeniden bağlanır.
    public class LiveDataService
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

        private readonly string _serverUrl;
        private HubConnection? _scadaHub;
        private HubConnection? _alarmHub;
        private CancellationTokenSource? _cancellation;
        private string? _token;

        // Olaylar arayüz iş parçacığında tetiklenir.
        public event Action<ScadaLiveDto>? DataReceived;
        public event Action<List<ActiveAlarmDto>>? AlarmsReceived;
        public event Action? ProfileChanged;
        public event Action? ConnectionChanged;

        public bool IsConnected => _scadaHub?.State == HubConnectionState.Connected;

        // Sunucu adresini alır.
        public LiveDataService(string serverUrl)
        {
            _serverUrl = serverUrl.TrimEnd('/');
        }

        // Verilen token'la iki hub'a bağlanmaya başlar; sunucu kapalıysa açılana kadar dener.
        public void Start(string token)
        {
            _token = token;
            _cancellation = new CancellationTokenSource();

            _scadaHub = Build("/scadaHub");
            _scadaHub.On<ScadaLiveDto>("ReceiveScadaData", dto => Post(() => DataReceived?.Invoke(dto)));
            _scadaHub.On("ReceiveProfileChanged", () => Post(() => ProfileChanged?.Invoke()));

            _alarmHub = Build("/alarmHub");
            _alarmHub.On<List<ActiveAlarmDto>>("ReceiveActiveAlarms", alarms => Post(() => AlarmsReceived?.Invoke(alarms)));

            _ = ConnectLoopAsync(_scadaHub, _cancellation.Token);
            _ = ConnectLoopAsync(_alarmHub, _cancellation.Token);
        }

        // Bağlantıları kapatır.
        public async Task StopAsync()
        {
            _cancellation?.Cancel();

            foreach (var hub in new[] { _scadaHub, _alarmHub })
            {
                if (hub == null)
                    continue;

                try
                {
                    await hub.DisposeAsync();
                }
                catch
                {
                    // Kapanırken oluşan bağlantı hataları önemsizdir.
                }
            }

            _scadaHub = null;
            _alarmHub = null;
        }

        // Verilen hub adresi için token'lı ve kendiliğinden yeniden bağlanan bağlantıyı kurar.
        private HubConnection Build(string hubPath)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(_serverUrl + hubPath, options => options.AccessTokenProvider = () => Task.FromResult(_token))
                .WithAutomaticReconnect(new ForeverRetryPolicy(RetryDelay))
                .Build();

            connection.Reconnecting += _ => { Post(() => ConnectionChanged?.Invoke()); return Task.CompletedTask; };
            connection.Reconnected += _ => { Post(() => ConnectionChanged?.Invoke()); return Task.CompletedTask; };
            connection.Closed += _ => { Post(() => ConnectionChanged?.Invoke()); return Task.CompletedTask; };

            return connection;
        }

        // İlk bağlantı kurulana kadar belirli aralıkla yeniden dener.
        private async Task ConnectLoopAsync(HubConnection connection, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await connection.StartAsync(token);
                    Post(() => ConnectionChanged?.Invoke());
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Sunucu kapalı ya da ulaşılamıyor; biraz bekleyip yeniden denenir.
                }

                try
                {
                    await Task.Delay(RetryDelay, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // Verilen işi arayüz iş parçacığında çalıştırır.
        private static void Post(Action action)
        {
            Application.Current?.Dispatcher.BeginInvoke(action);
        }
    }

    // Bağlantı koptuğunda süresiz olarak, sabit aralıkla yeniden bağlanmayı deneyen kural
    public class ForeverRetryPolicy : IRetryPolicy
    {
        private readonly TimeSpan _delay;

        // Denemeler arasındaki bekleme süresini alır.
        public ForeverRetryPolicy(TimeSpan delay)
        {
            _delay = delay;
        }

        // Her denemeden sonra aynı süre beklenmesini söyler; hiç vazgeçmez.
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            return _delay;
        }
    }
}