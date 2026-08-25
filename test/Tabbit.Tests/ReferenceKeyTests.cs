using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// References to tables keyed by something other than an `int`.
/// </summary>
/// <remarks>
/// A reference carries the target's primary index, and its type is the target's to decide.
/// `int32` used to be written in as a constant - in the exporters, in the format's element
/// mapping, in thirteen read switches and in the templates' member declarations - so a table
/// keyed by `string`, `bigint` or `uuid` could be read and generated but not pointed at, and
/// the refusal told the author to carry the key by hand.
///
/// The refusal these replace lived in `StringIndexTests`.
///
/// spec/reference-key-types.md.
/// </remarks>
public class ReferenceKeyTests
{
    private const string Scenario = "reference-keys";

    private static JsonElement Rows(string table)
        => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(Scenario), "json-named", table + ".json"))).RootElement;

    private static string Generated(params string[] parts)
        => File.ReadAllText(Path.Combine(
            new[] { RepoLayout.OutputDir(Scenario) }.Concat(parts).ToArray()));

    /// <summary>
    /// A workbook whose references point at a string, a bigint and a uuid key converts.
    /// </summary>
    /// <remarks>
    /// One table holding all three at once, so a mixture is pinned as well as each on its
    /// own - a generator picks each key's spelling from its own type table, and getting
    /// `string` right while `uuid` becomes a byte array is the kind of disagreement this is
    /// for.
    /// </remarks>
    [Fact]
    public void A_reference_may_point_at_any_key_a_table_can_have()
    {
        var result = TabbitRunner.Convert(Scenario);

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// The key travels as the sheet wrote it, not as a number standing in for it.
    /// </summary>
    /// <remarks>
    /// This is what the deferred conversion bought. A reference cell used to be parsed as an
    /// int during the data pass - before any table knew what the others looked like - so a
    /// cell holding `Idle_01` died there, and the fixture that pinned the old refusal had to
    /// write a number in its place to reach the check at all.
    /// </remarks>
    [Fact]
    public void The_exported_key_is_the_one_the_sheet_wrote()
    {
        var result = TabbitRunner.Convert(Scenario);
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var clip = Rows("Clip")[0];

        Assert.Equal("Idle_01", clip.GetProperty("anim").GetString());

        // A 64-bit key exports as text for the same reason any 64-bit value does: JSON's one
        // numeric type is a double and would round it.
        Assert.Equal("9007199254740993", clip.GetProperty("entry").GetString());

        Assert.Equal("3f2504e0-4f89-11d3-9a0c-0305e82c3301", clip.GetProperty("cover").GetString());
    }

    /// <summary>
    /// The generated member holding the key is typed by the key.
    /// </summary>
    /// <remarks>
    /// Checked in the source rather than through the golden because it is the one assertion
    /// that says what went wrong when it fails: the member was `int` for every key, and a
    /// string assigned to it does not compile.
    /// </remarks>
    [Fact]
    public void The_generated_key_member_has_the_key_s_type()
    {
        var result = TabbitRunner.Convert(Scenario);
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string clip = Generated("csharp", "tables", "ClipTable.cs");

        // The key is behind a property of the column's own name now, so the storage is
        // internal. spec/reference-surface-naming.md section 4.
        Assert.Contains("internal string _anim_Animation_index;", clip);
        Assert.Contains("internal long _entry_Ledger_index;", clip);
        Assert.Contains("internal System.Guid _cover_Art_index;", clip);

        Assert.Contains("public string Anim => _anim_Animation_index;", clip);
        Assert.Contains("public long Entry => _entry_Ledger_index;", clip);
        Assert.Contains("public System.Guid Cover => _cover_Art_index;", clip);
    }

    /// <summary>
    /// "Points at nothing" is spelled for the key's type rather than as `> 0`.
    /// </summary>
    /// <remarks>
    /// Zero is the convention for a reference that points nowhere, and the accessor asked it
    /// as a numeric comparison for every key. A string has no zero and comparing one against
    /// a number does not compile, so each key type needs its own spelling.
    /// spec/reference-optionality.md.
    /// </remarks>
    [Fact]
    public void The_points_at_nothing_test_fits_the_key_type()
    {
        var result = TabbitRunner.Convert(Scenario);
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string accessor = Generated("csharp", "ReferenceKeysAccessor.cs");

        Assert.Contains("_anim_Animation_index is { Length: > 0 }", accessor);
        Assert.Contains("_cover_Art_index != System.Guid.Empty", accessor);

        // The numeric key keeps the comparison it always had.
        Assert.Contains("_entry_Ledger_index > 0", accessor);
    }

    /// <summary>
    /// TypeScript agrees, including the conversion a 64-bit key needs on the way in.
    /// </summary>
    /// <remarks>
    /// Two languages because they build their lookups differently - `Dictionary<K,V>` against
    /// `Map<K,V>` - and a `bigint` is where they diverge most: the JSON carries it as text
    /// and TypeScript has to reconstruct it, which a raw assignment would not typecheck.
    /// </remarks>
    [Fact]
    public void Typescript_types_and_converts_the_key()
    {
        var result = TabbitRunner.Convert(Scenario);
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string clip = Generated("typescript", "tables", "clip.ts");

        Assert.Contains("_anim_Animation_index: string = \"\"", clip);
        Assert.Contains("_entry_Ledger_index: bigint = 0n", clip);

        // Reconstructed rather than assigned through, and from both row shapes.
        Assert.Contains("this._entry_Ledger_index = BigInt(dataRow.entry)", clip);
        Assert.Contains("BigInt(dataRow[offset++])", clip);
    }

    /// <summary>
    /// And the generated C# compiles.
    /// </summary>
    /// <remarks>
    /// The assertions above compare text, and text is happy with a member typed `string` that
    /// a numeric comparison is then applied to. Only a compiler answers that, and this whole
    /// change is one where the failure mode is a declaration rather than a wrong value - the
    /// same reason `KeyTypeCompileTests` exists beside the `key-types` golden.
    /// </remarks>
    [Fact]
    public void Generated_cs_compiles()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.Compile(Scenario, "ReferenceKeysAccessor");

        Assert.True(result.Succeeded,
            $"Generated C# for a non-int reference key does not compile."
            + $"{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// And the generated TypeScript type-checks.
    /// </summary>
    /// <remarks>
    /// Where the `bigint` lands: the JSON carries a 64-bit key as text and the member is a
    /// `bigint`, so an assignment straight through is exactly the mistake a type-check
    /// catches and a string comparison does not.
    /// </remarks>
    [Fact]
    public void Generated_typescript_type_checks()
    {
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to type-check generated TypeScript. {why}");

        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var check = TypescriptToolchain.TypeCheck(
            Path.Combine(RepoLayout.OutputDir(Scenario), "typescript"));

        Assert.True(check.Succeeded,
            $"Generated TypeScript for a non-int reference key does not compile."
            + $"{Environment.NewLine}{check.Output}");
    }

    /// <summary>
    /// And the binary this build writes is one the code it generates can read.
    /// </summary>
    /// <remarks>
    /// The gate that a compile cannot stand in for. What element a reference column declares
    /// and what element the writer emits are decided in two different places, and while a key
    /// could only be an int they agreed by accident. Teaching the writer the key's own element
    /// left three readers still checking for `i32` - a file this build wrote, refused by the
    /// code this build generated, and nothing to see at compile time. Found exactly that way.
    /// </remarks>
    [Fact]
    public void The_generated_reader_reads_what_the_writer_wrote()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.ReadBack(Scenario, "cs-check-reference-keys");

        Assert.True(result.Succeeded,
            $"Reading the binary back failed.{Environment.NewLine}{result.Output}");

        var clip = JsonDocument.Parse(result.StdOut).RootElement.GetProperty("Clip")[0];

        // The keys survived the wire.
        Assert.Equal("Idle_01", clip.GetProperty("animKey").GetString());
        Assert.Equal("9007199254740993", clip.GetProperty("entryKey").GetString());
        Assert.Equal(
            "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
            clip.GetProperty("coverKey").GetString());

        // And each found the row it names, which is what a reference is for.
        Assert.Equal("first", clip.GetProperty("entryNote").GetString());
        Assert.Equal("a.png", clip.GetProperty("coverPath").GetString());
    }
}
