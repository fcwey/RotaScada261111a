using Entities.Structures;
using System.Buffers.Binary;
using Xunit;

namespace Tests
{
    // Sahte PLC'nin DB201 ve DB205 alanlarını doldurmak için yardımcılar
    internal static class FakePlcData
    {
        // Verilen DB adresine DInt değeri yazar.
        public static void PutDInt(FakeS7Driver driver, int db, int address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            driver.WriteBytesAsync(db, address, bytes).Wait();
        }

        // Verilen DB adresine Real değeri yazar.
        public static void PutReal(FakeS7Driver driver, int db, int address, float value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteSingleBigEndian(bytes, value);
            driver.WriteBytesAsync(db, address, bytes).Wait();
        }

        // DB201'e sekans durumunu, şarj numarasını ve bitiş sebebini yazar.
        public static void SetStatus(FakeS7Driver driver, short seqState, int batchId, short endReason)
        {
            driver.SetInt(201, 12, seqState);
            PutDInt(driver, 201, 248, batchId);
            driver.SetInt(201, 252, endReason);
        }

        // DB205 başlığına reçete ve şarj numarasını yazar.
        public static void SetStepLogHeader(FakeS7Driver driver, int batchId, short recipeId)
        {
            driver.SetInt(205, 2, recipeId);
            PutDInt(driver, 205, 4, batchId);
        }

        // DB205'in verilen satırına (satır no = adım no) örnek bir adım özeti yazar.
        public static void SetStepRow(FakeS7Driver driver, int rowNo, short stepType, short flags, short durationMin, float tempAvg, float knAvg, float nh3L, float energyKwh)
        {
            int offset = 12 + (rowNo - 1) * 60;

            driver.SetInt(205, offset + 0, (short)rowNo);
            driver.SetInt(205, offset + 2, stepType);
            driver.SetInt(205, offset + 4, flags);
            driver.SetInt(205, offset + 6, durationMin);
            PutReal(driver, 205, offset + 16, tempAvg);
            PutReal(driver, 205, offset + 28, knAvg);
            PutReal(driver, 205, offset + 48, nh3L);
            PutReal(driver, 205, offset + 56, energyKwh);
        }
    }

    // DB205 adım özeti tablosunun çözülmesini doğrulayan testler
    public class StepLogCodecTests
    {
        // Sahte PLC'deki DB205'in tamamını okur.
        private static byte[] ReadStepLog(FakeS7Driver driver)
        {
            return driver.ReadBytesAsync(205, 0, 1212).Result;
        }

        // Başlık alanlarının doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void Decode_BaslikDogruAdresten()
        {
            var driver = new FakeS7Driver();
            FakePlcData.SetStepLogHeader(driver, 70000, 12);
            driver.SetInt(205, 8, 3);

            var data = StepLogCodec.Decode(ReadStepLog(driver));

            Assert.Equal(1, (int)data.LayoutVersion);
            Assert.Equal(12, (int)data.RecipeId);
            Assert.Equal(70000, data.BatchId);      // 16 bite sığmayan şarj numarası
            Assert.Equal(3, (int)data.ActiveRow);
        }

        // Satır alanlarının satır başına göre doğru adreslerden okunduğunu doğrular (2. satır = adres 72).
        [Fact]
        public void Decode_SatirAlanlariDogruAdresten()
        {
            var driver = new FakeS7Driver();
            int offset = 12 + 60;

            driver.SetInt(205, offset + 0, 2);       // StepNo
            driver.SetInt(205, offset + 2, 2);       // StepType
            driver.SetInt(205, offset + 4, 5);       // Flags: tamamlandı + H2 otomatik
            driver.SetInt(205, offset + 6, 130);     // DurationMin
            driver.SetInt(205, offset + 8, 7);       // GsoakWaitMin
            driver.SetInt(205, offset + 10, 3);      // HoldMin
            driver.SetInt(205, offset + 12, 110);    // H2InBandMin
            for (int i = 0; i < 11; i++)
                FakePlcData.PutReal(driver, 205, offset + 16 + i * 4, 100.5f + i);

            var row = StepLogCodec.Decode(ReadStepLog(driver)).Rows.Single();

            Assert.Equal(2, row.StepNo);
            Assert.Equal(2, row.StepType);
            Assert.Equal(5, row.Flags);
            Assert.Equal(130, row.DurationMin);
            Assert.Equal(7, row.GsoakWaitMin);
            Assert.Equal(3, row.HoldMin);
            Assert.Equal(110, row.H2InBandMin);
            Assert.Equal(100.5f, row.TempAvg);
            Assert.Equal(101.5f, row.TempMin);
            Assert.Equal(102.5f, row.TempMax);
            Assert.Equal(103.5f, row.KnAvg);
            Assert.Equal(104.5f, row.KnMin);
            Assert.Equal(105.5f, row.KnMax);
            Assert.Equal(106.5f, row.H2Avg);
            Assert.Equal(107.5f, row.N2L);
            Assert.Equal(108.5f, row.NH3L);
            Assert.Equal(109.5f, row.CO2L);
            Assert.Equal(110.5f, row.EnergyKwh);
        }

        // Hiç çalışmamış adımların (StepNo 0) satırlarının alınmadığını doğrular.
        [Fact]
        public void Decode_BosSatirlarAlinmaz()
        {
            var driver = new FakeS7Driver();
            FakePlcData.SetStepRow(driver, 1, 1, 1, 12, 30f, 0f, 0f, 0.2f);
            FakePlcData.SetStepRow(driver, 3, 2, 1, 240, 520f, 3.2f, 3100f, 41f);

            var rows = StepLogCodec.Decode(ReadStepLog(driver)).Rows;

            Assert.Equal(new[] { 1, 3 }, rows.Select(r => r.StepNo).ToArray());
        }

        // Son satırın (20. adım) blok sınırı içinde okunduğunu doğrular.
        [Fact]
        public void Decode_YirminciSatirOkunur()
        {
            var driver = new FakeS7Driver();
            FakePlcData.SetStepRow(driver, 20, 3, 1, 95, 180f, 0f, 0f, 1.5f);

            var row = StepLogCodec.Decode(ReadStepLog(driver)).Rows.Single();

            Assert.Equal(20, row.StepNo);
            Assert.Equal(1.5f, row.EnergyKwh);
        }

        // 1212 bayttan kısa veride hata fırlatıldığını doğrular.
        [Fact]
        public void Decode_KisaVerideHataFirlatir()
        {
            Assert.Throws<ArgumentException>(() => StepLogCodec.Decode(new byte[1211]));
        }
    }
}