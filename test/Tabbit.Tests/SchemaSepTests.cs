using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A whole declared value written into one cell.
/// </summary>
/// <remarks>
/// **The comparison is the gate**, and it is the one the composite value types are already
/// held to: two workbooks holding the same table under the same name, one writing a record
/// into a single cell and one writing its members as columns, and a file that must come out
/// identical byte for byte. What `sep` changes is how a sheet is written; a record has always
/// been one column per member on the wire.
///
/// A fold that did not happen fails here, and so does one that read a component as the wrong
/// member. Neither would need an assertion written for it.
///
/// notes/struct-dsl-design.md section 7.3.
/// </remarks>
public class SchemaSepTests
{
    private static void Convert(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Converting `{scenario}` failed.{System.Environment.NewLine}{result.Describe()}");
    }

    private static string Json(string scenario)
        => File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(scenario), "json-named", "Payout.json"));

    /// <summary>
    /// The two ways of writing the record produce the same file.
    /// </summary>
    [Fact]
    public void A_packed_record_and_its_own_columns_reach_the_same_file()
    {
        Convert("packed");
        Convert("packed-expanded");

        byte[] fromOneCell = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("packed"), "binary", "Payout.tcb"));

        byte[] fromColumns = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("packed-expanded"), "binary", "Payout.tcb"));

        Assert.Equal(fromColumns, fromOneCell);
    }

    /// <summary>
    /// And the same JSON, which is where a difference is readable.
    /// </summary>
    [Fact]
    public void The_two_ways_of_writing_the_record_produce_the_same_json()
    {
        Convert("packed");
        Convert("packed-expanded");

        Assert.Equal(Json("packed-expanded"), Json("packed"));
    }

    /// <summary>
    /// The components reach the members they are written beside.
    /// </summary>
    /// <remarks>
    /// The comparison would pass if both sides were wrong in the same way, which they cannot
    /// be - one side writes its members out - but nothing in it says what the values are.
    /// The bracketed row is here too: the notation takes brackets or none, and the same three
    /// values come out either way.
    /// </remarks>
    [Fact]
    public void A_cell_is_read_into_the_members_by_position()
    {
        Convert("packed");

        var rows = JsonDocument.Parse(Json("packed")).RootElement;

        var first = rows[0].GetProperty("reward");
        Assert.Equal(10, first.GetProperty("itemId").GetInt32());
        Assert.Equal(1, first.GetProperty("count").GetInt32());
        Assert.Equal("icon_a", first.GetProperty("icon").GetString());

        // Written `(20,3,)`, so the brackets come off and the last member has no value.
        var second = rows[1].GetProperty("reward");
        Assert.Equal(20, second.GetProperty("itemId").GetInt32());
        Assert.Equal("", second.GetProperty("icon").GetString());
    }
}
