using System.Collections.Generic;
using System.Linq;
using Google.Apis.Sheets.v4.Data;
using Tabbit.Importers;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Tabbit.Sources;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Reading a Google Sheets document's defined names, and putting them onto a cell grid.
/// </summary>
/// <remarks>
/// The API's own response objects are built here rather than fetched, because the fetch
/// wants a network and a consented OAuth token - neither of which belongs in a gate. What is
/// ours is everything after the response arrives, and that is what these cover.
///
/// This exists because the importer read no names at all, and the failure was a run that
/// succeeded with nothing in it: a layout that finds its tables by defined name treats a
/// sheet no name covers as an ordinary working sheet, so every sheet was skipped and the
/// conversion reported success with zero tables.
/// </remarks>
public class GoogleSheetsNamedRangeTests
{
    // ---- reading the response ----

    private static Spreadsheet Document(params NamedRange[] names)
        => new Spreadsheet { NamedRanges = names.ToList() };

    private static NamedRange Name(
        string name, int sheetId, int? firstRow, int? endRow, int? firstColumn, int? endColumn)
        => new NamedRange
        {
            Name = name,
            Range = new GridRange
            {
                SheetId = sheetId,
                StartRowIndex = firstRow,
                EndRowIndex = endRow,
                StartColumnIndex = firstColumn,
                EndColumnIndex = endColumn,
            },
        };

    /// <summary>
    /// A name is filed under the sheet its range points into, which the API says by id and
    /// never by title.
    /// </summary>
    [Fact]
    public void Names_are_grouped_by_the_sheet_id_they_point_into()
    {
        var resolved = GoogleSheetsImporter.ResolveNamedRanges(
            Document(
                Name("First", sheetId: 0, 0, 4, 0, 3),
                Name("Second", sheetId: 1806, 0, 4, 0, 3)),
            "Doc");

        Assert.Equal([0, 1806], resolved.Keys.OrderBy(id => id));
        Assert.Equal("First", Assert.Single(resolved[0]).Name);
        Assert.Equal("Second", Assert.Single(resolved[1806]).Name);
    }

    /// <summary>
    /// One sheet holds several tables, side by side, because a name can cover any rectangle.
    /// </summary>
    [Fact]
    public void One_sheet_can_hold_several_names()
    {
        var resolved = GoogleSheetsImporter.ResolveNamedRanges(
            Document(
                Name("Left", sheetId: 7, 0, 4, 0, 3),
                Name("Right", sheetId: 7, 0, 4, 5, 9)),
            "Doc");

        Assert.Equal(["Left", "Right"], resolved[7].Select(name => name.Name));
    }

    /// <summary>
    /// The API's end indexes are exclusive and a workbook's reference is inclusive. Getting
    /// this wrong costs one row and one column of every table, which is the header row of
    /// most layouts.
    /// </summary>
    [Fact]
    public void The_exclusive_end_index_becomes_an_inclusive_last_coordinate()
    {
        var resolved = GoogleSheetsImporter.ResolveNamedRanges(
            Document(Name("T", sheetId: 0, firstRow: 2, endRow: 12, firstColumn: 1, endColumn: 5)),
            "Doc");

        var named = Assert.Single(resolved[0]);

        Assert.Equal(2, named.FirstRow);
        Assert.Equal(11, named.LastRow);
        Assert.Equal(1, named.FirstColumn);
        Assert.Equal(4, named.LastColumn);
    }

    /// <summary>
    /// A name covering a whole column arrives with its row indexes absent, which means "as
    /// far as the sheet goes" rather than a rectangle of known extent.
    /// </summary>
    [Fact]
    public void An_unbounded_range_is_not_a_rectangle_and_is_skipped()
    {
        var resolved = GoogleSheetsImporter.ResolveNamedRanges(
            Document(
                Name("WholeColumn", sheetId: 0, firstRow: null, endRow: null, firstColumn: 0, endColumn: 1),
                Name("Real", sheetId: 0, 0, 4, 0, 3)),
            "Doc");

        Assert.Equal("Real", Assert.Single(resolved[0]).Name);
    }

    /// <summary>A name whose target was deleted has no range to read.</summary>
    [Fact]
    public void A_name_with_no_range_is_skipped()
    {
        var resolved = GoogleSheetsImporter.ResolveNamedRanges(
            Document(new NamedRange { Name = "Dangling", Range = null }),
            "Doc");

        Assert.Empty(resolved);
    }

    /// <summary>
    /// The rectangle is carried as numbers and never as text, so what a diagnostic says it
    /// covers is written here - in the notation a workbook's own reference uses.
    /// </summary>
    [Fact]
    public void A_rectangle_is_described_the_way_a_workbook_reference_is()
    {
        var resolved = GoogleSheetsImporter.ResolveNamedRanges(
            Document(Name("T", sheetId: 0, firstRow: 0, endRow: 10, firstColumn: 0, endColumn: 27)),
            "Doc");

        Assert.Equal("A1:AA10", Assert.Single(resolved[0]).Reference);
    }

    // ---- putting them onto the grid ----

