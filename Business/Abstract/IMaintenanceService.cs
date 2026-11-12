using Core.Utilities.Results;
using Entities.Dtos;
using System;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IMaintenanceService
    {
        // Saklama süresi dolan kayıtları siler; silinen toplam kayıt sayısını döner
        Task<IDataResult<int>> CleanupAsync(MaintenanceOptions options, DateTime now);

        // Bugünün yedeği yoksa veritabanının yedeğini alır; yeni yedek alındıysa true döner
        Task<IDataResult<bool>> BackupAsync(MaintenanceOptions options, DateTime now);
    }
}