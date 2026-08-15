using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Record groups read from JSON and from binary, compared value by value.
///
/// This is the check that says the notation, the JSON shape and the wire layout agree,
/// and no other language can make it: TypeScript is the only one that reads both formats.
/// The two routes are genuinely different - the JSON carries a record as an object, the
/// binary as one fixed-array column per member - so agreeing is evidence rather than
/// coincidence.
///
/// spec/nested-fields.md has the layout both paths are implementing.
/// </summary>
public class NestedTypescriptRoundTripTests
{
    private const string Scenario = "nested";

    private static RoundTripResult RunRoundTrip()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate
        // that quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        return TypescriptRoundTrip.Run(Scenario, driver: "ts-check-nested");
    }

    [Fact]
    public void Both_read_paths_agree_on_every_member_of_every_record()
    {
        var result = RunRoundTrip();

        Assert.True(result.Succeeded,
            $"JSON and binary read paths disagree.{Environment.NewLine}{result.Output}");

        var report = LastJsonObject(result.StdOut);
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// And the values are the sheet's, not merely the same on both paths.
    /// </summary>
    /// <remarks>
    /// Two paths can agree and both be wrong - a member read from the wrong column would
    /// look consistent if the JSON shape had the same mistake in it. These are the values
    /// the fixture's sheet holds.
    /// </remarks>
    [Fact]
    public void An_array_of_records_holds_the_members_the_sheet_wrote()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("slotFromBinary", out _));

        string[][] fromBinary = values.GetProperty("slotFromBinary").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(e => e.GetString()).ToArray()).ToArray();

        // Row 1 and 2 carry both members; row 3 leaves the labels empty, which is a value
        // and not a missing one.
        Assert.Equal(new[] { "10:sword", "11:shield" }, fromBinary[0]);
        Assert.Equal(new[] { "20:bow", "21:arrow" }, fromBinary[1]);
        Assert.Equal(new[] { "20:", "21:" }, fromBinary[2]);

        string[][] fromJson = values.GetProperty("slotFromJson").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(e => e.GetString()).ToArray()).ToArray();

        Assert.Equal(fromBinary, fromJson);
    }

    /// <summary>
    /// A record with no serial number is one object rather than an array of one.
    /// </summary>
    [Fact]
    public void A_record_with_no_number_is_a_single_object()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("posFromBinary", out _));

        string[] fromBinary = values.GetProperty("posFromBinary")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(new[] { "1.5,-2.5", "0,0", "0,0" }, fromBinary);

        Assert.Equal(fromBinary, values.GetProperty("posFromJson")
            .EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    private static System.Collections.Generic.IEnumerable<JsonElement> JsonObjects(string output)
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
