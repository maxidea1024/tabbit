using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What a cell says about having nothing: a blank, a `-`, and `\-` for the character itself.
/// </summary>
/// <remarks>
/// A blank cell used to mean "no value" in an optional column, which left the two statements
/// sharing one spelling - and left `string?` with no way to hold an empty string at all. The
/// spelling for absence is `-` now, and a blank is whatever the column's type has always read
/// a blank as.
///
/// The gates below are the whole of the rule: the three readings, the two places `-` is
/// refused, the one place an element may not say it, and the escape. spec/types/blank-and-null-cells.md.
/// </remarks>
public class BlankAndNullCellTests
{
    private static JsonElement Rows()
    {
        var result = TabbitRunner.Convert("blank-and-null");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("blank-and-null"), "json-named", "Cell.json"))).RootElement;
    }

    /// <summary>
    /// `-` is no value, in every type that can say it.
    /// </summary>
    [Fact]
    public void A_dash_is_no_value()
    {
        var row = Rows()[1];

        Assert.Equal(JsonValueKind.Null, row.GetProperty("text").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("count").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("flag").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("tags").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("costs").ValueKind);
    }

    /// <summary>
    /// A blank cell is the value its type reads a blank as, and the `?` does not change it.
    /// </summary>
    /// <remarks>
    /// The regression this whole rule exists for: an empty string is a value an author can
    /// mean, and while a blank meant absence there was no way to write one in a column that
    /// also had to be able to say "none".
    /// </remarks>
    [Fact]
    public void A_blank_is_the_type_s_own_reading_of_one()
    {
        var row = Rows()[2];

        Assert.Equal("", row.GetProperty("text").GetString());
        Assert.False(row.GetProperty("flag").GetBoolean());
        Assert.Equal(0, row.GetProperty("tags").GetArrayLength());
        Assert.Equal(0, row.GetProperty("costs").GetArrayLength());
    }

    /// <summary>
    /// `\-` is the one character `-`, in a cell and in an element.
    /// </summary>
    [Fact]
    public void The_escape_writes_the_mark_itself()
    {
        var row = Rows()[3];

        Assert.Equal("-", row.GetProperty("text").GetString());

        var tags = row.GetProperty("tags");
        Assert.Equal("a", tags[0].GetString());
        Assert.Equal("-", tags[1].GetString());
        Assert.Equal("b", tags[2].GetString());
    }

    /// <summary>
    /// A `-` anywhere but alone in a cell is the character it looks like.
    /// </summary>
    /// <remarks>
    /// The claim that keeps this rule from reaching sheets it has nothing to do with: a
    /// negative number, an identifier with a hyphen in it, a run of them. Two spellings are
    /// special and nothing else is.
    /// </remarks>
    [Fact]
    public void A_dash_inside_a_value_is_a_character()
    {
        var rows = Rows();

        Assert.Equal(-5, rows[3].GetProperty("count").GetInt32());
        Assert.Equal(-1, rows[3].GetProperty("costs")[0].GetInt32());

        Assert.Equal("A-1", rows[4].GetProperty("text").GetString());
        Assert.Equal("--", rows[4].GetProperty("tags")[0].GetString());
        Assert.Equal("-x", rows[4].GetProperty("tags")[1].GetString());
    }

    /// <summary>
    /// A required column has no absence to express, and says so at the cell.
    /// </summary>
    [Fact]
    public void A_required_column_refuses_no_value()
    {
        var result = TabbitRunner.Convert("no-value-refused");

        Assert.False(result.Succeeded, "`-` in a required column was accepted.");

        Assert.Contains("`Needed.Hp` has no value, and the sheet declares the column required",
                        result.StdOut);

        // The way out, spelled in this layout's own notation - which is the layout's to
        // spell, and the message is where an author finds it.
        Assert.Contains("type the column `int?`", result.StdOut);
    }

    /// <summary>
    /// An index has none either: it is what identifies the row.
    /// </summary>
    /// <remarks>
    /// Reported for the reason an optional index is refused where it is declared: absence
    /// parses to the type's empty value, so every row saying it has none would share the key
    /// `0` - and the failure would surface as duplicate keys, or in a table with one such row
    /// as nothing at all.
    /// </remarks>
    [Fact]
    public void An_index_refuses_no_value()
    {
        var result = TabbitRunner.Convert("no-value-refused");

        Assert.False(result.Succeeded, "`-` in an index was accepted.");

        Assert.Contains("`Keyless.Index` identifies the row", result.StdOut);
        Assert.Contains("an index cannot be absent", result.StdOut);
    }

    /// <summary>
    /// One element of an array cell cannot say it has no value.
    /// </summary>
    /// <remarks>
    /// Where the column did not say its elements may be absent. A cell holding `a;-;b` was
    /// written by someone who meant either the mark or the character, so the message names
    /// both - and the third way out, which is to declare the elements optional.
    /// spec/types/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void An_array_element_refuses_no_value()
    {
        var result = TabbitRunner.Convert("no-value-element");

        Assert.False(result.Succeeded, "`-` as an array element was accepted.");

        Assert.Contains("Element 2 of this cell is `-`", result.StdOut);
        Assert.Contains("this column's elements are required", result.StdOut);
    }

    /// <summary>
    /// A blank where a number belongs is refused, and the refusal names the cell.
    /// </summary>
    /// <remarks>
    /// The strict default. What it stops is a row somebody stopped filling in becoming a zero
    /// that nothing downstream can tell from a zero somebody typed.
    /// </remarks>
    [Fact]
    public void A_blank_where_a_number_belongs_is_refused()
    {
        var result = TabbitRunner.Convert("blank-cell-strict");

        Assert.False(result.Succeeded, "A blank in an `int` column was accepted.");

        Assert.Contains("This cell is empty, and a value of type `Int32` belongs here", result.StdOut);
        Assert.Contains("blank-cell.xlsx", result.StdOut);

        // The three ways out, since between them they cover what the author may have meant.
        Assert.Contains("Write one", result.StdOut);
        Assert.Contains("`-` to say this row has no value", result.StdOut);
        Assert.Contains("OnBlankCell", result.StdOut);
    }

    /// <summary>
    /// `OnBlankCell: "empty"` reads that cell as the type's empty value, and warns for it.
    /// </summary>
    /// <remarks>
    /// A value and not an absence: the concession is about a cell nobody filled in, and a row
    /// that has no value says so with `-`. So the zero here is present, which is what the
    /// consumer sees and what the presence bit carries.
    ///
    /// The warning is per column rather than per cell, and it is what hands the count back to
    /// whoever owns the sheet.
    /// </remarks>
    [Fact]
    public void The_blank_cell_concession_reads_it_as_the_empty_value()
    {
        var result = TabbitRunner.Convert("blank-cell-empty");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("blank-cell-empty"), "json-named", "Reading.json"))).RootElement;

        // Present, and zero - not null, which is what `-` would have produced.
        Assert.Equal(JsonValueKind.Number, rows[1].GetProperty("value").ValueKind);
        Assert.Equal(0, rows[1].GetProperty("value").GetInt32());

        Assert.Contains("`Reading.Value` holds cells nobody filled in", result.StdOut);
        Assert.Contains("OnBlankCell", result.StdOut);
    }
}
