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

    /// <summary>An `Assets.OnMissing` this tool does not understand.</summary>
    public const string AssetsOnMissingUnknown = "recipe.assets-on-missing-unknown";

    /// <summary>An asset root pointing at a folder that is not there.</summary>
    public const string AssetsRootMissing = "recipe.assets-root-missing";

    /// <summary>A `DataFileCase` this tool does not understand.</summary>
    public const string DataFileCaseUnknown = "recipe.data-file-case-unknown";

    /// <summary>A naming spelling this tool does not understand.</summary>
    public const string NamingCaseUnknown = "recipe.naming-case-unknown";

    /// <summary>A naming `OnViolation` this tool does not understand.</summary>
    public const string NamingOnViolationUnknown = "recipe.naming-on-violation-unknown";

    /// <summary>A naming severity this tool does not understand.</summary>
    public const string NamingSeverityUnknown = "recipe.naming-severity-unknown";

    /// <summary>A row-set pattern whose table group captured a different table.</summary>
    public const string RowSetsGroupCapturedOtherTable = "recipe.row-sets-group-captured-other-table";

    /// <summary>A source asking for a sheet layout that does not exist.</summary>
    public const string LayoutUnknown = "recipe.layout-unknown";

    /// <summary>A history target with no project key to tell its rows apart by.</summary>
    public const string HistoryNoProjectKey = "recipe.history-no-project-key";

    /// <summary>A history `OnFailure` this tool does not understand.</summary>
    public const string HistoryOnFailureUnknown = "recipe.history-on-failure-unknown";

    /// <summary>A report `OpenInBrowser` this tool does not understand.</summary>
    public const string ReportOpenUnknown = "recipe.report-open-unknown";

    /// <summary>A summary `Author` this tool does not understand.</summary>
    public const string SummaryAuthorUnknown = "recipe.summary-author-unknown";
    /// <summary>A `MemberCase` this tool does not understand.</summary>
    public const string MemberCaseUnknown = "recipe.member-case-unknown";

    /// <summary>A `--template` no starting recipe answers to.</summary>
    public const string TemplateUnknown = "recipe.template-unknown";

    /// <summary>A `TargetSide` this tool does not understand.</summary>
    public const string TargetSideUnknown = "recipe.target-side-unknown";

    /// <summary>A setting that had to be a string or a list of them.</summary>
    public const string StringListExpected = "recipe.string-list-expected";

    /// <summary>A layout option the named layout does not read.</summary>
    public const string LayoutOptionUnknown = "recipe.layout-option-unknown";

    /// <summary>A target section that would not read.</summary>
    public const string SectionCouldNotBeRead = "recipe.section-could-not-be-read";

    /// <summary>A target entry with no `Type` to say what it configures.</summary>
    public const string TargetEntryHasNoType = "recipe.target-entry-has-no-type";

    /// <summary>A target entry naming a target that does not exist.</summary>
    public const string TargetUnknown = "recipe.target-unknown";

    /// <summary>Environment variables a recipe names that are not set.</summary>
    public const string VariablesNotSet = "recipe.variables-not-set";

    /// <summary>
    /// As <see cref="VariablesNotSet"/>, where one of them is the environment name.
    /// </summary>
    /// <remarks>
    /// Its own id for the sake of one extra paragraph. A recipe carrying that variable is a
    /// recipe written for more than one environment, and somebody meeting it is usually running
    /// a colleague's - so the sentence worth adding is which flag names the environment.
    /// </remarks>
    public const string VariablesNotSetWithEnvironment = "recipe.variables-not-set-with-environment";

    /// <summary>A time zone this machine does not know.</summary>
    public const string TimeZoneUnknown = "recipe.time-zone-unknown";

    /// <summary>As <see cref="TimeZoneUnknown"/>, where near-misses were found.</summary>
    public const string TimeZoneUnknownWithSuggestions = "recipe.time-zone-unknown-with-suggestions";

    /// <summary>A time zone whose data this machine cannot read.</summary>
    public const string TimeZoneDataUnreadable = "recipe.time-zone-data-unreadable";

    /// <summary>A value that is neither a zone name nor an offset.</summary>
    public const string TimeZoneNotAnOffset = "recipe.time-zone-not-an-offset";

    /// <summary>An offset further from UTC than anywhere on earth.</summary>
    public const string TimeZoneOffsetTooLarge = "recipe.time-zone-offset-too-large";
}
