using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A record whose member is itself a record, read from JSON, from the compact JSON and from
/// binary, and compared value by value.
/// </summary>
/// <remarks>
/// The three routes are genuinely different at this depth. The named JSON carries the nested
/// object whole; the binary carries one fixed-array column per **leaf** and the reader rebuilds
/// the nesting from the member path; the compact JSON is positional over the wire columns,
/// which is the route most likely to be wrong - reading one entry per member rather than per
/// leaf would take the first leaf's run and call it the whole record.
///
/// TypeScript is the only language that can be asked this, because it is the only one that
/// reads JSON as well as binary.
///
/// spec/nested-multi-level.md.
/// </remarks>
public class NestedDeepTypescriptRoundTripTests
{
    private const string Scenario = "nested-deep";

    private static RoundTripResult RunRoundTrip()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        return TypescriptRoundTrip.Run(Scenario, driver: "ts-check-nested-deep");
    }

    [Fact]
    public void All_three_read_paths_agree_at_every_level()
    {
        var result = RunRoundTrip();

        Assert.True(result.Succeeded,
            $"The read paths disagree.{Environment.NewLine}{result.Output}");

        var report = LastJsonObject(result.StdOut);
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// And the values are the sheet's.
    /// </summary>
    /// <remarks>
    /// Three paths can agree and all be wrong: read the same columns into the same wrong shape
    /// and nothing above would notice. These are the values the fixture's sheet holds, and
    /// `10:11,12` is what says the value and the record at one level did not get crossed.
    /// </remarks>
    [Fact]
    public void Each_element_holds_its_value_and_its_record()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("starFromBinary", out _));

        string[] expected = { "10:11,12/20:21,22", "30:31,32/40:41,42" };

        Assert.Equal(expected, Strings(values, "starFromBinary"));
        Assert.Equal(expected, Strings(values, "starFromJson"));
        Assert.Equal(expected, Strings(values, "starFromCompact"));
    }

    private static string[] Strings(JsonElement values, string field)
        => values.GetProperty(field).EnumerateArray().Select(e => e.GetString()).ToArray();

    private static IEnumerable<JsonElement> JsonObjects(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("{")) continue;

            yield return JsonDocument.Parse(trimmed).RootElement.Clone();
        }
    }

    private static JsonElement LastJsonObject(string output) => JsonObjects(output).Last();
}
