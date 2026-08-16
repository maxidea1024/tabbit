using System;

namespace Tabbit;

/// <summary>
/// The word that says which environment a run is for.
/// </summary>
/// <remarks>
/// One word doing two jobs, which is the point of it being here rather than read twice.
///
/// It labels the run - the summary records it, so a build can be told from its output
/// instead of from whoever remembers launching it. And it is what the recipe's
/// `${TABBIT_ENV}` resolves to, so the sheets a run reads and the directory it writes
/// come from the same word that labels it.
///
/// Read as two settings those could disagree, and that disagreement is the whole reason
/// this exists: output stamped `live` that was built from the development sheets is
/// worse than output with no label at all, because it answers the question wrongly
/// rather than leaving it open.
/// </remarks>
internal static class RunEnvironment
{
    /// <summary>Variable a recipe names to reach the environment word.</summary>
    public const string Variable = "TABBIT_ENV";

    /// <summary>
    /// Settles the environment for this run, publishing it where the recipe can see it.
    /// </summary>
    /// <returns>The environment name, or null when the run does not name one.</returns>
    public static string? Establish(Options options)
    {
        string? asked = string.IsNullOrWhiteSpace(options.EnvironmentName)
            ? null
            : options.EnvironmentName.Trim();

        string? inherited = Environment.GetEnvironmentVariable(Variable);

        if (string.IsNullOrWhiteSpace(inherited))
            inherited = null;

        if (asked is null)
            return inherited;

        // Refused rather than resolved either way. Whichever one won, the run would be
        // labelled by one and built by the other for as long as nobody looked.
        if (inherited is not null && !string.Equals(inherited, asked, StringComparison.Ordinal))
        {
            throw new TabbitException(
                $"`--env {asked}` was given while {Variable} is already set to `{inherited}`. " +
                $"They label the same run and decide the same paths, so one of them is wrong. " +
                $"Clear the variable, or pass the environment it names.");
        }

        Environment.SetEnvironmentVariable(Variable, asked);

        return asked;
    }
}
