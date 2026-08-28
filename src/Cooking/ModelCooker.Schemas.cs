using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Cooking;

/// <summary>
/// Giving a column group the type a sheet named for it.
/// </summary>
/// <remarks>
/// **The notation this carries is (나) of section 7.2 of the design.** The columns are exactly
/// what they were - `Reward1.ItemId`, `Reward1.Count` - and what goes away is the repetition:
/// the group's first column names the struct in its type cell, the rest leave their type and
/// description cells empty, and the declaration says what they are. Nine columns still, and
/// the same nine on the wire.
///
/// **A pass rather than a change to the layouts, for the reason the composite expansion is
/// one.** A layout reads a sheet's notation; which struct covers a group is a question about
/// the whole group and about files no layout has read. So the reading keeps such a column as
/// text - <see cref="CookingContext.DeferredType"/> - and this settles it once every column
/// is in, which is also what a reference already does.
///
/// **Depth is not counted.** A path of `Star1.Position.X` walks `Position` in the group's
/// struct, finds a struct, and walks `X` in that one. Nothing here asks how deep it went.
/// </remarks>
public partial class ModelCooker
{
    /// <summary>
    /// Types every column whose group named a struct, and reports what is left untyped.
    /// </summary>
    private static void BindDeclaredStructs(
        CookingContext context, Model model, SchemaDeclarations declarations, Diagnostics diagnostics)
    {
        if (declarations.IsEmpty)
            return;

        // Which columns a group claimed, whether or not each one bound. A column already
        // reported against its declaration must not be reported a second time by the check
        // below for still having no type - that is the same mistake said twice, with the
        // second saying of it pointing away from the cause.
        var claimed = new HashSet<Field>();

        // A map written as pairs in one cell becomes the two columns it would have been
        // written as, before anything below has to know there were ever two notations.
        // spec/types/set-and-map.md section 5.2.
        //
        // A column it refused is left as it was written and named here, so the binding does
        // not report the consequence of a mistake it has already reported the cause of.
        var refused = new HashSet<Field>();

        ExpandMapCells(context, model, declarations, refused, diagnostics);

        foreach (var table in model.Tables)
        {
            foreach (var group in GroupsOf(table))
                BindGroup(context, table, group, declarations, claimed, refused, diagnostics);
        }

        // A whole value written into one cell, folded back into the columns it is made of.
        // After the groups, because a column inside one is already a member's own column and
        // there is nothing to unpack. notes/struct-dsl-design.md section 7.3.
        ExpandSepColumns(context, model, declarations, diagnostics);

        RefuseColumnsNobodyTyped(context, model, claimed, diagnostics);
    }

    /// <summary>
    /// The record groups of a table: columns that sit inside one, gathered by its name.
    /// </summary>
    /// <remarks>
    /// A column that is one element of a plain array is not a group here. Its path has one
    /// level, so there is no member name for a declaration to answer about - the struct
    /// notation is about a record, and a record is what two or more levels are.
    /// </remarks>
    private static IEnumerable<List<Field>> GroupsOf(Table table)
        => table.Fields
            .Where(field => field.IsRecordMember)
            .GroupBy(field => field.GroupName!)
            .Select(group => group.ToList());

    /// <summary>
    /// The columns of a group that named a struct in their type cell.
    /// </summary>
    /// <remarks>
    /// **One cell, not one field per element.** A multi-row group's header is one column and
    /// every element's field carries its type cell, so counting fields made a group of two
    /// elements look like one naming its struct twice. The location is what says which cell
    /// an author actually wrote. spec/types/polymorphism.md section 5.3.
    /// </remarks>
    private static List<Field> NamingColumnsOf(List<Field> group, SchemaDeclarations declarations)
        => group
            .Where(field => declarations.FindStruct(field.TypeName) is not null)
            .GroupBy(field => field.TypeLocation?.ToString() ?? field.RawName)
            .Select(byCell => byCell.First())
            .ToList();

