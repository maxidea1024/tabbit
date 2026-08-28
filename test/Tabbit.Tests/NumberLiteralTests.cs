using Tabbit.Cooking;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// What a number may be written as in a cell.
/// </summary>
/// <remarks>
/// The notation is C#'s, minus the suffixes - digit separators, exponents and `0x` / `0b`
/// literals - so most of what follows is a case C# already answers, asserted here because
/// nothing else in this repository would notice if the answer changed.
///
/// The one place the two differ is section 5 of the spec: an integer column reads `1e3` as
/// 1000, where C# would want a cast. The tests around <see cref="An_integer_column"/> are
/// what pin that down, including the half of it that refuses.
///
/// spec/types/number-literals.md.
/// </remarks>
public class NumberLiteralTests
{
    private static CookingContext Context()
        => new CookingContext(
            new Tabbit.Models.Model(), new Tabbit.Recipe.RecipeModel(), new Diagnostics());

    private static Tabbit.Models.Location Where()
        => new Tabbit.Models.Location { Filename = "memory.xlsx", Sheet = "T", Column = 1, Row = 1 };

    private static object Read(ValueType type, string cell)
        => Context().ParseValue(type, null, cell, Where());

    private static string Refusal(ValueType type, string cell)
        => Assert.Throws<TabbitException>(() => Read(type, cell)).Message;

    // ------------------------------------------------------- the digit separator

    [Theory]
    [InlineData("1_000", 1000)]
    [InlineData("1_000_000", 1000000)]
    [InlineData("1__0", 10)]
    [InlineData("-1_0", -10)]
    [InlineData("0xFF_FF", 65535)]
    [InlineData("0b_1010_1010", 170)]
    [InlineData("0x_FF", 255)]
    public void A_separator_between_digits_is_removed(string cell, int expected)
        => Assert.Equal(expected, Read(ValueType.Int32, cell));

    [Theory]
    [InlineData("3.141_592", 3.141592)]
    [InlineData("1_0.0_1", 10.01)]
    [InlineData("1_0e1_0", 1e11)]
    public void A_real_column_reads_them_too(string cell, double expected)
        => Assert.Equal(expected, (double)Read(ValueType.Double, cell), 9);

    /// <summary>
    /// Where the separator is not between two digits.
    /// </summary>
    /// <remarks>
    /// Every one of these is a compile error in C# as well. Accepting them would cost
    /// nothing in values - each names the number it looks like - but the rule then has no
    /// edge, and `1000_` in a sheet is a cell somebody stopped halfway through editing.
    /// </remarks>
    [Theory]
    [InlineData("_1000")]
    [InlineData("1000_")]
    [InlineData("1_.0")]
    [InlineData("1._0")]
    [InlineData("1e_5")]
    [InlineData("1_e5")]
    [InlineData("0x_")]
    [InlineData("_")]
    public void A_separator_anywhere_else_is_refused(string cell)
        => Assert.Contains("not between two digits", Refusal(ValueType.Double, cell));

    /// <summary>
    /// Both spellings in one cell.
    /// </summary>
    /// <remarks>
    /// `1,000_000` is a million under either reading, so this refuses nothing about the
    /// value. What it refuses is a cell where the author was writing two notations at once,
    /// which is the state a half-finished edit leaves behind.
    /// </remarks>
    [Fact]
    public void The_two_separators_are_not_mixed()
    {
        string message = Refusal(ValueType.Int32, "1,000_000");

        Assert.Contains("both", message);
        Assert.Contains("1,000_000", message);
    }

    /// <summary>The thousands separator alone still reads, as it did before any of this.</summary>
    [Fact]
    public void The_thousands_separator_is_untouched()
        => Assert.Equal(1000000, Read(ValueType.Int32, "1,000,000"));

    // ------------------------------------------------------------ the exponent

    /// <summary>
    /// An integer column reading a literal with an exponent or a point.
    /// </summary>
    /// <remarks>
    /// The departure from C#, where `int x = 1e3;` is an error. A spreadsheet writes large
    /// numbers as `1E+15` on its own, so a column refusing that would be refusing a cell
    /// nobody typed - and a type row has nowhere to write the cast that says the loss is
    /// intended, so a loss is answered with a message instead.
    /// </remarks>
    [Theory]
    [InlineData("1e3", 1000)]
    [InlineData("1E3", 1000)]
    [InlineData("1E+3", 1000)]
    [InlineData("1.5e3", 1500)]
    [InlineData("1.0", 1)]
    [InlineData("-2.5e2", -250)]
    [InlineData("0e-3", 0)]
    [InlineData("1000e-3", 1)]
    [InlineData("0.0", 0)]
    [InlineData("-0.0", 0)]
    public void An_integer_column(string cell, int expected)
        => Assert.Equal(expected, Read(ValueType.Int32, cell));

    /// <summary>
    /// The digits are shifted rather than converted, so nothing passes through a `double`.
    /// </summary>
    /// <remarks>
    /// `1e17` is above where a `double` counts by ones. Reading it through one would give a
    /// value near the right one and nothing downstream would say which - so this asserts the
    /// exact integer, which is what the shift produces and what a conversion would not.
    /// </remarks>
    [Fact]
    public void A_large_exponent_stays_exact()
    {
        Assert.Equal(100000000000000000L, Read(ValueType.Int64, "1e17"));
        Assert.Equal(123456789012345678L, Read(ValueType.Int64, "123456789012345678"));
    }

