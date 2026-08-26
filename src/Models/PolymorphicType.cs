using System.Collections.Generic;

namespace Tabbit.Models;

/// <summary>
/// An abstract type a sheet used, and the variants it may be.
/// </summary>
/// <remarks>
/// **One of these per declaration, not per table that names it.** A struct is an entity beside
/// a table and an enum: the declaration says "this is one type", and the generated code says
/// the same thing only if there is one type. Emitting it inside each table would give two
/// tables an `Effect` apiece that share a name and are not the same type, and then nothing can
/// be written that takes one - which is the opposite of what declaring it in one file was for.
///
/// **The members are columns, and the columns belong to whichever group got there first.** The
/// declaration fixes what the members are and the binding refuses a group whose columns
/// disagree, so any group that used the type answers the same - and taking the columns rather
/// than re-resolving the declaration means every generator keeps using the type names it
/// already knows how to write. spec/types/polymorphism.md section 7.1.
/// </remarks>
public sealed class PolymorphicType
{
    /// <summary>The abstract type's declared name.</summary>
    public required string Name { get; init; }

    /// <summary>Its own fields, which every variant carries.</summary>
    public required List<Field> BaseMembers { get; init; }

    /// <summary>What one of its values may be, in declaration order.</summary>
    public required List<PolymorphicTypeVariant> Variants { get; init; }
}

/// <summary>One variant of a <see cref="PolymorphicType"/>, with the columns it declares.</summary>
public sealed class PolymorphicTypeVariant
{
    /// <summary>The variant's declared name.</summary>
    public required string Name { get; init; }

    /// <summary>The number the file carries for it.</summary>
    public required int Discriminator { get; init; }

    /// <summary>
    /// The members this variant declares, beside the base ones.
    /// </summary>
    /// <remarks>
    /// A member several variants declare appears in each of their lists. The column is shared -
    /// one column, one type - and so is the field on each variant type.
    /// </remarks>
    public required List<Field> Members { get; init; }
}
