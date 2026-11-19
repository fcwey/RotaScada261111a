using DesktopUI.ViewModels;
using System.Windows;
using System.Windows.Media;

namespace DesktopUI.Views
{
    // Ana pencere: çerçevesizdir, üst durum şeridi başlık çubuğu görevi görür
    public partial class ShellWindow : Window
    {
        private static readonly Geometry MaximizeGeometry = Geometry.Parse("M0,0 H10 V10 H0 Z");
        private static readonly Geometry RestoreGeometry = Geometry.Parse("M0,2 H8 V10 H0 Z M2,2 V0 H10 V8 H8");

        // Görünüm modelini pencereye bağlar ve pencere durumunu izlemeye başlar.
        public ShellWindow(ShellViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            StateChanged += (_, _) => ApplyWindowState();
            ApplyWindowState();
        }

        // Ekranı kaplayınca taşan kenar kadar iç boşluk verir ve büyüt simgesini duruma göre değiştirir.
        private void ApplyWindowState()
        {
            if (WindowState == WindowState.Maximized)
            {
                Thickness edge = SystemParameters.WindowResizeBorderThickness;
                RootBorder.Margin = new Thickness(edge.Left + 4, edge.Top + 4, edge.Right + 4, edge.Bottom + 4);
                MaximizeIcon.Data = RestoreGeometry;
            }
            else
            {
                RootBorder.Margin = new Thickness(0);
                MaximizeIcon.Data = MaximizeGeometry;
            }
        }

        // Pencereyi görev çubuğuna küçültür.
        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // Pencereyi ekranı kaplar hale getirir ya da eski boyutuna döndürür.
        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // Pencereyi kapatır.
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}