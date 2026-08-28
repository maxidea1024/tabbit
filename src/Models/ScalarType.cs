using System.Collections.Generic;

namespace Tabbit.Models;

/// <summary>
/// The type names a cell may be declared with that hold one value.
/// </summary>
/// <remarks>
/// **One table, because the names were written out twice and are asked for a third time.**
/// The cooker both decides whether a name is a type and works out which type it is, and those
/// two lists have to agree; an editor offering the names to whoever is typing makes three
/// places that would have to be changed together. A name added here reaches all of them.
///
/// The composites are next door in <see cref="CompositeTypes"/> and are not repeated here -
/// their names carry a component type and an arity, so they are a table of their own.
/// </remarks>
public static class ScalarTypes
{
    /// <summary>Every scalar type name, and what a cell of it holds.</summary>
    /// <remarks>
    /// Matched exactly, which is what the two switches this replaces did. A layout lowers a
    /// type cell before it asks, so the spelling that arrives here is already settled.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, ValueType> ByName =
        new Dictionary<string, ValueType>(System.StringComparer.Ordinal)
        {
            ["string"] = ValueType.String,

            // Strings, and deliberately indistinguishable from one here. What separates these
            // from `string` is what else is done with the value - gathering it for
            // translation, checking that a file of that name exists - and never what the value
            // is. The difference travels on the field rather than in the type: see StringRole.
            ["text"] = ValueType.String,
            ["asset"] = ValueType.String,

            ["bool"] = ValueType.Bool,
            ["int"] = ValueType.Int32,
            ["bigint"] = ValueType.Int64,
            ["float"] = ValueType.Float,
            ["double"] = ValueType.Double,
            ["datetime"] = ValueType.DateTime,
            ["timespan"] = ValueType.TimeSpan,
            ["uuid"] = ValueType.Uuid,

            // Up to 64 flags. A separate name rather than `bigint` because the notation it
            // accepts is narrower, and a type that does not say it holds a pattern has no
            // ground to refuse a sign. spec/types/bitset.md.
            ["bitset"] = ValueType.Bitset,
        };

    /// <summary>Whether a name is one of them.</summary>
    public static bool Has(string name) => ByName.ContainsKey(name);
}
