using System.Collections.Generic;

namespace Tabbit.History;

/// <summary>
/// The answer to a range query: who changed what, between two commits.
///
/// The same shape whether it was asked for on the command line or over HTTP. Two
/// renderings of one question drift, and the one that is wrong looks exactly like the
/// one that is right - so there is one, and both entry points serialise it.
/// </summary>
public sealed class HistoryDocument
{
    public int SchemaVersion { get; set; } = 1;

    public required HistoryQueryInfo Query { get; set; }

    /// <summary>Oldest first, so a reader follows the changes forwards.</summary>
    public IReadOnlyList<HistorySnapshotView> Snapshots { get; set; } = [];

    /// <remarks>Filled once every snapshot has been read, so it cannot be supplied at construction.</remarks>
    public HistoryTotals? Totals { get; set; }

    /// <summary>
    /// What shipping this whole range requires: the union of its snapshots' verdicts.
    ///
    /// This is the range question asked directly - "to go from A to B, what do I
    /// deploy?" - and a union is the only honest answer, because a code deploy needed
    /// by any snapshot in the middle is needed by the range.
    /// </summary>
    public DeploymentAdvice? Deployment { get; set; }
}

/// <summary>What was asked, echoed back so a stored answer explains itself.</summary>
public sealed class HistoryQueryInfo
{
    public required string Project { get; set; }
    public required string Branch { get; set; }

    /// <summary>
    /// The commit the range starts after.
    ///
    /// Exclusive: it is the state being compared from, so its own changes belong to the
    /// range before this one. Null means from the beginning of the branch.
    /// </summary>
    public string? From { get; set; }

    /// <summary>The commit the range ends at, inclusive. Null means the branch's head.</summary>
    public string? To { get; set; }

    public string? Table { get; set; }
    public string? Field { get; set; }
    public string? Author { get; set; }

    public required string GeneratedAt { get; set; }

    /// <summary>How many changes were asked for at most.</summary>
    public int Limit { get; set; }

    /// <summary>
    /// Whether the answer was cut short.
    ///
    /// Said out loud rather than left to be noticed. A truncated list that does not
    /// admit it reads as a complete one, and the conclusion drawn from it - "nothing
    /// else changed" - is wrong.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>How many changes were left out by the limit.</summary>
    public long Omitted { get; set; }

    /// <summary>
    /// Things the answer did that were not asked for.
    ///
    /// A tag resolved to a commit; a commit with no snapshot stood in for by the one
    /// behind it. Each is a reasonable thing to do and each changes what the numbers
    /// describe, so each is said rather than assumed to be fine.
    /// </summary>
    public IReadOnlyList<string> Notes { get; set; } = [];
}

/// <summary>One snapshot, and what changed to reach it.</summary>
public sealed class HistorySnapshotView
{
    public long Id { get; set; }
    public long Seq { get; set; }

