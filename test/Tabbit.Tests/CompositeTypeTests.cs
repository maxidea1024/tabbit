using System.Linq;
using Tabbit.Cooking;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The composite types - vectors, rotations and colours - and the notation they read.
/// </summary>
/// <remarks>
/// **The refusals are the content of these types.** Three `float` columns already held a
/// position; what a `vec3f` column adds is that the sheet says so, and everything that follows
/// from saying so is a refusal: a tuple of the wrong length, a decimal point where an 8-bit
/// colour belongs, a direction name whose value depends on which engine reads it. A test that
/// only checked the accepted values would pass against a type that accepted everything.
///
/// spec/types/composite-value-types.md has the notation tables and the reasoning.
/// </remarks>
public class CompositeTypeTests
{
    private static CookingContext Context()
        => new CookingContext(new Model(), new Tabbit.Recipe.RecipeModel(), new Diagnostics());

    private static Location Where()
        => new Location { Filename = "memory.xlsx", Sheet = "T", Column = 1, Row = 1 };

    private static float[] Floats(string typeName, string cell)
        => (float[])Context().ParseValue(
            Context().ParseValueType(typeName, Where()), null, cell, Where())!;

    private static int[] Ints(string typeName, string cell)
        => (int[])Context().ParseValue(
            Context().ParseValueType(typeName, Where()), null, cell, Where())!;

    private static string Refusal(string typeName, string cell)
    {
        var context = Context();
        var type = context.ParseValueType(typeName, Where());

        return Assert.Throws<TabbitException>(
            () => context.ParseValue(type, null, cell, Where())).Message;
    }

    // ------------------------------------------------------------ the type row

    [Theory]
    [InlineData("vec2i")]
    [InlineData("vec3i")]
    [InlineData("vec4i")]
    [InlineData("vec2f")]
    [InlineData("vec3f")]
    [InlineData("vec4f")]
    [InlineData("euler")]
    [InlineData("quat")]
    [InlineData("axisangle")]
    [InlineData("color")]
    [InlineData("color32")]
    public void The_type_row_takes_every_name(string written)
    {
        Assert.True(Context().IsValidTypeName(written), $"`{written}` was not recognized.");
        Assert.True(Context().IsValidTypeName(written + "?"), $"`{written}?` was not recognized.");
    }

    /// <summary>
    /// The type row takes the canonical name only, even where a literal takes an alias.
    /// </summary>
    /// <remarks>
    /// `Vector2i.one` is a cell writing what the engine calls the type, and refusing it there
    /// would be refusing the spelling the author came for. A type row is a different place: it
    /// is where types are declared, and a type has one name there.
    /// </remarks>
    [Fact]
    public void An_engine_spelling_is_not_a_type_row_name()
        => Assert.False(Context().IsValidTypeName("vector2i"));

    /// <summary>
    /// A type row's name is matched exactly, so an enum keeps a name a composite also has.
    /// </summary>
    /// <remarks>
    /// The composite names are resolved before the enum declarations are searched. A
    /// case-insensitive match here would take `Color` away from a sheet that declares an enum
    /// by that name and has been converting for years - and it would do it by resolving the
    /// column to a different type rather than by reporting anything.
    /// </remarks>
    [Fact]
    public void An_enum_may_still_be_called_Color()
    {
        Assert.NotNull(CompositeTypes.ByName("color"));
        Assert.Null(CompositeTypes.ByName("Color"));
        Assert.Null(CompositeTypes.ByName("Vec3f"));

        // The literal prefix is cell notation rather than a declaration, and stays lenient.
        Assert.NotNull(CompositeTypes.BySpelling("Vector2i"));
    }

    /// <summary>
    /// One cell holding a list of them is not a shape this revision carries.
    /// </summary>
    /// <remarks>
    /// Refused by the check every unbracketable type is refused by, rather than by a case of
    /// its own: `ArrayOf` has no answer for a composite. spec/types/composite-value-types.md
    /// section 9 has why the shape is left out.
    /// </remarks>
    [Fact]
    public void The_array_form_is_refused()
    {
        Assert.Contains(
            "cannot be used as an array element",
            Assert.Throws<TabbitException>(
                () => Context().ParseValueType("vec2f[]", Where())).Message);
    }

    // ----------------------------------------------------------------- tuples

