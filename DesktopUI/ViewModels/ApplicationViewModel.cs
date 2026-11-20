using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopUI.Services;
using System.Reflection;
using System.Windows;

namespace DesktopUI.ViewModels
{
    // Ayarlar sayfasındaki Program sekmesi: ekran programının bilgilerini gösterir ve programı kapatır
    public class ApplicationViewModel : ObservableObject
    {
        private readonly Action _exit;

        public string FurnaceName { get; }
        public string ServerUrl { get; }
        public string UserName { get; }
        public string Version { get; }
        public IRelayCommand ExitCommand { get; }

        // Oturumu, ayarları ve programı kapatacak işlemi alır.
        public ApplicationViewModel(SessionService session, AppSettings settings, Action exit)
        {
            _exit = exit;

            FurnaceName = settings.FurnaceName;
            ServerUrl = settings.ServerUrl;
            UserName = !string.IsNullOrWhiteSpace(session.User?.Name) ? session.User!.Name : session.User?.Email ?? string.Empty;
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";

            ExitCommand = new RelayCommand(Exit);
        }

        // Onay alır ve ekran programını kapatır; sunucu ve PLC çalışmaya devam eder.
        private void Exit()
        {
            var answer = MessageBox.Show(
                "Ekran programı kapatılacak.\n\nFırın, PLC ve SCADA sunucusu çalışmaya devam eder; kayıt sürer. Ancak program yeniden açılana kadar fırın bu bilgisayardan izlenemez ve komut verilemez.\n\nProgramı kapatmak istiyor musunuz?",
                "Programı kapat", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (answer == MessageBoxResult.Yes)
                _exit();
        }
    }
}