using Tabbit.Models;

namespace Tabbit.Targets;

/// <summary>
/// Reads the side the whole run is narrowed to, from `--target-side`.
///
/// The counterpart of <see cref="Recipe.RecipeTargetSide"/>, separate for the same
/// reason that one is: the diagnostics differ. A bad value in a recipe has to name the
/// section it appeared in, while a bad value on the command line has to name the
/// option and list what it accepts.
///
/// Spelled-out names are accepted as well as the recipe's one-letter markers, because
/// a recipe is written once and read often whereas this is typed by hand, frequently
/// from a build script where `--target-side server` documents itself and `-s` does not.
/// </summary>
public static class CommandLineTargetSide
{
    /// <summary>
    /// The requested side, or <see cref="TargetSide.Both"/> when the option was not
    /// given - which makes a run without it behave exactly as before.
    /// </summary>
    public static TargetSide Of(Options? options) => Of(options?.TargetSide);

    /// <summary>
    /// Parses the option's value, or throws listing what is accepted.
    /// </summary>
    public static TargetSide Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TargetSide.Both;

        switch (text.Trim().ToLowerInvariant())
        {
            case "c":
            case "client":
                return TargetSide.ClientOnly;

            case "s":
            case "server":
                return TargetSide.ServerOnly;

            case "cs":
            case "sc":
            case "both":
                return TargetSide.Both;
        }

        throw new TabbitException(
            $"--target-side `{text}` is not recognized. " +
            "Use `client`, `server`, or `both` (the default).");
    }
}
