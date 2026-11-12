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

        // Tek şarjın özetini, reçete geçmişini ve şarj sırasındaki alarmları getirir
        Task<IDataResult<BatchDetailDto>> GetBatchDetailAsync(int batchId);
    }
}