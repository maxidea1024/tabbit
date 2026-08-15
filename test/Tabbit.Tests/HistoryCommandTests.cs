using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The whole thing through the command line: a conversion records a snapshot, and
/// `--history` and `--stats` read it back.
///
/// The store and the query layer are already checked directly against MySQL. What this
/// adds is the wiring - that the target runs at all, that the reading modes find the
/// connection in the recipe, and that both formats come out. Those are exactly the
/// joins that unit tests around either side cannot see.
/// </summary>
[Collection("databases")]
public class HistoryCommandTests
{
    private const string Scenario = "history";

    /// <summary>
    /// A commit identifier of its own per run.
    ///
    /// The database outlives the suite, so a fixed one would be refused the second time
    /// the tests ran - correctly, since the history already holds it.
    /// </summary>
    private readonly string _commit = "test-" + Guid.NewGuid().ToString("N");

    private IReadOnlyDictionary<string, string> Environment
    {
        get
        {
            HistoryTestBed.EnsureDatabase();
            return DatabaseFixture.ConverterEnvironment;
        }
    }

    private void Convert()
    {
        var result = TabbitRunner.Convert(Scenario, Environment,
            "--commit", _commit,
            "--branch", "endtoend",
            "--commit-author", "테스터 <tester@example.com>",
            "--commit-date", "2026-08-03T11:00:00+09:00",

            // Away from this repository, whose working copy is dirty whenever somebody
            // is working on it - a conversion from one of those is refused, correctly.
            // A CI job passes the same four options for the same reason: it knows what
            // it built, and the checkout it built from cannot say.
            "--repository", Path.GetTempPath());

        Assert.True(result.Succeeded, $"Conversion failed.{System.Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// The JSON out of a report, past whatever `dotnet run` printed first.
    ///
    /// The runner drives the CLI through `dotnet run`, which puts its own notices on
    /// standard output ahead of the program's - a NuGet warning did exactly that and
    /// broke these tests. A real caller invokes the executable and sees none of it.
    /// </summary>
    private static string Json(RunResult result)
    {
        int start = result.StdOut.IndexOf('{');

        Assert.True(start >= 0, $"The report printed no JSON.{System.Environment.NewLine}{result.StdOut}");

        return result.StdOut.Substring(start);
    }

    private RunResult Report(params string[] args)
    {
        var full = new List<string> { "--recipe", "test/fixtures/recipes/history.json" };
        full.AddRange(args);

        var result = TabbitRunner.Invoke(Environment, full.ToArray());

        Assert.True(result.Succeeded, $"The report failed.{System.Environment.NewLine}{result.Describe()}");

        return result;
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public void A_conversion_records_a_snapshot_that_the_history_reports_back()
    {
        Convert();

        var report = Report("--history", "--branch", "endtoend", "--from", _commit, "--to", _commit);

        // An empty range - from and to are the same snapshot, and from is exclusive.
        using var empty = JsonDocument.Parse(Json(report));
        Assert.Empty(empty.RootElement.GetProperty("snapshots").EnumerateArray());

        // The snapshot itself is in the range that ends at it.
        var whole = Report("--history", "--branch", "endtoend", "--to", _commit);

        using var document = JsonDocument.Parse(Json(whole));

        var snapshot = document.RootElement.GetProperty("snapshots").EnumerateArray()
                               .Single(s => s.GetProperty("commit").GetString() == _commit);

        Assert.Equal("테스터", snapshot.GetProperty("authorName").GetString());
        Assert.Equal("tester@example.com", snapshot.GetProperty("authorEmail").GetString());
        Assert.True(snapshot.GetProperty("attributable").GetBoolean());
    }

    /// <summary>
    /// The statistics the history holds for a commit are the ones the conversion wrote
    /// beside it, not a re-derivation that could differ.
    /// </summary>
    [Fact]
    public void Statistics_read_back_match_the_summary_the_conversion_wrote()
    {
        Convert();

        string path = Path.Combine(RepoLayout.OutputDir(Scenario), "summary", "summary.json");

        using var written = JsonDocument.Parse(File.ReadAllText(path));

        var report = Report("--stats", "--branch", "endtoend", "--at", _commit);

        using var stored = JsonDocument.Parse(Json(report));

        Assert.Equal(
            written.RootElement.GetProperty("data").GetProperty("hash").GetString(),
            stored.RootElement.GetProperty("data").GetProperty("hash").GetString());

        Assert.Equal(
            written.RootElement.GetProperty("data").GetProperty("totals").GetProperty("rows").GetInt32(),
            stored.RootElement.GetProperty("data").GetProperty("totals").GetProperty("rows").GetInt32());
    }

    [Fact]
    public void A_report_can_be_read_as_text()
    {
        Convert();

        var report = Report("--history", "--branch", "endtoend", "--to", _commit, "--format", "text");

        Assert.Contains("테스터", report.StdOut);
        Assert.Contains(_commit.Substring(0, 12), report.StdOut);
    }

    [Fact]
    public void Statistics_can_be_read_as_text()
    {
        Convert();

        var report = Report("--stats", "--branch", "endtoend", "--at", _commit, "--format", "text");

        Assert.Contains("tables", report.StdOut);
        Assert.Contains("ServerTuning", report.StdOut);
    }

    [Fact]
    public void A_report_can_be_written_to_a_file()
    {
        Convert();

        string path = Path.Combine(
            RepoLayout.OutputDir(Scenario), "report", "history.json");

        Report("--history", "--branch", "endtoend", "--to", _commit, "--out", path);

        Assert.True(File.Exists(path));

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void A_format_that_is_not_a_format_is_refused()
    {
        var result = TabbitRunner.Invoke(Environment,
            "--recipe", "test/fixtures/recipes/history.json", "--stats", "--format", "yaml");

        Assert.False(result.Succeeded, "An unknown format was accepted.");
        Assert.Contains("is not a format", result.StdOut);
    }
}
