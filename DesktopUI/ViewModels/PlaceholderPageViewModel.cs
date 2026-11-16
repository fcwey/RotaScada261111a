namespace DesktopUI.ViewModels
{
    // Henüz yapılmamış sayfaların yerini tutan boş sayfa
    public class PlaceholderPageViewModel
    {
        public string Title { get; }

        // Sayfa başlığını alır.
        public PlaceholderPageViewModel(string title)
        {
            Title = title;
        }
    }
}