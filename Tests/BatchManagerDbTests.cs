using Business.Concrete;
using DataAccess.Concrete.EntityFramework.Contexts;
using Xunit;

namespace Tests
{
    // PLC sekans durumuna göre şarj kaydının kapanmasını doğrulayan testler
    [Collection("Database")]
    public class BatchManagerDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public BatchManagerDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Verilen sekans durumlarını sırayla şarj yöneticisine bildirir.
        private static async Task TrackAsync(BatchManager manager, params short[] seqStates)
        {
            foreach (short seqState in seqStates)
            {
                var result = await manager.TrackSequenceAsync(seqState);
                Assert.True(result.IsSuccess);
            }
        }

        // Program bitince (SeqState 60) şarjın tamamlandı olarak kapandığını doğrular.
        [Fact]
        public async Task ProgramBitince_Tamamlandi()
        {
            int batchId = _database.InsertBatch("RUNNING");

            await TrackAsync(new BatchManager(), 30, 50, 60);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single(b => b.Id == batchId);
            Assert.Equal("COMPLETED", batch.Status);
            Assert.NotNull(batch.EndTime);
        }

        // Bittikten sonra reset verilip beklemeye dönülünce durumun değişmediğini doğrular.
        [Fact]
        public async Task BittiktenSonraReset_DurumDegismez()
        {
            int batchId = _database.InsertBatch("RUNNING");

            await TrackAsync(new BatchManager(), 30, 60, 0);

            using var context = new NorthwindContext();
            Assert.Equal("COMPLETED", context.Batches.Single(b => b.Id == batchId).Status);
        }

        // Program bitmeden beklemeye dönülürse (Stop) şarjın yarıda kesildi olarak kapandığını doğrular.
        [Fact]
        public async Task Durdurulunca_YaridaKesildi()
        {
            int batchId = _database.InsertBatch("RUNNING");

            await TrackAsync(new BatchManager(), 30, 40, 0);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single(b => b.Id == batchId);
            Assert.Equal("ABORTED", batch.Status);
            Assert.Contains("durduruldu", batch.CompletionNote);
        }

        // Azot yıkaması sürerken (SeqState 40) şarjın hâlâ açık olduğunu doğrular.
        [Fact]
        public async Task YikamaSurerken_SarjAcikKalir()
        {
            int batchId = _database.InsertBatch("RUNNING");

            await TrackAsync(new BatchManager(), 30, 40);

            using var context = new NorthwindContext();
            Assert.Equal("RUNNING", context.Batches.Single(b => b.Id == batchId).Status);
        }

        // Güvenli durdurma (SeqState 80) görüldüyse kapanış notunda bunun yazdığını doğrular.
        [Fact]
        public async Task GuvenliDurdurma_NottaBelirtilir()
        {
            int batchId = _database.InsertBatch("RUNNING");

            await TrackAsync(new BatchManager(), 30, 80, 40, 0);

            using var context = new NorthwindContext();
            var batch = context.Batches.Single(b => b.Id == batchId);
            Assert.Equal("ABORTED", batch.Status);
            Assert.Contains("Güvenli durdurma", batch.CompletionNote);
        }

        // SCADA açıldığında PLC beklemede ama açık şarj varsa sonucun bilinmiyor olarak kapandığını doğrular.
        [Fact]
        public async Task AcilistaBeklemede_SonucBilinmiyor()
        {
            int batchId = _database.InsertBatch("RUNNING");

            await TrackAsync(new BatchManager(), 0);

            using var context = new NorthwindContext();
            Assert.Equal("UNKNOWN", context.Batches.Single(b => b.Id == batchId).Status);
        }

        // SCADA açıldığında program hâlâ çalışıyorsa şarjın açık kaldığını doğrular.
        [Fact]
        public async Task AcilistaProgramCalisiyor_SarjAcikKalir()
        {
            int batchId = _database.InsertBatch("RUNNING");

            await TrackAsync(new BatchManager(), 30);

            using var context = new NorthwindContext();
            Assert.Equal("RUNNING", context.Batches.Single(b => b.Id == batchId).Status);
        }

        // Daha önce kapanmış şarjın yeniden değiştirilmediğini doğrular.
        [Fact]
        public async Task KapanmisSarja_Dokunulmaz()
        {
            int batchId = _database.InsertBatch("COMPLETED");

            await TrackAsync(new BatchManager(), 30, 0);

            using var context = new NorthwindContext();
            Assert.Equal("COMPLETED", context.Batches.Single(b => b.Id == batchId).Status);
        }
    }
}