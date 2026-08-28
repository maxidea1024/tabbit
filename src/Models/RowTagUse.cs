namespace Tabbit.Models;

/// <summary>
/// One row tag this run met, and what it did.
/// </summary>
/// <remarks>
/// **What a build left out, in the file a build leaves behind.** Tag names are not declared
/// anywhere, so nothing can tell a misspelled one from a new one - this list is what lets a
/// person see that `wpi` was written on one row and took nothing out with it.
///
/// On the model rather than only in the log, because the summary is the document every other
/// view renders from and a log line scrolls away. spec/layout/tags.md section 5.
/// </remarks>
public sealed class RowTagUse
{
    /// <summary>As the sheet wrote it: `wip`, or `stage=test`.</summary>
    public required string Tag { get; init; }

    /// <summary>Rows carrying it, left out or not.</summary>
    public required int Rows { get; init; }

    /// <summary>How many of those this build left out.</summary>
    public required int Omitted { get; init; }
}
