using DesktopUI.Controls;
using Entities.Concrete;
using System.Text;
using System.Windows;

namespace DesktopUI.Helpers
{
    // Reçete zaman çizgisinde bir adımın yeri ve sıcaklıkları
    public class TimelineStep
    {
        public RecipeStep Step { get; set; } = new RecipeStep();
        public int Kind { get; set; }           // 1 vakum, 2 ısıtma, 3 soğutma
        public double X1 { get; set; }          // Saat
        public double X2 { get; set; }
        public double StartTemp { get; set; }
        public double EndTemp { get; set; }
    }

    // Ölçülen sıcaklığın bir kaydı: ne zaman, hangi adımda, kaç derece
    public class TraceSample
    {
        public DateTime Time { get; set; }
        public int StepNo { get; set; }
        public double Temp { get; set; }
    }

    // Reçeteyi tek bir sıcaklık-zaman çizgisine çevirir; program konumu ile grafik konumu arasında dönüşüm yapar.
    public class RecipeTimeline
    {
        private const double VacuumShare = 0.03;    // Vakum adımının genişliği: toplam ısıtma süresinin oranı
        private const double CoolingShare = 0.15;   // Soğutma adımının genişliği: toplam ısıtma süresinin oranı

        public List<TimelineStep> Steps { get; } = new();
        public double StartTemp { get; private set; }
        public double HeatStart { get; private set; }   // İlk ısıtma adımının başladığı X
        public double HeatEnd { get; private set; }     // Son ısıtma adımının bittiği X
        public double XMax { get; private set; } = 1;
        public double YMax { get; private set; } = 150;

        // Reçetenin adımlarını sırayla zaman çizgisine dizer. Vakum ve soğutmanın süresi bilinmediği için ısıtma süresine oranlı dar bir genişlik verilir.
        public static RecipeTimeline Build(Recipe recipe, float startTemp)
        {
            var ordered = recipe.Steps.OrderBy(s => s.StepNo).ToList();
            double heatHours = ordered.Where(s => s.StepType == 2).Sum(s => Math.Max(0, s.StepTime)) / 60.0;
            double baseHours = heatHours > 0 ? heatHours : 1;

            var timeline = new RecipeTimeline { StartTemp = startTemp };
            double x = 0;
            double temp = startTemp;
            double maxTemp = startTemp;
            bool heatingSeen = false;

            foreach (var step in ordered)
            {
                double width = step.StepType switch
                {
                    1 => baseHours * VacuumShare,
                    2 => Math.Max(0, step.StepTime) / 60.0,
                    3 => baseHours * CoolingShare,
                    _ => 0
                };

                double endTemp = step.StepType == 1 ? temp : step.TargetTemp;

                if (step.StepType == 2 && !heatingSeen)
                {
                    timeline.HeatStart = x;
                    heatingSeen = true;
                }

                timeline.Steps.Add(new TimelineStep
                {
                    Step = step,
                    Kind = step.StepType,
                    X1 = x,
                    X2 = x + width,
                    StartTemp = temp,
                    EndTemp = endTemp
                });

                x += width;
                temp = endTemp;
                maxTemp = Math.Max(maxTemp, endTemp);

                if (step.StepType == 2)
                    timeline.HeatEnd = x;
            }

            if (!heatingSeen)
            {
                timeline.HeatStart = 0;
                timeline.HeatEnd = x;
            }

            timeline.XMax = Math.Max(x, 0.1);

            // Üstteki sıcaklık yazılarına yer kalsın diye eksen en yüksek sıcaklığın biraz üstünde biter.
            timeline.YMax = Math.Max(150, Math.Ceiling(maxTemp * 1.15 / 150.0) * 150);
            return timeline;
        }

        // Program sayfası için grafik verisi: tek eğri, renkli adım bölgeleri ve adım sonu noktaları.
        public ProfileChartData ToChartData()
        {
            var data = CreateBase();

            foreach (var step in Steps)
                data.Bands.Add(new ProfileBand { X1 = step.X1, X2 = step.X2, Label = step.Step.StepNo.ToString(), Kind = step.Kind });

            return data;
        }

        // Ana ekran için sade grafik verisi: tek eğri, adım geçiş noktaları ve üzerine gelince açılan adım bilgileri; bölge şeridi yok.
        public ProfileChartData ToSimpleChartData()
        {
            var data = CreateBase();

            foreach (var step in Steps)
                data.HoverZones.Add(new ProfileHoverZone { X1 = step.X1, X2 = step.X2, Text = DescribeStep(step.Step) });

            return data;
        }

        // Teorik eğrinin baştan verilen konuma kadar olan kısmını döner; program ilerledikçe dolan çizgi budur.
        public List<Point> GetProgressLine(double nowX)
        {
            var line = new List<Point> { new Point(0, StartTemp) };

            foreach (var step in Steps)
            {
                if (nowX >= step.X2)
                {
                    line.Add(new Point(step.X2, step.EndTemp));
                    continue;
                }

                // Konum bu adımın içinde: adımın o noktadaki set sıcaklığına kadar çizilir.
                double width = step.X2 - step.X1;
                double fraction = width > 0 ? Math.Clamp((nowX - step.X1) / width, 0, 1) : 1;
                line.Add(new Point(Math.Max(nowX, step.X1), step.StartTemp + (step.EndTemp - step.StartTemp) * fraction));
                break;
            }

            return line;
        }

