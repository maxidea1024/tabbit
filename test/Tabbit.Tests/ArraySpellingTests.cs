using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The three places an array may be written reach one file.
/// </summary>
/// <remarks>
/// **The comparison is the gate.** Section 5.1 of the spec says a delimited cell, numbered
/// columns and rows below the record are three ways of writing one array, and this is that
/// claim for two of them - the two that put the elements outside the cell.
///
/// The gate that compared this notation against the one it replaced retired with that
/// notation: once every fixture is written this way, "the output did not change" is the whole
/// golden suite rather than one pair. spec/layout/primary-layout.md section 15.
/// </remarks>
public class ArraySpellingTests
{
    private static void Convert(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Converting `{scenario}` failed.{System.Environment.NewLine}{result.Describe()}");
    }

    private const string Rows = "multirow-rows";
    private const string Columns = "multirow-columns";

    /// <summary>
    /// An array whose elements come from rows reaches the same file as one whose elements come
    /// from columns.
    /// </summary>
    /// <remarks>
    /// **Gate 2 of the spec.** Section 5.1 says the three places an array may be written reach
    /// one wire, and this is that claim for two of them. The elements are the same values in the
    /// same order either way, so an element read into the wrong slot, an array that did not end
    /// where the rows did, or a record boundary found in the wrong place all show up here.
    ///
    /// The control side needs `TrimTrailingArrayElements` and writes `-` in the elements a
    /// record does not reach. The multi-row side needs no setting - its elements are the rows
    /// that exist - and that difference is part of what the comparison is checking.
    /// </remarks>
    [Fact]
    public void Elements_from_rows_reach_the_same_file_as_elements_from_columns()
    {
        Convert(Rows);
        Convert(Columns);

        string fromRows = Path.Combine(RepoLayout.OutputDir(Rows), "binary");
        string fromColumns = Path.Combine(RepoLayout.OutputDir(Columns), "binary");

        var names = Directory.GetFiles(fromColumns, "*.tcb")
            .Select(Path.GetFileName).OrderBy(name => name).ToList();

        Assert.Equal(
            names,
            Directory.GetFiles(fromRows, "*.tcb")
                .Select(Path.GetFileName).OrderBy(name => name).ToList());

        Assert.NotEmpty(names);

        foreach (string name in names)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(fromColumns, name!)),
                File.ReadAllBytes(Path.Combine(fromRows, name!)));
        }
    }

    /// <summary>And the same JSON, which is where a difference is readable.</summary>
    [Fact]
    public void Elements_from_rows_reach_the_same_json_as_elements_from_columns()
    {
        Convert(Rows);
        Convert(Columns);

        string fromRows = Path.Combine(RepoLayout.OutputDir(Rows), "json-named");
        string fromColumns = Path.Combine(RepoLayout.OutputDir(Columns), "json-named");

        var names = Directory.GetFiles(fromColumns, "*.json")
            .Select(Path.GetFileName)
            .Where(name => !name!.StartsWith("manifest-", System.StringComparison.Ordinal))
            .OrderBy(name => name)
            .ToList();

        Assert.NotEmpty(names);

        foreach (string name in names)
        {
            Assert.Equal(
                File.ReadAllText(Path.Combine(fromColumns, name!)),
                File.ReadAllText(Path.Combine(fromRows, name!)));
        }
    }
}
