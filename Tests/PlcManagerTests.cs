using Business.Concrete;
using DataAccess.Abstract;
using Entities.Enums;
using Entities.Structures;
using S7.Net;
using System.Buffers.Binary;
using Xunit;

namespace Tests
{
    // PLC'yi bellekte taklit eden sahte sürücü: komut yazılınca DB201'e cevap yazar
    internal class FakeS7Driver : IS7DriverDal
    {
        private readonly Dictionary<int, byte[]> _dbs = new()
        {
            { 200, new byte[20] },
            { 201, new byte[248] },
            { 202, new byte[618] },
            { 203, new byte[618] },
            { 204, new byte[100] }
        };

        // PLC'nin komuta vereceği sonuç; null ise PLC hiç cevap vermez
        public short? AckResult { get; set; } = 1;

        // Yapılan toplam yazma sayısı
        public int WriteCount { get; private set; }

        public bool IsConnected => true;

        // Şema sürümünü DB201 ve DB204'e 3 olarak yazar.
        public FakeS7Driver()
        {
            SetInt(201, 2, 3);
            SetInt(204, 0, 3);
        }

        // Her zaman bağlanmış sayar.
        public Task<bool> ConnectAsync(string ipAddress, CpuType cpuType = CpuType.S71200, short rack = 0, short slot = 1)
        {
            return Task.FromResult(true);
        }

        // Sahte sürücüde bağlantı kesmenin etkisi yoktur.
        public void Disconnect()
        {
        }

        // İstenen DB'nin verilen adresinden bayt kopyası döner.
        public Task<byte[]> ReadBytesAsync(int db, int startByte, int count)
        {
            var result = new byte[count];
            Array.Copy(_dbs[db], startByte, result, 0, count);
            return Task.FromResult(result);
        }

        // Baytları DB'ye yazar; DB200'e komut yazıldıysa PLC gibi DB201'e cevabı yazar.
        public Task WriteBytesAsync(int db, int startByte, byte[] data)
        {
            Array.Copy(data, 0, _dbs[db], startByte, data.Length);
            WriteCount++;

            if (db == 200 && startByte == 0 && AckResult.HasValue)
            {
                SetInt(201, 4, GetInt(200, 0));     // AckCmdId = CmdId
                SetInt(201, 6, AckResult.Value);    // AckResult
            }

            return Task.CompletedTask;
        }

        // Verilen DB adresindeki Int değeri okur.
        public short GetInt(int db, int address)
        {
            return BinaryPrimitives.ReadInt16BigEndian(_dbs[db].AsSpan(address, 2));
        }

        // Verilen DB adresine Int değeri yazar.
        public void SetInt(int db, int address, short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(_dbs[db].AsSpan(address, 2), value);
        }
    }

    // Komut el sıkışması, sürüm kontrolü ve yaşam sinyali testleri
    public class PlcManagerTests
    {
        // Kabul edilen komutun başarılı döndüğünü ve DB200'e doğru yazıldığını doğrular.
        [Fact]
        public async Task SendCommand_KabulEdilir()
        {
            var driver = new FakeS7Driver();
            var manager = new PlcManager(driver);

            var result = await manager.SendCommandAsync(ScadaCommandCode.GotoStep, 3, 15, 0);

            Assert.True(result.IsSuccess);
            Assert.True(result.Data.Accepted);
            Assert.Equal(6, driver.GetInt(200, 2));    // CmdCode
            Assert.Equal(3, driver.GetInt(200, 4));    // Param1
            Assert.Equal(15, driver.GetInt(200, 6));   // Param2
            Assert.Equal(0, driver.GetInt(200, 8));    // Param3
        }

        // Komut numarasının PLC'de duran değerden bir artırılarak devam ettiğini doğrular.
        [Fact]
        public async Task SendCommand_CmdIdMevcutDegerdenDevamEder()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(200, 0, 41);
            var manager = new PlcManager(driver);

            var result = await manager.SendCommandAsync(ScadaCommandCode.BuzzerMute);

            Assert.Equal(42, driver.GetInt(200, 0));
            Assert.Equal(42, result.Data.CmdId);
        }

        // Art arda iki komutta numaranın her seferinde arttığını doğrular.
        [Fact]
        public async Task SendCommand_HerKomuttaCmdIdArtar()
        {
            var driver = new FakeS7Driver();
            var manager = new PlcManager(driver);

            await manager.SendCommandAsync(ScadaCommandCode.BuzzerMute);
            await manager.SendCommandAsync(ScadaCommandCode.BuzzerMute);

            Assert.Equal(2, driver.GetInt(200, 0));
        }

