using Serilog;

namespace Tabbit;

/// <summary>
/// Which step of a run a log line came from.
/// </summary>
/// <remarks>
/// A conversion writes a few hundred lines and they all read alike: a sheet being measured,
/// a table being parsed, a file being written. Which step each belongs to is what makes the
/// run legible - a warning about a name that does not resolve means one thing while sources
/// are being read and another while output is being written.
///
/// Each class says which step it belongs to, once, rather than the step being inferred from
/// whatever call stack a line happens to be under. The declaration is a property named `Log`,
/// which hides the static <see cref="Serilog.Log"/> the class was already calling - so the
/// calls themselves are untouched and a class that forgets to declare one still logs, under
/// <see cref="Default"/>.
///
/// The steps are the run's own, not any one project's: every conversion reads sources, cooks
/// them, checks them and writes them out, whatever the sheets hold. Each is named for what is
/// happening while the line is written, not for the thing it produces.
/// </remarks>
public static class LogCategory
{
    /// <summary>The property the output templates read.</summary>
    public const string PropertyName = "Category";

    /// <summary>What a line whose class declared no category is tagged with.</summary>
    public const string Default = "Tabbit";

    /// <summary>Reading the recipe and settling what the run was asked to do.</summary>
    public static ILogger Loading => Of("Loading");

    /// <summary>Reading the sources the recipe lists into the raw model.</summary>
    public static ILogger Importing => Of("Importing");

    /// <summary>Turning the raw model into the cooked one.</summary>
    public static ILogger Cooking => Of("Cooking");

    /// <summary>Checking the cooked model against the rules the recipe points at.</summary>
    public static ILogger Validating => Of("Validating");

    /// <summary>Writing every export and generating every language's code.</summary>
    public static ILogger Exporting => Of("Exporting");

    /// <summary>Moving the staged files onto their destinations.</summary>
    public static ILogger Committing => Of("Committing");

    /// <summary>Recording what a run produced, and answering questions about earlier ones.</summary>
    public static ILogger Recording => Of("Recording");

    /// <summary>Deciding what this run has to do at all, and what a previous one already did.</summary>
    public static ILogger Caching => Of("Caching");

    /// <summary>What each step of the run took.</summary>
    public static ILogger Timing => Of("Timing");

    /// <summary>
    /// A logger that tags everything written through it with <paramref name="category"/>.
    /// </summary>
    /// <remarks>
    /// Resolved on every call rather than held in a static field, because the static logger
    /// is not configured until the run starts - a field would capture whatever was there
    /// when its class was first touched, which for an early type is the silent logger.
    /// </remarks>
    public static ILogger Of(string category) => Serilog.Log.ForContext(PropertyName, category);
}
