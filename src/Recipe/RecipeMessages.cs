using Tabbit.Messages;

namespace Tabbit.Recipe;

/// <summary>
/// The reports about the recipe itself, named.
/// </summary>
/// <remarks>
/// `recipe` rather than the step that happened to notice. A setting written wrong is a fact
/// about the recipe wherever it is read from - the row-set patterns below are read while
/// cooking, and telling somebody to look at their sheets would send them to the wrong file.
///
/// What separates these from <see cref="Cooking.CookingMessages"/> is who fixes it, which is
/// the same line the exception types draw: the sheet's author, the recipe's author, or us.
/// </remarks>
[TabbitMessages("recipe")]
public static class RecipeMessages
{
    /// <summary>An array delimiter that is not exactly one character.</summary>
    public const string ArrayDelimiterNotOneCharacter = "recipe.array-delimiter-not-one-character";

    /// <summary>Two source entries declaring different row-set patterns.</summary>
    public const string RowSetsConflictingPatterns = "recipe.row-sets-conflicting-patterns";

    /// <summary>A row-set pattern that is not a regular expression.</summary>
    public const string RowSetsBadRegex = "recipe.row-sets-bad-regex";

    /// <summary>A row-set pattern without the two named groups it needs.</summary>
    public const string RowSetsMissingGroups = "recipe.row-sets-missing-groups";
}
