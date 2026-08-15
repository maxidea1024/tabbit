using Tabbit.Models;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A5 - spreadsheet column labelling.
///
/// Cell references appear in every diagnostic Tabbit prints and in the
/// `&amp;range=` fragment of its Google Sheets deep links, so an off-by-anything
/// here sends people to the wrong cell while looking authoritative.
///
/// The previous implementation counted the alphabet as 24 letters and only ever
/// emitted a single leading 'A', so everything from column X onward was wrong.
/// </summary>
public class LocationTests
{
    [Theory]
    // Single letters, including the two the old 24-based arithmetic got wrong.
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(23, "X")]
    [InlineData(24, "Y")]
    [InlineData(25, "Z")]
    // First carry.
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    // Last two-letter column and the first three-letter one. Bijective base-26
    // has no zero digit, so these are the boundaries that expose a naive carry.
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    // Excel's own last column.
    [InlineData(16383, "XFD")]
    public void ColumnName_matches_spreadsheet_labelling(int column, string expected)
    {
        Assert.Equal(expected, Location.ColumnName(column));
    }

    [Fact]
    public void CellRange_combines_column_label_with_one_based_row()
    {
        var location = new Location { Column = 26, Row = 8 };

        Assert.Equal("AA9", location.CellRange);
    }
}
