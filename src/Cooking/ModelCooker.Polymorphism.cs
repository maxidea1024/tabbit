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
/// spec/polymorphism.md section 5.2.
///
/// **Nothing new reaches the wire.** The discriminator is a plain integer column carrying each
/// variant's `@N`, base fields are ordinary columns present in every row, and a variant member
/// is an optional column - which v103 already carries. So this pass writes into the model what
/// the exporters already know how to write. spec/polymorphism.md section 6.
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

        BindDiscriminator(context, table, naming, declared, variants, declarations);

        foreach (var field in group)
        {
            if (ReferenceEquals(field, naming))
                continue;

            BindVariantMember(
                context, table, field, declared, variants, declarations, diagnostics);
        }
    }

    /// <summary>
    /// The `$type` column: an integer carrying each row's variant number.
    /// </summary>
    /// <remarks>
    /// Int32 rather than a generated enum. The cell holds a variant name and the file holds
    /// that variant's number, which is the relation an enum column has to its labels - but the
    /// variant is already a declared type, so a second name for it would be a third spelling
    /// of one thing. spec/polymorphism.md section 7.1.
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

        field.Variants = variants
            .Select(variant => new PolymorphicVariant
            {
                Name = variant.Name.ToPascalCase(),
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
    /// column that cannot be read. spec/polymorphism.md section 5.2.
    /// </remarks>
    private static void BindVariantMember(
        CookingContext context,
        Table table,
        Field field,
        SchemaStruct declared,
        List<SchemaStruct> variants,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        if (field.NamePath is not { Count: > 1 })
            return;

        string wanted = field.NamePath[^1].Name;

        var baseMember = declared.LiveFields.FirstOrDefault(
            member => member.Name.ToPascalCase() == wanted);

        if (baseMember is not null)
        {
            // A base field. Nothing optional about it - every variant carries it, which is the
            // reason section 5.1 puts the base fields in one column instead of copying them
            // into every variant.
            ApplyMemberType(context, table, field, baseMember, declared, declarations, diagnostics);
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

        ApplyMemberType(context, table, field, first.Member!, declared, declarations, diagnostics);

        field.VariantsDeclaringThis = declaring
            .Select(found => found.Variant.Name.ToPascalCase())
            .ToList();

        // A row of another variant leaves this blank, so the column carries a presence bit.
        // The generated variant type still declares the member as the declaration wrote it -
        // whether it is there is answered by the variant, not by a `Has` accessor.
        // spec/polymorphism.md section 7.
        field.IsRequired = false;
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
        Diagnostics diagnostics)
    {
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
                ("Written", field.TypeName)));

            return;
        }

        SchemaMetadata.Apply(table, field, member, diagnostics);

        if (string.IsNullOrEmpty(field.Comment))
            field.Comment = member.Comment;
    }
}
