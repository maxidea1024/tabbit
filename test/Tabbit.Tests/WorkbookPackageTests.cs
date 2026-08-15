using Tabbit.Importers.Xlsx;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The parts of reading a workbook's names and notes that are ours rather than a library's.
/// </summary>
/// <remarks>
/// These exist because the fixture workbooks carry neither a defined name nor a cell note -
/// `xlsx-reader.tsv` records `names=0 notes=0` for all of them - so nothing else in the
/// suite reaches this code. The layout tests build their named ranges in memory, which
/// checks the layout's rules and not the reading of a reference.
///
/// The shapes that must be refused are the point. A reference that is not one rectangle has
/// to be skipped and reported, because the alternative is converting a guess: a whole column
/// has no extent, a union is two tables, and a reference into another workbook names cells
/// this run cannot see.
/// </remarks>
public class WorkbookPackageTests
{
    // ---- references that resolve ----

    [Fact]
    public void A_reference_resolves_to_its_sheet_and_rectangle()
    {
        var area = WorkbookPackage.TryParseArea("Sheet1!$A$1:$D$10");

        Assert.NotNull(area);
        Assert.Equal("Sheet1", area.SheetName);
        Assert.Equal(0, area.FirstRow);
        Assert.Equal(0, area.FirstColumn);
        Assert.Equal(9, area.LastRow);
        Assert.Equal(3, area.LastColumn);
    }

    /// <summary>A sheet name holding a space arrives quoted.</summary>
    [Fact]
    public void A_quoted_sheet_name_keeps_its_spaces()
    {
        var area = WorkbookPackage.TryParseArea("'Ocean Zone'!$A$1:$IP$100");

        Assert.NotNull(area);
        Assert.Equal("Ocean Zone", area.SheetName);

        // IP is bijective base 26: (9 * 26 + 16) - 1. Getting this wrong is what once made
        // every reference past column X point at the wrong cell.
        Assert.Equal(249, area.LastColumn);
        Assert.Equal(99, area.LastRow);
    }

    /// <summary>Inside quotes, a literal apostrophe is doubled.</summary>
    [Fact]
    public void A_doubled_apostrophe_is_one_character_of_the_name()
    {
        var area = WorkbookPackage.TryParseArea("'It''s Here'!$B$2");

        Assert.NotNull(area);
        Assert.Equal("It's Here", area.SheetName);
        Assert.Equal(1, area.FirstRow);
        Assert.Equal(1, area.FirstColumn);
    }

    /// <summary>A single cell is a rectangle of one.</summary>
    [Fact]
    public void A_single_cell_reference_is_a_rectangle()
    {
        var area = WorkbookPackage.TryParseArea("Sheet1!$C$5");

        Assert.NotNull(area);
        Assert.Equal(4, area.FirstRow);
        Assert.Equal(4, area.LastRow);
        Assert.Equal(2, area.FirstColumn);
        Assert.Equal(2, area.LastColumn);
    }

    /// <summary>
    /// Corners in either order describe the same rectangle, so they are normalised.
    /// </summary>
    [Fact]
    public void Corners_are_ordered_whichever_way_they_were_written()
    {
        var area = WorkbookPackage.TryParseArea("Sheet1!$D$10:$A$1");

        Assert.NotNull(area);
        Assert.Equal(0, area.FirstRow);
        Assert.Equal(0, area.FirstColumn);
        Assert.Equal(9, area.LastRow);
        Assert.Equal(3, area.LastColumn);
    }

    /// <summary>The `$` is absolute-reference notation and means nothing to a stored name.</summary>
    [Fact]
    public void A_reference_without_dollars_reads_the_same()
    {
        var withDollars = WorkbookPackage.TryParseArea("Sheet1!$A$1:$B$2");
        var without = WorkbookPackage.TryParseArea("Sheet1!A1:B2");

        Assert.NotNull(without);
        Assert.Equal(withDollars.LastRow, without.LastRow);
        Assert.Equal(withDollars.LastColumn, without.LastColumn);
    }

    // ---- references that must be refused ----

