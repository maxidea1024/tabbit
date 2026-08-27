using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A `map` written in a sheet's own type cell, with no declarations at all.
/// </summary>
/// <remarks>
/// **The point is the absence of a `.tbs` file.** The declared notation needs one and puts
/// every column under the struct's group; a project that wants one map column should not have
/// to take either. The type cell says what the column is, and the notation's own parser reads
/// it - so the grammar is still written once, which is the whole of what section 2.3 was
/// waiting for.
///
/// spec/types/set-and-map.md section 2.3.
/// </remarks>
public class SheetContainerTests
{
    private const string Scenario = "containers-sheet";

    private static void Convert()
    {
        var result = TabbitRunner.Convert(Scenario);

        Assert.True(result.Succeeded,
            $"Converting `{Scenario}` failed.{Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// The pairs in one cell become the two columns a map is.
    /// </summary>
    [Fact]
    public void A_map_in_a_type_cell_becomes_its_two_columns()
    {
        Convert();

        using var document = JsonDocument.Parse(Json());
        var prices = document.RootElement[0].GetProperty("prices");

        Assert.Equal(
            [10, 11],
            prices.GetProperty("key").EnumerateArray().ToArray().Select(k => k.GetInt32()));

        Assert.Equal(
            [100, 120],
            prices.GetProperty("value").EnumerateArray().ToArray().Select(v => v.GetInt32()));
    }

    /// <summary>An empty cell is a map of no entries, not a row that has none.</summary>
    [Fact]
    public void An_empty_cell_is_a_map_of_no_entries()
    {
        Convert();

        using var document = JsonDocument.Parse(Json());
        var prices = document.RootElement[2].GetProperty("prices");

        Assert.Empty(prices.GetProperty("key").EnumerateArray());
    }

    /// <summary>
    /// And the generated code carries the lookup, not just the arrays.
    /// </summary>
    /// <remarks>
    /// A column typed in a sheet is the group itself rather than a member of one, so the
    /// lookup sits on the group's element type. Without this the surface would be half a
    /// container - the arrays, and nothing to ask with.
    /// </remarks>
    [Fact]
    public void The_generated_code_carries_the_lookup()
    {
        Convert();

        string generated = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "tables", "StallTable.cs"));

        Assert.Contains("Dictionary<int, int> _byKey;", generated);
        Assert.Contains("public bool TryGetValue(int key, out int value)", generated);
    }

    /// <summary>
    /// A `set` in a type cell is refused, and the report says where to put it instead.
    /// </summary>
    /// <remarks>
    /// **A refusal rather than a gap, for the reason `SupportsContainers` exists.** A map in
    /// a type cell becomes two columns and so becomes a record, which is a type a lookup can
    /// sit on; a set stays one column and there is no such type. Emitting the array alone
    /// would hand a consumer half a container and never say so.
    /// </remarks>
    [Fact]
    public void A_set_in_a_type_cell_is_refused_and_says_where_to_write_it()
    {
        var result = TabbitRunner.Convert("containers-sheet-set");

        Assert.False(result.Succeeded, "A set in a type cell was accepted.");

        Assert.Contains("is typed `set<string>` in the sheet", result.StdOut);
        Assert.Contains("Declare the member in a `.tbs` file instead", result.StdOut);
    }

    private static string Json()
        => File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "json-named", "Stall.json"));
}
