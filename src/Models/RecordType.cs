using System.Collections.Generic;

namespace Tabbit.Models;

/// <summary>
/// A declared struct a sheet used, and the members it is made of.
/// </summary>
/// <remarks>
/// **One of these per declaration, not per table that names it.** The declaration says "this
/// is one type", and the generated code says the same thing only if there is one type.
/// Emitting it inside each table gives two tables a `Reward` apiece that share a name and are
/// not the same type, and then nothing can be written that takes one - which is the opposite
/// of what declaring it in one file was for.
///
/// Beside <see cref="PolymorphicType"/> and built the same way, because it is the same
/// question asked of the other half of the notation: that one is for a declaration whose value
/// may be several shapes, and this one for a declaration whose value is one shape.
///
/// **The members are columns, and the columns belong to whichever group got there first.** The
/// declaration fixes what the members are and the binding refuses a group that leaves one out,
/// so any group that used the type answers the same - and taking the columns rather than
/// re-resolving the declaration means every generator keeps using the machinery it already
/// has. spec/types/declared-struct-identity.md.
/// </remarks>
public sealed class RecordType
{
    /// <summary>The declared name, Pascal cased, which is the type's name in every language.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The `///` block in front of the declaration, or empty where there was none.
    /// </summary>
    /// <remarks>
    /// Carried for the reason a polymorphic type carries its own: a member's description
    /// reaches the column that holds it and travels on from there, and the type's own
    /// describes a thing no column is.
    /// </remarks>
    public string Comment { get; init; } = "";

    /// <summary>Its members, in the order the group that got here first wrote them.</summary>
    public required List<RecordMember> Members { get; init; }
}
