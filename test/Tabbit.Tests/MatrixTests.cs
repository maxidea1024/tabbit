using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That a `:matrix` declaration reads a grid into the two tables it is defined as.
/// </summary>
/// <remarks>
/// The golden records every byte of the output; what it cannot say is which fact each byte
/// came from. These name the facts: the memo column left out, the excluded row gone, a cell
/// with no value distinguishable from a cell holding zero, a position that follows the order
/// of the `:col` row, and an axis of enum labels - the last being the thing the reading rule
/// cannot express at all, and therefore the reason the declaration exists.
///
/// spec/layout/matrix-declaration.md.
/// </remarks>
public class MatrixTests
{
    private const string Scenario = "matrix";

    private static string Json(string table)
        => File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(Scenario), "json", table + ".json"));

    // ------------------------------------------------------------------ the grid

    /// <summary>
    /// One declaration is two tables, and the grid is an array column on the first.
    /// </summary>
    [Fact]
    public void A_grid_becomes_a_values_table_and_a_column_table()
    {
        TabbitRunner.Convert(Scenario);

        string values = Json("TownPrice");

        // The row axis is the index, and the grid is one array beside it - the shape the wire
        // already carried, which is what makes the format unmoved by this feature.
        Assert.Contains("\"town\": 2001", values);
        Assert.Contains("\"price\"", values);

        // The column axis is a table of its own: the key, and where that key sits in every
        // one of those arrays.
        string columns = Json("TownPriceColumn");

        Assert.Contains("\"goods\": 101", columns);
        Assert.Contains("\"at\": 0", columns);
        Assert.Contains("\"goods\": 103", columns);
        Assert.Contains("\"at\": 2", columns);
    }

    /// <summary>
    /// A memo column is not a key, and the position of the keys after it does not move.
    /// </summary>
    /// <remarks>
    /// The fixture puts `#` between the second and third key. Reading it as a key would give
    /// `103` position 3 and every row a fourth cell - so this pins both halves at once.
    /// </remarks>
    [Fact]
    public void A_memo_column_is_no_part_of_the_grid()
    {
        TabbitRunner.Convert(Scenario);

        string columns = Json("TownPriceColumn");

        Assert.DoesNotContain("check", columns);

        // Three keys, positions 0 through 2, with the memo column between two of them.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(columns, "\"at\":").Count);
        Assert.Contains("\"goods\": 103", columns);
        Assert.Contains("\"at\": 2", columns);

        // And the rows are three cells wide rather than four.
        Assert.DoesNotContain("check", Json("TownPrice"));
    }

    /// <summary>A row the marker column excluded is not in the grid.</summary>
    [Fact]
    public void An_excluded_row_leaves_the_grid()
    {
        TabbitRunner.Convert(Scenario);

        Assert.DoesNotContain("2003", Json("TownPrice"));
    }

    /// <summary>
    /// A cell with no value stays distinguishable from a cell holding the type's empty one.
    /// </summary>
    /// <remarks>
    /// The reason a grid does not trim: `0` and "nobody filled it in" are different answers
    /// here, and a trimmed array would answer the second by being shorter - which also moves
    /// every position after it.
    /// </remarks>
    [Fact]
    public void A_cell_with_no_value_is_not_a_cell_holding_zero()
    {
        TabbitRunner.Convert(Scenario);

        string values = Json("TownPrice");

        // Row 2002 wrote `-` in the middle cell and `10` in the first.
        Assert.Contains("null", values);

        // Row 2001 wrote a real zero in the first, which is still a zero.
        Assert.Matches(@"""town"":\s*2001[\s\S]*?""price"":\s*\[\s*0,", values);
    }

    // ------------------------------------------------------------------ the axes

    /// <summary>
    /// An axis of enum labels, which is what the declaration buys over the reading rule.
    /// </summary>
    /// <remarks>
    /// `Fire` is not an integer, so a grid headed by labels is not a grid to the rule that
    /// reads column names - it is a table with a field called `Fire`. Declared, the heading
    /// is a key of the axis type and the label reads as one.
    /// </remarks>
    [Fact]
    public void An_axis_may_be_an_enum()
    {
        TabbitRunner.Convert(Scenario);

        // Both axes are `Element`, and both sides resolved the label to its value.
        Assert.Contains("\"attacker\": 1", Json("ElementChart"));
        Assert.Contains("\"defender\": 1", Json("ElementChartColumn"));

        // The order of the `:col` row is the order of the positions.
        string columns = Json("ElementChartColumn");

        Assert.Matches(@"""defender"":\s*3,\s*""at"":\s*2", columns);
    }

    // ------------------------------------------------------------------ run

    /// <summary>
    /// The generated Python, run against the exported files.
    /// </summary>
    /// <remarks>
    /// The one thing no golden can answer: whether the accessor gives the right cell. Both
    /// halves of the grid's own question are asked here - a cell holding an authored zero and
    /// a cell nobody filled in read the same value and differ only in `has_at`, which is the
    /// distinction the design exists to keep.
    ///
    /// Python because it needs no build step, so this stays a test rather than a toolchain.
    /// The script is written outside the generated tree - three tests walk that tree and judge
    /// every file in it.
    /// </remarks>
    [Fact]
    public void The_generated_python_reads_the_cell_the_two_keys_name()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded, conversion.Describe());

        string root = Path.Combine(RepoLayout.OutputDir(Scenario), "python");
        string binary = Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

        string script = Path.Combine(
            Path.GetTempPath(), $"tabbit-matrix-{Guid.NewGuid():N}.py");

        File.WriteAllText(script, $@"
import sys
sys.path.insert(0, r'{root}')

from matrix_data.tables import Tables

tables = Tables()
tables.read_all(r'{binary}', '.tcb')

price = tables.town_price
chart = tables.element_chart

print('cells', price.at(2001, 101), price.at(2002, 102))
print('has', price.has_at(2001, 101), price.has_at(2002, 102))
print('row', price.row(2001))
print('colkeys', price.col_keys)
print('chart', chart.at(1, 2), chart.at(2, 1))

for pair in [(2001, 999), (9999, 101)]:
    try:
        price.at(*pair)
        print('missing', pair, 'answered')
    except Exception as error:
        print('missing', pair, type(error).__name__)
");

        try
        {
            var (output, succeeded) = RunPython(script);

            Assert.True(succeeded, output);

            // A zero the sheet wrote and a cell it left with `-` read the same value...
            Assert.Contains("cells 0 0", output);

            // ...and are told apart by nothing else.
            Assert.Contains("has True False", output);

            // The row is in the order of the axis, with the memo column no part of it.
            Assert.Contains("row [0, -25, -125]", output);
            Assert.Contains("colkeys [101, 102, 103]", output);

            // The grid is not symmetric, so the two directions are different cells.
            Assert.Contains("chart 0.5 2.0", output);

            // A key on either axis that the grid does not hold is reported rather than
            // answered with the type's empty value.
            Assert.Contains("missing (2001, 999) RecordNotFoundError", output);
            Assert.Contains("missing (9999, 101) RecordNotFoundError", output);
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static (string Output, bool Succeeded) RunPython(string script)
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "python" : "python3",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(script);
        start.Environment["PYTHONIOENCODING"] = "utf-8";

        // **No `__pycache__` in the generated tree.** Importing the package writes bytecode
        // beside it by default, and three tests walk that tree and judge every file in it -
        // a compiler's working file there is read as something the converter produced.
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";

        using var process = Process.Start(start);

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        process.WaitForExit();

        return (output, process.ExitCode == 0);
    }

    // ------------------------------------------------------------------ the generated surface

    /// <summary>
    /// The generated C# composes the two lookups, and checks the two files agree.
    /// </summary>
    /// <remarks>
    /// Read out of the emitted page rather than run, because what is worth pinning here is
    /// that the surface exists and is keyed by the axis types - a `foreign` axis is keyed by
    /// the key it stores rather than by the row it points at, which is the one place this
    /// went wrong first.
    /// </remarks>
    [Fact]
    public void The_generated_accessor_takes_a_key_from_each_axis()
    {
        TabbitRunner.Convert(Scenario);

        string page = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "tables", "TownPriceTable.cs"));

        // The axis is `foreign Goods`; the lookup is keyed by the key, not by `GoodsRecord`.
        Assert.Contains("public int At(int town, int goods)", page);
        Assert.Contains("public int[] Row(int town)", page);

        // An optional grid also says which cells the sheet filled in.
        Assert.Contains("public bool HasAt(int town, int goods)", page);

        // The load-time check that the two files came from one build.
        Assert.Contains("public void LinkColumnAxis(TownPriceColumnTable columns)", page);
        Assert.Contains("The two files are from different builds.", page);

        // A grid whose cells are required has no `HasAt` - there is nothing for it to say.
        string chart = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "tables", "ElementChartTable.cs"));

        Assert.DoesNotContain("public bool HasAt(", chart);

        // And the accessor hands every grid its axis before the snapshot is published.
        string accessor = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "MatrixAccessor.cs"));

        Assert.Contains("snapshot.TownPrice.LinkColumnAxis(snapshot.TownPriceColumn);", accessor);
        Assert.Contains(
            "snapshot.ElementChart.LinkColumnAxis(snapshot.ElementChartColumn);", accessor);
    }
}
