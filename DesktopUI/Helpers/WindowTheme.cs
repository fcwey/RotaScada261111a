using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopUI.Helpers
{
    // Pencerelerin Windows başlık çubuğunu uygulamanın koyu temasına uydurur
    public static class WindowTheme
    {
        private const int DarkModeAttribute = 20;           // Windows 10 sürüm 2004 ve sonrası, Windows 11
        private const int DarkModeAttributeOld = 19;        // Windows 10 sürüm 1809–1909
        private const uint FrameChangedFlags = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;   // Boyut, konum, sıra ve etkinlik değişmeden çerçeveyi yeniden çiz

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        // Pencerenin başlık çubuğunu koyu yapar; desteklemeyen Windows sürümlerinde hiçbir şey yapmaz.
        public static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    return;

                int enabled = 1;
                if (DwmSetWindowAttribute(handle, DarkModeAttribute, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, DarkModeAttributeOld, ref enabled, sizeof(int));

                // Bazı sürümlerde renk ancak çerçeve yeniden çizilince değişir.
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, FrameChangedFlags);
            }
            catch
            {
                // Başlık çubuğu rengi görseldir; ayarlanamaması programı etkilememelidir.
            }
        }
    }
}