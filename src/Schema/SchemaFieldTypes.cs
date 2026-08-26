using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Extensions;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// What a declared member is, said in the terms a column is written in.
/// </summary>
/// <remarks>
/// One direction only: a <see cref="SchemaTypeRef"/> in and a <see cref="Field"/>'s type out.
/// The reverse question - what a column's type would be spelled as in a schema file - is not
/// asked anywhere, and writing it would be a second table of correspondences to keep in step
/// with this one.
///
/// **A column that was waiting is converted here as well as typed.** Its cells were kept as
/// the text the sheet wrote, because the type was not known while the sheet was being read;
/// giving the column a type without reading that text again would leave a column of strings
/// claiming to be numbers. <see cref="ModelCooker"/> does the same for a reference.
/// </remarks>
internal static class SchemaFieldTypes
{
    /// <summary>
    /// Gives a column its declared member's type, or checks the one it wrote against it.
    /// </summary>
    /// <param name="waiting">
    /// Whether the sheet left this column for the declaration to type. False is a column that
    /// wrote its own type, which is checked rather than overwritten.
    /// </param>
    /// <param name="wanted">How the declared type reads, for a report about it.</param>
    /// <returns>False when the member cannot type a column, or when the two disagree.</returns>
    public static bool Apply(
        CookingContext context,
        Table table,
        Field field,
        SchemaField member,
        SchemaDeclarations declarations,
        bool waiting,
        Diagnostics diagnostics,
        out string wanted,
        bool columnIsOptional = false)
    {
        wanted = member.Type.ToString();

        if (!Resolve(context, member.Type, declarations, out var resolved))
            return false;

        if (!waiting)
            return Agrees(field, resolved);

        field.Type = resolved.Type;
        field.TypeName = resolved.TypeName;
        // **The member and the column are different questions for a polymorphic group.** A
        // variant's member is required within that variant - a `DamageEffect` has a `damage` -
        // and the column is optional all the same, because the rows of the other variants
        // leave it blank. Set before the cells are read, since reading is what needs the
        // answer. spec/types/polymorphism.md section 5.2.
        field.IsRequired = !columnIsOptional && !member.Type.IsOptional;
        field.ElementsRequired = !member.Type.ElementsAreOptional;

        if (resolved.RefTables is not null)
        {
            field.RefTableName = resolved.RefTables[0];
            field.RefFieldName = null;
        }

        // A reference keeps its text for the pass that resolves it - which type its key is
        // depends on the target, and the target may not have been read yet. Everything else
        // is read now, because the type is settled and the text is what a cell still holds.
        if (resolved.RefTables is null)
        {
            Convert(
                context, table, field,
                SchemaDefaults.Read(context, member, resolved, diagnostics),
                columnIsOptional);
        }

        return true;
    }

    /// <summary>What a member's type is, once resolved.</summary>
    /// <param name="RefTables">
    /// The tables a reference may point at, or null when the member is not one. A reference
    /// keeps <see cref="CookingContext.DeferredType"/> until the pass that resolves it runs,
    /// because which type its key is depends on the target.
    /// </param>
    public readonly record struct Resolved(
        Models.ValueType Type, string TypeName, List<string>? RefTables);

    /// <summary>
    /// The same as <see cref="Resolve(CookingContext, SchemaTypeRef, SchemaDeclarations, out Resolved)"/>,
    /// for a caller that has a member rather than a type and wants null on failure.
    /// </summary>
    public static Resolved? ResolvedOf(
        CookingContext context, SchemaField member, SchemaDeclarations declarations)
        => Resolve(context, member.Type, declarations, out var resolved) ? resolved : null;

