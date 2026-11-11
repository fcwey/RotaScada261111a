using Entities.Concrete;
using System.Text;

namespace Entities.Structures
{
    // Reçeteyi DB202 / DB203 yapısına (58 bayt başlık + 20 x 28 bayt adım = 618 bayt) çevirir ve geri çözer
    public static class RecipeCodec
    {
        public const int TotalSize = 618;
        public const int HeaderSize = 58;
        public const int StepSize = 28;
        public const int MaxSteps = 20;
        public const int NameMaxLength = 30;

        private const int NameOffset = 4;

        // S7 String tek baytlık karakter kullanır; Türkçe karakterler Windows-1254 ile çevrilir.
        private static readonly Encoding TurkishEncoding = CreateTurkishEncoding();

        // Windows-1254 kod sayfasını kullanıma açar ve döner.
        private static Encoding CreateTurkishEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1254);
        }

        // Reçeteyi PLC'ye yazılacak 618 baytlık diziye çevirir; kullanılmayan adımlar 0 kalır.
        public static byte[] Encode(Recipe recipe)
        {
            var steps = recipe.Steps.OrderBy(s => s.StepNo).ToList();
            if (steps.Count > MaxSteps)
                throw new ArgumentException($"Reçetede en fazla {MaxSteps} adım olabilir.");

            var buffer = new byte[TotalSize];

            S7Bytes.SetInt(buffer, 0, ToInt16(recipe.Id, "RecipeID"));
            S7Bytes.SetInt(buffer, 2, ToInt16(recipe.RecipeType, "RecipeType"));
            WriteString(buffer, NameOffset, recipe.RecipeName);
            S7Bytes.SetInt(buffer, 36, ToInt16(steps.Count, "StepCount"));
            S7Bytes.SetInt(buffer, 38, ToInt16(recipe.PressureSetPoint, "PressureSetPoint"));
            S7Bytes.SetInt(buffer, 40, ToInt16(recipe.PressureMode, "PressureMode"));
            S7Bytes.SetInt(buffer, 42, ToInt16(recipe.PressureModeTime, "PressureModeTime"));
            S7Bytes.SetInt(buffer, 44, ToInt16(recipe.PressureModeTemp, "PressureModeTemp"));
            S7Bytes.SetInt(buffer, 46, ToInt16(recipe.GsoakTcId, "GsoakTcId"));
            S7Bytes.SetInt(buffer, 48, ToInt16(recipe.CO2_Period, "CO2_Period"));
            // Adres 50–57 Reserve: 0 bırakılır

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                int offset = HeaderSize + i * StepSize;

                S7Bytes.SetInt(buffer, offset + 0, ToInt16(step.StepType, "StepType"));
                S7Bytes.SetInt(buffer, offset + 2, ToInt16(step.Vac_SetPoint, "Vac_SetPoint"));
                S7Bytes.SetInt(buffer, offset + 4, ToInt16(step.Vac_Timeout, "Vac_Timeout"));
                S7Bytes.SetInt(buffer, offset + 6, ToInt16(step.TargetTemp, "TargetTemp"));
                S7Bytes.SetInt(buffer, offset + 8, ToInt16(step.StepTime, "StepTime"));
                S7Bytes.SetInt(buffer, offset + 10, ToInt16(step.GSoak, "GSoak"));
                S7Bytes.SetInt(buffer, offset + 12, ToInt16(step.N2_Flow, "N2_Flow"));
                S7Bytes.SetInt(buffer, offset + 14, ToInt16(step.NH3_Flow, "NH3_Flow"));
                S7Bytes.SetInt(buffer, offset + 16, ToInt16(step.dNH3_Flow, "dNH3_Flow"));
                S7Bytes.SetInt(buffer, offset + 18, ToInt16(step.CO2_Perc, "CO2_Perc"));
                S7Bytes.SetInt(buffer, offset + 20, ToInt16(step.Su_On, "Su_On"));
                S7Bytes.SetInt(buffer, offset + 22, ToInt16(step.H2_Auto, "H2_Auto"));
                S7Bytes.SetInt(buffer, offset + 24, ToInt16(step.H2_Set, "H2_Set"));
                S7Bytes.SetInt(buffer, offset + 26, ToInt16(step.FanSpeed, "FanSpeed"));
            }

            return buffer;
        }

        // PLC'den okunan 618 baytı reçete nesnesine çözer; yalnızca StepCount kadar adım alınır.
        public static Recipe Decode(byte[] buffer)
        {
            if (buffer == null || buffer.Length < TotalSize)
                throw new ArgumentException($"Reçete verisi {TotalSize} bayt olmalıdır.", nameof(buffer));

            int stepCount = Math.Clamp((int)S7Bytes.GetInt(buffer, 36), 0, MaxSteps);

            var recipe = new Recipe
            {
                Id = S7Bytes.GetInt(buffer, 0),
                RecipeType = S7Bytes.GetInt(buffer, 2),
                RecipeName = ReadString(buffer, NameOffset),
                TotalStepCount = stepCount,
                PressureSetPoint = S7Bytes.GetInt(buffer, 38),
                PressureMode = S7Bytes.GetInt(buffer, 40),
                PressureModeTime = S7Bytes.GetInt(buffer, 42),
                PressureModeTemp = S7Bytes.GetInt(buffer, 44),
                GsoakTcId = S7Bytes.GetInt(buffer, 46),
                CO2_Period = S7Bytes.GetInt(buffer, 48)
            };

            for (int i = 0; i < stepCount; i++)
            {
                int offset = HeaderSize + i * StepSize;

                recipe.Steps.Add(new RecipeStep
                {
                    RecipeId = recipe.Id,
                    StepNo = i + 1,
                    StepType = S7Bytes.GetInt(buffer, offset + 0),
                    Vac_SetPoint = S7Bytes.GetInt(buffer, offset + 2),
                    Vac_Timeout = S7Bytes.GetInt(buffer, offset + 4),
                    TargetTemp = S7Bytes.GetInt(buffer, offset + 6),
                    StepTime = S7Bytes.GetInt(buffer, offset + 8),
                    GSoak = S7Bytes.GetInt(buffer, offset + 10),
                    N2_Flow = S7Bytes.GetInt(buffer, offset + 12),
                    NH3_Flow = S7Bytes.GetInt(buffer, offset + 14),
                    dNH3_Flow = S7Bytes.GetInt(buffer, offset + 16),
                    CO2_Perc = S7Bytes.GetInt(buffer, offset + 18),
                    Su_On = S7Bytes.GetInt(buffer, offset + 20),
                    H2_Auto = S7Bytes.GetInt(buffer, offset + 22),
                    H2_Set = S7Bytes.GetInt(buffer, offset + 24),
                    FanSpeed = S7Bytes.GetInt(buffer, offset + 26)
                });
            }

            return recipe;
        }

        // Metni S7 String[30] olarak yazar: bayt 0 en fazla uzunluk, bayt 1 gerçek uzunluk, sonra karakterler.
        private static void WriteString(byte[] buffer, int offset, string? text)
        {
            byte[] chars = TurkishEncoding.GetBytes(text ?? string.Empty);
            int length = Math.Min(chars.Length, NameMaxLength);

            buffer[offset] = NameMaxLength;
            buffer[offset + 1] = (byte)length;
            Array.Copy(chars, 0, buffer, offset + 2, length);
        }

        // S7 String[30] alanını metne çevirir.
        private static string ReadString(byte[] buffer, int offset)
        {
            int length = Math.Min((int)buffer[offset + 1], NameMaxLength);
            return TurkishEncoding.GetString(buffer, offset + 2, length);
        }

        // Değerin PLC Int aralığına sığdığını doğrulayıp 16 bite çevirir.
        private static short ToInt16(int value, string fieldName)
        {
            if (value < short.MinValue || value > short.MaxValue)
                throw new ArgumentOutOfRangeException(fieldName, $"{fieldName} değeri ({value}) PLC Int aralığının dışında.");

            return (short)value;
        }
    }
}