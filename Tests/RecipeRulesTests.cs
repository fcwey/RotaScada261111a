using Business.ValidationRules;
using Entities.Concrete;
using Entities.Structures;
using Xunit;

namespace Tests
{
    // Reçete doğrulama kurallarının (kılavuz bölüm 6.3, kod 110–121) testleri
    public class RecipeRulesTests
    {
        private static readonly RecipeLimits Limits = new RecipeLimits
        {
            MaxPotaTemp = 750,
            FanMinSpeed = 30,
            N2MfcMaxLph = 2000,
            NH3MfcMaxLph = 1500
        };

        // Vakum adımı üretir.
        private static RecipeStep Vacuum()
        {
            return new RecipeStep { StepType = 1, Vac_SetPoint = 50, Vac_Timeout = 20 };
        }

        // Isıtma adımı üretir.
        private static RecipeStep Heating()
        {
            return new RecipeStep { StepType = 2, TargetTemp = 520, StepTime = 120, N2_Flow = 500 };
        }

        // Soğutma adımı üretir.
        private static RecipeStep Cooling()
        {
            return new RecipeStep { StepType = 3, TargetTemp = 150, N2_Flow = 500 };
        }

        // Verilen tipte ve adımlarla, adımları sırayla numaralanmış reçete üretir.
        private static Recipe Build(int recipeType, params RecipeStep[] steps)
        {
            for (int i = 0; i < steps.Length; i++)
                steps[i].StepNo = i + 1;

            return new Recipe
            {
                Id = 1,
                RecipeType = recipeType,
                RecipeName = "Test",
                Steps = steps.ToList()
            };
        }

        // Reçetenin beklenen kod ve adım numarasıyla reddedildiğini doğrular.
        private static void AssertRejected(Recipe recipe, int expectedCode, int expectedStep)
        {
            var error = RecipeRules.Validate(recipe, Limits);

            Assert.NotNull(error);
            Assert.Equal(expectedCode, (int)error!.ResultCode);
            Assert.Equal(expectedStep, (int)error.Detail);
            Assert.False(error.Accepted);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }

        // Geçerli nitrasyon reçetesinin kabul edildiğini doğrular.
        [Fact]
        public void GecerliNitrasyon_KabulEdilir()
        {
            var soak = Heating();
            soak.NH3_Flow = 800;
            soak.CO2_Perc = 5;

            Assert.Null(RecipeRules.Validate(Build(2, Vacuum(), Heating(), soak, Cooling()), Limits));
        }

        // Geçerli meneviş reçetesinin (vakumsuz, gazsız) kabul edildiğini doğrular.
        [Fact]
        public void GecerliMenevis_KabulEdilir()
        {
            Assert.Null(RecipeRules.Validate(Build(1, Heating(), Cooling()), Limits));
        }

        // Geçerli oksidasyon reçetesinin (su açık) kabul edildiğini doğrular.
        [Fact]
        public void GecerliOksidasyon_KabulEdilir()
        {
            var step = Heating();
            step.Su_On = 1;

            Assert.Null(RecipeRules.Validate(Build(3, step, Cooling()), Limits));
        }

        // Boş reçete adının reddedildiğini doğrular.
        [Fact]
        public void BosAd_Reddedilir()
        {
            var recipe = Build(1, Heating(), Cooling());
            recipe.RecipeName = "  ";

            AssertRejected(recipe, 118, 0);
        }

        // 30 karakterden uzun reçete adının reddedildiğini doğrular.
        [Fact]
        public void UzunAd_Reddedilir()
        {
            var recipe = Build(1, Heating(), Cooling());
            recipe.RecipeName = new string('A', 31);

            AssertRejected(recipe, 118, 0);
        }

        // Kod 110: reçete tipi 1, 2 ya da 3 değilse reddedilir.
        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        public void Kod110_GecersizReceteTipi(int recipeType)
        {
            AssertRejected(Build(recipeType, Heating(), Cooling()), 110, 0);
        }

        // Kod 111: adım yoksa reddedilir.
        [Fact]
        public void Kod111_AdimYok()
        {
            AssertRejected(Build(1), 111, 0);
        }

        // Kod 111: 20'den fazla adım varsa reddedilir.
        [Fact]
        public void Kod111_YirmidenFazlaAdim()
        {
            var steps = Enumerable.Range(0, 20).Select(_ => Heating()).Append(Cooling()).ToArray();

            AssertRejected(Build(1, steps), 111, 0);
        }

        // Kod 112: adım tipi 1, 2 ya da 3 değilse (boş adım) reddedilir.
        [Fact]
        public void Kod112_BosAdim()
        {
            var empty = new RecipeStep { StepType = 0 };

            AssertRejected(Build(1, Heating(), empty, Cooling()), 112, 2);
        }