    /// <summary>
    /// What a written member type is in the terms a column carries, or false when a single
    /// column cannot hold one.
    /// </summary>
    public static bool Resolve(
        CookingContext context,
        SchemaTypeRef written,
        SchemaDeclarations declarations,
        out Resolved resolved)
    {
        resolved = default;

        if (written.Form == SchemaTypeForm.Container)
            return false;

        if (written.Form == SchemaTypeForm.Foreign)
        {
            // An array of references resolves a varying number of targets per row, which the
            // generated readers have no shape for. Refused here for the same reason the
            // sheet notation refuses it, rather than allowed to reach them.
            if (written.IsArray)
                return false;

            resolved = new Resolved(
                CookingContext.DeferredType,
                "$Unresolved$",
                written.ForeignTables.Select(name => name.ToPascalCase()).ToList());

            return true;
        }

        // A member whose type is a struct is not a column. It is a level of the path, and the
        // columns underneath it are what carry values - so a path that stops here has named a
        // record where a value belongs.
        if (declarations.FindStruct(written.Name) is not null)
            return false;

        string name = declarations.FindEnum(written.Name) is { } declared
            ? declared.Name.ToPascalCase()
            : written.Name;

        var element = declarations.FindEnum(written.Name) is not null
            ? Models.ValueType.Enum
            : context.ParseValueType(written.Name, Location.OfTextFile("", 1, 1));

        if (!written.IsArray)
        {
            resolved = new Resolved(element, name, null);
            return true;
        }

        var array = ValueTypes.ArrayOf(element);
        if (array == Models.ValueType.None)
            return false;

        // The element's name, not the array's: an enum array's name is the enum to look up,
        // and every generator writes the brackets itself.
        resolved = new Resolved(array, name, null);
        return true;
    }

    /// <summary>
    /// Whether a column that wrote its own type wrote the declared one.
    /// </summary>
    /// <remarks>
    /// On the resolved type rather than on the spelling, because two spellings of one type
    /// are one type - a sheet writing `Element` where the declaration says `Element` should
    /// not turn on which of them lowered the cell.
    /// </remarks>
    private static bool Agrees(Field field, Resolved resolved)
        => field.Type == resolved.Type
           && string.Equals(field.TypeName, resolved.TypeName, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a column's cells again, now that there is a type to read them as.
    /// </summary>
    /// <remarks>
    /// Every set of the table's rows, not only its own: a cell that was not converted keeps
    /// the text the sheet wrote, and a consumer would then find a string where the generated
    /// code promised a number.
    ///
    /// A cell that is not text was parsed by the layout already, which happens where a column
    /// wrote its own type and only its description was left to the declaration. Left as it
    /// is - it is already the value it should be.
    ///
    /// **A column whose member declares a default must leave its type cell empty.** Written
    /// out, the layout reads the cell while the sheet is being parsed and refuses a blank
    /// before anything here runs; left empty, the cell arrives as text and this is where a
    /// blank becomes what the declaration says it means.
    /// </remarks>
    private static void Convert(
        CookingContext context, Table table, Field field, object? fallback,
        bool columnIsOptional = false)
    {
        foreach (var rowSet in table.RowSets)
        foreach (var row in rowSet.Rows)
        {
            if (field.Index >= row.Count)
                continue;

            var cell = row[field.Index];

            if (cell.Value is not string written)
                continue;

            // **A cell the layout already called absent stays absent.** A `[]` group is as
            // wide as the longest record, so a shorter one has no element at the far columns -
            // and the layout says so by leaving `HasValue` false. Parsing it anyway asks an
            // `int` member to read an empty string, and the report then names a cell that
            // holds the value it was supposed to read: the group is read at the wrong element.
            // A written-out type cell never reached here, which is why the pair hid it.
            if (!cell.HasValue && written.Length == 0)
                continue;

            var declaredEnum = field.Type is Models.ValueType.Enum or Models.ValueType.EnumArray
                ? context.Model.FindEnum(field.TypeName)
                : null;

            // **Here rather than in a pass of its own, because here is where the written
            // text still exists.** A column left for a declaration to type is carried as a
            // string until now, so its blank cell already reads as an empty string that a
            // row wrote - and nothing later could tell it from one somebody meant.
            if (fallback is not null && written.Length == 0)
            {
                cell.Value = fallback;
                cell.HasValue = true;
                continue;
            }

            // **A blank in a column that is only optional because of its group is absence,
            // not the empty value the sheet wrote.** A deferred column arrives as text, so a
            // blank cell is an empty string that reads as present - which is right for a
            // `string` member and wrong for a variant's, where the blank means this row is
            // another variant. spec/types/polymorphism.md section 5.2.
            if (columnIsOptional && written.Length == 0)
                cell.HasValue = false;

            cell.Value = context.ParseValue(
                field.Type, declaredEnum, written, cell.RawCell?.Location,
                required: field.IsRequired)!;
        }
    }
}
