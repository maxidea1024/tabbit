using System.Collections.Generic;
using System.Linq;
using Tabbit;
using Tabbit.Models;
using Tabbit.Recipe;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The list a recipe writes of reports it knows about and does not stop for.
/// </summary>
/// <remarks>
/// Every one of these is about the list staying honest rather than about it working: a list
/// that silences reports is easy, and one that tells you when it has gone out of date is the
/// feature. spec/known-problems.md.
/// </remarks>
public class KnownProblemTests
{
    private static Location At(string file, string sheet, int column, int row)
        => new Location { Filename = file, Sheet = sheet, Column = column, Row = row };

    private static Diagnostics WithErrors(params Location[] where)
    {
        var diagnostics = new Diagnostics();

        foreach (var location in where)
            diagnostics.Error(location, "something is wrong");

        return diagnostics;
    }

    private static KnownProblemRecipe Known(string at, string reason = "not ours to fix", int count = 0)
        => new KnownProblemRecipe { At = at, Reason = reason, Count = count };

    /// <summary>
    /// A written-down report stops ending the run, and stops being an error.
    /// </summary>
    [Fact]
    public void A_known_report_becomes_a_note()
    {
        var diagnostics = WithErrors(At("data/book.xlsx", "Sheet1", 2, 8));

        diagnostics.ApplyKnownProblems(new[] { Known("book.xlsx : Sheet1 : C9") });

        Assert.Equal(0, diagnostics.Count);
        Assert.Equal(0, diagnostics.ErrorCount);
        Assert.Equal(1, diagnostics.InfoCount);
    }

    /// <summary>
    /// The reason comes out with the report, because the list is a note and not a switch.
    /// </summary>
    [Fact]
    public void The_reason_is_reported_beside_the_problem()
    {
        var diagnostics = WithErrors(At("data/book.xlsx", "Sheet1", 0, 0));

        diagnostics.ApplyKnownProblems(new[] { Known("book.xlsx", "the sheet's owner is fixing it") });

        Assert.Contains("the sheet's owner is fixing it", diagnostics.Entries.Single().Detail.Message);
        Assert.Contains("something is wrong", diagnostics.Entries.Single().Detail.Message);
    }

    /// <summary>
    /// Three ways to name a place, widest first.
    /// </summary>
    /// <remarks>
    /// The file is matched by the end of the path, so one list works wherever the folder sits.
    /// </remarks>
    [Theory]
    [InlineData("book.xlsx")]
    [InlineData("data/book.xlsx")]
    [InlineData("book.xlsx : Sheet1")]
    [InlineData("book.xlsx : Sheet1 : C9")]
    public void A_place_may_be_a_file_a_sheet_or_a_cell(string place)
    {
        var diagnostics = WithErrors(At("data/book.xlsx", "Sheet1", 2, 8));

        diagnostics.ApplyKnownProblems(new[] { Known(place) });

        Assert.Equal(0, diagnostics.ErrorCount);
    }

    /// <summary>
    /// A place that names another sheet, or another cell, leaves the report alone.
    /// </summary>
    [Theory]
    [InlineData("other.xlsx")]
    [InlineData("book.xlsx : Other")]
    [InlineData("book.xlsx : Sheet1 : D9")]
    public void A_place_that_does_not_cover_the_report_leaves_it(string place)
    {
        var diagnostics = WithErrors(At("data/book.xlsx", "Sheet1", 2, 8));

        diagnostics.ApplyKnownProblems(new[] { Known(place) });

        // The report itself, and the entry that matched nothing.
        Assert.Equal(2, diagnostics.ErrorCount);
    }

    /// <summary>
    /// An entry that matched nothing is an error, whichever reason it is.
    /// </summary>
    /// <remarks>
    /// The problem is fixed or the place is wrong, and both are reasons to take the entry out.
    /// A list that keeps entries nobody checks is the thing this feature has to not become.
    /// </remarks>
    [Fact]
    public void An_entry_that_matches_nothing_is_an_error()
    {
        var diagnostics = new Diagnostics();

        diagnostics.ApplyKnownProblems(new[] { Known("book.xlsx : Sheet1") });

        Assert.Equal(1, diagnostics.ErrorCount);
        Assert.Contains("nothing was reported there", diagnostics.Entries.Single().Detail.Message);
    }

    /// <summary>
    /// A count that is short says something new is wrong in a place already written down.
    /// </summary>
    [Fact]
    public void One_more_report_than_the_count_is_an_error()
    {
        var diagnostics = WithErrors(
            At("book.xlsx", "Sheet1", 0, 0),
            At("book.xlsx", "Sheet1", 0, 1),
            At("book.xlsx", "Sheet1", 0, 2));

        diagnostics.ApplyKnownProblems(new[] { Known("book.xlsx : Sheet1", count: 2) });

        var about = diagnostics.Entries.Single(entry => entry.Severity == Severity.Error);

        Assert.Contains("accounts for 3", about.Detail.Message);
        Assert.Contains("Something new is wrong there", about.Detail.Message);
    }

