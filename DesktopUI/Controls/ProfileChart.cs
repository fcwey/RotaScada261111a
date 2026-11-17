using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopUI.Controls
{
    // Grafikte bir adımın kapladığı bölge
    public class ProfileBand
    {
        public double X1 { get; set; }
        public double X2 { get; set; }
        public string Label { get; set; } = string.Empty;
        public int Kind { get; set; }       // 1 vakum, 2 ısıtma, 3 soğutma
    }

    // Sıcaklık profili grafiğinin verisi: X saat, Y °C
    public class ProfileChartData
    {
        public List<List<Point>> Theory { get; set; } = new();   // Teorik eğri parçaları
        public List<Point> Actual { get; set; } = new();         // Gerçekleşen pota sıcaklığı
        public List<ProfileBand> Bands { get; set; } = new();    // Adım bölgeleri (isteğe bağlı)
        public double NowX { get; set; } = -1;                   // Şu anın X konumu; eksi ise çizilmez
        public double XMax { get; set; } = 1;
        public double YMax { get; set; } = 100;
        public double TickStart { get; set; }                    // Saat ekseninde 0'ın bulunduğu X
        public double TickEnd { get; set; }                      // Saat yazılarının bittiği X; 0 ise XMax
    }

    // Teorik sıcaklık eğrisini kesikli, gerçekleşen sıcaklığı düz çizgiyle çizen sade grafik
    public class ProfileChart : FrameworkElement
    {
        private const double PadLeft = 46, PadRight = 16, PadTop = 26, PadBottom = 24;

        private static readonly Brush GridBrush = Frozen("#1E2B42");
        private static readonly Brush TextBrush = Frozen("#8FA0BC");
        private static readonly Brush TheoryBrush = Frozen("#D5DEEC");
        private static readonly Brush ActualBrush = Frozen("#3B9BFF");
        private static readonly Brush NowBrush = Frozen("#5FE39A");
        private static readonly Typeface Face = new Typeface("Segoe UI");
        private static readonly Typeface BoldFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(ProfileChartData), typeof(ProfileChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public ProfileChartData? Data
        {
            get => (ProfileChartData?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        // Adım tipinin rengini döner (satırlardaki renklerle aynı).
        public static Color KindColor(int kind)
        {
            return kind switch
            {
                1 => Color.FromRgb(0x5B, 0x8D, 0xEF),
                2 => Color.FromRgb(0xFF, 0x9F, 0x45),
                3 => Color.FromRgb(0x3C, 0xC8, 0xD9),
                _ => Color.FromRgb(0x8F, 0xA0, 0xBC)
            };
        }

        // Adım bölgelerini, ızgarayı, eksen yazılarını, eğrileri ve "şimdi" işaretini çizer.
        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth - PadLeft - PadRight;
            double height = ActualHeight - PadTop - PadBottom;
            var data = Data;

            if (width <= 20 || height <= 20)
                return;

            if (data == null)
            {
                DrawText(dc, "Gösterilecek eğri yok", PadLeft + width / 2, PadTop + height / 2 - 8, TextAlignment.Center, TextBrush, Face);
                return;
            }

            double xMax = Math.Max(data.XMax, 0.01);
            double yMax = Math.Max(data.YMax, 1);

            double MapX(double x) => PadLeft + x / xMax * width;
            Point Map(Point p) => new Point(MapX(p.X), PadTop + height - p.Y / yMax * height);

            DrawBands(dc, data, MapX, height);

            // Yatay ızgara çizgileri ve sıcaklık yazıları
            var gridPen = new Pen(GridBrush, 1);
            for (int i = 0; i <= 3; i++)
            {
                double value = yMax * i / 3;
                double y = PadTop + height - height * i / 3;
                dc.DrawLine(gridPen, new Point(PadLeft, y), new Point(PadLeft + width, y));
                DrawText(dc, value.ToString("0"), PadLeft - 8, y - 8, TextAlignment.Right, TextBrush, Face);
            }
            DrawText(dc, "°C", PadLeft - 8, 0, TextAlignment.Right, TextBrush, Face);

            // Saat yazıları: 0, ısıtmanın (ya da grafiğin) başındadır
            double tickEnd = data.TickEnd > 0 ? data.TickEnd : xMax;
            double span = Math.Max(tickEnd - data.TickStart, 0.01);
            double step = span <= 8 ? 1 : span <= 16 ? 2 : span <= 40 ? 4 : 12;
            for (double hour = 0; hour <= span + 0.001; hour += step)
                DrawText(dc, hour.ToString("0"), MapX(data.TickStart + hour), PadTop + height + 6, TextAlignment.Center, TextBrush, Face);
            DrawText(dc, "saat", PadLeft - 8, PadTop + height + 6, TextAlignment.Right, TextBrush, Face);

            var theoryPen = new Pen(TheoryBrush, 2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            foreach (var segment in data.Theory)
                DrawLine(dc, theoryPen, segment.Select(Map).ToList());

            var actualPen = new Pen(ActualBrush, 3) { LineJoin = PenLineJoin.Round };
            var actual = data.Actual.Select(Map).ToList();
            DrawLine(dc, actualPen, actual);

            if (data.NowX >= 0 && data.NowX <= xMax)
            {
                double x = MapX(data.NowX);
                var nowPen = new Pen(NowBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
                dc.DrawLine(nowPen, new Point(x, PadTop), new Point(x, PadTop + height));
            }

            if (actual.Count > 0)
                dc.DrawEllipse(ActualBrush, null, actual[^1], 4.5, 4.5);
        }

        // Her adımı kendi renginde hafif bir bölge, üstte renkli çizgi ve adım yazısıyla çizer.
        private void DrawBands(DrawingContext dc, ProfileChartData data, Func<double, double> mapX, double height)
        {
            foreach (var band in data.Bands)
            {
                double x1 = mapX(band.X1), x2 = mapX(band.X2);
                if (x2 - x1 < 1)
                    continue;

                Color color = KindColor(band.Kind);
                var fill = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
                var solid = new SolidColorBrush(color);

                dc.DrawRectangle(fill, null, new Rect(x1, PadTop, x2 - x1, height));
                dc.DrawRectangle(solid, null, new Rect(x1 + 1, PadTop - 4, Math.Max(1, x2 - x1 - 2), 3));

                // Yazı bölgeye sığmıyorsa çizilmez.
                var text = Format(band.Label, solid, BoldFace);
                if (text.Width <= x2 - x1 - 4)
                    dc.DrawText(text, new Point((x1 + x2) / 2 - text.Width / 2, PadTop - 21));
            }
        }

        // Verilen noktaları sırayla birleştiren çizgiyi çizer.
        private static void DrawLine(DrawingContext dc, Pen pen, List<Point> points)
        {
            if (points.Count < 2)
                return;

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        // Yazıyı verilen hizalamayla çizer.
        private void DrawText(DrawingContext dc, string text, double x, double y, TextAlignment alignment, Brush brush, Typeface face)
        {
            var formatted = Format(text, brush, face);

            if (alignment == TextAlignment.Right) x -= formatted.Width;
            else if (alignment == TextAlignment.Center) x -= formatted.Width / 2;

            dc.DrawText(formatted, new Point(x, y));
        }

        // Ölçülebilir ve çizilebilir yazı nesnesi üretir.
        private FormattedText Format(string text, Brush brush, Typeface face)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        // Renk kodundan dondurulmuş fırça üretir.
        private static Brush Frozen(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}