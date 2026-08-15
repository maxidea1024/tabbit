using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// A primary index that is a string.
/// </summary>
/// <remarks>
/// The generated lookup is a dictionary over the index field's own type, so nothing about
/// this is new below it - the secondary indexes have accepted strings all along.
///
/// The `string-index` golden holds the generated lookup and the bytes. Being pointed at is
/// `ReferenceKeyTests`, which used to live here as the refusal this could not get past.
/// </remarks>
public class StringIndexTests
{
    [Fact]
    public void A_string_primary_index_is_accepted()
    {
        var context = Context();
        var table = ModelFactory.Table("T", new[] { ("Index", ValueType.String) });

        context.CheckPrimaryIndexValidity(table.Fields[0]);
    }

    /// <summary>
    /// Every type whose values identify a row is accepted as the primary index.
    /// </summary>
    /// <remarks>
    /// The index is not a list of two spellings that grew by one because somebody asked. The
    /// generated lookup is a dictionary over the field's own type, so what decides is whether
    /// the type can be a key at all - and that question is asked of a `*` column in exactly
    /// the same words. `int` and `string` were the ones a check happened to name.
    /// </remarks>
    [Theory]
    [InlineData(ValueType.Int32)]
    [InlineData(ValueType.String)]
    [InlineData(ValueType.Int64)]
    [InlineData(ValueType.Uuid)]
    [InlineData(ValueType.Enum)]
    public void Any_type_that_can_be_a_key_is_accepted_as_the_index(ValueType type)
    {
        var context = Context();
        var table = ModelFactory.Table("T", new[] { ("Index", type) });

        context.CheckPrimaryIndexValidity(table.Fields[0]);
    }

    /// <summary>
    /// And the types that cannot identify a row are refused, each for its own reason.
    /// </summary>
    /// <remarks>
    /// Four different faults rather than one list: a float does not compare exactly, a bool
    /// has two values for any number of rows, an array cell holds several values where a key
    /// holds one, and a time value compares exactly but is not what any sheet keys its rows
    /// by. `bool` used to be refused here and accepted as a `*` column, which is the split
    /// this consolidation removed - so the reason is asserted, not just the refusal.
    /// </remarks>
    [Theory]
    [InlineData(ValueType.Double, "floating point")]
    [InlineData(ValueType.Float, "floating point")]
    [InlineData(ValueType.Bool, "two rows")]
    [InlineData(ValueType.Int32Array, "array cell")]
    [InlineData(ValueType.StringArray, "array cell")]
    [InlineData(ValueType.DateTime, "time value")]
    [InlineData(ValueType.TimeSpan, "time value")]
    public void Types_that_cannot_identify_a_row_are_refused(ValueType type, string reason)
    {
        var context = Context();
        var table = ModelFactory.Table("T", new[] { ("Index", type) });

        var error = Assert.Throws<TabbitException>(
            () => context.CheckPrimaryIndexValidity(table.Fields[0]));

        Assert.Contains(reason, error.Message);
    }

    private static Tabbit.Cooking.CookingContext Context()
        => new Tabbit.Cooking.CookingContext(
            new Tabbit.Models.Model(), new Tabbit.Recipe.RecipeModel());
}
