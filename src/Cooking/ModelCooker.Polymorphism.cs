using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Cooking;

/// <summary>
/// Giving a column group an abstract type, so that each row of it is one of that type's
/// variants.
/// </summary>
/// <remarks>
/// **The shape is the union of the variants' columns**, and which of them a row may fill is
/// answered by that row's `$type` cell. A member several variants declare is one column, not
/// one per variant - which is what keeps a sheet with sixteen variants readable, and what
/// makes "the same name with two types" a refusal rather than a choice.
/// spec/types/polymorphism.md section 5.2.
///
/// **Nothing new reaches the wire.** The discriminator is a plain integer column carrying each
/// variant's `@N`, base fields are ordinary columns present in every row, and a variant member
/// is an optional column - which v103 already carries. So this pass writes into the model what
/// the exporters already know how to write. spec/types/polymorphism.md section 6.
/// </remarks>
public partial class ModelCooker
{
    /// <summary>
    /// Binds one group against an abstract type: the discriminator, the base fields, and the
    /// union of the variants' members.
    /// </summary>
    private static void BindPolymorphicGroup(
        CookingContext context,
        Table table,
        List<Field> group,
        Field naming,
        SchemaStruct declared,
        SchemaDeclarations declarations,
        HashSet<Field> claimed,
        Diagnostics diagnostics)
    {
        var variants = declarations.VariantsOf(declared.Name).ToList();

        // An abstract type with no variants is refused where it is declared, so reaching here
        // with none would mean that check did not run. Nothing to bind either way.
        if (variants.Count == 0)
            return;

        // The type cell has to be on the `$type` column. An abstract type is not a value - it
        // has no fixed field list - so a member column naming one is naming something that
        // cannot be a member's type, and saying which column it belongs on is the whole of the
        // report.
        if (!naming.IsDiscriminator)
        {
            diagnostics.Error(naming.TypeLocation, Message.Of(
                SchemaMessages.AbstractTypeOnAMemberColumn,
                ("Table", table.Name),
                ("Column", naming.RawName),
                ("Struct", declared.Name),
                ("Group", naming.GroupName ?? "")));

            return;
        }

        // **Every element's discriminator, not only the one that carried the type cell.** A
        // multi-row group's member is one column per element in the model, and the file stores
        // a member as one column with one type - so an element left as text is a member whose
        // elements disagree. spec/types/polymorphism.md section 5.3.
        var discriminators = group
            .Where(field => field.IsDiscriminator)
            .ToList();

        foreach (var field in discriminators)
            BindDiscriminator(context, table, field, declared, variants, declarations);

        // The columns this sheet did not write for members the declaration did, before the
        // members are bound - so that what is bound below is the declaration's member list
        // and not this sheet's subset of it.
        SynthesizeMissingVariantMembers(
            context, table, group, discriminators, declared, variants, claimed, diagnostics);

        foreach (var field in group)
        {
            if (field.IsDiscriminator)
                continue;

            BindVariantMember(
                context, table, field, declared, variants, declarations, diagnostics,
                groupIsArray: group.Any(other => other.NamePath is { Count: > 1 }
                                                 && other.NamePath[0].Index is not null));
        }
    }

