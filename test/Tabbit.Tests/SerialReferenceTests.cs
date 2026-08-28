using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// An array of references: numbered reference columns folded into one array.
/// </summary>
/// <remarks>
/// `foreign[]` is refused and its message names this shape instead, so it is the only way a
/// sheet can point at several rows from one field - and nothing in the corpus held one. Every
/// generator emitted code for it and no test ever read a byte of it, which is how the C# page
/// came to allocate the resolved array without the keys beside it, and how both the C# and
/// TypeScript linking passes came to walk the number the sheet had rather than the array.
///
/// Both forms of a reference are in the fixture, because they resolve to different types:
///
///   slot  `foreign` at `Piece`       resolves to the row
///   tier  `foreign` at `Piece.Tier`  resolves to one of that row's values
///
/// spec/types/nullable-array-elements.md · spec/references/references-in-records.md.
/// </remarks>
public class SerialReferenceTests
{
    private const string Scenario = "serial-ref";

    private static string LastJsonLine(string output)
        => output.Split('\n').Select(line => line.Trim())
            .Last(line => line.StartsWith("{") || line.StartsWith("["));

    private static JsonElement Objects(string output, string property)
        => output.Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("{"))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .First(element => element.TryGetProperty(property, out _));

    /// <summary>
    /// A folded group of references converts, and the exported key is the target's.
    /// </summary>
    [Fact]
    public void Numbered_reference_columns_fold_into_an_array()
    {
        var result = TabbitRunner.Convert(Scenario);
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "json-named", "Kit.json"))).RootElement;

        Assert.Equal(new[] { 1, 2 }, rows[0].GetProperty("slot")
            .EnumerateArray().Select(x => x.GetInt32()).ToArray());

        // A written zero stays one. It is the convention for "points at nothing", so the
        // export may not turn it into an absence.
        Assert.Equal(new[] { 2, 0 }, rows[2].GetProperty("slot")
            .EnumerateArray().Select(x => x.GetInt32()).ToArray());
    }

    /// <summary>
    /// The read allocates all three arrays from the file's count, and the linking pass walks
    /// the keys it has.
    /// </summary>
    /// <remarks>
    /// Read off the emitted page rather than inferred from behaviour, because behaviour cannot
    /// tell the two numbers apart while they agree - and they agree in every fixture. What
    /// would break is a file whose column grew, which no sheet in this corpus can produce.
    ///
    /// The keys and the flag are the ones that were missed: their declarations went from a
    /// sized allocation to an empty array when the length left the page, and the read had to
    /// take over sizing them. spec/types/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void The_page_sizes_every_array_from_the_file()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded, conversion.Describe());

        string cs = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "tables", "KitTable.cs"));

        Assert.Contains("record._slot = new PieceRecord[elementCount];", cs);
        Assert.Contains("record._slot_Piece_index = new int[elementCount];", cs);
        Assert.Contains("record._slot_F = new bool[elementCount];", cs);

        // And no length in the check: the shape is the kind, and the length is the row's.
        // spec/wire/tcb-v107-dynamic-arrays.md.
        Assert.Contains("TcbTable.KindArray", cs);
        Assert.DoesNotContain("column.Count", cs);

        string accessor = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "SerialRefAccessor.cs"));

        Assert.Contains(
            "for (int i = 0; i < record._slot_Piece_index.Length; i++)", accessor);

        string ts = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "typescript", "tables.ts"));

        Assert.Contains(
            "for (let i = 0; i < record._slot_Piece_index.length; i++)", ts);
    }

    /// <summary>
    /// C# reads the file and every element resolves to the row the sheet named.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is generated code that builds: before the read sized
    /// the keys, this threw on the first row - and a linking pass bounded by a constant would
    /// leave the last element unresolved instead, which is quieter.
    /// </remarks>
    [Fact]
    public void Csharp_resolves_every_element()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded, conversion.Describe());

        var result = CsToolchain.ReadBack(Scenario, "cs-check-serial-ref");
        Assert.True(result.Succeeded, result.Output);

        var kit = JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement.GetProperty("Kit");

        Assert.Equal(2, kit[0].GetProperty("length").GetInt32());

        Assert.Equal("sword", kit[0].GetProperty("slots")[0].GetProperty("resolved").GetString());
        Assert.Equal("shield", kit[0].GetProperty("slots")[1].GetProperty("resolved").GetString());
        Assert.Equal("ring", kit[1].GetProperty("slots")[0].GetProperty("resolved").GetString());
        Assert.Equal("sword", kit[1].GetProperty("slots")[1].GetProperty("resolved").GetString());

        // The zero points at nothing, and the element beside it still resolved.
        Assert.Equal("shield", kit[2].GetProperty("slots")[0].GetProperty("resolved").GetString());
        Assert.Equal("<unresolved>", kit[2].GetProperty("slots")[1].GetProperty("resolved").GetString());

        // A field reference: the target's own value, per element.
        Assert.Equal("3", kit[0].GetProperty("tiers")[0].GetProperty("resolved").GetString());
        Assert.Equal("5", kit[0].GetProperty("tiers")[1].GetProperty("resolved").GetString());
        Assert.Equal("<unresolved>", kit[2].GetProperty("tiers")[1].GetProperty("resolved").GetString());
    }

    /// <summary>
    /// And TypeScript's two read paths agree, so the values above are not one mistake read
    /// twice.
    /// </summary>
    /// <remarks>
    /// TypeScript is the only language that reads JSON as well as binary. The JSON route takes
    /// the keys from the named export and the binary route takes them from the column, and
    /// both then go through the same linking pass - so what this compares is the two ways in.
    /// </remarks>
    [Fact]
    public void The_two_read_paths_agree()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded, conversion.Describe());

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        var result = TypescriptRoundTrip.Run(Scenario, driver: "ts-check-serial-ref");

        Assert.True(result.Succeeded,
            $"The read paths disagree.{Environment.NewLine}{result.Output}");

        var report = JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement;
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());

        var values = Objects(result.StdOut, "slots");

        Assert.Equal(
            new[] { "sword/shield", "ring/sword", "shield/<unresolved>" },
            values.GetProperty("slots").EnumerateArray().Select(x => x.GetString()).ToArray());

        Assert.Equal(
            new[] { "3/5", "8/3", "5/<unresolved>" },
            values.GetProperty("tiers").EnumerateArray().Select(x => x.GetString()).ToArray());
    }
}
