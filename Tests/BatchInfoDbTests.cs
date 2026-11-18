using Business.Concrete;
using Xunit;

namespace Tests
{
    // Şarj ağırlığı ve şarj notlarının testleri
    [Collection("Database")]
    public class BatchInfoDbTests
    {
        private readonly DatabaseFixture _database;

        // Her testten önce tabloları boşaltır.
        public BatchInfoDbTests(DatabaseFixture database)
        {
            _database = database;
            _database.ClearData();
        }

        // Girilen ağırlığın kaydedildiğini ve sonradan düzeltilebildiğini doğrular.
        [Fact]
        public async Task Agirlik_KaydedilirVeDuzeltilir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var manager = new BatchInfoManager();

            Assert.True((await manager.SetLoadAsync(batchId, 850f)).IsSuccess);
            Assert.Equal(850f, (await manager.GetInfoAsync(batchId)).Data.LoadKg);

            Assert.True((await manager.SetLoadAsync(batchId, 920.5f)).IsSuccess);
            Assert.Equal(920.5f, (await manager.GetInfoAsync(batchId)).Data.LoadKg);
        }

        // Eksi ağırlığın reddedildiğini doğrular.
        [Fact]
        public async Task Agirlik_EksiDegerReddedilir()
        {
            int batchId = _database.InsertBatch("RUNNING");

            var result = await new BatchInfoManager().SetLoadAsync(batchId, -5f);

            Assert.False(result.IsSuccess);
        }

        // Notların eskiden yeniye sıralı döndüğünü ve baştaki sondaki boşlukların silindiğini doğrular.
        [Fact]
        public async Task Not_EklenirVeSiraylaDoner()
        {
            int batchId = _database.InsertBatch("COMPLETED");
            var manager = new BatchInfoManager();

            await manager.AddNoteAsync(batchId, 1, "  Sepet 2 katlı yüklendi  ");
            await manager.AddNoteAsync(batchId, 1, "Sertlik 62 HRC");

            var info = await manager.GetInfoAsync(batchId);

            Assert.Equal(2, info.Data.Notes.Count);
            Assert.Equal("Sepet 2 katlı yüklendi", info.Data.Notes[0].Text);
            Assert.Equal("Sertlik 62 HRC", info.Data.Notes[1].Text);
        }

        // Boş notun ve olmayan şarja notun reddedildiğini doğrular.
        [Fact]
        public async Task Not_BosVeOlmayanSarjReddedilir()
        {
            int batchId = _database.InsertBatch("RUNNING");
            var manager = new BatchInfoManager();

            Assert.False((await manager.AddNoteAsync(batchId, 1, "   ")).IsSuccess);
            Assert.False((await manager.AddNoteAsync(987654, 1, "not")).IsSuccess);
        }

        // Açık şarj numarasının çalışan şarjı verdiğini, kapanmış şarjı vermediğini doğrular.
        [Fact]
        public async Task AcikSarj_YalnizcaCalisaniVerir()
        {
            var manager = new BatchInfoManager();

            _database.InsertBatch("COMPLETED");
            Assert.Null(await manager.GetOpenBatchIdAsync());

            int running = _database.InsertBatch("RUNNING");
            Assert.Equal(running, await manager.GetOpenBatchIdAsync());
        }
    }
}