        // Komut numarasının 32767'den sonra 1'e döndüğünü doğrular.
        [Fact]
        public async Task SendCommand_CmdId32767denSonraBireDoner()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(200, 0, short.MaxValue);
            var manager = new PlcManager(driver);

            await manager.SendCommandAsync(ScadaCommandCode.BuzzerMute);

            Assert.Equal(1, driver.GetInt(200, 0));
        }

        // PLC'de aynı numarayla eski bir cevap duruyorsa bir numara atlandığını doğrular.
        [Fact]
        public async Task SendCommand_EskiCevaplaAyniNumaraAtlanir()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(200, 0, 5);    // Sıradaki numara 6 olacaktı
            driver.SetInt(201, 4, 6);    // Ama PLC'de 6 numaralı eski bir cevap duruyor
            driver.SetInt(201, 6, 1);
            var manager = new PlcManager(driver);

            await manager.SendCommandAsync(ScadaCommandCode.BuzzerMute);

            Assert.Equal(7, driver.GetInt(200, 0));
        }

        // PLC'nin reddettiği komutun ret koduyla ve açıklamasıyla döndüğünü doğrular.
        [Fact]
        public async Task SendCommand_RetKoduDoner()
        {
            var driver = new FakeS7Driver { AckResult = 101 };
            var manager = new PlcManager(driver);

            var result = await manager.SendCommandAsync(ScadaCommandCode.Start);

            Assert.False(result.IsSuccess);
            Assert.Equal(101, (int)result.Data.ResultCode);
            Assert.False(result.Data.Accepted);
            Assert.Equal("Aktif alarm var", result.Message);
        }

        // PLC cevap vermezse 2 saniye sonra haberleşme hatası döndüğünü doğrular.
        [Fact]
        public async Task SendCommand_CevapGelmezseZamanAsimi()
        {
            var driver = new FakeS7Driver { AckResult = null };
            var manager = new PlcManager(driver);

            var result = await manager.SendCommandAsync(ScadaCommandCode.Start);

            Assert.False(result.IsSuccess);
            Assert.Null(result.Data);
            Assert.Contains("cevap gelmedi", result.Message);
        }

        // DB204'teki şema sürümü 3 değilse komutun hiç yazılmadığını doğrular.
        [Fact]
        public async Task SendCommand_SurumUyusmazsaYazilmaz()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(204, 0, 2);
            var manager = new PlcManager(driver);

            var result = await manager.SendCommandAsync(ScadaCommandCode.Start);

            Assert.False(result.IsSuccess);
            Assert.Equal(0, driver.WriteCount);
        }

        // DB201'deki şema sürümü 3 değilse durum okumanın hata döndüğünü doğrular.
        [Fact]
        public async Task ReadStatus_SurumUyusmazsaHata()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(201, 2, 4);
            var manager = new PlcManager(driver);

            var result = await manager.ReadStatusAsync();

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Data);
            Assert.Equal(4, (int)result.Data.LayoutVersion);
        }

        // Sürüm doğruysa durum okumanın başarılı döndüğünü doğrular.
        [Fact]
        public async Task ReadStatus_SurumDogruysaBasarili()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(201, 12, 30);   // SeqState = ısıtma
            var manager = new PlcManager(driver);

            var result = await manager.ReadStatusAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(30, (int)result.Data.SeqState);
        }

        // Yaşam sinyalinin DB200 adres 10'a yazıldığını ve her çağrıda arttığını doğrular.
        [Fact]
        public async Task WriteHeartbeat_Adres10aYazarVeArtar()
        {
            var driver = new FakeS7Driver();
            var manager = new PlcManager(driver);

            await manager.WriteHeartbeatAsync();
            await manager.WriteHeartbeatAsync();

            Assert.Equal(2, driver.GetInt(200, 10));
            Assert.Equal(0, driver.GetInt(200, 0));    // Komut alanına dokunulmamış
        }

        // Sürüm uyuşmuyorsa yaşam sinyalinin yazılmadığını doğrular.
        [Fact]
        public async Task WriteHeartbeat_SurumUyusmazsaYazilmaz()
        {
            var driver = new FakeS7Driver();
            driver.SetInt(201, 2, 2);
            var manager = new PlcManager(driver);

            var result = await manager.WriteHeartbeatAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(0, driver.WriteCount);
        }
    }
}