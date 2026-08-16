using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mabbit.Tests;

/// <summary>
/// The three-way judgement, one test per row of the tables in sections 4.3 to 4.5 of
/// spec/workbook-merge.md.
///
/// Every one of them is about the same distinction: a side that did not change something has
/// nothing to say about it, and two sides that changed the same thing differently is the only
/// case a program may not settle. What makes these worth writing individually is that getting
/// one of them wrong is silent - the merge succeeds and somebody's edit is gone.
/// </summary>
public class MabbitMergeTests
{
    private static readonly string[] Header = ["id", "name", "price"];

    private static WorkbookGrid Book(string name, params string[][] rows)
        => WorkbookGrid.Of(name, ("Item", rows));

    private static MergePlan Judge(WorkbookGrid inBase, WorkbookGrid mine, WorkbookGrid theirs)
    {
        var schema = new HeuristicSchema();

        return WorkbookMerge.Judge(
            "base", TableViews.Of(inBase, schema),
            "mine", TableViews.Of(mine, schema),
            "theirs", TableViews.Of(theirs, schema));
    }

    private static TableMerge OneTable(MergePlan plan) => Assert.Single(plan.Tables);

    // ---- section 4.3, the cell ------------------------------------------------------

    [Fact]
    public void BothSidesMadeTheSameEditIsNotAChangeToMake()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "120"]);
        var b = Book("b", Header, ["1", "Sword", "120"]);

        Assert.Empty(Judge(o, a, b).Tables);
    }

    [Fact]
    public void OnlyTheOtherSideEditedTheCellSoItsValueIsTaken()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "100"]);
        var b = Book("b", Header, ["1", "Sword", "120"]);

        var cell = Assert.Single(Assert.Single(OneTable(Judge(o, a, b)).Rows).Cells);

        Assert.Equal(CellVerdict.TakeTheirs, cell.Verdict);
        Assert.Equal("price", cell.Column);
        Assert.Equal("120", cell.Theirs);
        Assert.Equal("Item!C2", cell.Location);
    }

    [Fact]
    public void OnlyThisSideEditedTheCellSoThereIsNothingToDo()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "120"]);
        var b = Book("b", Header, ["1", "Sword", "100"]);

        Assert.Empty(Judge(o, a, b).Tables);
    }

    [Fact]
    public void BothSidesEditedTheCellDifferentlyIsAConflictShowingAllThree()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "120"]);
        var b = Book("b", Header, ["1", "Sword", "140"]);

        var plan = Judge(o, a, b);
        var cell = Assert.Single(Assert.Single(OneTable(plan).Rows).Cells);

        Assert.Equal(CellVerdict.Conflict, cell.Verdict);
        Assert.Equal("100", cell.Base);
        Assert.Equal("120", cell.Mine);
        Assert.Equal("140", cell.Theirs);
        Assert.True(plan.HasConflicts);
    }

    // ---- section 4.4, the row -------------------------------------------------------

    [Fact]
    public void ARowOnlyTheOtherSideAddedIsTaken()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "100"]);
        var b = Book("b", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);

        var row = Assert.Single(OneTable(Judge(o, a, b)).Rows);

        Assert.Equal(RowVerdict.AddFromTheirs, row.Verdict);
        Assert.Equal("2", row.Key);
    }

    [Fact]
    public void ARowOnlyThisSideAddedNeedsNothingDone()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var b = Book("b", Header, ["1", "Sword", "100"]);

        Assert.Empty(Judge(o, a, b).Tables);
    }

    [Fact]
    public void BothSidesAddedTheSameKeyWithDifferentValuesIsAConflict()
    {
        // Two people appending to the same table is the accident this whole tool is for.
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var b = Book("b", Header, ["1", "Sword", "100"], ["2", "Axe", "160"]);

        var row = Assert.Single(OneTable(Judge(o, a, b)).Rows);

        Assert.Equal(RowVerdict.Conflict, row.Verdict);
        Assert.Contains("both sides added a row", row.Conflict);
    }

    [Fact]
    public void BothSidesAddedTheSameRowIdenticallyIsNotAConflict()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var b = Book("b", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);

        Assert.Empty(Judge(o, a, b).Tables);
    }

    [Fact]
    public void TheOtherSideDeletedARowThisSideLeftAloneSoItGoes()
    {
        var o = Book("o", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var a = Book("a", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var b = Book("b", Header, ["1", "Sword", "100"]);

        var row = Assert.Single(OneTable(Judge(o, a, b)).Rows);

        Assert.Equal(RowVerdict.RemoveFromMine, row.Verdict);
        Assert.Equal("2", row.Key);
    }

    [Fact]
    public void DeletedOnOneSideAndEditedOnTheOtherIsAConflict()
    {
        var o = Book("o", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var a = Book("a", Header, ["1", "Sword", "100"], ["2", "Bow", "150"]);
        var b = Book("b", Header, ["1", "Sword", "100"]);

        var row = Assert.Single(OneTable(Judge(o, a, b)).Rows);

        Assert.Equal(RowVerdict.Conflict, row.Verdict);
        Assert.Contains("deleted this row", row.Conflict);
    }

    [Fact]
    public void EditedOnTheOtherSideAndDeletedHereIsAlsoAConflict()
    {
        var o = Book("o", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var a = Book("a", Header, ["1", "Sword", "100"]);
        var b = Book("b", Header, ["1", "Sword", "100"], ["2", "Bow", "150"]);

        var row = Assert.Single(OneTable(Judge(o, a, b)).Rows);

        Assert.Equal(RowVerdict.Conflict, row.Verdict);
    }

    [Fact]
    public void BothSidesDeletedTheSameRowIsAgreement()
    {
        var o = Book("o", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var a = Book("a", Header, ["1", "Sword", "100"]);
        var b = Book("b", Header, ["1", "Sword", "100"]);

        Assert.Empty(Judge(o, a, b).Tables);
    }

    // ---- section 4.4, the column ----------------------------------------------------

    [Fact]
    public void AColumnOnlyTheOtherSideAddedIsTaken()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "100"]);
        var b = Book("b", ["id", "name", "price", "grade"], ["1", "Sword", "100", "A"]);

        var table = OneTable(Judge(o, a, b));
        var column = Assert.Single(table.Columns);

        Assert.Equal(ColumnVerdict.AddFromTheirs, column.Verdict);
        Assert.Equal("grade", column.Name);

        // The column arriving brings its values, and this side has none of them yet.
        var cell = Assert.Single(Assert.Single(table.Rows).Cells);
        Assert.Equal(CellVerdict.TakeTheirs, cell.Verdict);
        Assert.Equal("A", cell.Theirs);
    }

    [Fact]
    public void BothSidesAddedTheSameColumnIsNotAConflict()
    {
        string[] wider = ["id", "name", "price", "grade"];

        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", wider, ["1", "Sword", "100", "A"]);
        var b = Book("b", wider, ["1", "Sword", "100", "A"]);

        Assert.Empty(Judge(o, a, b).Tables);
    }

    [Fact]
    public void TheOtherSideRemovedAColumnThisSideLeftAloneSoItGoes()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "100"]);
        var b = Book("b", ["id", "name"], ["1", "Sword"]);

        var column = Assert.Single(OneTable(Judge(o, a, b)).Columns);

        Assert.Equal(ColumnVerdict.RemoveFromMine, column.Verdict);
        Assert.Equal("price", column.Name);
    }

    [Fact]
    public void AColumnRemovedOnOneSideAndEditedOnTheOtherIsAConflict()
    {
        // Renaming a column reads as a removal and an addition, so this is also what
        // "renamed here, edited there" comes out as.
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "120"]);
        var b = Book("b", ["id", "name"], ["1", "Sword"]);

        var table = OneTable(Judge(o, a, b));
        var column = Assert.Single(table.Columns);

        Assert.Equal(ColumnVerdict.Conflict, column.Verdict);
        Assert.Contains("removed this column", column.Conflict);
    }

    // ---- section 4.4, the table -----------------------------------------------------

    [Fact]
    public void ATableOnlyTheOtherSideHasIsTakenWhole()
    {
        var o = WorkbookGrid.Of("o", ("Item", [Header, ["1", "Sword", "100"]]));
        var a = WorkbookGrid.Of("a", ("Item", [Header, ["1", "Sword", "100"]]));

        var b = WorkbookGrid.Of("b",
            ("Item", [Header, ["1", "Sword", "100"]]),
            ("Skill", [["id", "power"], ["1", "5"]]));

        var table = OneTable(Judge(o, a, b));

        Assert.Equal("Skill", table.Name);
        Assert.Equal(TableVerdict.AddFromTheirs, table.Verdict);
        Assert.Equal(1, table.RowCount);
    }

    [Fact]
    public void ATableTheOtherSideDeletedAndThisSideChangedIsAConflict()
    {
        var o = WorkbookGrid.Of("o",
            ("Item", [Header, ["1", "Sword", "100"]]),
            ("Skill", [["id", "power"], ["1", "5"]]));

        var a = WorkbookGrid.Of("a",
            ("Item", [Header, ["1", "Sword", "100"]]),
            ("Skill", [["id", "power"], ["1", "9"]]));

        var b = WorkbookGrid.Of("b", ("Item", [Header, ["1", "Sword", "100"]]));

        var table = OneTable(Judge(o, a, b));

        Assert.Equal(TableVerdict.Conflict, table.Verdict);
        Assert.Contains("deleted this table", table.Conflict);
    }

    // ---- section 4.5, outside the tables --------------------------------------------

    /// <summary>A schema that leaves part of the sheet outside every table.</summary>
    private sealed class TopThreeRows : ITableSchema
    {
        public IReadOnlyList<TableRegion> TablesIn(WorkbookGrid workbook)
            => workbook.Sheets.Where(s => !s.IsEmpty).Select(s => new TableRegion(
                Name: s.Name, Sheet: s.Name,
                HeaderRow: 0, FirstDataRow: 1, LastDataRow: 2,
                FirstColumn: 0, LastColumn: 2,
                KeyColumn: 0)).ToList();
    }

    private static WorkbookGrid WithNote(string name, string note)
        => WorkbookGrid.Of(name, ("Item", [
            Header,
            ["1", "Sword", "100"],
            ["2", "Bow", "140"],
            [note, "", ""]]));

    [Fact]
    public void AChangeTheOtherSideMadeOutsideEveryTableIsAConflict()
    {
        // A merge writes inside table rectangles and nowhere else, so this cannot be
        // carried - and reporting success while dropping it is the failure the whole
        // fingerprint exists to prevent.
        var outside = WorkbookMerge.OutsideTables(
            WithNote("o", "draft"), WithNote("a", "draft"), WithNote("b", "reviewed"),
            new TopThreeRows());

        var change = Assert.Single(outside);

        Assert.Equal("Item", change.Sheet);
        Assert.Contains("the other side changed cells outside", change.Reason);
    }

    [Fact]
    public void AChangeThisSideMadeOutsideEveryTableNeedsNothing()
    {
        var outside = WorkbookMerge.OutsideTables(
            WithNote("o", "draft"), WithNote("a", "reviewed"), WithNote("b", "draft"),
            new TopThreeRows());

        Assert.Empty(outside);
    }

    [Fact]
    public void BothSidesChangingOutsideDifferentlyIsAConflict()
    {
        var outside = WorkbookMerge.OutsideTables(
            WithNote("o", "draft"), WithNote("a", "reviewed"), WithNote("b", "final"),
            new TopThreeRows());

        Assert.Contains("both sides changed cells outside", Assert.Single(outside).Reason);
    }

    [Fact]
    public void NothingOutsideTheTablesMovedIsNotReported()
    {
        var outside = WorkbookMerge.OutsideTables(
            WithNote("o", "draft"), WithNote("a", "draft"), WithNote("b", "draft"),
            new TopThreeRows());

        Assert.Empty(outside);
    }

    // ---- the whole judgement --------------------------------------------------------

    [Fact]
    public void AMergeWithNothingToSettleCountsItsActionsAndNoConflicts()
    {
        var o = Book("o", Header, ["1", "Sword", "100"], ["2", "Bow", "140"]);
        var a = Book("a", Header, ["1", "Long Sword", "100"], ["2", "Bow", "140"]);
        var b = Book("b", Header, ["1", "Sword", "100"], ["2", "Bow", "150"], ["3", "Axe", "90"]);

        var plan = Judge(o, a, b);

        Assert.False(plan.HasConflicts);

        // One cell to take and one row to add. This side's own rename is not an action:
        // it is already in this side's file.
        Assert.Equal(2, plan.ActionCount);
    }

    [Fact]
    public void TheReportShowsAllThreeValuesAtAConflict()
    {
        var o = Book("o", Header, ["1", "Sword", "100"]);
        var a = Book("a", Header, ["1", "Sword", "120"]);
        var b = Book("b", Header, ["1", "Sword", "140"]);

        string report = MergeReport.Text(Judge(o, a, b));

        Assert.Contains("base    100", report);
        Assert.Contains("mine    120", report);
        Assert.Contains("theirs  140", report);
        Assert.Contains("Item!C2", report);

        // Said on every run, because a tool that writes sometimes and not others is one
        // nobody can predict.
        Assert.Contains("Nothing was written", report);
    }
}
