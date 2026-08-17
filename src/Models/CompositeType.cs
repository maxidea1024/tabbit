using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Models;

/// <summary>
/// One of the types whose value has several named components: a vector, a rotation, a colour.
/// </summary>
/// <remarks>
/// A composite is a type for exactly as long as parsing lasts. What makes it one is the
/// notation it accepts - a tuple, a hex colour, a name - and that question is settled once a
/// cell has become a value. The cooker then expands the column into one column per component,
/// which is the record shape the wire, the generators and the exporters already carry.
///
/// So nothing below the cooker ever sees these members, and this table is what the parser and
/// the expansion agree on: how many components, what they are called, and what type each one
/// is. spec/composite-value-types.md.
/// </remarks>
public sealed class CompositeType
{
    /// <summary>The type row's name for it, and the only spelling that row accepts.</summary>
    public required string Name { get; init; }

    /// <summary>The value type this describes.</summary>
    public required ValueType Type { get; init; }

    /// <summary>
    /// Component names, in the order a tuple writes them. These become the member names of
    /// the generated record, so they are Pascal-cased already.
    /// </summary>
    public required IReadOnlyList<string> Components { get; init; }

    /// <summary>What each component is, which is the same for every component of a type.</summary>
    public required ValueType ComponentType { get; init; }

    /// <summary>
    /// What a blank cell reads as in an optional column, component by component.
    /// </summary>
    /// <remarks>
    /// Zero for most, and deliberately not for two. `quat`'s `(0,0,0,0)` is not a rotation, so
    /// the empty value is the identity one; `axisangle`'s axis cannot be the zero vector, so
    /// its empty value stands the axis up and leaves the angle at zero.
    /// </remarks>
    public required IReadOnlyList<object> EmptyComponents { get; init; }

    /// <summary>Whether this type reads colour notation - `#3366CC`, `red`, a palette name.</summary>
    public bool IsColor { get; init; }

    /// <summary>Whether the components are 8-bit, so the notation is integral and bounded.</summary>
    public bool IsEightBitColor { get; init; }

    /// <summary>
    /// Whether `zero` and `one` name a value of this type.
    /// </summary>
    /// <remarks>
    /// The vectors only. A colour is left out because `one` would mean "every component 1" to
    /// one of the two colour types and "white" to the other, and those are different colours -
    /// see spec/composite-value-types.md section 4.2. Colours have unambiguous names already.
    /// </remarks>
    public bool TakesZeroAndOne { get; init; }

    /// <summary>Whether `identity` names a value of this type.</summary>
    public bool TakesIdentity { get; init; }

    /// <summary>
    /// Other spellings accepted in front of a symbolic literal, as in `Vector2i.one`.
    /// </summary>
    /// <remarks>
    /// The qualified form exists so a sheet can write what the engine calls the type, and
    /// refusing `Vector2i` there while accepting `vec2i` would be refusing the spelling the
    /// author came for. The type row still takes <see cref="Name"/> alone: a type has one name
    /// in the place where types are declared.
    /// </remarks>
    public IReadOnlyList<string> Aliases { get; init; } = System.Array.Empty<string>();

    /// <summary>How many components a cell of this type holds.</summary>
    public int Arity => Components.Count;
}

/// <summary>
/// The composite types, and the lookups the parser and the expansion share.
/// </summary>
public static class CompositeTypes
{
    private static readonly IReadOnlyList<object> ZeroInts =
        new object[] { 0, 0, 0, 0 };

    private static CompositeType Vector(string name, ValueType type, int arity, bool integral, params string[] aliases)
    {
        var components = new[] { "X", "Y", "Z", "W" }.Take(arity).ToList();

        return new CompositeType
        {
            Name = name,
            Type = type,
            Components = components,
            ComponentType = integral ? ValueType.Int32 : ValueType.Float,
            EmptyComponents = Enumerable.Repeat(integral ? (object)0 : 0f, arity).ToList(),
            TakesZeroAndOne = true,
            Aliases = aliases,
        };
    }

