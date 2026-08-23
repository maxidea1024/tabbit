using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.Cooking;

/// <summary>
/// Reads a cell of a composite type into its components.
/// </summary>
/// <remarks>
/// **The refusals are the content of this type.** A tuple with the wrong number of components,
/// a decimal point in an 8-bit colour, a direction name whose value depends on the engine -
/// each of those is a mistake the notation exists to catch, and a reader that accepted them
/// would give the sheet no more than four `float` columns already gave it.
///
/// What comes back is a typed array of the component type: `int[]` or `float[]`, of the
/// arity the type declares. The expansion then hands each element to its own column.
///
/// spec/composite-value-types.md section 4.
/// </remarks>
public static class CompositeValues
{
    /// <summary>What separates the components of a tuple, in every layout and every entry.</summary>
    /// <remarks>
    /// Fixed rather than configurable. A tuple is one value and its inside is the type's
    /// notation, not the sheet's - and an entry that could change it would make the same cell
    /// text mean different values in two builds.
    /// </remarks>
    public const char ComponentSeparator = ',';

    private const NumberStyles IntegerStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands;

    private const NumberStyles DecimalStyles =
        NumberStyles.Float | NumberStyles.AllowThousands;

    /// <summary>
    /// Reads a cell into the components of <paramref name="composite"/>.
    /// </summary>
    public static System.Array Parse(
        CompositeType composite, string? rawValue, Location? location, ColorPalettes palettes)
    {
        string text = (rawValue ?? "").Trim();

        if (text.Length == 0)
        {
            throw new TabbitException(location,
                $"A `{composite.Name}` cell cannot be blank. Write its {composite.Arity} "
                + $"components as `({string.Join(", ", composite.Components.Select(_ => "0"))})`, "
                + $"or mark the column `{composite.Name}?` to let a blank mean no value.");
        }

        text = StripParentheses(composite, text, location);

        if (text.IndexOf(ComponentSeparator) >= 0)
            return FromTuple(composite, text, location);

        return FromSingleToken(composite, text, location, palettes);
    }

    /// <summary>The empty value of a composite type, component by component.</summary>
    public static System.Array Empty(CompositeType composite)
        => Pack(composite, composite.EmptyComponents.ToList());

    // ------------------------------------------------------------------ notation

    /// <summary>
    /// Takes the outer parentheses off, and refuses one that is not closed.
    /// </summary>
    /// <remarks>
    /// Optional rather than required, and section 8 of the spec is why: a spreadsheet's
    /// general format reads `(1,234)` as the accounting notation for -1234, so a rule that
    /// forced parentheses would force a cell into the one shape that gets rewritten before
    /// this code sees it.
    /// </remarks>
    private static string StripParentheses(CompositeType composite, string text, Location? location)
    {
        bool opens = text.StartsWith("(", System.StringComparison.Ordinal);
        bool closes = text.EndsWith(")", System.StringComparison.Ordinal);

        if (opens != closes)
        {
            throw new TabbitException(location,
                $"`{text}` has one parenthesis of a pair. A `{composite.Name}` cell is written "
                + $"`({string.Join(", ", composite.Components.Select(_ => "0"))})`, or without "
                + "the parentheses altogether.");
        }

        return opens ? text.Substring(1, text.Length - 2).Trim() : text;
    }

    private static System.Array FromTuple(CompositeType composite, string text, Location? location)
    {
        var parts = text.Split(ComponentSeparator).Select(part => part.Trim()).ToList();

        if (composite.IsColor && parts.Count == 3)
        {
            // The one place a component may be left out. Alpha has an answer that does not
            // depend on the other three - a colour nobody gave an alpha to is opaque - and
            // that is what makes it safe to default where a missing `Z` would not be.
            parts.Add(composite.IsEightBitColor ? "255" : "1");
        }

        if (parts.Count != composite.Arity)
            throw WrongComponentCount(composite, text, parts, location);

        if (parts.Any(part => part.Length == 0))
        {
            throw new TabbitException(location,
                $"`{text}` leaves one of its components empty. A `{composite.Name}` cell gives "
                + $"a value for each of {string.Join(", ", composite.Components)}.");
        }

        var values = new List<object>(parts.Count);

        for (int at = 0; at < parts.Count; at++)
            values.Add(Component(composite, at, parts[at], text, location));

        return Pack(composite, values);
    }

