using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The C++ generator, checked by compiling what it emits and running it.
///
/// C++ has no equivalent of the runtime that ships with the C# and TypeScript
/// output, so the binary reader in lib/cpp is a second implementation of a format
/// defined by the C# writer. Two programs that must agree byte for byte is exactly
/// the kind of thing that drifts silently, and the only way to know they still
/// agree is to write with one and read with the other.
///
/// So this loads the .tcb files the exporter produced, through the generated
/// header, and compares the result against the JSON exporter's view of the same
/// workbook.
/// </summary>
public class CppGeneratorTests
{
    private const string Scenario = "core";
    private const string Accessor = "CoreAccessor";

    private static JsonElement RunCppReader()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the TypeScript gate: a gate
        // that turns itself off silently is worse than no gate. CI installs a
        // compiler for this.
        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++17 compiler is required to check the generated C++. {why}");

        var result = CppToolchain.BuildAndRun(Scenario, Accessor);

        Assert.True(result.Succeeded,
            $"Generated C++ failed to build or run.{Environment.NewLine}{result.Output}");

        return JsonDocument.Parse(result.StdOut).RootElement.Clone();
    }

    private static JsonElement ExporterRows(string table)
    {
        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(Scenario), "json-named", table + ".json"));

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// Every primitive type read back through the generated C++ and matched
    /// against what the JSON exporter wrote from the same cells.
    /// </summary>
    [Fact]
    public void Generated_cpp_reads_back_every_primitive_type()
    {
        var cpp = RunCppReader().GetProperty("TestFieldTypes");
        var expected = ExporterRows("TestFieldTypes");

        Assert.Equal(expected.GetArrayLength(), cpp.GetArrayLength());

        for (int i = 0; i < expected.GetArrayLength(); i++)
        {
            var e = expected[i];
            var a = cpp[i];

            Assert.Equal(e.GetProperty("index").GetInt32(), a.GetProperty("index").GetInt32());
            Assert.Equal(e.GetProperty("stringField").GetString(), a.GetProperty("stringField").GetString());
            Assert.Equal(e.GetProperty("boolField").GetBoolean(), a.GetProperty("boolField").GetBoolean());
            Assert.Equal(e.GetProperty("intField").GetInt32(), a.GetProperty("intField").GetInt32());
            Assert.Equal(e.GetProperty("uuidField").GetString(), a.GetProperty("uuidField").GetString());
            Assert.Equal(e.GetProperty("valueTypeField").GetInt32(), a.GetProperty("valueTypeField").GetInt32());
        }
    }

    /// <summary>
    /// A17 - the writer truncated every 64 bit value to its low 32 bits.
    ///
    /// `Write(long)` cast through uint before widening again, so anything outside
    /// [0, uint.MaxValue] was silently corrupted: negatives came back as large
    /// positives and large positives lost their high half. The reader was always
    /// reading a full eight bytes, so only the writer was wrong - which is why
    /// nothing that only ever round-tripped through C# noticed.
    /// </summary>
    [Fact]
    public void A17_sixty_four_bit_values_survive_the_binary_round_trip()
    {
        var cpp = RunCppReader().GetProperty("TestFieldTypes");

        // Both sit far outside the 32 bit range the old writer preserved.
        Assert.Equal(9007199254740993L, cpp[0].GetProperty("bigIntField").GetInt64());
        Assert.Equal(-9007199254740993L, cpp[1].GetProperty("bigIntField").GetInt64());
    }

    /// <summary>
    /// The two array kinds are encoded differently - a delimited array carries its
    /// length, a serial field does not - so reading them back proves the C++ side
    /// makes the same distinction the writer does.
    /// </summary>
    [Fact]
    public void Generated_cpp_reads_both_array_kinds()
    {
        var cpp = RunCppReader().GetProperty("ArrayTypes");

        int[] Ints(JsonElement row, string name)
            => row.GetProperty(name).EnumerateArray().Select(x => x.GetInt32()).ToArray();

        string[] Strings(JsonElement row, string name)
            => row.GetProperty(name).EnumerateArray().Select(x => x.GetString()).ToArray();

        // Delimited: a different length in every row, including an empty one.
        Assert.Equal(new[] { "red", "green", "blue" }, Strings(cpp[0], "tags"));
        Assert.Equal(new[] { "solo" }, Strings(cpp[1], "tags"));
        Assert.Empty(Strings(cpp[2], "tags"));

        // Enum arrays arrive as their label values.
        Assert.Equal(new[] { 1, 2 }, Ints(cpp[0], "grades"));

        // Serial: fixed width, and unaffected by the delimited columns beside it.
        Assert.Equal(new[] { 1, 2 }, Ints(cpp[0], "slotArray"));
        Assert.Equal(new[] { 5, 6 }, Ints(cpp[2], "slotArray"));
    }

    /// <summary>
    /// References are stored as the target's index and turned into pointers only
    /// once every table is loaded, so this also checks the linking pass.
    /// </summary>
    [Fact]
    public void Generated_cpp_resolves_cross_table_references()
    {
        var cpp = RunCppReader().GetProperty("Item");

        Assert.Equal("Weapon", cpp[0].GetProperty("categoryName").GetString());
        Assert.Equal("Armor", cpp[1].GetProperty("categoryName").GetString());
        Assert.Equal("Potion", cpp[2].GetProperty("categoryName").GetString());
    }
}
