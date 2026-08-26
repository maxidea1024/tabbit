using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// A column name from one live project's workbooks, where the name is a path into the row.
/// </summary>
/// <remarks>
/// `patrolBuilding[0]` is element 0 of an array; `character[0]["Id"]` is the `Id` of element
/// 0 of an array of records; `pos["x"]` is the `x` of a record. The original exporter builds
/// its JSON straight out of these.
///
/// Translated into the same three things Tabbit's own `Group.Member` notation produces - a
/// group, a member, and an element ordinal - so one model sits behind two notations. That is
/// the whole point of doing it here: two models would be two answers to "what is this
/// column", and the second one would be wrong somewhere nobody looked.
///
/// The shapes that occur, measured across 615 exported tables, are in
/// samples/named-range/doc/레이아웃-분석-20260808.md. Depth one covers 99.5% of them, but the depth is
/// not capped here - what the name says, this reads.
/// </remarks>
public static class NamedRangeColumnPath
{
    /// <summary>
    /// `[0]` or `["Id"]`, in order.
    /// </summary>
    /// <remarks>
    /// The closing quote is optional, which is not generosity for its own sake: a real
    /// workbook holds `activeEffect[0]["Val]`, and the original exporter reads it as `Val`
    /// because it takes what is inside the brackets and trims quotes off both ends. Refusing
    /// it would mean this layout cannot read data that ships today. The parser reports the
    /// typo so it can be fixed at the source.
    /// </remarks>
    private static readonly Regex Steps =
        new Regex(@"\[\s*(?:(\d+)|""([^""\]]*)""?)\s*\]", RegexOptions.Compiled);

    /// <summary>
    /// Reads a column name as a path into the row: one step per level, outermost first.
    /// </summary>
    /// <param name="path">
    /// The levels, or null when the name has no brackets and so is a plain column. One step
    /// means an array of plain values; two or more mean a record.
    /// </param>
    /// <param name="problem">
    /// Why the name is a shape this does not support, or null when it is fine. Phrased as
    /// the middle of a sentence so the caller can name the column.
    /// </param>
    /// <returns>False only when <paramref name="problem"/> is set. A plain column is not a
    /// failure - it is the ordinary case.</returns>
    /// <remarks>
    /// Two rules, and nothing that counts the brackets:
    ///
    ///   * a bracket holding a name opens a **new level** - `pos["x"]`;
    ///   * a bracket holding a number **numbers the level to its left**, unless that level is
    ///     numbered already, in which case it opens a new level with no name of its own -
    ///     which is what an array of arrays is.
    ///
    /// That is the whole grammar. `character[0]["Id"]` and `guideBattleSkill["BattleSkill"][0]`
    /// fall out of it as the same two levels numbered in different places, and
    /// `a[0]["p"]["x"]` needs no case of its own. See spec/types/nested-multi-level.md.
    /// </remarks>
    public static bool TrySplit(string rawName, out List<Models.FieldPathStep> path, out string? problem)
    {
        path = null!;
        problem = null;

        if (string.IsNullOrEmpty(rawName))
        {
            problem = "is empty.";
            return false;
        }

        int firstBracket = rawName.IndexOf('[');
        if (firstBracket < 0)
            return true;

        string group = rawName.Substring(0, firstBracket).Trim();
        if (group.Length == 0)
        {
            problem = "opens with a bracket, so it names no group.";
            return false;
        }

        var matches = Steps.Matches(rawName, firstBracket);

        // Every bracket has to be one of the two forms. A leftover means the name is
        // something else - a formula, a typo - and guessing at it would put a column of
        // values under a name nobody wrote.
        int consumed = matches.Sum(m => m.Length);
        if (matches.Count == 0 || consumed != rawName.Length - firstBracket)
        {
            problem = $"is not a path this layout reads. Expected `name[0]` or `name[0][\"Member\"]`.";
            return false;
        }

        var steps = new List<Models.FieldPathStep> { new Models.FieldPathStep { Name = group } };

        foreach (Match match in matches)
        {
            // `parts["0"]` - a level named by a number is an element written as an object
            // key. `{"0": a, "1": b}` is a list spelled the long way, and no generated
            // language can have a property called `0`, so it is read as the number it means.
            bool isNumber = match.Groups[1].Success || AllDigits(match.Groups[2].Value);

            if (!isNumber)
            {
                steps.Add(new Models.FieldPathStep { Name = match.Groups[2].Value });
                continue;
            }

            int index = int.Parse(
                match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value,
                CultureInfo.InvariantCulture);

            // A second number on the same level is not a second index on it - it is the
            // level below, reached by position because the sheet gave it no name.
            if (steps[^1].IsIndexed)
                steps.Add(new Models.FieldPathStep());

            steps[^1].Index = index;
        }

        path = steps;
        return true;
    }

    /// <summary>Whether the text is one or more digits and nothing else.</summary>
    private static bool AllDigits(string text)
        => text.Length > 0 && text.All(char.IsAsciiDigit);
}
