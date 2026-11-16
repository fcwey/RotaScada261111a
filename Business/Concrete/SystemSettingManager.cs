using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Abstract;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Dtos;
using Entities.Structures;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class SystemSettingManager : ISystemSettingService
    {
        private const int ReadBackDelayMs = 150;            // PLC'nin değeri sınırlaması için beklenen süre (birkaç çevrim)
        private const string AutoBackupNote = "Otomatik yedek (son parametre değişikliğinden sonraki durum)";

        private readonly IS7DriverDal _s7DriverDal;
        private readonly IPlcService _plcService;

        // Aynı anda tek parametre yazma ya da geri yükleme işleminin yürümesini sağlar.
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        // Sürücü ve PLC servis bağımlılıklarını alır.
        public SystemSettingManager(IS7DriverDal s7DriverDal, IPlcService plcService)
        {
            _s7DriverDal = s7DriverDal;
            _plcService = plcService;
        }

        // DB204'ü tek blok okuyup katalogdaki her parametreyi güncel değeriyle döner.
        public async Task<IDataResult<List<ParamValueDto>>> GetParametersAsync()
        {
            try
            {
                var buffer = await ReadBlockAsync();

                var list = ParamCatalog.All.Select(p => new ParamValueDto
                {
                    Address = p.Address,
                    Name = p.Name,
                    Unit = p.Unit,
                    Description = p.Description,
                    Default = p.Default,
                    Min = p.Min,
                    Max = p.Max,
                    IsSafety = p.IsSafety,
                    Value = S7Bytes.GetInt(buffer, p.Address)
                }).ToList();

                return new SuccessDataResult<List<ParamValueDto>>(list);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<List<ParamValueDto>>($"Parametreler okunamadı: {ex.Message}");
            }
        }

        // Parametreyi katalog ve aralık kontrolünden geçirip DB204'e yazar; geri okuyup PLC'de geçerli olan değeri bildirir.
        public async Task<IDataResult<int>> UpdateParameterAsync(int address, int value, int userId = 0)
        {
            var definition = ParamCatalog.Find(address);
            if (definition == null)
                return new ErrorDataResult<int>($"Adres {address} yazılabilir bir parametre değil.");

            if (value < definition.Min || value > definition.Max)
            {
                return new ErrorDataResult<int>(
                    $"'{definition.Name}' değeri {definition.Min}–{definition.Max} {definition.Unit} aralığında olmalı (girilen: {value}).");
            }

            await _writeLock.WaitAsync();
            try
            {
                var layout = await _plcService.VerifyLayoutAsync();
                if (!layout.IsSuccess)
                    return new ErrorDataResult<int>(layout.Message);

                // Bazı parametreler yalnızca program çalışmıyorken (SeqState 0) değiştirilebilir.
                if (definition.IdleOnly && await ReadSeqStateAsync() != 0)
                    return new ErrorDataResult<int>($"'{definition.Name}' yalnızca program çalışmıyorken değiştirilebilir.");

                int oldValue = S7Bytes.GetInt(await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, address, 2), 0);

                await WriteValueAsync(address, value);

                // PLC güvenlik parametrelerini her çevrim kendi sınırlarına çeker; geçerli değer geri okunur.
                await Task.Delay(ReadBackDelayMs);
                int readBack = S7Bytes.GetInt(await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, address, 2), 0);

                // PLC'ye yeni program yüklenirse parametreler sıfırlanır; son hal her yazımdan sonra yedeklenir.
                await SaveAutoBackupAsync(userId);

                if (readBack != value)
                {
                    return new SuccessDataResult<int>(oldValue,
                        $"'{definition.Name}' için {value} yazıldı ama PLC değeri {readBack} {definition.Unit} olarak sınırladı (önceki: {oldValue}).");
                }

                return new SuccessDataResult<int>(oldValue,
                    $"'{definition.Name}' {oldValue} → {value} {definition.Unit} olarak değiştirildi.");
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<int>($"'{definition.Name}' yazılamadı: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // Yedekleri yeniden eskiye listeler.
        public async Task<IDataResult<List<ParamBackupDto>>> GetBackupsAsync()
        {
            try
            {
                await using var context = new NorthwindContext();

                var list = await context.ParamBackups
                    .AsNoTracking()
                    .OrderByDescending(b => b.Timestamp)
                    .Select(b => new ParamBackupDto
                    {
                        Id = b.Id,
                        Timestamp = b.Timestamp,
                        UserId = b.UserId,
                        Note = b.Note,
                        IsAuto = b.IsAuto
                    })
                    .ToListAsync();

                return new SuccessDataResult<List<ParamBackupDto>>(list);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<List<ParamBackupDto>>($"Yedekler okunamadı: {ex.Message}");
            }
        }

        // PLC'deki parametrelerin o anki halini notuyla birlikte kalıcı bir yedek olarak kaydeder.
        public async Task<IDataResult<int>> CreateBackupAsync(string? note, int userId)
        {
            try
            {
                var values = ExtractValues(await ReadBlockAsync());

                string cleanNote = string.IsNullOrWhiteSpace(note) ? "Elle alınan yedek" : note.Trim();
                if (cleanNote.Length > 200)
                    cleanNote = cleanNote.Substring(0, 200);

                var backup = new ParamBackup
                {
                    Timestamp = DateTime.Now,
                    UserId = userId,
                    Note = cleanNote,
                    IsAuto = false,
                    ValuesJson = JsonSerializer.Serialize(values)
                };

                await using var context = new NorthwindContext();
                await context.ParamBackups.AddAsync(backup);
                await context.SaveChangesAsync();

                return new SuccessDataResult<int>(backup.Id, $"{values.Count} parametre yedeklendi.");
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<int>($"Yedek alınamadı: {ex.Message}");
            }
        }

        // Yedekteki değerlerle PLC'deki değerleri karşılaştırıp farklı olan parametreleri döner.
        public async Task<IDataResult<List<ParamDiffDto>>> CompareBackupAsync(int backupId)
        {
            try
            {
                var backupValues = await LoadBackupValuesAsync(backupId);
                if (backupValues == null)
                    return new ErrorDataResult<List<ParamDiffDto>>("Yedek bulunamadı ya da içeriği okunamıyor.");

                var plcBlock = await ReadBlockAsync();
                var diffs = new List<ParamDiffDto>();

                foreach (var definition in ParamCatalog.All)
                {
                    if (!backupValues.TryGetValue(definition.Address, out int backupValue))
                        continue;

                    int plcValue = S7Bytes.GetInt(plcBlock, definition.Address);
                    if (plcValue == backupValue)
                        continue;

                    diffs.Add(new ParamDiffDto
                    {
                        Address = definition.Address,
                        Name = definition.Name,
                        Unit = definition.Unit,
                        IsSafety = definition.IsSafety,
                        BackupValue = backupValue,
                        PlcValue = plcValue
                    });
                }

                string message = diffs.Count == 0 ? "PLC'deki parametreler yedekle aynı." : $"{diffs.Count} parametre yedekten farklı.";
                return new SuccessDataResult<List<ParamDiffDto>>(diffs, message);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<List<ParamDiffDto>>($"Karşılaştırma yapılamadı: {ex.Message}");
            }
        }

        // Yedekteki değerlerden PLC'dekinden farklı olanları geri yazar; yalnızca fırın beklemedeyken çalışır.
        public async Task<IDataResult<List<ParamRestoreItemDto>>> RestoreBackupAsync(int backupId, int userId)
        {
            await _writeLock.WaitAsync();
            try
            {
                var layout = await _plcService.VerifyLayoutAsync();
                if (!layout.IsSuccess)
                    return new ErrorDataResult<List<ParamRestoreItemDto>>(layout.Message);

                if (await ReadSeqStateAsync() != 0)
                    return new ErrorDataResult<List<ParamRestoreItemDto>>("Geri yükleme yalnızca fırın beklemedeyken (program yokken) yapılabilir.");

                var backupValues = await LoadBackupValuesAsync(backupId);
                if (backupValues == null)
                    return new ErrorDataResult<List<ParamRestoreItemDto>>("Yedek bulunamadı ya da içeriği okunamıyor.");

                var before = await ReadBlockAsync();
                var items = new List<ParamRestoreItemDto>();
                var written = new List<ParamRestoreItemDto>();

                foreach (var definition in ParamCatalog.All)
                {
                    if (!backupValues.TryGetValue(definition.Address, out int backupValue))
                        continue;

                    int current = S7Bytes.GetInt(before, definition.Address);
                    if (current == backupValue)
                        continue;

                    var item = new ParamRestoreItemDto
                    {
                        Address = definition.Address,
                        Name = definition.Name,
                        Unit = definition.Unit,
                        PreviousValue = current,
                        BackupValue = backupValue,
                        AppliedValue = current
                    };
                    items.Add(item);

                    // Yedek eski bir katalogdan geliyorsa değer bugünkü aralığın dışında olabilir; yazılmaz.
                    if (backupValue < definition.Min || backupValue > definition.Max)
                    {
                        item.Status = $"Yazılmadı: yedekteki değer {definition.Min}–{definition.Max} aralığının dışında";
                        continue;
                    }

                    await WriteValueAsync(definition.Address, backupValue);
                    written.Add(item);
                }

                // PLC'nin sınırladığı değerleri görmek için yazılanlar topluca geri okunur.
                if (written.Count > 0)
                {
                    await Task.Delay(ReadBackDelayMs);
                    var after = await ReadBlockAsync();

                    foreach (var item in written)
                    {
                        item.AppliedValue = S7Bytes.GetInt(after, item.Address);
                        item.Status = item.AppliedValue == item.BackupValue
                            ? "Geri yüklendi"
                            : $"PLC değeri {item.AppliedValue} olarak sınırladı";
                    }

                    await SaveAutoBackupAsync(userId);
                }

                int skipped = items.Count - written.Count;
                string message = items.Count == 0
                    ? "PLC'deki parametreler yedekle zaten aynı; bir şey yazılmadı."
                    : $"{written.Count} parametre geri yüklendi, {skipped} parametre yazılmadı.";

                return new SuccessDataResult<List<ParamRestoreItemDto>>(items, message);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<List<ParamRestoreItemDto>>($"Geri yükleme tamamlanamadı: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // DB204'ün tamamını okur; şema sürümü uyuşmuyorsa hata fırlatır.
        private async Task<byte[]> ReadBlockAsync()
        {
            var buffer = await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbParam, 0, ParamCatalog.BlockSize);

            short version = S7Bytes.GetInt(buffer, ScadaLayout.ParamVersionOffset);
            if (version != ScadaLayout.LayoutVersion)
                throw new InvalidOperationException($"Şema sürümü uyuşmuyor (DB204: {version}, beklenen: {ScadaLayout.LayoutVersion}).");

            return buffer;
        }

        // DB201'den o anki sekans durumunu okur.
        private async Task<short> ReadSeqStateAsync()
        {
            return S7Bytes.GetInt(
                await _s7DriverDal.ReadBytesAsync(ScadaLayout.DbStatus, ScadaLayout.StatusSeqStateOffset, 2), 0);
        }

        // Tek bir parametre değerini DB204'teki adresine yazar.
        private async Task WriteValueAsync(int address, int value)
        {
            var data = new byte[2];
            S7Bytes.SetInt(data, 0, (short)value);
            await _s7DriverDal.WriteBytesAsync(ScadaLayout.DbParam, address, data);
        }

        // DB204 bloğundan katalogdaki parametrelerin değerlerini adres -> değer eşlemesi olarak çıkarır.
        private static Dictionary<int, int> ExtractValues(byte[] block)
        {
            return ParamCatalog.All.ToDictionary(p => p.Address, p => (int)S7Bytes.GetInt(block, p.Address));
        }

        // Yedeğin değerlerini veritabanından yükler; yedek yoksa ya da içeriği bozuksa null döner.
        private static async Task<Dictionary<int, int>?> LoadBackupValuesAsync(int backupId)
        {
            await using var context = new NorthwindContext();

            var backup = await context.ParamBackups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == backupId);
            if (backup == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<Dictionary<int, int>>(backup.ValuesJson);
            }
            catch
            {
                return null;
            }
        }

        // Tek otomatik yedeği PLC'deki son değerlerle günceller (yoksa oluşturur); hata parametre işlemini etkilemez.
        private async Task SaveAutoBackupAsync(int userId)
        {
            try
            {
                var values = ExtractValues(await ReadBlockAsync());

                await using var context = new NorthwindContext();

                var backup = await context.ParamBackups.FirstOrDefaultAsync(b => b.IsAuto);
                if (backup == null)
                {
                    backup = new ParamBackup { IsAuto = true, Note = AutoBackupNote };
                    await context.ParamBackups.AddAsync(backup);
                }

                backup.Timestamp = DateTime.Now;
                backup.UserId = userId;
                backup.ValuesJson = JsonSerializer.Serialize(values);

                await context.SaveChangesAsync();
            }
            catch
            {
                // Yedek yazılamasa da parametre PLC'ye yazılmıştır; işlem başarılı sayılır.
            }
        }
    }
}