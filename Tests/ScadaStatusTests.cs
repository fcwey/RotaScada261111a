using Entities.Structures;
using System.Buffers.Binary;
using Xunit;

namespace Tests
{
    // DB201 durum bloğunun (336 bayt) kılavuzdaki adreslerden çözüldüğünü doğrulayan testler
    public class ScadaStatusTests
    {
        private const int Size = 336;

        // Verilen adrese Int değeri yazar.
        private static void PutInt(byte[] buffer, int address, short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(address, 2), value);
        }

        // Verilen adrese DInt değeri yazar.
        private static void PutDInt(byte[] buffer, int address, int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(address, 4), value);
        }

        // Verilen adrese Real değeri yazar.
        private static void PutReal(byte[] buffer, int address, float value)
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer.AsSpan(address, 4), value);
        }

        // Her alanına kendi adresinden türetilmiş değer yazılmış 336 baytlık örnek blok üretir.
        private static byte[] BuildBuffer()
        {
            var buffer = new byte[Size];

            // Int alanlar: adres + 1000
            foreach (int address in new[]
            {
                0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 44, 196,
                252, 254, 260, 262, 280, 282, 284, 286, 324, 326, 332, 334
            })
                PutInt(buffer, address, (short)(address + 1000));

            // Int diziler: TcStatus, DeviceStatus, AlarmActive, AlarmUnack
            for (int address = 140; address < 196; address += 2)
                PutInt(buffer, address, (short)(address + 1000));

            // DInt alanlar: adres + 100000
            foreach (int address in new[] { 36, 40, 248, 264 })
                PutDInt(buffer, address, address + 100000);

            // Real alanlar: adres + 0,5
            for (int address = 48; address < 140; address += 4)
                PutReal(buffer, address, address + 0.5f);
            for (int address = 200; address < 248; address += 4)
                PutReal(buffer, address, address + 0.5f);
            for (int address = 288; address <= 320; address += 4)
                PutReal(buffer, address, address + 0.5f);
            foreach (int address in new[] { 256, 268, 272, 276, 328 })
                PutReal(buffer, address, address + 0.5f);

            return buffer;
        }

        // Başlık ve sekans alanlarının doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void Parse_SekansAlanlariDogruAdresten()
        {
            var status = ScadaStatus.Parse(BuildBuffer());

            Assert.Equal(1000, status.PlcHeartbeat);
            Assert.Equal(1002, status.LayoutVersion);
            Assert.Equal(1004, status.AckCmdId);
            Assert.Equal(1006, status.AckResult);
            Assert.Equal(1008, status.AckDetail);
            Assert.Equal(1010, status.Mode);
            Assert.Equal(1012, status.SeqState);
            Assert.Equal(1014, status.SeqMessage);
            Assert.Equal(1016, status.ActiveStep);
            Assert.Equal(1018, status.ActiveStepType);
            Assert.Equal(1020, status.StepElapsedMin);
            Assert.Equal(1022, status.StepRemainMin);
            Assert.Equal(1024, status.PrgElapsedMin);
            Assert.Equal(1026, status.PrgRemainMin);
            Assert.Equal(1028, status.HoldReason);
            Assert.Equal(1030, status.VacCycleNo);
            Assert.Equal(1032, status.ActiveRecipeID);
            Assert.Equal(1034, status.ActiveRecipeType);
            Assert.Equal(100036, status.PrgStartUnix);
            Assert.Equal(100040, status.PrgEndEstUnix);
            Assert.Equal(1044, status.Permits);
            Assert.Equal(1196, status.CommStatus);
        }

        // Sıcaklık, basınç ve gaz ölçümlerinin doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void Parse_OlcumAlanlariDogruAdresten()
        {
            var status = ScadaStatus.Parse(BuildBuffer());

            Assert.Equal(48.5f, status.TempPota);
            Assert.Equal(52.5f, status.TempAlt);
            Assert.Equal(56.5f, status.TempUst);
            Assert.Equal(60.5f, status.TempPotaAlt);
            Assert.Equal(64.5f, status.TempPotaUst);
            Assert.Equal(68.5f, status.TempMalzeme);
            Assert.Equal(72.5f, status.TempOps1);
            Assert.Equal(76.5f, status.TempOps2);
            Assert.Equal(80.5f, status.TempSet);
            Assert.Equal(84.5f, status.Pressure);
            Assert.Equal(88.5f, status.PressureSet);
            Assert.Equal(92.5f, status.N2FlowAct);
            Assert.Equal(96.5f, status.N2FlowSet);
            Assert.Equal(100.5f, status.NH3FlowAct);
            Assert.Equal(104.5f, status.NH3FlowSet);
            Assert.Equal(108.5f, status.H2Act);
            Assert.Equal(112.5f, status.H2Set);
            Assert.Equal(116.5f, status.Kn);
            Assert.Equal(120.5f, status.HeatOutAlt);
            Assert.Equal(124.5f, status.HeatOutUst);
            Assert.Equal(128.5f, status.FanSpeedAct);
            Assert.Equal(132.5f, status.N2Total);
            Assert.Equal(136.5f, status.NH3Total);
        }

        // Termokupl, cihaz ve alarm dizilerinin doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void Parse_DizilerDogruAdresten()
        {
            var status = ScadaStatus.Parse(BuildBuffer());

            Assert.Equal(8, status.TcStatus.Length);
            Assert.Equal(1140, status.TcStatus[0]);
            Assert.Equal(1154, status.TcStatus[7]);

            Assert.Equal(4, status.DeviceStatus.Length);
            Assert.Equal(1156, status.DeviceStatus[0]);
            Assert.Equal(1162, status.DeviceStatus[3]);

            Assert.Equal(8, status.AlarmActive.Length);
            Assert.Equal(1164, status.AlarmActive[0]);
            Assert.Equal(1178, status.AlarmActive[7]);

            Assert.Equal(8, status.AlarmUnack.Length);
            Assert.Equal(1180, status.AlarmUnack[0]);
            Assert.Equal(1194, status.AlarmUnack[7]);
        }

        // Enerji ve sürücü alanlarının doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void Parse_EnerjiAlanlariDogruAdresten()
        {
            var status = ScadaStatus.Parse(BuildBuffer());

            Assert.Equal(new[] { 200.5f, 204.5f, 208.5f }, status.Volt);
            Assert.Equal(new[] { 212.5f, 216.5f, 220.5f }, status.Cur);
            Assert.Equal(224.5f, status.PowerKw);
            Assert.Equal(228.5f, status.EnergyKwh);
            Assert.Equal(232.5f, status.VfdFreq);
            Assert.Equal(236.5f, status.VfdCurrent);
            Assert.Equal(240.5f, status.DpuAltLoad);
            Assert.Equal(244.5f, status.DpuUstLoad);
        }

        // Şarj, PID ve arıza alanlarının (248..287) doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void Parse_SarjVePidAlanlariDogruAdresten()
        {
            var status = ScadaStatus.Parse(BuildBuffer());

            Assert.Equal(100248, status.BatchId);
            Assert.Equal(1252, status.EndReason);
            Assert.Equal(1254, status.EndCount);
            Assert.Equal(256.5f, status.BatchEnergyKwh);
            Assert.Equal(1260, status.PidState);
            Assert.Equal(1262, status.TuneStatus);
            Assert.Equal(100264, status.PidError);
            Assert.Equal(268.5f, status.PidGain);
            Assert.Equal(272.5f, status.PidTi);
            Assert.Equal(276.5f, status.PidTd);
            Assert.Equal(1280, status.InterlockBits);
            Assert.Equal(1282, status.FaultBits);
            Assert.Equal(1284, status.DpuAltStatus);
            Assert.Equal(1286, status.DpuUstStatus);
        }

        // Sayaç ve kaçak testi alanlarının (288..335) doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void Parse_SayacVeKacakAlanlariDogruAdresten()
        {
            var status = ScadaStatus.Parse(BuildBuffer());

            Assert.Equal(288.5f, status.CO2Total);
            Assert.Equal(292.5f, status.WaterTotal);
            Assert.Equal(296.5f, status.FillN2Total);
            Assert.Equal(300.5f, status.CircHours);
            Assert.Equal(304.5f, status.CoolFanHours);
            Assert.Equal(308.5f, status.VacPumpHours);
            Assert.Equal(312.5f, status.HydPumpHours);
            Assert.Equal(316.5f, status.HeatHours);
            Assert.Equal(320.5f, status.EmgN2Total);

            Assert.Equal(1324, status.LeakState);
            Assert.Equal(1326, status.LeakResult);
            Assert.Equal(328.5f, status.LeakRise);
            Assert.Equal(1332, status.LeakRemainSec);
            Assert.Equal(1334, status.LeakCount);
        }

        // 336 bayttan kısa veride (eski 248 baytlık blok dahil) hata fırlatıldığını doğrular.
        [Fact]
        public void Parse_KisaVerideHataFirlatir()
        {
            Assert.Throws<ArgumentException>(() => ScadaStatus.Parse(new byte[335]));
            Assert.Throws<ArgumentException>(() => ScadaStatus.Parse(new byte[248]));
        }

        // Alarm 22'nin kelime 1, bit 5'e (adres 166) denk geldiğini doğrular.
        [Fact]
        public void IsAlarmActive_Alarm22Kelime1Bit5()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 166, 1 << 5);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsAlarmActive(22));
            Assert.False(status.IsAlarmActive(21));
            Assert.False(status.IsAlarmActive(23));
        }

        // Kılavuzdaki örneği doğrular: alarm 35 → kelime 2 (adres 168), bit 2.
        [Fact]
        public void IsAlarmActive_Alarm35Kelime2Bit2()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 168, 4);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsAlarmActive(35));
            Assert.False(status.IsAlarmActive(34));
        }

        // Yeni alarmların dördüncü kelimeye denk geldiğini doğrular: alarm 59 → adres 170, bit 10.
        [Fact]
        public void IsAlarmActive_Alarm59Kelime3Bit10()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 170, 1 << 10);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsAlarmActive(59));
            Assert.False(status.IsAlarmActive(58));
        }

        // Kelime sınırlarını doğrular: alarm 16 → kelime 0 bit 15 (negatif okunan değer), alarm 17 → kelime 1 bit 0.
        [Fact]
        public void IsAlarmActive_KelimeSinirlari()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 164, unchecked((short)0x8000));
            PutInt(buffer, 166, 1);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsAlarmActive(16));
            Assert.True(status.IsAlarmActive(17));
            Assert.False(status.IsAlarmActive(1));
            Assert.False(status.IsAlarmActive(18));
        }

        // Görülmemiş alarm bitinin 180 adresinden başlayarak okunduğunu doğrular.
        [Fact]
        public void IsAlarmUnack_Adres180denOkur()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 180, 1);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsAlarmUnack(1));
            Assert.False(status.IsAlarmActive(1));
        }

        // İzin bitlerinin adres 44'teki Int'ten okunduğunu doğrular.
        [Fact]
        public void HasPermit_BitleriDogruOkur()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 44, 0b01101);   // bit 0, 2 ve 3 dolu

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.HasPermit(0));
            Assert.False(status.HasPermit(1));
            Assert.True(status.HasPermit(2));
            Assert.True(status.HasPermit(3));
            Assert.False(status.HasPermit(4));
        }

        // Cihaz bitlerinin doğru kelimeden okunduğunu doğrular (DeviceStatus[1] bit 10 = kısıtlı manuel, bit 13 = Kn yaklaşık).
        [Fact]
        public void GetDeviceBit_DogruKelimedenOkur()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 158, (1 << 10) | (1 << 13));

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.GetDeviceBit(1, 10));
            Assert.True(status.GetDeviceBit(1, 13));
            Assert.False(status.GetDeviceBit(1, 9));
            Assert.False(status.GetDeviceBit(0, 10));
        }

        // Manuel basınç modunun bit olarak değil, DeviceStatus[2]'deki değer olarak okunduğunu doğrular.
        [Fact]
        public void ManuelBasincModu_DegerOlarakOkunur()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 160, 2);

            Assert.Equal(2, ScadaStatus.Parse(buffer).GetManualPressureMode());
        }

        // Yalnızca durumu 0 olan termokuplun geçerli sayıldığını doğrular (1 kopuk, 3 kanal kapalı).
        [Fact]
        public void IsTcValid_YalnizcaSaglamKanal()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 140 + 2 * 3, 1);   // Kanal 3 kopuk
            PutInt(buffer, 140 + 2 * 6, 3);   // Kanal 6 kapalı

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsTcValid(2));
            Assert.False(status.IsTcValid(3));
            Assert.False(status.IsTcValid(6));
            Assert.False(status.IsTcValid(8));   // Olmayan kanal
        }

        // Kilitleme, arıza ve haberleşme bitlerinin kendi adreslerinden okunduğunu doğrular.
        [Fact]
        public void KilitlemeArizaVeHaberlesmeBitleri()
        {
            var buffer = new byte[Size];
            PutInt(buffer, 280, 1 << 1);    // Isıtma isteniyor ama izin yok
            PutInt(buffer, 282, 1 << 8);    // Giriş/çıkış modülü arızası
            PutInt(buffer, 196, 1 << 3);    // Sürücü cevap veriyor

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.HasInterlock(1));
            Assert.False(status.HasInterlock(0));
            Assert.True(status.HasFault(8));
            Assert.False(status.HasFault(1));
            Assert.True(status.IsCommOk(3));
            Assert.False(status.IsCommOk(0));
        }
    }
}