        // Kod 113: vakum adımı en başta değilse reddedilir.
        [Fact]
        public void Kod113_VakumBastaDegil()
        {
            AssertRejected(Build(2, Heating(), Vacuum(), Cooling()), 113, 2);
        }

        // Kod 114: soğutmadan sonra adım gelirse reddedilir.
        [Fact]
        public void Kod114_SogutmadanSonraAdim()
        {
            AssertRejected(Build(2, Vacuum(), Heating(), Cooling(), Heating()), 114, 4);
        }

        // Kod 115: son adım soğutma değilse reddedilir.
        [Fact]
        public void Kod115_SonAdimSogutmaDegil()
        {
            AssertRejected(Build(2, Vacuum(), Heating()), 115, 2);
        }

        // Kod 116: hiç ısıtma adımı yoksa reddedilir.
        [Fact]
        public void Kod116_IsitmaAdimiYok()
        {
            AssertRejected(Build(2, Vacuum(), Cooling()), 116, 0);
        }

        // Kod 117: menevişte amonyak verilirse reddedilir.
        [Fact]
        public void Kod117_MenevisteAmonyak()
        {
            var step = Heating();
            step.NH3_Flow = 100;

            AssertRejected(Build(1, step, Cooling()), 117, 1);
        }

        // Kod 117: nitrasyonda su açılırsa reddedilir.
        [Fact]
        public void Kod117_NitrasyondaSu()
        {
            var step = Heating();
            step.Su_On = 1;

            AssertRejected(Build(2, Vacuum(), step, Cooling()), 117, 2);
        }

        // Kod 117: oksidasyonda CO2 verilirse reddedilir.
        [Fact]
        public void Kod117_OksidasyondaCO2()
        {
            var step = Heating();
            step.CO2_Perc = 10;

            AssertRejected(Build(3, step, Cooling()), 117, 1);
        }

        // Kod 118: sıcaklık MaxPotaTemp değerini aşarsa reddedilir.
        [Fact]
        public void Kod118_SicaklikSiniriAsiyor()
        {
            var step = Heating();
            step.TargetTemp = 751;

            AssertRejected(Build(1, step, Cooling()), 118, 1);
        }

        // Kod 118: sıcaklık tam sınırdaysa kabul edilir.
        [Fact]
        public void Kod118_SicaklikTamSinirdaKabul()
        {
            var step = Heating();
            step.TargetTemp = 750;

            Assert.Null(RecipeRules.Validate(Build(1, step, Cooling()), Limits));
        }

        // Kod 118: azot debisi MFC tam skalasını aşarsa reddedilir.
        [Fact]
        public void Kod118_AzotDebisiTamSkalayiAsiyor()
        {
            var step = Heating();
            step.N2_Flow = 2001;

            AssertRejected(Build(1, step, Cooling()), 118, 1);
        }

        // Kod 118: amonyak tam skalası 0 iken (girilmemiş) her amonyak debisi reddedilir.
        [Fact]
        public void Kod118_AmonyakTamSkalaGirilmemis()
        {
            var limits = new RecipeLimits { MaxPotaTemp = 750, FanMinSpeed = 30, N2MfcMaxLph = 2000, NH3MfcMaxLph = 0 };
            var step = Heating();
            step.NH3_Flow = 800;

            var error = RecipeRules.Validate(Build(2, Vacuum(), step, Cooling()), limits);

            Assert.NotNull(error);
            Assert.Equal(118, (int)error!.ResultCode);
            Assert.Equal(2, (int)error.Detail);
        }

        // Kod 118: fan hızı 0 değilse ve en düşük hızın altındaysa reddedilir.
        [Fact]
        public void Kod118_FanHiziEnDusugunAltinda()
        {
            var step = Heating();
            step.FanSpeed = 10;

            AssertRejected(Build(1, step, Cooling()), 118, 1);
        }

        // Kod 118: fan hızı 0 (varsayılan hız) kabul edilir.
        [Fact]
        public void Kod118_FanHiziSifirKabul()
        {
            var step = Heating();
            step.FanSpeed = 0;

            Assert.Null(RecipeRules.Validate(Build(1, step, Cooling()), Limits));
        }

        // Kod 118: ısıtma adımının süresi 1 dakikadan azsa reddedilir.
        [Fact]
        public void Kod118_IsitmaSuresiSifir()
        {
            var step = Heating();
            step.StepTime = 0;

            AssertRejected(Build(1, step, Cooling()), 118, 1);
        }

        // Kod 118: vakum zaman aşımı 1 dakikadan azsa reddedilir.
        [Fact]
        public void Kod118_VakumZamanAsimiSifir()
        {
            var vacuum = Vacuum();
            vacuum.Vac_Timeout = 0;

            AssertRejected(Build(2, vacuum, Heating(), Cooling()), 118, 1);
        }