        // Ölçülen sıcaklık kayıtlarını zaman çizgisine yerleştirir: her adımın kayıtları o adımın bölgesine, son adımınkiler şu anki konuma kadar yayılır.
        public List<Point> BuildTrace(IReadOnlyList<TraceSample> samples, double nowX)
        {
            // Kayıtlar art arda aynı adımda olanlar bir grup olacak şekilde ayrılır.
            var groups = new List<TraceGroup>();

            foreach (var sample in samples)
            {
                var step = Steps.FirstOrDefault(s => s.Step.StepNo == sample.StepNo);
                if (step == null)
                    continue;

                var last = groups.Count > 0 ? groups[^1] : null;
                if (last != null)
                    last.End = sample.Time;

                if (last != null && last.Step == step)
                {
                    last.Items.Add(sample);
                    continue;
                }

                // Geri bir adıma gidilmişse o adımdan sonra çizilmiş kısım atılır; çizgi kendi üstüne dönmez.
                while (groups.Count > 0 && groups[^1].Step.Step.StepNo >= step.Step.StepNo)
                    groups.RemoveAt(groups.Count - 1);

                var group = new TraceGroup { Step = step, End = sample.Time };
                group.Items.Add(sample);
                groups.Add(group);
            }

            var points = new List<Point>();

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                bool isLast = i == groups.Count - 1;

                // Biten adım bölgesinin sonuna kadar, süren adım programın şu anki yerine kadar yayılır.
                double xEnd = isLast && nowX >= group.Step.X1 ? Math.Min(nowX, group.Step.X2) : group.Step.X2;
                DateTime start = group.Items[0].Time;
                double seconds = (group.End - start).TotalSeconds;

                foreach (var item in group.Items)
                {
                    double fraction = seconds > 0 ? Math.Clamp((item.Time - start).TotalSeconds / seconds, 0, 1) : 1;
                    points.Add(new Point(group.Step.X1 + fraction * (xEnd - group.Step.X1), item.Temp));
                }
            }

            return points;
        }

        // Programın bulunduğu yeri grafik konumuna çevirir: vakumda zaman aşımına, ısıtmada adım süresine, soğutmada sıcaklığın inişine göre.
        public double GetPositionX(int stepNo, double elapsedMinutes, double currentTemp)
        {
            var step = Steps.FirstOrDefault(s => s.Step.StepNo == stepNo);
            if (step == null)
                return -1;

            double fraction = step.Kind switch
            {
                1 => elapsedMinutes / Math.Max(1, step.Step.Vac_Timeout),
                2 => elapsedMinutes / Math.Max(1, step.Step.StepTime),
                3 => step.StartTemp > step.EndTemp ? (step.StartTemp - currentTemp) / (step.StartTemp - step.EndTemp) : 0,
                _ => 0
            };

            return step.X1 + Math.Clamp(fraction, 0, 1) * (step.X2 - step.X1);
        }

        // Gidilecek adım ve dakikanın grafik konumunu döner; soğutmada adımın başıdır.
        public double GetGotoX(int stepNo, int minute)
        {
            var step = Steps.FirstOrDefault(s => s.Step.StepNo == stepNo && s.Kind != 1);
            if (step == null)
                return -1;

            if (step.Kind != 2)
                return step.X1;

            double fraction = minute / (double)Math.Max(1, step.Step.StepTime);
            return step.X1 + Math.Clamp(fraction, 0, 1) * (step.X2 - step.X1);
        }

        // Sürüklenen konumu gidilebilecek bir yere çeker: vakum bölgesinden ilk ısıtma adımının başına, soğutma bölgesinde adımın başına.
        public double Snap(double x)
        {
            var targets = Steps.Where(s => s.Kind != 1).ToList();
            if (targets.Count == 0)
                return -1;

            double clamped = Math.Clamp(x, targets[0].X1, targets[^1].X2);
            var step = FindStep(clamped);

            return step != null && step.Kind == 3 ? step.X1 : clamped;
        }

        // Grafik konumunun hangi adımın kaçıncı dakikası olduğunu döner.
        public (int StepNo, int Minute) Locate(double x)
        {
            var step = FindStep(x);
            if (step == null)
                return (0, 0);

            if (step.Kind != 2)
                return (step.Step.StepNo, 0);

            int minute = (int)Math.Round((x - step.X1) * 60);
            return (step.Step.StepNo, Math.Clamp(minute, 0, Math.Max(0, step.Step.StepTime)));
        }

