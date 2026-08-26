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