    /// <summary>
    /// A grid whose rows carry the sheet coordinates they came from, which is what the
    /// translation is against.
    /// </summary>
    /// <param name="firstRow">Where the grid sits in the sheet, as Optimize leaves it.</param>
    private static RawSheet Grid(int firstRow, int firstColumn, int height, int width)
    {
        var location = new Location { Filename = "googlesheets.Doc", Sheet = "Tab" };

        return new RawSheet
        {
            Location = location.CloneWithXY(firstColumn, firstRow),
            ColumnCount = width,
            Rows = Enumerable.Range(0, height).Select(row =>
                Enumerable.Range(0, width).Select(column => new RawCell
                {
                    Location = location.CloneWithXY(firstColumn + column, firstRow + row),
                    Value = "x",
                    Note = "",
                }).ToList()).ToList(),
        };
    }

    private static SheetNamedRange Rectangle(
        string name, int firstRow, int firstColumn, int lastRow, int lastColumn)
        => new SheetNamedRange(name, $"{name}!ref", firstRow, firstColumn, lastRow, lastColumn);

    /// <summary>
    /// Coordinates are indexes into the trimmed grid, not the sheet's - so a grid that
    /// starts partway down the sheet shifts every name by the same offset.
    /// </summary>
    [Fact]
    public void A_name_is_translated_against_where_the_trimmed_grid_starts()
    {
        var sheet = Grid(firstRow: 3, firstColumn: 2, height: 10, width: 6);

        SheetNamedRanges.Attach(
            sheet,
            [Rectangle("T", firstRow: 5, firstColumn: 4, lastRow: 8, lastColumn: 6)],
            SheetFilter.All, "Doc", "googlesheets.Doc");

        var named = Assert.Single(sheet.NamedRanges);

        Assert.Equal(2, named.Row);
        Assert.Equal(2, named.Column);
        Assert.Equal(4, named.Height);
        Assert.Equal(3, named.Width);
    }

    /// <summary>
    /// A range drawn generously over trailing blanks is ordinary, and those blanks are
    /// exactly what Optimize removes. The table is the cells that exist.
    /// </summary>
    [Fact]
    public void A_name_reaching_past_the_grid_is_clamped_to_it()
    {
        var sheet = Grid(firstRow: 0, firstColumn: 0, height: 4, width: 3);

        SheetNamedRanges.Attach(
            sheet,
            [Rectangle("T", firstRow: 0, firstColumn: 0, lastRow: 999, lastColumn: 999)],
            SheetFilter.All, "Doc", "googlesheets.Doc");

        var named = Assert.Single(sheet.NamedRanges);

        Assert.Equal(4, named.Height);
        Assert.Equal(3, named.Width);
    }

    /// <summary>A name pointing outside the grid entirely has no cells to give.</summary>
    [Fact]
    public void A_name_outside_the_grid_is_dropped()
    {
        var sheet = Grid(firstRow: 0, firstColumn: 0, height: 4, width: 3);

        SheetNamedRanges.Attach(
            sheet,
            [Rectangle("Below", firstRow: 40, firstColumn: 0, lastRow: 44, lastColumn: 2)],
            SheetFilter.All, "Doc", "googlesheets.Doc");

        Assert.Empty(sheet.NamedRanges);
    }

    /// <summary>
    /// This source omits interior blank rows rather than sending them, and
    /// <see cref="RawSheet.Optimize"/> fills the gaps back in. A name has to land on the
    /// restored grid, which is the one place the two sources meet a different shape.
    /// </summary>
    [Fact]
    public void A_name_lands_correctly_on_a_grid_whose_blank_rows_were_restored()
    {
        var location = new Location { Filename = "googlesheets.Doc", Sheet = "Tab" };

        // Sheet rows 0, 1 and 4. Rows 2 and 3 are blank and so were never sent.
        var sheet = new RawSheet
        {
            Location = location.CloneWithXY(0, 0),
            Rows = new[] { 0, 1, 4 }.Select(row =>
                Enumerable.Range(0, 2).Select(column => new RawCell
                {
                    Location = location.CloneWithXY(column, row),
                    Value = "x",
                    Note = "",
                }).ToList()).ToList(),
        };

        Assert.True(sheet.Optimize());
        Assert.Equal(5, sheet.Rows.Count);

        SheetNamedRanges.Attach(
            sheet,
            [Rectangle("T", firstRow: 3, firstColumn: 0, lastRow: 4, lastColumn: 1)],
            SheetFilter.All, "Doc", "googlesheets.Doc");

        var named = Assert.Single(sheet.NamedRanges);

        Assert.Equal(3, named.Row);
        Assert.Equal(2, named.Height);
    }

    /// <summary>
    /// In a layout that reads defined names the name is what a table is called, so selecting
    /// tables in a recipe means selecting names.
    /// </summary>
    [Fact]
    public void The_recipe_selects_tables_by_naming_them()
    {
        var filter = SheetFilter.From(
            new RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe
            {
                IncludeSheets = ["Wanted"],
            },
            "Sources.GoogleSheets[0]");

        var sheet = Grid(firstRow: 0, firstColumn: 0, height: 4, width: 3);

        SheetNamedRanges.Attach(
            sheet,
            [
                Rectangle("Wanted", 0, 0, 3, 2),
                Rectangle("Dropdown", 0, 0, 3, 0),
            ],
            filter, "Doc", "googlesheets.Doc");

        Assert.Equal("Wanted", Assert.Single(sheet.NamedRanges).Name);
    }
}
