using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.History;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// Reading the history back.
///
/// The half a designer actually uses. What is checked here is mostly about answers that
/// mislead rather than answers that fail: a range whose ends are off by one snapshot, a
/// truncated list that does not admit it, a changeset spanning six people's commits
/// presented as one person's work.
/// </summary>
[Collection("databases")]
public class HistoryQueryTests : IDisposable
{
    private readonly string _project = "q" + Guid.NewGuid().ToString("N").Substring(0, 12);
    private readonly string _connectionString;

    private static readonly (string, ValueType)[] Columns =
    {
        ("id", ValueType.Int32),
        ("name", ValueType.String),
        ("power", ValueType.Int32),
    };

    private readonly List<string> _cleanup = new List<string>();

    public HistoryQueryTests() => _connectionString = HistoryTestBed.EnsureDatabase();

    public void Dispose()
    {
        foreach (var directory in _cleanup)
        {
            try
            {
                // git marks its object files read-only, which a plain recursive delete
                // refuses on Windows.
                foreach (var file in System.IO.Directory.EnumerateFiles(
                             directory, "*", System.IO.SearchOption.AllDirectories))
                {
                    System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                }

                System.IO.Directory.Delete(directory, recursive: true);
            }
            catch (System.IO.IOException)
            {
                // A leftover temp directory is not worth failing a passing test over.
            }
        }
    }

    // ------------------------------------------------------------- fixtures

    private static Model Items(params object[][] rows)
        => ModelFactory.Of(ModelFactory.Table("Item", Columns, rows));

    private static CommitInfo Commit(string hash, string author, int minute)
        => CommitInfo.Resolve(
            new Options
            {
                Repository = System.IO.Path.GetTempPath(),
                Commit = hash,
                Branch = "main",
                CommitAuthor = $"{author} <{author.ToLowerInvariant()}@example.com>",
                CommitDate = $"2026-08-03T10:{minute:00}:00+09:00",
            },
            new Tabbit.Recipe.RecipeModel());

    private void Record(Model model, CommitInfo commit)
    {
        using var store = HistoryStore.Open(_connectionString, _project, "main");

        HistoryRecorder.Record(
            store, SummaryBuilder.Build(model, commit, null), ModelFingerprint.Of(model),
            commit, new HistoryRecipe(), out _);
    }

    private HistoryQuery Query() => HistoryQuery.Open(_connectionString);

