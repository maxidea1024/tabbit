using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The workbook level of a source entry's filter, converting for real.
/// </summary>
/// <remarks>
/// <see cref="SheetFilterTests"/> covers what a pattern matches. What it cannot cover is the
/// wiring: that the importer names a workbook the way a recipe does, and that the name reaches
/// the sheet decision. Those are the two places this could be right in the filter and wrong in
/// the run - a pattern matched against an absolute path matches nothing, and a sheet decision
/// made without the workbook silently turns every qualified pattern into a global one.
///
/// So these convert `core.xlsx` and look at which tables came out.
/// </remarks>
public class WorkbookFilterTests
{
    /// <summary>The `Serial` sheet of the core fixture holds exactly this table.</summary>
    private const string SheetsOnlyTable = "Localization.json";

    private static string[] Tables(string scenario)
        => Directory.GetFiles(Path.Combine(RepoLayout.OutputDir(scenario), "json"), "*.json");

    [Fact]
    public void A_sheet_excluded_by_workbook_and_name_is_the_only_one_dropped()
    {
        var result = TabbitRunner.Convert("workbook-scoped-sheet");

        Assert.True(result.Succeeded,
            $"A qualified `ExcludeSheets` entry failed the run.{Environment.NewLine}{result.Describe()}");

        var written = Array.ConvertAll(Tables("workbook-scoped-sheet"), Path.GetFileName);

        Assert.DoesNotContain(SheetsOnlyTable, written);

        // The rest of the same workbook is untouched, so what was excluded is a sheet rather
        // than the file it was in.
        Assert.Contains("Item.json", written);
    }

    /// <summary>
    /// The same pattern with another workbook's name drops nothing.
    /// </summary>
    /// <remarks>
    /// The half that fails if the workbook part is ignored, which is what makes the pair worth
    /// having: a qualifier that is silently discarded would pass the test above.
    /// </remarks>
    [Fact]
    public void A_sheet_excluded_in_another_workbook_is_kept_here()
    {
        var result = TabbitRunner.Convert("workbook-scoped-sheet-elsewhere");

        Assert.True(result.Succeeded,
            $"The run failed.{Environment.NewLine}{result.Describe()}");

        var written = Array.ConvertAll(Tables("workbook-scoped-sheet-elsewhere"), Path.GetFileName);

        Assert.Contains(SheetsOnlyTable, written);
    }

    /// <summary>
    /// An excluded workbook is declined by the name a recipe writes, and the run says which.
    /// </summary>
    [Fact]
    public void An_excluded_workbook_is_named_in_the_run()
    {
        var result = TabbitRunner.Convert("workbook-excluded");

        // The name as the recipe writes it - relative to the directory searched - because that
        // is what the pattern was matched against. An absolute path here would mean the
        // exclusion happened to work on a fixture with no subdirectories and nowhere else.
        Assert.Contains("Skipping workbook `core.xlsx`", result.StdOut);

        string json = Path.Combine(RepoLayout.OutputDir("workbook-excluded"), "json");

        // The exporter still writes its manifest, which is the ledger of what it put there and
        // is not a table. Everything else would be a table from a workbook nobody asked for.
        var written = Directory.Exists(json)
            ? Array.ConvertAll(Directory.GetFiles(json, "*.json"), Path.GetFileName)
            : [];

        Assert.Equal(["manifest-json.json"], written);
    }
}
