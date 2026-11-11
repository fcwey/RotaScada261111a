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

        // Tek bir parametreyi aralık kontrolüyle yazar; başarılıysa eski değeri döner
        Task<IDataResult<int>> UpdateParameterAsync(int address, int value);
    }
}