using Tabbit.Messages;

namespace Tabbit;

/// <summary>
/// The reports about how the run itself was asked for.
/// </summary>
/// <remarks>
/// Not a step of a conversion but the thing that decides which conversion happens: two flags
/// that contradict each other, an environment named twice, a `--target-side` that is not one
/// of the three. None of these are about a recipe or a sheet, and reporting them under
/// `recipe.` would send somebody to open a file that is correct.
///
/// Small on purpose. Most command-line mistakes never reach here - the argument parser writes
/// its own message and the help text, in its own words. What is left is the handful this tool
/// has to judge for itself, because they are about two settings meaning something together.
/// </remarks>
[TabbitMessages("run")]
public static class RunMessages
{
    /// <summary>A `--target-side` that is not one of the three.</summary>
    public const string TargetSideUnknown = "run.target-side-unknown";

    /// <summary>`--validate-only` and `--force-output` together.</summary>
    public const string ValidateOnlyWithForceOutput = "run.validate-only-with-force-output";

    /// <summary>An environment named on the command line and in the environment.</summary>
    public const string EnvironmentNamedTwice = "run.environment-named-twice";

    /// <summary>An environment name holding characters a path cannot carry.</summary>
    public const string EnvironmentNameIllegal = "run.environment-name-illegal";
}
