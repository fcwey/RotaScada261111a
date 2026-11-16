using Core.Utilities.Results;
using Entities.Concrete;
using Entities.Structures;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IPlcEventService
    {
        // PLC'ye (yeniden) bağlanıldığını bildirir; bir sonraki izlemede saat eşitlenir
        void OnConnected();

        // Durumu izler: gerekiyorsa saati eşitler, sonuçlanan kaçak testini ve biten PID ayarını kaydeder
        Task<IResult> TrackAsync(ScadaStatus status);

        // Verilen tarih aralığındaki kaçak testi kayıtlarını yeniden eskiye listeler
        Task<IDataResult<List<LeakTestRecord>>> GetLeakTestsAsync(DateTime start, DateTime end);
    }
}