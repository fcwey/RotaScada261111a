using Entities.Structures;
using System.Buffers.Binary;
using Xunit;

namespace Tests
{
    // DB201 durum bloğunun kılavuzdaki adreslerden çözüldüğünü doğrulayan testler
    public class ScadaStatusTests
    {
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

        // Her alanına kendi adresinden türetilmiş değer yazılmış 248 baytlık örnek blok üretir.
        private static byte[] BuildBuffer()
        {
            var buffer = new byte[248];

            // Int alanlar: adres + 1000
            foreach (int address in new[] { 0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 44, 196 })
                PutInt(buffer, address, (short)(address + 1000));

            // Int diziler: TcStatus, DeviceStatus, AlarmActive, AlarmUnack
            for (int address = 140; address < 196; address += 2)
                PutInt(buffer, address, (short)(address + 1000));

            // DInt alanlar: adres + 100000
            PutDInt(buffer, 36, 100036);
            PutDInt(buffer, 40, 100040);

            // Real alanlar: adres + 0,5
            for (int address = 48; address < 140; address += 4)
                PutReal(buffer, address, address + 0.5f);
            for (int address = 200; address < 248; address += 4)
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

        // 248 bayttan kısa veride hata fırlatıldığını doğrular.
        [Fact]
        public void Parse_KisaVerideHataFirlatir()
        {
            Assert.Throws<ArgumentException>(() => ScadaStatus.Parse(new byte[247]));
        }

        // Kılavuzdaki örneği doğrular: alarm 22 → kelime 1, bit 5 (adres 166).
        [Fact]
        public void IsAlarmActive_Alarm22Kelime1Bit5()
        {
            var buffer = new byte[248];
            PutInt(buffer, 166, 1 << 5);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsAlarmActive(22));
            Assert.False(status.IsAlarmActive(21));
            Assert.False(status.IsAlarmActive(23));
        }

        // Kelime sınırlarını doğrular: alarm 16 → kelime 0 bit 15, alarm 17 → kelime 1 bit 0.
        [Fact]
        public void IsAlarmActive_KelimeSinirlari()
        {
            var buffer = new byte[248];
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
            var buffer = new byte[248];
            PutInt(buffer, 180, 1);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.IsAlarmUnack(1));
            Assert.False(status.IsAlarmActive(1));
        }

        // İzin bitlerinin adres 44'teki Int'ten okunduğunu doğrular.
        [Fact]
        public void HasPermit_BitleriDogruOkur()
        {
            var buffer = new byte[248];
            PutInt(buffer, 44, 0b01101);   // bit 0, 2 ve 3 dolu

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.HasPermit(0));
            Assert.False(status.HasPermit(1));
            Assert.True(status.HasPermit(2));
            Assert.True(status.HasPermit(3));
            Assert.False(status.HasPermit(4));
        }

        // Cihaz bitlerinin doğru kelimeden okunduğunu doğrular (DeviceStatus[1] bit 9 = otomatik mod).
        [Fact]
        public void GetDeviceBit_DogruKelimedenOkur()
        {
            var buffer = new byte[248];
            PutInt(buffer, 158, 1 << 9);

            var status = ScadaStatus.Parse(buffer);

            Assert.True(status.GetDeviceBit(1, 9));
            Assert.False(status.GetDeviceBit(0, 9));
        }
    }
}