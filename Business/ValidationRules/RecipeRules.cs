using Entities.Concrete;
using Entities.Structures;

namespace Business.ValidationRules
{
    // Reçeteyi PLC'ye gitmeden önce kılavuz bölüm 6.3'teki kurallarla denetler
    public static class RecipeRules
    {
        private const int Vacuum = 1;
        private const int Heating = 2;
        private const int Cooling = 3;

        // Adımları yeniden numaralar, adım sayısını günceller ve adım tipinin kullanmadığı alanları 0 yapar.
        public static void Normalize(Recipe recipe)
        {
            recipe.RecipeName = (recipe.RecipeName ?? string.Empty).Trim();

            var steps = recipe.Steps.OrderBy(s => s.StepNo).ToList();

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                step.StepNo = i + 1;

                if (step.StepType == Vacuum)
                {
                    // Vakum adımı yalnızca vakum seti ve zaman aşımını kullanır.
                    step.TargetTemp = 0;
                    step.StepTime = 0;
                    step.GSoak = 0;
                    step.N2_Flow = 0;
                    step.NH3_Flow = 0;
                    step.CO2_Perc = 0;
                    step.Su_On = 0;
                    step.H2_Auto = 0;
                    step.H2_Set = 0;
                    step.FanSpeed = 0;
                }
                else if (step.StepType == Heating)
                {
                    step.Vac_SetPoint = 0;
                    step.Vac_Timeout = 0;
                }
                else if (step.StepType == Cooling)
                {
                    // Soğutma adımı yalnızca bitiş sıcaklığı, azot ve fan hızını kullanır; amonyak verilmez.
                    step.Vac_SetPoint = 0;
                    step.Vac_Timeout = 0;
                    step.StepTime = 0;
                    step.GSoak = 0;
                    step.NH3_Flow = 0;
                    step.CO2_Perc = 0;
                    step.Su_On = 0;
                    step.H2_Auto = 0;
                    step.H2_Set = 0;
                }
            }

            recipe.TotalStepCount = steps.Count;
        }

