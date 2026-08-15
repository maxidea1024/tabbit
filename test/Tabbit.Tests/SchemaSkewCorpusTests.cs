using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Every language's reader, built against one schema, reading a file written by a later one.
/// </summary>
/// <remarks>
/// The binary format's promise is that a column added since a reader was generated is
/// skipped rather than misread - a client that has not shipped yet keeps working when the
/// data does. Until now that promise was **tested in C# alone**. The other twelve had the
/// conformance corpus, which proves they read values correctly, and proves nothing about
/// what they do with a column they have never heard of.
///
/// So the corpus's own drivers are pointed at a second generation of its data. The
/// `conformance-skew` scenario is the same table with one column appended and exported as
/// binary only; the readers are the ones `conformance` generated. **The right answer is that
/// nothing changes** - every value the driver prints must be exactly what it prints from its
/// own generation's file, because the column it does not know is not its business.
///
/// Appended rather than inserted, because the columns spell no `@N` and take their tags from
/// their positions. That is the shape a schema change has in a sheet nobody has tagged, and
/// it is the shape that has to keep working.
///
/// **So the unknown column is the last one in the file.** A reader that gave up at the first
/// tag it did not know would pass this and fail on a sheet that spells its tags out. The
/// generated loops walk the header and skip by byte length rather than stopping, so position
/// should not matter - but this gate does not prove that, and saying so is cheaper than
/// letting the next person assume it did.
///
/// doc/binary-format.md has what a tag guarantees and what it does not.
/// </remarks>
public class SchemaSkewCorpusTests
{
    /// <summary>Where the readers come from.</summary>
    private const string Readers = "conformance";

    /// <summary>Where the data comes from - the same table, one column later.</summary>
    private const string Data = "conformance-skew";

    /// <summary>
    /// C# is checked here as well as in <see cref="SchemaEvolutionTests"/>.
    /// </summary>
    /// <remarks>
    /// That one asks the deeper questions - deletions, renames, promotions, refusals - of a
    /// fixture built for them. This asks the one question of all thirteen at once, and
    /// leaving C# out of the row would make the comparison uneven for no reason.
    /// </remarks>
    [Fact]
    public void Csharp_skips_a_column_added_after_it_was_generated()
    {
        ConvertTheLaterGeneration();
        Check("C#", ConformanceHarness.RunCsharp(Readers, Data));
    }

    [Fact]
    public void Cpp_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++17 compiler is required to check the generated C++. {why}");

        ConvertTheLaterGeneration();
        Check("C++", ConformanceHarness.RunCpp(Readers, Data));
    }

    [Fact]
    public void Typescript_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node is required to check the generated TypeScript. {why}");

        ConvertTheLaterGeneration();
        Check("TypeScript", ConformanceHarness.RunTypescript(Readers, Data));
    }

    [Fact]
    public void Go_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"The Go toolchain is required to check the generated Go. {why}");

        ConvertTheLaterGeneration();
        Check("Go", ConformanceHarness.RunGo(Readers, Data));
    }

    [Fact]
    public void Rust_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.RustIsAvailable(out string why),
            $"The Rust toolchain is required to check the generated Rust. {why}");

        ConvertTheLaterGeneration();
        Check("Rust", ConformanceHarness.RunRust(Readers, Data));
    }

    [Fact]
    public void Python_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"Python is required to check the generated Python. {why}");

        ConvertTheLaterGeneration();
        Check("Python", ConformanceHarness.RunPython(Readers, Data));
    }

    [Fact]
    public void Java_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.JavaIsAvailable(out string why),
            $"A JDK is required to check the generated Java. {why}");

        ConvertTheLaterGeneration();
        Check("Java", ConformanceHarness.RunJava(Readers, Data));
    }

    [Fact]
    public void Kotlin_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.KotlinIsAvailable(out string why),
            $"The Kotlin compiler is required to check the generated Kotlin. {why}");

        ConvertTheLaterGeneration();
        Check("Kotlin", ConformanceHarness.RunKotlin(Readers, Data));
    }

    [Fact]
    public void Ruby_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.RubyIsAvailable(out string why),
            $"Ruby is required to check the generated Ruby. {why}");

        ConvertTheLaterGeneration();
        Check("Ruby", ConformanceHarness.RunRuby(Readers, Data));
    }

    [Fact]
    public void Dart_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.DartIsAvailable(out string why),
            $"The Dart SDK is required to check the generated Dart. {why}");

        ConvertTheLaterGeneration();
        Check("Dart", ConformanceHarness.RunDart(Readers, Data));
    }

    [Fact]
    public void C_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.CIsAvailable(out string why),
            $"A C compiler is required to check the generated C. {why}");

        ConvertTheLaterGeneration();
        Check("C", ConformanceHarness.RunC(Readers, Data));
    }

    [Fact]
    public void Php_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"PHP is required to check the generated PHP. {why}");

        ConvertTheLaterGeneration();
        Check("PHP", ConformanceHarness.RunPhp(Readers, Data));
    }

    [Fact]
    public void Unreal_skips_a_column_added_after_it_was_generated()
    {
        Assert.True(ConformanceHarness.UnrealOffEngineIsAvailable(out string why), why);

        ConvertTheLaterGeneration();
        Check("Unreal", ConformanceHarness.RunUnreal(Readers, Data));
    }

    /// <summary>
    /// Writes the later generation's data. The readers' own scenario is converted by
    /// <see cref="ConformanceTests.Expected"/>, which every check calls anyway.
    /// </summary>
    private static void ConvertTheLaterGeneration()
    {
        var conversion = TabbitRunner.Convert(Data);
        Assert.True(conversion.Succeeded,
            $"Converting `{Data}` failed.{Environment.NewLine}{conversion.Describe()}");
    }

    /// <summary>
    /// The reader came back with the corpus, unchanged by the column it does not know.
    /// </summary>
    private static void Check(string language, ToolResult harness)
    {
        Assert.True(harness.Succeeded,
            $"{language} harness failed reading a file one schema newer than itself."
            + $"{Environment.NewLine}{harness.Output}");

        ConformanceTests.Compare(
            language, ConformanceTests.Expected(), ConformanceTests.Parse(harness.StdOut));
    }
}
