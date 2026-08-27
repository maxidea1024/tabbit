using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Every language that carries a `set` and a `map`, reading the same file into both layers.
/// </summary>
/// <remarks>
/// **One set of assertions for all of them, which is the point of the surface being two
/// layers.** The spelling differs per language and the questions do not: what the arrays
/// hold in the file's order, what a lookup answers about a key the row has and one it does
/// not, and that a row which wrote nothing has containers of no entries rather than none.
///
/// A language opts in by growing the container and a driver; if it cannot answer these, it
/// has not carried the shape and the refusal should still be naming it.
///
/// spec/types/set-and-map.md sections 7 and 9.
/// </remarks>
public class ContainerLanguageTests
{
    [Theory]
    [InlineData("go")]
    [InlineData("java")]
    [InlineData("kotlin")]
    [InlineData("dart")]
    [InlineData("swift")]
    [InlineData("rust")]
    [InlineData("python")]
    [InlineData("ruby")]
    [InlineData("php")]
    [InlineData("cpp")]
    [InlineData("c")]
    [InlineData("lua")]
    public void The_generated_reader_fills_both_layers(string language)
    {
        var values = ContainerHarness.Run(language);

        Assert.Equal(
            ["new", "sale"],
            values.GetProperty("tags").EnumerateArray().ToArray().Select(v => v.GetString()));

        // A set answers about what this row holds, and not about what it does not.
        Assert.True(values.GetProperty("hasSale").GetBoolean());
        Assert.False(values.GetProperty("hasGone").GetBoolean());

        // A map of scalars answers with the value.
        Assert.Equal(120, values.GetProperty("priceOf11").GetInt32());

        // A map of structs answers with the entry's position, and the members are read at it.
        Assert.Equal(1, values.GetProperty("dropIndexOf2").GetInt32());
        Assert.Equal(102, values.GetProperty("dropItemAt2").GetInt32());
        Assert.Equal(3, values.GetProperty("dropCountAt2").GetInt32());
    }

    /// <summary>
    /// A row that wrote nothing has containers of no entries, not none.
    /// </summary>
    /// <remarks>
    /// Where a reader that allocated on a length it never read shows up, and where a lookup
    /// built from an array that is not there would.
    /// </remarks>
    [Theory]
    [InlineData("go")]
    [InlineData("java")]
    [InlineData("kotlin")]
    [InlineData("dart")]
    [InlineData("swift")]
    [InlineData("rust")]
    [InlineData("python")]
    [InlineData("ruby")]
    [InlineData("php")]
    [InlineData("cpp")]
    [InlineData("c")]
    [InlineData("lua")]
    public void A_row_with_no_entries_reads_as_empty(string language)
    {
        var values = ContainerHarness.Run(language);

        Assert.Equal(0, values.GetProperty("emptyTagCount").GetInt32());
        Assert.Equal(0, values.GetProperty("emptyPriceCount").GetInt32());
    }
}
