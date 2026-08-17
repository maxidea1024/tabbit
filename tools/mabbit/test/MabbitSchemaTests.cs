using System.Linq;
using Xunit;

namespace Mabbit.Tests;

/// <summary>
/// Reading the table geometry out of the file the converter writes.
///
/// This is what separates a guess from an answer. The heuristic takes a whole sheet as a
/// table, which is right for a sheet that is nothing but one; the file says where the table
/// actually is, which is the only way the merge knows what lies outside it - and therefore the
/// only way it can tell that somebody's note under the table would be written over.
/// </summary>
public class MabbitSchemaTests
{
    private const string Schema = "fixtures/schema.json";
    private const string Workbook = "fixtures/workbook.xlsx";

    [Fact]
    public void TheSchemaSaysWhereTheTableIsRatherThanTakingTheWholeSheet()
    {
        var grid = WorkbookGrid.Read(Workbook);

        var region = Assert.Single(SchemaFile.Read(Schema, "data/workbook.xlsx").TablesIn(grid));

        Assert.Equal("Enums", region.Name);
        Assert.Equal("TableEnums", region.Sheet);
        Assert.Equal(6, region.LastDataRow);
        Assert.Equal(2, region.LastColumn);

        // The sheet holds far more than that, which is the whole difference: everything past
        // row 6 is outside the table and a merge must not write there.
        var sheet = grid.Sheets.First(s => !s.IsEmpty);
        Assert.True(sheet.LastRow > region.LastDataRow);
    }

    [Fact]
    public void ATableFromTheSchemaReadsItsRowsByKey()
    {
        var grid = WorkbookGrid.Read(Workbook);
        var view = Assert.Single(TableViews.Of(grid, SchemaFile.Read(Schema, "data/workbook.xlsx")));

        Assert.Equal(3, view.Columns.Count);
        Assert.NotEmpty(view.Rows);

        // Every row it reports is inside the rectangle the schema named.
        Assert.All(view.Rows, row => Assert.InRange(row.RowIndex, 1, 6));
    }

    [Fact]
    public void TheSchemaIsMatchedByFileNameSoATemporaryPathStillWorks()
    {
        // What a merge driver is handed: content under a generated name, and the repository
        // path beside it. Matching on the path is what makes the schema findable at all.
        var byPath = SchemeCount("data/workbook.xlsx");
        var byNameOnly = SchemeCount("workbook.xlsx");
        var byOtherDirectory = SchemeCount("some/other/place/workbook.xlsx");

        Assert.Equal(byPath, byNameOnly);
        Assert.Equal(byPath, byOtherDirectory);
    }

    private static int SchemeCount(string workbook)
        => SchemaFile.Read(Schema, workbook).TablesIn(WorkbookGrid.Read(Workbook)).Count;

    [Fact]
    public void AWorkbookTheSchemaDoesNotDescribeIsRefusedWithWhatItDoesDescribe()
    {
        var error = Assert.Throws<MabbitException>(
            () => SchemaFile.Read(Schema, "data/Nothing.xlsx"));

        // Being handed another project's schema is the likely mistake, and "no tables" would
        // read as "this workbook is empty".
        Assert.Contains("Nothing.xlsx", error.Message);
        Assert.Contains("CollectionData.xlsx", error.Message);
    }

    [Fact]
    public void ASchemaFileThatIsNotThereIsSaidSoRatherThanGuessedAround()
    {
        var error = Assert.Throws<MabbitException>(
            () => SchemaFile.Read("fixtures/no-such-schema.json", "data/workbook.xlsx"));

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public void ASheetTheSchemaNamesButThisFileLacksIsSkippedRatherThanFailing()
    {
        // Three versions of one workbook, and one of them may not have a sheet yet. Which is
        // something the merge judges - a table arriving - rather than something to fail on
        // while reading the schema.
        var empty = WorkbookGrid.Of("empty", ("Other", [["id"], ["1"]]));

        Assert.Empty(SchemaFile.Read(Schema, "data/workbook.xlsx").TablesIn(empty));
    }

    [Fact]
    public void AMergeUnderTheSchemaSeesWhatLiesOutsideTheTable()
    {
        var schema = SchemaFile.Read(Schema, "data/workbook.xlsx");
        var grid = WorkbookGrid.Read(Workbook);

        // Under the heuristic the table is the whole sheet, so there is nothing outside it
        // and this check can never fire. Under the schema there is, which is the case section
        // 4.5 exists for.
        var outside = WorkbookMerge.OutsideTables(grid, grid, grid, schema);

        Assert.Empty(outside);

        var region = schema.TablesIn(grid).Single();
        var sheet = grid.Sheets.First(s => !s.IsEmpty);

        Assert.Contains(sheet.NonEmptyCells(), c => c.Row > region.LastDataRow);
    }
}
