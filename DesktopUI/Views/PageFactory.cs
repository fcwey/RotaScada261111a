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

                default:
                    return (new PlaceholderPageViewModel(title), null);
            }
        }
    }
}