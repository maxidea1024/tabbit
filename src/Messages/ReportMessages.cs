using Tabbit.Messages;

namespace Tabbit;

/// <summary>
/// What the reporting machinery writes: the headline over a list of problems, and every
/// word of the page that list is written onto.
/// </summary>
/// <remarks>
/// The first entry exists because a headline over a list is a sentence of its own: a summary
/// the caller supplies, with a count added when there is more than one thing under it. The
/// count cannot go in the caller's own sentence - the caller does not know it yet - and it
/// cannot be spliced in afterwards without putting a conditional inside a message.
///
/// The rest is the build report's own wording. It is in the catalog rather than in the page
/// writer for one reason: the reports on that page arrive already written in whatever
/// language the run was asked for, and a page whose headings stayed English around them
/// would be a page in two languages. spec/build-report.md.
/// </remarks>
[TabbitMessages("report")]
public static class ReportMessages
{
    /// <summary>A summary with the number of problems it stands over.</summary>
    public const string ProblemsCounted = "report.problems-counted";

    // ---------------------------------------------------------------- the page

    /// <summary>The page's title, which is also its tab.</summary>
    public const string PageTitle = "report.page-title";

    /// <summary>Said across the top: the run did what it was asked to.</summary>
    public const string OutcomeSuccess = "report.outcome-success";

    /// <summary>The same, for a run that found nothing to redo.</summary>
    public const string OutcomeNothingToDo = "report.outcome-nothing-to-do";

    /// <summary>The same, for a run the data stopped.</summary>
    public const string OutcomeStopped = "report.outcome-stopped";

    /// <summary>The same, for a run that failed for another reason.</summary>
    public const string OutcomeFailed = "report.outcome-failed";

    // ------------------------------------------------------------------ counts

    public const string LabelErrors = "report.label-errors";
    public const string LabelWarnings = "report.label-warnings";
    public const string LabelNotes = "report.label-notes";
    public const string LabelNew = "report.label-new";
    public const string LabelPersisting = "report.label-persisting";
    public const string LabelResolved = "report.label-resolved";

    // ---------------------------------------------------------------- sections

    public const string SectionProblems = "report.section-problems";
    public const string SectionKnown = "report.section-known";
    public const string SectionResolved = "report.section-resolved";
    public const string SectionNotes = "report.section-notes";
    public const string SectionDefect = "report.section-defect";

    /// <summary>The group a report with no cell to name belongs to.</summary>
    public const string GroupRun = "report.group-run";

    // -------------------------------------------------------------------- meta

    public const string MetaRecipe = "report.meta-recipe";
    public const string MetaTool = "report.meta-tool";
    public const string MetaStarted = "report.meta-started";
    public const string MetaElapsed = "report.meta-elapsed";

    // ---------------------------------------------------------------- controls

    public const string SearchPlaceholder = "report.search-placeholder";
    public const string ExpandAll = "report.expand-all";
    public const string CollapseAll = "report.collapse-all";

    /// <summary>Which axis the problems are gathered under.</summary>
    /// <remarks>
    /// Two, because they answer different questions. By sheet is the order the work is done
    /// in - open a workbook, fix its cells. By kind is the order the work is understood in,
    /// and it is the one that helps when a single kind accounts for most of the page.
    /// </remarks>
    public const string GroupBy = "report.group-by";

    public const string GroupBySheet = "report.group-by-sheet";
    public const string GroupByKind = "report.group-by-kind";
    public const string Copy = "report.copy";
    public const string Copied = "report.copied";
    public const string OpenInSheet = "report.open-in-sheet";

    // ------------------------------------------------------------------ states

    public const string NoProblems = "report.no-problems";
    public const string NothingMatches = "report.nothing-matches";

    /// <summary>Said where the page stops listing, naming the file that does not.</summary>
    public const string Truncated = "report.truncated";

    /// <summary>Said instead of the new/still-here counts on a first run.</summary>
    public const string Uncompared = "report.uncompared";

    public const string BadgeNew = "report.badge-new";
    public const string BadgePersisting = "report.badge-persisting";

    /// <summary>
    /// Said over a defect, so nobody goes looking through their sheets for a cause that is
    /// not there.
    /// </summary>
    public const string DefectNote = "report.defect-note";
}
