using System;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Static validation of a cooked workbook.
///
/// The checks existed as a 204-line method that nothing ever called, and whose
/// uniqueness loop skipped exactly the fields it meant to inspect. Catching this
/// class of mistake before it reaches a game build is the tool's main claim, so
/// these tests cover both that the checks fire and that they report together.
/// </summary>
public class ValidationTests
{
    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Reporting every problem in one run is the point, not a nicety: a sheet with
    /// several mistakes used to take one run per mistake to clean up.
    /// </summary>
    [Fact]
    public void All_problems_in_a_workbook_are_reported_in_one_run()
    {
        var result = TabbitRunner.Convert("invalid");

        Assert.False(result.Succeeded, "The invalid fixture converted successfully.");

        Assert.Contains("(5 problems)", result.StdOut);

        // Validation failures.
        Assert.Contains("Index field `Catalog.Index` repeats the value `1`", result.StdOut);
        Assert.Contains("Index field `Catalog.Code` repeats the value `X`", result.StdOut);
        Assert.Contains("references `Catalog` row `99`, which does not exist", result.StdOut);

        // Resolution failures. These used to abort the run where they were found,
        // so they could never appear next to the problems above.
        Assert.Contains("references table `NoSuchTable`, which does not exist", result.StdOut);
        Assert.Contains("has no field named `NoSuchField`", result.StdOut);

        // Resolution and validation both look at references, so a broken one must
        // not be reported by each of them in turn.
        Assert.Equal(1, CountOccurrences(result.StdOut, "references table `NoSuchTable`"));
    }

    /// <summary>
    /// Each problem has to say where it is. A report that names a mistake without
    /// pointing at the cell leaves the reader to search the workbook for it.
    /// </summary>
    [Fact]
    public void Each_reported_problem_carries_a_cell_location()
    {
        var result = TabbitRunner.Convert("invalid");

        var reported = result.StdOut
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

        int problems = reported.Count(l => l.TrimStart().StartsWith("["));

        // Narrowed to lines naming a workbook: the runner passes --debug, so the
        // fatal report also prints stack frames, and those start with "at " too.
        int locations = reported.Count(l => l.TrimStart().StartsWith("at ") && l.Contains(".xlsx"));

        Assert.Equal(5, problems);
        Assert.Equal(problems, locations);

        // Locations are real cell references, not just a file name.
        Assert.Contains(reported, l => l.Contains("invalid.xlsx : Bad : B10"));
    }

    /// <summary>
    /// The duplicate-index check compares boxed values, so it has to go through
    /// Equals. A reference comparison treats every boxed int as distinct and finds
    /// nothing, which is what the original code did.
    /// </summary>
    [Fact]
    public void Duplicate_detection_compares_boxed_values_by_equality()
    {
        var result = TabbitRunner.Convert("invalid");

        // The duplicated primary index is an int - the case reference comparison
        // would miss entirely.
        Assert.Contains("Index field `Catalog.Index` repeats the value `1`", result.StdOut);
    }

    /// <summary>
    /// Target-side filtering can strip the table a surviving reference points at.
    /// Nothing used to notice, and the breakage surfaced in the consuming
    /// project's compiler rather than here.
    /// </summary>
    [Fact]
    public void Reference_to_a_table_excluded_by_target_side_is_rejected()
    {
        var result = TabbitRunner.Convert("side-dangling");

        Assert.False(result.Succeeded, "A client build kept a reference to a server-only table.");
        Assert.Contains("In a `client` build", result.StdOut);
        Assert.Contains("references table `ServerOnlyTarget`", result.StdOut);
    }

    /// <summary>
    /// The same workbook is perfectly valid when nothing is filtered out, so the
    /// check must not fire for an unfiltered build.
    /// </summary>
    [Fact]
    public void Cross_side_reference_is_accepted_when_nothing_is_filtered()
    {
        var result = TabbitRunner.Convert("side-dangling-both");

        Assert.True(result.Succeeded,
            $"An unfiltered build rejected a valid workbook.{Environment.NewLine}{result.Describe()}");
    }
}
