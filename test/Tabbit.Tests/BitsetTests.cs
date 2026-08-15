using Tabbit.Cooking;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// `bitset` - up to 64 flags - and the `0x` / `0b` literals the numeric types read.
/// </summary>
/// <remarks>
/// **The refusals are the content of this type.** What separates `bitset` from `bigint` is
/// not what it holds - both are 64 bits - but what it declines to read: a bit pattern has no
/// sign, no thousands separator and no fractional part, so each of those is a mistake rather
/// than a notation to accommodate. A `bigint` cannot say that, because a magnitude may
/// legitimately carry all three.
///
/// So most of what follows asserts an error and the sentence it comes with. A test that only
/// checked the accepted values would pass against a type that accepted everything.
///
/// spec/bitset.md has the notation table and why this is a type rather than a role.
/// </remarks>
public class BitsetTests
{
    private static CookingContext Context()
        => new CookingContext(new Tabbit.Models.Model(), new Tabbit.Recipe.RecipeModel());

    private static Tabbit.Models.Location Where()
        => new Tabbit.Models.Location { Filename = "memory.xlsx", Sheet = "T", Column = 1, Row = 1 };

    private static long Bitset(string cell)
        => (long)Context().ParseValue(ValueType.Bitset, null, cell, Where());

    private static string Refusal(ValueType type, string cell)
        => Assert.Throws<TabbitException>(
            () => Context().ParseValue(type, null, cell, Where())).Message;

    // ------------------------------------------------------------ the type row

    [Theory]
    [InlineData("bitset")]
    [InlineData("bitset?")]
    [InlineData("bitset[]")]
    [InlineData("bitset[]?")]
    public void The_type_row_takes_the_name(string written)
        => Assert.True(Context().IsValidTypeName(written), $"`{written}` was not recognized.");

    [Fact]
    public void The_name_resolves_to_its_own_type()
    {
        Assert.Equal(ValueType.Bitset, Context().ParseValueType("bitset", Where()));
        Assert.Equal(ValueType.BitsetArray, Context().ParseValueType("bitset[]", Where()));
    }

