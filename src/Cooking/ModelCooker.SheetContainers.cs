using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Cooking;

/// <summary>
/// A container written in a sheet's own type cell, rather than in a declaration.
/// </summary>
/// <remarks>
/// **The spelling is read by the notation's own parser and nothing here knows the grammar.**
/// A type cell saying `map&lt;int,int&gt;` is handed to <see cref="SchemaParser.ParseTypeExpression"/>,
/// checked by the same code that checks a declared member, and expanded by the same code that
/// expands a declared one. What this file adds is the route from a cell to those - which is
/// the whole of what section 2.3 deferred, and the reason it was deferred was the fear of a
/// second grammar. There is not one.
///
/// **A sheet with no `.tbs` file at all can use this**, which is what it is for: declaring a
/// struct to hold one map is a level of nesting nobody asked for.
///
/// spec/types/set-and-map.md section 2.3.
/// </remarks>
public partial class ModelCooker
{
    private static void ExpandSheetContainers(
        CookingContext context,
        Model model,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            var written = table.Fields
                .Where(field => CookingContext.IsContainerTypeName(field.TypeName))
                .ToList();

            if (written.Count == 0)
                continue;

            var packed = new Dictionary<Field, SchemaField>();

            foreach (var field in written)
            {
                if (MemberOf(context, table, field, declarations, diagnostics) is { } member)
                    packed.Add(field, member);
            }

            if (packed.Count == 0)
                continue;

            var original = table.Fields.ToList();

            table.Fields = original
                .SelectMany(field => packed.TryGetValue(field, out var member)
                                     && SchemaContainers.KindOf(member.Type) == ContainerKind.Map
                    ? SplitIntoKeyAndValue(field, member)
                    : [field])
                .ToList();

            RewriteSheetContainerRows(context, table, original, packed, diagnostics);

            for (int at = 0; at < table.Fields.Count; at++)
                table.Fields[at].Index = at;

            table.InvalidateDerivedColumns();

            // The types and the marks before the tags, because assigning a tag builds the
            // wire columns and those snapshot both. Built from a column list that had
            // neither, the group would be cached as an ordinary record and the mark that
            // says it is a map would never be seen.
            TypeSheetContainerColumns(context, table, packed, declarations, diagnostics);

            // Ordinal tags were positions in a column list that no longer exists. A table
            // that writes its own tags has been refused in `MemberOf`.
            foreach (var field in table.Fields)
                field.WireTag = null;

            table.InvalidateDerivedColumns();
            context.AssignTags(table);
        }
    }

    /// <summary>
    /// The member a type cell spells, or null when it does not spell one this can carry.
    /// </summary>
    /// <remarks>
    /// **Reported as `Table.Column`.** The checks were written for a declaration and name the
    /// struct and the member, and a table and a column read the same way in every one of those
    /// sentences - so the two are handed in under those names rather than given a parallel set
    /// of messages that would have to be kept in step.
    /// </remarks>
    private static SchemaField? MemberOf(
        CookingContext context,
        Table table,
        Field field,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        var type = SchemaParser.ParseTypeExpression(
            field.TypeName, field.TypeLocation ?? field.NameLocation, diagnostics);

        if (type is null)
            return null;

        var member = new SchemaField
        {
            Name = field.RawName,
            Location = field.TypeLocation ?? field.NameLocation,
            Comment = field.Comment,
            Type = type,
        };

        var owner = new SchemaStruct
        {
            Name = table.Name,
            Location = table.Location,
        };

        if (!SchemaContainers.Check(context, owner, member, declarations, diagnostics))
            return null;

        // **A `set` here has nowhere to publish its lookup.** A `map` becomes two columns and
        // so becomes a record, and a record is a type the generated code can hang one on. A
        // set stays one column and the group it makes is a plain array - so what would come
        // out is the array and nothing to ask with, which is the half surface
        // `SupportsContainers` exists to keep out. The declared notation has the record.
        // spec/types/set-and-map.md section 2.3.
        if (SchemaContainers.KindOf(type) == ContainerKind.Set)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.SetInATypeCell,
                ("Table", table.Name), ("Column", field.RawName),
                ("Type", type.ToString()), ("Element", type.Arguments[0].Name)));

            return null;
        }

        // One column becoming two is one wire tag becoming two, and a table that writes its
        // own has written one. The same refusal a packed struct and a paired map meet.
        if (SchemaContainers.KindOf(type) == ContainerKind.Map && table.HasExplicitTags)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.MapPairsTableWritesTags,
                ("Table", table.Name), ("Column", field.RawName), ("Member", field.RawName)));

            return null;
        }

        // A struct value is several columns under `Value`, and a type cell has no second cell
        // to write them in. The declared notation is where that shape is written.
        if (SchemaContainers.KindOf(type) == ContainerKind.Map
            && declarations.FindStruct(type.Arguments[1].Name) is { } structured)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.MapPairsHoldAStruct,
                ("Table", table.Name), ("Column", field.RawName),
                ("Member", field.RawName), ("Type", structured.Name)));

            return null;
        }

        return member;
    }

    /// <summary>
    /// Splits the cells of every paired map, and leaves every other column as it was.
    /// </summary>
    private static void RewriteSheetContainerRows(
        CookingContext context,
        Table table,
        List<Field> original,
        Dictionary<Field, SchemaField> packed,
        Diagnostics diagnostics)
    {
        bool anyMap = packed.Values.Any(
            member => SchemaContainers.KindOf(member.Type) == ContainerKind.Map);

        if (!anyMap)
            return;

        foreach (var row in table.Data)
        {
            var rewritten = new List<Cell>(table.Fields.Count);

            foreach (var field in original)
            {
                var cell = row[field.Index];

                if (!packed.TryGetValue(field, out var member)
                    || SchemaContainers.KindOf(member.Type) != ContainerKind.Map)
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
    /// Gives every column a container produced the type its argument says, and the mark that
    /// says which container it came from.
    /// </summary>
    private static void TypeSheetContainerColumns(
        CookingContext context,
        Table table,
        Dictionary<Field, SchemaField> packed,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        foreach (var (written, member) in packed)
        {
            var kind = SchemaContainers.KindOf(member.Type);

            if (kind == ContainerKind.Set)
            {
                var column = SchemaContainers.ColumnMemberOfSet(member)!;

                Type(context, table, written, column, declarations, diagnostics);

                // Level zero: the column is the container, and a plain column's path - when
                // it has one at all - is that one level. What reads the mark is the group
                // rather than a member, because a group of one column is what this is.
                written.Container = ContainerKind.Set;
                written.ContainerLevel = (written.NamePath?.Count ?? 1) - 1;

                continue;
            }

            // The two the map became, which the split above left under the paired column's
            // name with `Key` and `Value` appended. The column that was split is gone from
            // the table, so it is matched by the name it had.
            string owner = written.NamePath is { Count: > 0 } wrote
                ? wrote[^1].Name
                : written.Name;

            foreach (var field in table.Fields)
            {
                if (field.NamePath is not { Count: > 1 } path || path[^2].Name != owner)
                    continue;

                if (SchemaContainers.SlotOf(member, path[^1].Name) is not { } slot)
                    continue;

                Type(context, table, field, slot, declarations, diagnostics);

                field.Container = ContainerKind.Map;
                field.ContainerLevel = path.Count - 2;
            }
        }
    }

    private static void Type(
        CookingContext context,
        Table table,
        Field field,
        SchemaField member,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        if (!SchemaFieldTypes.Apply(
                context, table, field, member, declarations,
                waiting: true, diagnostics, out string wanted))
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.MemberTypeUnusable,
                ("Table", table.Name),
                ("Column", field.RawName),
                ("Struct", table.Name),
                ("Member", member.Name),
                ("Declared", wanted),
                ("Written", SchemaFieldTypes.ColumnSpelling(field))));

            return;
        }

        SchemaMetadata.Apply(table, field, member, diagnostics);
    }
}
