using Tabbit.Helpers;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Splitting a column name written in the `Group.Member` notation.
///
/// The split happens before Pascal-casing, which is what makes it safe: the separator is
/// gone by the time the case conversion sees either part, so no rule about `_` or about
/// runs of capitals can produce or swallow one. The notation itself is spelled out in
/// spec/types/nested-fields.md.
/// </summary>
public class NestedNameTests
{
    [Theory]
    // A record with no serial number: one record, not an array.
    [InlineData("Pos.X", "Pos", "X")]
    [InlineData("Pos.Y", "Pos", "Y")]
    // A serial number on the group is what makes it an array. The number stays on the
    // group part, because that is the part the existing folding rules read.
    [InlineData("Slot1.Id", "Slot1", "Id")]
    [InlineData("Slot12.Count", "Slot12", "Count")]
    // Digits in the member are just part of its name and say nothing about folding.
    [InlineData("Slot1.Value2", "Slot1", "Value2")]
    // Neither part is normalized here, so what goes in comes out.
    [InlineData("slot_1.item_id", "slot_1", "item_id")]
    // Spaces around the separator are the kind of thing a spreadsheet cell collects.
    [InlineData("Slot1 . Id", "Slot1", "Id")]
    public void Splits_a_group_from_its_member(string raw, string group, string member)
    {
        Assert.True(NestedName.TrySplit(raw, out var parts, out string problem));
        Assert.Null(problem);
        Assert.Equal(new[] { group, member }, parts);
    }

    [Theory]
    // Depth is what the sheet wrote, not a number this knows. A level further in is read by
    // the same rule as the one outside it - spec/types/nested-multi-level.md.
    [InlineData("A.B.C", new[] { "A", "B", "C" })]
    [InlineData("Star1.Position.X", new[] { "Star1", "Position", "X" })]
    [InlineData("A.B.C.D.E", new[] { "A", "B", "C", "D", "E" })]
    // A level of digits alone is numbered rather than named, which is a shape and not a
    // typo: the level below an array of arrays has no name of its own.
    [InlineData("Grid1.2", new[] { "Grid1", "2" })]
    [InlineData("Grid1.2.3", new[] { "Grid1", "2", "3" })]
    public void Splits_as_many_levels_as_the_name_names(string raw, string[] expected)
    {
        Assert.True(NestedName.TrySplit(raw, out var parts, out string problem));
        Assert.Null(problem);
        Assert.Equal(expected, parts);
    }

    [Theory]
    // The ordinary case. Not nested is not a failure - it reports itself as a single
    // level, because every existing column in every existing sheet arrives here.
    [InlineData("Index")]
    [InlineData("Text1")]
    [InlineData("Item1Bonus")]
    [InlineData("already_snake")]
    [InlineData("")]
    [InlineData(null)]
    public void Reports_a_plain_column_by_returning_one_level(string raw)
    {
        Assert.True(NestedName.TrySplit(raw, out var parts, out string problem));
        Assert.Null(problem);
        Assert.Equal(new[] { raw }, parts);
    }

    [Theory]
    // An empty level is a typo far more often than an intent, and letting it through
    // produces a level with no name that fails much later.
    [InlineData(".Id")]
    [InlineData("Slot1.")]
    [InlineData(".")]
    [InlineData("Slot1. ")]
    [InlineData("A..B")]
    [InlineData("A.B.")]
    public void Refuses_a_name_it_cannot_support(string raw)
    {
        Assert.False(NestedName.TrySplit(raw, out _, out string problem));
        Assert.NotNull(problem);
        // The message is the middle of a sentence so the caller can put the cell in front
        // of it. If it ever starts with a capital, the diagnostics read wrong.
        Assert.False(char.IsUpper(problem[0]));
    }

    [Theory]
    [InlineData("Slot1.Id", true)]
    [InlineData("A.B.C", true)]
    [InlineData(".", true)]
    [InlineData("Slot1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Tells_a_nested_looking_name_from_a_plain_one(string raw, bool expected)
    {
        Assert.Equal(expected, NestedName.LooksNested(raw));
    }
}
