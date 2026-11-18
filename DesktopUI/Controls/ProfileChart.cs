using System.Globalization;
using System.Windows;
using System.Windows.Input;
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

    // Eğri üzerinde işaretlenen nokta (adımın başlangıcı ya da bitişi) ve yanındaki yazı
    public class ProfileMarker
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    // Fareyle üzerine gelindiğinde bilgi kutusu açılan bölge
    public class ProfileHoverZone
    {
        public double X1 { get; set; }
        public double X2 { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    // Sıcaklık profili grafiğinin verisi: X saat, Y °C
    public class ProfileChartData
    {
        public List<List<Point>> Theory { get; set; } = new();          // Teorik eğri parçaları
        public List<Point> Progress { get; set; } = new();              // Teorik eğrinin program tarafından geçilmiş kısmı
        public List<Point> Actual { get; set; } = new();                // Gerçekleşen pota sıcaklığı (tek nokta da olabilir)
        public List<ProfileBand> Bands { get; set; } = new();           // Adım bölgeleri (isteğe bağlı)
        public List<ProfileMarker> Markers { get; set; } = new();       // Adım başlangıç ve bitiş noktaları (isteğe bağlı)
        public List<ProfileHoverZone> HoverZones { get; set; } = new(); // Üzerine gelince bilgi gösterilen bölgeler (isteğe bağlı)
        public double NowX { get; set; } = -1;                          // Şu anın X konumu; eksi ise çizilmez
        public double XMax { get; set; } = 1;
        public double YMax { get; set; } = 100;
        public double TickStart { get; set; }                           // Saat ekseninde 0'ın bulunduğu X
        public double TickEnd { get; set; }                             // Saat yazılarının bittiği X; 0 ise XMax
    }

    // Teorik sıcaklık eğrisini, programın ilerleyişini, fareyle açılan adım bilgisini ve sürüklenebilen hedef çizgisini çizen grafik
    public class ProfileChart : FrameworkElement
    {
        private const double PadLeft = 46, PadRight = 22, PadTop = 30, PadBottom = 24;

        private static readonly Brush GridBrush = Frozen("#1E2B42");
        private static readonly Brush SeparatorBrush = Frozen("#33456A");
        private static readonly Brush ShadeBrush = Frozen("#0AFFFFFF");
        private static readonly Brush HoverShadeBrush = Frozen("#14FFFFFF");
        private static readonly Brush BackBrush = Frozen("#121D31");
        private static readonly Brush InfoBackBrush = Frozen("#F20A111E");
        private static readonly Brush TextBrush = Frozen("#8FA0BC");
        private static readonly Brush StrongTextBrush = Frozen("#E6EDF7");
        private static readonly Brush TheoryBrush = Frozen("#D5DEEC");
        private static readonly Brush ActualBrush = Frozen("#3B9BFF");
        private static readonly Brush NowBrush = Frozen("#5FE39A");
        private static readonly Brush TargetBrush = Frozen("#FFB84A");
        private static readonly Typeface Face = new Typeface("Segoe UI");
        private static readonly Typeface BoldFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        // Farenin grafik üzerindeki son konumu; bilgi kutusu buna göre çizilir.
        private Point _hoverPoint;
        private bool _isHovering;

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(ProfileChartData), typeof(ProfileChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TargetXProperty = DependencyProperty.Register(
            nameof(TargetX), typeof(double), typeof(ProfileChart),
            new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty TargetTextProperty = DependencyProperty.Register(
            nameof(TargetText), typeof(string), typeof(ProfileChart),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CanDragProperty = DependencyProperty.Register(
            nameof(CanDrag), typeof(bool), typeof(ProfileChart),
            new FrameworkPropertyMetadata(false, OnCanDragChanged));

        public ProfileChartData? Data
        {
            get => (ProfileChartData?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        // Sürüklenen hedef çizgisinin X konumu; eksi ise çizgi yoktur.
        public double TargetX
        {
            get => (double)GetValue(TargetXProperty);
            set => SetValue(TargetXProperty, value);
        }

        // Hedef çizgisinin yanındaki kutuda gösterilen çok satırlı açıklama
        public string TargetText
        {
            get => (string)GetValue(TargetTextProperty);
            set => SetValue(TargetTextProperty, value);
        }

        // true ise grafiğe tıklanıp sürüklenerek hedef seçilebilir.
        public bool CanDrag
        {
            get => (bool)GetValue(CanDragProperty);
            set => SetValue(CanDragProperty, value);
        }

        // Adım tipinin rengini döner (satırlardaki şerit renkleriyle aynı).
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

        // Sürükleme açılıp kapanınca fare imlecini değiştirir.
        private static void OnCanDragChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ProfileChart)d).Cursor = (bool)e.NewValue ? Cursors.SizeWE : null;
        }

        // Tıklanan yere hedef çizgisini koyar ve sürüklemeyi başlatır.
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (!CanDrag || Data == null)
                return;

            CaptureMouse();
            MoveTarget(e.GetPosition(this));
            e.Handled = true;
        }

        // Sürükleme sırasında hedef çizgisini taşır; sürükleme yokken farenin altındaki adımın bilgisini gösterir.
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (IsMouseCaptured)
            {
                MoveTarget(e.GetPosition(this));
                return;
            }

            if (Data != null && Data.HoverZones.Count > 0)
            {
                _hoverPoint = e.GetPosition(this);
                _isHovering = true;
                InvalidateVisual();
            }
        }

        // Fare grafikten çıkınca adım bilgisini kapatır.
        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            if (_isHovering)
            {
                _isHovering = false;
                InvalidateVisual();
            }
        }

        // Sürüklemeyi bitirir; hedef çizgisi bırakıldığı yerde kalır.
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (IsMouseCaptured)
                ReleaseMouseCapture();
        }

        // Fare konumunu grafiğin X değerine çevirip hedef olarak yazar.
        private void MoveTarget(Point position)
        {
            var data = Data;
            double width = ActualWidth - PadLeft - PadRight;
            if (data == null || width <= 20)
                return;

            double xMax = Math.Max(data.XMax, 0.01);
            double x = (position.X - PadLeft) / width * xMax;
            SetCurrentValue(TargetXProperty, Math.Clamp(x, 0, xMax));
        }

        // Adım bölgelerini, ızgarayı, eksen yazılarını, eğrileri, ilerlemeyi, adım noktalarını, hedef çizgisini ve fare bilgisini çizer.
        protected override void OnRender(DrawingContext dc)
        {
            // Boş alanlara da tıklanabilsin ve fare izlenebilsin diye görünmez zemin çizilir.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

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
            double MapY(double y) => PadTop + height - y / yMax * height;
            Point Map(Point p) => new Point(MapX(p.X), MapY(p.Y));

            var hoverZone = FindHoverZone(data, xMax, width, height);
            if (hoverZone != null)
                dc.DrawRectangle(HoverShadeBrush, null, new Rect(MapX(hoverZone.X1), PadTop, Math.Max(1, MapX(hoverZone.X2) - MapX(hoverZone.X1)), height));

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

            // Programın geçtiği kısım teorik eğrinin tam üzerinde dolu çizgiyle gösterilir.
            var progress = data.Progress.Select(Map).ToList();
            DrawLine(dc, new Pen(NowBrush, 4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, progress);

            var actualPen = new Pen(ActualBrush, 3) { LineJoin = PenLineJoin.Round };
            var actual = data.Actual.Select(Map).ToList();
            DrawLine(dc, actualPen, actual);

            DrawMarkers(dc, data, Map, width);

            if (data.NowX >= 0 && data.NowX <= xMax)
            {
                double x = MapX(data.NowX);
                dc.DrawLine(new Pen(NowBrush, 2), new Point(x, PadTop), new Point(x, PadTop + height));
            }

            if (progress.Count > 0)
                dc.DrawEllipse(NowBrush, new Pen(BackBrush, 2), progress[^1], 5, 5);

            if (actual.Count > 0)
                dc.DrawEllipse(ActualBrush, new Pen(BackBrush, 2), actual[^1], 5.5, 5.5);

            DrawTarget(dc, MapX, xMax, width, height);

            if (hoverZone != null)
                DrawInfoBox(dc, hoverZone.Text, _hoverPoint.X + 16, _hoverPoint.Y + 12, SeparatorBrush, width, height, true);
        }

        // Farenin altındaki bilgi bölgesini döner; fare grafik alanının dışındaysa ya da sürükleme varsa null döner.
        private ProfileHoverZone? FindHoverZone(ProfileChartData data, double xMax, double width, double height)
        {
            if (!_isHovering || IsMouseCaptured || data.HoverZones.Count == 0)
                return null;

            if (_hoverPoint.Y < PadTop - 8 || _hoverPoint.Y > PadTop + height || _hoverPoint.X < PadLeft || _hoverPoint.X > PadLeft + width)
                return null;

            double x = (_hoverPoint.X - PadLeft) / width * xMax;
            return data.HoverZones.FirstOrDefault(z => x >= z.X1 && x < z.X2) ?? (x >= data.HoverZones[^1].X2 ? data.HoverZones[^1] : null);
        }

        // Her adımı ayırıcı çizgiyle böler, bir atlayarak hafif gölgelendirir; üstte tip renginde ince şerit ve adım numarası çizer.
        private void DrawBands(DrawingContext dc, ProfileChartData data, Func<double, double> mapX, double height)
        {
            var separatorPen = new Pen(SeparatorBrush, 1);

            for (int i = 0; i < data.Bands.Count; i++)
            {
                var band = data.Bands[i];
                double x1 = mapX(band.X1), x2 = mapX(band.X2);
                if (x2 - x1 < 1)
                    continue;

                if (i % 2 == 1)
                    dc.DrawRectangle(ShadeBrush, null, new Rect(x1, PadTop, x2 - x1, height));

                dc.DrawLine(separatorPen, new Point(x1, PadTop - 6), new Point(x1, PadTop + height));
                if (i == data.Bands.Count - 1)
                    dc.DrawLine(separatorPen, new Point(x2, PadTop - 6), new Point(x2, PadTop + height));

                var stripe = new SolidColorBrush(KindColor(band.Kind));
                dc.DrawRectangle(stripe, null, new Rect(x1 + 2, PadTop - 6, Math.Max(1, x2 - x1 - 4), 3));

                // Yazı bölgeye sığmıyorsa çizilmez.
                var text = Format(band.Label, StrongTextBrush, BoldFace, 11);
                if (text.Width <= x2 - x1 - 4)
                    dc.DrawText(text, new Point((x1 + x2) / 2 - text.Width / 2, PadTop - 23));
            }
        }

        // Adımların başlangıç ve bitiş noktalarını daireyle işaretler ve üstlerine sıcaklığı yazar.
        private void DrawMarkers(DrawingContext dc, ProfileChartData data, Func<Point, Point> map, double width)
        {
            var ringPen = new Pen(TheoryBrush, 2);

            foreach (var marker in data.Markers)
            {
                Point p = map(new Point(marker.X, marker.Y));
                dc.DrawEllipse(BackBrush, ringPen, p, 4, 4);

                var text = Format(marker.Label, StrongTextBrush, BoldFace, 11);

                // Yazı noktanın üstüne, grafiğin dışına taşmayacak şekilde yerleştirilir; tepeye yakınsa altına iner.
                double x = Math.Clamp(p.X - text.Width / 2, PadLeft + 2, PadLeft + width - text.Width);
                double y = p.Y - 20 < PadTop ? p.Y + 8 : p.Y - 20;

                dc.DrawRectangle(BackBrush, null, new Rect(x - 2, y, text.Width + 4, text.Height));
                dc.DrawText(text, new Point(x, y));
            }
        }

        // Seçilen hedefi turuncu çizgi, üstünde tutamak ve yanında bilgi kutusuyla çizer.
        private void DrawTarget(DrawingContext dc, Func<double, double> mapX, double xMax, double width, double height)
        {
            if (TargetX < 0)
                return;

            double x = mapX(Math.Min(TargetX, xMax));
            dc.DrawLine(new Pen(TargetBrush, 2), new Point(x, PadTop - 4), new Point(x, PadTop + height));

            var handle = new StreamGeometry();
            using (var context = handle.Open())
            {
                context.BeginFigure(new Point(x - 7, PadTop - 14), true, true);
                context.LineTo(new Point(x + 7, PadTop - 14), true, false);
                context.LineTo(new Point(x, PadTop - 3), true, false);
            }
            handle.Freeze();
            dc.DrawGeometry(TargetBrush, null, handle);

            if (!string.IsNullOrEmpty(TargetText))
                DrawInfoBox(dc, TargetText, x + 12, PadTop + 10, TargetBrush, width, height, false);
        }

        // Çok satırlı bilgiyi çerçeveli kutu içinde çizer; kutu sağa ya da aşağı taşacaksa öbür yana alınır.
        private void DrawInfoBox(DrawingContext dc, string content, double x, double y, Brush border, double width, double height, bool followMouse)
        {
            var text = Format(content, StrongTextBrush, Face, 12);
            text.LineHeight = 18;

            double boxWidth = text.Width + 20;
            double boxHeight = text.Height + 14;

            if (x + boxWidth > PadLeft + width)
                x = x - boxWidth - (followMouse ? 32 : 24);
            x = Math.Max(PadLeft, x);

            if (followMouse && y + boxHeight > PadTop + height)
                y = Math.Max(PadTop, y - boxHeight - 24);

            dc.DrawRoundedRectangle(InfoBackBrush, new Pen(border, 1), new Rect(x, y, boxWidth, boxHeight), 5, 5);
            dc.DrawText(text, new Point(x + 10, y + 7));
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
            var formatted = Format(text, brush, face, 11);

            if (alignment == TextAlignment.Right) x -= formatted.Width;
            else if (alignment == TextAlignment.Center) x -= formatted.Width / 2;

            dc.DrawText(formatted, new Point(x, y));
        }

        // Ölçülebilir ve çizilebilir yazı nesnesi üretir.
        private FormattedText Format(string text, Brush brush, Typeface face, double size)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
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