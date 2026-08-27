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
    /// And a map written as `k:v` pairs in one cell is the same file again.
    /// </summary>
    /// <remarks>
    /// Three workbooks, one file. That is what says the paired cell is a way of writing a
    /// map rather than a second way of holding one - spec/types/set-and-map.md section 5.2.
    /// </remarks>
    [Fact]
    public void A_map_written_as_pairs_reaches_the_same_file()
    {
        Convert("containers");
        Convert("containers-paired");

        byte[] fromColumns = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("containers"), "binary", "Shop.tcb"));

        byte[] fromPairs = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("containers-paired"), "binary", "Shop.tcb"));

        Assert.Equal(fromColumns, fromPairs);
    }

    /// <summary>
    /// The same claim where a difference is readable - two `.tcb` files that differ say so
    /// in an offset.
    /// </summary>
    [Theory]
    [InlineData("containers-expanded")]
    [InlineData("containers-paired")]
    public void The_ways_of_writing_the_containers_produce_the_same_json(string other)
    {
        Convert("containers");
        Convert(other);

        Assert.Equal(Json(other), Json("containers"));
    }

    /// <summary>
    /// A map whose value is a struct is a key column beside a group of columns, each holding
    /// every entry's value of one member.
    /// </summary>
    /// <remarks>
    /// The struct-of-arrays the wire has always written a record as, one level further in -
    /// spec/types/set-and-map.md section 3. Read out because it is the shape most easily got
    /// wrong: a member declared `int` becomes a column of `int[]`, and nothing in the
    /// declaration says so.
    /// </remarks>
    [Fact]
    public void A_map_whose_value_is_a_struct_holds_one_column_per_member()
    {
        Convert("containers");

        using var document = JsonDocument.Parse(Json("containers"));
        var drops = document.RootElement[0].GetProperty("bag").GetProperty("drops");

        Assert.Equal([1, 2], drops.GetProperty("key").EnumerateArray().ToArray().Select(k => k.GetInt32()));

        var value = drops.GetProperty("value");

        Assert.Equal(
            [101, 102],
            value.GetProperty("itemId").EnumerateArray().ToArray().Select(v => v.GetInt32()));

        Assert.Equal(
            [1, 3],
            value.GetProperty("count").EnumerateArray().ToArray().Select(v => v.GetInt32()));
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

    // ------------------------------------------------------------------ refusals

    /// <summary>
    /// What a container promises about its values, and what saying so costs a sheet that
    /// does not keep it.
    /// </summary>
    /// <remarks>
    /// All three in one run rather than the first of them. Correcting a sheet is a matter of
    /// reading a list, and a run that stopped at the first would make it one run per mistake -
    /// which is the same reason the binding reports every disagreeing column.
    ///
    /// And each one names the cell. "Which cell" is what the column constraints settled as
    /// the standard for a report about a value, and a container is a report about values.
    /// </remarks>
    [Fact]
    public void A_row_that_breaks_a_container_says_so_for_every_break()
    {
        var result = TabbitRunner.Convert("containers-refused");

        Assert.False(result.Succeeded, "Rows that break their containers were accepted.");

        Assert.Contains(
            "`Shop.Bag.Tags` is a set and this row holds `new` twice - at element 3, "
            + "and already at element 1",
            result.StdOut);

        Assert.Contains(
            "`Shop.Bag.Prices` is a map and this row keys two entries by `10` - element 2, "
            + "and already element 1",
            result.StdOut);

        Assert.Contains(
            "`Shop.Bag.Prices` holds 2 key(s) and 1 value(s) in this row",
            result.StdOut);
    }

    /// <summary>
    /// What one cell of pairs cannot be: an entry that is not a pair, and a value that is
    /// several columns.
    /// </summary>
    [Fact]
    public void What_a_cell_of_pairs_cannot_hold_is_named_where_it_is_written()
    {
        var result = TabbitRunner.Convert("containers-refused");

        Assert.Contains(
            "`Stall.Bag.Prices` writes its map as pairs and this entry has no `:` in it - `10`",
            result.StdOut);

        Assert.Contains(
            "`Depot.Crate.Drops` writes its map as pairs in one cell, and `drops` holds "
            + "`Reward` - a struct",
            result.StdOut);
    }

    /// <summary>
    /// And the column it refused is not reported a second time for the consequence.
    /// </summary>
    /// <remarks>
    /// A column left unfolded still has a member type one column cannot hold, so the binding
    /// would report that too - a second sentence about a cause the first one already named,
    /// and one that names a member (`Crate.Value`) nobody wrote.
    /// </remarks>
    [Fact]
    public void A_column_the_pairs_pass_refused_is_not_reported_again()
    {
        var result = TabbitRunner.Convert("containers-refused");

        Assert.DoesNotContain("`Crate.Value` is declared", result.StdOut);
    }

    /// <summary>Each of them pointing at the cell that holds it.</summary>
    [Fact]
    public void Each_break_names_the_cell_it_is_in()
    {
        var result = TabbitRunner.Convert("containers-refused");

        Assert.Contains("Containers : D7", result.StdOut);
        Assert.Contains("Containers : E8", result.StdOut);
        Assert.Contains("Containers : F9", result.StdOut);
    }

    /// <summary>
    /// A target with no container type names itself rather than writing a list.
    /// </summary>
    /// <remarks>
    /// **This is the boundary of the rollout, and it has to be a refusal rather than a gap.**
    /// The file carries a set and a map without changing, so a target that has not learned
    /// them writes something plausible - an array, and two arrays side by side - and nothing
    /// downstream ever finds out the tool was told these were distinct elements and keyed
    /// entries. spec/types/set-and-map.md section 7.
    ///
    /// Every generated language carries them now, so what is left is the exporters that have
    /// a spelling of their own to settle: `text` and the database targets, which are stage 3.
    /// Repoint this as each lands.
    /// </remarks>
    [Fact]
    public void A_target_with_no_container_type_refuses_by_name()
    {
        var result = TabbitRunner.Convert("containers-unsupported");

        Assert.False(result.Succeeded, "A target with no container type was handed one.");

        Assert.Contains("Target `text` does not support set columns yet.", result.StdOut);
        Assert.Contains("Table `Shop` field `Bag.Tags` is declared a `set`.", result.StdOut);
    }

    private static string Json(string scenario)
        => File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(scenario), "json-named", "Shop.json"));
}
