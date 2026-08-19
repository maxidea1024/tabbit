using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The four spellings of an array's optionality: `int[]`, `int[]?`, `int?[]` and `int?[]?`.
/// </summary>
/// <remarks>
/// The marker after the brackets answers for the array and the one before them answers for an
/// element, which is the reading C# gives the same four. What a cell writes does not change -
/// absence is `-` wherever it is written, and a blank element is whatever its type reads a
/// blank as.
///
/// The meaning lands in JSON and in the file first. The thirteen runtimes learn the element
/// bitmap in the step after this one, so a generator still refuses a column of this shape by
/// name - which is what keeps a partial rollout from losing the distinction quietly.
/// spec/nullable-array-elements.md.
/// </remarks>
public class NullableArrayElementTests
{
    private static JsonElement Rows()
    {
        var result = TabbitRunner.Convert("nullable-elements");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("nullable-elements"), "json-named", "Listing.json"))).RootElement;
    }

    private static JsonElement Row(int index) => Rows()[index];

    /// <summary>
    /// `int?[]` holds an element that has no value, and the array around it is still there.
    /// </summary>
    [Fact]
    public void An_element_can_have_no_value()
    {
        var row = Row(1);

        var holes = row.GetProperty("holes");
        Assert.Equal(3, holes.GetArrayLength());
        Assert.Equal(1, holes[0].GetInt32());
        Assert.Equal(JsonValueKind.Null, holes[1].ValueKind);
        Assert.Equal(3, holes[2].GetInt32());
    }

    /// <summary>
    /// The two markers answer for different things, and a row can show both at once.
    /// </summary>
    /// <remarks>
    /// Row 3 leaves the array itself absent where the marker is outside the brackets, and
    /// keeps it where the marker is inside - the same cell content, `-`, read against two
    /// declarations. That is the whole of what the notation buys.
    /// </remarks>
    [Fact]
    public void The_array_and_its_elements_are_separate_answers()
    {
        var holes = Row(1);

        // `int?[]` - elements may be absent, so a `-` element is one.
        Assert.Equal(JsonValueKind.Null, holes.GetProperty("holes")[1].ValueKind);

        // `int[]?` - the array may be, and its elements may not, so nothing is null inside it.
        Assert.Equal(3, holes.GetProperty("maybe").GetArrayLength());
        Assert.Equal(2, holes.GetProperty("maybe")[1].GetInt32());

        var absent = Row(2);

        // The same `-`, written as the whole cell: the array is gone where the column allows
        // it, and stays where the marker is on the element instead.
        Assert.Equal(JsonValueKind.Null, absent.GetProperty("maybe").ValueKind);
        Assert.Equal(JsonValueKind.Null, absent.GetProperty("both").ValueKind);
        Assert.Equal(3, absent.GetProperty("holes").GetArrayLength());
    }

    /// <summary>
    /// A blank element is the empty string, not an absence, and `\-` is the character.
    /// </summary>
    /// <remarks>
    /// The cell rule applied to an element. `string?[]` is where it matters: a value
    /// comparison cannot tell an empty string from an absent element, so this is the pair a
    /// reader has to get right. spec/blank-and-null-cells.md.
    /// </remarks>
    [Fact]
    public void A_blank_element_is_a_value_and_the_escape_is_a_character()
    {
        var blank = Row(3).GetProperty("words");
        Assert.Equal("a", blank[0].GetString());
        Assert.Equal("", blank[1].GetString());
        Assert.Equal("c", blank[2].GetString());

        var escaped = Row(4).GetProperty("words");
        Assert.Equal("-", escaped[1].GetString());
    }

    /// <summary>
    /// Where the elements are required, `-` in one is refused and the message says the fix.
    /// </summary>
    [Fact]
    public void A_required_element_refuses_no_value()
    {
        var result = TabbitRunner.Convert("no-value-element");

        Assert.False(result.Succeeded, "`-` in a required element was accepted.");

        Assert.Contains("Element 2 of this cell is `-`", result.StdOut);
        Assert.Contains("this column's elements are required", result.StdOut);
        Assert.Contains("`?` inside the brackets", result.StdOut);
    }

    /// <summary>
    /// A target that cannot say an element is absent refuses the column rather than losing it.
    /// </summary>
    /// <remarks>
    /// `html` is the target here because it will never carry a bit per element - it renders a
    /// page, and a page has nowhere to put one - so this gate does not move as the thirteen
    /// readers learn the bitmap one at a time. The fixture has nothing optional at the row
    /// level, so what the target meets is this refusal rather than the one beside it.
    /// </remarks>
    [Fact]
    public void A_target_that_cannot_say_it_refuses_the_column()
    {
        var result = TabbitRunner.Convert("element-only");

        Assert.False(result.Succeeded, "A target with no element presence accepted the column.");

        Assert.Contains("does not support arrays whose elements may be absent yet", result.StdOut);
        Assert.Contains("`Holder` column `Holes` is typed `int?[]`", result.StdOut);
    }

    /// <summary>
    /// The file says which columns carry a bitmap, and says the two independently.
    /// </summary>
    /// <remarks>
    /// Bit 6 is the row bitmap and bit 7 the element one, and `int?[]?` sets both - the whole
    /// claim that the two are orthogonal, read off the bytes rather than off the model that
    /// wrote them. Nothing reads the bitmap itself yet; the thirteen runtimes learn it in the
    /// step after this one, and the round trip that compares it against the JSON belongs
    /// there. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void The_file_declares_the_two_bitmaps_separately()
    {
        var result = TabbitRunner.Convert("nullable-elements-binary");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        byte[] file = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("nullable-elements-binary"), "binary", "Listing.tcb"));

        Assert.Equal(106u, BitConverter.ToUInt32(file, Tabbit.Exporters.TcbFormat.VersionOffset));

        var wires = WireBytes(file);

        // `Maybe` and `Both` may have no array; `Holes`, `Both` and `Words` may have an
        // element with no value; `Both` is the one that says both.
        Assert.Equal(2, wires.Count(wire => Tabbit.Exporters.TcbFormat.NullableOf(wire)));
        Assert.Equal(3, wires.Count(wire => Tabbit.Exporters.TcbFormat.ElementNullableOf(wire)));
        Assert.Equal(1, wires.Count(wire =>
            Tabbit.Exporters.TcbFormat.NullableOf(wire)
            && Tabbit.Exporters.TcbFormat.ElementNullableOf(wire)));
    }

    /// <summary>The wire byte of every column descriptor, in file order.</summary>
    private static System.Collections.Generic.List<byte> WireBytes(byte[] file)
    {
        int at = 42;                                     // the fixed header, whole

        int ReadCounter32()
        {
            int shift = 0;
            uint value = 0;

            while (true)
            {
                byte b = file[at++];
                value |= (uint)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                    break;

                shift += 7;
            }

            return (int)((value >> 1) ^ (uint)-(int)(value & 1));
        }

        ReadCounter32();                                 // row count
        int columnCount = ReadCounter32();

        var wires = new System.Collections.Generic.List<byte>();

        for (int column = 0; column < columnCount; column++)
        {
            ReadCounter32();                             // tag
            wires.Add(file[at++]);                       // wire
            at++;                                        // encoding
            ReadCounter32();                             // elements per row
            at += 4;                                     // block length
        }

        return wires;
    }

    /// <summary>
    /// The generated C# reads the bitmap back to the same answers the JSON holds.
    /// </summary>
    /// <remarks>
    /// The question a compile cannot answer and a golden tree cannot either: the file carries
    /// one bit per element written, the generated code walks it with a counter that steps once
    /// per element of every row, and whether those are the same walk is settled by reading.
    ///
    /// `words` is a `string?[]`, and it is in the comparison on purpose - an absent element and
    /// an empty string are the same value, so only the bit tells them apart.
    /// spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void The_generated_reader_agrees_with_the_json()
    {
        var converted = TabbitRunner.Convert("nullable-elements");
        Assert.True(converted.Succeeded,
            $"Conversion failed.{Environment.NewLine}{converted.Describe()}");

        var run = CsToolchain.ReadBack("nullable-elements", "cs-check-nullable-elements");

        Assert.True(run.Succeeded, run.Output);

        var read = JsonDocument.Parse(run.StdOut).RootElement;
        var written = Rows();

        Assert.Equal(written.GetArrayLength(), read.GetArrayLength());

        for (int row = 0; row < written.GetArrayLength(); row++)
        {
            foreach (string column in new[] { "holes", "both", "words" })
            {
                // Serialized rather than compared as raw text: one side was written indented
                // and the other by a harness that had no reason to be.
                Assert.Equal(
                    JsonSerializer.Serialize(written[row].GetProperty(column)),
                    JsonSerializer.Serialize(read[row].GetProperty(column)));
            }
        }
    }

    /// <summary>
    /// The TypeScript reader walks the bitmap to the same answers the JSON holds.
    /// </summary>
    /// <remarks>
    /// A second reader, because the walk is the part a single implementation can get
    /// consistently wrong: the counter steps once per element of every row, and a reader
    /// that stepped per row instead would still be self-consistent. The JSON side is read
    /// from the file rather than through the generated JSON path, so both sides do not go
    /// through one reader. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void The_typescript_reader_agrees_with_the_json()
    {
        var converted = TabbitRunner.Convert("nullable-elements");
        Assert.True(converted.Succeeded,
            $"Conversion failed.{Environment.NewLine}{converted.Describe()}");

        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        var result = TypescriptRoundTrip.Run("nullable-elements", driver: "ts-check-nullable-elements");

        Assert.True(result.Succeeded,
            $"JSON and binary read paths disagree.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// A `?` where neither spelling puts it is refused, naming both.
    /// </summary>
    [Fact]
    public void A_misplaced_marker_names_the_two_spellings()
    {
        var context = new Tabbit.Cooking.CookingContext(
            new Tabbit.Models.Model(), new Tabbit.Recipe.RecipeModel(), new Tabbit.Diagnostics());

        var refusal = Assert.Throws<TabbitException>(
            () => context.RequiresValidTypeName("?int[]", null!));

        Assert.Contains("`int?[]` says an element may", refusal.Message);
    }

    /// <summary>
    /// The markers come off the type name, and each is answered separately.
    /// </summary>
    [Theory]
    [InlineData("int", true, true)]
    [InlineData("int?", false, true)]
    [InlineData("int[]", true, true)]
    [InlineData("int[]?", false, true)]
    [InlineData("int?[]", true, false)]
    [InlineData("int?[]?", false, false)]
    public void Both_markers_are_read_off_the_type_name(
        string written, bool required, bool elementsRequired)
    {
        string bare = Tabbit.Cooking.CookingContext.SplitOptionalMarkers(
            written, out bool isRequired, out bool areElementsRequired);

        Assert.Equal(required, isRequired);
        Assert.Equal(elementsRequired, areElementsRequired);

        // What is left is the type as everything downstream reads it - no marker on either
        // side of the brackets.
        Assert.DoesNotContain("?", bare);
    }
}