    [Theory]
    // A whole column and a whole row: no extent to read.
    [InlineData("Sheet1!$A:$A")]
    [InlineData("Sheet1!$1:$1")]
    // A union is not one rectangle.
    [InlineData("Sheet1!$A$1:$B$2,Sheet1!$D$4")]
    // Another workbook's cells, which this run cannot see.
    [InlineData("[1]Sheet1!$A$1")]
    [InlineData("'[1]Sheet 1'!$A$1")]
    // No sheet named at all.
    [InlineData("$A$1:$B$2")]
    [InlineData("A1")]
    // Truncated and malformed.
    [InlineData("Sheet1!")]
    [InlineData("!$A$1")]
    [InlineData("'Unclosed!$A$1")]
    [InlineData("Sheet1!$A$1:$B$2:$C$3")]
    [InlineData("Sheet1!$A$0")]
    [InlineData("")]
    public void A_reference_that_is_not_one_rectangle_is_refused(string reference)
        => Assert.Null(WorkbookPackage.TryParseArea(reference));

    // ---- cell references ----

    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("Z1", 0, 25)]
    [InlineData("AA1", 0, 26)]
    [InlineData("ZZ1", 0, 701)]
    [InlineData("AAA1", 0, 702)]
    [InlineData("B12", 11, 1)]
    public void A_cell_reference_reads_as_a_zero_based_position(string cell, int row, int column)
    {
        Assert.True(WorkbookPackage.TryParseCell(cell, out int gotRow, out int gotColumn));
        Assert.Equal(row, gotRow);
        Assert.Equal(column, gotColumn);
    }

    [Theory]
    [InlineData("A")]      // no row
    [InlineData("1")]      // no column
    [InlineData("A1B")]    // digits then letters again
    [InlineData("A0")]     // rows count from one
    [InlineData("")]
    public void A_malformed_cell_reference_is_refused(string cell)
        => Assert.False(WorkbookPackage.TryParseCell(cell, out _, out _));

    // ---- escaped characters ----

    /// <summary>
    /// The format spells characters XML cannot carry as `_xHHHH_`. A carriage return is the
    /// one that occurs, and passing it through put the literal text `_x000D_` into a note -
    /// which is the defect that decided the reader this tool uses.
    /// </summary>
    [Fact]
    public void An_escaped_carriage_return_becomes_one()
        => Assert.Equal("first\rsecond", WorkbookPackage.Unescape("first_x000D_second"));

    [Theory]
    [InlineData("_x0041_", "A")]
    [InlineData("a_x000D_b_x000D_c", "a\rb\rc")]
    // Nothing that is not the escape is touched, including the near misses.
    [InlineData("plain text", "plain text")]
    [InlineData("under_score", "under_score")]
    [InlineData("_x00_", "_x00_")]
    [InlineData("_xZZZZ_", "_xZZZZ_")]
    [InlineData("trailing_x000D", "trailing_x000D")]
    [InlineData("", "")]
    public void Escapes_are_read_and_everything_else_is_left_alone(string text, string expected)
        => Assert.Equal(expected, WorkbookPackage.Unescape(text));

    // ---- the author prefix of a note ----

    /// <summary>
    /// A spreadsheet program puts the author's name at the head of a note. What is left
    /// becomes the doc comment of whatever the cell declares, and the name is not part of it.
    /// </summary>
    [Fact]
    public void The_author_prefix_is_removed()
        => Assert.Equal("what the column means",
            WorkbookPackage.StripAuthorPrefix("Somebody:\nwhat the column means"));

    [Theory]
    // A colon with no line break after it is part of the text.
    [InlineData("ratio: one to three", "ratio: one to three")]
    // No colon at all.
    [InlineData("just a note", "just a note")]
    // Surrounding whitespace goes either way.
    [InlineData("  padded  ", "padded")]
    [InlineData("", "")]
    public void Text_that_is_not_an_author_prefix_is_kept(string text, string expected)
        => Assert.Equal(expected, WorkbookPackage.StripAuthorPrefix(text));
}