        // Grafik konumundaki adımın verilerini sürükleme sırasında gösterilen çok satırlı metne çevirir.
        public string Describe(double x)
        {
            var step = FindStep(x);
            if (step == null)
                return string.Empty;

            var located = Locate(x);
            var s = step.Step;
            var sb = new StringBuilder();

            if (step.Kind == 3)
            {
                sb.AppendLine($"Adım {s.StepNo} · Soğutma (baştan)");
                sb.AppendLine($"Bitiş sıcaklığı {s.TargetTemp} °C");
                sb.Append($"Azot {s.N2_Flow} L/h · Fan %{s.FanSpeed}");
                return sb.ToString();
            }

            double fraction = s.StepTime > 0 ? located.Minute / (double)s.StepTime : 1;
            double setTemp = step.StartTemp + (step.EndTemp - step.StartTemp) * fraction;

            sb.AppendLine($"Adım {s.StepNo} · Isıtma");
            sb.AppendLine($"{located.Minute}. dakika (adım süresi {s.StepTime} dk)");
            sb.AppendLine($"Set {setTemp:0} °C · adım hedefi {s.TargetTemp} °C");
            sb.AppendLine(DescribeGases(s));
            sb.Append(DescribeExtras(s));

            return sb.ToString();
        }

        // Bir adımın bütün değerlerini çok satırlı özet olarak döner (fareyle üzerine gelince gösterilir).
        public static string DescribeStep(RecipeStep s)
        {
            var sb = new StringBuilder();

            switch (s.StepType)
            {
                case 1:
                    sb.AppendLine($"Adım {s.StepNo} · Vakum");
                    sb.AppendLine($"Vakum seti {s.Vac_SetPoint} mbar mutlak");
                    sb.Append($"Zaman aşımı {s.Vac_Timeout} dk");
                    break;

                case 2:
                    sb.AppendLine($"Adım {s.StepNo} · Isıtma");
                    sb.AppendLine($"Hedef {s.TargetTemp} °C · süre {s.StepTime} dk");
                    sb.AppendLine(DescribeGases(s));
                    sb.Append(DescribeExtras(s));
                    break;

                case 3:
                    sb.AppendLine($"Adım {s.StepNo} · Soğutma");
                    sb.AppendLine($"Bitiş sıcaklığı {s.TargetTemp} °C");
                    sb.Append($"Azot {s.N2_Flow} L/h · Fan %{s.FanSpeed}");
                    break;

                default:
                    sb.Append($"Adım {s.StepNo}");
                    break;
            }

            return sb.ToString();
        }

        // Eğriyi, adım geçiş noktalarını ve eksen sınırlarını içeren ortak grafik verisini üretir.
        private ProfileChartData CreateBase()
        {
            var data = new ProfileChartData
            {
                XMax = XMax,
                YMax = YMax,
                TickStart = HeatStart,
                TickEnd = HeatEnd
            };

            var line = new List<Point> { new Point(0, StartTemp) };
            data.Markers.Add(new ProfileMarker { X = HeatStart, Y = StartTemp, Label = $"{StartTemp:0} °C" });

            foreach (var step in Steps)
            {
                line.Add(new Point(step.X2, step.EndTemp));

                if (step.Kind != 1)
                    data.Markers.Add(new ProfileMarker { X = step.X2, Y = step.EndTemp, Label = $"{step.EndTemp:0} °C" });
            }

            data.Theory.Add(line);
            return data;
        }

        // Adımın gaz değerlerini tek satırda yazar; verilmeyen gaz yazılmaz.
        private static string DescribeGases(RecipeStep s)
        {
            string gases = $"Azot {s.N2_Flow} L/h";
            if (s.NH3_Flow > 0) gases += $" · Amonyak {s.NH3_Flow} L/h";
            if (s.CO2_Perc > 0) gases += $" · CO₂ %{s.CO2_Perc}";
            if (s.Su_On != 0) gases += " · Su açık";
            return gases;
        }

        // Adımın fan, hidrojen seti ve Gsoak değerlerini tek satırda yazar.
        private static string DescribeExtras(RecipeStep s)
        {
            string extras = $"Fan %{s.FanSpeed}";
            if (s.H2_Auto != 0) extras += $" · H₂ seti %{s.H2_Set / 10.0:0.0}";
            if (s.GSoak > 0) extras += $" · Gsoak {s.GSoak} °C";
            return extras;
        }

        // Konumun içinde bulunduğu ısıtma ya da soğutma adımını döner; sınırdaki konum sonraki adımın başı sayılır.
        private TimelineStep? FindStep(double x)
        {
            var targets = Steps.Where(s => s.Kind != 1).ToList();
            if (targets.Count == 0)
                return null;

            foreach (var step in targets)
            {
                if (x < step.X2 - 1e-9)
                    return step;
            }

            return targets[^1];
        }

        // Art arda aynı adımda alınmış ölçüm kayıtları ve o adımdan çıkılan an
        private class TraceGroup
        {
            public TimelineStep Step { get; set; } = new TimelineStep();
            public List<TraceSample> Items { get; } = new();
            public DateTime End { get; set; }
        }
    }
}