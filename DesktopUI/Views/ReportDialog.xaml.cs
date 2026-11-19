using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DesktopUI.Views
{
    // Şarj raporunu gösteren pencere; rapor dosyaya kaydedilebilir ya da yazdırılabilir
    public partial class ReportDialog : Window
    {
        private readonly string _fileName;
        private readonly string _text;

        // Pencereyi kurar; başlık ve önerilen dosya adı şarj kodundan üretilir.
        public ReportDialog(string batchCode, string text)
        {
            InitializeComponent();

            _fileName = string.IsNullOrWhiteSpace(batchCode) ? "Sarj_Raporu" : batchCode;
            _text = text;

            Title = $"Şarj raporu · {batchCode}";
            ReportBox.Text = text;
        }

        // Raporu kullanıcının seçtiği yere metin dosyası olarak kaydeder.
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                FileName = _fileName + ".txt",
                Filter = "Metin dosyası (*.txt)|*.txt",
                DefaultExt = ".txt"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                // Türkçe harfler Not Defteri'nde doğru görünsün diye dosya başına kodlama işareti yazılır.
                File.WriteAllText(dialog.FileName, _text, new UTF8Encoding(true));
                MessageText.Text = $"Kaydedildi: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                MessageText.Text = $"Kaydedilemedi: {ex.Message}";
            }
        }

        // Raporu seçilen yazıcıya, sütunlar hizalı kalacak şekilde küçük sabit genişlikli yazıyla gönderir.
        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var document = new FlowDocument(new Paragraph(new Run(_text)))
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.5,
                    Foreground = Brushes.Black,
                    Background = Brushes.White,
                    PagePadding = new Thickness(40),
                    PageWidth = dialog.PrintableAreaWidth,
                    PageHeight = dialog.PrintableAreaHeight,
                    ColumnWidth = double.PositiveInfinity
                };

                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, Title);
                MessageText.Text = "Yazıcıya gönderildi.";
            }
            catch (Exception ex)
            {
                MessageText.Text = $"Yazdırılamadı: {ex.Message}";
            }
        }

        // Pencereyi kapatır.
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}