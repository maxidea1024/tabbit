using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace Mabbit.Tests;

/// <summary>
/// Writing cells into a workbook.
///
/// The promise this has to keep is narrow and checkable: the cells asked for change, and
/// everything else in the file is the bytes it already was. That second half is the whole
/// reason the writer patches the package instead of opening the workbook and saving it - and
/// it is what these tests spend most of their assertions on, because a formatting rule or a
/// chart going missing is not something anybody notices until much later.
/// </summary>
public class MabbitWriteTests : IDisposable
{
    private const string Fixture = "fixtures/workbook.xlsx";
    private const string Binary = "fixtures/workbook.xlsb";

    private readonly List<string> _temporary = [];

    private string Temp()
    {
        string path = Path.Combine(Path.GetTempPath(), "mabbit-" + Path.GetRandomFileName() + ".xlsx");
        _temporary.Add(path);

        return path;
    }

    public void Dispose()
    {
        foreach (string path in _temporary.Where(File.Exists))
            File.Delete(path);

        GC.SuppressFinalize(this);
    }

    private static GridSheet FirstSheet(WorkbookGrid grid)
        => grid.Sheets.First(s => !s.IsEmpty);

    [Fact]
    public void AWrittenCellHoldsTheNewValueAndItsNeighboursDoNot()
    {
        var before = WorkbookGrid.Read(Fixture);
        var sheet = FirstSheet(before);

        int row = sheet.FirstRow + 1;
        int column = sheet.FirstColumn;

        string output = Temp();
        XlsxPatcher.Apply(Fixture, output, [new CellEdit(sheet.Name, row, column, "MABBIT")]);

        var after = WorkbookGrid.Read(output);
        var written = FirstSheet(after);

        Assert.Equal("MABBIT", written.Cell(row, column));

        // Every other cell of the sheet reads exactly as it did.
        foreach (var (r, c, value) in sheet.NonEmptyCells())
        {
            if (r == row && c == column)
                continue;

            Assert.Equal(value, written.Cell(r, c));
        }
    }

    [Fact]
    public void EveryPartTheEditDidNotTouchIsTheSameBytes()
    {
        var before = WorkbookGrid.Read(Fixture);
        var sheet = FirstSheet(before);

        string output = Temp();
        XlsxPatcher.Apply(Fixture, output,
            [new CellEdit(sheet.Name, sheet.FirstRow + 1, sheet.FirstColumn, "MABBIT")]);

        var original = Parts(Fixture);
        var written = Parts(output);

        // The sheet is rewritten, the workbook part gains a recalculation flag, and the
        // cached calculation order is dropped. Nothing else may differ - and this fixture
        // carries comments, drawings, a vml part, a theme and styles, which is exactly the
        // kind of thing a library that re-saved the file would quietly lose.
        var mayDiffer = new[] { "xl/worksheets/sheet1.xml", "xl/workbook.xml", "xl/calcChain.xml" };

        foreach (var (name, bytes) in original)
        {
            if (mayDiffer.Contains(name, StringComparer.Ordinal))
                continue;

            Assert.True(written.ContainsKey(name), $"`{name}` is missing from the written file.");
            Assert.Equal(bytes, written[name]);
        }

        Assert.Equal(
            original.Keys.Where(n => !string.Equals(n, "xl/calcChain.xml", StringComparison.Ordinal)).Order(),
            written.Keys.Order());
    }

    [Fact]
    public void ACellTheSheetDidNotHaveIsInserted()
    {
        var before = WorkbookGrid.Read(Fixture);
        var sheet = FirstSheet(before);

        // Beyond the last column any row holds, so there is no cell element to replace and
        // one has to be put in - which is what taking a value into a blank cell needs.
        int row = sheet.FirstRow + 1;
        int column = sheet.LastColumn + 1;

        Assert.Equal("", sheet.Cell(row, column));

        string output = Temp();
        XlsxPatcher.Apply(Fixture, output, [new CellEdit(sheet.Name, row, column, "INSERTED")]);

        Assert.Equal("INSERTED", FirstSheet(WorkbookGrid.Read(output)).Cell(row, column));
    }

    [Fact]
    public void ANumberIsWrittenAsANumberAndReadsBackAsOne()
    {
        var sheet = FirstSheet(WorkbookGrid.Read(Fixture));

        string output = Temp();
        XlsxPatcher.Apply(Fixture, output,
            [new CellEdit(sheet.Name, sheet.FirstRow + 1, sheet.LastColumn + 1, "1234")]);

        // Written as text it would read back as text, and the next comparison would see a
        // difference this tool created itself.
        Assert.Equal("1234", FirstSheet(WorkbookGrid.Read(output)).Cell(sheet.FirstRow + 1, sheet.LastColumn + 1));
    }

    [Fact]
    public void TheCachedCalculationOrderIsDropped()
    {
        var sheet = FirstSheet(WorkbookGrid.Read(Fixture));

        string output = Temp();
        XlsxPatcher.Apply(Fixture, output,
            [new CellEdit(sheet.Name, sheet.FirstRow + 1, sheet.FirstColumn, "MABBIT")]);

        Assert.DoesNotContain("xl/calcChain.xml", Parts(output).Keys);

        // And the workbook asks to be recalculated, so a formula that depended on the cell
        // does not go on showing what it worked out before.
        Assert.Contains("fullCalcOnLoad", System.Text.Encoding.UTF8.GetString(Parts(output)["xl/workbook.xml"]));
    }

