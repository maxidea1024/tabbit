using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Record arrays whose length is the row's, read from JSON and from binary and compared.
/// </summary>
/// <remarks>
/// A variable length reaches the two formats by different routes - the JSON writes a shorter
/// array, the binary writes a count per row per member - so agreeing is evidence rather than
/// coincidence. TypeScript is the only reader that takes both paths, which is why this check
/// exists here and not once per language.
///
/// spec/types/variable-length-record-arrays.md has the rule both paths are implementing. The
/// `record-trim` golden pins the bytes and the text; these assert what the numbers are.
/// </remarks>
public class RecordTrimTests
{
    private const string Scenario = "record-trim";

    private static RoundTripResult RunRoundTrip()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        return TypescriptRoundTrip.Run(Scenario, driver: "ts-check-record-trim");
    }

    [Fact]
    public void Both_read_paths_agree_on_every_element_of_every_row()
    {
        var result = RunRoundTrip();

        Assert.True(result.Succeeded,
            $"JSON and binary read paths disagree.{Environment.NewLine}{result.Output}");

        var report = LastJsonObject(result.StdOut);
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// The lengths are what the sheet's rows filled in, and the rule is trailing-only.
    /// </summary>
    /// <remarks>
    /// Row 3 is the one that matters: it leaves the middle element empty and keeps its
    /// length, because closing the gap would put the third slot at index 1 on that row and
    /// index 2 on every other, and then the index no longer names the column.
    /// </remarks>
    [Fact]
    public void The_length_is_the_last_element_the_row_filled_in()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("lengthsFromBinary", out _));

        int[] fromBinary = values.GetProperty("lengthsFromBinary")
            .EnumerateArray().Select(e => e.GetInt32()).ToArray();

        Assert.Equal(new[]
        {
            3, // all three filled
            2, // the last one empty
            3, // a gap in the middle, so nothing is dropped
            0, // nothing at all, which a fixed length cannot say
            2, // zeroes the author wrote, then an empty one
        }, fromBinary);

        Assert.Equal(fromBinary, values.GetProperty("lengthsFromJson")
            .EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    /// <summary>
    /// A zero somebody typed is a value, and only a blank cell is absence.
    /// </summary>
    /// <remarks>
    /// The distinction the whole feature rests on. Trimming on the parsed value instead of
    /// on whether the cell held one would cut row 5 to a single element and row 3's gap to
    /// nothing - both times deleting something the author wrote.
    /// </remarks>
    [Fact]
    public void An_authored_zero_is_not_an_empty_element()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("slotFromBinary", out _));

        string[][] fromBinary = values.GetProperty("slotFromBinary").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(e => e.GetString()).ToArray()).ToArray();

        // Row 5: `0:0` is the author's, and the element after it is the blank that was cut.
        Assert.Equal(new[] { "10:1", "0:0" }, fromBinary[4]);

        // Row 3: the empty middle element is carried as the type's empty value, in place.
        Assert.Equal(new[] { "10:1", "0:0", "30:3" }, fromBinary[2]);

        Assert.Equal(fromBinary, values.GetProperty("slotFromJson").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(e => e.GetString()).ToArray()).ToArray());
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
