using System;
using System.Text.RegularExpressions;

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
    /// What an environment name may be made of.
    /// </summary>
    /// <remarks>
    /// Narrow because this word ends up inside paths. A recipe writing to
    /// `./build/${TABBIT_ENV}/data` is the case this feature is for, and a name holding a
    /// separator or a `..` would put the output somewhere the recipe does not describe -
    /// a typo, not an attack, and one whose result is a build written over the wrong tree.
    ///
    /// Every environment anybody names - `dev`, `live`, `staging`, `qa-2` - is inside this.
    /// </remarks>
    private static readonly Regex Allowed = new Regex(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

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

        // Both of them, because a variable exported by a shell profile reaches the same
        // paths a flag does. Checking only the flag would leave the check off wherever
        // the recipe is actually driven from.
        Check(asked, $"`--env {asked}`");
        Check(inherited, $"{Variable}=`{inherited}`");

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

    private static void Check(string? name, string described)
    {
        if (name is null || Allowed.IsMatch(name))
            return;

        throw new TabbitException(
            $"{described} is not an environment name. This word goes into the paths a " +
            $"recipe builds with `${{{Variable}}}`, so it is limited to letters, digits, " +
            $"`.`, `_` and `-` - anything else would write the output somewhere the " +
            $"recipe does not describe.");
    }
}