    /// <summary>
    /// Adds a column, blank in every row, for each variant member the sheet left out.
    /// </summary>
    /// <remarks>
    /// **A variant member is an optional column, so a sheet may leave it out - and the type
    /// the generators write is one per declaration, not one per sheet.** Two tables naming the
    /// same abstract type used to arrive with different member lists when one of them had no
    /// row using some member and so no column for it. The shared variant type took its
    /// members from whichever table was gathered first, and the other table's flat entry -
    /// which has only its own columns - then had no field for the code that builds the variant
    /// to read. In one order that did not compile; in the other, the member was dropped from
    /// the type and a column the second sheet did fill was unreachable.
    ///
    /// The column added here is exactly the one the author would have written and left blank:
    /// the same name, the same optional presence, the same place at the end of the group. So
    /// the file, the entry and the variant type come out the same whether the sheet wrote it
    /// or not, and the generators need know nothing about it. spec/types/polymorphism.md
    /// section 5.2.
    ///
    /// Two cases are refused rather than filled in. A base field is present in every row by
    /// definition, so a group with no column for one is not that type - the same report a
    /// plain struct group gets. And a table whose sheet wrote its wire tags out has no tag to
    /// give a column nobody wrote, and inventing one would be the very thing explicit tags
    /// exist to rule out.
    ///
    /// Per element of a multi-row group, because each element's members are columns of their
    /// own in the model and the sheet may have left one out for every element at once.
    /// </remarks>
    private static void SynthesizeMissingVariantMembers(
        CookingContext context,
        Table table,
        List<Field> group,
        List<Field> discriminators,
        SchemaStruct declared,
        List<SchemaStruct> variants,
        HashSet<Field> claimed,
        Diagnostics diagnostics)
    {
        var added = new List<(Field After, Field Field)>();

        foreach (var discriminator in discriminators)
        {
            if (discriminator.NamePath is not { Count: > 1 } path)
                continue;

            int? element = path[0].Index;

            var siblings = group
                .Where(field => field.NamePath is { Count: > 1 }
                                && field.NamePath[0].Index == element)
                .ToList();

            var written = siblings
                .Select(field => field.NamePath![^1].Name)
                .ToHashSet(System.StringComparer.Ordinal);

            // A multi-row group's header is one column per member, so what one element lacks
            // every element lacks. Said once, against the first.
            bool reporting = ReferenceEquals(discriminator, discriminators[0]);

            foreach (var member in declared.LiveFields)
            {
                if (written.Contains(member.Name.ToPascalCase()) || !reporting)
                    continue;

                diagnostics.Error(member.Location, Message.Of(
                    SchemaMessages.MemberHasNoColumn,
                    ("Table", table.Name),
                    ("Group", discriminator.GroupName ?? ""),
                    ("Struct", declared.Name),
                    ("Member", member.Name)));
            }

            foreach (var variant in variants)
            {
                foreach (var member in variant.LiveFields)
                {
                    string spelled = member.Name.ToPascalCase();

                    // A member two variants share is one column, so the second declaration
                    // of it finds the first one's column - written or added just now.
                    if (!written.Add(spelled))
                        continue;

                    if (table.HasExplicitTags)
                    {
                        if (reporting)
                        {
                            diagnostics.Error(member.Location, Message.Of(
                                SchemaMessages.MemberHasNoColumn,
                                ("Table", table.Name),
                                ("Group", discriminator.GroupName ?? ""),
                                ("Struct", variant.Name),
                                ("Member", member.Name)));
                        }

                        continue;
                    }

                    var last = siblings[^1];

                    var field = SynthesizeMemberColumn(table, discriminator, last, member.Name);

                    siblings.Add(field);
                    added.Add((last, field));
                }
            }
        }

        if (added.Count == 0)
            return;

        // The cells first, so that every field's index names a cell in every row. One column
        // per added field, appended - the row is as wide as the sheet's columns and no field
        // reads past them.
        int width = table.Fields.Count == 0 ? 0 : table.Fields.Max(field => field.Index) + 1;

        foreach (var rowSet in table.RowSets)
        foreach (var row in rowSet.Rows)
            width = System.Math.Max(width, row.Count);

        for (int at = 0; at < added.Count; at++)
        {
            var field = added[at].Field;
            field.Index = width + at;

            foreach (var rowSet in table.RowSets)
            foreach (var row in rowSet.Rows)
            {
                // A row is where its own cells put it. The `$type` cell of the row is the one
                // a report about this row's group would point at, so the blank borrows its
                // place - not its text, which is a variant's name.
                var where = row.Count > 0
                    ? row[System.Math.Min(row.Count - 1, discriminators[0].Index)].RawCell?.Location
                    : null;

                while (row.Count < field.Index)
                    row.Add(BlankCell(where ?? discriminators[0].NameLocation, present: false));

                row.Add(BlankCell(where ?? discriminators[0].NameLocation, present: true));
            }
        }

        // Then the fields, each after the last column of its element, so a member the sheet
        // left out sits where the author would have put it and the group stays in one piece.
        foreach (var (after, field) in added)
        {
            table.Fields.Insert(table.Fields.IndexOf(after) + 1, field);
            group.Add(field);
            claimed.Add(field);
        }

        // Ordinal tags were positions in a column list that has just changed. Cleared rather
        // than adjusted, because an ordinal tag has no identity to preserve - and a table that
        // wrote its tags out was refused above, so none of these is one the author chose.
        table.InvalidateDerivedColumns();

        foreach (var field in table.Fields)
            field.WireTag = null;

        context.AssignTags(table);
    }