    /// <summary>
    /// Reads one component in the notation its own type reads.
    /// </summary>
    /// <remarks>
    /// So `(0xFF, 0x80, 0x40)` works in an integral type: the radix literals are the numeric
    /// types' notation and a component is one of those values. The colour forms - `#3366CC`
    /// and the names - are the whole cell's notation rather than a component's, which is why
    /// they are read before this and not here.
    /// </remarks>
    private static object Component(
        CompositeType composite, int at, string part, string whole, Location? location)
    {
        string name = composite.Components[at];

        if (composite.ComponentType == ValueType.Int32)
        {
            if (part.IndexOf('.') >= 0)
            {
                throw new TabbitException(location, composite.IsEightBitColor
                    ? $"`{whole}` writes `{part}` with a decimal point, and `color32` holds "
                      + "8-bit components. `(1.0, 1.0, 1.0)` is white to `color`, and to this "
                      + "type it would be three 255ths - so it is refused rather than guessed "
                      + "at. Write `(255, 255, 255)`, or make the column `color`."
                    : $"`{whole}` writes `{part}` with a decimal point, and `{composite.Name}` "
                      + $"holds whole numbers. Write the column as its `float` counterpart if "
                      + "the value is not whole.");
            }

            int value = ReadInteger(part, whole, name, composite, location);

            if (composite.IsEightBitColor && value is < 0 or > 255)
            {
                throw new TabbitException(location,
                    $"`{whole}` gives {name} the value {value}, and an 8-bit colour component "
                    + "is 0 to 255. A colour with values outside that range is a `color`.");
            }

            return value;
        }

        return ReadFloat(part, whole, name, composite, location);
    }

    private static int ReadInteger(
        string part, string whole, string name, CompositeType composite, Location? location)
    {
        try
        {
            return int.Parse(CookingContext.ComponentLiteral(part, ValueType.Int32, location), IntegerStyles, CultureInfo.InvariantCulture);
        }
        catch (System.Exception ex) when (ex is System.FormatException or System.OverflowException)
        {
            throw new TabbitException(location,
                $"`{whole}` gives {name} the value `{part}`, which is not a whole number a "
                + $"`{composite.Name}` component can hold. ({ex.Message})");
        }
    }

    private static float ReadFloat(
        string part, string whole, string name, CompositeType composite, Location? location)
    {
        try
        {
            return float.Parse(CookingContext.ComponentLiteral(part, ValueType.Float, location), DecimalStyles, CultureInfo.InvariantCulture);
        }
        catch (System.Exception ex) when (ex is System.FormatException or System.OverflowException)
        {
            throw new TabbitException(location,
                $"`{whole}` gives {name} the value `{part}`, which is not a number a "
                + $"`{composite.Name}` component can hold. ({ex.Message})");
        }
    }

    // --------------------------------------------------------------- single token

    private static System.Array FromSingleToken(
        CompositeType composite, string text, Location? location, ColorPalettes palettes)
    {
        if (composite.IsColor)
        {
            if (ColorPalettes.TryReadHex(text, out uint packed))
                return Color(composite, Unpack(packed));

            if (palettes.TryLookup(text, out int[] rgba, out string? problem))
                return Color(composite, rgba);

            // A name that is not a colour and not a number is a name, so the palette's report
            // is the one that helps. A number is the spreadsheet trap below.
            if (!LooksNumeric(text))
                throw new TabbitException(location, problem!);
        }

        if (TrySymbolic(composite, text, location, out var symbolic))
            return symbolic!;

        throw WrongComponentCount(composite, text, new List<string> { text }, location);
    }

    /// <summary>
    /// Reads `zero`, `one`, `identity`, and the qualified forms of them.
    /// </summary>
    /// <remarks>
    /// The prefix has to name the column's own type. A `vec2i` column holding `vec3f.one` is
    /// a cell that was edited against the wrong column, and the arity happening to differ is
    /// not what makes it wrong - `vec2i.one` in a `vec2f` column is the same mistake with the
    /// same number of components.
    /// </remarks>
    private static bool TrySymbolic(
        CompositeType composite, string text, Location? location, out System.Array? value)
    {
        value = null;

        string literal = text;

        int dot = text.LastIndexOf('.');
        if (dot > 0)
        {
            string prefix = text.Substring(0, dot);
            literal = text.Substring(dot + 1);

            var named = CompositeTypes.BySpelling(prefix);

            if (named is null)
            {
                // Not a type name at all, so this is not the qualified form and whatever it
                // is will be reported by the caller.
                return false;
            }

            if (named.Type != composite.Type)
            {
                throw new TabbitException(location,
                    $"`{text}` names the type `{named.Name}`, and this column is "
                    + $"`{composite.Name}`. Write `{composite.Name}.{literal}`, or fix the "
                    + "column the value was meant for.");
            }
        }

        if (composite.TakesZeroAndOne)
        {
            if (Is(literal, "zero"))
            {
                value = Pack(composite, Repeated(composite, 0));
                return true;
            }

            if (Is(literal, "one"))
            {
                value = Pack(composite, Repeated(composite, 1));
                return true;
            }
        }

        if (composite.TakesIdentity && Is(literal, "identity"))
        {
            value = Empty(composite);
            return true;
        }

        // A qualified literal whose prefix was right and whose name was not: the column is not
        // in doubt, so say what this type actually offers.
        if (dot > 0)
        {
            throw new TabbitException(location,
                $"`{text}` is not a literal of `{composite.Name}`. "
                + $"{Offered(composite)}");
        }

        return false;
    }

