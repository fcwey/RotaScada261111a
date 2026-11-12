using Core.Utilities.Results;
using Entities.Dtos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IBatchService
    {
        // PLC sekans durumunu izler; program bittiğinde ya da durduğunda açık şarj kaydını kapatır
        Task<IResult> TrackSequenceAsync(short seqState);

        // Başlangıç zamanı verilen aralıkta olan şarjları yeniden eskiye listeler
        Task<IDataResult<List<BatchListDto>>> GetBatchesAsync(DateTime start, DateTime end);

        // Tek şarjın özetini, plan geçmişini ve şarj sırasındaki alarmları getirir
        Task<IDataResult<BatchDetailDto>> GetBatchDetailAsync(int batchId);

        // Çalışan şarjın plan geçmişine adıma atlama (GOTO) ya da soğutmaya geçiş (SKIP) olayını ekler
        Task<IResult> RecordPlanEventAsync(string reason, int stepNo, int stepMinute, int userId);
    }
}