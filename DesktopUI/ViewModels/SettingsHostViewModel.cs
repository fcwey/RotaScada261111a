using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace DesktopUI.ViewModels
{
    // Ayarlar sayfasındaki bir sekme; içeriği ilk açıldığında üretilir
    public class SettingsTab
    {
        private readonly Func<object> _create;
        private object? _content;

        public string Title { get; }

        // Sekmenin başlığını ve içeriğini üretecek işlemi alır.
        public SettingsTab(string title, Func<object> create)
        {
            Title = title;
            _create = create;
        }

        // Sekmenin içeriğini döner; ilk çağrıda üretir, sonra aynısını verir.
        public object GetContent()
        {
            return _content ??= _create();
        }
    }

    // Ayarlar sayfasının sekmelerini ve seçili sekmenin içeriğini tutar
    public class SettingsHostViewModel : ObservableObject
    {
        private SettingsTab? _selectedTab;
        private object? _currentContent;

        public ObservableCollection<SettingsTab> Tabs { get; } = new();

        public object? CurrentContent { get => _currentContent; private set => SetProperty(ref _currentContent, value); }

        // Seçili sekme; değişince içeriği gösterilir.
        public SettingsTab? SelectedTab
        {
            get => _selectedTab;
            set
            {
                // Liste seçimi boşaltmaya çalışırsa (aynı sekmeye tıklama gibi) mevcut sekme korunur.
                if (value == null)
                {
                    OnPropertyChanged();
                    return;
                }

                if (SetProperty(ref _selectedTab, value))
                    CurrentContent = value.GetContent();
            }
        }

        // Yeni bir sekme ekler; ilk eklenen sekme seçili olur.
        public void AddTab(string title, Func<object> create)
        {
            var tab = new SettingsTab(title, create);
            Tabs.Add(tab);

            if (Tabs.Count == 1)
                SelectedTab = tab;
        }
    }
}