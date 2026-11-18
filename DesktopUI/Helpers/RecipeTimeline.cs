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

    // Reçeteyi tek bir sıcaklık-zaman çizgisine çevirir; program konumu ile grafik konumu arasında dönüşüm yapar.
    public class RecipeTimeline
    {
        public List<TimelineStep> Steps { get; } = new();
        public double StartTemp { get; private set; }
        public double HeatStart { get; private set; }   // İlk ısıtma adımının başladığı X
        public double HeatEnd { get; private set; }     // Son ısıtma adımının bittiği X
        public double XMax { get; private set; } = 1;
        public double YMax { get; private set; } = 150;

        // Reçetenin adımlarını sırayla zaman çizgisine dizer. Vakum ve soğutmanın süresi bilinmediği için sabit genişlik verilir.
        public static RecipeTimeline Build(Recipe recipe, float startTemp)
        {
            var ordered = recipe.Steps.OrderBy(s => s.StepNo).ToList();
            double heatHours = ordered.Where(s => s.StepType == 2).Sum(s => Math.Max(0, s.StepTime)) / 60.0;
            double unit = Math.Max(0.2, heatHours * 0.07);

            var timeline = new RecipeTimeline { StartTemp = startTemp };
            double x = 0;
            double temp = startTemp;
            double maxTemp = startTemp;
            bool heatingSeen = false;

            foreach (var step in ordered)
            {
                double width = step.StepType switch
                {
                    1 => unit,
                    2 => Math.Max(0, step.StepTime) / 60.0,
                    3 => unit * 2,
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
    }
}