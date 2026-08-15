using System.Collections.Generic;

namespace Tabbit.History;

/// <summary>
/// Everything one view of the history needs, in one object.
///
/// The page is drawn from this whether it was written into a file by
/// `--history --format html` or fetched from the server. One contract, two sources -
/// so the offline copy somebody mailed around and the live page cannot disagree, and
/// there is one renderer rather than two that drift.
/// </summary>
public sealed class DashboardDocument
{
    public int SchemaVersion { get; set; } = 1;

    public required string Project { get; set; }

    public required string Branch { get; set; }

    /// <summary>Every branch of the project, so the page can offer them.</summary>
    public IReadOnlyList<string> Branches { get; set; } = [];

    /// <summary>The statistics of the range's end. Null when the branch has no snapshots.</summary>
    public required SummaryDocument? Stats { get; set; }

    /// <summary>What changed over the range.</summary>
    public required HistoryDocument History { get; set; }

    /// <summary>Snapshots of the branch, newest first - what the timeline is drawn from.</summary>
    public IReadOnlyList<SnapshotListing> Snapshots { get; set; } = [];

    /// <summary>Row count per snapshot, oldest first.</summary>
    public IReadOnlyList<TrendPoint> Rows { get; set; } = [];

    /// <summary>Changed cells per snapshot, oldest first.</summary>
    public IReadOnlyList<TrendPoint> Churn { get; set; } = [];

    /// <summary>Who changed how much, over the range.</summary>
    public IReadOnlyList<AuthorSummary> Authors { get; set; } = [];
}
