using Tabbit.Models;

namespace Tabbit.Recipe;

/// <summary>
/// Reads the TargetSide field of a recipe entry.
///
/// Separate from the sheet-side parsing in ModelCooker because the diagnostics
/// differ: a bad marker in a sheet points at a cell, whereas a bad recipe value
/// has to name the recipe section instead.
/// </summary>
public static class RecipeTargetSide
{
    /// <summary>
    /// Parses a recipe's target side, or throws naming the offending section.
    /// </summary>
    /// <param name="text">Value as written in the recipe: "c", "s", or "cs"/blank.</param>
    /// <param name="recipeSection">Dotted path of the section, used in the error message.</param>
    public static TargetSide Of(string text, string recipeSection)
    {
        if (TargetSides.TryParse(text, out var side))
            return side;

            throw new TabbitException(null,
                Messages.Message.Of(RecipeMessages.TargetSideUnknown,
                    ("Section", recipeSection), ("Value", text)));
    }
}