    private static bool Is(string written, string literal)
        => string.Equals(written, literal, System.StringComparison.OrdinalIgnoreCase);

    private static List<object> Repeated(CompositeType composite, int value)
        => Enumerable
            .Repeat(composite.ComponentType == ValueType.Int32 ? (object)value : (float)value,
                composite.Arity)
            .ToList();

    private static string Offered(CompositeType composite)
    {
        var literals = new List<string>();

        if (composite.TakesZeroAndOne)
            literals.AddRange(new[] { "`zero`", "`one`" });

        if (composite.TakesIdentity)
            literals.Add("`identity`");

        if (composite.IsColor)
            literals.AddRange(new[] { "`#RRGGBB`", "a CSS colour name" });

        return literals.Count == 0
            ? $"Write its {composite.Arity} components as a tuple."
            : $"It takes {string.Join(", ", literals)}, or its {composite.Arity} components "
              + "as a tuple.";
    }

    // ------------------------------------------------------------------- colour

    private static int[] Unpack(uint packed) => new[]
    {
        (int)((packed >> 24) & 0xFF),
        (int)((packed >> 16) & 0xFF),
        (int)((packed >> 8) & 0xFF),
        (int)(packed & 0xFF),
    };

    /// <summary>
    /// Turns 8-bit components into whichever colour type the column declared.
    /// </summary>
    /// <remarks>
    /// `color32` takes them as they are. `color` divides by 255, which is exact enough to
    /// return the same 8-bit value when it is multiplied back: every n/255 has a distinct
    /// nearest `float`, and that is what the round-trip gate checks.
    /// </remarks>
    private static System.Array Color(CompositeType composite, int[] rgba)
    {
        if (composite.IsEightBitColor)
            return rgba;

        var result = new float[4];

        for (int at = 0; at < 4; at++)
            result[at] = rgba[at] / 255f;

        return result;
    }

    // ------------------------------------------------------------------- errors

    /// <summary>
    /// The report for a cell that did not hold the right number of components.
    /// </summary>
    /// <remarks>
    /// One component where several were expected gets the spreadsheet trap added to it. A
    /// general-format cell reads `(1,234)` as -1234 and `1,234` as 1234, so a tuple of whole
    /// numbers can arrive here as a single number - and an author looking at their sheet sees
    /// two components and no reason for the complaint. Naming the format is the only part of
    /// this report that leads anywhere.
    /// </remarks>
    private static TabbitException WrongComponentCount(
        CompositeType composite, string text, List<string> parts, Location? location)
    {
        string report =
            $"`{text}` holds {parts.Count} component{(parts.Count == 1 ? "" : "s")} and "
            + $"`{composite.Name}` takes {composite.Arity} "
            + $"({string.Join(", ", composite.Components)}).";

        if (parts.Count == 1 && LooksNumeric(text))
        {
            report += " A spreadsheet's general format rewrites `(1,234)` as -1234 and `1,234` "
                    + "as 1234, so a cell written with commas can arrive as one number. Format "
                    + "the column as text, or prefix the cell with `'`.";
        }
        else if (parts.Count == 1)
        {
            // A single token that is not a number is a name of some kind - a direction, a
            // colour, a literal misremembered - so what this type does accept is the part
            // worth saying.
            report += $" {Offered(composite)}";
        }

        return new TabbitException(location, report);
    }

    private static bool LooksNumeric(string text)
        => double.TryParse(text, DecimalStyles, CultureInfo.InvariantCulture, out _);

    // -------------------------------------------------------------------- packing

    private static System.Array Pack(CompositeType composite, List<object> values)
    {
        if (composite.ComponentType == ValueType.Int32)
            return values.Select(System.Convert.ToInt32).ToArray();

        return values.Select(System.Convert.ToSingle).ToArray();
    }
}
