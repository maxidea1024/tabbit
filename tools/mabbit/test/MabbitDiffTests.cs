using System.Linq;
using Xunit;

namespace Mabbit.Tests;

/// <summary>
/// What the workbook comparison says two files differ by.
///
/// The case that decides whether this tool is worth having is the inserted row. A
/// comparison that matched rows by position reports it and every row below it as changed,
/// which is the same answer as "the file was rewritten" and no use to anybody resolving a
/// conflict. Matching by key reports the one row that arrived, and the tests below are
/// mostly about that distinction and about what happens where a key cannot be trusted.
/// </summary>
public class MabbitDiffTests
{
    private static readonly string[] Header = ["id", "name", "price"];

    private static WorkbookGrid Workbook(string name, params string[][] rows)
        => WorkbookGrid.Of(name, ("Item", rows));

    private static DiffResult Compare(WorkbookGrid before, WorkbookGrid after)
    {
        var schema = new HeuristicSchema();

        return WorkbookDiff.Compare(
            before.Name, TableViews.Of(before, schema),
            after.Name, TableViews.Of(after, schema));
    }

    [Fact]
    public void IdenticalWorkbooksDifferInNothing()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"], ["2", "Shield", "80"]);
        var after = Workbook("b", Header, ["1", "Sword", "100"], ["2", "Shield", "80"]);

        Assert.True(Compare(before, after).IsEmpty);
    }

    [Fact]
    public void ARowInsertedInTheMiddleIsOneChangeAndNotEveryRowBelowIt()
    {
        var before = Workbook("a", Header,
            ["1", "Sword", "100"],
            ["2", "Shield", "80"],
            ["3", "Potion", "10"]);

        var after = Workbook("b", Header,
            ["1", "Sword", "100"],
            ["9", "Bow", "140"],
            ["2", "Shield", "80"],
            ["3", "Potion", "10"]);

        var table = Assert.Single(Compare(before, after).Tables);
        var row = Assert.Single(table.Rows);

        Assert.Equal(ChangeKind.Added, row.Kind);
        Assert.Equal("9", row.Key);

        // The row's own position, not the position it displaced.
        Assert.Equal("Item!A3", row.Location);
    }

    [Fact]
    public void AChangedCellNamesItsColumnAndItsCell()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"]);
        var after = Workbook("b", Header, ["1", "Long Sword", "100"]);

        var table = Assert.Single(Compare(before, after).Tables);
        var row = Assert.Single(table.Rows);
        var cell = Assert.Single(row.Cells);

        Assert.Equal(ChangeKind.Modified, row.Kind);
        Assert.Equal("name", cell.Column);
        Assert.Equal("Sword", cell.Before);
        Assert.Equal("Long Sword", cell.After);
        Assert.Equal("Item!B2", cell.Location);
    }

    [Fact]
    public void AReorderedTableIsNotAChange()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"], ["2", "Shield", "80"]);
        var after = Workbook("b", Header, ["2", "Shield", "80"], ["1", "Sword", "100"]);

        // Moving a row changes no value, and a merge has nothing to resolve about it. A
        // position-based comparison calls this two changed rows.
        Assert.True(Compare(before, after).IsEmpty);
    }

    [Fact]
    public void ARemovedRowIsReportedAgainstWhereItWas()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"], ["2", "Shield", "80"]);
        var after = Workbook("b", Header, ["1", "Sword", "100"]);

        var table = Assert.Single(Compare(before, after).Tables);
        var row = Assert.Single(table.Rows);

        Assert.Equal(ChangeKind.Removed, row.Kind);
        Assert.Equal("2", row.Key);
        Assert.Equal("Item!A3", row.Location);
    }

    [Fact]
    public void AnAddedColumnIsReportedOnceAndNotAsACellPerRow()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"], ["2", "Shield", "80"]);

        var after = WorkbookGrid.Of("b", ("Item", [
            ["id", "name", "price", "grade"],
            ["1", "Sword", "100", "A"],
            ["2", "Shield", "80", "B"]]));

        var table = Assert.Single(Compare(before, after).Tables);
        var column = Assert.Single(table.Columns);

        Assert.Equal(ChangeKind.Added, column.Kind);
        Assert.Equal("grade", column.Name);

        // The rows themselves did not change, and saying so once per row would bury the
        // rows that did.
        Assert.Empty(table.Rows);
    }

    [Fact]
    public void ARowWithNoKeyIsReportedRatherThanDropped()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"]);
        var after = Workbook("b", Header, ["1", "Sword", "100"], ["", "Nameless", "5"]);

        var result = Compare(before, after);

        // The row cannot be followed, so there is nothing to say it changed - but a merge
        // will not be able to say anything about it either, and that is worth knowing.
        Assert.Empty(result.Tables);
        Assert.Contains(result.Notes, note => note.Text.Contains("Item!A3"));
    }

    [Fact]
    public void ARepeatedKeyIsReportedAndTheFirstRowIsTheOneFollowed()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"]);
        var after = Workbook("b", Header, ["1", "Sword", "100"], ["1", "Sword copy", "100"]);

        var result = Compare(before, after);

        // The first row matched and is unchanged, so nothing is reported as a row change -
        // but the file is not clean and the note is what says so.
        Assert.Empty(result.Tables);
        Assert.Contains(result.Notes, note => note.Text.Contains("repeat a key"));
    }

    [Fact]
    public void AFileComparedAgainstItselfDiffersInNothingHoweverUntidyItIs()
    {
        // Notes describe one file, not a difference between two. Counting them as changes
        // had a workbook report a changed table against a byte-identical copy of itself,
        // which is the one answer a comparison must never give wrongly.
        var untidy = Workbook("a", Header,
            ["1", "Sword", "100"],
            ["1", "Sword again", "100"],
            ["", "Nameless", "5"]);

        var copy = Workbook("b", Header,
            ["1", "Sword", "100"],
            ["1", "Sword again", "100"],
            ["", "Nameless", "5"]);

        var result = Compare(untidy, copy);

        Assert.True(result.IsEmpty);
        Assert.NotEmpty(result.Notes);
        Assert.Contains("No difference in any table.", DiffReport.Text(result));
    }

    [Fact]
    public void ASheetOnlyOneSideHasIsAWholeTable()
    {
        var before = WorkbookGrid.Of("a", ("Item", [Header, ["1", "Sword", "100"]]));

        var after = WorkbookGrid.Of("b",
            ("Item", [Header, ["1", "Sword", "100"]]),
            ("Skill", [["id", "power"], ["1", "5"], ["2", "9"]]));

        var table = Assert.Single(Compare(before, after).Tables);

        Assert.Equal("Skill", table.Name);
        Assert.Equal(ChangeKind.Added, table.Kind);
        Assert.Equal(2, table.RowCount);
    }

    [Fact]
    public void TheKeyColumnCanBeSaidWhenItIsNotTheFirstOne()
    {
        string[] header = ["region", "id", "price"];

        var before = WorkbookGrid.Of("a", ("Item", [header, ["kr", "1", "100"], ["kr", "2", "80"]]));
        var after = WorkbookGrid.Of("b", ("Item", [header, ["kr", "1", "100"], ["kr", "2", "90"]]));

        var schema = new HeuristicSchema(new System.Collections.Generic.Dictionary<string, string>
        {
            ["Item"] = "id",
        });

        var result = WorkbookDiff.Compare(
            "a", TableViews.Of(before, schema),
            "b", TableViews.Of(after, schema));

        var row = Assert.Single(Assert.Single(result.Tables).Rows);

        // Without the override every row shares the key `kr` and only the first of them is
        // ever compared, which hides the change on the second.
        Assert.Equal("2", row.Key);
        Assert.Equal("price", Assert.Single(row.Cells).Column);
    }

    [Fact]
    public void TheReportNamesEveryChangedCell()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"]);
        var after = Workbook("b", Header, ["1", "Sword", "120"]);

        string report = DiffReport.Text(Compare(before, after));

        Assert.Contains("price:  100  ->  120", report);
        Assert.Contains("Item!C2", report);
    }

    [Fact]
    public void AClearedCellIsVisibleInTheReport()
    {
        var before = Workbook("a", Header, ["1", "Sword", "100"]);
        var after = Workbook("b", Header, ["1", "", "100"]);

        string report = DiffReport.Text(Compare(before, after));

        // An arrow with nothing after it depends on the reader noticing a gap.
        Assert.Contains("Sword  ->  (empty)", report);
    }
}

