using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Cooking;

/// <summary>
/// A whole value written into one cell, and folded back into the columns it is made of.
/// </summary>
/// <remarks>
/// **Not a new mechanism.** The composite value types already read a cell as several values
/// and fold the result into a record - `ExpandCompositeColumns` - and this is that same move
/// with the shape coming from a declaration rather than from the seven built-in ones. What
/// the two produce is identical: one column per component, which is what a record has always
/// been on the wire. Section 7.3 of the design.
///
/// So a table written `Reward1` with `101,2,icon_a` in the cell and a table written
/// `Reward1.ItemId` / `Reward1.Count` / `Reward1.Icon` reach the same file. That is the gate,
/// and it is the same gate the composite revision is held to.
///
/// **What one cell cannot hold is refused rather than half-read.** A member that is itself a
/// record or an array has no place in a positional cell; a column that identifies the row
/// cannot be several values; a table writing its own wire tags has one tag where this needs
/// one per member. Each of those is reported where it is written.
/// </remarks>
public partial class ModelCooker
{
    /// <summary>
    /// Expands every column whose type is a struct that writes itself into one cell.
    /// </summary>
    private static void ExpandSepColumns(
        CookingContext context,
        Model model,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            var packed = table.Fields
                .Where(field => SepStructOf(field, declarations) is not null)
                .ToList();

            if (packed.Count == 0)
                continue;

            foreach (var field in packed)
                RefuseWhatOneCellCannotCarry(table, field, declarations, diagnostics);

            var original = table.Fields.ToList();

            table.Fields = original
                .SelectMany(field => ExpandOne(context, table, field, declarations))
                .ToList();

            // Rows first and renumbering second. A column that was not packed is the **same
            // object** in both lists, so renumbering would move the very index the rewrite
            // reads its cells by - the mistake `ExpandCompositeColumns` writes down.
            RewritePackedRows(context, table, original, declarations, diagnostics);

            for (int at = 0; at < table.Fields.Count; at++)
                table.Fields[at].Index = at;

            // The wire columns snapshot a field's type and tag assignment has already built
            // them, so nothing here is visible until they are rebuilt.
            table.InvalidateDerivedColumns();

            // Ordinal tags were positions in a column list that no longer exists. A table
            // that wrote its tags out has been refused above.
            foreach (var field in table.Fields)
                field.WireTag = null;

            context.AssignTags(table);
        }
    }

    /// <summary>
    /// The struct a column packs into one cell, or null when it packs nothing.
    /// </summary>
    /// <remarks>
    /// A column inside a record group is never one of these. Its members are already columns
    /// of their own - that is notation (나) - and a group whose type cell names a struct has
    /// been bound before this runs.
    /// </remarks>
    private static SchemaStruct? SepStructOf(Field field, SchemaDeclarations declarations)
    {
        if (field.IsRecordMember)
            return null;

        var declared = declarations.FindStruct(field.TypeName);

        return declared?.Meta.Value("sep") is { Length: > 0 } ? declared : null;
    }

    private static char SeparatorOf(SchemaStruct declared)
        => declared.Meta.Value("sep")![0];

    private static void RefuseWhatOneCellCannotCarry(
        Table table, Field field, SchemaDeclarations declarations, Diagnostics diagnostics)
    {
        var declared = SepStructOf(field, declarations)!;

        if (field.Indexing)
        {
            diagnostics.Error(field.NameLocation, Message.Of(
                SchemaMessages.SepColumnIsAnIndex,
                ("Table", table.Name), ("Column", field.Name), ("Struct", declared.Name)));
        }

        if (table.HasExplicitTags)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.SepTableWritesTags,
                ("Table", table.Name), ("Column", field.Name), ("Struct", declared.Name)));
        }

        if (!field.Constraints.IsEmpty)
        {
            diagnostics.Error(field.NameLocation, Message.Of(
                SchemaMessages.SepColumnHasConstraints,
                ("Table", table.Name), ("Column", field.Name), ("Struct", declared.Name)));
        }
    }

    /// <summary>
    /// One field, or one field per member when the column packs a struct.
    /// </summary>
    /// <remarks>
    /// The member becomes one more level of the column's name path, which is what makes the
    /// result identical to a hand-written `Reward1.ItemId`. Whatever nesting the column
    /// already had composes with it, exactly as it does for a composite.
    /// </remarks>
    private static IEnumerable<Field> ExpandOne(
        CookingContext context, Table table, Field field, SchemaDeclarations declarations)
    {
        var declared = SepStructOf(field, declarations);

        if (declared is null)
        {
            yield return field;
            yield break;
        }

        var basePath = field.NamePath is null
            ? [new FieldPathStep { Name = field.Name, Index = null }]
            : new List<FieldPathStep>(field.NamePath);

        foreach (var member in declared.LiveFields)
        {
            if (!SchemaFieldTypes.Resolve(context, member.Type, declarations, out var resolved))
                continue;

            string spelled = member.Name.ToPascalCase();

            yield return new Field
            {
                OwnerTable = table,

                // All four of the header cells the column was declared in. A report about a
                // member points at the cell the author can edit, which is the packed
                // column's own header - there is no cell of its own to point at.
                NameLocation = field.NameLocation,
                TypeLocation = field.TypeLocation,
                DetailTypeLocation = field.DetailTypeLocation,
                TargetSideLocation = field.TargetSideLocation,

                // **The declaration's own spelling, not the cased one.** A member of a
                // declared struct is a name somebody wrote, so the naming rules judge it -
                // and they have to judge what was written. Pascal-casing it here made a
                // `.tbs` declaring `currency_id` arrive as `CurrencyId`, which a recipe
                // asking for `snake` then reported. The same struct written out as member
                // columns passed, because there the sheet's own text is what arrives.
                RawName = $"{field.RawName}.{member.Name}",
                Name = field.Name + spelled,
                NamePath = [.. basePath, new FieldPathStep { Name = spelled, Index = null }],

                TargetSide = field.TargetSide,

                // The member's own optionality, not the column's. A packed cell has one
                // answer to "did the sheet write anything here" and the members inside it
                // are declared each for themselves.
                IsRequired = !member.Type.IsOptional,
                ElementsRequired = !member.Type.ElementsAreOptional,

                Comment = member.Comment,

                Type = resolved.Type,
                TypeName = resolved.TypeName,
                RefTableName = resolved.RefTables?[0],

                Index = 0,
            };
        }
    }

    /// <summary>
    /// Splits each packed cell across the columns that now hold its members.
    /// </summary>
    private static void RewritePackedRows(
        CookingContext context,
        Table table,
        List<Field> original,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        // Once per packed column rather than once per cell. The literal is the same string
        // in every row, so parsing it is the same answer - and a literal its member's type
        // cannot read would otherwise be reported once for every row of the table.
        var defaults = new Dictionary<Field, List<object?>>();

        foreach (var field in original)
        {
            if (SepStructOf(field, declarations) is not { } declared)
                continue;

            defaults[field] = declared.LiveFields
                .Select(member => SchemaFieldTypes.ResolvedOf(context, member, declarations) is { } r
                    ? SchemaDefaults.Read(context, member, r, diagnostics)
                    : null)
                .ToList();
        }

        foreach (var row in table.Data)
        {
            var rewritten = new List<Cell>(table.Fields.Count);

            foreach (var field in original)
            {
                var declared = SepStructOf(field, declarations);

                var cell = row[field.Index];

                if (declared is null)
                {
                    rewritten.Add(cell);
                    continue;
                }

                rewritten.AddRange(Split(
                    context, table, field, declared, declarations,
                    defaults[field], cell, diagnostics));
            }

            row.Clear();
            row.AddRange(rewritten);
        }
    }

    /// <summary>
    /// One packed cell, read into one value per member.
    /// </summary>
    /// <remarks>
    /// **The count has to match exactly.** The cell is positional, so a component short means
    /// every member after it is read as the wrong one - and reading a short cell as "the rest
    /// are empty" would make a typo indistinguishable from a decision.
    ///
    /// Surrounding brackets are optional, which is the notation the composite value types
    /// already accept: `(1,2,3)` and `1,2,3` are the same three values.
    /// </remarks>
    private static IEnumerable<Cell> Split(
        CookingContext context,
        Table table,
        Field field,
        SchemaStruct declared,
        SchemaDeclarations declarations,
        List<object?> defaults,
        Cell cell,
        Diagnostics diagnostics)
    {
        var members = declared.LiveFields.ToList();

        string written = (cell.Value as string ?? "").Trim();

        if (written.StartsWith('(') && written.EndsWith(')'))
            written = written[1..^1];

        var components = cell.HasValue && written.Length > 0
            ? written.Split(SeparatorOf(declared)).Select(part => part.Trim()).ToList()
            : [];

        // A blank cell is every member's own empty value, which is the one answer the
        // notation can give: the components came from one cell, so "did the sheet write
        // this" has one answer for all of them.
        if (components.Count > 0 && components.Count != members.Count)
        {
            diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation, Message.Of(
                SchemaMessages.SepComponentCount,
                ("Table", table.Name),
                ("Column", field.Name),
                ("Struct", declared.Name),
                ("Given", components.Count),
                ("Wanted", members.Count),
                ("Written", written)));

            components = [];
        }

        for (int at = 0; at < members.Count; at++)
        {
            var member = members[at];

            if (!SchemaFieldTypes.Resolve(context, member.Type, declarations, out var resolved))
                continue;

            string? part = at < components.Count ? components[at] : null;

            yield return new Cell
            {
                // The packed cell's own, so every member reports at the cell somebody can
                // edit - there is no cell of its own to point at.
                RawCell = cell.RawCell!,

                // Per member, and **this is where a declared struct parts company with a
                // composite value type.** A composite's members are never optional, so one
                // answer for the whole cell was the only one it could give. A declared
                // member may be written `string?`, and then an empty component is that
                // member saying it has none - which is exactly what the same table written
                // as columns says with an empty cell. Giving them all the cell's own answer
                // would make the two notations produce different files, and section 7.1
                // requires that they produce the same one.
                HasValue = (cell.HasValue && !string.IsNullOrEmpty(part))
                    || (member.DefaultValue is not null && defaults[at] is not null),

                Value = Read(context, resolved, member, defaults[at], part, cell)!,
            };
        }
    }

    private static object Read(
        CookingContext context,
        SchemaFieldTypes.Resolved resolved,
        SchemaField member,
        object? declared,
        string? part,
        Cell cell)
    {
        // Nothing written where a member declares what nothing means. The same answer the
        // written-out notation gives an empty cell, which is what keeps the two identical.
        if (string.IsNullOrEmpty(part) && member.DefaultValue is not null)
            return declared ?? (part ?? "");

        // A reference keeps its text until the pass that resolves it: which type its key is
        // depends on the target, which may not have been read yet.
        if (resolved.RefTables is not null)
            return part ?? "";

        var declaredEnum = resolved.Type is Models.ValueType.Enum or Models.ValueType.EnumArray
            ? context.Model.FindEnum(resolved.TypeName)
            : null;

        return context.ParseValue(
            resolved.Type, declaredEnum, part ?? "", cell.RawCell?.Location,
            required: part is not null && !member.Type.IsOptional)!;
    }
}
