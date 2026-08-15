using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Models;

/// <summary>
/// One level of a column's path into the row: what it is called, and which element of it
/// this column holds.
/// </summary>
/// <remarks>
/// A column name is a path, whichever notation wrote it. `Slot1.Id` and
/// `character[0]["Id"]` are the same two levels said two ways, and this is where the two
/// meet - so the folding below never has to know which notation it came from.
///
/// The two questions are independent, and that independence is the whole content of the
/// type. A level can be named and not repeat (`Pos` in `Pos.X`), repeat and not be named
/// (the second level of `Grid1.2`), both, or neither. Every shape the tool supports is
/// some combination of those two bits at each level - see spec/nested-multi-level.md.
/// </remarks>
public sealed class FieldPathStep
{
    /// <summary>
    /// What this level is called, Pascal cased, or empty when the level is reached by
    /// number instead of by name.
    /// </summary>
    /// <remarks>
    /// Empty is not a missing name. It says the sheet numbered this level rather than
    /// naming it, which is what makes an array of arrays an array of arrays: there is no
    /// word a consumer could write, so it indexes instead.
    /// </remarks>
    public string Name { get; set; } = "";

    /// <summary>
    /// Which element of this level the column holds, or null when the level does not
    /// repeat.
    /// </summary>
    /// <remarks>
    /// Only the order matters, not the base: the folding sorts by it, so a sheet counting
    /// from 1 and a sheet counting from 0 both come out right.
    /// </remarks>
    public int? Index { get; set; }

    /// <summary>Whether this level is reached by number rather than by name.</summary>
    public bool IsAnonymous => Name.Length == 0;

    /// <summary>Whether this level repeats, so the thing above it holds several.</summary>
    public bool IsIndexed => Index.HasValue;

    /// <summary>How this level reads in a diagnostic.</summary>
    public override string ToString()
        => IsIndexed
            ? (IsAnonymous ? $"[{Index}]" : $"{Name}[{Index}]")
            : Name;
}

/// <summary>
/// Helpers over a column's path, kept off <see cref="Field"/> so the questions the folding
/// asks are in one place rather than spread across the callers that ask them.
/// </summary>
public static class FieldPath
{
    /// <summary>
    /// How a path reads in a diagnostic: the levels joined the way the notation writes
    /// them.
    /// </summary>
    public static string Describe(IReadOnlyList<FieldPathStep> path)
        => (path is null) ? "" : string.Join(".", path.Select(step => step.ToString()));

    /// <summary>
    /// Which levels of a path repeat, by their position in it.
    /// </summary>
    /// <remarks>
    /// The one thing every column of a group has to agree on. Names do not have to agree -
    /// `Pos.X` and `Pos.Y` are siblings, and `Star1.Id` beside `Star1.Position.X` is a
    /// record holding a value and a record - but **where the element number sits** does. A
    /// group written `Pos.X1` in one column and `Pos1.X` in another is an array of records
    /// and a record of arrays at once, and the generated declaration would have to be both.
    /// </remarks>
    public static List<int> RepeatingLevels(IReadOnlyList<FieldPathStep> path)
    {
        var result = new List<int>();

        if (path is null)
            return result;

        for (int level = 0; level < path.Count; level++)
        {
            if (path[level].IsIndexed)
                result.Add(level);
        }

        return result;
    }

    /// <summary>
    /// Whether two paths number the same levels.
    /// </summary>
    public static bool SameRepeatingLevels(IReadOnlyList<FieldPathStep> left, IReadOnlyList<FieldPathStep> right)
        => RepeatingLevels(left).SequenceEqual(RepeatingLevels(right));
}
