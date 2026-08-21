using System;

namespace Tabbit;

/// <summary>
/// A defect in this tool, as opposed to a problem with the data or the recipe.
/// </summary>
/// <remarks>
/// Every report this tool writes belongs to somebody: a cell somebody has to fix, a recipe
/// setting somebody has to write, or a gap in this program that only we can close. The first
/// two are <see cref="TabbitException"/>. This is the third, and it is separate for two
/// reasons.
///
/// **It must not be catchable as a data problem.** The places that turn an exception into a
/// diagnostic and carry on - a layout parser skipping a table it could not read, the
/// validation pipeline recording that a rule threw - are right to do that for a sheet
/// problem and wrong to do it for this one: the run would report somebody else's data as
/// broken and then produce output. So this does not derive from
/// <see cref="TabbitException"/>, which those handlers name, and the handlers that catch
/// everything say `when (… is not TabbitDefectException)` so it passes through them.
///
/// **It is not translated.** The reports a data owner reads are worth having in their own
/// language; a defect report is read by us, and a stack trace beside a sentence we cannot
/// find in our own repository is worse than one we can. spec/message-ids.md §3.
///
/// The stack is what makes one of these actionable, so <c>Program</c> prints it without
/// being asked - unlike a data problem, where the location in the sheet is the whole story
/// and a stack is noise.
/// </remarks>
public sealed class TabbitDefectException : Exception
{
    /// <summary>Construct with message.</summary>
    public TabbitDefectException(string message) : base(message)
    {
    }

    /// <summary>Construct with message and the exception that revealed the defect.</summary>
    public TabbitDefectException(string message, Exception inner) : base(message, inner)
    {
    }
}