    private static void BindGroup(
        CookingContext context,
        Table table,
        List<Field> group,
        SchemaDeclarations declarations,
        HashSet<Field> claimed,
        HashSet<Field> refused,
        Diagnostics diagnostics)
    {
        // Which column named the struct. Exactly one, and the report says so either way: a
        // group naming none is the ordinary notation and nothing here applies to it, and a
        // group naming two has two answers to what it is.
        var naming = NamingColumnsOf(group, declarations);

        if (naming.Count == 0)
            return;

        if (naming.Count > 1)
        {
            diagnostics.Error(naming[1].TypeLocation, Message.Of(
                SchemaMessages.GroupTypedTwice,
                ("Table", table.Name),
                ("Group", naming[0].GroupName ?? ""),
                ("First", naming[0].TypeName),
                ("Second", naming[1].TypeName)));

            return;
        }

        var declared = declarations.FindStruct(naming[0].TypeName)!;

        foreach (var field in group)
            claimed.Add(field);

        // An abstract type says the group's rows are not all one shape, and then the columns
        // are the union of its variants' rather than one struct's members. A different pass,
        // because every question below - which member, is it required, is a missing column a
        // problem - has a different answer there. spec/types/polymorphism.md section 5.2.
        if (declared.IsAbstract)
        {
            BindPolymorphicGroup(
                context, table, group, naming[0], declared, declarations, diagnostics);

            return;
        }

        foreach (var field in group)
        {
            // Already reported where it was written. A second report about it would sit
            // between the reader and the cause of the first.
            if (refused.Contains(field))
                continue;

            BindColumn(context, table, field, declared, declarations, diagnostics);
        }

        RefuseMembersWithNoColumn(table, group, declared, diagnostics);
    }

    /// <summary>
    /// Walks a column's path through the declarations and gives it what it finds.
    /// </summary>
    private static void BindColumn(
        CookingContext context,
        Table table,
        Field field,
        SchemaStruct declared,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        var member = Walk(field, declared, declarations, out var container, out int containerLevel);

        // Carried on every column at or below the container, with the level saying which of
        // them the container itself is. A map's `Key` and `Value` are one level further in
        // and answer for the container rather than being one.
        field.Container = container;
        field.ContainerLevel = containerLevel;

        if (member is null)
        {
            diagnostics.Error(field.NameLocation, Message.Of(
                SchemaMessages.ColumnNotAMember,
                ("Table", table.Name),
                ("Column", FieldPath.Describe(field.NamePath!)),
                ("Struct", declared.Name)));

            return;
        }

        // What the sheet already resolved for itself. A column that wrote its own type is
        // checked against the declaration rather than overwritten - a sheet part-way through
        // moving to this notation still converts, and one that disagrees is told which of the
        // two it disagrees with. Section 7.2 of the design.
        bool waiting = context.IsDeferredTypeName(field.TypeName);

        if (!SchemaFieldTypes.Apply(
                context, table, field, member, declarations, waiting, diagnostics,
                out string wanted))
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

        // Two shapes a default cannot survive, reported where the column is.
        //
        // An index, because every row leaving it blank would take that same key. And a
        // written-out type cell, because the layout reads such a column while the sheet is
        // being parsed - which settles what the blank means before the declaration is ever
        // consulted. notes/struct-dsl-design.md section 11 stage 4.
        if (member.DefaultValue is not null)
        {
            if (field.Indexing)
            {
                diagnostics.Error(field.NameLocation, Message.Of(
                    SchemaMessages.DefaultOnAnIndex,
                    ("Table", table.Name),
                    ("Column", field.RawName),
                    ("Struct", declared.Name),
                    ("Member", member.Name),
                    ("Written", member.DefaultValue)));
            }
            else if (!waiting)
            {
                diagnostics.Error(field.TypeLocation, Message.Of(
                    SchemaMessages.DefaultNeedsAnEmptyTypeCell,
                    ("Table", table.Name),
                    ("Column", field.RawName),
                    ("Struct", declared.Name),
                    ("Member", member.Name),
                    ("Written", field.TypeName)));
            }
        }

        // What the brackets said, narrowed against whatever the sheet's own rows said about
        // this one column. notes/struct-dsl-design.md section 6.3.
        SchemaMetadata.Apply(table, field, member, diagnostics);

        // The description cell wins where a sheet wrote one, and the declaration answers
        // where it did not. The other way round would silently reword the generated code of
        // every sheet that has one written today, which is a change nobody asked for in a
        // change about types.
        if (string.IsNullOrEmpty(field.Comment))
            field.Comment = member.Comment;
    }

    /// <summary>
    /// The member a column's path names, or null when the declaration has no such member.
    /// </summary>
    /// <remarks>
    /// From level one, because level zero is the group itself - the column named `Reward1` and
    /// the struct is what `Reward` is. Every level but the last has to be a struct, and that
    /// is the whole of the depth rule.
    /// </remarks>
    private static SchemaField? Walk(Field field, SchemaStruct declared, SchemaDeclarations declarations)
        => Walk(field, declared, declarations, out _, out _);

