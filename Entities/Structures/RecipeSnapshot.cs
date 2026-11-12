using Entities.Concrete;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Entities.Structures
{
    // Reçeteyi şarj kaydında saklanacak metne çevirir ve geri çözer
    public static class RecipeSnapshot
    {
        // Türkçe karakterler veritabanında okunabilir kalsın diye kaçış karakterine çevrilmez.
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Reçetenin başlığını ve sıralı adımlarını metne çevirir.
        public static string Serialize(Recipe recipe)
        {
            var steps = recipe.Steps.OrderBy(s => s.StepNo).ToList();

            var copy = new Recipe
            {
                Id = recipe.Id,
                RecipeType = recipe.RecipeType,
                RecipeName = recipe.RecipeName,
                TotalStepCount = steps.Count,
                PressureSetPoint = recipe.PressureSetPoint,
                PressureMode = recipe.PressureMode,
                PressureModeTime = recipe.PressureModeTime,
                PressureModeTemp = recipe.PressureModeTemp,
                GsoakTcId = recipe.GsoakTcId,
                CO2_Period = recipe.CO2_Period,
                CreatedAt = recipe.CreatedAt,
                IsActive = recipe.IsActive,
                Steps = steps.Select(s => new RecipeStep
                {
                    RecipeId = recipe.Id,
                    StepNo = s.StepNo,
                    StepType = s.StepType,
                    Vac_SetPoint = s.Vac_SetPoint,
                    Vac_Timeout = s.Vac_Timeout,
                    TargetTemp = s.TargetTemp,
                    StepTime = s.StepTime,
                    GSoak = s.GSoak,
                    N2_Flow = s.N2_Flow,
                    NH3_Flow = s.NH3_Flow,
                    dNH3_Flow = s.dNH3_Flow,
                    CO2_Perc = s.CO2_Perc,
                    Su_On = s.Su_On,
                    H2_Auto = s.H2_Auto,
                    H2_Set = s.H2_Set,
                    FanSpeed = s.FanSpeed
                }).ToList()
            };

            return JsonSerializer.Serialize(copy, Options);
        }

        // Saklanan metni reçeteye çevirir; metin bozuksa null döner.
        public static Recipe? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<Recipe>(json, Options);
            }
            catch
            {
                return null;
            }
        }
    }
}