using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the C# generator's record groups and optional columns are valid C#.
/// </summary>
/// <remarks>
/// The gate the other four targets got when they learned these shapes, and C# - the first of
/// the thirteen - did not. Its absence was not theoretical: an optional array assigned
/// `default(int)` to an `int[]`, so the `optional` fixture's generated C# had never compiled,
/// and the golden recorded that page for as long as the feature had existed.
///
/// Compile-only, for the reason the C and C++ gates next door are: the round-trip driver names
/// one fixture's tables and cannot be pointed at these, and what a golden tree cannot answer is
/// whether the page it holds is a program.
/// </remarks>
public class CsNestedAndOptionalTests
{
    /// <summary>
    /// A record, an array of records whose members are of different types, and a scalar
    /// serial field beside them.
    /// </summary>
    [Fact]
    public void A_record_group_compiles()
        => AssertCompiles("nested", "NestedAccessor");

    /// <summary>
    /// Every type that can be optional, including the array and the enum.
    /// </summary>
    [Fact]
    public void Optional_columns_compile()
        => AssertCompiles("optional", "OptionalAccessor");

    /// <summary>
    /// A record array whose length is each row's, which reads a count per member.
    /// </summary>
    [Fact]
    public void A_trimmed_record_array_compiles()
        => AssertCompiles("record-trim", "RecordTrimAccessor");

    /// <summary>
    /// A record whose members are arrays - the same columns as an array of records, turned
    /// inside out. spec/types/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_compiles()
        => AssertCompiles("member-array", "MemberArrayAccessor");

    /// <summary>
    /// A record whose member is itself a record, which declares a struct per level.
    /// </summary>
    /// <remarks>
    /// The compile is the question here rather than the shape: a nested struct that names a
    /// type declared beside it, a member of that type, and a read assigning
    /// `_star[j].Position.X` all have to be legal in one file. The shape itself is pinned by
    /// `NestedTargetSupportTests` against the exported JSON.
    ///
    /// spec/types/nested-multi-level.md.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_compiles()
        => AssertCompiles("nested-deep", "NestedDeepAccessor");

    /// <summary>
    /// A record whose member references another table, which declares the resolved row, the
    /// key and the flag inside the element type. spec/references/references-in-records.md.
    /// </summary>
    [Fact]
    public void A_reference_inside_a_record_compiles()
        => AssertCompiles("record-ref", "RecordRefAccessor");

    /// <summary>
    /// An array of references: numbered reference columns folded into one array.
    /// </summary>
    /// <remarks>
    /// The shape `foreign[]`'s refusal points at, and nothing in the corpus held one - so this
    /// page was generated for every language and never compiled. Both forms of a reference
    /// are in the fixture, because they resolve to different types: a whole row and one of that
    /// row's values. spec/types/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void An_array_of_references_compiles()
        => AssertCompiles("serial-ref", "SerialRefAccessor");

    /// <summary>
    /// An array whose elements may be absent, which reads a second bitmap per column.
    /// </summary>
    /// <remarks>
    /// The read walks the bitmap with a counter that steps once per element of every row, and
    /// the per-element answer beside the value is a shape the page did not have before.
    /// spec/types/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void Optional_array_elements_compile()
        => AssertCompiles("nullable-elements", "NullableElementsAccessor");

    /// <summary>
    /// Five composite columns, each of which the cooker turned into a record.
    /// </summary>
    /// <remarks>
    /// The equivalence gate says the file is the same as a hand-written record's, and the
    /// golden says the page looks right. Neither says the page is a program - and the record
    /// here was assembled by a cooker pass rather than by a sheet, so what compiles is the
    /// part nothing else asks about. spec/types/composite-value-types.md.
    /// </remarks>
    [Fact]
    public void Composite_columns_compile()
        => AssertCompiles("composite", "CompositeAccessor");

    private static void AssertCompiles(string scenario, string accessor)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.Compile(scenario, accessor);

        Assert.True(result.Succeeded,
            $"The generated C# for `{scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }
}