    /// <summary>
    /// The column a sheet would have written for a variant member and left blank.
    /// </summary>
    /// <remarks>
    /// Declared the way a member column whose type cell was left empty is: untyped until the
    /// binding reads its declaration, which is what happens to it next. The header cells it
    /// points at are the discriminator's, because that is the cell an author edits to change
    /// what the group is.
    /// </remarks>
    private static Field SynthesizeMemberColumn(
        Table table, Field discriminator, Field last, string memberName)
    {
        var path = discriminator.NamePath!;
        string spelled = memberName.ToPascalCase();

        var namePath = new List<FieldPathStep>(path.Take(path.Count - 1))
        {
            new FieldPathStep { Name = spelled, Index = null },
        };

        int dot = last.RawName.LastIndexOf('.');
        string prefix = dot < 0 ? last.RawName : last.RawName[..dot];

        return new Field
        {
            OwnerTable = table,
            NameLocation = discriminator.NameLocation,
            TypeLocation = discriminator.TypeLocation,
            DetailTypeLocation = discriminator.DetailTypeLocation,
            TargetSideLocation = discriminator.TargetSideLocation,
            TargetSide = discriminator.TargetSide,

            // The declaration's own spelling, for the reason a packed struct's member keeps
            // it: the naming rules judge what somebody wrote, and this name was written in
            // the `.tbs` file.
            RawName = $"{prefix}.{memberName}",
            Name = string.Concat(namePath.Select(
                step => step.Name
                        + (step.Index?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                           ?? ""))),
            NamePath = namePath,

            Comment = "",
            TypeName = "",
            Type = CookingContext.DeferredType,
            IsRequired = true,
            ElementsRequired = true,
            WireTag = null,
            Index = 0,

            // Nobody wrote this column, so the spelling rules have nothing to judge and a
            // report about a blank in it has to say the column is missing rather than point
            // at a cell. See `Field.Synthesized`.
            Synthesized = true,
        };
    }

    /// <summary>A cell the sheet did not write, in a column it did not have.</summary>
    /// <remarks>
    /// Present and empty, exactly as a blank cell under a written header arrives: the binding
    /// that reads the column as its declared type is what turns an empty string in an optional
    /// column into absence, and it has to see the same thing here that it sees there.
    /// </remarks>
    private static Cell BlankCell(Location where, bool present)
        => new Cell
        {
            RawCell = new Models.Raw.RawCell { Location = where, Value = "" },
            Value = "",
            HasValue = present,
        };

    /// <summary>
    /// The `$type` column: an integer carrying each row's variant number.
    /// </summary>
    /// <remarks>
    /// Int32 rather than a generated enum. The cell holds a variant name and the file holds
    /// that variant's number, which is the relation an enum column has to its labels - but the
    /// variant is already a declared type, so a second name for it would be a third spelling
    /// of one thing. spec/types/polymorphism.md section 7.1.
    /// </remarks>
    private static void BindDiscriminator(
        CookingContext context,
        Table table,
        Field field,
        SchemaStruct declared,
        List<SchemaStruct> variants,
        SchemaDeclarations declarations)
    {
        field.AbstractTypeName = declared.Name;
        field.AbstractTypeComment = declared.Comment;

        field.Variants = variants
            .Select(variant => new PolymorphicVariant
            {
                Name = variant.Name.ToPascalCase(),
                Comment = variant.Comment,
                Discriminator = declarations.DiscriminatorOf(variant),
            })
            .ToList();

        field.Type = Models.ValueType.Int32;
        field.TypeName = "int";
        field.IsRequired = true;

        // The cells still hold variant names. Converting them needs the whole list, which is
        // what this method just wrote, so it runs as a pass of its own once every group is
        // bound - see ConvertDiscriminatorCells.
        _ = context;
        _ = table;
    }

