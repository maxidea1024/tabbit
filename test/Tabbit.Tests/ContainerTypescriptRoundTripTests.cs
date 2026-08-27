using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A `set` and a `map` read from JSON and from binary, and compared value by value.
/// </summary>
/// <remarks>
/// **The lookups are what makes both paths worth running.** Neither export carries a `Set`
/// or a `Map` - they are built from the arrays where the rows are published, which is the
/// one place the two read paths meet. A lookup built on the binary path alone would leave a
/// project reading the .json with an empty `Map` and no error at all, and TypeScript is the
/// only language that reads both.
///
/// spec/types/set-and-map.md sections 7.3 and 8.
/// </remarks>
public class ContainerTypescriptRoundTripTests
{
    private const string Scenario = "containers-target";

    private static RoundTripResult RunRoundTrip()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        return TypescriptRoundTrip.Run(Scenario, driver: "ts-check-containers");
    }

    [Fact]
    public void Both_read_paths_agree_on_the_arrays_and_the_lookups()
    {
        var result = RunRoundTrip();

        Assert.True(result.Succeeded,
            $"JSON and binary read paths disagree.{Environment.NewLine}{result.Output}");

        var report = LastJsonObject(result.StdOut);
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// And the lookups answer what the sheet wrote.
    /// </summary>
    /// <remarks>
    /// Two paths can agree and both be wrong - build both lookups from the same wrong column
    /// and nothing above would notice. These are the values the fixture's sheet holds.
    /// </remarks>
    [Fact]
    public void The_lookups_answer_what_the_sheet_wrote()
    {
        var result = RunRoundTrip();
        var values = FirstJsonObject(result.StdOut);

        Assert.Equal(
            ["new", "sale"],
            values.GetProperty("tags").EnumerateArray().ToArray().Select(v => v.GetString()));

        Assert.True(values.GetProperty("hasSale").GetBoolean());
        Assert.False(values.GetProperty("hasGone").GetBoolean());

        // A map of scalars answers with the value; a map of structs with the position.
        Assert.Equal(120, values.GetProperty("priceOf11").GetInt32());
        Assert.Equal(1, values.GetProperty("dropIndexOf2").GetInt32());
        Assert.Equal(102, values.GetProperty("dropItemAt2").GetInt32());
    }

    /// <summary>
    /// Iterating a lookup gives the file's order back, which is the sheet's.
    /// </summary>
    /// <remarks>
    /// Nothing sorts the entries - a tool that did would have to choose whose order, and the
    /// golden would then hold that choice. `Map` and `Set` keep insertion order here, so the
    /// lookup and the array agree on what the second entry is.
    /// spec/types/set-and-map.md section 4.
    /// </remarks>
    [Fact]
    public void A_lookup_iterates_in_the_files_order()
    {
        var result = RunRoundTrip();
        var values = FirstJsonObject(result.StdOut);

        Assert.Equal(
            [10, 11],
            values.GetProperty("priceKeysInOrder").EnumerateArray().ToArray()
                .Select(v => v.GetInt32()));
    }

    /// <summary>A row that wrote nothing has containers of no entries, not none.</summary>
    [Fact]
    public void A_row_with_no_entries_has_empty_lookups()
    {
        var result = RunRoundTrip();
        var values = FirstJsonObject(result.StdOut);

        Assert.Equal(0, values.GetProperty("emptyTagCount").GetInt32());
        Assert.Equal(0, values.GetProperty("emptyPriceCount").GetInt32());
    }

    private static JsonElement FirstJsonObject(string stdout)
        => JsonDocument.Parse(JsonLines(stdout).First()).RootElement.Clone();

    private static JsonElement LastJsonObject(string stdout)
        => JsonDocument.Parse(JsonLines(stdout).Last()).RootElement.Clone();

    private static string[] JsonLines(string stdout)
        => stdout.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('{'))
            .ToArray();
}
