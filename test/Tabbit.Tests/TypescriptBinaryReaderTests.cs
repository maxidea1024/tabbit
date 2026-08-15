using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The TypeScript binary reader, checked by reading the same tables both ways.
///
/// TypeScript used to read only JSON while C# and C++ read the binary export, so a
/// project using TypeScript had to export JSON as well. The reader closes that gap -
/// and, being a third implementation of a format the C# writer defines, it needs the
/// same treatment the C++ one got: write with one, read with the other, compare.
/// </summary>
public class TypescriptBinaryReaderTests
{
    private const string Scenario = "core";

    private static RoundTripResult RunRoundTrip()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate
        // that quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        return TypescriptRoundTrip.Run(Scenario);
    }

    /// <summary>
    /// Every field of every checked table, read from JSON and from binary, has to match.
    ///
    /// A generated table exposes one API regardless of where its data came from, so a
    /// disagreement between the two paths is a defect in one of them whichever way it
    /// falls.
    /// </summary>
    [Fact]
    public void Both_read_paths_agree_on_every_value()
    {
        var result = RunRoundTrip();

        Assert.True(result.Succeeded,
            $"JSON and binary read paths disagree.{Environment.NewLine}{result.Output}");

        // The check prints its findings, so an empty list is asserted rather than
        // inferred from the exit code alone.
        var report = LastJsonObject(result.StdOut);
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// A23 - a 64-bit integer used to be rounded on the way through JSON.
    ///
    /// JSON has one numeric type and every reader treats it as a double, so
    /// 9007199254740993 came back as ...992 - silently, and in a way that hides: the
    /// obvious equality check against a literal also parses to the wrong value, so
    /// both sides agree and the comparison passes.
    ///
    /// Exported as a string now, and reconstructed exactly on both paths.
    /// </summary>
    [Fact]
    public void A23_sixty_four_bit_integers_are_exact_from_both_sources()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var lines = JsonObjects(result.StdOut).ToList();
        var bigints = lines.First(o => o.TryGetProperty("bigIntFromBinary", out _));

        string[] fromBinary = bigints.GetProperty("bigIntFromBinary")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        string[] fromJson = bigints.GetProperty("bigIntFromJson")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(new[] { "9007199254740993", "-9007199254740993", "0" }, fromBinary);
        Assert.Equal(fromBinary, fromJson);
    }

    /// <summary>
    /// References read from binary carry the target's index, resolved after loading.
    /// </summary>
    [Fact]
    public void References_read_from_binary_carry_the_target_index()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var items = JsonObjects(result.StdOut).First(o => o.TryGetProperty("itemNames", out _));

        Assert.Equal(new[] { "Short Sword", "Leather Armor", "Small Potion" },
            items.GetProperty("itemNames").EnumerateArray().Select(e => e.GetString()).ToArray());

        Assert.Equal(new[] { 1, 2, 3 },
            items.GetProperty("categoryIndices").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    /// <summary>
    /// The accessor links references, from either format.
    /// </summary>
    /// <remarks>
    /// It did not. `solveCrossReferences` was generated as an empty method, so nothing
    /// ever called the `setReference_*_INTERNAL` methods sitting on every record: from
    /// binary a reference stayed undefined, and from JSON it was assigned the raw key -
    /// a number in a member declared as a row, which no type checker sees because the
    /// declaration is what lies.
    ///
    /// Nothing caught it because the round-trip check read tables one at a time, where
    /// being unlinked is correct, and compared the raw key rather than the row.
    /// </remarks>
    [Fact]
    public void The_accessor_links_references_from_both_formats()
    {
        var result = RunRoundTrip();
        Assert.True(result.Succeeded, result.Output);

        var linked = JsonObjects(result.StdOut)
            .First(o => o.TryGetProperty("linkedCategoryNames", out _));

        Assert.Equal(new[] { "Weapon", "Armor", "Potion" },
            linked.GetProperty("linkedCategoryNames")
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