    /// <summary>
    /// Three commits by three people, each changing one cell. Small enough to state the
    /// expected answer exactly, which is what makes a range test worth anything.
    /// </summary>
    private void ThreeCommits()
    {
        Record(Items(new object[] { 1, "Sword", 10 }), Commit("aaaaaaaa1111", "Kim", 0));
        Record(Items(new object[] { 1, "Sword", 20 }), Commit("bbbbbbbb2222", "Park", 5));
        Record(Items(new object[] { 1, "Sword", 30 }), Commit("cccccccc3333", "Lee", 10));
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public void A_range_reports_who_changed_what()
    {
        ThreeCommits();

        using var query = Query();

        var document = query.Diff(_project, "main", from: "aaaaaaaa1111", to: "cccccccc3333");

        Assert.Equal(2, document.Snapshots.Count);

        // Oldest first, which is the direction changes are read in.
        Assert.Equal(new[] { "bbbbbbbb2222", "cccccccc3333" },
            document.Snapshots.Select(s => s.Commit));

        var second = document.Snapshots[0];

        Assert.Equal("Park", second.AuthorName);

        var cell = Assert.Single(second.Cells);
        Assert.Equal("Item", cell.Table);
        Assert.Equal("power", cell.Field);
        Assert.Equal("10", cell.Before);
        Assert.Equal("20", cell.After);
    }

    /// <summary>
    /// The ship verdict, end to end: a cell edit is a data patch, an enum label is a
    /// code deploy, and the range needs whatever any snapshot in it needed.
    ///
    /// The classification itself is tested change-by-change in
    /// <see cref="DeploymentAdviceTests"/>; this proves the verdict actually rides the
    /// document - through the store, past the budget, onto both the snapshot and the
    /// range.
    /// </summary>
    [Fact]
    public void A_range_says_what_shipping_it_requires()
    {
        var enums = ModelFactory.Enum("Grade", "None", "Common", "Rare");

        var v1 = Items(new object[] { 1, "Sword", 10 });
        v1.Enums.Add(enums);

        // One cell edited: data only.
        var v2 = Items(new object[] { 1, "Sword", 20 });
        v2.Enums.Add(enums);

        // One label appended, no data touched: code only.
        var v3 = Items(new object[] { 1, "Sword", 20 });
        v3.Enums.Add(ModelFactory.Enum("Grade", "None", "Common", "Rare", "Epic"));

        Record(v1, Commit("aaaaaaaa1111", "Kim", 0));
        Record(v2, Commit("bbbbbbbb2222", "Park", 5));
        Record(v3, Commit("cccccccc3333", "Lee", 10));

        using var query = Query();

        var document = query.Diff(_project, "main", from: "aaaaaaaa1111", to: "cccccccc3333");

        var dataOnly = document.Snapshots[0].Deployment;
        Assert.True(dataOnly.Data);
        Assert.False(dataOnly.Code);

        var codeOnly = document.Snapshots[1].Deployment;
        Assert.False(codeOnly.Data);
        Assert.True(codeOnly.Code);
        Assert.Contains(codeOnly.Reasons, r => r.Contains("Grade"));

        // The range needs both, because each snapshot needed one.
        Assert.True(document.Deployment.Data);
        Assert.True(document.Deployment.Code);
    }

    /// <summary>
    /// `from` is the state compared from, so its own changes belong to the range before
    /// this one. Getting this off by one puts somebody else's edit in your report.
    /// </summary>
    [Fact]
    public void The_start_of_a_range_is_exclusive_and_the_end_is_inclusive()
    {
        ThreeCommits();

        using var query = Query();

        var document = query.Diff(_project, "main", from: "bbbbbbbb2222", to: "cccccccc3333");

        var only = Assert.Single(document.Snapshots);

        Assert.Equal("cccccccc3333", only.Commit);
        Assert.Equal("Lee", only.AuthorName);
    }

    [Fact]
    public void A_range_with_no_ends_covers_the_whole_branch()
    {
        ThreeCommits();

        using var query = Query();

        Assert.Equal(3, query.Diff(_project, "main").Snapshots.Count);
    }

    [Fact]
    public void A_commit_can_be_named_by_a_prefix()
    {
        ThreeCommits();

        using var query = Query();

        Assert.Single(query.Diff(_project, "main", from: "bbbb", to: "cccc").Snapshots);
    }

    [Fact]
    public void An_ambiguous_prefix_is_refused_rather_than_guessed()
    {
        Record(Items(new object[] { 1, "Sword", 10 }), Commit("ffff1111", "Kim", 0));
        Record(Items(new object[] { 1, "Sword", 20 }), Commit("ffff2222", "Park", 5));

        using var query = Query();

        var ex = Assert.Throws<TabbitException>(() => query.Diff(_project, "main", to: "ffff"));

        Assert.Equal(Tabbit.History.RecordMessages.CommitAmbiguous, ex.MessageId);
        Assert.Contains("matches 2 commits", ex.Message);
    }

    [Fact]
    public void A_commit_the_history_does_not_hold_is_reported_as_such()
    {
        ThreeCommits();

        using var query = Query();

        var ex = Assert.Throws<TabbitException>(() => query.Diff(_project, "main", to: "9999"));

        Assert.Equal(Tabbit.History.RecordMessages.SnapshotNotFound, ex.MessageId);
    }

    [Fact]
    public void A_range_the_wrong_way_round_is_refused()
    {
        ThreeCommits();

        using var query = Query();

        var ex = Assert.Throws<TabbitException>(
            () => query.Diff(_project, "main", from: "cccccccc3333", to: "aaaaaaaa1111"));

        Assert.Equal(Tabbit.History.RecordMessages.RangeReversed, ex.MessageId);
    }

    [Fact]
    public void A_report_can_be_narrowed_to_one_person()
    {
        ThreeCommits();

        using var query = Query();

        var document = query.Diff(_project, "main", author: "Park");

        var only = Assert.Single(document.Snapshots);
        Assert.Equal("Park", only.AuthorName);
    }

    [Fact]
    public void A_report_can_be_narrowed_to_one_table()
    {
        Record(ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }),
            ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 })),
            Commit("aaaa1111", "Kim", 0));

        Record(ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 12 }),
            ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 33 })),
            Commit("bbbb2222", "Park", 5));

        using var query = Query();

        var document = query.Diff(_project, "main", from: "aaaa1111", table: "Item");

        var cell = Assert.Single(document.Snapshots[0].Cells);
        Assert.Equal("Item", cell.Table);
    }

    /// <summary>
    /// A cut list that does not say it was cut reads as a complete one, and the
    /// conclusion drawn from it - "nothing else changed" - is wrong.
    /// </summary>
    [Fact]
    public void A_truncated_report_says_how_much_it_left_out()
    {
        var first = Enumerable.Range(1, 20).Select(i => new object[] { i, "n" + i, i }).ToArray();
        var second = Enumerable.Range(1, 20).Select(i => new object[] { i, "n" + i, i * 2 }).ToArray();

        Record(Items(first), Commit("aaaa1111", "Kim", 0));
        Record(Items(second), Commit("bbbb2222", "Park", 5));

        using var query = Query();

        var document = query.Diff(_project, "main", from: "aaaa1111", limit: 5);

        Assert.True(document.Query.Truncated);
        Assert.Equal(5, document.Totals.Cells + document.Totals.Rows + document.Totals.Schema);

        // 20 rows changed, so 20 cell changes and 20 row changes; five were reported.
        Assert.Equal(35, document.Query.Omitted);
    }

    [Fact]
    public void An_untruncated_report_says_so_too()
    {
        ThreeCommits();

        using var query = Query();

        var document = query.Diff(_project, "main");

        Assert.False(document.Query.Truncated);
        Assert.Equal(0, document.Query.Omitted);
    }

    /// <summary>
    /// A snapshot recorded from an identifier git knows nothing about cannot be shown to
    /// follow its parent - and claiming a gap that may not exist would put a warning on
    /// a clean report. The unknown case reads as "follows".
    /// </summary>
    [Fact]
    public void A_snapshot_whose_ancestry_cannot_be_checked_is_not_reported_as_a_gap()
    {
        ThreeCommits();

        using var query = Query();

        Assert.Equal(0, query.Diff(_project, "main").Totals.Gaps);
    }

    // ------------------------------------------------------------ statistics

    [Fact]
    public void Statistics_are_read_back_as_the_conversion_recorded_them()
    {
        Record(Items(
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 }), Commit("aaaa1111", "Kim", 0));

        using var query = Query();

        var summary = query.Stats(_project, "main");

        Assert.Equal(1, summary.Data.Totals.Tables);
        Assert.Equal(2, summary.Data.Totals.Rows);
        Assert.Equal("aaaa1111", summary.Run.Commit.Hash);
    }

    /// <summary>
    /// An old commit's statistics describe that commit, not today's workbook - which is
    /// why they are stored rather than recomputed.
    /// </summary>
    [Fact]
    public void Statistics_of_an_older_commit_describe_that_commit()
    {
        Record(Items(new object[] { 1, "Sword", 10 }), Commit("aaaa1111", "Kim", 0));

        Record(Items(
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 },
            new object[] { 3, "Bow", 15 }), Commit("bbbb2222", "Park", 5));

        using var query = Query();

        Assert.Equal(1, query.Stats(_project, "main", "aaaa1111").Data.Totals.Rows);
        Assert.Equal(3, query.Stats(_project, "main").Data.Totals.Rows);
    }

    [Fact]
    public void A_trend_runs_oldest_first()
    {
        Record(Items(new object[] { 1, "Sword", 10 }), Commit("aaaa1111", "Kim", 0));

        Record(Items(
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 }), Commit("bbbb2222", "Park", 5));

        using var query = Query();

        var trend = query.Trend(_project, "main", "rows");

        Assert.Equal(new long[] { 1, 2 }, trend.Select(p => p.Value));
        Assert.Equal("aaaa1111", trend[0].Commit);
    }

    [Fact]
    public void A_metric_that_is_not_a_metric_is_refused()
    {
        ThreeCommits();

        using var query = Query();

        Assert.Throws<TabbitException>(() => query.Trend(_project, "main", "vibes"));
    }

    [Fact]
    public void Authors_are_summarised_over_a_range()
    {
        ThreeCommits();

        using var query = Query();

        var authors = query.Authors(_project, "main", from: "aaaaaaaa1111");

        Assert.Equal(2, authors.Count);
        Assert.All(authors, a => Assert.Equal(1, a.Snapshots));
        Assert.Contains(authors, a => a.Name == "Park");
        Assert.DoesNotContain(authors, a => a.Name == "Kim");
    }

    /// <summary>
    /// The question a designer actually asks: this number is wrong, when did it become
    /// this, and who made it so.
    /// </summary>
    [Fact]
    public void One_cells_whole_history_can_be_followed()
    {
        ThreeCommits();

        using var query = Query();

        var entries = query.CellHistory(_project, "main", "Item", rowKey: "1", field: "power");

        // Newest first: the question starts from the value that is wrong now.
        Assert.Equal(new[] { "Lee", "Park", "Kim" }, entries.Select(e => e.AuthorName));
        Assert.Equal(new[] { "30", "20", "10" }, entries.Select(e => e.After));
        Assert.Equal(new[] { "20", "10", null }, entries.Select(e => e.Before));
    }

    // ------------------------------------------------------- tags and revisions

    /// <summary>
    /// A repository whose commits are the ones the history holds, with a tag on one.
    ///
    /// Real git rather than a stub: what is being checked is that a name a person would
    /// type resolves the way git resolves it, and only git knows that. An annotated tag
    /// in particular is its own object with its own hash, and looking that hash up in
    /// the history would find nothing.
    /// </summary>
    private string RepositoryWithTags(out string[] commits)
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "tabbit-tags-" + Guid.NewGuid().ToString("N"));

        System.IO.Directory.CreateDirectory(directory);
        _cleanup.Add(directory);

        Git(directory, "init", "--initial-branch=main");
        Git(directory, "config", "user.name", "T");
        Git(directory, "config", "user.email", "t@example.com");
        Git(directory, "config", "commit.gpgsign", "false");

        var made = new List<string>();

        for (int i = 1; i <= 4; i++)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "sheet.txt"), i.ToString());

            Git(directory, "add", "sheet.txt");
            Git(directory, "commit", "-m", "change " + i);

            made.Add(Git(directory, "rev-parse", "HEAD").Trim());
        }

        // Annotated, on the second commit. A lightweight tag would resolve to the
        // commit directly and would not exercise the part that can go wrong.
        Git(directory, "tag", "-a", "v1.0.0", "-m", "release 1.0", made[1]);

        // And one on a commit no conversion will have run on - the ordinary shape of a
        // release tag, since bumping a version touches no sheets.
        Git(directory, "tag", "-a", "v2.0.0", "-m", "release 2.0", made[3]);

        commits = made.ToArray();
        return directory;
    }

    private static string Git(string directory, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(false),
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(psi);

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit(30_000);

        Assert.True(process.ExitCode == 0, $"git {string.Join(" ", args)} failed: {error}");

        return output;
    }

    /// <summary>
    /// The question a release actually gets asked in. Nobody remembers the hash a
    /// version was cut at.
    /// </summary>
    [Fact]
    public void A_range_can_be_asked_for_by_tag()
    {
        string repository = RepositoryWithTags(out var commits);

        // The first three commits are converted; the fourth is not, which is what makes
        // v2.0.0 the interesting case.
        for (int i = 0; i < 3; i++)
            Record(Items(new object[] { 1, "Sword", (i + 1) * 10 }), Commit(commits[i], "Kim", i * 5));

        using var query = Query();
        query.RepositoryPath = repository;

        var document = query.Diff(_project, "main", from: "v1.0.0");

        // v1.0.0 is the second commit, so the range is the third alone.
        var only = Assert.Single(document.Snapshots);

        Assert.Equal(commits[2], only.Commit);
    }

    /// <summary>
    /// A tag on a commit nobody converted - a version bump touches no sheets - falls
    /// back to the snapshot behind it, and says so. Erroring would send somebody hunting
    /// for a hash; substituting quietly would answer a different question.
    /// </summary>
    [Fact]
    public void A_tag_on_an_unconverted_commit_falls_back_and_says_so()
    {
        string repository = RepositoryWithTags(out var commits);

        for (int i = 0; i < 3; i++)
            Record(Items(new object[] { 1, "Sword", (i + 1) * 10 }), Commit(commits[i], "Kim", i * 5));

        using var query = Query();
        query.RepositoryPath = repository;

        var document = query.Diff(_project, "main", to: "v2.0.0");

        // The fourth commit has no snapshot, so the third stands in and the range ends
        // there - all three snapshots.
        Assert.Equal(3, document.Snapshots.Count);

        Assert.Contains(document.Query.Notes,
            note => note.Contains("v2.0.0") && note.Contains("no conversion ever ran on"));
    }

    [Fact]
    public void A_revision_expression_works_too()
    {
        string repository = RepositoryWithTags(out var commits);

        for (int i = 0; i < 3; i++)
            Record(Items(new object[] { 1, "Sword", (i + 1) * 10 }), Commit(commits[i], "Kim", i * 5));

        using var query = Query();
        query.RepositoryPath = repository;

        // The commit before the third one, which is the second.
        var document = query.Diff(_project, "main", from: commits[2] + "^");

        Assert.Single(document.Snapshots);
        Assert.Equal(commits[2], document.Snapshots[0].Commit);
    }

    /// <summary>
    /// A commit hash that is already in the history must not be sent to git - it works
    /// with no checkout at all, which is what the server usually has.
    /// </summary>
    [Fact]
    public void A_stored_hash_needs_no_repository()
    {
        ThreeCommits();

        using var query = Query();

        Assert.Null(query.RepositoryPath);
        Assert.Single(query.Diff(_project, "main", from: "bbbbbbbb2222").Snapshots);
    }

    /// <summary>
    /// Without a working copy a tag cannot be resolved, and the error says that rather
    /// than blaming the history for not holding a snapshot it was never asked about.
    /// </summary>
    [Fact]
    public void A_tag_with_no_repository_is_refused_with_the_reason()
    {
        ThreeCommits();

        using var query = Query();

        var ex = Assert.Throws<TabbitException>(() => query.Diff(_project, "main", from: "v1.0.0"));

        Assert.Equal(Tabbit.History.RecordMessages.SnapshotNotFound, ex.MessageId);
    }

    // ------------------------------------------------------------- pruning

    private PruneResult Prune(string before, int keep)
    {
        using var connection = new MySqlConnector.MySqlConnection(_connectionString);
        connection.Open();

        return HistoryMaintenance.Prune(
            connection, _project, "main", HistoryMaintenance.ParseCutoff(before), keep);
    }

    /// <summary>
    /// What grows without bound is the change log. A pruned snapshot keeps everything
    /// that describes the data and loses only the cell-by-cell record of getting there.
    /// </summary>
    [Fact]
    public void Pruning_removes_the_detail_and_keeps_the_statistics()
    {
        ThreeCommits();

        var pruned = Prune(before: null, keep: 1);

        Assert.Equal(2, pruned.Snapshots);
        Assert.True(pruned.CellChanges > 0);

        using var query = Query();

        var document = query.Diff(_project, "main");

        Assert.Equal(3, document.Snapshots.Count);
        Assert.Equal(2, document.Totals.Pruned);

        // The oldest two say what happened to them rather than showing nothing.
        Assert.True(document.Snapshots[0].Pruned);
        Assert.Empty(document.Snapshots[0].Cells);

        Assert.False(document.Snapshots[2].Pruned);
        Assert.Single(document.Snapshots[2].Cells);

        // And the statistics of a pruned snapshot are still there.
        Assert.Equal(1, query.Stats(_project, "main", "aaaaaaaa1111").Data.Totals.Rows);
    }

    /// <summary>
    /// `--keep` is a floor rather than an alternative to the cutoff: a branch nobody has
    /// touched for a year would otherwise lose everything and become a history with no
    /// history in it.
    /// </summary>
    [Fact]
    public void The_most_recent_snapshots_survive_any_cutoff()
    {
        ThreeCommits();

        // "older than now", so the cutoff excludes nothing and `keep` is the only thing
        // deciding. The fixture's commits are dated today, so a cutoff of a day ago
        // would spare all three and say nothing about the floor.
        var pruned = Prune(before: "0d", keep: 2);

        Assert.Equal(1, pruned.Snapshots);

        using var query = Query();

        Assert.Equal(1, query.Diff(_project, "main").Totals.Pruned);
    }

    [Fact]
    public void A_cutoff_nothing_is_older_than_prunes_nothing()
    {
        ThreeCommits();

        Assert.Equal(0, Prune(before: "3650d", keep: 0).Snapshots);
    }

    [Fact]
    public void Pruning_twice_finds_nothing_the_second_time()
    {
        ThreeCommits();

        Assert.Equal(2, Prune(before: null, keep: 1).Snapshots);
        Assert.Equal(0, Prune(before: null, keep: 1).Snapshots);
    }

    [Fact]
    public void An_age_that_is_not_an_age_is_refused()
    {
        Assert.Throws<TabbitException>(() => HistoryMaintenance.ParseCutoff("a while"));
    }

    /// <summary>
    /// A value still referenced by the surviving snapshot's state must not be collected,
    /// or the history would show blanks where it holds values.
    /// </summary>
    [Fact]
    public void Collecting_the_value_pool_keeps_what_the_current_state_uses()
    {
        ThreeCommits();

        Prune(before: null, keep: 1);

        using var store = HistoryStore.Open(_connectionString, _project, "main");

        var cells = store.ReadCells("Item", new[] { "1" });

        Assert.Equal("Sword", cells[new CellAddress("1", "name")]);
        Assert.Equal("30", cells[new CellAddress("1", "power")]);
    }

    [Fact]
    public void Branches_and_tables_can_be_listed()
    {
        ThreeCommits();

        using var query = Query();

        Assert.Contains(_project, query.Projects());
        Assert.Equal(new[] { "main" }, query.Branches(_project));
        Assert.Equal(new[] { "Item" }, query.Tables(_project, "main"));
        Assert.Equal("main", query.DefaultBranch(_project));
    }

    [Fact]
    public void Snapshots_can_be_listed_newest_first_with_their_counts()
    {
        ThreeCommits();

        using var query = Query();

        var snapshots = query.Snapshots(_project, "main");

        Assert.Equal(new[] { "cccccccc3333", "bbbbbbbb2222", "aaaaaaaa1111" },
            snapshots.Select(s => s.Commit));

        Assert.Equal(1, snapshots[0].Counts.Cells);
    }
}
