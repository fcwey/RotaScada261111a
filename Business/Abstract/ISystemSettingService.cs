using Core.Utilities.Results;
using Entities.Dtos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface ISystemSettingService
    {
        // DB204'teki bütün parametreleri tanımları ve güncel değerleriyle döner
        Task<IDataResult<List<ParamValueDto>>> GetParametersAsync();

        // Tek bir parametreyi aralık kontrolüyle yazar; başarılıysa eski değeri döner ve otomatik yedeği günceller
        Task<IDataResult<int>> UpdateParameterAsync(int address, int value, int userId = 0);

        // Yedekleri yeniden eskiye listeler
        Task<IDataResult<List<ParamBackupDto>>> GetBackupsAsync();

        // PLC'deki parametrelerin o anki halini notuyla birlikte yedekler; yedeğin numarasını döner
        Task<IDataResult<int>> CreateBackupAsync(string? note, int userId);

        // Yedekteki değerlerle PLC'deki değerlerin farklarını döner
        Task<IDataResult<List<ParamDiffDto>>> CompareBackupAsync(int backupId);

        // Yedekteki değerleri PLC'ye geri yükler (yalnızca fırın beklemedeyken); işlem gören parametreleri döner
        Task<IDataResult<List<ParamRestoreItemDto>>> RestoreBackupAsync(int backupId, int userId);
    }
}