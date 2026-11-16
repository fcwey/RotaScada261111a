using Entities.Concrete;

namespace Entities.Structures
{
    // DB205'in çözülmüş hali: başlık ve çalışmış adımların özet satırları
    public class StepLogData
    {
        public short LayoutVersion { get; set; }    // Tablonun kendi sürümü (1)
        public short RecipeId { get; set; }
        public int BatchId { get; set; }            // Özetlerin ait olduğu şarj
        public short ActiveRow { get; set; }        // Şu an dolan satır; 0 = yok
        public List<BatchStepSummary> Rows { get; set; } = new();
    }

    // DB205 (DB_StepLog, 1212 bayt: 12 bayt başlık + 20 x 60 bayt satır) bloğunu çözer
    public static class StepLogCodec
    {
        public const int HeaderSize = 12;
        public const int RowSize = 60;
        public const int MaxRows = 20;

        // Ham veriyi başlık ve satırlara çözer; hiç çalışmamış adımların (StepNo 0) satırları alınmaz.
        public static StepLogData Decode(byte[] buffer)
        {
            if (buffer == null || buffer.Length < ScadaLayout.StepLogSize)
                throw new ArgumentException($"DB205 verisi {ScadaLayout.StepLogSize} bayt olmalıdır.", nameof(buffer));

            var data = new StepLogData
            {
                LayoutVersion = S7Bytes.GetInt(buffer, 0),
                RecipeId = S7Bytes.GetInt(buffer, 2),
                BatchId = S7Bytes.GetDInt(buffer, 4),
                ActiveRow = S7Bytes.GetInt(buffer, 8)
            };

            for (int row = 0; row < MaxRows; row++)
            {
                int offset = HeaderSize + row * RowSize;

                short stepNo = S7Bytes.GetInt(buffer, offset + 0);
                if (stepNo == 0)
                    continue;

                data.Rows.Add(new BatchStepSummary
                {
                    StepNo = stepNo,
                    StepType = S7Bytes.GetInt(buffer, offset + 2),
                    Flags = (ushort)S7Bytes.GetInt(buffer, offset + 4),
                    DurationMin = S7Bytes.GetInt(buffer, offset + 6),
                    GsoakWaitMin = S7Bytes.GetInt(buffer, offset + 8),
                    HoldMin = S7Bytes.GetInt(buffer, offset + 10),
                    H2InBandMin = S7Bytes.GetInt(buffer, offset + 12),
                    TempAvg = S7Bytes.GetReal(buffer, offset + 16),
                    TempMin = S7Bytes.GetReal(buffer, offset + 20),
                    TempMax = S7Bytes.GetReal(buffer, offset + 24),
                    KnAvg = S7Bytes.GetReal(buffer, offset + 28),
                    KnMin = S7Bytes.GetReal(buffer, offset + 32),
                    KnMax = S7Bytes.GetReal(buffer, offset + 36),
                    H2Avg = S7Bytes.GetReal(buffer, offset + 40),
                    N2L = S7Bytes.GetReal(buffer, offset + 44),
                    NH3L = S7Bytes.GetReal(buffer, offset + 48),
                    CO2L = S7Bytes.GetReal(buffer, offset + 52),
                    EnergyKwh = S7Bytes.GetReal(buffer, offset + 56)
                });
            }

            return data;
        }
    }
}