using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The table geometry `--dump-schema` writes out.
///
/// This is the only thing this program tells the tools that read the same workbooks without
/// cooking them - a comparison, a merge. Getting a rectangle wrong there does not fail: it
/// makes the other tool read the wrong cells as a table and report every row of it as changed,
/// or worse, write a merged value into a cell that is not the one the value came from.
/// </summary>
public class SheetSchemaTests
{
    private static Models.Model TwoRows() => ModelFactory.Of(
        ModelFactory.Table("Item",
            [("id", ValueType.Int32), ("name", ValueType.String)],
            [1, "Sword"],
            [2, "Bow"]));

    [Fact]
    public void ATableReportsTheRectangleItsCellsOccupy()
    {
        var table = Assert.Single(SheetSchema.Of(TwoRows(), "test").Tables);

        Assert.Equal("Item", table.Name);
        Assert.Equal("Item", table.Sheet);
        Assert.Equal("memory.xlsx", table.Workbook);

        // The fixture puts headers on row 1 and rows from row 2, counted from zero.
        Assert.Equal(1, table.HeaderRow);
        Assert.Equal(2, table.FirstDataRow);
        Assert.Equal(3, table.LastDataRow);
        Assert.Equal(0, table.FirstColumn);
        Assert.Equal(1, table.LastColumn);
    }

    [Fact]
    public void TheKeyColumnIsTheColumnTheFirstFieldSitsIn()
    {
        var table = Assert.Single(SheetSchema.Of(TwoRows(), "test").Tables);

        // Field zero is the primary index by construction, and a key addressing exactly one
        // row is what lets the other tool follow a row from one file to another at all.
        Assert.Equal(0, table.KeyColumn);
    }

    [Fact]
    public void ATableWithNoRowsStillHasSomewhereItsFirstRowWouldGo()
    {
        var model = ModelFactory.Of(
            ModelFactory.Table("Empty", [("id", ValueType.Int32), ("name", ValueType.String)]));

        var table = Assert.Single(SheetSchema.Of(model, "test").Tables);

        // A merge taking the first row into an empty table needs to know where that row goes,
        // so reporting nothing here would make that case unmergeable rather than empty.
        Assert.Equal(1, table.HeaderRow);
        Assert.Equal(2, table.FirstDataRow);
        Assert.Equal(1, table.LastDataRow);
        Assert.Equal(0, table.FirstColumn);
        Assert.Equal(1, table.LastColumn);
    }

    [Fact]
    public void EveryTableOfTheModelIsReported()
    {
        var model = ModelFactory.Of(
            ModelFactory.Table("Item", [("id", ValueType.Int32)], [1]),
            ModelFactory.Table("Skill", [("id", ValueType.Int32)], [1], [2]));

        var schema = SheetSchema.Of(model, "1.2.3");

        Assert.Equal(2, schema.Tables.Count);
        Assert.Equal("1.2.3", schema.Tool);

        // The version travels with it because the geometry is an agreement between two
        // programs, and knowing which one wrote it is how a mismatch gets diagnosed.
        Assert.Contains(schema.Tables, t => t.Name == "Skill" && t.LastDataRow == 3);
    }
}
