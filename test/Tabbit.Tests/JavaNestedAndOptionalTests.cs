using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the Java generator's record groups and optional columns compile.
/// </summary>
/// <remarks>
/// Compile-only, for the reason the gates next door are: the round-trip harness names one
/// fixture's tables and cannot be pointed at these, and what a golden tree cannot answer is
/// whether the page it holds is a program.
///
/// Java is where a record group's element type is placed differently from every target before
/// it - nested in the record rather than beside it, because a public top-level type must be
/// alone in its file. That the nesting resolves from the table class next door is exactly what
/// only a compiler can say.
/// </remarks>
public class JavaNestedAndOptionalTests
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
    /// A record whose member is itself a record, which declares a class per level.
    /// </summary>
    /// <remarks>
    /// Both classes are nested in the same record, so the nested type name has to carry the
    /// path - and the member is constructed at its declaration, because Java would otherwise
    /// leave it null. spec/nested-multi-level.md.
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
        Assert.True(ConformanceHarness.JavaIsAvailable(out string why),
            $"A Java toolchain is required to check the generated code. {why}");

        var result = ConformanceHarness.CompileJava(scenario);

        Assert.True(result.Succeeded,
            $"The generated Java for `{scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }
}
