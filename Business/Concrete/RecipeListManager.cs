using Business.Abstract;
using Core.Utilities.Results;
using DataAccess.Concrete.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace Business.Concrete
{
    public class RecipeListManager : IRecipeListService
    {
        // Reçetenin sabitleme işaretini değiştirir; reçetenin adımlarına ve PLC'ye giden alanlarına dokunmaz.
        public async Task<IResult> SetPinnedAsync(int recipeId, bool pinned)
        {
            try
            {
                await using var context = new NorthwindContext();

                var recipe = await context.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId && r.IsActive);
                if (recipe == null)
                    return new ErrorResult("Reçete bulunamadı.");

                recipe.IsPinned = pinned;
                await context.SaveChangesAsync();

                return new SuccessResult(pinned ? "Reçete sabitlendi." : "Sabitleme kaldırıldı.");
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Sabitleme değiştirilemedi: {ex.Message}");
            }
        }
    }
}