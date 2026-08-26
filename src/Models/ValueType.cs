namespace Tabbit.Models;

/// <summary>
/// What a field holds, as written in a table's type row.
///
/// The array members start at 32 rather than following on from the scalars, leaving
/// room to add scalar types without renumbering - and letting
/// <see cref="ValueTypes.IsArray"/> be a single comparison.
/// </summary>
public enum ValueType
{
    /// <summary>Not set.</summary>
    None = 0,

    /// <summary>`string` - UTF-8 text.</summary>
    String = 1,
    /// <summary>`bool` - Y/N, YES/NO, TRUE/FALSE, 1/0, or blank for false.</summary>
    Bool = 2,
    /// <summary>`int` - 32-bit signed integer.</summary>
    Int32 = 3,
    /// <summary>`bigint` - 64-bit signed integer.</summary>
    Int64 = 4,
    /// <summary>`float` - single precision.</summary>
    Float = 5,
    /// <summary>`double` - double precision.</summary>
    Double = 6,
    /// <summary>`timespan` - a duration, carried as 100 ns ticks.</summary>
    TimeSpan = 7,
    /// <summary>`datetime` - a point in time, carried as 100 ns ticks.</summary>
    DateTime = 8,
    /// <summary>`uuid` - a 128-bit identifier.</summary>
    Uuid = 9,
    /// <summary>A label of an `enum` entity declared in the sheets.</summary>
    Enum = 10,

    /// <summary>
    /// A `foreign` reference to a whole row of another table. Stored as that row's
    /// primary index and turned into a reference by the generated code once every
    /// table is loaded.
    /// </summary>
    ForeignRecord = 11,

    /// <summary>
    /// Placeholder for a reference whose target is not known yet.
    ///
    /// Table data is parsed before references are resolved, so nothing should still
    /// hold this by the time a value is read - a field that does means resolution
    /// never ran for it.
    /// </summary>
    Unresolved = 12,

    /// <summary>
    /// `bitset` - up to 64 flags, carried as the bit pattern of a 64-bit integer.
    ///
    /// What separates it from `bigint` is the notation it accepts rather than what it
    /// holds: a bit pattern has no sign and no thousands separator, so it can refuse
    /// both, and a magnitude cannot. spec/types/bitset.md says why that makes it a type
    /// rather than a role.
    /// </summary>
    Bitset = 13,

    // The composite types: one cell, several named components. Like `bitset` these exist for
    // as long as parsing lasts - what makes each one a type is the notation it accepts - and
    // the cooker expands a column of one into a record before anything downstream sees it.
    // There is deliberately no array counterpart: `ArrayOf` answers None for all of them, so
    // `vec2f[]` is refused by the check every other unbracketable type is refused by.
    // spec/types/composite-value-types.md.

    /// <summary>`vec2i` - two `int` components, `X` and `Y`.</summary>
    Vec2i = 14,
    /// <summary>`vec3i` - three `int` components.</summary>
    Vec3i = 15,
    /// <summary>`vec4i` - four `int` components.</summary>
    Vec4i = 16,
    /// <summary>`vec2f` - two `float` components.</summary>
    Vec2f = 17,
    /// <summary>`vec3f` - three `float` components.</summary>
    Vec3f = 18,
    /// <summary>`vec4f` - four `float` components.</summary>
    Vec4f = 19,

    /// <summary>`euler` - three angles in degrees. The order they compose in is the consumer's.</summary>
    Euler = 20,
    /// <summary>`quat` - a rotation as `X` `Y` `Z` `W`.</summary>
    Quat = 21,
    /// <summary>`axisangle` - a unit axis and an angle in degrees.</summary>
    AxisAngle = 22,

    /// <summary>`color` - `float` RGBA in sRGB, unbounded so an HDR colour fits.</summary>
    Color = 23,
    /// <summary>`color32` - 8-bit RGBA in sRGB, each component 0 to 255.</summary>
    Color32 = 24,

    /// <summary>`string[]`</summary>
    StringArray = 32,
    /// <summary>`bool[]`</summary>
    BoolArray = 33,
    /// <summary>`int[]`</summary>
    Int32Array = 34,
    /// <summary>`bigint[]`</summary>
    Int64Array = 35,
    /// <summary>`float[]`</summary>
    FloatArray = 36,
    /// <summary>`double[]`</summary>
    DoubleArray = 37,
    /// <summary>`timespan[]`</summary>
    TimeSpanArray = 38,
    /// <summary>`datetime[]`</summary>
    DateTimeArray = 39,
    /// <summary>`uuid[]`</summary>
    UuidArray = 40,
    /// <summary>`enum[]`</summary>
    EnumArray = 41,

    /// <summary>
    /// Reserved. `foreign[]` is rejected by the cooker: resolving a varying number of
    /// references per row is a shape the generated readers do not have.
    /// </summary>
    ForeignRecordArray = 42,

    /// <summary>`bitset[]`</summary>
    BitsetArray = 43,
}

