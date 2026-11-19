using DesktopUI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopUI.Views
{
    // Trend sayfasının görünümü: görünüm modelinin hazırladığı serileri ScottPlot grafiğine çizer
    public partial class TrendView : UserControl
    {
        private readonly TrendViewModel _viewModel;
        private ScottPlot.Plottables.VerticalLine? _cursor;

        // Görünümü kurar, grafiğe koyu temayı uygular ve veri ile fare olaylarına abone olur.
        public TrendView(TrendViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;

            ApplyTheme();

            _viewModel.DataChanged += Render;
            Unloaded += (_, _) => _viewModel.DataChanged -= Render;

            Chart.MouseMove += OnChartMouseMove;
            Chart.MouseLeave += OnChartMouseLeave;

            Render();
        }

        // Grafiğin zemin, eksen ve ızgara renklerini uygulamanın koyu temasına uydurur; alt ekseni tarih-saat yapar.
        private void ApplyTheme()
        {
            var plot = Chart.Plot;

            plot.FigureBackground.Color = ScottPlot.Color.FromHex("#0E1728");
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#0A111E");
            plot.Axes.Color(ScottPlot.Color.FromHex("#B4C2D9"));
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#1E2B42");
            plot.Axes.DateTimeTicksBottom();
            plot.HideLegend();
        }

        // Serileri baştan çizer: sıcaklıklar sol eksene, seçilen grup sağ eksene; sonra görünümü veriye sığdırır.
        private void Render()
        {
            var plot = Chart.Plot;
            plot.Clear();
            _cursor = null;

            bool anyRight = false;
            double firstX = 0;

            foreach (var series in _viewModel.Series)
            {
                if (series.X.Length == 0)
                    continue;

                var scatter = plot.Add.Scatter(series.X, series.Y);
                scatter.Color = ScottPlot.Color.FromHex(series.ColorHex);
                scatter.LineWidth = series.IsDashed ? 1.5f : 2f;
                scatter.MarkerSize = 0;

                if (series.IsDashed)
                    scatter.LinePattern = ScottPlot.LinePattern.Dashed;

                if (series.IsRight)
                {
                    scatter.Axes.YAxis = plot.Axes.Right;
                    anyRight = true;
                }

                firstX = series.X[0];
            }

            plot.Axes.Left.Label.Text = "Sıcaklık (°C)";
            plot.Axes.Right.Label.Text = anyRight ? _viewModel.RightAxisLabel : string.Empty;

            // Görünüm, imleç çizgisi eklenmeden önce sığdırılır; çizgi eksen sınırlarını bozmasın.
            plot.Axes.AutoScale();

            if (_viewModel.Series.Count > 0)
            {
                _cursor = plot.Add.VerticalLine(firstX);
                _cursor.Color = ScottPlot.Color.FromHex("#8FA0BC");
                _cursor.LineWidth = 1;
                _cursor.IsVisible = false;
            }

            Chart.Refresh();
        }

        // Fare grafikte gezerken dikey çizgiyi en yakın kayda taşır ve o andaki değerleri göstergeye yazdırır.
        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            if (_cursor == null)
                return;

            Point position = e.GetPosition(Chart);
            var pixel = new ScottPlot.Pixel(position.X * Chart.DisplayScale, position.Y * Chart.DisplayScale);
            var coordinates = Chart.Plot.GetCoordinates(pixel);

            double? x = _viewModel.ShowValuesAt(coordinates.X);
            if (x == null)
                return;

            _cursor.X = x.Value;
            _cursor.IsVisible = true;
            Chart.Refresh();
        }

        // Fare grafikten çıkınca dikey çizgiyi gizler ve göstergeyi temizler.
        private void OnChartMouseLeave(object sender, MouseEventArgs e)
        {
            _viewModel.ClearValues();

            if (_cursor == null)
                return;

            _cursor.IsVisible = false;
            Chart.Refresh();
        }
    }
}