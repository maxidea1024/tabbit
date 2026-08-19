using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the Go generator's record groups and optional columns are a Go program.
/// </summary>
/// <remarks>
/// Compile-only, for the reason the gates next door are: the round-trip harness names one
/// fixture's tables and cannot be pointed at these, and what a golden tree cannot answer is
/// whether the page it holds compiles.
///
/// Go is stricter about this than most of the thirteen and so answers more: an unused import,
/// an unused local and a value assigned to the wrong type are all errors rather than warnings,
/// which is what makes `go build` worth running over generated code that no test executes.
/// </remarks>
public class GoNestedAndOptionalTests
{
    /// <summary>
    /// A record, an array of records whose members are of different types, and a scalar
    /// serial field beside them.
    /// </summary>
    [Fact]
    public void A_record_group_compiles() => AssertCompiles("nested");

    /// <summary>
    /// Every type that can be optional, including the array and the enum.
    /// </summary>
    [Fact]
    public void Optional_columns_compile() => AssertCompiles("optional");

    /// <summary>
    /// A record array whose length is each row's, which reads a count per member.
    /// </summary>
    [Fact]
    public void A_trimmed_record_array_compiles() => AssertCompiles("record-trim");

    /// <summary>
    /// A record whose members are arrays - the same columns as an array of records, turned
    /// inside out. spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_compiles() => AssertCompiles("member-array");

    /// <summary>
    /// A record whose member is itself a record, which declares a struct per level.
    /// </summary>
    /// <remarks>
    /// Go answers more than most here: a struct field of a type declared beside it, and an
    /// assignment through two levels, are both things it refuses to compile if the view got the
    /// type name or the member path wrong. spec/nested-multi-level.md.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_compiles() => AssertCompiles("nested-deep");

    /// <summary>
    /// A record whose member references another table.
    /// </summary>
    /// <remarks>
    /// What the element type holds grew by two - the row the reference resolved to beside the
    /// key that came off the wire - and the read and the linking both have to name them the
    /// way the declaration does. Every shape a record group has is in this fixture, including
    /// the trimmed one and a target keyed by a string.
    /// spec/references-in-records.md.
    /// </remarks>
    [Fact]
    public void A_reference_inside_a_record_compiles() => AssertCompiles("record-ref");

    /// <summary>
    /// An array of references: numbered reference columns folded into one array.
    /// </summary>
    /// <remarks>
    /// The shape `foreign[]`'s refusal points at, and nothing in the corpus held one - so this
    /// page was generated for thirteen languages and never compiled. Both forms of a reference
    /// are in the fixture, because they resolve to different types: a whole row and one of that
    /// row's values. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void An_array_of_references_compiles() => AssertCompiles("serial-ref");

    /// <summary>
    /// An array whose elements may be absent, which reads a second bitmap per column.
    /// </summary>
    /// <remarks>
    /// The read walks the bitmap with a counter that steps once per element of every row, and
    /// the per-element answer beside the value is a shape the page did not have before.
    /// spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void Optional_array_elements_compile()
        => AssertCompiles("nullable-elements");

    private static void AssertCompiles(string scenario)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"A Go toolchain is required to check the generated code. {why}");

        var result = ConformanceHarness.CompileGo(scenario);

        Assert.True(result.Succeeded,
            $"The generated Go for `{scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }
}
