using CommunityToolkit.Mvvm.ComponentModel;
using Entities.Concrete;

namespace DesktopUI.ViewModels
{
    // Reçete düzenleme tablosundaki bir adım satırı; hangi alanların görüneceğini adım ve reçete tipine göre belirler.
    public class RecipeStepRow : ObservableObject
    {
        // Yeni adımlarda kullanılan başlangıç değerleri
        public const int DefaultVacSetPoint = 200;   // mbar mutlak
        public const int DefaultVacTimeout = 20;     // dk
        public const int DefaultFanSpeed = 100;      // %

        private readonly Action _changed;

        private int _stepNo;
        private int _stepType;
        private int _recipeType;
        private int _vacSetPoint;
        private int _vacTimeout;
        private int _targetTemp;
        private int _stepTime;
        private int _gSoak;
        private int _n2Flow;
        private int _nh3Flow;
        private int _co2Perc;
        private bool _waterOn;
        private bool _h2Auto;
        private double _h2SetPercent;
        private int _fanSpeed;

        public int StepNo { get => _stepNo; set => SetProperty(ref _stepNo, value); }

        public int StepType
        {
            get => _stepType;
            set
            {
                if (!Change(ref _stepType, value))
                    return;

                ApplyTypeDefaults();
                NotifyVisibility();
            }
        }

        // Reçete tipi satıra dışarıdan verilir; gaz alanlarının görünürlüğünü belirler.
        public int RecipeType
        {
            get => _recipeType;
            set
            {
                if (SetProperty(ref _recipeType, value))
                    NotifyVisibility();
            }
        }

        public int VacSetPoint { get => _vacSetPoint; set => Change(ref _vacSetPoint, value); }
        public int VacTimeout { get => _vacTimeout; set => Change(ref _vacTimeout, value); }
        public int TargetTemp { get => _targetTemp; set => Change(ref _targetTemp, value); }
        public int StepTime { get => _stepTime; set => Change(ref _stepTime, value); }
        public int GSoak { get => _gSoak; set => Change(ref _gSoak, value); }
        public int N2Flow { get => _n2Flow; set => Change(ref _n2Flow, value); }
        public int NH3Flow { get => _nh3Flow; set => Change(ref _nh3Flow, value); }
        public int CO2Perc { get => _co2Perc; set => Change(ref _co2Perc, Math.Clamp(value, 0, 100)); }
        public bool WaterOn { get => _waterOn; set => Change(ref _waterOn, value); }
        public int FanSpeed { get => _fanSpeed; set => Change(ref _fanSpeed, Math.Clamp(value, 0, 100)); }

        // Hidrojen seti yüzde olarak girilir ve 0–100 arasına sınırlanır.
        public double H2SetPercent
        {
            get => _h2SetPercent;
            set
            {
                double clamped = Math.Clamp(value, 0, 100);
                if (!Change(ref _h2SetPercent, clamped) && clamped != value)
                    OnPropertyChanged();
            }
        }

        public bool H2Auto
        {
            get => _h2Auto;
            set
            {
                if (Change(ref _h2Auto, value))
                    OnPropertyChanged(nameof(ShowH2Set));
            }
        }

        public bool IsVacuum => StepType == 1;
        public bool IsHeating => StepType == 2;
        public bool IsCooling => StepType == 3;

        public bool ShowTemp => !IsVacuum;
        public bool ShowN2 => !IsVacuum;
        public bool ShowFan => !IsVacuum;
        public bool ShowNH3 => IsHeating && RecipeType == 2;
        public bool ShowCO2 => IsHeating && RecipeType == 2;
        public bool ShowH2 => IsHeating && RecipeType == 2;
        public bool ShowH2Set => ShowH2 && H2Auto;
        public bool ShowWater => IsHeating && RecipeType == 3;

        // Satır değiştiğinde çağrılacak bildirimi alır.
        public RecipeStepRow(Action changed)
        {
            _changed = changed;
        }

        // Veritabanı adımından satır üretir.
        public static RecipeStepRow FromEntity(RecipeStep step, int recipeType, Action changed)
        {
            return new RecipeStepRow(changed)
            {
                _stepNo = step.StepNo,
                _stepType = step.StepType,
                _recipeType = recipeType,
                _vacSetPoint = step.Vac_SetPoint,
                _vacTimeout = step.Vac_Timeout,
                _targetTemp = step.TargetTemp,
                _stepTime = step.StepTime,
                _gSoak = step.GSoak,
                _n2Flow = step.N2_Flow,
                _nh3Flow = step.NH3_Flow,
                _co2Perc = step.CO2_Perc,
                _waterOn = step.Su_On != 0,
                _h2Auto = step.H2_Auto != 0,
                _h2SetPercent = step.H2_Set / 10.0,
                _fanSpeed = step.FanSpeed
            };
        }

        // Satırı sunucuya gönderilecek adıma çevirir; o adımda görünmeyen alanlar 0 gider.
        public RecipeStep ToEntity()
        {
            return new RecipeStep
            {
                StepNo = StepNo,
                StepType = StepType,
                Vac_SetPoint = IsVacuum ? VacSetPoint : 0,
                Vac_Timeout = IsVacuum ? VacTimeout : 0,
                TargetTemp = ShowTemp ? TargetTemp : 0,
                StepTime = IsHeating ? StepTime : 0,
                GSoak = IsHeating ? GSoak : 0,
                N2_Flow = ShowN2 ? N2Flow : 0,
                NH3_Flow = ShowNH3 ? NH3Flow : 0,
                dNH3_Flow = 0,
                CO2_Perc = ShowCO2 ? CO2Perc : 0,
                Su_On = ShowWater && WaterOn ? 1 : 0,
                H2_Auto = ShowH2 && H2Auto ? 1 : 0,
                H2_Set = ShowH2Set ? (int)Math.Round(H2SetPercent * 10) : 0,
                FanSpeed = ShowFan ? FanSpeed : 0
            };
        }

        // Adım tipi değişince boş kalan alanlara başlangıç değerlerini koyar: vakumda set ve zaman aşımı, diğerlerinde fan.
        private void ApplyTypeDefaults()
        {
            if (IsVacuum && _vacSetPoint == 0 && _vacTimeout == 0)
            {
                _vacSetPoint = DefaultVacSetPoint;
                _vacTimeout = DefaultVacTimeout;
                OnPropertyChanged(nameof(VacSetPoint));
                OnPropertyChanged(nameof(VacTimeout));
            }

            if (!IsVacuum && _fanSpeed == 0)
            {
                _fanSpeed = DefaultFanSpeed;
                OnPropertyChanged(nameof(FanSpeed));
            }
        }

        // Alanı günceller ve değiştiyse reçetenin değiştiğini bildirir.
        private bool Change<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (!SetProperty(ref field, value, name))
                return false;

            _changed();
            return true;
        }

        // Adım ya da reçete tipi değişince alanların görünürlüğünü yeniden hesaplatır.
        private void NotifyVisibility()
        {
            OnPropertyChanged(nameof(IsVacuum));
            OnPropertyChanged(nameof(IsHeating));
            OnPropertyChanged(nameof(IsCooling));
            OnPropertyChanged(nameof(ShowTemp));
            OnPropertyChanged(nameof(ShowN2));
            OnPropertyChanged(nameof(ShowFan));
            OnPropertyChanged(nameof(ShowNH3));
            OnPropertyChanged(nameof(ShowCO2));
            OnPropertyChanged(nameof(ShowH2));
            OnPropertyChanged(nameof(ShowH2Set));
            OnPropertyChanged(nameof(ShowWater));
        }
    }
}