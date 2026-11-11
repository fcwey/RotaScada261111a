using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Abstract;
using Entities.Concrete;
using Entities.Dtos;
using Entities.Structures;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Business.Concrete
{
    public class AlarmManager : IAlarmService
    {
        private readonly IAlarmDal _alarmDal;

        // Aynı anda tek alarm işleme çalışmasını sağlar.
        private readonly SemaphoreSlim _processLock = new SemaphoreSlim(1, 1);

        private short[]? _lastActive;   // Son işlenen aktif alarm kelimeleri
        private short[]? _lastUnack;    // Son işlenen görülmemiş alarm kelimeleri

        // Bağımlılıkları alır ve ayarlardaki alarm sınıfı değişikliklerini yükler.
        public AlarmManager(IAlarmDal alarmDal, IConfiguration configuration)
        {
            _alarmDal = alarmDal;
            LoadAlarmClassOverrides(configuration);
        }

        // appsettings içindeki AlarmClasses bölümünü okuyup alarm kataloğuna uygular.
        private static void LoadAlarmClassOverrides(IConfiguration configuration)
        {
            var overrides = new Dictionary<int, int>();

            foreach (var item in configuration.GetSection("AlarmClasses").GetChildren())
            {
                if (int.TryParse(item.Key, out int alarmNo) && int.TryParse(item.Value, out int alarmClass))
                    overrides[alarmNo] = alarmClass;
            }

            AlarmCatalog.SetClassOverrides(overrides);
        }

        // PLC alarm bitlerini veritabanındaki kayıtlarla eşitler: gelen alarmı açar, gideni kapatır, reset verileni görüldü yapar.
        public async Task<IDataResult<bool>> ProcessAlarmWordsAsync(short[] activeWords, short[] unackWords)
        {
            await _processLock.WaitAsync();
            try
            {
                // Bitler değişmediyse veritabanına gidilmez.
                if (_lastActive != null && _lastUnack != null &&
                    _lastActive.SequenceEqual(activeWords) && _lastUnack.SequenceEqual(unackWords))
                {
                    return new SuccessDataResult<bool>(false);
                }

                var openAlarms = (await _alarmDal.GetListAsync(a => a.IsActive || !a.IsAcknowledged)).ToList();
                DateTime now = DateTime.Now;

                for (int alarmNo = 1; alarmNo <= AlarmCatalog.MaxAlarmNo; alarmNo++)
                {
                    bool isActive = GetAlarmBit(activeWords, alarmNo);
                    bool isUnack = GetAlarmBit(unackWords, alarmNo);
                    string tag = AlarmCatalog.GetTag(alarmNo);

                    var records = openAlarms.Where(a => a.TagName == tag).ToList();

                    // Alarm geldi: açık kaydı yoksa yeni kayıt oluşturulur.
                    if (isActive && !records.Any(a => a.IsActive))
                    {
                        await _alarmDal.AddAsync(new Alarm
                        {
                            TagName = tag,
                            Description = AlarmCatalog.GetText(alarmNo),
                            PriorityLevel = AlarmCatalog.GetPriority(alarmNo),
                            TriggeredAt = now,
                            IsActive = true,
                            IsAcknowledged = !isUnack,
                            AcknowledgedAt = isUnack ? null : now
                        });
                    }

                    foreach (var record in records)
                    {
                        bool changed = false;

                        // Alarm gitti: açık kayıt kapatılır.
                        if (!isActive && record.IsActive)
                        {
                            record.IsActive = false;
                            record.ResolvedAt = now;
                            changed = true;
                        }

                        // PLC'de reset verildi: kayıt görüldü olarak işaretlenir.
                        if (!isUnack && !record.IsAcknowledged)
                        {
                            record.IsAcknowledged = true;
                            record.AcknowledgedAt = now;
                            changed = true;
                        }

                        if (changed)
                            await _alarmDal.UpdateAsync(record);
                    }
                }

                // Kayıt başarılıysa son durum saklanır; hata olursa bir sonraki çevrimde yeniden denenir.
                _lastActive = (short[])activeWords.Clone();
                _lastUnack = (short[])unackWords.Clone();

                return new SuccessDataResult<bool>(true);
            }
            catch (Exception ex)
            {
                return new ErrorDataResult<bool>(false, $"Alarm bitleri işlenemedi: {ex.Message}");
            }
            finally
            {
                _processLock.Release();
            }
        }

        // Aktif alarmları arayüz için listeler.
        public async Task<IDataResult<List<ActiveAlarmDto>>> GetActiveAlarmsAsync()
        {
            var activeAlarms = await _alarmDal.GetListAsync(a => a.IsActive);
            var dtos = activeAlarms.Select(a => new ActiveAlarmDto
            {
                Id = a.Id,
                TagName = a.TagName,
                Description = a.Description,
                PriorityLevel = a.PriorityLevel,
                TriggeredAt = a.TriggeredAt,
                IsAcknowledged = a.IsAcknowledged
            }).ToList();

            return new SuccessDataResult<List<ActiveAlarmDto>>(dtos);
        }

        // Operatörün bir alarm kaydını notuyla birlikte onaylamasını kaydeder.
        public async Task<IResult> AcknowledgeAlarmAsync(int alarmId, int userId, string? userNote = null)
        {
            var alarm = await _alarmDal.GetAsync(a => a.Id == alarmId);
            if (alarm == null)
                return new ErrorResult("Alarm kaydı bulunamadı.");

            if (alarm.IsAcknowledged)
                return new ErrorResult("Bu alarm zaten onaylanmış.");

            alarm.IsAcknowledged = true;
            alarm.AcknowledgedAt = DateTime.Now;
            alarm.AcknowledgedByUserId = userId;
            alarm.UserNote = userNote;

            await _alarmDal.UpdateAsync(alarm);
            return new SuccessResult("Alarm başarıyla onaylandı (ACK).");
        }

        // Tarih aralığı ve önceliğe göre geçmiş alarm kayıtlarını getirir.
        public async Task<IDataResult<List<AlarmHistoryDto>>> GetAlarmHistoryAsync(DateTime start, DateTime end, int? priorityLevel = null)
        {
            var history = await _alarmDal.GetListAsync(a =>
                a.TriggeredAt >= start &&
                a.TriggeredAt <= end &&
                (!priorityLevel.HasValue || a.PriorityLevel == priorityLevel.Value));

            var dtos = history.Select(a => new AlarmHistoryDto
            {
                Id = a.Id,
                TagName = a.TagName,
                Description = a.Description,
                PriorityLevel = a.PriorityLevel,
                TriggeredAt = a.TriggeredAt,
                AcknowledgedAt = a.AcknowledgedAt,
                ResolvedAt = a.ResolvedAt,
                UserNote = a.UserNote
            }).ToList();

            return new SuccessDataResult<List<AlarmHistoryDto>>(dtos);
        }

        // Alarm numarasını (1'den başlar) kelime ve bit konumuna çevirip biti okur.
        private static bool GetAlarmBit(short[] words, int alarmNo)
        {
            int word = (alarmNo - 1) / 16;
            int bit = (alarmNo - 1) % 16;
            return word < words.Length && S7Bytes.GetBit(words[word], bit);
        }
    }
}