using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Tabbit;
using Tabbit.Models;
using Tabbit.Recipe;
using Tabbit.Reporting;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The report a run writes about itself.
/// </summary>
/// <remarks>
/// Golden covers none of this - no golden fixture holds a diagnostic - so these stand in for
/// it, and they are about the two claims the feature makes rather than about the markup. The
/// first is that a run which stopped still leaves a report, because that is the run the
/// report exists for. The second is that the report says whether problems are piling up.
///
/// The browser is never launched. What is checked is whether the run decided to launch one:
/// a test that actually opened a page would open it on whoever ran the suite.
/// spec/build-report.md §11.
/// </remarks>
public class BuildReportTests : IDisposable
{
    private readonly string _folder;
    private readonly Func<string, bool> _opener;

    public BuildReportTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tabbit-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        _opener = ReportOpening.Opener;
        ReportOpening.Opener = _ => true;
    }

    public void Dispose()
    {
        ReportOpening.Opener = _opener;

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder that could not be removed is not a failing test.
        }
    }

    // ------------------------------------------------------------------ set-up

    private Options OptionsFor(string recipeName = "book.jsonc")
        => new Options
        {
            RecipeFilename = Path.Combine(_folder, recipeName),
            CacheDirectory = Path.Combine(_folder, "cache"),
        };

    private static RecipeModel RecipeWith(ReportRecipe report)
        => new RecipeModel { Report = report };

    private static Location At(string file, string sheet, int column, int row)
        => new Location { Filename = file, Sheet = sheet, Column = column, Row = row };

    private static Diagnostics Found(params (Severity Severity, Location Where, string What)[] reports)
    {
        var diagnostics = new Diagnostics();

        foreach (var (severity, where, what) in reports)
            diagnostics.Add(severity, where, what);

        return diagnostics;
    }

    private static ReportDocument Read(string path)
        => JsonConvert.DeserializeObject<ReportDocument>(File.ReadAllText(path))!;

    // ------------------------------------------------------------------- basics

    /// <summary>
    /// The run this exists for: one that stopped still leaves its list behind.
    /// </summary>
    /// <remarks>
    /// The reports were collected before the throw and the failure arrives after it, and
    /// both have to be in the file. A report written only by a successful run would be
    /// written exactly when nobody needs it.
    /// </remarks>
    [Fact]
    public void A_run_that_failed_still_writes_its_report()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        var diagnostics = Found(
            (Severity.Error, At("book.xlsx", "Item", 2, 6), "the id is not a number"),
            (Severity.Warning, At("book.xlsx", "Item", 3, 6), "the icon has not been drawn"));

        report.Take(diagnostics);

        // What `ThrowIfAny` throws: a headline, and the stopping half of the collector the
        // stage already handed over.
        report.Failed(new TabbitException(null, "Validation failed")
        {
            Details =
            [
                new TabbitException.Detail
                {
                    Location = At("book.xlsx", "Item", 2, 6),
                    Message = "the id is not a number",
                },
            ],
        });

        report.Write(ExitCode.Failed, null);

        var written = Read(report.JsonPath);

        Assert.Equal(ReportOutcome.StoppedByValidation, written.Outcome);
        Assert.Equal(1, written.Counts.Errors);
        Assert.Equal(1, written.Counts.Warnings);
        Assert.True(File.Exists(report.HtmlPath));
    }

    /// <summary>
    /// A report that arrives twice - once from the stage, once on the exception - is one row.
    /// </summary>
    [Fact]
    public void A_report_carried_by_the_failure_is_not_counted_twice()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        var where = At("book.xlsx", "Item", 2, 6);

        report.Take(Found((Severity.Error, where, "the id is not a number")));

        report.Failed(new TabbitException(null, "Validation failed")
        {
            Details =
            [
                new TabbitException.Detail { Location = where, Message = "the id is not a number" },
            ],
        });

        report.Write(ExitCode.Failed, null);

        Assert.Equal(1, Read(report.JsonPath).Counts.Errors);
    }

    /// <summary>
    /// A defect is kept apart from the data's problems, with its stack.
    /// </summary>
    /// <remarks>
    /// The console says this out loud for a reason - the person holding the workbook cannot
    /// fix it - and the page has to make the same separation, or they go looking through
    /// their sheets for a cause that is not there.
    /// </remarks>
    [Fact]
    public void A_defect_is_reported_as_ours_rather_than_as_the_data()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        report.Failed(new TabbitDefectException("a type nobody taught this switch about"));
        report.Write(ExitCode.Failed, null);

        var written = Read(report.JsonPath);

        Assert.NotNull(written.Defect);
        Assert.Contains("nobody taught", written.Defect!.Message);
        Assert.Empty(written.Entries);
        Assert.Equal(ReportOutcome.Failed, written.Outcome);
    }

    /// <summary>
    /// The written-down reports are a list of their own.
    /// </summary>
    [Fact]
    public void Known_problems_are_kept_out_of_the_problem_list()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        var diagnostics = Found((Severity.Error, At("book.xlsx", "Item", 2, 6), "not ours"));

        diagnostics.ApplyKnownProblems(new[]
        {
            new KnownProblemRecipe { At = "book.xlsx", Reason = "the sheet's owner is fixing it" },
        });

        report.Take(diagnostics);
        report.Write(ExitCode.Success, null);

        var written = Read(report.JsonPath);

        Assert.Empty(written.Entries);
        Assert.Single(written.KnownProblems);
        Assert.Equal(0, written.Counts.Errors);
    }

    /// <summary>
    /// A hosted document's cell arrives as a link, and a workbook's as text.
    /// </summary>
    [Fact]
    public void A_cell_with_a_url_becomes_a_link_on_the_page()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        var linked = At("sheet-id", "Item", 2, 6);
        linked.SheetUrl = "https://docs.google.com/spreadsheets/d/x/edit#gid=1&range=C7";

        report.Take(Found(
            (Severity.Error, linked, "the id is not a number"),
            (Severity.Error, At("book.xlsx", "Item", 2, 6), "the id is not a number either")));

        report.Write(ExitCode.Failed, null);

        string page = File.ReadAllText(report.HtmlPath);

        Assert.Contains("href=\"https://docs.google.com/spreadsheets/d/x/edit#gid=1&amp;range=C7\"", page);
        Assert.Contains("book.xlsx : Item : C7", page);

        // The one it cannot link, it offers to copy rather than linking to nothing.
        Assert.Contains("class=\"copy\"", page);

        var written = Read(report.JsonPath);

        Assert.Equal("C7", written.Entries[0].Location!.Cell);
        Assert.Equal("", written.Entries[1].Location!.Url);
    }

    // -------------------------------------------------------------- comparison

    /// <summary>
    /// A first run marks nothing new, because there is nothing to have been new against.
    /// </summary>
    [Fact]
    public void A_first_run_says_it_had_nothing_to_compare_with()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        report.Take(Found((Severity.Error, At("book.xlsx", "Item", 2, 6), "the id is not a number")));
        report.Write(ExitCode.Failed, null);

        var written = Read(report.JsonPath);

        Assert.False(written.Counts.Compared);
        Assert.Equal(0, written.Counts.New);
        Assert.Equal(ReportFate.Uncompared, written.Entries.Single().Fate);
    }

    /// <summary>
    /// The three columns the whole thing is for: what is new, what is still here, what has gone.
    /// </summary>
    [Fact]
    public void The_second_run_says_what_is_new_what_stayed_and_what_was_fixed()
    {
        var options = OptionsFor();
        var settings = new ReportRecipe();

        var first = BuildReport.Create(options, RecipeWith(settings))!;

        first.Take(Found(
            (Severity.Error, At("book.xlsx", "Item", 2, 6), "the id is not a number"),
            (Severity.Error, At("book.xlsx", "Item", 2, 9), "this one gets fixed")));

        first.Write(ExitCode.Failed, null);

        var second = BuildReport.Create(options, RecipeWith(settings))!;

        second.Take(Found(
            (Severity.Error, At("book.xlsx", "Item", 2, 6), "the id is not a number"),
            (Severity.Error, At("book.xlsx", "Item", 4, 6), "and now this one too")));

        second.Write(ExitCode.Failed, null);

        var written = Read(second.JsonPath);

        Assert.True(written.Counts.Compared);
        Assert.Equal(1, written.Counts.New);
        Assert.Equal(1, written.Counts.Persisting);
        Assert.Equal(1, written.Counts.Resolved);
        Assert.Equal("this one gets fixed", written.Resolved.Single().Message);
    }

    /// <summary>
    /// Notes are left out of the comparison.
    /// </summary>
    /// <remarks>
    /// They say what was checked, so they arrive on every run by definition. Counting them
    /// would fill "still here" with things nobody has to do anything about, which is the
    /// column the page is read for.
    /// </remarks>
    [Fact]
    public void Notes_do_not_count_as_problems_that_are_still_here()
    {
        var options = OptionsFor();
        var settings = new ReportRecipe();

        var first = BuildReport.Create(options, RecipeWith(settings))!;
        first.Take(Found((Severity.Info, null, "checked 12,000 rows")));
        first.Write(ExitCode.Success, null);

        var second = BuildReport.Create(options, RecipeWith(settings))!;
        second.Take(Found((Severity.Info, null, "checked 12,000 rows")));
        second.Write(ExitCode.Success, null);

        var written = Read(second.JsonPath);

        Assert.Equal(0, written.Counts.Persisting);
        Assert.Equal(0, written.Counts.Resolved);
        Assert.Equal(1, written.Counts.Notes);
    }

    /// <summary>
    /// A previous report of a shape this build does not know costs the columns and nothing else.
    /// </summary>
    [Fact]
    public void A_previous_report_of_another_version_is_not_compared_against()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report.JsonPath))!);
        File.WriteAllText(report.JsonPath, "{ \"Version\": 99, \"Entries\": [] }");

        report.Take(Found((Severity.Error, At("book.xlsx", "Item", 2, 6), "the id is not a number")));
        report.Write(ExitCode.Failed, null);

        Assert.False(Read(report.JsonPath).Counts.Compared);
    }

    // ----------------------------------------------------------------- writing

    /// <summary>
    /// The report sits beside the build seal, and a recipe may send it somewhere else.
    /// </summary>
    [Fact]
    public void The_report_is_written_beside_the_seal_unless_the_recipe_says_otherwise()
    {
        var options = OptionsFor();

        var beside = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_folder, "cache")),
            Path.GetFullPath(Path.GetDirectoryName(beside.JsonPath)!));

        string elsewhere = Path.Combine(_folder, "artifacts");

        var moved = BuildReport.Create(options, RecipeWith(new ReportRecipe { Path = elsewhere }))!;

        Assert.Equal(
            Path.GetFullPath(elsewhere),
            Path.GetFullPath(Path.GetDirectoryName(moved.JsonPath)!));

        // Same stem either way, so the two halves of one run are found together.
        Assert.Equal(
            Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(beside.JsonPath)),
            Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(moved.HtmlPath)));
    }

    /// <summary>A recipe may switch the whole thing off.</summary>
    [Fact]
    public void A_recipe_that_wants_no_report_gets_none()
        => Assert.Null(BuildReport.Create(
            OptionsFor(), RecipeWith(new ReportRecipe { Enabled = false })));

    /// <summary>
    /// The page says where it stopped listing, and the JSON does not stop.
    /// </summary>
    [Fact]
    public void What_the_page_leaves_out_is_said_on_the_page_and_kept_in_the_json()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe { MaxHtmlEntries = 3 }))!;

        var many = new List<(Severity, Location, string)>();

        for (int row = 0; row < 10; row++)
            many.Add((Severity.Error, At("book.xlsx", "Item", 2, row), $"row {row} is wrong"));

        report.Take(Found(many.ToArray()));
        report.Write(ExitCode.Failed, null);

        Assert.Equal(10, Read(report.JsonPath).Entries.Count);

        string page = File.ReadAllText(report.HtmlPath);

        Assert.Contains("3 of 10", page);
        Assert.DoesNotContain("row 9 is wrong", page);
    }

    /// <summary>
    /// Reports are grouped by the sheet they came from rather than listed one after another.
    /// </summary>
    /// <remarks>
    /// A flat list of thousands is a wall. The reader's question is which sheet to open
    /// first, and a group answers it where a list only answers it by being read through.
    /// </remarks>
    [Fact]
    public void The_page_groups_reports_by_where_they_came_from()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        report.Take(Found(
            (Severity.Error, At("book.xlsx", "Item", 2, 6), "one"),
            (Severity.Error, At("book.xlsx", "Item", 2, 7), "two"),
            (Severity.Warning, At("book.xlsx", "Quest", 1, 3), "three"),
            (Severity.Error, null, "about the run itself")));

        report.Write(ExitCode.Failed, null);

        string page = File.ReadAllText(report.HtmlPath);

        // Three places, three groups - and the one with no cell has a group of its own.
        Assert.Equal(3, page.Split("<details class=\"grp\"").Length - 1);
        Assert.Contains("book.xlsx : Item</span>", page);
        Assert.Contains("book.xlsx : Quest</span>", page);
    }

    /// <summary>
    /// The filter's hiding actually hides.
    /// </summary>
    /// <remarks>
    /// A row is a flex box, and an author's `display` beats the one a browser attaches to
    /// the `hidden` attribute. Without the override the filter recounts every group and
    /// hides nothing - a page that says one match and shows twelve. Pinned here because it
    /// is invisible to every other kind of check: the markup is right, the script is right,
    /// and only the rendering is wrong.
    /// </remarks>
    [Fact]
    public void The_filter_can_hide_a_row()
    {
        var options = OptionsFor();
        var report = BuildReport.Create(options, RecipeWith(new ReportRecipe()))!;

        report.Take(Found((Severity.Error, At("book.xlsx", "Item", 2, 6), "the id is not a number")));
        report.Write(ExitCode.Failed, null);

        Assert.Contains("[hidden] { display: none !important; }", File.ReadAllText(report.HtmlPath));
    }

    // ------------------------------------------------------------------ opening

    /// <summary>
    /// What `OpenInBrowser` means, and that a misspelling is refused rather than defaulted.
    /// </summary>
    [Theory]
    [InlineData("never", false, false)]
    [InlineData("never", true, false)]
    [InlineData("problems", false, false)]
    [InlineData("problems", true, true)]
    [InlineData("always", false, true)]
    [InlineData("always", true, true)]
    public void The_page_opens_when_the_recipe_asked_and_there_is_a_reason(
        string written, bool hasProblems, bool expected)
        => Assert.Equal(expected, ReportOpening.Wanted(ReportOpening.PolicyOf(written), hasProblems));

    [Fact]
    public void An_OpenInBrowser_nobody_meant_is_refused()
        => Assert.Throws<TabbitException>(() => ReportOpening.PolicyOf("problmes"));

    /// <summary>
    /// Three ways of saying there is nobody in front of this run, and each beats the setting.
    /// </summary>
    /// <remarks>
    /// A build agent that opens a browser opens it for nobody and leaves a process behind,
    /// so these are not weighed against the recipe - they win.
    /// </remarks>
    [Theory]
    [InlineData(false, false, null, ReportOpening.Suppression.None)]
    [InlineData(false, false, "true", ReportOpening.Suppression.ContinuousIntegration)]
    [InlineData(false, true, null, ReportOpening.Suppression.NotATerminal)]
    [InlineData(true, false, null, ReportOpening.Suppression.Silent)]
    public void Nothing_opens_where_nobody_is_watching(
        bool silent, bool redirected, string ci, ReportOpening.Suppression expected)
        => Assert.Equal(expected, ReportOpening.SuppressedBy(silent, redirected, ci));

    /// <summary>
    /// `--show-report` opens the last one, and says so plainly when there is none.
    /// </summary>
    [Fact]
    public void The_last_report_can_be_opened_again_without_running_anything()
    {
        var options = OptionsFor();
        var recipe = RecipeWith(new ReportRecipe());

        Assert.Equal(ExitCode.Failed, BuildReport.ShowLast(options, recipe));

        var report = BuildReport.Create(options, recipe)!;
        report.Take(Found((Severity.Warning, At("book.xlsx", "Item", 2, 6), "worth a look")));
        report.Write(ExitCode.Success, null);

        string opened = null;
        ReportOpening.Opener = path => { opened = path; return true; };

        Assert.Equal(ExitCode.Success, BuildReport.ShowLast(options, recipe));
        Assert.Equal(Path.GetFullPath(report.HtmlPath), Path.GetFullPath(opened!));
    }
}
