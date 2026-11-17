using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopUI.Controls
{
    // Sıcaklık profili grafiğinin verisi: X saat, Y °C
    public class ProfileChartData
    {
        public List<List<Point>> Theory { get; set; } = new();   // Teorik eğri parçaları
        public List<Point> Actual { get; set; } = new();         // Gerçekleşen pota sıcaklığı
        public double NowX { get; set; }                         // Şu anın X konumu
        public double XMax { get; set; } = 1;
        public double YMax { get; set; } = 100;
    }

    // Teorik sıcaklık eğrisini kesikli, gerçekleşen sıcaklığı düz çizgiyle çizen sade grafik
    public class ProfileChart : FrameworkElement
    {
        private const double Left = 40, Right = 12, Top = 18, Bottom = 26;

        private static readonly Brush GridBrush = Frozen("#1E2B42");
        private static readonly Brush TextBrush = Frozen("#8FA0BC");
        private static readonly Brush TheoryBrush = Frozen("#B4C2D9");
        private static readonly Brush ActualBrush = Frozen("#3B9BFF");
        private static readonly Brush NowBrush = Frozen("#5FE39A");
        private static readonly Typeface Face = new Typeface("Segoe UI");

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(ProfileChartData), typeof(ProfileChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public ProfileChartData? Data
        {
            get => (ProfileChartData?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        // Izgarayı, eksen yazılarını, teorik ve gerçekleşen eğrileri ve "şimdi" işaretini çizer.
        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth - Left - Right;
            double height = ActualHeight - Top - Bottom;
            var data = Data;

            if (width <= 20 || height <= 20)
                return;

            if (data == null)
            {
                DrawText(dc, "Çalışan program yok", Left + width / 2, Top + height / 2 - 8, TextAlignment.Center);
                return;
            }

            double xMax = Math.Max(data.XMax, 0.1);
            double yMax = Math.Max(data.YMax, 1);

            Point Map(Point p) => new Point(Left + p.X / xMax * width, Top + height - p.Y / yMax * height);

            // Yatay ızgara çizgileri ve sıcaklık yazıları
            var gridPen = new Pen(GridBrush, 1);
            for (int i = 0; i <= 3; i++)
            {
                double value = yMax * i / 3;
                double y = Top + height - height * i / 3;
                dc.DrawLine(gridPen, new Point(Left, y), new Point(Left + width, y));
                DrawText(dc, value.ToString("0"), Left - 6, y - 8, TextAlignment.Right);
            }

            // Saat yazıları
            double step = xMax <= 8 ? 1 : xMax <= 16 ? 2 : xMax <= 40 ? 4 : 12;
            for (double hour = 0; hour <= xMax + 0.001; hour += step)
                DrawText(dc, hour.ToString("0"), Left + hour / xMax * width, Top + height + 6, TextAlignment.Center);

            DrawText(dc, "°C", Left - 6, 0, TextAlignment.Right);
            DrawText(dc, "saat", Left + width, Top + height + 6, TextAlignment.Right);

            var theoryPen = new Pen(TheoryBrush, 1.5) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            foreach (var segment in data.Theory)
                DrawLine(dc, theoryPen, segment.Select(Map).ToList());

            var actualPen = new Pen(ActualBrush, 3) { LineJoin = PenLineJoin.Round };
            var actual = data.Actual.Select(Map).ToList();
            DrawLine(dc, actualPen, actual);

            if (data.NowX >= 0 && data.NowX <= xMax)
            {
                double x = Left + data.NowX / xMax * width;
                var nowPen = new Pen(NowBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
                dc.DrawLine(nowPen, new Point(x, Top), new Point(x, Top + height));
            }

            if (actual.Count > 0)
                dc.DrawEllipse(ActualBrush, null, actual[^1], 4.5, 4.5);
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

        // Küçük eksen yazısını verilen hizalamayla çizer.
        private void DrawText(DrawingContext dc, string text, double x, double y, TextAlignment alignment)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Face, 11, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (alignment == TextAlignment.Right) x -= formatted.Width;
            else if (alignment == TextAlignment.Center) x -= formatted.Width / 2;

            dc.DrawText(formatted, new Point(x, y));
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