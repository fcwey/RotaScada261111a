using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopUI.Controls
{
    // Gaz şeridindeki bir çubuk: gazın verildiği adımın zaman aralığı ve üzerinde yazan değer
    public class GasBar
    {
        public double X1 { get; set; }
        public double X2 { get; set; }
        public string Text { get; set; } = string.Empty;         // Birimiyle değer: "500 L/h", "%40,0"
        public string ShortText { get; set; } = string.Empty;    // Çubuk darsa yazılan birimsiz değer
        public bool IsError { get; set; }
    }

    // Tek bir gazın şeridi
    public class GasLane
    {
        public string Name { get; set; } = string.Empty;
        public Color Color { get; set; }
        public List<GasBar> Bars { get; } = new();
    }

    // Eğrinin altında çizilen bütün şeritler; XMax eğrinin zaman ekseniyle aynıdır
    public class GasLaneData
    {
        public double XMax { get; set; } = 1;
        public List<GasLane> Lanes { get; } = new();
    }

    // Sıcaklık eğrisinin altında, aynı zaman ekseniyle hizalı gaz şeritlerini çizer: hangi adımda hangi gaz, ne kadar
    public class GasLanes : FrameworkElement
    {
        // Sol ve sağ boşluk ProfileChart ile aynı olmalıdır; şeritler eğrinin adım bölgeleriyle alt alta gelir.
        private const double PadLeft = 46, PadRight = 22;
        private const double LaneHeight = 22, LaneGap = 4;

        private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x8F, 0xA0, 0xBC));
        private static readonly Brush ValueBrush = Frozen(Color.FromRgb(0xE6, 0xED, 0xF7));
        private static readonly Brush ErrorBrush = Frozen(Color.FromRgb(0xFF, 0x5A, 0x5F));
        private static readonly Typeface Face = new Typeface("Segoe UI");
        private static readonly Typeface BoldFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(GasLaneData), typeof(GasLanes),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

        public GasLaneData? Data
        {
            get => (GasLaneData?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        // Yüksekliği şerit sayısına göre ister; genişlik bulunduğu yere göre belirlenir.
        protected override Size MeasureOverride(Size availableSize)
        {
            int count = Data?.Lanes.Count ?? 0;
            return new Size(0, count * (LaneHeight + LaneGap));
        }

        // Her şeridin adını sola, çubuklarını zaman eksenindeki yerlerine çizer; değer birimiyle yazılır.
        protected override void OnRender(DrawingContext dc)
        {
            var data = Data;
            double width = ActualWidth - PadLeft - PadRight;

            if (data == null || width <= 20)
                return;

            double xMax = Math.Max(data.XMax, 0.01);
            double y = 0;

            foreach (var lane in data.Lanes)
            {
                var name = Format(lane.Name, LabelBrush, Face, 10.5);
                dc.DrawText(name, new Point(PadLeft - 6 - name.Width, y + (LaneHeight - name.Height) / 2));

                var fill = new SolidColorBrush(lane.Color) { Opacity = 0.18 };
                var pen = new Pen(new SolidColorBrush(lane.Color) { Opacity = 0.6 }, 1);

                foreach (var bar in lane.Bars)
                {
                    double x1 = PadLeft + bar.X1 / xMax * width + 2;
                    double x2 = PadLeft + bar.X2 / xMax * width - 2;
                    if (x2 - x1 < 2)
                        continue;

                    var rect = new Rect(x1, y, x2 - x1, LaneHeight);
                    dc.DrawRoundedRectangle(bar.IsError ? null : fill, bar.IsError ? new Pen(ErrorBrush, 1.5) : pen, rect, 3, 3);

                    // Önce birimli değer denenir; çubuğa sığmazsa birimsiz olan yazılır, o da sığmazsa hiçbiri yazılmaz.
                    var brush = bar.IsError ? ErrorBrush : ValueBrush;
                    var text = Format(bar.Text, brush, BoldFace, 11);
                    if (text.Width > rect.Width - 4 && bar.ShortText.Length > 0)
                        text = Format(bar.ShortText, brush, BoldFace, 11);

                    if (text.Width <= rect.Width - 4)
                        dc.DrawText(text, new Point((x1 + x2) / 2 - text.Width / 2, y + (LaneHeight - text.Height) / 2));
                }

                y += LaneHeight + LaneGap;
            }
        }

        // Ölçülebilir ve çizilebilir yazı nesnesi üretir.
        private FormattedText Format(string text, Brush brush, Typeface face, double size)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        // Renkten dondurulmuş fırça üretir.
        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}