    public required string Commit { get; set; }
    public required string? ShortCommit { get; set; }
    public required string Branch { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public string? CommittedAt { get; set; }
    public string? Subject { get; set; }
    public string? ConvertedAt { get; set; }
    public string? ConvertedBy { get; set; }

    public bool Dirty { get; set; }

    /// <summary>Whether these changes can honestly be credited to this commit's author.</summary>
    public bool Attributable { get; set; }

    /// <summary>
    /// Whether the previous snapshot's commit is this one's parent in the repository.
    ///
    /// False means nothing converted the commits in between, so these changes cover
    /// more than this commit made. Reported rather than smoothed over: the alternative
    /// is a report that credits one person with several people's work.
    /// </summary>
    public bool FollowsParent { get; set; }

    /// <summary>The commit these changes are measured from. Null for a branch's first.</summary>
    public string? PreviousCommit { get; set; }

    /// <summary>
    /// Whether this snapshot's change detail has been removed to reclaim space.
    ///
    /// Its statistics and its stored summary are still here; the cell-by-cell log is
    /// not. Reported, because an empty changeset that does not say why reads as
    /// "nothing changed in this commit" - which is a different and wrong answer.
    /// </summary>
    public bool Pruned { get; set; }

    /// <remarks>Counted once the snapshot's changes have been read, so it cannot be
    /// supplied where the snapshot row is.</remarks>
    public HistoryChangeCounts Counts { get; set; } = new();

    /// <summary>
    /// What shipping this snapshot requires - data patch, code deploy, or both.
    ///
    /// Null for a pruned snapshot: its change detail is gone, and a verdict computed
    /// from nothing would read as "nothing to ship", which is a different and wrong
    /// answer.
    /// </summary>
    public DeploymentAdvice? Deployment { get; set; }

    /// <remarks>Filled after the snapshot row is read, when its changes are fetched.</remarks>
    public IReadOnlyList<SchemaChangeView> Schema { get; set; } = [];
    /// <remarks>Filled with the same pass as Schema and Rows, once the snapshot row
    /// itself has been read. Not required for that reason.</remarks>
    public IReadOnlyList<CellChangeView> Cells { get; set; } = [];
    /// <remarks>Filled after the snapshot row is read, with the same pass that fetches
    /// its schema changes. Not required for that reason.</remarks>
    public IReadOnlyList<RowChangeView> Rows { get; set; } = [];
}

public sealed class HistoryChangeCounts
{
    public int Schema { get; set; }
    public int Rows { get; set; }
    public int Cells { get; set; }
}

public sealed class HistoryTotals
{
    public int Snapshots { get; set; }
    public long Schema { get; set; }
    public long Rows { get; set; }
    public long Cells { get; set; }

    /// <summary>How many snapshots in the range cover more than their own commit.</summary>
    public int Gaps { get; set; }

    /// <summary>How many have had their change detail removed.</summary>
    public int Pruned { get; set; }
}

public sealed class SchemaChangeView
{
    public required string EntityKind { get; set; }
    public required string Entity { get; set; }
    public required string? Member { get; set; }
    public required string Kind { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public SummaryLocation? Location { get; set; }

    /// <summary>The name this column had before, when it was renamed rather than replaced.</summary>
    public string? RenamedFrom { get; set; }
}

public sealed class RowChangeView
{
    public string? Table { get; set; }
    public required string RowKey { get; set; }
    public required string Kind { get; set; }
}

public sealed class CellChangeView
{
    public string? Table { get; set; }
    public required string RowKey { get; set; }
    public string? Field { get; set; }
    public required string Kind { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public SummaryLocation? Location { get; set; }
}

// -------------------------------------------------------------- listings

/// <summary>One snapshot, without its changes: what a timeline is drawn from.</summary>
public sealed class SnapshotListing
{
    public long Id { get; set; }
    public long Seq { get; set; }
    public required string Commit { get; set; }
    public required string? ShortCommit { get; set; }
    public required string Branch { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public string? CommittedAt { get; set; }
    public string? Subject { get; set; }
    public string? ConvertedAt { get; set; }
    public bool Dirty { get; set; }
    public bool Attributable { get; set; }
    public bool Pruned { get; set; }

    public required HistoryChangeCounts Counts { get; set; }
}

/// <summary>One point of a trend line.</summary>
public sealed class TrendPoint
{
    public required string Commit { get; set; }
    public required string? ShortCommit { get; set; }
    public string? CommittedAt { get; set; }
    public long Value { get; set; }
}

/// <summary>How much one person changed over a range.</summary>
public sealed class AuthorSummary
{
    public required string Name { get; set; }
    public required string? Email { get; set; }
    public int Snapshots { get; set; }
    public long Cells { get; set; }
    public long Rows { get; set; }
    public long Schema { get; set; }
    public required string? FirstAt { get; set; }
    public required string? LastAt { get; set; }
}

/// <summary>Every value one cell has held, newest first.</summary>
public sealed class CellHistoryEntry
{
    public required string Commit { get; set; }
    public required string? ShortCommit { get; set; }
    public string? AuthorName { get; set; }
    public string? CommittedAt { get; set; }
    public string? Table { get; set; }
    public required string RowKey { get; set; }
    public string? Field { get; set; }
    public required string Kind { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}
