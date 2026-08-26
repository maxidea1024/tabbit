using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Helpers;

/// <summary>
/// The `Group.Member` notation that folds several columns into a record.
///
/// `Pos.X` and `Pos.Y` are one record; `Slot1.Id` and `Slot2.Id` are an array of them,
/// with the array length coming from the serial number on the group part exactly as it
/// does for a plain <see cref="Tabbit.Models.SerialField"/>. The rules and the reasons
/// are in spec/types/nested-fields.md.
///
/// Splitting happens before Pascal-casing, so each part is normalized on its own and a
/// separator can never be produced or consumed by the case conversion.
/// </summary>
public static class NestedName
{
    /// <summary>
    /// What separates a group from its member. A `.` because it is the one character a
    /// field name could not already contain - <see cref="Extensions.StringExtensions.IsValidIdentifier"/>
    /// rejects it - so the notation takes over names that were errors rather than
    /// changing the meaning of names that worked.
    /// </summary>
    public const char MemberSeparator = '.';

    /// <summary>
    /// Splits a column name into its levels, outermost first.
    /// </summary>
    /// <param name="rawName">
    /// The name as written in the sheet, after the wire tag and any `*` have been taken
    /// off, and before Pascal-casing.
    /// </param>
    /// <param name="parts">
    /// The levels the name names. One entry - the whole name - when it is not nested, and
    /// one per level otherwise. **However many the sheet wrote**: the notation does not cap
    /// the depth, because the shapes that occur do not (spec/types/nested-multi-level.md).
    /// </param>
    /// <param name="problem">
    /// Why the name uses the separator in a way this does not support, or null when it is
    /// fine. Phrased as the middle of a sentence so callers can name the cell.
    /// </param>
    /// <returns>False only when <paramref name="problem"/> is set. A name with no
    /// separator is not a failure - it is the ordinary case, and reports itself by
    /// returning a single part.</returns>
    public static bool TrySplit(string rawName, out List<string> parts, out string? problem)
    {
        parts = new List<string> { rawName };
        problem = null;

        if (string.IsNullOrEmpty(rawName))
            return true;

        if (rawName.IndexOf(MemberSeparator) < 0)
            return true;

        var split = rawName.Split(MemberSeparator).Select(part => part.Trim()).ToList();

        // Every level has to name something. `.Id`, `Slot1.` and `A..B` are more likely a
        // typo than an intent, and any of them would otherwise produce a level with an
        // empty name that fails much later with a worse message.
        //
        // A level written as digits alone is **not** empty - `Grid1.2` numbers a level
        // rather than naming it, which is a shape rather than a mistake.
        if (split.Any(part => part.Length == 0))
        {
            problem = $"has an empty level around `{MemberSeparator}`. "
                    + $"Write it as `Group{MemberSeparator}Member`, as in `Slot1{MemberSeparator}Id`.";
            return false;
        }

        parts = split;
        return true;
    }

    /// <summary>
    /// Whether a name carries the separator at all, without deciding whether it does so
    /// legally. For the callers that only need to know a name is not a plain column.
    /// </summary>
    public static bool LooksNested(string rawName)
        => !string.IsNullOrEmpty(rawName) && rawName.IndexOf(MemberSeparator) >= 0;
}
