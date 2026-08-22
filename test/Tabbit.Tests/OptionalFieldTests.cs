using Tabbit.Cooking;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// A trailing `?` on a field's type says a blank cell is expected there.
/// </summary>
/// <remarks>
/// Required is the default and always was - a blank cell in a number column has always been
/// an error - so the marker only ever loosens, never tightens. Which makes it additive for
/// every sheet that exists: nothing without a `?` changes behaviour.
///
/// The one place it must not loosen is the index, and that is most of what these tests are
/// for. `string` and `bool` are the other edge: they have always read a blank as `""` and
/// `false`, so `?` on them is a statement of intent rather than a change.
/// </remarks>
public class OptionalFieldTests
{
    private static CookingContext Context()
        => new CookingContext(new Tabbit.Models.Model(), new Tabbit.Recipe.RecipeModel(), new Diagnostics());

    private static Tabbit.Models.Location Where()
        => new Tabbit.Models.Location { Filename = "memory.xlsx", Sheet = "T", Column = 1, Row = 1 };

    [Theory]
    [InlineData("int", "int", true)]
    [InlineData("int?", "int", false)]
    [InlineData("string ?", "string", false)]
    // After the brackets, so this is an optional array rather than an array of optionals -
    // the elements of one delimited cell are all present or all absent together.
    [InlineData("int[]?", "int[]", false)]
    [InlineData("datetime?", "datetime", false)]
    public void The_marker_comes_off_the_end_of_the_type(string written, string type, bool required)
    {
        string bare = CookingContext.SplitOptionalMarker(written, out bool isRequired);

        Assert.Equal(type, bare);
        Assert.Equal(required, isRequired);
    }

    /// <summary>
    /// A marked type still has to name a real type.
    /// </summary>
    /// <remarks>
    /// The marker is stripped before the name is looked up, so `int?` resolves to the same
    /// <see cref="ValueType"/> as `int`. Nothing downstream of the parser sees the `?` -
    /// requiredness travels on the field, not in the type name.
    /// </remarks>
    [Fact]
    public void A_marked_type_resolves_to_the_bare_type()
    {
        var context = Context();
        var where = Where();

        Assert.True(context.IsValidTypeName("int?"));
        Assert.Equal(ValueType.Int32, context.ParseValueType("int?", where));
        Assert.Equal(ValueType.Int32Array, context.ParseValueType("int[]?", where));
    }

    [Fact]
    public void An_unmarked_blank_is_still_an_error_for_the_strict_types()
    {
        var context = Context();
        var where = Where();

        Assert.ThrowsAny<System.Exception>(
            () => context.ParseValue(ValueType.Int32, null, "", where, null));
    }

    [Theory]
    [InlineData(ValueType.Int32, 0)]
    [InlineData(ValueType.Int64, 0L)]
    [InlineData(ValueType.Float, 0f)]
    [InlineData(ValueType.Double, 0d)]
    [InlineData(ValueType.String, "")]
    [InlineData(ValueType.Bool, false)]
    public void A_marked_blank_reads_as_the_type_s_empty_value(ValueType type, object empty)
    {
        var context = Context();
        var where = Where();

        Assert.Equal(empty, context.ParseValue(type, null, "", where, null, required: false));
    }

    /// <summary>
    /// A cell with something in it parses the same whether or not the column is optional.
    /// </summary>
    [Fact]
    public void The_marker_only_governs_blank_cells()
    {
        var context = Context();
        var where = Where();

        Assert.Equal(7, context.ParseValue(ValueType.Int32, null, "7", where, null, required: false));
        Assert.ThrowsAny<System.Exception>(
            () => context.ParseValue(ValueType.Int32, null, "nonsense", where, null, required: false));
    }

    /// <summary>
    /// The index is never optional, whatever the type cell says.
    /// </summary>
    /// <remarks>
    /// It is what identifies a row and what every reference to the table resolves through, so
    /// a blank one has nothing to mean. Left alone, `int?` on the first column would hand every
    /// blank row the same index 0 and the failure would surface as duplicate keys - or, for a
    /// table with one such row, not at all.
    /// </remarks>
    [Fact]
    public void The_index_field_refuses_the_marker()
    {
        var context = Context();
        var table = ModelFactory.Table("T", new[] { ("Index", ValueType.Int32) });
        var field = table.Fields[0];

        // Required, which the first column is by default, passes the same check.
        context.CheckPrimaryIndexValidity(field);

        field.IsRequired = false;

        var error = Assert.Throws<TabbitException>(() => context.CheckPrimaryIndexValidity(field));
        Assert.Equal(Tabbit.Cooking.CookingMessages.IndexFieldOptional, error.MessageId);
    }
}
