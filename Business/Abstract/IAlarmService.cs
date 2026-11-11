using Core.Utilities.Results;
using Entities.Dtos;
using System;
using System.Collections.Generic;
using System.Text;
namespace Business.Abstract
{
    public interface IAlarmService
    {
        // DB201'deki aktif ve görülmemiş alarm kelimelerini işler; değişiklik olduysa true döner
        Task<IDataResult<bool>> ProcessAlarmWordsAsync(short[] activeWords, short[] unackWords);

        // Arayüzde gösterilecek aktif alarmları getirir
        Task<IDataResult<List<ActiveAlarmDto>>> GetActiveAlarmsAsync();

        // Operatörün bir alarmı görmesini/onaylamasını sağlar (ACK)
        Task<IResult> AcknowledgeAlarmAsync(int alarmId, int userId, string? userNote = null);

        // Tarih ve önceliğe göre geçmiş alarm kayıtlarını filtreler
        Task<IDataResult<List<AlarmHistoryDto>>> GetAlarmHistoryAsync(DateTime start, DateTime end, int? priorityLevel = null);
    }
}