    /// <summary>Every composite type, in the order the documentation lists them.</summary>
    public static readonly IReadOnlyList<CompositeType> All = new List<CompositeType>
    {
        Vector("vec2i", ValueType.Vec2i, 2, integral: true, "vector2i", "int2"),
        Vector("vec3i", ValueType.Vec3i, 3, integral: true, "vector3i", "int3"),
        Vector("vec4i", ValueType.Vec4i, 4, integral: true, "vector4i", "int4"),
        Vector("vec2f", ValueType.Vec2f, 2, integral: false, "vector2", "vector2f", "float2"),
        Vector("vec3f", ValueType.Vec3f, 3, integral: false, "vector3", "vector3f", "float3"),
        Vector("vec4f", ValueType.Vec4f, 4, integral: false, "vector4", "vector4f", "float4"),

        new CompositeType
        {
            Name = "euler",
            Type = ValueType.Euler,
            // Named after the axes rather than pitch/yaw/roll, because which axis each of
            // those is differs between engines and this type does not pick one. What it holds
            // is three angles in degrees; the order they compose in is the consumer's
            // convention. spec/composite-value-types.md section 5.
            Components = new[] { "X", "Y", "Z" },
            ComponentType = ValueType.Float,
            EmptyComponents = new object[] { 0f, 0f, 0f },
            Aliases = new[] { "eulerangles" },
        },

        new CompositeType
        {
            Name = "quat",
            Type = ValueType.Quat,
            Components = new[] { "X", "Y", "Z", "W" },
            ComponentType = ValueType.Float,
            EmptyComponents = new object[] { 0f, 0f, 0f, 1f },
            TakesIdentity = true,
            Aliases = new[] { "quaternion" },
        },

        new CompositeType
        {
            Name = "axisangle",
            Type = ValueType.AxisAngle,
            Components = new[] { "X", "Y", "Z", "Angle" },
            ComponentType = ValueType.Float,
            EmptyComponents = new object[] { 0f, 0f, 1f, 0f },
            TakesIdentity = true,
        },

        new CompositeType
        {
            Name = "color",
            Type = ValueType.Color,
            Components = new[] { "R", "G", "B", "A" },
            ComponentType = ValueType.Float,
            // Transparent rather than opaque black. A cell nobody filled in showing up as a
            // black rectangle is a value; showing up as nothing is the absence it was.
            EmptyComponents = new object[] { 0f, 0f, 0f, 0f },
            IsColor = true,
            Aliases = new[] { "linearcolor", "flinearcolor" },
        },

        new CompositeType
        {
            Name = "color32",
            Type = ValueType.Color32,
            Components = new[] { "R", "G", "B", "A" },
            ComponentType = ValueType.Int32,
            EmptyComponents = ZeroInts,
            IsColor = true,
            IsEightBitColor = true,
            Aliases = new[] { "fcolor" },
        },
    };

    private static readonly Dictionary<ValueType, CompositeType> ByType =
        All.ToDictionary(entry => entry.Type);

    // Case sensitive, and that is not an oversight. A type row's name is resolved before the
    // enum declarations are searched, so a case-insensitive table here would take an enum
    // called `Color` away from a sheet that already has one. The primitive names are matched
    // by a `switch` on the lowered spelling for the same reason, and a layout lowers a type
    // cell before it asks - only an enum's name reaches the lookup as the author wrote it.
    private static readonly Dictionary<string, CompositeType> ByCanonicalName =
        All.ToDictionary(entry => entry.Name, System.StringComparer.Ordinal);

    private static readonly Dictionary<string, CompositeType> ByAnySpelling = BuildSpellings();

    private static Dictionary<string, CompositeType> BuildSpellings()
    {
        var result = new Dictionary<string, CompositeType>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var entry in All)
        {
            result[entry.Name] = entry;

            foreach (string alias in entry.Aliases)
                result[alias] = entry;
        }

        return result;
    }

    /// <summary>Whether a value type is one whose cell holds several components.</summary>
    public static bool IsComposite(ValueType type) => ByType.ContainsKey(type);

    /// <summary>The description of a composite type, or null when it is not one.</summary>
    public static CompositeType? Of(ValueType type)
        => ByType.TryGetValue(type, out var entry) ? entry : null;

    /// <summary>
    /// The composite a type row's name declares, or null. Canonical names only - the type row
    /// is where types are declared and a type has one name there.
    /// </summary>
    public static CompositeType? ByName(string name)
        => ByCanonicalName.TryGetValue(name, out var entry) ? entry : null;

    /// <summary>
    /// The composite a qualified literal's prefix names, or null. Aliases included, so
    /// `Vector2i.one` reaches `vec2i`.
    /// </summary>
    public static CompositeType? BySpelling(string name)
        => ByAnySpelling.TryGetValue(name, out var entry) ? entry : null;
}