    /// <summary>
    /// One member column of a polymorphic group, against the abstract type and every variant.
    /// </summary>
    /// <remarks>
    /// The abstract type's own fields first, because those are the base fields: present in
    /// every row, so a plain column. Then the variants, and a member that several declare has
    /// to have one type among them - the column is shared, and two types in one column is a
    /// column that cannot be read. spec/types/polymorphism.md section 5.2.
    /// </remarks>
    private static void BindVariantMember(
        CookingContext context,
        Table table,
        Field field,
        SchemaStruct declared,
        List<SchemaStruct> variants,
        SchemaDeclarations declarations,
        Diagnostics diagnostics,
        bool groupIsArray = false)
    {
        if (field.NamePath is not { Count: > 1 })
            return;

        string wanted = field.NamePath[^1].Name;

        var baseMember = declared.LiveFields.FirstOrDefault(
            member => member.Name.ToPascalCase() == wanted);

        if (baseMember is not null)
        {
            // A base field. Every variant carries it, which is the reason section 5.1 puts the
            // base fields in one column instead of copying them into every variant - so the
            // column is required.
            //
            // **Except where the group is an array.** There a row with fewer elements has no
            // cell for the ones it does not have, which is what trimming means: the tail is
            // elements the row lacks rather than blanks its author left. The same reason the
            // trimmed-record fixture types its members optional. spec/types/polymorphism.md 5.3.
            ApplyMemberType(
                context, table, field, baseMember, declared, declarations, diagnostics,
                columnIsOptional: groupIsArray);

            field.VariantsDeclaringThis.Clear();

            return;
        }

        var declaring = variants
            .Select(variant => (Variant: variant,
                                Member: variant.LiveFields.FirstOrDefault(
                                    member => member.Name.ToPascalCase() == wanted)))
            .Where(found => found.Member is not null)
            .ToList();

        if (declaring.Count == 0)
        {
            diagnostics.Error(field.NameLocation, Message.Of(
                SchemaMessages.MemberNoVariantDeclares,
                ("Table", table.Name),
                ("Column", FieldPath.Describe(field.NamePath)),
                ("Struct", declared.Name),
                ("Variants", string.Join("`, `", variants.Select(variant => variant.Name)))));

            return;
        }

        // Two variants spelling one member differently typed. Reported against the second,
        // with both types named: the column is one column and the sheet cannot hold both.
        var first = declaring[0];

        foreach (var other in declaring.Skip(1))
        {
            if (first.Member!.Type.ToString() == other.Member!.Type.ToString())
                continue;

            diagnostics.Error(field.NameLocation, Message.Of(
                SchemaMessages.MemberTypeVariesByVariant,
                ("Table", table.Name),
                ("Column", FieldPath.Describe(field.NamePath)),
                ("Struct", declared.Name),
                ("First", first.Variant.Name),
                ("FirstType", first.Member.Type.ToString()),
                ("Second", other.Variant.Name),
                ("SecondType", other.Member.Type.ToString())));

            return;
        }

        // A row of another variant leaves this blank, so the column carries a presence bit.
        // The generated variant type still declares the member as the declaration wrote it -
        // whether it is there is answered by the variant, not by a `Has` accessor.
        // spec/types/polymorphism.md section 7.
        ApplyMemberType(
            context, table, field, first.Member!, declared, declarations, diagnostics,
            columnIsOptional: true);

        field.VariantsDeclaringThis = declaring
            .Select(found => found.Variant.Name.ToPascalCase())
            .ToList();
    }

