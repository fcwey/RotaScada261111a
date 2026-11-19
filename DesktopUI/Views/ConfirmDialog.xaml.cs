using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopUI.Views
{
    // Uygulamanın temasındaki çerçevesiz onay ve bilgi penceresi; Windows'un açık renkli mesaj kutusunun yerini alır
    public partial class ConfirmDialog : Window
    {
        private readonly bool _isQuestion;

        // Kullanıcının verdiği cevap; pencere Esc ile kapatılırsa soru için Hayır, bilgi için Tamam sayılır.
        public MessageBoxResult Result { get; private set; }

        // Pencereyi başlık, metin, düğme türü ve ağırlık seviyesiyle kurar; soruda varsayılan düğme istenen cevaba göre seçilir.
        public ConfirmDialog(string caption, string text, bool isQuestion, MessageBoxImage level, bool defaultYes)
        {
            InitializeComponent();

            _isQuestion = isQuestion;
            Result = isQuestion ? MessageBoxResult.No : MessageBoxResult.OK;

            Title = caption;
            CaptionText.Text = caption;
            MessageText.Text = text;

            LevelStripe.Background = (Brush)FindResource(level switch
            {
                MessageBoxImage.Error => "BadBrush",
                MessageBoxImage.Warning => "WarnBrush",
                _ => "AccentBrush"
            });

            if (isQuestion)
            {
                // Enter varsayılan düğmeye, Esc her zaman Hayır'a basar.
                YesButton.IsDefault = defaultYes;
                NoButton.IsDefault = !defaultYes;
                NoButton.IsCancel = true;
            }
            else
            {
                // Bilgi penceresinde tek düğme vardır.
                YesButton.Visibility = Visibility.Collapsed;
                NoButton.Content = "Tamam";
                NoButton.IsDefault = true;
                NoButton.IsCancel = true;
            }

            Loaded += (_, _) => (isQuestion && defaultYes ? YesButton : NoButton).Focus();
        }

        // Başlık çubuğu olmadığı için kutunun boş bir yerinden tutulup sürüklenmesini sağlar.
        private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        // Evet cevabını kaydedip pencereyi kapatır.
        private void OnYesClick(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            Close();
        }

        // Soruda Hayır, bilgi penceresinde Tamam cevabını kaydedip pencereyi kapatır.
        private void OnNoClick(object sender, RoutedEventArgs e)
        {
            Result = _isQuestion ? MessageBoxResult.No : MessageBoxResult.OK;
            Close();
        }
    }
}