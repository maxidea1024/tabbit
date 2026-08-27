using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What the generated C and C++ headers include, and whether each one stands on its own.
///
/// These two are the only targets where this can go wrong at all. Every other language
/// resolves a name by module or package, and a file that names something it did not import
/// fails the moment anything loads it. C and C++ resolve a name by whatever text came before,
/// so the includes *are* the dependency graph rather than a description of one - and a header
/// that is missing an include still compiles inside a translation unit that happened to
/// include the right thing first.
///
/// Which is exactly what splitting the output into a header per type made possible, and what
/// building the sources does not check.
/// </summary>
[Collection("conformance-tree")]
public class HeaderIncludeTests
{
    /// <summary>
    /// Both corpora that generate these targets, because they fail differently.
    ///
    /// `conformance` has an enum a table is typed with, a constant set typed with that enum,
    /// and two tables referencing each other's rows - the edges. `reserved-words` has names
    /// taken from the keyword list - the escaping. Neither covers the other: dropping the enum
    /// include is invisible to reserved-words, which declares no enum. That is not
    /// hypothetical; it is how the first version of this gate passed while the generated code
    /// was broken.
    /// </summary>
    public static TheoryData<string> Scenarios => new TheoryData<string> { "conformance", "reserved-words" };

    // ------------------------------------------------------------------- C

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_generated_c_header_compiles_on_its_own(string scenario)
    {
        Assert.True(ConformanceHarness.CIsAvailable(out string why), why);

        Convert(scenario);

        var result = ConformanceHarness.CompileEachCHeaderAlone(scenario);

        Assert.True(result.Succeeded, result.Output);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void No_c_table_header_includes_another(string scenario)
    {
        Convert(scenario);

        AssertNoTableHeaderIncludesAnother(
            Path.Combine(RepoLayout.OutputDir(scenario), "c"), recordMarker: "Record_t {");
    }

    // ----------------------------------------------------------------- C++

    /// <summary>
    /// The same for C++, where it matters more: the target is header only, so there is no
    /// source file whose include order could paper over a missing include.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_generated_cpp_header_compiles_on_its_own(string scenario)
    {
        Assert.True(CppToolchain.IsAvailable(out string why), why);

        Convert(scenario);

        string root = Path.Combine(RepoLayout.OutputDir(scenario), "cpp");

        foreach (var header in Directory.GetFiles(root, "*.h", SearchOption.AllDirectories)
                                        .OrderBy(path => path))
        {
            // The path relative to the output, not the base name: the headers live in
            // `tables/`, `enums/` and `constants/` now, and that is how the umbrella
            // includes them - so it is what a translation unit has to write too.
            string relative = Path.GetRelativePath(root, header).Replace('\\', '/');

            // Compile names the header from that path and includes nothing else.
            var result = CppToolchain.Compile(
                scenario, relative.Substring(0, relative.Length - ".h".Length));

            Assert.True(result.Succeeded,
                $"{Path.GetFileName(header)} does not compile on its own." +
                $"{Environment.NewLine}{result.Output}");
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void No_cpp_table_header_includes_another(string scenario)
    {
        Convert(scenario);

        AssertNoTableHeaderIncludesAnother(
            Path.Combine(RepoLayout.OutputDir(scenario), "cpp"), recordMarker: "Record {");
    }

    // ------------------------------------------------------------- shared

    /// <summary>
    /// That no header declaring a record includes another header that declares one.
    /// </summary>
    /// <remarks>
    /// Two tables referencing each other's rows is legal in the sheets and does happen, so an
    /// include between table headers would be a cycle - and a cycle between include-guarded
    /// headers does not fail loudly. It resolves: whichever is reached first sees an
    /// incomplete version of the other and compiles, or does not, depending on which
    /// translation unit got there first. The generated code would work until somebody included
    /// the headers in the other order.
    ///
    /// A pointer member needs only an incomplete type, so every record is forward declared in
    /// one header that all of them include. This says that is what happened, rather than that
    /// today's corpus happens not to contain the cycle.
    /// </remarks>
    private static void AssertNoTableHeaderIncludesAnother(string root, string recordMarker)
    {
        Assert.True(Directory.Exists(root), $"Nothing was generated at {root}.");

        // Recursive: the table headers live in `tables/` now, and a search that only
        // looked at the top of the output would find none of them - and `Assert.NotEmpty`
        // below is what turns that into a failure rather than a silent pass.
        var headers = Directory.GetFiles(root, "*.h", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetFileName(path), path => Normalize(File.ReadAllText(path)));

        // The umbrella and the forward header declare no record: the first includes everything
        // on purpose, and the second is what makes that safe.
        var tableHeaders = headers
            .Where(header => header.Value.Contains(recordMarker, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(tableHeaders);

        foreach (var header in tableHeaders)
        {
            foreach (var other in tableHeaders)
            {
                if (other.Key == header.Key)
                    continue;

                Assert.DoesNotContain($"#include \"{other.Key}\"", header.Value);
            }
        }
    }

    /// <summary>LF, so a marker spanning a line break matches on either platform.</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static void Convert(string scenario)
    {
        var conversion = TabbitRunner.Convert(scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }
}