    /// <param name="containerKind">
    /// The container this column is under, or none. Reported alongside the member because
    /// the walk is the one place that knows - by the time a column is a column, a
    /// `set&lt;string&gt;` and a `string[]` are the same thing.
    /// </param>
    /// <param name="containerLevel">Which level of the path that container sits at.</param>
    private static SchemaField? Walk(
        Field field,
        SchemaStruct declared,
        SchemaDeclarations declarations,
        out Models.ContainerKind containerKind,
        out int containerLevel)
    {
        var path = field.NamePath!;
        var here = declared;
        SchemaField? member = null;

        containerKind = Models.ContainerKind.None;
        containerLevel = -1;

        for (int level = 1; level < path.Count; level++)
        {
            if (path[level].IsAnonymous)
                return null;

            // One level below a `map` are its two columns, and the declaration named
            // neither - so the step is taken against the container rather than against a
            // struct's members. spec/types/set-and-map.md section 3.
            if (member is not null
                && SchemaContainers.KindOf(member.Type) != Models.ContainerKind.None)
            {
                member = SchemaContainers.SlotOf(member, path[level].Name);

                if (member is null)
                    return null;

                here = declarations.FindStruct(member.Type.Name);
                continue;
            }

            if (here is null)
                return null;

            member = here.LiveFields.FirstOrDefault(
                candidate => candidate.Name.ToPascalCase() == path[level].Name);

            if (member is null)
                return null;

            if (SchemaContainers.KindOf(member.Type) is var found
                && found != Models.ContainerKind.None)
            {
                containerKind = found;
                containerLevel = level;
            }

            here = member.Type.Form == SchemaTypeForm.Named
                ? declarations.FindStruct(member.Type.Name)
                : null;
        }

        // A member of a struct that a container holds is one column per entry, so its column
        // is an array of what the member declares. The container's own two columns are made
        // that way already; this is every level below them.
        // spec/types/set-and-map.md section 3.
        if (member is not null && containerLevel >= 0 && path.Count - 1 > containerLevel + 1)
            member = SchemaContainers.Held(member);

        // A `set` is one column, and the brackets that say what its elements are were
        // written on the argument. The declared member carries only what is about the
        // container, so handing it on as it stands would drop the role, the bounds and the
        // pattern - parsed, checked for where they sit, and then ignored. Section 2.2.
        if (member is not null
            && containerLevel == path.Count - 1
            && SchemaContainers.KindOf(member.Type) == Models.ContainerKind.Set)
        {
            member = SchemaContainers.ColumnMemberOfSet(member) ?? member;
        }

        return member;
    }

    /// <summary>
    /// Reports a member the declaration has and the sheet gave no column.
    /// </summary>
    /// <remarks>
    /// The type is a promise about what a value of it holds, so a group missing one of its
    /// members is a group that is not that type. Left out rather than filled with the type's
    /// empty value, because a consumer reading the generated code would find the member there
    /// and no row would ever have written it.
    ///
    /// Only the group's own level. A member that is itself a struct is answered by the
    /// columns underneath it, and reporting it here as well would say the same thing twice.
    /// </remarks>
    private static void RefuseMembersWithNoColumn(
        Table table, List<Field> group, SchemaStruct declared, Diagnostics diagnostics)
    {
        var written = group
            .Where(field => field.NamePath!.Count > 1)
            .Select(field => field.NamePath![1].Name)
            .ToHashSet(System.StringComparer.Ordinal);

        foreach (var member in declared.LiveFields)
        {
            if (written.Contains(member.Name.ToPascalCase()))
                continue;

            diagnostics.Error(member.Location, Message.Of(
                SchemaMessages.MemberHasNoColumn,
                ("Table", table.Name),
                ("Group", group[0].GroupName ?? ""),
                ("Struct", declared.Name),
                ("Member", member.Name)));
        }
    }

    /// <summary>
    /// Reports every column still waiting for a type once the groups have been bound.
    /// </summary>
    /// <remarks>
    /// **This is what pays for the type cell being allowed to be blank at all.** A run that
    /// reads declarations lets a layout hand over an untyped column, on the promise that a
    /// group would claim it; a column no group claimed is that promise broken, and it must
    /// not reach the output as a string nobody asked for.
    /// </remarks>
    private static void RefuseColumnsNobodyTyped(
        CookingContext context, Model model, HashSet<Field> claimed, Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            foreach (var field in table.Fields)
            {
                if (claimed.Contains(field) || !context.IsDeferredTypeName(field.TypeName))
                    continue;

                diagnostics.Error(field.TypeLocation, Message.Of(
                    string.IsNullOrEmpty(field.TypeName)
                        ? SchemaMessages.ColumnHasNoType
                        : SchemaMessages.ColumnTypedWithAStruct,
                    ("Table", table.Name),
                    ("Column", field.RawName),
                    ("Written", field.TypeName)));
            }
        }
    }
}