    [Theory]
    [InlineData("(111, 222)")]
    [InlineData("111,222")]
    [InlineData("  ( 111 , 222 )  ")]
    public void A_tuple_reads_with_or_without_its_parentheses(string cell)
        => Assert.Equal(new[] { 111, 222 }, Ints("vec2i", cell));

    /// <summary>
    /// A component is a value of its own type, so it reads that type's notation.
    /// </summary>
    [Fact]
    public void Components_read_radix_literals()
        => Assert.Equal(new[] { 255, 128, 64 }, Ints("vec3i", "(0xFF, 0x80, 0b1000000)"));

    [Fact]
    public void A_float_tuple_reads_signs_and_decimals()
        => Assert.Equal(new[] { 1.5f, -2.5f, 0f }, Floats("vec3f", "(1.5,-2.5,0)"));

    [Fact]
    public void An_angle_triple_is_three_floats()
        => Assert.Equal(new[] { 0f, 90f, 0f }, Floats("euler", "(0, 90, 0)"));

    // ------------------------------------------------------- refused tuples

    /// <summary>
    /// The count is exact. Filling a missing component in with zero would make "left it out"
    /// and "wrote zero" the same cell.
    /// </summary>
    [Theory]
    [InlineData("vec3f", "(1, 2)")]
    [InlineData("vec2i", "(1, 2, 3)")]
    [InlineData("quat", "(0, 0, 1)")]
    public void A_tuple_of_the_wrong_length_is_refused(string typeName, string cell)
    {
        string message = Refusal(typeName, cell);

        Assert.Contains("component", message);
        Assert.Contains(typeName, message);
    }

    [Fact]
    public void An_empty_component_is_refused()
        => Assert.Contains("empty", Refusal("vec2i", "(1,)"));

    [Fact]
    public void A_half_written_pair_of_parentheses_is_refused()
        => Assert.Contains("parenthesis", Refusal("vec2i", "(1, 2"));

    /// <summary>
    /// The spreadsheet trap, and the one report that leads anywhere.
    /// </summary>
    /// <remarks>
    /// A general-format cell reads `(1,234)` as the accounting notation for -1234 and `1,234`
    /// as a thousands-separated 1234, so a two-component cell can arrive here as one number.
    /// It is always caught - every composite has at least two components - but the count
    /// alone would send an author back to a sheet that plainly shows two. Naming the format
    /// is what makes the report actionable. spec/types/composite-value-types.md section 8.
    /// </remarks>
    [Theory]
    [InlineData("-1234")]
    [InlineData("1234")]
    public void A_cell_the_spreadsheet_rewrote_names_the_format(string arrived)
    {
        string message = Refusal("vec2i", arrived);

        Assert.Contains("general format", message);
        Assert.Contains("text", message);
    }

    // ------------------------------------------------------- symbolic literals

    [Fact]
    public void Zero_and_one_name_a_vector()
    {
        Assert.Equal(new[] { 0, 0 }, Ints("vec2i", "zero"));
        Assert.Equal(new[] { 1, 1, 1 }, Ints("vec3i", "one"));
        Assert.Equal(new[] { 1f, 1f }, Floats("vec2f", "ONE"));
    }

    /// <summary>
    /// The qualified form, in the type's own name and in the engine's.
    /// </summary>
    [Theory]
    [InlineData("vec2i.one")]
    [InlineData("Vector2i.one")]
    [InlineData("VECTOR2I.ONE")]
    [InlineData("int2.one")]
    public void A_literal_may_name_its_type(string cell)
        => Assert.Equal(new[] { 1, 1 }, Ints("vec2i", cell));

    /// <summary>
    /// A prefix naming a different type is a cell edited against the wrong column.
    /// </summary>
    [Fact]
    public void A_prefix_that_names_another_type_is_refused()
    {
        string message = Refusal("vec2i", "vec3f.one");

        Assert.Contains("vec3f", message);
        Assert.Contains("vec2i", message);
    }

