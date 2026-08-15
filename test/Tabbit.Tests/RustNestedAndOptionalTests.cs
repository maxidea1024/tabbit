using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the Rust generator's record groups and optional columns are a Rust crate.
/// </summary>
/// <remarks>
/// Compile-only, for the reason the gates next door are: the round-trip harness names one
/// fixture's tables and cannot be pointed at these, and what a golden tree cannot answer is
/// whether the page it holds compiles.
///
/// Rust answers more of that question than most of the thirteen. A record group's element
/// type has to derive what the row derives or the row stops deriving it; a member read has to
/// borrow the row mutably exactly once; and a name re-exported from two modules is an error
/// rather than a shadowing. None of those are visible in a diff.
/// </remarks>
public class RustNestedAndOptionalTests
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
    /// A record whose member is itself a record, which declares a struct per level.
    /// </summary>
    /// <remarks>
    /// Rust answers a good deal here: a field of a type declared beside it, `Default` derived
    /// through both levels, and an assignment through two of them. spec/nested-multi-level.md.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_compiles() => AssertCompiles("nested-deep");

    /// <summary>
    /// A record whose member references another table.
    /// </summary>
    /// <remarks>
    /// This output keeps a reference as its key rather than resolving it - a record holding a
    /// borrow of another record is a graph, and Rust will not let one own its neighbours - so
    /// what the member gains here is its name and the type of that key.
    /// spec/references-in-records.md.
    /// </remarks>
    [Fact]
    public void A_reference_inside_a_record_compiles() => AssertCompiles("record-ref");

    /// <summary>
    /// A record whose members are arrays - the same columns as an array of records, turned
    /// inside out. spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_compiles() => AssertCompiles("member-array");

    private static void AssertCompiles(string scenario)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.RustIsAvailable(out string why),
            $"A Rust toolchain is required to check the generated code. {why}");

        var result = ConformanceHarness.CompileRust(scenario);

        Assert.True(result.Succeeded,
            $"The generated Rust for `{scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }
}
