using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// An array element left empty between two filled ones.
/// </summary>
/// <remarks>
/// The middle of an array is kept rather than closed up, so that index `k` is always the
/// column numbered `k` - that rule is not changing. What changed is whether such a row is
/// accepted at all: it travels as an array whose middle element holds the type's empty
/// value, and a consumer indexing into it cannot tell that from a value somebody wrote.
///
/// One workbook, two recipes. `record-trim` says the gap was meant and its golden holds what
/// the gap becomes; `record-trim-strict` reads the same rows without saying so and is
/// refused. Pinning the setting rather than two workbooks is what keeps the two halves about
/// the same data.
///
/// spec/variable-length-record-arrays.md has the rule and `AllowArrayGaps`.
/// </remarks>
public class ArrayGapTests
{
    [Fact]
    public void A_gap_in_an_array_is_refused_by_default()
    {
        var result = TabbitRunner.Convert("record-trim-strict");

        Assert.False(result.Succeeded, "An array with a gap in it was accepted.");

        // What is wrong, and which element of which group.
        Assert.Contains("leaves element 1 empty", result.StdOut);
        Assert.Contains("Loot.Slot", result.StdOut);

        // And where, because a row of empty-looking cells is not something an author finds
        // by re-reading the sheet.
        Assert.Contains("record-trim.xlsx : Trim : G11", result.StdOut);

        // The message says how to accept it, since the strict reading is a default rather
        // than a judgement about this sheet.
        Assert.Contains("AllowArrayGaps", result.StdOut);
    }

    /// <summary>
    /// And the same rows pass where the source says the gap was meant.
    /// </summary>
    /// <remarks>
    /// The `record-trim` golden is what says the gap is then kept rather than closed up, so
    /// this only has to establish that the conversion runs - the bytes are pinned elsewhere.
    /// </remarks>
    [Fact]
    public void The_same_rows_pass_where_the_source_allows_gaps()
    {
        var result = TabbitRunner.Convert("record-trim");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");
    }
}