    [Fact]
    public void Identity_names_the_rotation_that_turns_nothing()
    {
        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, Floats("quat", "identity"));
        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, Floats("quat", "Quaternion.identity"));
        Assert.Equal(new[] { 0f, 0f, 1f, 0f }, Floats("axisangle", "identity"));
    }

    /// <summary>
    /// Directions are not offered, because their values differ between engines.
    /// </summary>
    /// <remarks>
    /// Unity's up is `+Y` and Unreal's is `+Z`. A core that picked one would be silently
    /// wrong for every project using the other, and three components that are all zero and
    /// one are exactly the kind of value a diff never questions.
    /// spec/types/composite-value-types.md section 5.
    /// </remarks>
    [Theory]
    [InlineData("up")]
    [InlineData("forward")]
    [InlineData("right")]
    public void A_direction_name_is_not_a_literal(string cell)
        => Assert.Contains("component", Refusal("vec3f", cell));

    /// <summary>
    /// A colour's `one` would be white to one colour type and near-black to the other.
    /// </summary>
    [Theory]
    [InlineData("color")]
    [InlineData("color32")]
    public void A_colour_does_not_take_zero_and_one(string typeName)
    {
        Assert.Contains("CSS colour name", Refusal(typeName, "one"));
        Assert.Contains("CSS colour name", Refusal(typeName, "zero"));

        // And the names it does take say the same things without the ambiguity.
        var context = Context();
        var white = (System.Array)context.ParseValue(
            context.ParseValueType(typeName, Where()), null, "white", Where())!;

        Assert.Equal(4, white.Length);
    }

    // ------------------------------------------------------------------ colour

    [Theory]
    [InlineData("#39C")]
    [InlineData("#3399CC")]
    [InlineData("#3399CCFF")]
    [InlineData("0x3399CC")]
    [InlineData("(51, 153, 204)")]
    [InlineData("(51, 153, 204, 255)")]
    public void Every_notation_reaches_the_same_eight_bit_colour(string cell)
        => Assert.Equal(new[] { 51, 153, 204, 255 }, Ints("color32", cell));

    [Fact]
    public void A_short_hex_repeats_each_digit_rather_than_padding_it()
        => Assert.Equal(new[] { 255, 0, 0, 255 }, Ints("color32", "#F00"));

    [Fact]
    public void A_name_reaches_both_colour_types()
    {
        Assert.Equal(new[] { 100, 149, 237, 255 }, Ints("color32", "cornflowerblue"));
        Assert.Equal(new[] { 1f, 1f, 1f, 1f }, Floats("color", "White"));
        Assert.Equal(new[] { 0f, 0f, 0f, 0f }, Floats("color", "transparent"));
    }

    /// <summary>
    /// The float form is the 8-bit one divided by 255, and it divides back exactly.
    /// </summary>
    /// <remarks>
    /// Every n/255 has its own nearest `float`, so a colour written as hex and read as a
    /// `color` returns the byte it was written as. This is what lets the two types carry the
    /// same palette without one of them rounding.
    /// </remarks>
    [Fact]
    public void The_float_colour_returns_the_byte_it_came_from()
    {
        for (int n = 0; n <= 255; n++)
        {
            var asFloat = Floats("color", $"#{n:X2}{n:X2}{n:X2}");
            var asBytes = Ints("color32", $"#{n:X2}{n:X2}{n:X2}");

            Assert.Equal(n, asBytes[0]);
            Assert.Equal(n, (int)System.Math.Round(asFloat[0] * 255f));
        }
    }

    /// <summary>
    /// `color` is unbounded because a colour above 1 is an HDR colour, and `color32` is not
    /// because 8 bits has no value above 255 to carry.
    /// </summary>
    [Fact]
    public void Only_the_eight_bit_colour_has_a_range()
    {
        Assert.Equal(new[] { 4f, 2f, 1f, 1f }, Floats("color", "(4, 2, 1)"));

        string message = Refusal("color32", "(256, 0, 0)");
        Assert.Contains("0 to 255", message);
    }

    /// <summary>
    /// A decimal point says the value is not 8-bit, so the column is the other colour type.
    /// </summary>
    /// <remarks>
    /// `(1.0, 1.0, 1.0)` is white to `color` and three 255ths to `color32`, which is the whole
    /// reason this is refused rather than rounded. The same ground `bitset` refuses `1.0` on.
    /// </remarks>
    [Fact]
    public void A_decimal_point_in_an_eight_bit_colour_is_refused()
    {
        string message = Refusal("color32", "(1.0, 1.0, 1.0)");

        Assert.Contains("decimal point", message);
        Assert.Contains("color", message);
    }

    [Fact]
    public void An_unknown_colour_name_says_where_names_come_from()
    {
        string message = Refusal("color", "brand-primary");

        Assert.Contains("CSS colour name", message);
        Assert.Contains("palette", message);
    }

    [Fact]
    public void An_undeclared_palette_and_a_missing_colour_are_separate_reports()
    {
        Assert.Contains("No palette called `material`", Refusal("color", "material.blue.500"));
        Assert.Contains("has no colour called", Refusal("color", "css.brandblue"));
    }

    /// <summary>
    /// The built-in palette is the CSS Color Module Level 4 list plus `transparent`.
    /// </summary>
    [Fact]
    public void The_built_in_palette_holds_the_css_colours()
    {
        // 148 named colours, and `transparent`, which is a keyword rather than one of them.
        Assert.Equal(149, ColorPalettes.BuiltInColorCount);

        Assert.Equal(new[] { 102, 51, 153, 255 }, Ints("color32", "rebeccapurple"));
        Assert.Equal(new[] { 255, 99, 71, 255 }, Ints("color32", "tomato"));
    }

    // ------------------------------------------------------------- empty values

    /// <summary>
    /// A blank cell in an optional column, which is zero for most and deliberately not for
    /// three.
    /// </summary>
    [Fact]
    public void The_empty_value_of_a_rotation_is_a_rotation()
    {
        var context = Context();

        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, (float[])context.ParseValue(
            ValueType.Quat, null, "", Where(), required: false)!);

        Assert.Equal(new[] { 0f, 0f, 1f, 0f }, (float[])context.ParseValue(
            ValueType.AxisAngle, null, "", Where(), required: false)!);

        // Transparent rather than opaque black: a cell nobody filled in showing up as a black
        // rectangle is a value, and showing up as nothing is the absence it was.
        Assert.Equal(new[] { 0f, 0f, 0f, 0f }, (float[])context.ParseValue(
            ValueType.Color, null, "", Where(), required: false)!);
    }

    [Fact]
    public void A_blank_in_a_required_column_says_how_to_allow_one()
        => Assert.Contains("vec2i?", Refusal("vec2i", ""));

    // ---------------------------------------------------------------- index keys

    /// <summary>
    /// Refused against the name the sheet used, rather than against one of the components it
    /// would have become.
    /// </summary>
    [Fact]
    public void A_composite_cannot_be_an_index_key()
    {
        Assert.False(ValueTypes.CanBeIndexKey(ValueType.Vec2i, out string why));
        Assert.Contains("several components", why);
    }

    // ------------------------------------------------------------------- shape

    /// <summary>
    /// Every type's component names are the ones the generated record will carry.
    /// </summary>
    [Fact]
    public void Component_names_are_declared_once_and_are_identifiers()
    {
        foreach (var composite in CompositeTypes.All)
        {
            Assert.InRange(composite.Arity, 2, 4);
            Assert.Equal(composite.Arity, composite.Components.Distinct().Count());
            Assert.Equal(composite.Arity, composite.EmptyComponents.Count);

            foreach (string component in composite.Components)
                Assert.Matches("^[A-Z][A-Za-z]*$", component);
        }
    }
    /// <summary>
    /// A tagged table holding a composite column is refused, and the report says why.
    /// </summary>
    /// <remarks>
    /// **The ordering is the whole of this one.** A composite becomes one column per
    /// component, so a table writing its tags out needs a tag per component - and that report
    /// is collected rather than thrown. The expansion then used to carry on: clear the tags
    /// the sheet wrote, ask for them to be assigned again, and throw about the `#`-excluded
    /// column reserving a tag while no live field carries one.
    ///
    /// Both sentences are about wire tags and only one is about the sheet. The second is true
    /// of the table this tool had just built - it had cleared those tags itself - and it
    /// aborted the run before the first was ever printed, sending the author to a `#` column
    /// they wrote correctly.
    ///
    /// So the fixture holds both at once, and this asserts which one speaks.
    /// spec/types/composite-value-types.md section 6.
    /// </remarks>
    [Fact]
    public void A_tagged_table_holding_a_composite_column_is_refused_by_name()
    {
        var result = TabbitRunner.Convert("composite-tagged");

        Assert.False(result.Succeeded, "A tagged table with a composite column was accepted.");

        // The cause, naming the column and what it expands into.
        Assert.Contains("`Spot` is `vec2i`", result.StdOut);
        Assert.Contains("one column per component (X, Y)", result.StdOut);

        // And not the tombstone, which is about tags this tool cleared rather than tags the
        // sheet left off.
        Assert.DoesNotContain("no live field carries one", result.StdOut);
    }

}
