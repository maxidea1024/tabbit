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
}
