using Tabbit.Messages;

namespace Tabbit.Importers;

/// <summary>
/// The reports reading a run's sources produces.
/// </summary>
/// <remarks>
/// `import` because that is the step - <see cref="LogCategory.Importing"/> covers reading the
/// sources the recipe lists into the raw model.
///
/// **The line against <see cref="Recipe.RecipeMessages"/> is what kind of thing is wrong, not
/// which class noticed.** A setting whose value this tool does not understand - `OnBlankCell`
/// set to something that is neither `error` nor `empty`, a sheet pattern with an unclosed
/// bracket - is a fact about the recipe, and it is `recipe.*` even though the importer is what
/// reads it. What is here is the other kind: the recipe is well written and the world does not
/// match it. A path that is not there, a workbook the source turned out not to have, a key
/// file somebody moved.
///
/// The distinction is worth the thought because it decides which file the reader opens. Told
/// their recipe is wrong they read the recipe; told a file is missing they look at the disk.
/// </remarks>
[TabbitMessages("import")]
public static class ImportMessages
{
    /// <summary>Workbooks the entry asked for that the source does not have.</summary>
    public const string WorkbooksNotFound = "import.workbooks-not-found";

    /// <summary>Sheets the entry asked for that the source does not have.</summary>
    public const string SheetsNotFound = "import.sheets-not-found";

    /// <summary>
    /// As <see cref="SheetsNotFound"/>, where the entry also skipped whole workbooks.
    /// </summary>
    /// <remarks>
    /// Its own id rather than an empty placeholder. The extra line is what usually explains
    /// the whole report - a pattern matched nothing because the workbook holding its sheet was
    /// never opened - and a list of the sheets that were read cannot say that. An id that is
    /// sometimes a line longer is an id a translator cannot lay out.
    /// </remarks>
    public const string SheetsNotFoundWithSkipped = "import.sheets-not-found-with-skipped";

    /// <summary>A source path that is not there.</summary>
    public const string WorkbookPathMissing = "import.workbook-path-missing";

    /// <summary>A file whose extension is not one of the workbook formats.</summary>
    public const string WorkbookFormatUnsupported = "import.workbook-format-unsupported";

    /// <summary>A named service account key file that is not there.</summary>
    public const string GoogleKeyFileMissing = "import.google-key-file-missing";

    /// <summary>A named environment variable holding no service account key.</summary>
    public const string GoogleKeyVariableNotSet = "import.google-key-variable-not-set";

    /// <summary>A service account key that would not read.</summary>
    public const string GoogleKeyUnreadable = "import.google-key-unreadable";

    /// <summary>A named client secret file that is not there.</summary>
    public const string GoogleClientSecretMissing = "import.google-client-secret-missing";

    /// <summary>A log line: `{File}` is a binary workbook, whose cell notes are not read.</summary>

    /// <summary>A log line: This machine's cached token was issued before Tabbit asked for `{Scope}`, and a cached token is.</summary>
    public const string LogCachedTokenPredatesScope = "import.log-cached-token-predates-scope";

    /// <summary>A log line: Defined name `{Name}` of `{Document}` refers to no range.</summary>
    public const string LogDefinedNameNoRange = "import.log-defined-name-no-range";

    /// <summary>A log line: Defined name `{Name}` of `{File}` refers to `{Range}`, which is not a range.</summary>
    public const string LogDefinedNameNotARange = "import.log-defined-name-not-a-range";

    /// <summary>A log line: Defined name `{Name}` of `{Document}` covers {Range}, which this importer cannot read as a singl.</summary>
    public const string LogDefinedNameNotOneRectangle = "import.log-defined-name-not-one-rectangle";

    /// <summary>A log line: Defined name `{Name}` of `{File}` refers to `{Range}`, which this importer cannot read as a sing.</summary>
    public const string LogDefinedNameNotReadable = "import.log-defined-name-not-readable";

    /// <summary>A log line: Defined name `{Name}` of `{Source}` covers {Range}, which is outside the cells sheet `{Sheet}` h.</summary>
    public const string LogDefinedNameOutsideSheet = "import.log-defined-name-outside-sheet";

    /// <summary>A log line: The Drive API is not enabled for the project this credential belongs to.</summary>
    public const string LogDriveApiDisabled = "import.log-drive-api-disabled";

    /// <summary>A log line: `{File}` sheet `{Sheet}`: the workbook reader gave {Rows} row(s) fewer cells than the file holds.</summary>
    public const string LogRowsReadShort = "import.log-rows-read-short";

    /// <summary>A log line: The service account is not allowed `{Scope}`.</summary>
    public const string LogScopeNotAllowed = "import.log-scope-not-allowed";

    /// <summary>A log line: Sheet `{Sheet}` is marked as excluded and is ignored.</summary>
    public const string LogSheetExcluded = "import.log-sheet-excluded";

    /// <summary>A log line: Sheet `{Sheet}.</summary>
    public const string LogTabExcluded = "import.log-tab-excluded";

    /// <summary>A log line: Until it is granted, every run imports the documents whether they changed or not.</summary>
    public const string LogUntilGrantedImportsEverything = "import.log-until-granted-imports-everything";

    /// <summary>A log line: Could not read the version of document `{Document}`, so this run imports it whether or not it ch.</summary>
    public const string LogVersionUnreadable = "import.log-version-unreadable";
}
