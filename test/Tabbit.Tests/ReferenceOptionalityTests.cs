using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A reference cell left empty, and one holding a written zero.
/// </summary>
/// <remarks>
/// A `foreign` cell holds the target's index, so an empty one parses to zero - and zero is
/// this tool's convention for "points at nothing". The value alone therefore cannot tell a
/// cell nobody filled in from one somebody wrote a zero into, which is why the answer comes
/// from `Cell.HasValue` and from what the column declared rather than from the value.
///
/// The refusal is not new: the value parser used to stop a blank required reference with
/// "cannot parse `` as a value of type `Int32`". What is new is that the refusal now names
/// the reference and says how to fix it, which is the whole of what these gates check on
/// that side.
///
/// spec/reference-optionality.md.
/// </remarks>
public class ReferenceOptionalityTests
{
    private static JsonElement Holder(string scenario)
        => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(scenario), "json-named", "Holder.json"))).RootElement;

    /// <summary>
    /// A required reference nobody filled in is refused, at the cell, in words that say what
    /// to do about it.
    /// </summary>
    /// <remarks>
    /// The two ways out are both named because which one is right is the author's call: the
    /// row may be missing a target, or the column may have been marked required by habit. A
    /// message that offers only the first sends anyone in the second case looking for a row
    /// to invent.
    ///
    /// The notation for declaring a column optional is a layout's own, so the message stops
    /// at "declare the column optional" rather than spelling one.
    /// </remarks>
    [Fact]
    public void A_required_reference_left_empty_is_refused()
    {
        var result = TabbitRunner.Convert("reference-required-blank");

        Assert.False(result.Succeeded, "A required reference left empty was accepted.");

        Assert.Contains("`Holder.Must` references `Target`", result.StdOut);
        Assert.Contains("leaves it empty, but the column is declared required", result.StdOut);

        // Both ways out, since the message is the only place they are offered.
        Assert.Contains("Give it a row to point at", result.StdOut);
        Assert.Contains("declare the column optional", result.StdOut);

        // And the cell, because a blank is not something an author finds by re-reading.
        Assert.Contains("reference-required-blank.xlsx : Refs : J10", result.StdOut);
    }

    /// <summary>
    /// The refusal comes from the rule rather than from the value parser.
    /// </summary>
    /// <remarks>
    /// This is the regression that matters. The check reads `Cell.HasValue`, and the layout
    /// only produces a false one where a blank is allowed to mean absence - so a layout that
    /// hands the blank to `int.Parse` first makes the check unreachable while every gate
    /// above still passes on the parser's message. Naming the type that a reader of this
    /// message would have no reason to care about is what tells the two apart.
    /// </remarks>
    [Fact]
    public void The_refusal_is_the_rule_and_not_a_failed_parse()
    {
        var result = TabbitRunner.Convert("reference-required-blank");

        Assert.DoesNotContain("as a value of type `Int32`", result.StdOut);
    }

    /// <summary>
    /// Where the column says a blank may mean absence, it does, and it travels as null.
    /// </summary>
    [Fact]
    public void An_optional_reference_left_empty_is_absence()
    {
        var result = TabbitRunner.Convert("reference-optional");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var rows = Holder("reference-optional");

        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("maybe").ValueKind);
    }

    /// <summary>
    /// A written zero stays a zero, in both kinds of column.
    /// </summary>
    /// <remarks>
    /// Zero is the convention for "points at nothing" and it is left alone deliberately -
    /// what the rule catches is a cell nobody filled in acquiring a meaning, not the meaning
    /// of a value somebody typed. Checked in the optional column as well as the required one
    /// because that is where the two could be confused: a column that can say absence is
    /// exactly the one where a zero might be quietly read as saying it.
    /// </remarks>
    [Fact]
    public void A_written_zero_passes_in_both_kinds_of_column()
    {
        var result = TabbitRunner.Convert("reference-optional");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var rows = Holder("reference-optional");

        // Required column, second row.
        Assert.Equal(0, rows[1].GetProperty("zero").GetInt32());

        // Optional column, third row - a zero and not a null.
        Assert.Equal(JsonValueKind.Number, rows[2].GetProperty("maybe").ValueKind);
        Assert.Equal(0, rows[2].GetProperty("maybe").GetInt32());
    }
}
