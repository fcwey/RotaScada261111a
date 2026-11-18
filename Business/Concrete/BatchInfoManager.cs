using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Entities.Concrete;
using Entities.Dtos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class BatchInfoManager : IBatchInfoService
    {
        private const float MaxLoadKg = 100000f;
        private const int MaxNoteLength = 2000;

        // Şarjın ağırlığını ve notlarını (eskiden yeniye) döner.
        public async Task<IDataResult<BatchInfoDto>> GetInfoAsync(int batchId)
        {
            try
            {
                await using var context = new NorthwindContext();

                var batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId);
                if (batch == null)
                    return new ErrorDataResult<BatchInfoDto>("Şarj bulunamadı.");

                var notes = await context.BatchNotes
                    .AsNoTracking()
                    .Where(n => n.BatchId == batchId)
                    .OrderBy(n => n.Timestamp)
                    .ToListAsync();

                var userIds = notes.Select(n => n.UserId).Distinct().ToList();
                var users = await context.Users
                    .AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Email);

                var info = new BatchInfoDto
                {
                    BatchId = batch.Id,
                    BatchCode = batch.BatchCode,
                    LoadKg = batch.LoadKg,
                    Notes = notes.Select(n => new BatchNoteDto
                    {
                        Id = n.Id,
                        Timestamp = n.Timestamp,
                        UserId = n.UserId,
                        UserEmail = users.TryGetValue(n.UserId, out var email) ? email : $"kullanıcı {n.UserId}",
                        Text = n.NoteText
                    }).ToList()
                };

                return new SuccessDataResult<BatchInfoDto>(info);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<BatchInfoDto>($"Şarj bilgisi okunamadı: {ex.Message}");
            }
        }

        // Şarj ağırlığını yazar ya da düzeltir; değer 0–100000 kg aralığında olmalıdır.
        public async Task<IResult> SetLoadAsync(int batchId, float? loadKg)
        {
            if (loadKg.HasValue && (float.IsNaN(loadKg.Value) || loadKg.Value < 0 || loadKg.Value > MaxLoadKg))
                return new ErrorResult($"Şarj ağırlığı 0–{MaxLoadKg:0} kg aralığında olmalı.");

            try
            {
                await using var context = new NorthwindContext();

                var batch = await context.Batches.FirstOrDefaultAsync(b => b.Id == batchId);
                if (batch == null)
                    return new ErrorResult("Şarj bulunamadı.");

                batch.LoadKg = loadKg;
                await context.SaveChangesAsync();

                return new SuccessResult("Şarj ağırlığı kaydedildi.");
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Şarj ağırlığı kaydedilemedi: {ex.Message}");
            }
        }

        // Şarja not ekler; boş not kabul edilmez, fazla uzun not kısaltılır.
        public async Task<IDataResult<BatchNoteDto>> AddNoteAsync(int batchId, int userId, string? text)
        {
            string clean = (text ?? string.Empty).Trim();
            if (clean.Length == 0)
                return new ErrorDataResult<BatchNoteDto>("Not boş olamaz.");

            if (clean.Length > MaxNoteLength)
                clean = clean.Substring(0, MaxNoteLength);

            try
            {
                await using var context = new NorthwindContext();

                if (!await context.Batches.AnyAsync(b => b.Id == batchId))
                    return new ErrorDataResult<BatchNoteDto>("Şarj bulunamadı.");

                var note = new BatchNote
                {
                    BatchId = batchId,
                    Timestamp = DateTime.Now,
                    UserId = userId,
                    NoteText = clean
                };

                await context.BatchNotes.AddAsync(note);
                await context.SaveChangesAsync();

                string email = await context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync() ?? $"kullanıcı {userId}";

                var dto = new BatchNoteDto { Id = note.Id, Timestamp = note.Timestamp, UserId = userId, UserEmail = email, Text = clean };
                return new SuccessDataResult<BatchNoteDto>(dto, "Not eklendi.");
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<BatchNoteDto>($"Not eklenemedi: {ex.Message}");
            }
        }

        // En son açılan ve henüz kapanmamış şarjın numarasını döner; yoksa ya da veritabanına ulaşılamıyorsa null döner.
        public async Task<int?> GetOpenBatchIdAsync()
        {
            try
            {
                await using var context = new NorthwindContext();

                return await context.Batches
                    .AsNoTracking()
                    .Where(b => b.Status == "RUNNING" || b.Status == "STARTING")
                    .OrderByDescending(b => b.Id)
                    .Select(b => (int?)b.Id)
                    .FirstOrDefaultAsync();
            }
            catch
            {
                return null;
            }
        }
    }
}