using DesktopUI.Views;
using System.Windows;

namespace DesktopUI.ViewModels
{
    // Görünüm modellerindeki MessageBox.Show çağrılarını uygulamanın koyu onay penceresine yönlendirir.
    // Bu sınıf bilerek Windows'un MessageBox sınıfıyla aynı adı taşır: C# aynı ad alanındaki (DesktopUI.ViewModels)
    // sınıfı "using System.Windows" ile gelenden önce bulduğu için, görünüm modellerindeki mevcut çağrılar
    // değiştirilmeden bu pencereyi açar. Windows'un kendi kutusu gerekirse System.Windows.MessageBox yazılmalıdır.
    public static class MessageBox
    {
        // Soruyu ya da bilgiyi koyu pencerede gösterir ve cevabı döner; düğme türü Evet/Hayır değilse tek "Tamam" düğmesi çıkar.
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage image,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            bool isQuestion = button == MessageBoxButton.YesNo || button == MessageBoxButton.YesNoCancel;
            bool defaultYes = defaultResult == MessageBoxResult.Yes;

            var dialog = new ConfirmDialog(caption, text, isQuestion, image, defaultYes);

            // Pencere, o anda etkin olan pencerenin (ana pencere ya da açık bir iletişim kutusu) üstünde ortalanır.
            var app = Application.Current;
            Window? owner = app?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded) ?? app?.MainWindow;

            if (owner != null && owner.IsLoaded && !ReferenceEquals(owner, dialog))
                dialog.Owner = owner;
            else
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}