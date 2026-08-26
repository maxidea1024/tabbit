using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A record whose members are arrays, read from JSON and from binary and compared value by
/// value.
/// </summary>
/// <remarks>
/// This shape shares its columns and its wire with an array of records; all that differs is
/// what they are assembled into. So the check worth having is that both read paths assemble
/// the same thing - and TypeScript is the only language that can make it, because it is the
/// only one that reads both formats.
///
/// The two routes are genuinely different. The JSON carries the object whole, members and
/// all; the binary carries one fixed-array column per member and the reader indexes the
/// member rather than the record. Agreeing is evidence rather than coincidence.
///
/// spec/types/nested-multi-level.md.
/// </remarks>
public class MemberArrayTypescriptRoundTripTests
{
    private const string Scenario = "member-array";

    private static RoundTripResult RunRoundTrip()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate
        // that quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        return TypescriptRoundTrip.Run(Scenario, driver: "ts-check-member-array");
    }

    [Fact]
    public void Both_read_paths_agree_on_every_member()
    {
        var result = RunRoundTrip();

        Assert.True(result.Succeeded,
            $"JSON and binary read paths disagree.{Environment.NewLine}{result.Output}");

        var report = LastJsonObject(result.StdOut);
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// And the values are the sheet's.
    /// </summary>
    /// <remarks>
    /// Two paths can agree and both be wrong: read the same columns into the same wrong
    /// shape and nothing here would notice. These are the values the fixture's sheet holds,
    /// and they are what says the elements went to the member rather than to the record.
    /// </remarks>
    [Fact]
    public void Each_member_holds_all_of_its_elements()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("skillFromBinary", out _));

        string[] fromBinary = values.GetProperty("skillFromBinary")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        // `10|11` is one member holding both elements. An array of records would have put
        // 10 and 11 in different objects, which no rendering of this could produce.
        Assert.Equal(new[] { "10|11/a|b", "20|21/c|d", "0|0/|" }, fromBinary);

        Assert.Equal(fromBinary, values.GetProperty("skillFromJson")
            .EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>
    /// And the array of arrays holds both levels, on both read paths.
    /// </summary>
    /// <remarks>
    /// The same columns and the same wire as the record above; only the assembly differs.
    /// `1|2|3` is one inner array - a record of arrays would have keyed it by a name, and an
    /// array of records would have split those three across three objects.
    /// </remarks>
    [Fact]
    public void An_array_of_arrays_holds_both_levels()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("gridFromBinary", out _));

        string[] fromBinary = values.GetProperty("gridFromBinary")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(new[] { "1|2|3/4|5|6", "7|8|9/10|11|12", "0|0|0/0|0|0" }, fromBinary);

        Assert.Equal(fromBinary, values.GetProperty("gridFromJson")
            .EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>
    /// The plain record beside it is untouched - members still scalars, group still one
    /// object.
    /// </summary>
    [Fact]
    public void A_record_with_no_number_is_still_scalar_members()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("posFromBinary", out _));

        Assert.Equal(
            new[] { "1.5,-2.5", "0,0", "0,0" },
            values.GetProperty("posFromBinary").EnumerateArray().Select(e => e.GetString()).ToArray());
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
