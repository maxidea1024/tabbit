using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The primary layout reads a sheet into the model the notation it replaces did.
/// </summary>
/// <remarks>
/// **The comparison is the gate.** `PrimaryLayoutTests` checks the notation rule by rule, and
/// everything under those rules rests on one claim - that a table written the new way and the
/// same table written the old way arrive as the same table. Two workbooks hold the same tables
/// under the same names, one written each way, and the produced files must be identical byte
/// for byte.
///
/// What fails here without an assertion written for it: a column path read wrong, an element
/// numbered from the wrong base, a folded type expression that resolved to something else, a
/// memo column that left a trace, a header row order that changed which row was read.
///
/// Both workbooks come from one `TableSpec` in `FixtureGen`, so the two sheets cannot drift
/// apart into a comparison that passes because both sides are wrong.
///
/// spec/primary-layout.md section 15, gate 1.
/// </remarks>
public class PrimaryLayoutEquivalenceTests
{
    private const string New = "primary-equiv";
    private const string Old = "primary-equiv-old";

    private static void Convert(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Converting `{scenario}` failed.{System.Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// Every binary table file is the same in both trees.
    /// </summary>
    /// <remarks>
    /// The file list is compared first. A table the new notation failed to find would otherwise
    /// pass a per-file loop that only walks the files it did produce.
    /// </remarks>
    [Fact]
    public void The_two_notations_reach_the_same_binary_files()
    {
        Convert(New);
        Convert(Old);

        string fromNew = Path.Combine(RepoLayout.OutputDir(New), "binary");
        string fromOld = Path.Combine(RepoLayout.OutputDir(Old), "binary");

        var newNames = Directory.GetFiles(fromNew, "*.tcb")
            .Select(Path.GetFileName).OrderBy(name => name).ToList();

        var oldNames = Directory.GetFiles(fromOld, "*.tcb")
            .Select(Path.GetFileName).OrderBy(name => name).ToList();

        Assert.Equal(oldNames, newNames);
        Assert.NotEmpty(newNames);

        foreach (string name in newNames)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(fromOld, name!)),
                File.ReadAllBytes(Path.Combine(fromNew, name!)));
        }
    }

    /// <summary>
    /// And the same JSON, which is where a difference is readable.
    /// </summary>
    /// <remarks>
    /// The binary comparison above is the one that matters and the one that would catch a
    /// difference the JSON rounds away. This is here so that when it fails, the diff names the
    /// table and the column rather than an offset.
    /// </remarks>
    [Fact]
    public void The_two_notations_reach_the_same_json()
    {
        Convert(New);
        Convert(Old);

        string fromNew = Path.Combine(RepoLayout.OutputDir(New), "json-named");
        string fromOld = Path.Combine(RepoLayout.OutputDir(Old), "json-named");

        // The manifest is left out: it records when the run happened, so two runs never match
        // and never could. What it says about the tables is the tables, which are compared
        // below and byte for byte by the test above.
        var names = Directory.GetFiles(fromOld, "*.json")
            .Select(Path.GetFileName)
            .Where(name => !name!.StartsWith("manifest-", System.StringComparison.Ordinal))
            .OrderBy(name => name)
            .ToList();

        Assert.NotEmpty(names);

        foreach (string name in names)
        {
            Assert.Equal(
                File.ReadAllText(Path.Combine(fromOld, name!)),
                File.ReadAllText(Path.Combine(fromNew, name!)));
        }
    }
}
