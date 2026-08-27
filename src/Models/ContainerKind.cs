namespace Tabbit.Models;

/// <summary>
/// Which container a group was declared as, or none for a group that is only an array.
/// </summary>
/// <remarks>
/// **The columns and the file are the same either way, which is why this is a mark rather
/// than a shape.** A `set&lt;T&gt;` holds what a `T[]` holds and a `map&lt;K,V&gt;` holds what a
/// record of two arrays holds - so the wire needs nothing new, and what the generated code
/// needs is to be told which of the two the author declared. Without this mark the two are
/// indistinguishable by the time a generator sees them.
///
/// spec/types/set-and-map.md sections 3 and 4.
/// </remarks>
public enum ContainerKind
{
    /// <summary>An array, or a record of them. Every table written before containers existed.</summary>
    None,

    /// <summary>`set&lt;T&gt;` - one array column whose elements are distinct.</summary>
    Set,

    /// <summary>`map&lt;K,V&gt;` - two array columns of the same length, `Key` and `Value`.</summary>
    Map,
}

/// <summary>The two member names a `map` group's columns are written under.</summary>
/// <remarks>
/// Here rather than beside the notation, because a generator needs them and a generator does
/// not read declarations - by the time it runs, `Key` is a member name like any other and the
/// only thing that says it is a map's is <see cref="ContainerKind"/>.
/// </remarks>
public static class ContainerMembers
{
    /// <summary>The column every entry's key is in.</summary>
    public const string Key = "Key";

    /// <summary>What the entries hold: one column, or a group of them.</summary>
    public const string Value = "Value";
}
