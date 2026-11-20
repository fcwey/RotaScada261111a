using DesktopUI.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace DesktopUI.Views
{
    // Ana pencere: başlık çubuğu ve pencere düğmeleri yoktur; hep ekranı kaplar, küçültülemez ve izin verilmeden kapatılamaz
    public partial class ShellWindow : Window
    {
        // Pencerenin kapanmasına izin verir; yalnızca oturum kapatılırken ya da Ayarlar'dan program kapatılırken açılır.
        public bool AllowClose { get; set; }

        // Görünüm modelini pencereye bağlar ve pencere durumunu izlemeye başlar.
        public ShellWindow(ShellViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            StateChanged += (_, _) => KeepMaximized();
            ApplyEdgeMargin();
        }

        // İzin verilmemişse kapanmayı engeller (Alt+F4 ve görev çubuğundan kapatma dahil).
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose)
                e.Cancel = true;

            base.OnClosing(e);
        }

        // Pencere küçültülür ya da eski boyutuna döndürülürse yeniden ekranı kaplar hale getirir.
        private void KeepMaximized()
        {
            if (WindowState != WindowState.Maximized)
            {
                Dispatcher.BeginInvoke(() => WindowState = WindowState.Maximized);
                return;
            }

            ApplyEdgeMargin();
        }

        // Ekranı kaplayan pencerenin ekran dışına taşan kenarı kadar iç boşluk verir.
        private void ApplyEdgeMargin()
        {
            Thickness edge = SystemParameters.WindowResizeBorderThickness;
            RootBorder.Margin = new Thickness(edge.Left + 4, edge.Top + 4, edge.Right + 4, edge.Bottom + 4);
        }
    }
}