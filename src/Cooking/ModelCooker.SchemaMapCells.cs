using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Cooking;

/// <summary>
/// A map written as pairs in one cell, folded into the two columns it is.
/// </summary>
/// <remarks>
/// **Not a second way of holding a map - a second way of writing one.** `1:10;2:20` in one
/// cell and `1;2` beside `10;20` in two are the same entries, and this turns the first into
/// the second before anything downstream can tell which was written. So the byte-identical
/// pair that the two column notations are held to covers this one as well.
///
/// The two halves come out as **text**, joined by the array delimiter, rather than as parsed
/// values. That is what makes this small: the columns that result are ordinary delimited
/// array columns, and every element is then read, reported on and constrained by the code
/// that already reads those.
///
/// spec/types/set-and-map.md section 5.2.
/// </remarks>
public partial class ModelCooker
{
    /// <summary>What separates a key from its value inside one entry.</summary>
    /// <remarks>
    /// One of the three characters this notation spends - the entries themselves are
    /// separated by the array delimiter, which is the source entry's to choose.
    /// </remarks>
    private const char MapPairSeparator = ':';

    /// <param name="refused">
    /// Filled with the columns this pass reported on. Such a column is left as it was
    /// written, and the binding skips it rather than reporting the consequence of a mistake
    /// whose cause has already been named.
    /// </param>
    private static void ExpandMapCells(
        CookingContext context,
        Model model,
        SchemaDeclarations declarations,
        HashSet<Field> refused,
        Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            var packed = new Dictionary<Field, SchemaField>();

            foreach (var group in GroupsOf(table))
            {
                var naming = NamingColumnsOf(group, declarations);

                if (naming.Count != 1)
                    continue;

                var declared = declarations.FindStruct(naming[0].TypeName);

                if (declared is null || declared.IsAbstract)
                    continue;

                foreach (var field in group)
                {
                    if (PairedMapOf(field, declared, declarations) is { } member)
                        packed.Add(field, member);
                }
            }

            if (packed.Count == 0)
                continue;

            // A column this refuses is not folded. Splitting a cell whose shape has just
            // been reported would produce two columns from a notation that was told it
            // cannot have them, and every pass below would then report those.
            foreach (var (field, member) in packed.ToList())
            {
                if (RefuseWhatPairsCannotCarry(table, field, member, declarations, diagnostics))
                    continue;

                refused.Add(field);
                packed.Remove(field);
            }

            if (packed.Count == 0)
                continue;

            var original = table.Fields.ToList();

            table.Fields = original
                .SelectMany(field => packed.TryGetValue(field, out var member)
                    ? SplitIntoKeyAndValue(field, member)
                    : [field])
                .ToList();

            // Rows before renumbering, for the reason the packed-struct expansion writes
            // down: a column that was not split is the same object in both lists, so
            // renumbering first would move the very index the rewrite reads its cells by.
            RewritePairedRows(context, table, original, packed, diagnostics);

            for (int at = 0; at < table.Fields.Count; at++)
                table.Fields[at].Index = at;

            // The wire columns snapshot a field's type and tag assignment has already built
            // them, so nothing here is visible until they are rebuilt.
            table.InvalidateDerivedColumns();

            // Ordinal tags were positions in a column list that no longer exists - one
            // column became two, and every column after it moved. A table that wrote its own
            // tags has been refused above.
            foreach (var field in table.Fields)
                field.WireTag = null;

            context.AssignTags(table);
        }
    }

    /// <summary>
    /// The map a column holds as pairs, or null when the column is not one.
    /// </summary>
    /// <remarks>
    /// The path has to stop at the map. A sheet that wrote `Prices.Key` and `Prices.Value`
    /// has already written the two columns, and there is nothing here to fold.
    /// </remarks>
    private static SchemaField? PairedMapOf(
        Field field, SchemaStruct declared, SchemaDeclarations declarations)
    {
        var member = Walk(field, declared, declarations, out var container, out int level);

        if (member is null || container != ContainerKind.Map)
            return null;

        return level == field.NamePath!.Count - 1 ? member : null;
    }

    /// <summary>
    /// The shapes one cell of pairs cannot hold, reported where the column is.
    /// </summary>
    /// <returns>False when something was reported, so the column is left unfolded.</returns>
    private static bool RefuseWhatPairsCannotCarry(
        Table table,
        Field field,
        SchemaField member,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        // A struct value is several columns, so one component of one entry would have to be
        // several values - which needs a third separator, and settling that is the problem
        // `sep` has rather than the one this section solves.
        if (declarations.FindStruct(member.Type.Arguments[1].Name) is { } structured)
        {
            diagnostics.Error(field.NameLocation, Message.Of(
                SchemaMessages.MapPairsHoldAStruct,
                ("Table", table.Name), ("Column", field.RawName),
                ("Member", member.Name), ("Type", structured.Name)));

            return false;
        }

        // One column becoming two is one wire tag becoming two, and a table that writes its
        // own has written one. The same refusal a packed struct meets, for the same reason.
        if (table.HasExplicitTags)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.MapPairsTableWritesTags,
                ("Table", table.Name), ("Column", field.RawName), ("Member", member.Name)));

            return false;
        }

        return true;
    }

    /// <summary>
    /// The two columns a paired map is, named the way the two-column notation names them.
    /// </summary>
    /// <remarks>
    /// Both left for the binding to type. The first keeps the type cell it was written with,
    /// so a group that named its struct in this column still names it; the second has no
    /// cell of its own and says so by leaving the name empty, which is what an unwritten
    /// type cell already means where declarations are read.
    /// </remarks>
    private static IEnumerable<Field> SplitIntoKeyAndValue(Field field, SchemaField member)
    {
        foreach (string slot in new[] { SchemaContainers.KeyMember, SchemaContainers.ValueMember })
        {
            bool first = slot == SchemaContainers.KeyMember;

            yield return new Field
            {
                OwnerTable = field.OwnerTable,

                // All four of the header cells the column was written in. A report about
                // either half points at the cell an author can edit, and that is the paired
                // column's own header - neither half has one.
                NameLocation = field.NameLocation,
                TypeLocation = field.TypeLocation,
                DetailTypeLocation = field.DetailTypeLocation,
                TargetSideLocation = field.TargetSideLocation,

                RawName = $"{field.RawName}.{slot}",
                Name = field.Name + slot,
                NamePath = [.. field.NamePath!, new FieldPathStep { Name = slot, Index = null }],

                TargetSide = field.TargetSide,
                Comment = first ? field.Comment : "",

                // The declaration types both, so the name here is what says which of them
                // still carries the group's own type cell.
                TypeName = first ? field.TypeName : "",
                Type = Models.ValueType.None,

                Index = 0,
            };
        }
    }

    /// <summary>
    /// Splits each cell of pairs into a cell of keys and a cell of values.
    /// </summary>
    private static void RewritePairedRows(
        CookingContext context,
        Table table,
        List<Field> original,
        Dictionary<Field, SchemaField> packed,
        Diagnostics diagnostics)
    {
        foreach (var row in table.Data)
        {
            var rewritten = new List<Cell>(table.Fields.Count);

            foreach (var field in original)
            {
                var cell = row[field.Index];

                if (!packed.ContainsKey(field))
                {
                    rewritten.Add(cell);
                    continue;
                }

                rewritten.AddRange(SplitPairs(context, table, field, cell, diagnostics));
            }

            row.Clear();
            row.AddRange(rewritten);
        }
    }

    /// <summary>
    /// One cell of `k:v` entries, read into a cell of keys and a cell of values.
    /// </summary>
    /// <remarks>
    /// Both halves come out as the text a sheet would have written in the two columns, so
    /// what parses the elements is the same code that parses every other delimited cell.
    /// A key or a value holding the separator writes it `\:`, which is the escape the blank
    /// and null markers already use.
    /// </remarks>
    private static IEnumerable<Cell> SplitPairs(
        CookingContext context, Table table, Field field, Cell cell, Diagnostics diagnostics)
    {
        char delimiter = context.ArrayDelimiter;

        string written = (cell.Value as string ?? "").Trim();

        var keys = new List<string>();
        var values = new List<string>();

        if (cell.HasValue && written.Length > 0)
        {
            foreach (string entry in written.Split(delimiter))
            {
                if (!SplitOnePair(entry, out string key, out string value))
                {
                    diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation, Message.Of(
                        SchemaMessages.MapPairMalformed,
                        ("Table", table.Name), ("Column", field.RawName),
                        ("Written", entry.Trim()), ("Separator", MapPairSeparator)));

                    keys.Clear();
                    values.Clear();
                    break;
                }

                keys.Add(key);
                values.Add(value);
            }
        }

        yield return PartOf(cell, string.Join(delimiter, keys));
        yield return PartOf(cell, string.Join(delimiter, values));
    }

    /// <summary>One entry, split at the first separator that was not written `\:`.</summary>
    private static bool SplitOnePair(string entry, out string key, out string value)
    {
        key = "";
        value = "";

        var left = new System.Text.StringBuilder();
        int at = 0;

        for (; at < entry.Length; at++)
        {
            if (entry[at] == '\\' && at + 1 < entry.Length && entry[at + 1] == MapPairSeparator)
            {
                left.Append(MapPairSeparator);
                at++;
                continue;
            }

            if (entry[at] == MapPairSeparator)
                break;

            left.Append(entry[at]);
        }

        if (at >= entry.Length)
            return false;

        key = left.ToString().Trim();
        value = entry[(at + 1)..]
            .Replace("\\" + MapPairSeparator, MapPairSeparator.ToString())
            .Trim();

        return true;
    }

    /// <summary>
    /// One half of a split cell: the text, reported at the cell it came out of.
    /// </summary>
    private static Cell PartOf(Cell cell, string written)
        => new Cell
        {
            RawCell = cell.RawCell!,

            // The paired cell's own answer. Both halves came from one cell, so "did the
            // sheet write this" has one answer for the two of them - which is the same
            // thing an empty cell says in the two-column notation.
            HasValue = cell.HasValue,
            Value = written,
        };
}
