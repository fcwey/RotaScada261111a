using Core.Constants;
using DesktopUI.Services;
using DesktopUI.ViewModels;

namespace DesktopUI.Views
{
    // Menüden seçilen sayfanın görünümünü ve görünüm modelini üretir.
    public class PageFactory
    {
        private readonly ApiClient _api;
        private readonly SessionService _session;
        private readonly LiveDataService _live;
        private readonly AppSettings _settings;

        // Ayarlar sayfası bir sonraki açılışında bu anahtarlı sekmeyle açılır; bir kez kullanılır.
        public string? PendingSettingsTab { get; set; }

        // Ayarlar sayfasındaki "Programı kapat" düğmesinin çağırdığı işlem; ana pencere verir.
        public Action? ExitAction { get; set; }

        // Sayfaların ihtiyaç duyduğu ortak servisleri alır.
        public PageFactory(ApiClient api, SessionService session, LiveDataService live, AppSettings settings)
        {
            _api = api;
            _session = session;
            _live = live;
            _settings = settings;
        }

        // Anahtarı verilen sayfayı üretir; yapılmamış sayfalar için yer tutucu döner. İkinci değer sayfa kapanırken kapatılacak nesnedir.
        public (object Content, IDisposable? Owner) Create(string key, string title, Action<string> navigate)
        {
            switch (key)
            {
                case "overview":
                    {
                        var viewModel = new OverviewViewModel(_api, _session, _live, _settings);
                        viewModel.NavigateRequested += navigate;
                        return (new OverviewView(viewModel), viewModel);
                    }

                case "program":
                    {
                        var viewModel = new ProgramViewModel(_api, _session, _live, _settings);
                        return (new ProgramView(viewModel), viewModel);
                    }

                case "recipes":
                    {
                        var viewModel = new RecipesViewModel(_api, _session);
                        viewModel.NavigateRequested += navigate;
                        return (new RecipesView(viewModel), viewModel);
                    }

                case "trend":
                    {
                        var viewModel = new TrendViewModel(_api);
                        return (new TrendView(viewModel), viewModel);
                    }

                case "alarms":
                    {
                        var viewModel = new AlarmsViewModel(_api, _live);
                        return (new AlarmsView(viewModel), viewModel);
                    }

                case "manual":
                    {
                        var viewModel = new ManualViewModel(_api, _session, _live, _settings);
                        return (new ManualView(viewModel), viewModel);
                    }

                case "batches":
                    {
                        var viewModel = new BatchesViewModel(_api, _session);
                        return (new BatchesView(viewModel), null);
                    }

                case "maintenance":
                    {
                        var viewModel = new MaintenanceViewModel(_api, _session, _live, _settings);
                        return (new MaintenanceView(viewModel), viewModel);
                    }

                case "settings":
                    return (CreateSettings(), null);

                default:
                    return (new PlaceholderPageViewModel(title), null);
            }
        }

        // Ayarlar sayfasını sekmeleriyle kurar; Bağlantı sekmesi PLC ayar yetkisine; kullanıcı, işlem kaydı ve program sekmeleri Admin'e gösterilir.
        private SettingsHostView CreateSettings()
        {
            var host = new SettingsHostViewModel();

            host.AddTab("parameters", "PLC Parametreleri", () => new SettingsView(new SettingsViewModel(_api, _session)));

            if (_session.HasRole(ParameterPermissions.PlcIpConfigWrite))
                host.AddTab("connection", "Bağlantı", () => new ConnectionView(new ConnectionViewModel(_api, _live, _settings)));

            if (_session.HasRole(PlcPermissions.Admin))
            {
                host.AddTab("users", "Kullanıcılar", () => new UsersView(new UsersViewModel(_api, _session)));
                host.AddTab("audit", "İşlem Kayıtları", () => new AuditLogsView(new AuditLogsViewModel(_api)));
                host.AddTab("application", "Program", () => new ApplicationView(new ApplicationViewModel(_session, _settings, () => ExitAction?.Invoke())));
            }

            if (PendingSettingsTab != null)
            {
                host.SelectTab(PendingSettingsTab);
                PendingSettingsTab = null;
            }

            return new SettingsHostView(host);
        }
    }
}