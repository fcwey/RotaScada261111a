using Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Dtos
{
    // Mimik ekrandaki cihazların, izinlerin ve arızaların anlık durumu (DB201 bit alanları)
    public class MimicStateDto : IDto
    {
        public bool IsConnected { get; set; }               // PLC'den okuma yapılabildi mi
        public string SafetyStatusText { get; set; } = string.Empty; // Güvenlik durum metni
        public bool HasActiveAlarm { get; set; }            // Aktif alarm var mı
        public short SeqState { get; set; }                 // Sekans durumu

        // İzinler (Permits)
        public bool CanStartProgram { get; set; }           // Bit 0: Program başlatılabilir
        public bool CanOpenLid { get; set; }                // Bit 1: Kapak açılabilir
        public bool CanFeedAmmonia { get; set; }            // Bit 2: Amonyak verilebilir
        public bool CanHeat { get; set; }                   // Bit 3: Isıtma yapılabilir
        public bool CanFeedWater { get; set; }              // Bit 4: Su verilebilir (oksidasyon)

        // Cihazlar (DeviceStatus[0])
        public bool VacuumMotor { get; set; }               // Bit 0: Vakum motoru
        public bool VacuumValve { get; set; }               // Bit 1: Vakum vanası
        public bool ExternalN2Valve { get; set; }           // Bit 2: Harici azot vanası
        public bool N2LineValve { get; set; }               // Bit 3: Azot hat vanası
        public bool NH3LineValve { get; set; }              // Bit 4: Amonyak hat vanası
        public bool CO2Valve { get; set; }                  // Bit 5: CO2 vanası
        public bool WaterDosingPump { get; set; }           // Bit 6: Su dozaj pompası
        public bool HeaterContactors { get; set; }          // Bit 7: Rezistans kontaktörleri
        public bool CirculationFan { get; set; }            // Bit 8: Sirkülasyon
        public bool CoolingFan { get; set; }                // Bit 9: Soğutma fanı
        public bool DamperOpen { get; set; }                // Bit 10: Klepe açık (konum anahtarı)
        public bool DamperClosed { get; set; }              // Bit 11: Klepe kapalı (konum anahtarı)
        public bool PressureControlValveOpen { get; set; }  // Bit 12: Basınç kontrol vanası açık (komut durumu)
        public bool DrainValveOpen { get; set; }            // Bit 13: Tahliye vanası açık (komut durumu)
        public bool EmergencyN2ValveOpen { get; set; }      // Bit 14: Acil azot vanası açık (komut durumu)
        public bool BurnerOn { get; set; }                  // Bit 15: Brülör yanıyor

        // Cihazlar ve modlar (DeviceStatus[1])
        public bool LidClosedAndLocked { get; set; }        // Bit 0: Kapak kapalı ve kilitli
        public bool ExchangerWaterValve { get; set; }       // Bit 1: Eşanjör su vanası
        public bool ExchangerFeedValve { get; set; }        // Bit 2: Eşanjör besleme vanası
        public bool ExchangerPump { get; set; }             // Bit 3: Eşanjör pompası
        public bool ExchangerHasWater { get; set; }         // Bit 4: Eşanjörde su var
        public bool H2SensorPower { get; set; }             // Bit 5: H2 sensörü beslemesi
        public bool HydraulicMotor { get; set; }            // Bit 6: Hidrolik motoru
        public bool AmmoniaAtmosphereInside { get; set; }   // Bit 7: İçeride amonyak atmosferi var
        public bool ManualMode { get; set; }                // Bit 8: Manuel mod
        public bool AutoMode { get; set; }                  // Bit 9: Otomatik mod
        public bool RestrictedManual { get; set; }          // Bit 10: Kısıtlı manuel (basınç, gaz, vakum, sirkülasyon otomatik)
        public bool SimulationMode { get; set; }            // Bit 11: Simülasyon modu
        public bool LidOverrideActive { get; set; }         // Bit 12: Kapak kilidi aşılmış
        public bool KnApproximate { get; set; }             // Bit 13: CO2 veriliyor, Kn yaklaşık

        // Manuel basınç modu (DeviceStatus[2]): 0 atmosfere açık, 1 otomatik kontrol, 2 elle vana
        public short ManualPressureMode { get; set; }

        // İstek var ama izin yok (InterlockBits): "neden çalışmıyor" göstergesi
        public bool VacuumBlocked { get; set; }             // Bit 0
        public bool HeatingBlocked { get; set; }            // Bit 1
        public bool CoolingFanBlocked { get; set; }         // Bit 2
        public bool CirculationBlocked { get; set; }        // Bit 3
        public bool LidBlocked { get; set; }                // Bit 4

        // Arızalar (FaultBits)
        public bool VacuumMotorFault { get; set; }          // Bit 0
        public bool CirculationFault { get; set; }          // Bit 1
        public bool CoolingFanFault { get; set; }           // Bit 2: Fan ya da klepe
        public bool HydraulicFault { get; set; }            // Bit 3
        public bool N2MfcFault { get; set; }                // Bit 4
        public bool NH3MfcFault { get; set; }               // Bit 5
        public bool ExchangerValveFault { get; set; }       // Bit 6
        public bool HeatingSensorFault { get; set; }        // Bit 7
        public bool IoModuleFault { get; set; }             // Bit 8
    }
}