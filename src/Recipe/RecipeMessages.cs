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

    /// <summary>A source entry's array delimiter that is not exactly one character.</summary>
    public const string EntryArrayDelimiterNotOneCharacter = "recipe.entry-array-delimiter-not-one-character";

    /// <summary>An `OnBlankCell` this tool does not understand.</summary>
    public const string OnBlankCellUnknown = "recipe.on-blank-cell-unknown";

    /// <summary>An `OnFormulaError` this tool does not understand.</summary>
    public const string OnFormulaErrorUnknown = "recipe.on-formula-error-unknown";

    /// <summary>An `OnDuplicateIndex` this tool does not understand.</summary>
    public const string OnDuplicateIndexUnknown = "recipe.on-duplicate-index-unknown";

    /// <summary>A workbook list entry written as `[workbook]sheet`.</summary>
    public const string WorkbookPatternHasSheet = "recipe.workbook-pattern-has-sheet";

    /// <summary>A sheet pattern that opens a bracket and never closes it.</summary>
    public const string SheetPatternUnclosedBracket = "recipe.sheet-pattern-unclosed-bracket";

    /// <summary>A sheet pattern with empty brackets.</summary>
    public const string SheetPatternNoWorkbook = "recipe.sheet-pattern-no-workbook";

    /// <summary>A sheet pattern naming a workbook and no sheet.</summary>
    public const string SheetPatternNoSheet = "recipe.sheet-pattern-no-sheet";

    /// <summary>Both ways of naming a Google service account key at once.</summary>
    public const string GoogleKeyFileAndVariable = "recipe.google-key-file-and-variable";

    /// <summary>A service account key and a client secret at once.</summary>
    public const string GoogleServiceAccountAndClientSecret = "recipe.google-service-account-and-client-secret";

    /// <summary>A Google Sheets section naming no credential at all.</summary>
    public const string GoogleNoCredential = "recipe.google-no-credential";
}
