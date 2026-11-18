using System.Globalization;
using System.Windows;

namespace DesktopUI.Views
{
    // Program başlatılırken şarj ağırlığını ve notu soran pencere
    public partial class StartDialog : Window
    {
        private const float MaxLoadKg = 100000f;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        public float LoadKg { get; private set; }
        public string Note { get; private set; } = string.Empty;

        // Başlatılacak reçetenin adını başlığa yazar ve imleci ağırlık kutusuna koyar.
        public StartDialog(string recipeName)
        {
            InitializeComponent();
            TitleText.Text = $"'{recipeName}' reçetesiyle program başlatılacak";
            Loaded += (_, _) => WeightBox.Focus();
        }

        // Ağırlığı denetler; geçerliyse değerleri saklayıp pencereyi onayla kapatır.
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // Hem virgül hem nokta ondalık ayırıcı olarak kabul edilir.
            string text = WeightBox.Text.Trim().Replace('.', ',');

            if (text.Length == 0)
            {
                ShowError("Şarj ağırlığını girin.");
                return;
            }

            if (!float.TryParse(text, NumberStyles.Float, Tr, out float kg) || kg <= 0 || kg > MaxLoadKg)
            {
                ShowError($"Şarj ağırlığı 0'dan büyük, en fazla {MaxLoadKg:0} kg olan bir sayı olmalı.");
                return;
            }

            LoadKg = kg;
            Note = NoteBox.Text.Trim();
            DialogResult = true;
        }

        // Hata mesajını gösterir ve imleci ağırlık kutusuna döndürür.
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            WeightBox.Focus();
            WeightBox.SelectAll();
        }
    }
}