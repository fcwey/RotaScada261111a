namespace Entities.Structures
{
    // DB201 (DB_ScadaStatus, 336 bayt) bloğunun çözülmüş hali; SCADA bu bloğu yalnızca okur
    public class ScadaStatus
    {
        // Arızalı termokuplda PLC'nin yazdığı sıcaklık değeri; gösterimden önce TcStatus'a bakılır
        public const float TcFaultValue = 3276.7f;

        // Haberleşme ve komut cevabı
        public short PlcHeartbeat { get; set; }       // 0
        public short LayoutVersion { get; set; }      // 2
        public short AckCmdId { get; set; }           // 4
        public short AckResult { get; set; }          // 6
        public short AckDetail { get; set; }          // 8

        // Program durumu
        public short Mode { get; set; }               // 10: 0 kapalı, 1 manuel, 2 otomatik
        public short SeqState { get; set; }           // 12: 0, 20, 30, 40, 45, 50, 60, 70, 80
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
        public int PrgStartUnix { get; set; }         // 36 (PLC yerel saati)
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
        public float N2Total { get; set; }            // 132: şarjda MFC'den geçen azot (L)
        public float NH3Total { get; set; }           // 136: şarjda geçen amonyak (L)

        // Durum ve alarm bit dizileri (bitler işaretsiz 16 bit olarak yorumlanır)
        public short[] TcStatus { get; set; } = new short[8];       // 140: 0 sağlam, 1 kopuk, 2 aralık dışı, 3 kanal kapalı
        public short[] DeviceStatus { get; set; } = new short[4];   // 156: [0] ve [1] bit, [2] manuel basınç modu
        public short[] AlarmActive { get; set; } = new short[8];    // 164
        public short[] AlarmUnack { get; set; } = new short[8];     // 180
        public short CommStatus { get; set; }                       // 196

        // Elektrik ölçümleri (geçerlilik için CommStatus'a bakılır)
        public float[] Volt { get; set; } = new float[3];           // 200
        public float[] Cur { get; set; } = new float[3];            // 212
        public float PowerKw { get; set; }            // 224
        public float EnergyKwh { get; set; }          // 228
        public float VfdFreq { get; set; }            // 232
        public float VfdCurrent { get; set; }         // 236
        public float DpuAltLoad { get; set; }         // 240
        public float DpuUstLoad { get; set; }         // 244

        // Şarj, PID ve arıza alanları
        public int BatchId { get; set; }              // 248: Start komutuyla verilen şarj numarası
        public short EndReason { get; set; }          // 252: 0 yok, 1 tamamlandı, 2 stop, 3 güvenli durdurma
        public short EndCount { get; set; }           // 254: her program bitişinde 1 artar
        public float BatchEnergyKwh { get; set; }     // 256
        public short PidState { get; set; }           // 260
        public short TuneStatus { get; set; }         // 262
        public int PidError { get; set; }             // 264
        public float PidGain { get; set; }            // 268
        public float PidTi { get; set; }              // 272
        public float PidTd { get; set; }              // 276
        public short InterlockBits { get; set; }      // 280: istek var ama izin yok
        public short FaultBits { get; set; }          // 282
        public short DpuAltStatus { get; set; }       // 284
        public short DpuUstStatus { get; set; }       // 286

        // Sayaçlar
        public float CO2Total { get; set; }           // 288: şarjda verilen CO2 (L, hesap)
        public float WaterTotal { get; set; }         // 292: şarjda verilen su (L, hesap)
        public float FillN2Total { get; set; }        // 296: vakum dolumunda harcanan azot (L, hesap)
        public float CircHours { get; set; }          // 300
        public float CoolFanHours { get; set; }       // 304
        public float VacPumpHours { get; set; }       // 308
        public float HydPumpHours { get; set; }       // 312
        public float HeatHours { get; set; }          // 316
        public float EmgN2Total { get; set; }         // 320: şarjda acil azot hattından giden azot (L, hesap)

        // Kaçak testi
        public short LeakState { get; set; }          // 324: 0 boşta, 1 vakum, 2 dinlenme, 3 ölçüm, 4 dolum
        public short LeakResult { get; set; }         // 326
        public float LeakRise { get; set; }           // 328: mbar
        public short LeakRemainSec { get; set; }      // 332
        public short LeakCount { get; set; }          // 334: her test sonuçlandığında 1 artar

        // DB201'in 336 baytlık ham verisini alanlara çözer.
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
                DpuUstLoad = S7Bytes.GetReal(buffer, 244),

                BatchId = S7Bytes.GetDInt(buffer, 248),
                EndReason = S7Bytes.GetInt(buffer, 252),
                EndCount = S7Bytes.GetInt(buffer, 254),
                BatchEnergyKwh = S7Bytes.GetReal(buffer, 256),
                PidState = S7Bytes.GetInt(buffer, 260),
                TuneStatus = S7Bytes.GetInt(buffer, 262),
                PidError = S7Bytes.GetDInt(buffer, 264),
                PidGain = S7Bytes.GetReal(buffer, 268),
                PidTi = S7Bytes.GetReal(buffer, 272),
                PidTd = S7Bytes.GetReal(buffer, 276),
                InterlockBits = S7Bytes.GetInt(buffer, 280),
                FaultBits = S7Bytes.GetInt(buffer, 282),
                DpuAltStatus = S7Bytes.GetInt(buffer, 284),
                DpuUstStatus = S7Bytes.GetInt(buffer, 286),

                CO2Total = S7Bytes.GetReal(buffer, 288),
                WaterTotal = S7Bytes.GetReal(buffer, 292),
                FillN2Total = S7Bytes.GetReal(buffer, 296),
                CircHours = S7Bytes.GetReal(buffer, 300),
                CoolFanHours = S7Bytes.GetReal(buffer, 304),
                VacPumpHours = S7Bytes.GetReal(buffer, 308),
                HydPumpHours = S7Bytes.GetReal(buffer, 312),
                HeatHours = S7Bytes.GetReal(buffer, 316),
                EmgN2Total = S7Bytes.GetReal(buffer, 320),

                LeakState = S7Bytes.GetInt(buffer, 324),
                LeakResult = S7Bytes.GetInt(buffer, 326),
                LeakRise = S7Bytes.GetReal(buffer, 328),
                LeakRemainSec = S7Bytes.GetInt(buffer, 332),
                LeakCount = S7Bytes.GetInt(buffer, 334)
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

        // DeviceStatus dizisindeki bir cihaz bitinin dolu olup olmadığını döner (kelime 0 ve 1).
        public bool GetDeviceBit(int wordIndex, int bit)
        {
            return S7Bytes.GetBit(DeviceStatus[wordIndex], bit);
        }

        // Manuel basınç modunu döner: 0 atmosfere açık, 1 otomatik kontrol, 2 elle vana.
        public short GetManualPressureMode()
        {
            return DeviceStatus[2];
        }

        // Termokupl kanalının (0–7) sağlam olup olmadığını döner; sağlam değilse sıcaklığı gösterilmemelidir.
        public bool IsTcValid(int channel)
        {
            return channel >= 0 && channel < TcStatus.Length && TcStatus[channel] == 0;
        }

        // Cihaz isteniyor ama izni yok bitini döner (0 vakum, 1 ısıtma, 2 fan soğutma, 3 sirkülasyon, 4 kapak).
        public bool HasInterlock(int bit)
        {
            return S7Bytes.GetBit(InterlockBits, bit);
        }

        // Arıza bitini döner (0 vakum motoru, 1 sirkülasyon, 2 fan soğutma, 3 hidrolik, 4 azot MFC, 5 amonyak MFC, 6 eşanjör vanası, 7 ısıtma sensörü, 8 giriş/çıkış modülü).
        public bool HasFault(int bit)
        {
            return S7Bytes.GetBit(FaultBits, bit);
        }

        // Modbus okuma işinin cevap alıp almadığını döner (CommStatus bitleri, kılavuz bölüm 6).
        public bool IsCommOk(int bit)
        {
            return S7Bytes.GetBit(CommStatus, bit);
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