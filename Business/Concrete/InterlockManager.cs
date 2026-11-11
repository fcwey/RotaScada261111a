using Business.Abstract;
using Core.Utilities.Results;
using Entities.Dtos;
using Entities.Structures;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Business.Concrete
{
    // Ekrandaki düğmelerin etkin/pasif gösterimi ve mimik şema için PLC'nin izin ve cihaz bitlerini okur.
    // İzinleri SCADA hesaplamaz; yalnızca PLC'nin DB201'de verdiği bitleri aktarır. Son karar her zaman PLC'dedir.
    public class InterlockManager : IInterlockService
    {
        private readonly IPlcService _plcService;

        // PLC servis bağımlılığını alır.
        public InterlockManager(IPlcService plcService)
        {
            _plcService = plcService;
        }

        // İşlem koduna karşılık gelen PLC izin bitini kontrol eder (START, OPEN_LID, NH3, HEAT, WATER).
        public async Task<IResult> ValidateOperationSafetyAsync(string operationCode)
        {
            var statusResult = await _plcService.ReadStatusAsync();
            if (!statusResult.IsSuccess || statusResult.Data == null)
                return new ErrorResult($"PLC durumu okunamadı, izin kontrolü yapılamadı: {statusResult.Message}");

            var status = statusResult.Data;

            switch (operationCode)
            {
                case "START":
                    if (status.Mode != 2)
                        return new ErrorResult("İZİN YOK: Otomatik mod seçili değil.");
                    if (!status.HasPermit(0))
                        return new ErrorResult("İZİN YOK: Başlatma izni yok (kapak, eşanjörde su, güç).");
                    break;

                case "OPEN_LID":
                    if (!status.HasPermit(1))
                        return new ErrorResult("İZİN YOK: Kapak açılamaz (sıcaklık yüksek ya da azot yıkaması tamamlanmadı).");
                    break;

                case "NH3":
                    if (!status.HasPermit(2))
                        return new ErrorResult("İZİN YOK: Amonyak verilemez.");
                    break;

                case "HEAT":
                    if (!status.HasPermit(3))
                        return new ErrorResult("İZİN YOK: Isıtma yapılamaz.");
                    break;

                case "WATER":
                    if (!status.HasPermit(4))
                        return new ErrorResult("İZİN YOK: Su verilemez.");
                    break;

                default:
                    return new ErrorResult($"Bilinmeyen işlem kodu: {operationCode}");
            }

            return new SuccessResult("PLC izni var.");
        }

        // DB201'deki izin ve cihaz bitlerini mimik ekran için tek pakette döner.
        public async Task<IDataResult<MimicStateDto>> GetMimicStateAsync()
        {
            var statusResult = await _plcService.ReadStatusAsync();
            if (!statusResult.IsSuccess || statusResult.Data == null)
            {
                return new SuccessDataResult<MimicStateDto>(new MimicStateDto
                {
                    IsConnected = false,
                    SafetyStatusText = "PLC Bağlantısı Kopuk"
                });
            }

            var status = statusResult.Data;
            bool hasActiveAlarm = status.AlarmActive.Any(word => word != 0);

            var mimicDto = new MimicStateDto
            {
                IsConnected = true,
                HasActiveAlarm = hasActiveAlarm,
                SafetyStatusText = GetSafetyStatusText(status.SeqState, hasActiveAlarm),

                CanStartProgram = status.HasPermit(0),
                CanOpenLid = status.HasPermit(1),
                CanFeedAmmonia = status.HasPermit(2),
                CanHeat = status.HasPermit(3),
                CanFeedWater = status.HasPermit(4),

                VacuumMotor = status.GetDeviceBit(0, 0),
                VacuumValve = status.GetDeviceBit(0, 1),
                ExternalN2Valve = status.GetDeviceBit(0, 2),
                N2LineValve = status.GetDeviceBit(0, 3),
                NH3LineValve = status.GetDeviceBit(0, 4),
                CO2Valve = status.GetDeviceBit(0, 5),
                WaterDosingPump = status.GetDeviceBit(0, 6),
                HeaterContactors = status.GetDeviceBit(0, 7),
                CirculationFan = status.GetDeviceBit(0, 8),
                CoolingFan = status.GetDeviceBit(0, 9),
                DamperOpen = status.GetDeviceBit(0, 10),
                DamperClosed = status.GetDeviceBit(0, 11),
                PressureControlValveOpen = status.GetDeviceBit(0, 12),
                DrainValveOpen = status.GetDeviceBit(0, 13),
                EmergencyN2ValveOpen = status.GetDeviceBit(0, 14),
                BurnerOn = status.GetDeviceBit(0, 15),

                LidClosedAndLocked = status.GetDeviceBit(1, 0),
                ExchangerWaterValve = status.GetDeviceBit(1, 1),
                ExchangerFeedValve = status.GetDeviceBit(1, 2),
                ExchangerPump = status.GetDeviceBit(1, 3),
                ExchangerHasWater = status.GetDeviceBit(1, 4),
                H2SensorPower = status.GetDeviceBit(1, 5),
                HydraulicMotor = status.GetDeviceBit(1, 6),
                AmmoniaAtmosphereInside = status.GetDeviceBit(1, 7),
                ManualMode = status.GetDeviceBit(1, 8),
                AutoMode = status.GetDeviceBit(1, 9)
            };

            return new SuccessDataResult<MimicStateDto>(mimicDto);
        }

        // Sekans durumu ve alarm bilgisinden operatöre gösterilecek güvenlik metnini üretir.
        private static string GetSafetyStatusText(short seqState, bool hasActiveAlarm)
        {
            if (seqState == 80)
                return "GÜVENLİ DURDURMA";

            return hasActiveAlarm ? "AKTİF ALARM VAR" : "SİSTEM GÜVENLİ";
        }
    }
}