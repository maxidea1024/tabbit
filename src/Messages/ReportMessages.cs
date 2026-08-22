using Tabbit.Messages;

namespace Tabbit;

/// <summary>
/// The few reports the reporting machinery writes about itself.
/// </summary>
/// <remarks>
/// One entry, and it exists because the headline over a list of problems is a sentence of its
/// own: a summary the caller supplies, with a count added when there is more than one thing
/// under it. The count cannot go in the caller's own sentence - the caller does not know it
/// yet - and it cannot be spliced in afterwards without putting a conditional inside a
/// message.
/// </remarks>
[TabbitMessages("report")]
public static class ReportMessages
{
    /// <summary>A summary with the number of problems it stands over.</summary>
    public const string ProblemsCounted = "report.problems-counted";
}
