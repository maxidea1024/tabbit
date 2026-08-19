using System;
using System.IO;
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
/// JSON only for now. The format carries a presence bit per row and has nowhere to put one per
/// element, so the binary and the thirteen readers refuse a column of this shape by name until
/// they learn it. spec/nullable-array-elements.md.
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
    /// The staging that makes a partial rollout safe: the meaning lands in JSON first, and
    /// everything that would have to write a bit per element says so by name until it can.
    /// The same shape `SupportsOptionalFields` used while thirteen readers learned the row
    /// bitmap.
    /// </remarks>
    [Fact]
    public void A_target_that_cannot_say_it_refuses_the_column()
    {
        var result = TabbitRunner.Convert("nullable-elements-binary");

        Assert.False(result.Succeeded, "A target with no element presence accepted the column.");

        Assert.Contains("does not support arrays whose elements may be absent yet", result.StdOut);
        Assert.Contains("`Listing` column `Holes` is typed `int?[]`", result.StdOut);
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
