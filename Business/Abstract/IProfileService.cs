using Core.Utilities.Results;
using Entities.Dtos;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IProfileService
    {
        // Kayıtlı bir reçetenin teorik sıcaklık eğrisini dakika cinsinden döner
        Task<IDataResult<RecipeProfileDto>> GetRecipeProfileAsync(int recipeId, float startTemp);

        // Bir şarjın teorik eğrisini gerçek saatlere oturtarak döner; original true ise başlangıçtaki planı verir
        Task<IDataResult<RecipeProfileDto>> GetBatchProfileAsync(int batchId, bool original = false);

        // O an çalışan şarjın geçerli teorik eğrisini döner
        Task<IDataResult<RecipeProfileDto>> GetActiveBatchProfileAsync();
    }
}