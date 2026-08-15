using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Cell values that used to be accepted silently.
///
/// Each of these was a lenient fallback that turned a human mistake into wrong data
/// rather than a message - which is the opposite of what a tool whose purpose is
/// static validation should do.
/// </summary>
public class StrictValueTests
{
    /// <summary>
    /// A19 - a boolean cell holding neither a recognized word nor a number.
    ///
    /// ParseBool fell through to false, so `Ture` and `Yes please` both read as false
    /// and the sheet looked fine. An empty cell is still false, deliberately: a blank
    /// means "not set" and false is the useful reading of that.
    /// </summary>
    [Fact]
    public void A19_unrecognized_boolean_text_is_rejected()
    {
        var result = TabbitRunner.Convert("strict-values");

        Assert.False(result.Succeeded, "A misspelled boolean was accepted.");
        Assert.Contains("`Ture` is not a boolean", result.StdOut);

        // The message says what is accepted, since the answer is not obvious.
        Assert.Contains("Y/N", result.StdOut);

        // And where it is.
        Assert.Contains("strict-values.xlsx : Bad : C10", result.StdOut);
    }

    /// <summary>
    /// A22 - more than one `*` on a field name.
    ///
    /// One `*` marks a secondary index. Stripping only the first left `*Name`, which
    /// then failed the identifier check - reporting that `*Name` is not a valid
    /// identifier, which names the symptom rather than the typo.
    /// </summary>
    [Fact]
    public void A22_repeated_index_marker_is_reported_as_such()
    {
        var result = TabbitRunner.Convert("double-star");

        Assert.False(result.Succeeded, "A doubled index marker was accepted.");
        Assert.Contains("more than one leading `*`", result.StdOut);
        Assert.Contains("secondary index", result.StdOut);
    }

    /// <summary>
    /// A20 - a formula that evaluated to an error.
    ///
    /// The cell used to yield the literal text `$error$`. A typed column would at
    /// least fail to parse it, but a string column stored it, so a broken formula
    /// reached the game as the text "$error$".
    /// </summary>
    [Fact]
    public void A20_formula_error_cells_are_reported()
    {
        var result = TabbitRunner.Convert("formula-error");

        Assert.False(result.Succeeded, "A formula error cell was accepted.");
        Assert.Contains("#DIV/0!", result.StdOut);
        Assert.DoesNotContain("$error$", result.StdOut);
    }

    /// <summary>
    /// A21 - an enum cell holding the label's number instead of its name.
    ///
    /// Designers do write `1` rather than `Common`, and the intent is unambiguous, so
    /// refusing it was pedantry.
    /// </summary>
    [Fact]
    public void A21_enum_cells_may_hold_the_label_value()
    {
        var result = TabbitRunner.Convert("enum-by-value");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(
            RepoLayout.OutputDir("enum-by-value"), "json-named", "Items.json"));

        // Authored as `2` and `Rare` respectively; both resolve to the same label.
        Assert.Contains("\"grade\": 2", json);
    }
}
