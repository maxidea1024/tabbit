using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That Unreal's record groups and optional columns are types the header tool accepts.
/// </summary>
/// <remarks>
/// UHT is the only thing that can answer this. A record group generates a `USTRUCT` and a
/// `TArray` of it, and whether that is a property Unreal will reflect is not a question a
/// compiler answers, let alone a golden tree - the header tool has its own rules about what
/// may be a UPROPERTY, and they are why the optional member is a `bHas` flag rather than a
/// `TOptional`.
///
/// Skipped when no engine is configured, like the other UHT tests: the gate needs a real
/// engine and not every checkout has one.
/// </remarks>
public class UnrealNestedAndOptionalTests
{
    [Fact]
    public void A_record_group_is_a_type_the_header_tool_accepts()
        => AssertHeaderToolAccepts("nested", "Nested", "FNested.h");

    [Fact]
    public void Optional_columns_are_types_the_header_tool_accepts()
        => AssertHeaderToolAccepts("optional", "Optional", "FOptional.h");

    [Fact]
    public void A_trimmed_record_array_is_a_type_the_header_tool_accepts()
        => AssertHeaderToolAccepts("record-trim", "RecordTrim", "FRecordTrim.h");

    /// <summary>
    /// A record whose member is itself a record, which declares a USTRUCT per level.
    /// </summary>
    /// <remarks>
    /// The claim worth checking here is that this shape **keeps** its reflection. A struct
    /// member of a USTRUCT type is a property UHT accepts, where the nested container an array
    /// of arrays needs is not - so unlike that shape, nothing had to be dropped from
    /// reflection, and UHT is the only thing that can say whether that is true.
    /// spec/nested-multi-level.md.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_is_a_type_the_header_tool_accepts()
        => AssertHeaderToolAccepts("nested-deep", "NestedDeep", "FNestedDeep.h");

    /// <summary>
    /// A record whose member references another table.
    /// </summary>
    /// <remarks>
    /// This output keeps a reference as its key rather than resolving it - a raw pointer in a
    /// USTRUCT is not something the garbage collector tracks - so what UHT is being asked here
    /// is whether the key member it declares inside the element type is a property.
    /// spec/references-in-records.md.
    /// </remarks>
    [Fact]
    public void A_reference_inside_a_record_is_a_type_the_header_tool_accepts()
        => AssertHeaderToolAccepts("record-ref", "RecordRefData", "FRecordRefData.h");

    private static void AssertHeaderToolAccepts(string scenario, string module, string header)
    {
        string engineRoot = Environment.GetEnvironmentVariable("TABBIT_UE_ROOT");

        if (string.IsNullOrEmpty(engineRoot))
            return;

        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        var result = UnrealToolchain.RunHeaderTool(
            engineRoot,
            Path.Combine(RepoLayout.OutputDir(scenario), "unreal", "Source", module),
            moduleName: module,
            headerName: header);

        Assert.True(result.Succeeded,
            $"Unreal Header Tool rejected the generated module for `{scenario}`."
            + $"{Environment.NewLine}{result.Output}");
    }
}