    // ------------------------------------------------------------- what it reads

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("123", 123L)]
    [InlineData("0x1f", 31L)]
    [InlineData("0X1F", 31L)]
    [InlineData("0b1011", 11L)]
    [InlineData("0B1011", 11L)]
    // 2^53 exactly - the largest decimal a spreadsheet's numeric cell still holds without
    // having rounded it. One more is refused below.
    [InlineData("9007199254740992", 9007199254740992L)]
    public void Accepted_notations(string cell, long value)
        => Assert.Equal(value, Bitset(cell));

    /// <summary>
    /// The one type whose value is a pattern rather than a magnitude, so it reaches bit 63.
    /// </summary>
    /// <remarks>
    /// Decimal cannot: `9223372036854775808` is outside a signed 64-bit value, and the wire
    /// carries the pattern as the signed integer it shares its bits with.
    /// </remarks>
    [Fact]
    public void A_pattern_may_fill_all_sixty_four_bits()
    {
        Assert.Equal(-1L, Bitset("0xFFFFFFFFFFFFFFFF"));
        Assert.Equal(long.MinValue, Bitset("0x8000000000000000"));
        Assert.Equal(-1L, Bitset("0b" + new string('1', 64)));
    }

    [Fact]
    public void The_array_form_reads_each_element_by_the_same_rules()
    {
        var values = (long[])Context().ParseValue(ValueType.BitsetArray, null, "0x1;0b10;3", Where());

        Assert.Equal(new[] { 1L, 2L, 3L }, values);
    }

    [Fact]
    public void A_blank_is_no_flags_where_the_column_says_a_blank_is_expected()
    {
        Assert.Equal(0L, (long)Context().ParseValue(ValueType.Bitset, null, "", Where(), required: false));

        // And where it does not, the report says how to ask for one.
        Assert.Contains("bitset?", Refusal(ValueType.Bitset, ""));
    }

    // ----------------------------------------------------------- what it refuses

    /// <summary>
    /// Each refusal names the character that says so, because a cell full of digits with one
    /// comma in it is not something an author finds by re-reading.
    /// </summary>
    [Theory]
    // `1.0` goes with `1.5`. "The fractional part is zero, so allow it" is where the
    // ambiguity starts, and an ambiguous rule is worse than a strict one.
    [InlineData("1.5", "decimal point")]
    [InlineData("1.0", "decimal point")]
    [InlineData("-1", "sign")]
    [InlineData("+1", "sign")]
    [InlineData("-0x10", "sign")]
    [InlineData("1,000", "thousands separator")]
    [InlineData("1e3", "exponent notation")]
    [InlineData("0b1012", "base-2 digit")]
    [InlineData("0b1010_1010", "base-2 digit")]
    [InlineData("0xZZ", "base-16 digit")]
    [InlineData("0x", "decimal digit")]
    public void Refused_notations(string cell, string reason)
    {
        string message = Refusal(ValueType.Bitset, cell);

        Assert.Contains(reason, message);
        Assert.Contains(cell, message);
    }

    [Theory]
    [InlineData("0x1FFFFFFFFFFFFFFFF", "at most 16")]
    public void A_literal_wider_than_sixty_four_bits_is_refused(string cell, string reason)
        => Assert.Contains(reason, Refusal(ValueType.Bitset, cell));

    [Fact]
    public void More_than_sixty_four_binary_digits_is_refused()
        => Assert.Contains("at most 64", Refusal(ValueType.Bitset, "0b" + new string('1', 65)));

    /// <summary>
    /// A decimal above 2^53 has already been rounded before anything here sees it.
    /// </summary>
    /// <remarks>
    /// A spreadsheet holds a numeric cell as a double, and the importer renders it round
    /// trip: `9007199254740993` arrives as `9007199254740992`. Refusing the range costs no
    /// expressible value, because a numeric cell cannot carry those in the first place - and
    /// the report says which notation can.
    /// </remarks>
    [Fact]
    public void A_decimal_above_the_mantissa_is_refused_rather_than_rounded()
    {
        string message = Refusal(ValueType.Bitset, "9007199254740993");

        Assert.Contains("2^53", message);
        Assert.Contains("0x", message);
    }

    // ------------------------------------------------- radix on the numeric types

    /// <summary>
    /// `0x` and `0b` are notation rather than a type, so the numeric types read them too.
    /// </summary>
    /// <remarks>
    /// `float` and `double` included. A layout that does not narrow its number columns
    /// widens them to `double`, so a rule stopping at the integers would miss those columns
    /// in the configuration that is the default one - and colour values, which is where
    /// these literals mostly are, sit in exactly them.
    /// </remarks>
    [Fact]
    public void Radix_literals_reach_the_numeric_types()
    {
        var context = Context();

        Assert.Equal(31, (int)context.ParseValue(ValueType.Int32, null, "0x1f", Where()));
        Assert.Equal(-16, (int)context.ParseValue(ValueType.Int32, null, "-0x10", Where()));
        Assert.Equal(4294967295L, (long)context.ParseValue(ValueType.Int64, null, "0xFFFFFFFF", Where()));
        Assert.Equal(16d, (double)context.ParseValue(ValueType.Double, null, "0x10", Where()));
        Assert.Equal(11f, (float)context.ParseValue(ValueType.Float, null, "0b1011", Where()));
    }

    /// <summary>
    /// The base does not widen the type. A column that means a 32-bit pattern is a `bitset`.
    /// </summary>
    [Fact]
    public void A_radix_literal_stays_inside_the_type_it_is_written_in()
    {
        string message = Refusal(ValueType.Int32, "0xFFFFFFFF");

        // The report names what the sheet holds, not the decimal it was rewritten to.
        Assert.Contains("0xFFFFFFFF", message);
        Assert.DoesNotContain("4294967295", message);
    }

    /// <summary>
    /// A float column takes the literal only where it holds the integer exactly.
    /// </summary>
    /// <remarks>
    /// Above the mantissa the value reads back as a neighbouring one and nothing downstream
    /// would say so - the same silent failure the whole-number encoding checks for before it
    /// carries a float column as integers.
    /// </remarks>
    [Theory]
    [InlineData(ValueType.Float, "0x1000001")]
    [InlineData(ValueType.Double, "0x20000000000001")]
    public void A_literal_a_float_cannot_hold_exactly_is_refused(ValueType type, string cell)
        => Assert.Contains("read back as a different value", Refusal(type, cell));

    /// <summary>
    /// A sign is part of the number rather than the base, so a magnitude keeps it.
    /// </summary>
    [Fact]
    public void A_sign_is_refused_only_where_a_sign_has_no_meaning()
    {
        Assert.Equal(-31, (int)Context().ParseValue(ValueType.Int32, null, "-0x1f", Where()));
        Assert.Contains("sign", Refusal(ValueType.Bitset, "-0x1f"));
    }

    // ------------------------------------------------------------------ the fold

    /// <summary>
    /// A `bitset` column and a `bigint` column holding the same values arrive as the same
    /// column.
    /// </summary>
    /// <remarks>
    /// The fixture writes each row's value twice - once in whichever notation the row is
    /// named for, once as a plain decimal - so this compares the two columns against each
    /// other rather than against a list of expected numbers. A fold that did not happen shows
    /// up as a difference between them, and a notation read wrongly shows up the same way.
    ///
    /// The golden tree covers the rest: the wire, and three languages each spelling the width
    /// their own way.
    /// </remarks>
    [Fact]
    public void A_bitset_column_and_a_bigint_column_hold_the_same_value()
    {
        var result = TabbitRunner.Convert("bitset");
        Assert.True(result.Succeeded, $"Conversion failed.{System.Environment.NewLine}{result.Describe()}");

        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(
            RepoLayout.OutputDir("bitset"), "json-named", "Flags.json"));

        var rows = System.Text.Json.JsonDocument.Parse(json).RootElement;

        Assert.Equal(4, rows.GetArrayLength());

        foreach (var row in rows.EnumerateArray())
        {
            Assert.Equal(
                row.GetProperty("same").GetString(),
                row.GetProperty("mask").GetString());
        }

        // And the row decimal cannot express, which is the reason the pattern notations are
        // in the type at all.
        Assert.Equal("-1", rows[3].GetProperty("mask").GetString());
    }
}