        // Reçeteyi kurallara göre denetler; ilk hatayı PLC'nin ret koduyla döner, hata yoksa null döner.
        public static ScadaCommandResult? Validate(Recipe recipe, RecipeLimits limits)
        {
            var steps = recipe.Steps.OrderBy(s => s.StepNo).ToList();

            // Başlık kontrolleri
            if (string.IsNullOrWhiteSpace(recipe.RecipeName))
                return Error(118, 0, "Reçete adı boş olamaz.");

            if (recipe.RecipeName.Length > RecipeCodec.NameMaxLength)
                return Error(118, 0, $"Reçete adı en fazla {RecipeCodec.NameMaxLength} karakter olabilir.");

            if (recipe.RecipeType < 1 || recipe.RecipeType > 3)
                return Error(110, 0);

            if (steps.Count < 1 || steps.Count > RecipeCodec.MaxSteps)
                return Error(111, 0);

            if (recipe.PressureMode != 0 && recipe.PressureMode != 10 && recipe.PressureMode != 20)
                return Error(118, 0, "Basınç modu 0, 10 ya da 20 olmalı.");

            if (!InRange(recipe.PressureSetPoint, 0, short.MaxValue) ||
                !InRange(recipe.PressureModeTime, 0, short.MaxValue) ||
                !InRange(recipe.PressureModeTemp, 0, short.MaxValue) ||
                !InRange(recipe.CO2_Period, 0, short.MaxValue))
                return Error(118, 0, "Başlıktaki basınç, süre ve sıcaklık değerleri 0–32767 aralığında olmalı.");

            if (!InRange(recipe.GsoakTcId, 0, 3))
                return Error(118, 0, "Gsoak termokuplu 0–3 aralığında olmalı.");

            // 112: her adımın tipi geçerli olmalı
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].StepType < Vacuum || steps[i].StepType > Cooling)
                    return Error(112, i + 1);
            }

            // 113: vakum adımları en başta ve ardışık olmalı
            bool nonVacuumSeen = false;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].StepType != Vacuum)
                    nonVacuumSeen = true;
                else if (nonVacuumSeen)
                    return Error(113, i + 1);
            }

            // 114: soğutma tek olmalı ve ondan sonra adım gelmemeli
            int firstCooling = steps.FindIndex(s => s.StepType == Cooling);
            if (firstCooling >= 0 && firstCooling < steps.Count - 1)
                return Error(114, firstCooling + 2);

            // 115: son adım soğutma olmalı
            if (steps[steps.Count - 1].StepType != Cooling)
                return Error(115, steps.Count);

            // 116: en az bir ısıtma adımı olmalı
            if (!steps.Any(s => s.StepType == Heating))
                return Error(116, 0);

            // Adım bazlı kontroller: 117, 118, 119, 120
            for (int i = 0; i < steps.Count; i++)
            {
                var error = ValidateStep(recipe.RecipeType, steps[i], i + 1, limits);
                if (error != null)
                    return error;
            }

            // 121: nitrasyon reçetesinde en az bir vakum adımı olmalı
            if (recipe.RecipeType == 2 && !steps.Any(s => s.StepType == Vacuum))
                return Error(121, 0);

            return null;
        }

        // Tek bir adımı gaz uygunluğu ve değer sınırları açısından denetler.
        private static ScadaCommandResult? ValidateStep(int recipeType, RecipeStep step, int stepNo, RecipeLimits limits)
        {
            // 119: dNH3_Flow her adımda 0 olmalı (donanım yok)
            if (step.dNH3_Flow != 0)
                return Error(119, stepNo);

            if (step.StepType == Vacuum)
            {
                if (!InRange(step.Vac_SetPoint, 0, 1000))
                    return Error(118, stepNo, $"Adım {stepNo}: vakum seti 0–1000 mbar aralığında olmalı.");

                if (!InRange(step.Vac_Timeout, 1, short.MaxValue))
                    return Error(118, stepNo, $"Adım {stepNo}: vakum zaman aşımı en az 1 dk olmalı.");

                return null;
            }

            // 117: gaz reçete tipine uygun olmalı (ısıtma ve soğutma adımlarında)
            bool gasNotAllowed = recipeType switch
            {
                1 => step.NH3_Flow != 0 || step.CO2_Perc != 0 || step.Su_On != 0 || step.H2_Auto != 0,
                2 => step.Su_On != 0,
                3 => step.NH3_Flow != 0 || step.CO2_Perc != 0 || step.H2_Auto != 0,
                _ => false
            };
            if (gasNotAllowed)
                return Error(117, stepNo);

            // 118: değer sınırları
            if (!InRange(step.TargetTemp, 1, limits.MaxPotaTemp))
                return Error(118, stepNo, $"Adım {stepNo}: sıcaklık 1–{limits.MaxPotaTemp} °C aralığında olmalı.");

            if (!InRange(step.N2_Flow, 0, limits.N2MfcMaxLph))
                return Error(118, stepNo, $"Adım {stepNo}: azot debisi 0–{limits.N2MfcMaxLph} L/saat aralığında olmalı.");

            if (!InRange(step.NH3_Flow, 0, limits.NH3MfcMaxLph))
                return Error(118, stepNo, $"Adım {stepNo}: amonyak debisi 0–{limits.NH3MfcMaxLph} L/saat aralığında olmalı.");

            if (step.FanSpeed != 0 && !InRange(step.FanSpeed, limits.FanMinSpeed, 100))
                return Error(118, stepNo, $"Adım {stepNo}: fan hızı 0 ya da {limits.FanMinSpeed}–100 aralığında olmalı.");

            if (step.StepType == Heating)
            {
                if (!InRange(step.StepTime, 1, short.MaxValue))
                    return Error(118, stepNo, $"Adım {stepNo}: adım süresi en az 1 dk olmalı.");

                if (!InRange(step.GSoak, 0, short.MaxValue))
                    return Error(118, stepNo, $"Adım {stepNo}: Gsoak değeri negatif olamaz.");

                if (!InRange(step.CO2_Perc, 0, 100))
                    return Error(118, stepNo, $"Adım {stepNo}: CO2 0–100 aralığında olmalı.");

                if (!InRange(step.Su_On, 0, 1) || !InRange(step.H2_Auto, 0, 1))
                    return Error(118, stepNo, $"Adım {stepNo}: Su_On ve H2_Auto 0 ya da 1 olmalı.");

                // 120: H2_Auto = 1 ise NH3_Flow ≥ 1 ve H2_Set 1–1000 olmalı
                if (step.H2_Auto == 1 && (step.NH3_Flow < 1 || !InRange(step.H2_Set, 1, 1000)))
                    return Error(120, stepNo);
            }

            return null;
        }

        // Değerin verilen aralıkta (sınırlar dahil) olup olmadığını döner.
        private static bool InRange(int value, int min, int max)
        {
            return value >= min && value <= max;
        }

        // PLC ret koduyla hata sonucu üretir; özel metin verilmezse kodun standart açıklaması kullanılır.
        private static ScadaCommandResult Error(short code, int stepNo, string? message = null)
        {
            return new ScadaCommandResult
            {
                ResultCode = code,
                Detail = (short)stepNo,
                Message = message ?? ScadaCommandResult.Describe(code, (short)stepNo)
            };
        }
    }
}