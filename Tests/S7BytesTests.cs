using Entities.Structures;
using Xunit;

namespace Tests
{
    // S7 big-endian bayt okuma ve yazma yardımcılarının testleri
    public class S7BytesTests
    {
        // Int değerin yüksek bayt önce okunduğunu doğrular.
        [Fact]
        public void GetInt_YuksekBaytOnceOkur()
        {
            Assert.Equal((short)258, S7Bytes.GetInt(new byte[] { 0x01, 0x02 }, 0));
        }

        // İşaret biti dolu Int değerin negatif okunduğunu doğrular.
        [Fact]
        public void GetInt_NegatifDegeriDogruOkur()
        {
            Assert.Equal((short)-2, S7Bytes.GetInt(new byte[] { 0xFF, 0xFE }, 0));
        }

        // DInt değerin dört bayttan big-endian okunduğunu doğrular.
        [Fact]
        public void GetDInt_DortBaytiBigEndianOkur()
        {
            Assert.Equal(65536, S7Bytes.GetDInt(new byte[] { 0x00, 0x01, 0x00, 0x00 }, 0));
        }

        // Real değerin IEEE 754 big-endian okunduğunu doğrular (12,5 = 41 48 00 00).
        [Fact]
        public void GetReal_BigEndianOkur()
        {
            Assert.Equal(12.5f, S7Bytes.GetReal(new byte[] { 0x41, 0x48, 0x00, 0x00 }, 0));
        }

        // PLC'den NaN gelirse 0 döndüğünü doğrular.
        [Fact]
        public void GetReal_NaNGelirseSifirDoner()
        {
            Assert.Equal(0f, S7Bytes.GetReal(new byte[] { 0x7F, 0xC0, 0x00, 0x00 }, 0));
        }

        // PLC'den sonsuz gelirse 0 döndüğünü doğrular.
        [Fact]
        public void GetReal_SonsuzGelirseSifirDoner()
        {
            Assert.Equal(0f, S7Bytes.GetReal(new byte[] { 0x7F, 0x80, 0x00, 0x00 }, 0));
        }

        // Int yazarken yüksek baytın önce yazıldığını doğrular (325 = 01 45).
        [Fact]
        public void SetInt_YuksekBaytiOnceYazar()
        {
            var buffer = new byte[4];
            S7Bytes.SetInt(buffer, 2, 325);

            Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x45 }, buffer);
        }

        // Bit 15 dolu (negatif) değerde bit testinin doğru çalıştığını doğrular.
        [Fact]
        public void GetBit_Bit15DoluykenDogruCalisir()
        {
            short word = unchecked((short)0x8000);

            Assert.True(S7Bytes.GetBit(word, 15));
            Assert.False(S7Bytes.GetBit(word, 0));
        }

        // Art arda duran Int dizisinin doğru adreslerden okunduğunu doğrular.
        [Fact]
        public void GetIntArray_ArdisikDegerleriOkur()
        {
            var buffer = new byte[] { 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03 };

            Assert.Equal(new short[] { 1, 2, 3 }, S7Bytes.GetIntArray(buffer, 1, 3));
        }
    }
}