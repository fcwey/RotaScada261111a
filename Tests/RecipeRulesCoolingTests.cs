using Business.ValidationRules;
using Entities.Concrete;
using Xunit;

namespace Tests
{
    // Soğutma adımında amonyak verilmediğini doğrulayan testler
    public class RecipeRulesCoolingTests
    {
        // Vakum, amonyaklı ısıtma ve amonyak girilmiş soğutma adımından oluşan nitrasyon reçetesi üretir.
        private static Recipe BuildRecipe()
        {
            return new Recipe
            {
                Id = 1,
                RecipeType = 2,
                RecipeName = "Test",
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 },
                    new RecipeStep { StepNo = 2, StepType = 2, TargetTemp = 520, StepTime = 120, N2_Flow = 100, NH3_Flow = 800 },
                    new RecipeStep { StepNo = 3, StepType = 3, TargetTemp = 150, N2_Flow = 500, NH3_Flow = 300, FanSpeed = 80 }
                }
            };
        }

        // Soğutma adımına girilen amonyak debisinin 0'a çekildiğini doğrular.
        [Fact]
        public void Normalize_SogutmadaAmonyakSifirlanir()
        {
            var recipe = BuildRecipe();

            RecipeRules.Normalize(recipe);

            Assert.Equal(0, recipe.Steps.Single(s => s.StepNo == 3).NH3_Flow);
        }

        // Soğutma adımında azot debisi ve fan hızının korunduğunu doğrular.
        [Fact]
        public void Normalize_SogutmadaAzotVeFanKorunur()
        {
            var recipe = BuildRecipe();

            RecipeRules.Normalize(recipe);

            var cooling = recipe.Steps.Single(s => s.StepNo == 3);
            Assert.Equal(500, cooling.N2_Flow);
            Assert.Equal(80, cooling.FanSpeed);
            Assert.Equal(150, cooling.TargetTemp);
        }

        // Isıtma adımındaki amonyak debisine dokunulmadığını doğrular.
        [Fact]
        public void Normalize_IsitmadaAmonyakKorunur()
        {
            var recipe = BuildRecipe();

            RecipeRules.Normalize(recipe);

            Assert.Equal(800, recipe.Steps.Single(s => s.StepNo == 2).NH3_Flow);
        }
    }
}