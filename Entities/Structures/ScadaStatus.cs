namespace Entities.Structures
{
    // DB201 (DB_ScadaStatus) bloğunun çözülmüş hali; SCADA bu bloğu yalnızca okur
    public class ScadaStatus
    {
        // Haberleşme ve komut cevabı
        public short PlcHeartbeat { get; set; }       // 0
        public short LayoutVersion { get; set; }      // 2
        public short AckCmdId { get; set; }           // 4
        public short AckResult { get; set; }          // 6
        public short AckDetail { get; set; }          // 8

        // Sekans durumu
        public short Mode { get; set; }               // 10: 0 kapalı, 1 manuel, 2 otomatik
        public short SeqState { get; set; }           // 12
        public short SeqMessage { get; set; }         // 14
        public short ActiveStep { get; set; }         // 16
        public short ActiveStepType { get; set; }     // 18: 1 vakum, 2 ısıtma, 3 soğutma
        public short StepElapsedMin { get; set; }     // 20
        public short StepRemainMin { get; set; }      // 22
        public short PrgElapsedMin { get; set; }      // 24
        public short PrgRemainMin { get; set; }       // 26
        public short HoldReason { get; set; }         // 28
        public short VacCycleNo { get; set; }         // 30
        public short ActiveRecipeID { get; set; }     // 32
        public short ActiveRecipeType { get; set; }   // 34
        public int PrgStartUnix { get; set; }         // 36
        public int PrgEndEstUnix { get; set; }        // 40
        public short Permits { get; set; }            // 44

        // Sıcaklıklar (°C)
        public float TempPota { get; set; }           // 48
        public float TempAlt { get; set; }            // 52
        public float TempUst { get; set; }            // 56
        public float TempPotaAlt { get; set; }        // 60
        public float TempPotaUst { get; set; }        // 64
        public float TempMalzeme { get; set; }        // 68
        public float TempOps1 { get; set; }           // 72
        public float TempOps2 { get; set; }           // 76
        public float TempSet { get; set; }            // 80

        // Basınç (mbar) ve gazlar (L/saat, %)
        public float Pressure { get; set; }           // 84
        public float PressureSet { get; set; }        // 88
        public float N2FlowAct { get; set; }          // 92
        public float N2FlowSet { get; set; }          // 96
        public float NH3FlowAct { get; set; }         // 100
        public float NH3FlowSet { get; set; }         // 104
        public float H2Act { get; set; }              // 108
        public float H2Set { get; set; }              // 112
        public float Kn { get; set; }                 // 116
        public float HeatOutAlt { get; set; }         // 120
        public float HeatOutUst { get; set; }         // 124
        public float FanSpeedAct { get; set; }        // 128
        public float N2Total { get; set; }            // 132
        public float NH3Total { get; set; }           // 136

        // Durum ve alarm bit dizileri
        public short[] TcStatus { get; set; } = new short[8];       // 140
        public short[] DeviceStatus { get; set; } = new short[4];   // 156
        public short[] AlarmActive { get; set; } = new short[8];    // 164
        public short[] AlarmUnack { get; set; } = new short[8];     // 180
        public short CommStatus { get; set; }                       // 196

        // Enerji ve sürücü (200–231 ve 240–247 henüz doldurulmuyor)
        public float[] Volt { get; set; } = new float[3];           // 200
        public float[] Cur { get; set; } = new float[3];            // 212
        public float PowerKw { get; set; }            // 224
        public float EnergyKwh { get; set; }          // 228
        public float VfdFreq { get; set; }            // 232
        public float VfdCurrent { get; set; }         // 236
        public float DpuAltLoad { get; set; }         // 240
        public float DpuUstLoad { get; set; }         // 244

        // DB201'in 248 baytlık ham verisini alanlara çözer.
        public static ScadaStatus Parse(byte[] buffer)
        {
            if (buffer == null || buffer.Length < ScadaLayout.StatusSize)
                throw new ArgumentException($"DB201 verisi {ScadaLayout.StatusSize} bayt olmalıdır.", nameof(buffer));

            return new ScadaStatus
            {
                PlcHeartbeat = S7Bytes.GetInt(buffer, 0),
                LayoutVersion = S7Bytes.GetInt(buffer, 2),
                AckCmdId = S7Bytes.GetInt(buffer, 4),
                AckResult = S7Bytes.GetInt(buffer, 6),
                AckDetail = S7Bytes.GetInt(buffer, 8),

                Mode = S7Bytes.GetInt(buffer, 10),
                SeqState = S7Bytes.GetInt(buffer, 12),
                SeqMessage = S7Bytes.GetInt(buffer, 14),
                ActiveStep = S7Bytes.GetInt(buffer, 16),
                ActiveStepType = S7Bytes.GetInt(buffer, 18),
                StepElapsedMin = S7Bytes.GetInt(buffer, 20),
                StepRemainMin = S7Bytes.GetInt(buffer, 22),
                PrgElapsedMin = S7Bytes.GetInt(buffer, 24),
                PrgRemainMin = S7Bytes.GetInt(buffer, 26),
                HoldReason = S7Bytes.GetInt(buffer, 28),
                VacCycleNo = S7Bytes.GetInt(buffer, 30),
                ActiveRecipeID = S7Bytes.GetInt(buffer, 32),
                ActiveRecipeType = S7Bytes.GetInt(buffer, 34),
                PrgStartUnix = S7Bytes.GetDInt(buffer, 36),
                PrgEndEstUnix = S7Bytes.GetDInt(buffer, 40),
                Permits = S7Bytes.GetInt(buffer, 44),

                TempPota = S7Bytes.GetReal(buffer, 48),
                TempAlt = S7Bytes.GetReal(buffer, 52),
                TempUst = S7Bytes.GetReal(buffer, 56),
                TempPotaAlt = S7Bytes.GetReal(buffer, 60),
                TempPotaUst = S7Bytes.GetReal(buffer, 64),
                TempMalzeme = S7Bytes.GetReal(buffer, 68),
                TempOps1 = S7Bytes.GetReal(buffer, 72),
                TempOps2 = S7Bytes.GetReal(buffer, 76),
                TempSet = S7Bytes.GetReal(buffer, 80),

                Pressure = S7Bytes.GetReal(buffer, 84),
                PressureSet = S7Bytes.GetReal(buffer, 88),
                N2FlowAct = S7Bytes.GetReal(buffer, 92),
                N2FlowSet = S7Bytes.GetReal(buffer, 96),
                NH3FlowAct = S7Bytes.GetReal(buffer, 100),
                NH3FlowSet = S7Bytes.GetReal(buffer, 104),
                H2Act = S7Bytes.GetReal(buffer, 108),
                H2Set = S7Bytes.GetReal(buffer, 112),
                Kn = S7Bytes.GetReal(buffer, 116),
                HeatOutAlt = S7Bytes.GetReal(buffer, 120),
                HeatOutUst = S7Bytes.GetReal(buffer, 124),
                FanSpeedAct = S7Bytes.GetReal(buffer, 128),
                N2Total = S7Bytes.GetReal(buffer, 132),
                NH3Total = S7Bytes.GetReal(buffer, 136),

                TcStatus = S7Bytes.GetIntArray(buffer, 140, 8),
                DeviceStatus = S7Bytes.GetIntArray(buffer, 156, 4),
                AlarmActive = S7Bytes.GetIntArray(buffer, 164, 8),
                AlarmUnack = S7Bytes.GetIntArray(buffer, 180, 8),
                CommStatus = S7Bytes.GetInt(buffer, 196),

                Volt = S7Bytes.GetRealArray(buffer, 200, 3),
                Cur = S7Bytes.GetRealArray(buffer, 212, 3),
                PowerKw = S7Bytes.GetReal(buffer, 224),
                EnergyKwh = S7Bytes.GetReal(buffer, 228),
                VfdFreq = S7Bytes.GetReal(buffer, 232),
                VfdCurrent = S7Bytes.GetReal(buffer, 236),
                DpuAltLoad = S7Bytes.GetReal(buffer, 240),
                DpuUstLoad = S7Bytes.GetReal(buffer, 244)
            };
        }

        // Verilen numaralı alarmın (1'den başlar) aktif olup olmadığını döner.
        public bool IsAlarmActive(int alarmNo)
        {
            return GetAlarmBit(AlarmActive, alarmNo);
        }

        // Verilen numaralı alarmın (1'den başlar) görülmemiş olup olmadığını döner.
        public bool IsAlarmUnack(int alarmNo)
        {
            return GetAlarmBit(AlarmUnack, alarmNo);
        }

        // İzin bitinin dolu olup olmadığını döner (0 başlat, 1 kapak, 2 amonyak, 3 ısıtma, 4 su).
        public bool HasPermit(int bit)
        {
            return S7Bytes.GetBit(Permits, bit);
        }

        // DeviceStatus dizisindeki bir cihaz bitinin dolu olup olmadığını döner.
        public bool GetDeviceBit(int wordIndex, int bit)
        {
            return S7Bytes.GetBit(DeviceStatus[wordIndex], bit);
        }

        // Alarm numarasını kelime ve bit konumuna çevirip ilgili biti okur.
        private static bool GetAlarmBit(short[] words, int alarmNo)
        {
            if (alarmNo < 1 || alarmNo > words.Length * 16)
                throw new ArgumentOutOfRangeException(nameof(alarmNo), "Alarm numarası 1–128 aralığında olmalıdır.");

            int word = (alarmNo - 1) / 16;
            int bit = (alarmNo - 1) % 16;
            return S7Bytes.GetBit(words[word], bit);
        }
    }
}