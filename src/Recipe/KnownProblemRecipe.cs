namespace Tabbit.Recipe;

/// <summary>
/// One place whose validation reports this run knows about and does not stop for.
/// </summary>
/// <remarks>
/// For the reports nobody running this tool can fix: the data belongs to somebody else, and
/// one bad cell otherwise means a workbook of five hundred tables produces nothing at all.
///
/// **The report does not go away.** It comes out as <see cref="Severity.Info"/> with
/// <see cref="Reason"/> beside it, on every run. An entry that matches nothing is an error,
/// and so is a <see cref="Count"/> that no longer holds - which is what keeps the list from
/// becoming a switch that lets anything through. spec/known-problems.md.
/// </remarks>
public class KnownProblemRecipe
{
    /// <summary>
    /// The place, as `file`, `file : sheet`, or `file : sheet : cell`.
    /// </summary>
    /// <remarks>
    /// Matched against the front of a report's location, so the wider forms cover everything
    /// under them. The file is matched by the end of the path: a recipe reads a folder, so
    /// entries holding a full path would be a list that differs per machine.
    /// </remarks>
    public string At { get; set; } = "";

    /// <summary>
    /// How many reports this entry accounts for, or zero for "one or more".
    /// </summary>
    /// <remarks>
    /// Stated so that a new problem in a place already written down stops the run. Without it
    /// a whole sheet can be written off and the seventh defect in it arrives unannounced.
    /// </remarks>
    public int Count { get; set; }

    /// <summary>Why this is not being fixed now. Required.</summary>
    /// <remarks>
    /// Half of what this feature is. An entry that need not say why is a switch, and a switch
    /// like this one gets turned on once and never looked at again.
    /// </remarks>
    public string Reason { get; set; } = "";
}
