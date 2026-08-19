using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the C generator's record groups and optional columns are valid C.
/// </summary>
/// <remarks>
/// Compile-only, for the reason the C++ gate next door is: the round-trip harness names one
/// fixture's tables and cannot be pointed at these, and what a golden tree cannot answer is
/// whether the emitted header is a translation unit at all - a struct used before its tag is
/// declared, a member of a type that does not exist, a name that collides with the table
/// beside it.
///
/// C is where a record group costs the most to name: there is one namespace for struct tags,
/// so the element type carries the record's name to keep two tables' `Slot` groups apart.
/// </remarks>
public class CNestedAndOptionalTests
{
    [Fact]
    public void A_record_group_compiles()
        => AssertCompiles("nested", "Nested");

    [Fact]
    public void Optional_columns_compile()
        => AssertCompiles("optional", "Optional");

    [Fact]
    public void A_trimmed_record_array_compiles()
        => AssertCompiles("record-trim", "RecordTrim");

    /// <summary>
    /// A record whose members are arrays - the same columns as an array of records, turned
    /// inside out. spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_compiles()
        => AssertCompiles("member-array", "MemberArray");

    /// <summary>
    /// A record whose member is itself a record, which declares a struct per level.
    /// </summary>
    /// <remarks>
    /// C requires the levels declared innermost first - a struct has to be complete before
    /// another declares a member of it - and it has one namespace for struct tags, so the
    /// nested type name has to carry the path. Neither is something a golden tree can answer.
    /// spec/nested-multi-level.md.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_compiles()
        => AssertCompiles("nested-deep", "NestedDeep");

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
    public void A_reference_inside_a_record_compiles() => AssertCompiles("record-ref", "RecordRef");

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
        => AssertCompiles("nullable-elements", "NullableElements");

    private static void AssertCompiles(string scenario, string accessor)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(CToolchain.IsAvailable(out string why),
            $"A C compiler is required to check the generated code. {why}");

        string includeDir = Path.Combine(RepoLayout.OutputDir(scenario), "c");
        string workDir = Path.Combine(RepoLayout.OutputDir("_ccheck"), scenario + "-nested");

        // Every generated .c, so the check covers the read paths and not only the headers -
        // the record and presence shapes are almost entirely in the .c files.
        var sources = Directory
            .EnumerateFiles(includeDir, "*.c", SearchOption.AllDirectories)
            .ToList();

        var result = CToolchain.CompileOnly(workDir, includeDir, sources, accessor + ".h");

        Assert.True(result.Succeeded,
            $"The generated C for `{scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }
}
