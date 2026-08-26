using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A `set` and a `map` declared, against the array columns they are written out as.
/// </summary>
/// <remarks>
/// **The pair is the whole claim about the wire.** spec/types/set-and-map.md section 4 says
/// a set is one array column and a map is two of equal length, so the format needs no change
/// for either - and the way to say that is to write the same table both ways and require one
/// file. A run that read a container as something else would produce a different file, and
/// nothing here would have to assert what the difference was.
///
/// `SchemaParserTests` and `SchemaDeclarationsTests` cover the notation and what is refused.
/// This is what happens to the values.
/// </remarks>
public class SchemaContainerTests
{
    private static void Convert(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Converting `{scenario}` failed.{System.Environment.NewLine}{result.Describe()}");
    }

    [Fact]
    public void A_declared_container_and_its_written_columns_reach_the_same_file()
    {
        Convert("containers");
        Convert("containers-expanded");

        byte[] fromSchema = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("containers"), "binary", "Shop.tcb"));

        byte[] fromCells = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("containers-expanded"), "binary", "Shop.tcb"));

        Assert.Equal(fromCells, fromSchema);
    }

    /// <summary>
    /// The same claim where a difference is readable - two `.tcb` files that differ say so
    /// in an offset.
    /// </summary>
    [Fact]
    public void The_two_ways_of_writing_the_containers_produce_the_same_json()
    {
        Convert("containers");
        Convert("containers-expanded");

        Assert.Equal(Json("containers-expanded"), Json("containers"));
    }

    /// <summary>
    /// A map is two arrays of the same length, and a set is one - which is what the values
    /// have to look like once they are through.
    /// </summary>
    /// <remarks>
    /// The equivalence gate above would pass if both sides were wrong in the same way, which
    /// they cannot be - one side writes its types out - but nothing in it says what the
    /// values are. Reading a row out is what makes a failure legible without opening a
    /// workbook.
    /// </remarks>
    [Fact]
    public void A_declared_map_arrives_as_two_arrays_of_the_same_length()
    {
        Convert("containers");

        using var document = JsonDocument.Parse(Json("containers"));
        var bag = document.RootElement[0].GetProperty("bag");

        Assert.Equal(
            ["new", "sale"],
            bag.GetProperty("tags").EnumerateArray().ToArray().Select(item => item.GetString()));

        var prices = bag.GetProperty("prices");

        Assert.Equal([10, 11], prices.GetProperty("key").EnumerateArray().ToArray().Select(k => k.GetInt32()));
        Assert.Equal([100, 120], prices.GetProperty("value").EnumerateArray().ToArray().Select(v => v.GetInt32()));
    }

    /// <summary>
    /// An empty cell is a container with nothing in it, not a row that has no container.
    /// </summary>
    [Fact]
    public void An_empty_cell_is_a_container_of_no_elements()
    {
        Convert("containers");

        using var document = JsonDocument.Parse(Json("containers"));
        var bag = document.RootElement[2].GetProperty("bag");

        Assert.Empty(bag.GetProperty("tags").EnumerateArray());
        Assert.Empty(bag.GetProperty("prices").GetProperty("key").EnumerateArray());
    }

    private static string Json(string scenario)
        => File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(scenario), "json-named", "Shop.json"));
}
