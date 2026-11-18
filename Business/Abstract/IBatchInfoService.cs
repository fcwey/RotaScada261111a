using Core.Utilities.Results;
using Entities.Dtos;
using System.Threading.Tasks;

namespace Business.Abstract
{
    // Şarjın operatör tarafından girilen bilgileri: ağırlık ve notlar
    public interface IBatchInfoService
    {
        // Şarjın ağırlığını ve notlarını döner
        Task<IDataResult<BatchInfoDto>> GetInfoAsync(int batchId);

        // Şarj ağırlığını yazar ya da düzeltir; null verilirse siler
        Task<IResult> SetLoadAsync(int batchId, float? loadKg);

        // Şarja not ekler
        Task<IDataResult<BatchNoteDto>> AddNoteAsync(int batchId, int userId, string? text);

        // O an açık olan (başlayan ya da çalışan) şarjın numarasını döner; yoksa null
        Task<int?> GetOpenBatchIdAsync();
    }
}