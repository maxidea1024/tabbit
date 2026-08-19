using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the C++ generator's record groups and optional columns are valid C++.
/// </summary>
/// <remarks>
/// Compile-only, and that is the question worth asking here. The round-trip driver next door
/// names the tables of one fixture, so it cannot be pointed at these; and what a golden tree
/// cannot answer is whether the emitted header is a program at all - a member declared as one
/// type and assigned another, a struct declared after its use, a name that collides with the
/// table beside it.
///
/// The two features are checked together because they were added together and share the
/// split they needed: declaration per field, reading per wire column.
/// </remarks>
public class CppNestedAndOptionalTests
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
    /// inside out. spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_inside_a_record_compiles()
        => AssertCompiles("nested-deep", "NestedDeepAccessor");

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
    public void A_reference_inside_a_record_compiles() => AssertCompiles("record-ref", "RecordRefAccessor");
    /// <summary>
    /// A record whose members are arrays - the same columns as an array of records, turned
    /// inside out. spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_compiles()
        => AssertCompiles("member-array", "MemberArrayAccessor");

    /// <summary>
    /// An array whose elements may be absent, which reads a second bitmap per column.
    /// </summary>
    /// <remarks>
    /// The read walks the bitmap with a counter that steps once per element of every row, and
    /// the accessor beside the value is a call rather than a field. Both are new shapes in the
    /// generated page. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void Optional_array_elements_compile()
        => AssertCompiles("nullable-elements", "NullableElementsAccessor");
    private static void AssertCompiles(string scenario, string accessor)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++ compiler is required to check the generated header. {why}");

        var result = CppToolchain.Compile(scenario, accessor);

        Assert.True(result.Succeeded,
            $"The generated C++ for `{scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }
}
