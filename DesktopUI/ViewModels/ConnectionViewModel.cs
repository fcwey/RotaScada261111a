using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopUI.Services;
using Entities.Dtos;
using System.Net;
using System.Text.Json;
using System.Windows;

namespace DesktopUI.ViewModels
{
    // Ayarlar sayfasındaki Bağlantı sekmesi: sunucu ve PLC bağlantısının durumunu gösterir, PLC'ye verilen adresten yeniden bağlanır
    public class ConnectionViewModel : ObservableObject
    {
        private const string NoValue = "—";

        private readonly ApiClient _api;
        private readonly LiveDataService _live;

        private string _serverState = NoValue;
        private string _plcState = NoValue;
        private string _plcDetail = string.Empty;
        private string _lastDataText = NoValue;
        private string _versionText = NoValue;
        private string _ipAddress = string.Empty;
        private string _message = string.Empty;
        private bool _isBusy;
        private bool _programRunning;

        public string ServerUrl { get; }
        public IAsyncRelayCommand ConnectCommand { get; }

        public string ServerState { get => _serverState; private set => SetProperty(ref _serverState, value); }
        public string PlcState { get => _plcState; private set => SetProperty(ref _plcState, value); }
        public string PlcDetail { get => _plcDetail; private set => SetProperty(ref _plcDetail, value); }
        public string LastDataText { get => _lastDataText; private set => SetProperty(ref _lastDataText, value); }
        public string VersionText { get => _versionText; private set => SetProperty(ref _versionText, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }

        // Bağlanılacak PLC adresi; kullanıcı yazar.
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value))
                    ConnectCommand.NotifyCanExecuteChanged();
            }
        }

        // Bağımlılıkları alır ve bağlan komutunu kurar.
        public ConnectionViewModel(ApiClient api, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _live = live;

            ServerUrl = settings.ServerUrl;
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(IpAddress));
        }

        // Sekme göründüğünde canlı veri olaylarına abone olur.
        public void Start()
        {
            _live.DataReceived += OnDataReceived;
            _live.ConnectionChanged += OnConnectionChanged;
            OnConnectionChanged();
        }

        // Sekme kapanınca abonelikleri bırakır.
        public void Stop()
        {
            _live.DataReceived -= OnDataReceived;
            _live.ConnectionChanged -= OnConnectionChanged;
        }

        // Sunucu bağlantısının durumunu yazar; sunucu yoksa PLC bilgisi de silinir.
        private void OnConnectionChanged()
        {
            ServerState = _live.IsConnected ? "bağlı" : "ulaşılamıyor";

            if (!_live.IsConnected)
            {
                PlcState = NoValue;
                PlcDetail = string.Empty;
                VersionText = NoValue;
            }
        }

        // Gelen canlı veriye göre PLC bağlantı durumunu, son veri saatini ve arayüz sürümünü yazar.
        private void OnDataReceived(ScadaLiveDto data)
        {
            ServerState = "bağlı";
            LastDataText = DateTime.Now.ToString("HH:mm:ss");

            bool plcOk = data.IsConnected && data.Status != null;
            _programRunning = plcOk && data.Status!.SeqState is 20 or 30 or 40 or 45 or 50 or 70;

            if (!plcOk)
            {
                PlcState = "bağlı değil";
                PlcDetail = data.Error ?? "Sunucu PLC'yi okuyamıyor.";
                VersionText = NoValue;
                return;
            }

            PlcState = data.IsStale ? "bağlı, yaşam sinyali değişmiyor" : "bağlı";
            PlcDetail = data.Error ?? string.Empty;
            VersionText = data.Status!.LayoutVersion.ToString();
        }

        // Adresi denetler, onay alır ve sunucudan PLC'ye bu adresten bağlanmasını ister.
        private async Task ConnectAsync()
        {
            string ip = IpAddress.Trim();
            if (!IPAddress.TryParse(ip, out _) || ip.Split('.').Length != 4)
            {
                Message = "Adres 192.168.0.1 biçiminde olmalı.";
                return;
            }

            string warning = _programRunning
                ? "PROGRAM ÇALIŞIYOR. Bağlantı yeniden kurulurken izleme kısa süre kesilir; yanlış adres verilirse fırın ekrandan izlenemez.\n\n"
                : string.Empty;

            var answer = MessageBox.Show(
                $"{warning}Sunucu PLC'ye {ip} adresinden yeniden bağlanacak. Devam edilsin mi?",
                "PLC bağlantısı", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
                return;

            SetBusy(true);
            Message = "Bağlanıyor…";

            var result = await _api.PostAsync<JsonElement>($"api/plc/connect?ipAddress={Uri.EscapeDataString(ip)}");

            Message = result.IsSuccess
                ? $"{DateTime.Now:HH:mm:ss}  {ip} adresine bağlanıldı."
                : $"{DateTime.Now:HH:mm:ss}  Bağlanılamadı: {result.Message}";

            SetBusy(false);
        }

        // Meşgul durumunu ayarlar ve bağlan düğmesinin etkinliğini yeniler.
        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            ConnectCommand.NotifyCanExecuteChanged();
        }
    }
}