    /// <summary>
    /// Hands a column the type its declaration gives it, reporting where the sheet disagrees.
    /// </summary>
    /// <remarks>
    /// The same call the ordinary group binding makes, so a polymorphic group's members are
    /// typed by exactly the rules a plain one's are - including a sheet that wrote its own
    /// type cell being checked rather than overwritten.
    /// </remarks>
    private static void ApplyMemberType(
        CookingContext context,
        Table table,
        Field field,
        SchemaField member,
        SchemaStruct declared,
        SchemaDeclarations declarations,
        Diagnostics diagnostics,
        bool columnIsOptional = false)
    {
        bool waiting = context.IsDeferredTypeName(field.TypeName);

        if (!SchemaFieldTypes.Apply(
                context, table, field, member, declarations, waiting, diagnostics,
                out string wanted, columnIsOptional))
        {
            diagnostics.Error(waiting ? member.Type.Location : field.TypeLocation, Message.Of(
                waiting ? SchemaMessages.MemberTypeUnusable : SchemaMessages.ColumnTypeDisagrees,
                ("Table", table.Name),
                ("Column", FieldPath.Describe(field.NamePath!)),
                ("Struct", declared.Name),
                ("Member", member.Name),
                ("Declared", wanted),
                ("Written", SchemaFieldTypes.ColumnSpelling(field))));

            return;
        }

        SchemaMetadata.Apply(table, field, member, diagnostics);

        if (string.IsNullOrEmpty(field.Comment))
            field.Comment = member.Comment;
    }

