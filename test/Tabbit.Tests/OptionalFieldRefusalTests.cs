using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The places a trailing `?` is refused, converted end to end.
/// </summary>
/// <remarks>
/// <see cref="OptionalFieldTests"/> covers the same rules against the model. These two go
/// through the whole converter, because the point of them is the diagnostic a sheet author
/// reads: it has to name the cell and say what to change.
///
/// spec/optional-fields.md has the rules. The shapes that do work are pinned by the
/// `optional` golden.
/// </remarks>
public class OptionalFieldRefusalTests
{
    /// <summary>
    /// An index column marked optional stops the conversion.
    /// </summary>
    /// <remarks>
    /// Left alone it would hand every blank row the same index 0, and the failure would
    /// surface as duplicate keys - or, in a table with one such row, not at all. The rule is
    /// worth an end-to-end test because it is the one refusal of this feature that a real
    /// sheet is likely to hit by copying a type from the column next to it.
    /// </remarks>
    [Fact]
    public void An_optional_index_is_refused()
    {
        var result = TabbitRunner.Convert("optional-index");

        Assert.False(result.Succeeded, $"Expected a refusal.\n{result.Describe()}");

        string output = result.StdOut + result.StdErr;

        Assert.Contains("index field cannot be optional", output);

        // The cell, so the author knows which one to edit rather than which table to search.
        Assert.Contains("optional-index.xlsx", output);
    }
}
