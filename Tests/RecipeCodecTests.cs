using Entities.Concrete;
using Entities.Structures;
using System.Buffers.Binary;
using Xunit;

namespace Tests
{
    // Reçetenin DB202 / DB203 yapısına çevrilmesini ve geri çözülmesini doğrulayan testler
    public class RecipeCodecTests
    {
        // Verilen adresteki Int değeri okur.
        private static short ReadInt(byte[] buffer, int address)
        {
            return BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(address, 2));
        }

        // Başlık alanları dolu, üç adımlı örnek bir nitrasyon reçetesi üretir.
        private static Recipe BuildRecipe()
        {
            return new Recipe
            {
                Id = 7,
                RecipeType = 2,
                RecipeName = "Deneme",
                PressureSetPoint = 20,
                PressureMode = 10,
                PressureModeTime = 15,
                PressureModeTemp = 300,
                GsoakTcId = 1,
                CO2_Period = 5,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { StepNo = 1, StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 },
                    new RecipeStep
                    {
                        StepNo = 2, StepType = 2, Vac_SetPoint = 11, Vac_Timeout = 12, TargetTemp = 13, StepTime = 14,
                        GSoak = 15, N2_Flow = 16, NH3_Flow = 17, dNH3_Flow = 18, CO2_Perc = 19, Su_On = 1,
                        H2_Auto = 1, H2_Set = 325, FanSpeed = 50
                    },
                    new RecipeStep { StepNo = 3, StepType = 3, TargetTemp = 150, N2_Flow = 500 }
                }
            };
        }

        // Kodlanan verinin her zaman 618 bayt olduğunu doğrular.
        [Fact]
        public void Encode_618BaytUretir()
        {
            Assert.Equal(618, RecipeCodec.Encode(BuildRecipe()).Length);
        }

        // Başlık alanlarının kılavuzdaki adreslere yazıldığını doğrular.
        [Fact]
        public void Encode_BaslikAlanlariDogruAdreste()
        {
            var buffer = RecipeCodec.Encode(BuildRecipe());

            Assert.Equal(7, ReadInt(buffer, 0));      // RecipeID
            Assert.Equal(2, ReadInt(buffer, 2));      // RecipeType
            Assert.Equal(3, ReadInt(buffer, 36));     // StepCount
            Assert.Equal(20, ReadInt(buffer, 38));    // PressureSetPoint
            Assert.Equal(10, ReadInt(buffer, 40));    // PressureMode
            Assert.Equal(15, ReadInt(buffer, 42));    // PressureModeTime
            Assert.Equal(300, ReadInt(buffer, 44));   // PressureModeTemp
            Assert.Equal(1, ReadInt(buffer, 46));     // GsoakTcId
            Assert.Equal(5, ReadInt(buffer, 48));     // CO2_Period
        }

        // Reserve alanının (adres 50–57) 0 bırakıldığını doğrular.
        [Fact]
        public void Encode_ReserveAlaniSifir()
        {
            var buffer = RecipeCodec.Encode(BuildRecipe());

            for (int address = 50; address < 58; address++)
                Assert.Equal(0, buffer[address]);
        }

        // Reçete adının S7 String[30] biçiminde yazıldığını doğrular.
        [Fact]
        public void Encode_AdS7StringOlarakYazilir()
        {
            var buffer = RecipeCodec.Encode(BuildRecipe());

            Assert.Equal(30, buffer[4]);          // En fazla uzunluk
            Assert.Equal(6, buffer[5]);           // Gerçek uzunluk ("Deneme")
            Assert.Equal((byte)'D', buffer[6]);
            Assert.Equal((byte)'e', buffer[11]);
            Assert.Equal(0, buffer[12]);          // Kalan baytlar sıfır
        }

        // Türkçe karakterlerin Windows-1254 ile tek bayt olarak yazıldığını doğrular.
        [Fact]
        public void Encode_TurkceKarakterlerWindows1254()
        {
            var recipe = BuildRecipe();
            recipe.RecipeName = "Şğıİöç";

            var buffer = RecipeCodec.Encode(recipe);

            Assert.Equal(6, buffer[5]);
            Assert.Equal(new byte[] { 0xDE, 0xF0, 0xFD, 0xDD, 0xF6, 0xE7 }, buffer[6..12]);
        }

        // 30 karakterden uzun adın 30 karakterde kesildiğini doğrular.
        [Fact]
        public void Encode_UzunAd30KarakterdeKesilir()
        {
            var recipe = BuildRecipe();
            recipe.RecipeName = new string('A', 40);

            var buffer = RecipeCodec.Encode(recipe);

            Assert.Equal(30, buffer[5]);
            Assert.Equal((byte)'A', buffer[35]);
            Assert.Equal(3, ReadInt(buffer, 36));   // Ad, StepCount alanına taşmamış
        }

        // Adım alanlarının adım başına göre kılavuzdaki adreslere yazıldığını doğrular (2. adım = adres 86).
        [Fact]
        public void Encode_AdimAlanlariDogruAdreste()
        {
            var buffer = RecipeCodec.Encode(BuildRecipe());
            int step2 = 58 + 28;

            Assert.Equal(2, ReadInt(buffer, step2 + 0));      // StepType
            Assert.Equal(11, ReadInt(buffer, step2 + 2));     // Vac_SetPoint
            Assert.Equal(12, ReadInt(buffer, step2 + 4));     // Vac_Timeout
            Assert.Equal(13, ReadInt(buffer, step2 + 6));     // TargetTemp
            Assert.Equal(14, ReadInt(buffer, step2 + 8));     // StepTime
            Assert.Equal(15, ReadInt(buffer, step2 + 10));    // GSoak
            Assert.Equal(16, ReadInt(buffer, step2 + 12));    // N2_Flow
            Assert.Equal(17, ReadInt(buffer, step2 + 14));    // NH3_Flow
            Assert.Equal(18, ReadInt(buffer, step2 + 16));    // dNH3_Flow
            Assert.Equal(19, ReadInt(buffer, step2 + 18));    // CO2_Perc
            Assert.Equal(1, ReadInt(buffer, step2 + 20));     // Su_On
            Assert.Equal(1, ReadInt(buffer, step2 + 22));     // H2_Auto
            Assert.Equal(325, ReadInt(buffer, step2 + 24));   // H2_Set
            Assert.Equal(50, ReadInt(buffer, step2 + 26));    // FanSpeed
        }

        // İlk adımın adres 58'den, üçüncü adımın adres 114'ten başladığını doğrular.
        [Fact]
        public void Encode_AdimBaslangicAdresleri()
        {
            var buffer = RecipeCodec.Encode(BuildRecipe());

            Assert.Equal(1, ReadInt(buffer, 58));           // 1. adım StepType
            Assert.Equal(50, ReadInt(buffer, 58 + 2));      // 1. adım Vac_SetPoint
            Assert.Equal(3, ReadInt(buffer, 114));          // 3. adım StepType
            Assert.Equal(150, ReadInt(buffer, 114 + 6));    // 3. adım TargetTemp
        }

        // Kullanılmayan adımların (StepCount'tan sonrası) 0 ile doldurulduğunu doğrular.
        [Fact]
        public void Encode_KullanilmayanAdimlarSifir()
        {
            var buffer = RecipeCodec.Encode(BuildRecipe());

            for (int address = 58 + 3 * 28; address < 618; address++)
                Assert.Equal(0, buffer[address]);
        }

        // Adımların listedeki sıraya göre değil StepNo sırasına göre yazıldığını doğrular.
        [Fact]
        public void Encode_AdimlarStepNoSirasinaGore()
        {
            var recipe = BuildRecipe();
            recipe.Steps = recipe.Steps.Reverse().ToList();

            var buffer = RecipeCodec.Encode(recipe);

            Assert.Equal(1, ReadInt(buffer, 58));     // İlk sırada yine vakum adımı
            Assert.Equal(3, ReadInt(buffer, 114));    // Son sırada yine soğutma adımı
        }

        // PLC Int aralığına sığmayan değerde hata fırlatıldığını doğrular.
        [Fact]
        public void Encode_IntAraligiDisindaHataFirlatir()
        {
            var recipe = BuildRecipe();
            recipe.Steps.First().Vac_SetPoint = 40000;

            Assert.Throws<ArgumentOutOfRangeException>(() => RecipeCodec.Encode(recipe));
        }

        // 20'den fazla adımda hata fırlatıldığını doğrular.
        [Fact]
        public void Encode_YirmidenFazlaAdimdaHataFirlatir()
        {
            var recipe = BuildRecipe();
            recipe.Steps = Enumerable.Range(1, 21)
                .Select(i => new RecipeStep { StepNo = i, StepType = 2 })
                .ToList();

            Assert.Throws<ArgumentException>(() => RecipeCodec.Encode(recipe));
        }

        // Kodlanıp geri çözülen reçetenin bütün alanlarının aynı kaldığını doğrular.
        [Fact]
        public void Decode_KodlananReceteyiAynenGeriVerir()
        {
            var original = BuildRecipe();
            original.RecipeName = "Meneviş Çelik Şaft Ölçü";

            var decoded = RecipeCodec.Decode(RecipeCodec.Encode(original));

            Assert.Equal(original.Id, decoded.Id);
            Assert.Equal(original.RecipeType, decoded.RecipeType);
            Assert.Equal(original.RecipeName, decoded.RecipeName);
            Assert.Equal(3, decoded.TotalStepCount);
            Assert.Equal(original.PressureSetPoint, decoded.PressureSetPoint);
            Assert.Equal(original.PressureMode, decoded.PressureMode);
            Assert.Equal(original.PressureModeTime, decoded.PressureModeTime);
            Assert.Equal(original.PressureModeTemp, decoded.PressureModeTemp);
            Assert.Equal(original.GsoakTcId, decoded.GsoakTcId);
            Assert.Equal(original.CO2_Period, decoded.CO2_Period);

            var expected = original.Steps.OrderBy(s => s.StepNo).ToList();
            var actual = decoded.Steps.OrderBy(s => s.StepNo).ToList();
            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(i + 1, actual[i].StepNo);
                Assert.Equal(expected[i].StepType, actual[i].StepType);
                Assert.Equal(expected[i].Vac_SetPoint, actual[i].Vac_SetPoint);
                Assert.Equal(expected[i].Vac_Timeout, actual[i].Vac_Timeout);
                Assert.Equal(expected[i].TargetTemp, actual[i].TargetTemp);
                Assert.Equal(expected[i].StepTime, actual[i].StepTime);
                Assert.Equal(expected[i].GSoak, actual[i].GSoak);
                Assert.Equal(expected[i].N2_Flow, actual[i].N2_Flow);
                Assert.Equal(expected[i].NH3_Flow, actual[i].NH3_Flow);
                Assert.Equal(expected[i].dNH3_Flow, actual[i].dNH3_Flow);
                Assert.Equal(expected[i].CO2_Perc, actual[i].CO2_Perc);
                Assert.Equal(expected[i].Su_On, actual[i].Su_On);
                Assert.Equal(expected[i].H2_Auto, actual[i].H2_Auto);
                Assert.Equal(expected[i].H2_Set, actual[i].H2_Set);
                Assert.Equal(expected[i].FanSpeed, actual[i].FanSpeed);
            }
        }

        // StepCount'tan sonraki adımların çözülürken dikkate alınmadığını doğrular.
        [Fact]
        public void Decode_StepCountSonrasiniYokSayar()
        {
            var buffer = RecipeCodec.Encode(BuildRecipe());
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(36, 2), 2);   // StepCount 3 → 2

            var decoded = RecipeCodec.Decode(buffer);

            Assert.Equal(2, decoded.TotalStepCount);
            Assert.Equal(2, decoded.Steps.Count);
        }

        // 618 bayttan kısa veride hata fırlatıldığını doğrular.
        [Fact]
        public void Decode_KisaVerideHataFirlatir()
        {
            Assert.Throws<ArgumentException>(() => RecipeCodec.Decode(new byte[617]));
        }
    }
}