    [Fact]
    public void ABinaryWorkbookIsRefusedRatherThanWrittenWrongly()
    {
        var error = Assert.Throws<MabbitException>(
            () => XlsxPatcher.Apply(Binary, Temp(), [new CellEdit("any", 1, 1, "x")]));

        Assert.Contains(".xlsb", error.Message);
    }

    [Fact]
    public void ASheetTheWorkbookDoesNotHaveIsRefused()
    {
        var error = Assert.Throws<MabbitException>(
            () => XlsxPatcher.Apply(Fixture, Temp(), [new CellEdit("NoSuchSheet", 1, 1, "x")]));

        Assert.Contains("NoSuchSheet", error.Message);
    }

    [Fact]
    public void AFailedWriteLeavesNoFileBehind()
    {
        string output = Temp();

        Assert.Throws<MabbitException>(
            () => XlsxPatcher.Apply(Fixture, output, [new CellEdit("NoSuchSheet", 1, 1, "x")]));

        Assert.False(File.Exists(output));
    }

    // ---- the whole path, from three files to a written one --------------------------

    [Fact]
    public void TwoSidesEditingDifferentCellsMergeIntoOneWorkbook()
    {
        var schema = new HeuristicSchema();

        var baseGrid = WorkbookGrid.Read(Fixture, reportAs: "base");
        var view = TableViews.Of(baseGrid, schema).First(t => t.Rows.Count > 1 && t.Columns.Count > 1);

        var sheet = view.Region.Sheet;
        int valueColumn = view.Region.FirstColumn + 1;

        string mine = Temp();
        string theirs = Temp();
        string result = Temp();

        XlsxPatcher.Apply(Fixture, mine,
            [new CellEdit(sheet, view.Rows[0].RowIndex, valueColumn, "MINE")]);

        XlsxPatcher.Apply(Fixture, theirs,
            [new CellEdit(sheet, view.Rows[1].RowIndex, valueColumn, "THEIRS")]);

        var mineGrid = WorkbookGrid.Read(mine, reportAs: "mine");
        var theirsGrid = WorkbookGrid.Read(theirs, reportAs: "theirs");

        var mineTables = TableViews.Of(mineGrid, schema);

        var plan = WorkbookMerge.Judge(
            "base", TableViews.Of(baseGrid, schema),
            "mine", mineTables,
            "theirs", TableViews.Of(theirsGrid, schema));

        Assert.False(plan.HasConflicts);

        var write = MergeWriter.Prepare(plan, mineTables);

        Assert.True(write.CanWrite, string.Join("; ", write.Refusals.Select(r => r.Reason)));

        XlsxPatcher.Apply(mine, result, write.Edits);

        var merged = FirstSheet(WorkbookGrid.Read(result));

        // Both edits are in the one file, and neither side's is the one that survived.
        Assert.Equal("MINE", merged.Cell(view.Rows[0].RowIndex, valueColumn));
        Assert.Equal("THEIRS", merged.Cell(view.Rows[1].RowIndex, valueColumn));
    }

    [Fact]
    public void AConflictStopsTheWriteBeforeAnythingIsDecided()
    {
        string[] header = ["id", "name"];

        var o = WorkbookGrid.Of("o", ("Item", [header, ["1", "Sword"]]));
        var a = WorkbookGrid.Of("a", ("Item", [header, ["1", "Long Sword"]]));
        var b = WorkbookGrid.Of("b", ("Item", [header, ["1", "Great Sword"]]));

        var schema = new HeuristicSchema();
        var mine = TableViews.Of(a, schema);

        var plan = WorkbookMerge.Judge(
            "o", TableViews.Of(o, schema), "a", mine, "b", TableViews.Of(b, schema));

        var write = MergeWriter.Prepare(plan, mine);

        Assert.False(write.CanWrite);
        Assert.Contains(write.Refusals, r => r.Reason.Contains("conflict"));
    }

    [Fact]
    public void ARowArrivingIsRefusedBecauseInsertingOneIsNotWritingCells()
    {
        string[] header = ["id", "name"];

        var o = WorkbookGrid.Of("o", ("Item", [header, ["1", "Sword"]]));
        var a = WorkbookGrid.Of("a", ("Item", [header, ["1", "Sword"]]));
        var b = WorkbookGrid.Of("b", ("Item", [header, ["1", "Sword"], ["2", "Bow"]]));

        var schema = new HeuristicSchema();
        var mine = TableViews.Of(a, schema);

        var plan = WorkbookMerge.Judge(
            "o", TableViews.Of(o, schema), "a", mine, "b", TableViews.Of(b, schema));

        var write = MergeWriter.Prepare(plan, mine);

        Assert.False(write.CanWrite);
        Assert.Contains(write.Refusals, r => r.Reason.Contains("needs a row to be inserted"));
    }

    private static Dictionary<string, byte[]> Parts(string path)
    {
        using var archive = ZipFile.OpenRead(path);

        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();

            stream.CopyTo(memory);
            parts[entry.FullName] = memory.ToArray();
        }

        return parts;
    }
}
