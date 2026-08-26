using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Optional columns read from JSON and from binary, compared value by value and presence
/// by presence.
/// </summary>
/// <remarks>
/// The two formats carry absence by completely different means - the JSON writes `null`,
/// the binary writes a bit in a bitmap at the front of the column's block - so agreeing is
/// evidence rather than coincidence. TypeScript is the only reader that takes both paths.
///
/// spec/types/optional-fields.md has the layout. The `optional` golden pins the bytes and the
/// generated accessors; this asserts what they mean.
/// </remarks>
public class OptionalRoundTripTests
{
    private const string Scenario = "optional";

    private static RoundTripResult RunRoundTrip()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        return TypescriptRoundTrip.Run(Scenario, driver: "ts-check-optional");
    }

    [Fact]
    public void Both_read_paths_agree_on_every_value_and_its_presence()
    {
        var result = RunRoundTrip();

        Assert.True(result.Succeeded,
            $"JSON and binary read paths disagree.{Environment.NewLine}{result.Output}");

        var report = LastJsonObject(result.StdOut);
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// And presence is what the sheet said, not merely the same on both paths.
    /// </summary>
    /// <remarks>
    /// Row 1 fills every optional column; rows 2 and 3 leave all of them blank. `string`
    /// and `bool` are in the list on purpose: those two have always read a blank as `""`
    /// and `false`, so their **values** are the same either way and only presence tells the
    /// rows apart. If the bitmap were wrong for them, nothing else would show it.
    /// </remarks>
    [Fact]
    public void Presence_is_what_the_sheet_wrote()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("presenceFromBinary", out _));

        bool[][] fromBinary = values.GetProperty("presenceFromBinary").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(e => e.GetBoolean()).ToArray()).ToArray();

        // bonus, label, hidden - an int, a string and a bool.
        Assert.Equal(new[] { true, true, true }, fromBinary[0]);
        Assert.Equal(new[] { false, false, false }, fromBinary[1]);
        Assert.Equal(new[] { false, false, false }, fromBinary[2]);

        Assert.Equal(fromBinary, values.GetProperty("presenceFromJson").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(e => e.GetBoolean()).ToArray()).ToArray());
    }

    /// <summary>
    /// An absent value still reads as the type's empty one.
    /// </summary>
    /// <remarks>
    /// Which is the half of the design that keeps this cheap: the values are written for
    /// every row, so the nine column encodings and their decodes never had to learn about
    /// presence. A consumer that does not care about absence sees exactly what it saw
    /// before the marker existed.
    /// </remarks>
    [Fact]
    public void An_absent_value_reads_as_the_types_empty_one()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("bonusFromBinary", out _));

        int[] fromBinary = values.GetProperty("bonusFromBinary")
            .EnumerateArray().Select(e => e.GetInt32()).ToArray();

        Assert.Equal(new[] { 5, 0, 0 }, fromBinary);
        Assert.Equal(fromBinary, values.GetProperty("bonusFromJson")
            .EnumerateArray().Select(e => e.GetInt32()).ToArray());
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
