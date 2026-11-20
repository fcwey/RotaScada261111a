using DesktopUI.Controls;
using DesktopUI.ViewModels;
using Entities.Concrete;
using System.Globalization;
using System.Windows.Media;

namespace DesktopUI.Helpers
{
    // Reçete adımlarını ekranda okunan metne ve eğrinin altındaki gaz şeritlerine çevirir; her değer birimiyle yazılır.
    public static class RecipeText
    {
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Hidrojen setini (onda bir yüzde) başında yüzde işaretiyle yazar: %40,0
        public static string FormatH2(int tenths)
        {
            return "%" + (tenths / 10.0).ToString("0.0", Tr);
        }

        // Adımın ne yaptığını tek cümleyle yazar; ısıtmada önceki sıcaklığa göre "git" ya da "bekle" der.
        public static string DescribeMain(RecipeStep step, double previousTemp)
        {
            return step.StepType switch
            {
                1 => $"{step.Vac_SetPoint} mbar mutlak basınca in",
                2 => Math.Abs(step.TargetTemp - previousTemp) < 0.5
                    ? $"{step.TargetTemp} °C sıcaklıkta bekle"
                    : $"{step.TargetTemp} °C sıcaklığa git",
                3 => $"{step.TargetTemp} °C altına soğut",
                _ => $"Adım {step.StepNo}"
            };
        }

        // Adım tipinin büyük harfli adını döner.
        public static string DescribeKind(int stepType)
        {
            return stepType switch
            {
                1 => "VAKUM",
                2 => "ISITMA",
                3 => "SOĞUTMA",
                _ => "ADIM"
            };
        }

        // Okuma tablosundaki satırı adımın değerleriyle doldurur: cümle, süre ve birimli gaz değerleri.
        public static void Fill(StepLine line, RecipeStep step, int recipeType, double previousTemp)
        {
            line.StepNo = step.StepNo;
            line.StepType = step.StepType;
            line.KindText = DescribeKind(step.StepType);
            line.Main = DescribeMain(step, previousTemp);
            line.ShowNitriding = recipeType == 2;
            line.ShowWater = recipeType == 3;

            switch (step.StepType)
            {
                case 1:
                    line.Note = $"zaman aşımı {step.Vac_Timeout} dk";
                    break;

                case 2:
                    line.Duration = $"{step.StepTime} dk";
                    if (step.N2_Flow > 0) line.N2 = $"{step.N2_Flow} L/h";
                    if (step.NH3_Flow > 0) line.NH3 = $"{step.NH3_Flow} L/h";
                    if (step.CO2_Perc > 0) line.CO2 = $"%{step.CO2_Perc}";
                    if (step.Su_On != 0) line.Water = "açık";
                    if (step.H2_Auto != 0) line.H2 = FormatH2(step.H2_Set);
                    if (step.GSoak > 0) line.Gsoak = $"{step.GSoak} °C";
                    line.Fan = $"%{step.FanSpeed}";
                    break;

                case 3:
                    if (step.N2_Flow > 0) line.N2 = $"{step.N2_Flow} L/h";
                    line.Fan = $"%{step.FanSpeed}";
                    break;
            }
        }

        // Adımın gazlarını tek satırda, birimleriyle yazar; verilmeyen gaz yazılmaz.
        public static string DescribeGases(RecipeStep step)
        {
            var parts = new List<string>();

            if (step.N2_Flow > 0) parts.Add($"Azot {step.N2_Flow} L/h");
            if (step.NH3_Flow > 0) parts.Add($"Amonyak {step.NH3_Flow} L/h");
            if (step.CO2_Perc > 0) parts.Add($"CO₂ %{step.CO2_Perc}");
            if (step.Su_On != 0) parts.Add("Su açık");
            if (step.H2_Auto != 0) parts.Add($"H₂ oto {FormatH2(step.H2_Set)}");
            if (step.StepType != 1) parts.Add($"Fan %{step.FanSpeed}");

            return string.Join(" · ", parts);
        }

        // Eğrinin altındaki gaz şeritlerini üretir: her şerit bir gazı, her çubuk o gazın verildiği adımı birimli değeriyle gösterir.
        public static GasLaneData BuildLanes(RecipeTimeline timeline)
        {
            var data = new GasLaneData { XMax = timeline.XMax };

            AddLane(data, timeline, "Azot", "#8FB7FF", s => s.N2_Flow > 0 ? ($"{s.N2_Flow} L/h", s.N2_Flow.ToString()) : null);
            AddLane(data, timeline, "Amonyak", "#C58BFF", s => s.NH3_Flow > 0 ? ($"{s.NH3_Flow} L/h", s.NH3_Flow.ToString()) : null);
            AddLane(data, timeline, "CO₂", "#7FD4C1", s => s.CO2_Perc > 0 ? ($"%{s.CO2_Perc}", s.CO2_Perc.ToString()) : null);
            AddLane(data, timeline, "Su", "#5BC0F0", s => s.Su_On != 0 ? ("açık", "✓") : null);
            AddLane(data, timeline, "H₂ oto", "#FFD23B", s => s.H2_Auto != 0 ? (FormatH2(s.H2_Set), (s.H2_Set / 10.0).ToString("0.0", Tr)) : null);

            return data;
        }

        // Verilen gaz için şerit ekler; hiçbir adımda kullanılmıyorsa şerit eklenmez.
        private static void AddLane(GasLaneData data, RecipeTimeline timeline, string name, string color,
            Func<RecipeStep, (string Text, string Short)?> describe)
        {
            var lane = new GasLane { Name = name, Color = (Color)ColorConverter.ConvertFromString(color) };

            foreach (var step in timeline.Steps.Where(s => s.Kind != 1))
            {
                var value = describe(step.Step);
                if (value == null)
                    continue;

                lane.Bars.Add(new GasBar { X1 = step.X1, X2 = step.X2, Text = value.Value.Text, ShortText = value.Value.Short });
            }

            if (lane.Bars.Count > 0)
                data.Lanes.Add(lane);
        }
    }
}