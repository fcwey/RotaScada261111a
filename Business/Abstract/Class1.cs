using Core.Utilities.Results;
using System.Threading.Tasks;

namespace Business.Abstract
{
    // Reçetenin yalnızca listede kullanılan, PLC'ye gitmeyen özellikleri
    public interface IRecipeListService
    {
        // Reçeteyi listede kendi tipinin başına sabitler ya da sabitlemeyi kaldırır
        Task<IResult> SetPinnedAsync(int recipeId, bool pinned);
    }
}