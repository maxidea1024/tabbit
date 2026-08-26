using Tabbit;
using Tabbit.Cooking;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Which cells a broken formula is reported for, and how.
/// </summary>
/// <remarks>
/// The stage that reads a workbook records the error and says nothing, because whether a broken
/// formula matters depends on whether anything reads the cell - and which columns of a named
/// rectangle carry data is the layout's answer, given later. So the report happens where the
/// cell is read as a value. spec/types/formula-errors.md.
/// </remarks>
public class FormulaErrorTests
{
    private static CookingContext Context(Diagnostics diagnostics)
        => new CookingContext(new Model(), new RecipeModel(), diagnostics);

    private static Location Somewhere()
        => new Location { Filename = "book.xlsx", Sheet = "Sheet1", Column = 2, Row = 8 };

    /// <summary>
    /// The strict policy names the error, and the cell.
    /// </summary>
    /// <remarks>
    /// The message matters here: read as an empty cell instead, the same value is refused as
    /// "this cell is empty", which sends the author looking for a cell nobody emptied.
    /// </remarks>
    [Fact]
    public void The_strict_policy_names_the_formula_error()
    {
        var context = Context(new Diagnostics());

        var thrown = Assert.Throws<TabbitException>(() => context.ReadCell(
            Models.ValueType.Float, null, "", Somewhere(),
            formulaError: "#DIV/0!",
            onFormulaError: FormulaErrorPolicy.Error));

        Assert.Equal(Tabbit.Cooking.CookingMessages.FormulaError, thrown.MessageId);
        Assert.Contains("#DIV/0!", thrown.Message);
    }

    /// <summary>
    /// The lenient policy reads the type's empty value and says so once per column.
    /// </summary>
    /// <remarks>
    /// Per column rather than per cell: a column of a trimmed array can hold thousands, and a
    /// thousand lines saying one thing is a thousand lines nobody reads to the end.
    /// </remarks>
    [Fact]
    public void The_lenient_policy_reads_it_as_empty()
    {
        var context = Context(new Diagnostics());

        var reading = context.ReadCell(
            Models.ValueType.Float, null, "", Somewhere(),
            column: "T.Value",
            formulaError: "#N/A",
            onFormulaError: FormulaErrorPolicy.Empty);

        Assert.True(reading.HasValue);
        Assert.Equal(0f, reading.Value);
    }

    /// <summary>
    /// A cell with no formula error reads as it always did.
    /// </summary>
    /// <remarks>
    /// The parameter is optional and every caller that does not pass one has to be unaffected,
    /// which is most of this tool.
    /// </remarks>
    [Fact]
    public void A_cell_without_one_is_unaffected()
    {
        var context = Context(new Diagnostics());

        var reading = context.ReadCell(Models.ValueType.Float, null, "1.5", Somewhere());

        Assert.True(reading.HasValue);
        Assert.Equal(1.5f, reading.Value);
    }

    /// <summary>
    /// A blank cell that is not a formula error is still refused as a blank.
    /// </summary>
    /// <remarks>
    /// The two concessions are separate settings and stay separate: a sheet may be allowed its
    /// broken formulas without being allowed its unfilled cells.
    /// </remarks>
    [Fact]
    public void A_blank_cell_is_still_a_blank_cell()
    {
        var context = Context(new Diagnostics());

        var thrown = Assert.Throws<TabbitException>(() => context.ReadCell(
            Models.ValueType.Float, null, "", Somewhere(),
            onFormulaError: FormulaErrorPolicy.Empty));

        Assert.Equal(Tabbit.Cooking.CookingMessages.BlankCellRequired, thrown.MessageId);
    }
}
