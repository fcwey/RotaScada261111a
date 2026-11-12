using Entities.Concrete;
using Entities.Dtos;

namespace Entities.Structures
{
    // Reçetenin ısıtma adımlarından teorik sıcaklık eğrisini hesaplar
    public static class RecipeProfile
    {
        private const int Heating = 2;
        private const int Cooling = 3;

        // Eğriyi ilk ısıtma adımının başından hesaplar: başlangıç noktası ve her ısıtma adımının bittiği nokta.
        public static List<ProfilePointDto> Calculate(Recipe recipe, float startTemp)
        {
            int? firstStep = GetFirstHeatingStepNo(recipe);
            if (firstStep == null)
                return new List<ProfilePointDto>();

            return CalculateFrom(recipe, firstStep.Value, 0, startTemp);
        }

        // Eğriyi verilen ısıtma adımının verilen dakikasından başlatarak hesaplar (adıma atlama için).
        public static List<ProfilePointDto> CalculateFrom(Recipe recipe, int startStepNo, double elapsedMinutes, float batchStartTemp)
        {
            var points = new List<ProfilePointDto>();

            var heatingSteps = recipe.Steps
                .Where(s => s.StepType == Heating)
                .OrderBy(s => s.StepNo)
                .ToList();

            int index = heatingSteps.FindIndex(s => s.StepNo == startStepNo);
            if (index < 0)
                return points;

            var first = heatingSteps[index];

            // Adımın rampası, önceki ısıtma adımının hedefinden (ilk adımda şarjın başlangıç sıcaklığından) başlar.
            float previousTemp = index == 0 ? batchStartTemp : heatingSteps[index - 1].TargetTemp;
            double elapsed = Math.Clamp(elapsedMinutes, 0, first.StepTime);
            double fraction = first.StepTime > 0 ? elapsed / first.StepTime : 1;
            float tempAtStart = (float)(previousTemp + (first.TargetTemp - previousTemp) * fraction);

            points.Add(new ProfilePointDto { Minute = 0, Temp = tempAtStart, StepNo = first.StepNo });

            // Başlanan adımın kalan süresi, sonra izleyen adımların tam süreleri eklenir.
            double minute = first.StepTime - elapsed;
            points.Add(new ProfilePointDto { Minute = minute, Temp = first.TargetTemp, StepNo = first.StepNo });

            for (int i = index + 1; i < heatingSteps.Count; i++)
            {
                minute += heatingSteps[i].StepTime;
                points.Add(new ProfilePointDto { Minute = minute, Temp = heatingSteps[i].TargetTemp, StepNo = heatingSteps[i].StepNo });
            }

            return points;
        }

        // Reçetedeki ilk ısıtma adımının numarasını döner; ısıtma adımı yoksa null döner.
        public static int? GetFirstHeatingStepNo(Recipe recipe)
        {
            var first = recipe.Steps
                .Where(s => s.StepType == Heating)
                .OrderBy(s => s.StepNo)
                .FirstOrDefault();

            return first?.StepNo;
        }

        // Verilen adımın ısıtma adımı olup olmadığını döner.
        public static bool IsHeatingStep(Recipe recipe, int stepNo)
        {
            return recipe.Steps.Any(s => s.StepNo == stepNo && s.StepType == Heating);
        }

        // Soğutma adımının bitiş sıcaklığını döner; soğutma adımı yoksa null döner.
        public static int? GetCoolingEndTemp(Recipe recipe)
        {
            var cooling = recipe.Steps
                .Where(s => s.StepType == Cooling)
                .OrderBy(s => s.StepNo)
                .LastOrDefault();

            return cooling?.TargetTemp;
        }
    }
}