/// <summary>
/// Reading a real workbook off disk, which the comparison tests deliberately do not do.
///
/// They build their grids in memory so that what they check is the comparison. This checks
/// the other half: that a file arrives as the cells it holds, in both of the formats that
/// matter - the zip of XML and the binary one, which are different code inside the reader.
/// </summary>
public class MabbitWorkbookReadTests
{
    private const string Xlsx = "fixtures/workbook.xlsx";
    private const string Xlsb = "fixtures/workbook.xlsb";

    [Fact]
    public void AWorkbookReadsAsItsCells()
    {
        var grid = WorkbookGrid.Read(Xlsx);

        Assert.NotEmpty(grid.Sheets);

        var sheet = grid.Sheets.First(s => !s.IsEmpty);

        // A sheet reports the rectangle its content occupies, and the first cell of that
        // rectangle holds something - which is what every schema starts from.
        Assert.NotEqual("", sheet.Cell(sheet.FirstRow, sheet.FirstColumn));
    }

    [Fact]
    public void AFileUnderATemporaryNameReadsWhenThePathSaysTheFormat()
    {
        string temporary = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mabbit_merge_file_" + System.IO.Path.GetRandomFileName());

        System.IO.File.Copy(Xlsx, temporary, overwrite: true);

        try
        {
            // What a version control system hands its merge driver: the content of a
            // workbook under a name that says nothing about what it is.
            Assert.Throws<MabbitException>(() => WorkbookGrid.Read(temporary));

            var grid = WorkbookGrid.Read(temporary, formatFrom: "data/Items.xlsx");
            Assert.NotEmpty(grid.Sheets);
        }
        finally
        {
            System.IO.File.Delete(temporary);
        }
    }

    [Fact]
    public void TheBinaryFormatReadsToo()
    {
        var grid = WorkbookGrid.Read(Xlsb);

        Assert.NotEmpty(grid.Sheets);

        var sheet = grid.Sheets.First(s => !s.IsEmpty);
        Assert.NotEqual("", sheet.Cell(sheet.FirstRow, sheet.FirstColumn));
    }

    [Fact]
    public void ABinaryWorkbookComparesAgainstItselfAsUnchanged()
    {
        var schema = new HeuristicSchema();

        var before = WorkbookGrid.Read(Xlsb, reportAs: "a");
        var after = WorkbookGrid.Read(Xlsb, reportAs: "b");

        var result = WorkbookDiff.Compare(
            before.Name, TableViews.Of(before, schema),
            after.Name, TableViews.Of(after, schema));

        // Reading is one thing and reading the same way twice is another: a reader that
        // returned a number formatted through the machine's locale, or that walked a sheet
        // in a different order on a second pass, would show up right here.
        Assert.True(result.IsEmpty);
    }
}