        // Kod 118: CO2 yüzdesi 100'ü aşarsa reddedilir.
        [Fact]
        public void Kod118_CO2YuzdeyiAsiyor()
        {
            var step = Heating();
            step.CO2_Perc = 101;

            AssertRejected(Build(2, Vacuum(), step, Cooling()), 118, 2);
        }

        // Kod 119: dNH3_Flow 0 değilse reddedilir.
        [Fact]
        public void Kod119_dNH3SifirDegil()
        {
            var step = Heating();
            step.dNH3_Flow = 5;

            AssertRejected(Build(2, Vacuum(), step, Cooling()), 119, 2);
        }

        // Kod 120: H2_Auto açıkken amonyak debisi yoksa reddedilir.
        [Fact]
        public void Kod120_H2AutoAmonyakYok()
        {
            var step = Heating();
            step.H2_Auto = 1;
            step.H2_Set = 325;

            AssertRejected(Build(2, Vacuum(), step, Cooling()), 120, 2);
        }

        // Kod 120: H2_Auto açıkken H2_Set 0 ise reddedilir.
        [Fact]
        public void Kod120_H2AutoSetYok()
        {
            var step = Heating();
            step.H2_Auto = 1;
            step.NH3_Flow = 800;

            AssertRejected(Build(2, Vacuum(), step, Cooling()), 120, 2);
        }

        // Kod 120: H2_Auto açık, amonyak ve H2_Set geçerliyse kabul edilir.
        [Fact]
        public void Kod120_H2AutoGecerliKabul()
        {
            var step = Heating();
            step.H2_Auto = 1;
            step.NH3_Flow = 800;
            step.H2_Set = 325;

            Assert.Null(RecipeRules.Validate(Build(2, Vacuum(), step, Cooling()), Limits));
        }

        // Kod 121: nitrasyon reçetesinde vakum adımı yoksa reddedilir.
        [Fact]
        public void Kod121_NitrasyondaVakumYok()
        {
            AssertRejected(Build(2, Heating(), Cooling()), 121, 0);
        }

        // Normalize: adımları sıraya göre 1'den yeniden numaralar ve adım sayısını günceller.
        [Fact]
        public void Normalize_AdimlariYenidenNumaralar()
        {
            var recipe = Build(1, Heating(), Heating(), Cooling());
            var steps = recipe.Steps.ToList();
            steps[0].StepNo = 5;
            steps[1].StepNo = 9;
            steps[2].StepNo = 12;

            RecipeRules.Normalize(recipe);

            Assert.Equal(new[] { 1, 2, 3 }, recipe.Steps.OrderBy(s => s.StepNo).Select(s => s.StepNo).ToArray());
            Assert.Equal(3, recipe.TotalStepCount);
        }

        // Normalize: vakum adımında sıcaklık, süre ve gaz alanlarını 0 yapar.
        [Fact]
        public void Normalize_VakumAdimindaDigerAlanlarSifirlanir()
        {
            var vacuum = Vacuum();
            vacuum.TargetTemp = 500;
            vacuum.StepTime = 60;
            vacuum.N2_Flow = 100;
            vacuum.FanSpeed = 80;
            var recipe = Build(2, vacuum, Heating(), Cooling());

            RecipeRules.Normalize(recipe);

            Assert.Equal(0, vacuum.TargetTemp);
            Assert.Equal(0, vacuum.StepTime);
            Assert.Equal(0, vacuum.N2_Flow);
            Assert.Equal(0, vacuum.FanSpeed);
            Assert.Equal(50, vacuum.Vac_SetPoint);
            Assert.Equal(20, vacuum.Vac_Timeout);
        }

        // Normalize: soğutma adımında süre ve yalnızca ısıtmaya ait alanları 0 yapar, sıcaklık ve gazı korur.
        [Fact]
        public void Normalize_SogutmaAdimindaIsitmaAlanlariSifirlanir()
        {
            var cooling = Cooling();
            cooling.StepTime = 60;
            cooling.GSoak = 5;
            cooling.CO2_Perc = 10;
            cooling.Vac_SetPoint = 50;
            var recipe = Build(2, Vacuum(), Heating(), cooling);

            RecipeRules.Normalize(recipe);

            Assert.Equal(0, cooling.StepTime);
            Assert.Equal(0, cooling.GSoak);
            Assert.Equal(0, cooling.CO2_Perc);
            Assert.Equal(0, cooling.Vac_SetPoint);
            Assert.Equal(150, cooling.TargetTemp);
            Assert.Equal(500, cooling.N2_Flow);
        }

        // Normalize: reçete adının başındaki ve sonundaki boşlukları siler.
        [Fact]
        public void Normalize_AdinBosluklariniSiler()
        {
            var recipe = Build(1, Heating(), Cooling());
            recipe.RecipeName = "  Deneme  ";

            RecipeRules.Normalize(recipe);

            Assert.Equal("Deneme", recipe.RecipeName);
        }
    }
}