    /// <summary>
    /// A count that is long says some of it is fixed, which is also worth stopping for.
    /// </summary>
    /// <remarks>
    /// The direction that keeps the list pruned. Nothing else would ever tell you that an
    /// entry now covers more than the sheets do.
    /// </remarks>
    [Fact]
    public void One_report_fewer_than_the_count_is_an_error()
    {
        var diagnostics = WithErrors(At("book.xlsx", "Sheet1", 0, 0));

        diagnostics.ApplyKnownProblems(new[] { Known("book.xlsx : Sheet1", count: 2) });

        var about = diagnostics.Entries.Single(entry => entry.Severity == Severity.Error);

        Assert.Contains("Some of it is fixed", about.Detail.Message);
    }

    /// <summary>
    /// An entry with no reason is refused, and so is one with no place.
    /// </summary>
    [Theory]
    [InlineData("book.xlsx", "")]
    [InlineData("", "a reason")]
    public void An_entry_needs_both_a_place_and_a_reason(string at, string reason)
    {
        var diagnostics = WithErrors(At("book.xlsx", "Sheet1", 0, 0));

        diagnostics.ApplyKnownProblems(
            new[] { new KnownProblemRecipe { At = at, Reason = reason } });

        // The report stayed an error, and the entry is reported as one too.
        Assert.Equal(2, diagnostics.ErrorCount);
        Assert.Contains(diagnostics.Entries,
            entry => entry.Detail.Message.Contains("needs both `At` and `Reason`"));
    }

    /// <summary>
    /// An entry with nothing in it at all is the skeleton's placeholder, and is skipped.
    /// </summary>
    /// <remarks>
    /// `--new-recipe` fills every list with a blank entry so that the shape is visible, and
    /// that file has to run. Half an entry is still a mistake.
    /// </remarks>
    [Fact]
    public void A_wholly_blank_entry_is_not_an_entry()
    {
        var diagnostics = new Diagnostics();

        diagnostics.ApplyKnownProblems(new[] { new KnownProblemRecipe() });

        Assert.Equal(0, diagnostics.ErrorCount);
        Assert.Empty(diagnostics.Entries);
    }

    /// <summary>
    /// A report about the run rather than about a cell is not covered by any place.
    /// </summary>
    /// <remarks>
    /// Such a report is the recipe's own problem - a path that is not there, a setting that
    /// contradicts another - and a list of places in sheets has no business reaching it.
    /// </remarks>
    [Fact]
    public void A_report_with_no_location_is_covered_by_nothing()
    {
        var diagnostics = new Diagnostics();
        diagnostics.Error(null, "the recipe names a folder that is not there");

        diagnostics.ApplyKnownProblems(new[] { Known("book.xlsx") });

        // Both the original report and the entry that matched nothing.
        Assert.Equal(2, diagnostics.ErrorCount);
    }

    /// <summary>
    /// The narrower entry takes its own report when both cover it.
    /// </summary>
    /// <remarks>
    /// First match wins, so a cell written above a sheet keeps its own reason and its own
    /// count rather than being swallowed by the wider one.
    /// </remarks>
    [Fact]
    public void The_first_place_that_covers_a_report_takes_it()
    {
        var diagnostics = WithErrors(
            At("book.xlsx", "Sheet1", 0, 0),
            At("book.xlsx", "Sheet1", 0, 1));

        diagnostics.ApplyKnownProblems(new[]
        {
            Known("book.xlsx : Sheet1 : A1", "the one cell", count: 1),
            Known("book.xlsx : Sheet1", "the rest of the sheet", count: 1),
        });

        Assert.Equal(0, diagnostics.ErrorCount);
        Assert.Contains(diagnostics.Entries, entry => entry.Detail.Message.Contains("the one cell"));
        Assert.Contains(diagnostics.Entries,
            entry => entry.Detail.Message.Contains("the rest of the sheet"));
    }

    /// <summary>
    /// A warning is written down the same way an error is.
    /// </summary>
    /// <remarks>
    /// It matters for a run with `TreatWarningsAsErrors`, where a warning is what stops it -
    /// and a list that only reached errors would have nothing to say there.
    /// </remarks>
    [Fact]
    public void A_warning_is_written_down_too()
    {
        var diagnostics = new Diagnostics { PromoteWarnings = true };
        diagnostics.Warn(At("book.xlsx", "Sheet1", 0, 0), "worth a look");

        diagnostics.ApplyKnownProblems(new[] { Known("book.xlsx") });

        Assert.Equal(0, diagnostics.Count);
        Assert.Equal(1, diagnostics.InfoCount);
    }
}
