using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// A column's declared bounds and whitelist, checked against the values it holds.
/// </summary>
/// <remarks>
/// The type says what a value is; these say which of those values are allowed. A sheet with
/// somewhere to write "1 to 99" has been checking it after the fact with a script over the
/// exported JSON - reading it into the model means the check happens where the cell is, and
/// a diagnostic can name it.
///
/// spec/column-constraints.md.
/// </remarks>
public class ColumnConstraintTests
{
    /// <summary>A one-column table holding the given values, with the given constraints.</summary>
    private static Table TableOf(
        ColumnConstraints constraints, bool required, params object[] values)
    {
        var table = ModelFactory.Table("T", new List<(string, ValueType)>
        {
            ("Index", ValueType.Int32),
            ("Level", ValueType.Int32),
        });

        table.Fields[1].Constraints = constraints;
        table.Fields[1].IsRequired = required;

        for (int at = 0; at < values.Length; at++)
        {
            table.Data.Add(new List<Cell>
            {
                new Cell { RawCell = RawCellAt(at, 0), Value = at, HasValue = true },
                new Cell
                {
                    RawCell = RawCellAt(at, 1),
                    Value = values[at],
                    HasValue = values[at] is not null,
                },
            });
        }

        return table;
    }

    /// <summary>A cell of a sheet that does not exist, so a diagnostic has a place to point.</summary>
    private static Tabbit.Models.Raw.RawCell RawCellAt(int row, int column)
        => new Tabbit.Models.Raw.RawCell
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "S", Row = row, Column = column },
            Value = "",
            Note = "",
        };

    private static IReadOnlyList<string> Check(Table table)
    {
        var diagnostics = new Diagnostics();
        new ModelCooker().ValidateColumnConstraints(table, table.RowSets.First(), diagnostics);

        // The collector holds them; the message is what a reader sees, so that is what
        // these assert on.
        var thrown = Record.Exception(() => diagnostics.ThrowIfAny("bad"));
        if (thrown is null)
            return System.Array.Empty<string>();

        return ((TabbitException)thrown).Details.Select(d => d.Message).ToList();
    }

    /// <summary>A value below the declared minimum is reported, and one inside is not.</summary>
    [Fact]
    public void A_value_below_the_minimum_is_reported()
    {
        var problems = Check(TableOf(
            new ColumnConstraints { Minimum = 1, Maximum = 99 }, required: true, 0, 50, 100));

        Assert.Equal(2, problems.Count);
        Assert.Contains("below the minimum 1", problems[0]);
        Assert.Contains("above the maximum 99", problems[1]);
    }

    /// <summary>
    /// A value the whitelist does not name is reported, compared as the sheet wrote it.
    /// </summary>
    [Fact]
    public void A_value_outside_the_whitelist_is_reported()
    {
        var problems = Check(TableOf(
            new ColumnConstraints { AllowedValues = new[] { "1", "2" } }, required: true, 1, 3));

        string problem = Assert.Single(problems);
        Assert.Contains("`3`", problem);
        Assert.Contains("Allowed: 1, 2", problem);
    }

    /// <summary>
    /// A required column with no value is reported - which is the check a layout that reads
    /// a blank as the type's empty value has no other way to make.
    /// </summary>
    /// <remarks>
    /// A layout that refuses a blank outright never reaches here. The one that writes `-`
    /// for "no value" and reads it as zero does, and without this the declaration it wrote
    /// beside the column would mean nothing.
    /// </remarks>
    [Fact]
    public void A_required_column_with_no_value_is_reported()
    {
        var problems = Check(TableOf(new ColumnConstraints(), required: true, 1, null, 3));

        Assert.Contains("has no value", Assert.Single(problems));
    }

    /// <summary>And an optional one with no value is not.</summary>
    [Fact]
    public void An_optional_column_may_be_empty()
    {
        Assert.Empty(Check(TableOf(new ColumnConstraints(), required: false, 1, null, 3)));
    }

    /// <summary>
    /// A cell with no value is held to no bound either.
    /// </summary>
    /// <remarks>
    /// The empty value is the type's rather than the author's, so measuring it against a
    /// minimum would report the absence twice and under the wrong name.
    /// </remarks>
    [Fact]
    public void A_cell_with_no_value_is_not_measured_against_a_bound()
    {
        Assert.Empty(Check(TableOf(
            new ColumnConstraints { Minimum = 1 }, required: false, 5, null)));
    }

    /// <summary>A column that declares nothing is checked for nothing.</summary>
    [Fact]
    public void A_column_with_no_constraints_is_left_alone()
    {
        Assert.Empty(Check(TableOf(new ColumnConstraints(), required: false, -1, 0, 999999)));
    }
}