/// <summary>
/// Relates each scalar value type to its array counterpart.
///
/// Tabbit has two separate notions of "array":
///
///   * a serial field, where consecutively numbered columns (Text1, Text2, ...)
///     are folded into one array. Every row has the same number of elements,
///     because the count is the number of columns.
///
///   * an array type, written `int[]` in the type row, where one cell holds
///     several delimited values. Length varies from row to row.
///
/// The array ValueType members below the scalars have existed since the start but
/// nothing ever produced one; they describe the second kind.
/// </summary>
public static class ValueTypes
{
    /// <summary>Whether this type describes a delimited array cell.</summary>
    public static bool IsArray(ValueType type) => type >= ValueType.StringArray;

    /// <summary>
    /// The element type of an array type; the type itself when it is already
    /// scalar, so callers can normalize without testing first.
    /// </summary>
    public static ValueType ElementOf(ValueType type)
    {
        return type switch
        {
            ValueType.StringArray => ValueType.String,
            ValueType.BoolArray => ValueType.Bool,
            ValueType.Int32Array => ValueType.Int32,
            ValueType.Int64Array => ValueType.Int64,
            ValueType.FloatArray => ValueType.Float,
            ValueType.DoubleArray => ValueType.Double,
            ValueType.TimeSpanArray => ValueType.TimeSpan,
            ValueType.DateTimeArray => ValueType.DateTime,
            ValueType.UuidArray => ValueType.Uuid,
            ValueType.EnumArray => ValueType.Enum,
            ValueType.ForeignRecordArray => ValueType.ForeignRecord,
            ValueType.BitsetArray => ValueType.Bitset,
            _ => type,
        };
    }

    /// <summary>
    /// Whether a column of this type can carry a lookup key, and what is wrong with it
    /// when it cannot.
    /// </summary>
    /// <remarks>
    /// One rule for every index rather than one per kind of index. There used to be two -
    /// the first column was held to `int` or `string`, and a `*` column only to not being a
    /// float - so `*Flag: bool` was accepted as an index and then reported as repeated
    /// values, which names the symptom instead of the mistake. A key is a key; where it
    /// sits in the sheet does not change what it has to be.
    ///
    /// Four separate reasons to refuse, which is why the answer carries one:
    ///
    ///   * `bool` holds two values, so a table keyed by one can have two rows. That is not
    ///     a lookup, and the duplicate a third row produces does not say so.
    ///   * `float` and `double` do not compare exactly. The generated lookup is keyed by
    ///     the value itself, so `1.1` from the sheet and `1.1` from a caller's arithmetic
    ///     are two different keys and the lookup misses without failing.
    ///   * an array cell holds several values, and a key is one. Marking one `*` used to be
    ///     accepted and then quietly do nothing: the uniqueness check ran over boxed arrays
    ///     and no lookup was generated for it.
    ///   * `datetime` and `timespan` would compare exactly - they are ticks - and are still
    ///     refused, on the one ground that no sheet keys its rows by when something
    ///     happened. Allowing them would mean standing behind a lookup keyed by a time in
    ///     every language, several of which reach for a type whose equality is not the
    ///     value's, and there is nothing asking for it.
    ///
    /// Everything else is a key, `enum` included: its labels are a list the author wrote,
    /// and a table with a row per label is a shape sheets really have. What a non-`int` key
    /// costs is references *to* that table - the wire carries a reference as the target's
    /// key in an int32 - and that is said where references are checked, not here.
    /// </remarks>
    public static bool CanBeIndexKey(ValueType type, out string? why)
    {
        if (IsArray(type))
        {
            why = "and an array cell holds several values where a key is one.";
            return false;
        }

        // Asked before the switch so the answer names the composite rather than one of its
        // components. The expansion would refuse it anyway - a record is not a key - but by
        // then the column is `Pos.X` and a report about it would not say `vec3f`, which is
        // the word the sheet used and the one an author can act on.
        if (CompositeTypes.IsComposite(type))
        {
            why = "and a value with several components is not one value to look a row up by.";
            return false;
        }

        switch (type)
        {
            case ValueType.Bool:
                why = "and a table keyed by a bool can only hold two rows.";
                return false;

            case ValueType.DateTime:
            case ValueType.TimeSpan:
                why = "and rows are not looked up by a time value.";
                return false;

            case ValueType.Float:
            case ValueType.Double:
                why = "and a lookup keyed by a floating point value misses on values "
                      + "that look equal but are not.";
                return false;

            default:
                why = null;
                return true;
        }
    }

    /// <summary>
    /// The array type holding <paramref name="element"/>, or None when there is no
    /// array form of it.
    /// </summary>
    public static ValueType ArrayOf(ValueType element)
    {
        return element switch
        {
            ValueType.String => ValueType.StringArray,
            ValueType.Bool => ValueType.BoolArray,
            ValueType.Int32 => ValueType.Int32Array,
            ValueType.Int64 => ValueType.Int64Array,
            ValueType.Float => ValueType.FloatArray,
            ValueType.Double => ValueType.DoubleArray,
            ValueType.TimeSpan => ValueType.TimeSpanArray,
            ValueType.DateTime => ValueType.DateTimeArray,
            ValueType.Uuid => ValueType.UuidArray,
            ValueType.Enum => ValueType.EnumArray,
            ValueType.ForeignRecord => ValueType.ForeignRecordArray,
            ValueType.Bitset => ValueType.BitsetArray,
            _ => ValueType.None,
        };
    }
}
