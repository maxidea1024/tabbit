namespace Tabbit.Recipe;

/// <summary>
/// When a run opens what it found, and where it leaves it.
/// </summary>
/// <remarks>
/// The report exists because the person who can fix a sheet is not the person watching the
/// console. Everything here is about reaching them: whether the page opens by itself, and
/// where it waits when it does not. spec/build-report.md.
/// </remarks>
public class ReportRecipe
{
    /// <summary>Whether a run writes a report at all.</summary>
    /// <remarks>
    /// On, and the default is the feature. A report somebody has to switch on is a log file
    /// with better formatting - the reports it carries are already in the console and in
    /// `logs/`, and neither is read by the person holding the workbook.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Folder to write the report into. Beside the build seal when left out.
    /// </summary>
    /// <remarks>
    /// Set it to somewhere a build pipeline collects artifacts from. The default is under
    /// `.tabbit/`, which is out of version control already - a report is about one run on
    /// one machine and committing it would put a merge conflict on every branch.
    /// </remarks>
    public string Path { get; set; } = "";

    /// <summary>
    /// When to open the report in a browser: `never`, `problems`, or `always`.
    /// </summary>
    /// <remarks>
    /// Text rather than an enum because it comes from JSON, and a misspelling should be
    /// reported against the recipe rather than deserialize quietly to the default - the
    /// same reason a target's side is text. <see cref="ReportOpening"/> reads it.
    /// </remarks>
    public string OpenInBrowser { get; set; } = "problems";

    /// <summary>
    /// Most reports to put on the page. `0` for all of them.
    /// </summary>
    /// <remarks>
    /// The page is one file and a run has produced 5,831 reports, which is a page nobody
    /// can open on the machine that has to open it fastest. What is cut is stated on the
    /// page and never cut from the JSON, so the number is a reading limit rather than a
    /// record limit.
    /// </remarks>
    public int MaxHtmlEntries { get; set; } = 5000;
}
