using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the Swift generator's record groups and optional columns compile.
/// </summary>
/// <remarks>
/// Compile-only, for the reason the gates next door are: the round-trip harness names one
/// fixture's tables and cannot be pointed at these, and what a golden tree cannot answer is
/// whether the page it holds is a program.
///
/// Swift asks two questions of its own here.
///
/// A record element is a struct while a row is a class, so a member column's assignment goes
/// through a subscript into a value type - `record.slots[j].position.x = v`. That mutates in
/// place or it does not compile, and which one it is is a compiler's answer rather than a
/// diff's. spec/swift-language-support.md.
///
/// And every one of these fixtures is type-checked in both language modes with warnings as
/// errors, because generated code lands in somebody else's build and a consumer picks the
/// mode.
/// </remarks>
public class SwiftNestedAndOptionalTests
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
    /// <remarks>
    /// The one place this output deliberately reads unlike Swift: a value property with a
    /// `has` beside it rather than a `T?`. If that shape did not compile there would be no
    /// argument left for it. spec/optional-fields.md.
    /// </remarks>
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
    /// Both structs are nested in the same record, so the nested type name has to carry the
    /// path. And the member is initialized by calling its own struct, which is what lets the
    /// values inside it reach the empty values a scalar member gets.
    /// spec/nested-multi-level.md.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_compiles() => AssertCompiles("nested-deep");

    /// <summary>
    /// A record whose member references another table.
    /// </summary>
    /// <remarks>
    /// What the element holds grew by two - the row the reference resolved to beside the key
    /// that came off the wire - and the read and the linking both have to name them the way
    /// the declaration does. The resolved row is an optional class reference inside a struct,
    /// which is the shape that makes rows classes in the first place: a copy would duplicate
    /// the row rather than point at it. spec/references-in-records.md.
    /// </remarks>
    [Fact]
    public void A_reference_inside_a_record_compiles() => AssertCompiles("record-ref");

    /// <summary>
    /// An array of references: numbered reference columns folded into one array.
    /// </summary>
    /// <remarks>
    /// Both forms of a reference are in the fixture, because they resolve to different types:
    /// a whole row and one of that row's values. spec/nullable-array-elements.md.
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
    public void Optional_array_elements_compile() => AssertCompiles("nullable-elements");

    private static void AssertCompiles(string scenario)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.SwiftIsAvailable(out string why),
            $"A Swift toolchain is required to check the generated code. {why}");

        var result = ConformanceHarness.CompileSwift(scenario);

        Assert.True(result.Succeeded,
            $"The generated Swift for `{scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }
}