    /// <summary>
    /// Turns each `$type` cell from the variant's name into the number the file carries.
    /// </summary>
    /// <remarks>
    /// **Here rather than while the sheet was open, for the reason a reference cell waits.**
    /// Which variants exist is not a fact any one sheet carries - the declarations hold it, and
    /// a variant may be declared in a file this sheet never mentions. So the layout keeps the
    /// cell as written and this settles it once the binding has put the list on the column.
    /// spec/types/polymorphism.md section 5.2.
    /// </remarks>
    private static void ConvertDiscriminatorCells(Model model, Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            foreach (var field in table.Fields.Where(field => field.IsDiscriminator))
            {
                if (field.Variants.Count == 0)
                    continue;

                var byName = field.Variants.ToDictionary(
                    variant => variant.Name, variant => variant.Discriminator,
                    System.StringComparer.OrdinalIgnoreCase);

                string names = string.Join("`, `", field.Variants.Select(variant => variant.Name));

                // Every set of rows the table has, for the reason the reference conversion
                // walks them all: a cell left as text matches no variant afterwards.
                foreach (var rowSet in table.RowSets)
                foreach (var row in rowSet.Rows)
                {
                    if (field.Index >= row.Count)
                        continue;

                    // **Past this row's last element there is nothing to say.** A trimmed array
                    // ends where the row's values end, and the columns after that are elements
                    // the row does not have - not elements whose kind was left out. The same
                    // skip the reference check makes one level over.
                    // spec/types/polymorphism.md section 5.3.
                    var serial = table.SerialFields.FirstOrDefault(
                        candidate => candidate.Name == field.GroupName);

                    if (serial is not null
                        && field.NamePath is { Count: > 0 } path
                        && path[0].Index is { } at
                        && at >= table.ElementCountIn(serial, row))
                    {
                        continue;
                    }

                    var cell = row[field.Index];

                    if (cell.Value is not string written || written.Length == 0)
                    {
                        // A blank one is a row that did not say what it is. Nothing else in
                        // the group can be read then - which member columns apply is exactly
                        // what this cell answers. spec/types/polymorphism.md section 8.
                        if (!cell.HasValue || cell.Value is string)
                        {
                            diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                                Message.Of(SchemaMessages.DiscriminatorCellBlank,
                                    ("Table", table.Name), ("Column", field.RawName),
                                    ("Struct", field.AbstractTypeName ?? ""), ("Variants", names)));
                        }

                        continue;
                    }

                    if (!byName.TryGetValue(written.Trim(), out int discriminator))
                    {
                        diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                            Message.Of(SchemaMessages.DiscriminatorCellUnknown,
                                ("Table", table.Name), ("Column", field.RawName),
                                ("Written", written), ("Struct", field.AbstractTypeName ?? ""),
                                ("Variants", names)));

                        continue;
                    }

                    cell.Value = discriminator;
                }
            }
        }
    }

    /// <summary>
    /// Reports a value written in a column the row's own variant does not declare.
    /// </summary>
    /// <remarks>
    /// **The refusal the union notation needs most.** The columns are every variant's members
    /// side by side, so a row always has blank cells that are not its own - and a value put in
    /// one of them looks like ordinary data. Nothing would ever read it: the generated variant
    /// type has no such member. spec/types/polymorphism.md section 8.
    /// </remarks>
    private static void RefuseValuesOutsideTheRowsVariant(Model model, Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            var discriminators = table.Fields
                .Where(field => field.IsDiscriminator && field.Variants.Count > 0)
                .ToList();

            if (discriminators.Count == 0)
                continue;

            foreach (var discriminator in discriminators)
            {
                string group = discriminator.GroupName ?? "";

                // **The same element, not the whole group.** A multi-row group has one
                // discriminator per element, and the members it answers for are the ones at
                // that element. spec/types/polymorphism.md section 5.3.
                int? element = discriminator.NamePath is { Count: > 0 }
                    ? discriminator.NamePath[0].Index
                    : null;

                // The member columns of this group, and which variants each belongs to. A base
                // field carries an empty list and is never reported - every row has it.
                var members = table.Fields
                    .Where(field => !field.IsDiscriminator
                                    && field.GroupName == group
                                    && field.VariantsDeclaringThis.Count > 0
                                    && (field.NamePath is { Count: > 0 }
                                        ? field.NamePath[0].Index
                                        : null) == element)
                    .ToList();

                if (members.Count == 0)
                    continue;

                var nameOf = discriminator.Variants.ToDictionary(
                    variant => variant.Discriminator, variant => variant.Name);

                foreach (var rowSet in table.RowSets)
                foreach (var row in rowSet.Rows)
                {
                    if (discriminator.Index >= row.Count
                        || row[discriminator.Index].Value is not int written
                        || !nameOf.TryGetValue(written, out string? variant))
                    {
                        continue;
                    }

                    foreach (var member in members)
                    {
                        if (member.Index >= row.Count)
                            continue;

                        var cell = row[member.Index];

                        if (!cell.HasValue)
                            continue;

                        if (member.VariantsDeclaringThis.Contains(
                                variant, System.StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        diagnostics.Error(cell.RawCell?.Location ?? member.NameLocation,
                            Message.Of(SchemaMessages.ValueOutsideTheRowsVariant,
                                ("Table", table.Name),
                                ("Column", FieldPath.Describe(member.NamePath!)),
                                ("Variant", variant),
                                ("Declared", string.Join(
                                    "`, `", member.VariantsDeclaringThis))));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Puts the rows of a table that has a polymorphic group in discriminator order.
    /// </summary>
    /// <remarks>
    /// **So that a sheet's own order stops deciding the file's size.** A variant member column
    /// carries the type's empty value on every row of another variant, and the run-length
    /// encodings pay for those runs being broken up - measured at 1.9x on the same data
    /// shuffled. Sorting gathers the runs and the sheet can be written in any order.
    ///
    /// **In the cooking rather than in an exporter**, because the JSON export and the binary
    /// one have to agree row for row: the conformance gate reads one and compares it against
    /// the other. One model, one order.
    ///
    /// Stable, so nothing moves inside a variant - the author's order is kept there, which is
    /// all a run needs. Per row set, because each set is its own file with its own rows.
    /// spec/types/polymorphism.md section 6.3.
    /// </remarks>
    private static void SortRowsByDiscriminator(Model model)
    {
        foreach (var table in model.Tables)
        {
            // **A scalar group only.** Section 6.3 gathers runs by putting each row's variant
            // together, and an array group has no single variant to sort by - its elements each
            // have their own, the count differs per row, and ordering by the tuple of them
            // reorders rows for no measured gain while losing the author's order. So a
            // polymorphic array is left where the sheet put it. spec/types/polymorphism.md 5.3.
            var discriminators = table.Fields
                .Where(field => field.IsDiscriminator
                                && field.Variants.Count > 0
                                && (field.NamePath is not { Count: > 0 }
                                    || field.NamePath[0].Index is null))
                .OrderBy(field => field.Index)
                .ToList();

            if (discriminators.Count == 0)
                continue;

            foreach (var rowSet in table.RowSets)
            {
                var sorted = rowSet.Rows.OrderBy(
                    row => Key(row, discriminators), DiscriminatorKeys.Instance);

                var ordered = sorted.ToList();

                rowSet.Rows.Clear();
                rowSet.Rows.AddRange(ordered);
            }
        }
    }

    /// <summary>The discriminator values of one row, in column order.</summary>
    private static int[] Key(List<Cell> row, List<Field> discriminators)
        => discriminators
            .Select(field => field.Index < row.Count && row[field.Index].Value is int written
                ? written
                : 0)
            .ToArray();

    /// <summary>Compares two rows' discriminator values, first one that differs.</summary>
    private sealed class DiscriminatorKeys : IComparer<int[]>
    {
        public static readonly DiscriminatorKeys Instance = new DiscriminatorKeys();

        public int Compare(int[]? left, int[]? right)
        {
            if (left is null || right is null)
                return 0;

            for (int at = 0; at < left.Length && at < right.Length; at++)
            {
                int answer = left[at].CompareTo(right[at]);

                if (answer != 0)
                    return answer;
            }

            return 0;
        }
    }

    /// <summary>
    /// Gathers the abstract types the sheets used, one entry per declaration.
    /// </summary>
    /// <remarks>
    /// **First group wins, and any group would do.** The declaration fixes what the members
    /// are, and the binding makes every group carry all of them: a column whose type disagrees
    /// is refused, a base field with no column is refused, and a variant member with no column
    /// is added blank - `SynthesizeMissingVariantMembers`. So two tables using one abstract
    /// type give the same answer, and taking the columns from one of them is what lets every
    /// generator write the type with the machinery it already has. Without that last rule the
    /// answer depended on which table came first, and a table whose sheet had no row using
    /// some member got a variant builder reading a field its entry did not have.
    ///
    /// Nothing is gathered for an abstract type no sheet used. A declaration on its own is not
    /// a type in the output, the same way an enum nobody typed a column with is not.
    /// spec/types/polymorphism.md section 7.1.
    /// </remarks>
    private static void GatherPolymorphicTypes(Model model)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var table in model.Tables)
        {
            foreach (var discriminator in table.Fields.Where(field => field.IsDiscriminator))
            {
                string? name = discriminator.AbstractTypeName;

                if (name is null || discriminator.Variants.Count == 0 || !seen.Add(name))
                    continue;

                string group = discriminator.GroupName ?? "";

                var members = table.Fields
                    .Where(field => !field.IsDiscriminator && field.GroupName == group)
                    .ToList();

                model.PolymorphicTypes.Add(new PolymorphicType
                {
                    Name = name.ToPascalCase(),
                    Comment = discriminator.AbstractTypeComment,
                    BaseMembers = members
                        .Where(field => field.VariantsDeclaringThis.Count == 0)
                        .ToList(),
                    Variants = discriminator.Variants
                        .Select(variant => new PolymorphicTypeVariant
                        {
                            Name = variant.Name,
                            Comment = variant.Comment,
                            Discriminator = variant.Discriminator,
                            Members = members
                                .Where(field => field.VariantsDeclaringThis.Contains(
                                    variant.Name, System.StringComparer.OrdinalIgnoreCase))
                                .ToList(),
                        })
                        .ToList(),
                });
            }
        }
    }
}