    [Theory]
    [InlineData("1e-3")]
    [InlineData("1.5")]
    [InlineData("2.5e-1")]
    public void A_value_with_a_fraction_is_refused_by_an_integer_column(string cell)
        => Assert.Contains("fractional part", Refusal(ValueType.Int32, cell));

    /// <summary>An exponent naming a value no integer type holds.</summary>
    [Fact]
    public void An_exponent_out_of_range_overflows()
    {
        Assert.Contains("Cannot parse", Refusal(ValueType.Int32, "1e30"));
        Assert.Contains("Cannot parse", Refusal(ValueType.Int64, "1e300"));

        // The bound the shift stops building at. Without it this allocates the string
        // rather than answering, which is a hang and not a message.
        Assert.Contains("Cannot parse", Refusal(ValueType.Int64, "1e2000000000"));
    }

    /// <summary>A real column still reads an exponent the way it always did.</summary>
    [Fact]
    public void A_real_column_keeps_its_fraction()
    {
        Assert.Equal(0.001, (double)Read(ValueType.Double, "1e-3"), 9);
        Assert.Equal(1.5, (double)Read(ValueType.Double, "1.5"), 9);
    }

    // -------------------------------------------------------------- what is not read

    /// <summary>
    /// The suffixes, which this notation leaves out.
    /// </summary>
    /// <remarks>
    /// The type row already says what type a column is, so a suffix would only give an
    /// author a second place to say it and a way to disagree with the first.
    /// </remarks>
    [Theory]
    [InlineData(ValueType.Float, "1.5f")]
    [InlineData(ValueType.Double, "1.5d")]
    [InlineData(ValueType.Int64, "100L")]
    [InlineData(ValueType.Int32, "0xFFu")]
    public void A_suffix_is_not_part_of_a_literal(ValueType type, string cell)
        => Assert.Throws<TabbitException>(() => Read(type, cell));

    [Theory]
    [InlineData("0x1.8p3")]
    [InlineData("0o777")]
    public void Notations_C_sharp_does_not_have(string cell)
        => Assert.Throws<TabbitException>(() => Read(ValueType.Double, cell));

    // ------------------------------------------------------------------ bitset

    /// <summary>
    /// The reversal: a flag column takes the separator it has the most use for.
    /// </summary>
    /// <remarks>
    /// The refusal used to be documented as deliberate, on the grounds that a bit pattern
    /// has no use for the notation. That was wrong about which notation - `0b1010_1010` is
    /// how a mask is written where it is written at all. spec/types/number-literals.md
    /// section 7.
    /// </remarks>
    [Theory]
    [InlineData("0b1010_1010", 170L)]
    [InlineData("0xFF_FF", 65535L)]
    [InlineData("1_000", 1000L)]
    [InlineData("0b_1111", 15L)]
    public void A_bitset_reads_the_separator(string cell, long expected)
        => Assert.Equal(expected, Read(ValueType.Bitset, cell));

    /// <summary>
    /// And keeps every other refusal.
    /// </summary>
    /// <remarks>
    /// The separator widens what is accepted without giving any cell a second reading. Each
    /// of these does, which is why the reversal reaches none of them.
    /// </remarks>
    [Theory]
    [InlineData("-1")]
    [InlineData("1,000")]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    public void A_bitset_keeps_its_other_refusals(string cell)
        => Assert.Throws<TabbitException>(() => Read(ValueType.Bitset, cell));

    /// <summary>A misplaced separator in a flag column is reported as one.</summary>
    /// <remarks>
    /// The character loop this type reads its decimals with would otherwise call `_` a
    /// character that is not a digit, which names the wrong thing about `1000_`.
    /// </remarks>
    [Fact]
    public void A_misplaced_separator_in_a_bitset_says_so()
        => Assert.Contains("not between two digits", Refusal(ValueType.Bitset, "1000_"));

    // --------------------------------------------------------------- composites

    /// <summary>
    /// A component of a composite cell reads the separator, and not the whole-number rule.
    /// </summary>
    /// <remarks>
    /// `color32` refuses `(1.0, 1.0, 1.0)` because whether that is white or three 255ths is
    /// the ambiguity the type exists to refuse. Reading `1.0` as `1` there would answer it
    /// on the author's behalf, so the component path deliberately leaves that reading out.
    /// </remarks>
    [Fact]
    public void A_component_takes_separators_and_not_the_whole_number_reading()
    {
        var colour = Read(ValueType.Color32, "(0xFF, 0x8_0, 64)");
        Assert.NotNull(colour);

        Assert.Throws<TabbitException>(() => Read(ValueType.Color32, "(1.0, 1.0, 1.0)"));
    }

    // -------------------------------------------------------------------- arrays

    /// <summary>Each element of an array cell is a literal of its own.</summary>
    [Fact]
    public void Array_elements_read_the_same_notation()
    {
        var values = (int[])Read(ValueType.Int32Array, "1_000; 0xF_F; 2e3");

        Assert.Equal(new[] { 1000, 255, 2000 }, values);
    }
}
