using Core.Utilities.Results;
using Entities.Concrete;
using Entities.Structures;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Business.Abstract
{
    public interface IRecipeService
    {
        // Veritabanındaki reçeteler
        Task<IDataResult<List<Recipe>>> GetAllRecipesAsync();
        Task<IDataResult<Recipe>> GetRecipeByIdAsync(int id);
        Task<IDataResult<int>> AddRecipeAsync(Recipe recipe);
        Task<IResult> UpdateRecipeAsync(Recipe recipe);
        Task<IResult> DeleteRecipeAsync(int id);

        // PLC işlemleri
        Task<IDataResult<ScadaCommandResult>> ValidateOnPlcAsync(int recipeId);
        Task<IDataResult<ScadaCommandResult>> StartRecipeAsync(int recipeId, int userId);
        Task<IDataResult<Recipe>> GetActiveRecipeAsync();
        Task<IDataResult<ScadaCommandResult>> ApplyEditAsync(Recipe editedRecipe, int userId);
    }
}