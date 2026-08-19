using System;
using System.IO;

using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That every language's reader actually checks the MAC, asked the only way that cannot
/// pass by accident: give it a file that was altered after it was signed.
/// </summary>
/// <remarks>
/// The corpus is exported signed, so `ConformanceTests` already has all the readers
/// verifying a real tag on every run - but a reader that skipped the check entirely would
/// pass that just as well, and so would one whose harness forgot to set the key. Only a file
/// that must be refused tells those apart.
///
/// The edit is four bytes of a value block, which is what changing a number looks like.
/// Nothing about the file's shape changes: the block lengths still add up, the run lengths
/// still cover the rows, and the dictionary indices are still in range. Before the MAC this
/// was a file that loaded and gave different answers - `CsEncryptedReadTests` asserts that
/// half directly, and it is the reason this gate exists.
///
/// One test per language rather than a theory over an enumeration, because each needs its
/// own toolchain check and its own skip message, and because a theory that silently loses a
/// language is the failure this whole corpus exists to prevent.
/// </remarks>
public class ConformanceMacTests
{
    private const string Scenario = "conformance";

    /// <summary>
    /// The altered corpus, exported beside the good one under a scenario name of its own.
    /// </summary>
    /// <remarks>
    /// A directory rather than an in-place edit, so the harnesses that read the real corpus
    /// are unaffected and can run in parallel with these. The runners take the data
    /// directory as a scenario name, which is what makes this cost nothing in the thirteen
    /// runners themselves.
    /// </remarks>
    private const string Tampered = "conformance-tampered";

    [Fact]
    public void Csharp_refuses_an_altered_file() => Refuses(
        "C#", () => ConformanceHarness.RunCsharp(Scenario, Tampered));

    [Fact]
    public void Cpp_refuses_an_altered_file()
    {
        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++17 compiler is required to check the generated C++. {why}");

        Refuses("C++", () => ConformanceHarness.RunCpp(Scenario, Tampered));
    }

    [Fact]
    public void Unreal_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.UnrealOffEngineIsAvailable(out string why), why);

        Refuses("Unreal", () => ConformanceHarness.RunUnreal(Scenario, Tampered));
    }

    [Fact]
    public void Typescript_refuses_an_altered_file()
    {
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript harness. {why}");

        Refuses("TypeScript", () => ConformanceHarness.RunTypescript(Scenario, Tampered));
    }

    [Fact]
    public void Go_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"A Go toolchain is required to check the generated Go. {why}");

        Refuses("Go", () => ConformanceHarness.RunGo(Scenario, Tampered));
    }

    [Fact]
    public void Rust_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.RustIsAvailable(out string why),
            $"A Rust toolchain is required to check the generated Rust. {why}");

        Refuses("Rust", () => ConformanceHarness.RunRust(Scenario, Tampered));
    }

    [Fact]
    public void Python_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"A Python interpreter is required to check the generated Python. {why}");

        Refuses("Python", () => ConformanceHarness.RunPython(Scenario, Tampered));
    }

    [Fact]
    public void Java_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.JavaIsAvailable(out string why),
            $"A JDK is required to check the generated Java. {why}");

        Refuses("Java", () => ConformanceHarness.RunJava(Scenario, Tampered));
    }

    [Fact]
    public void Kotlin_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.KotlinIsAvailable(out string why),
            $"A Kotlin compiler is required to check the generated Kotlin. {why}");

        Refuses("Kotlin", () => ConformanceHarness.RunKotlin(Scenario, Tampered));
    }

    [Fact]
    public void Swift_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.SwiftIsAvailable(out string why),
            $"A Swift toolchain is required to check the generated Swift. {why}");

        Refuses("Swift", () => ConformanceHarness.RunSwift(Scenario, Tampered));
    }

    [Fact]
    public void Lua_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.LuaIsAvailable(out string why),
            $"A C toolchain is required to build the Lua host. {why}");

        Refuses("Lua", () => ConformanceHarness.RunLua(Scenario, Tampered));
    }

    [Fact]
    public void Ruby_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.RubyIsAvailable(out string why),
            $"A Ruby interpreter is required to check the generated Ruby. {why}");

        Refuses("Ruby", () => ConformanceHarness.RunRuby(Scenario, Tampered));
    }

    [Fact]
    public void Php_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"A PHP interpreter is required to check the generated PHP. {why}");

        Refuses("PHP", () => ConformanceHarness.RunPhp(Scenario, Tampered));
    }

    [Fact]
    public void Dart_refuses_an_altered_file()
    {
        Assert.True(ConformanceHarness.DartIsAvailable(out string why),
            $"A Dart SDK is required to check the generated Dart. {why}");

        Refuses("Dart", () => ConformanceHarness.RunDart(Scenario, Tampered));
    }

    [Fact]
    public void C_refuses_an_altered_file()
    {
        Assert.True(CToolchain.IsAvailable(out string why),
            $"A C99 compiler is required to check the generated C. {why}");

        Refuses("C", () => ConformanceHarness.RunC(Scenario, Tampered));
    }

    /// <summary>
    /// The harness reads the altered corpus and fails, saying it was the MAC.
    /// </summary>
    /// <remarks>
    /// The message is asserted, not only the exit code. A harness that failed for any other
    /// reason - a missing file, a toolchain that could not build - would satisfy "did not
    /// succeed" while proving nothing about the check this is here for.
    /// </remarks>
    private static void Refuses(string language, Func<ToolResult> run)
    {
        Alter();

        var harness = run();

        Assert.False(harness.Succeeded,
            $"The {language} reader loaded a file that was altered after it was signed."
            + $"{Environment.NewLine}{harness.Output}");

        Assert.Contains("MAC", harness.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Exports the corpus, copies it, and changes four bytes of one table's data.
    /// </summary>
    /// <remarks>
    /// Well past the header so that what moves is a value rather than a field the reader
    /// checks on its own. The edit has to be inside a block the exporter wrote, which the
    /// last bytes of the file always are - the blocks are everything after the descriptors.
    /// </remarks>
    private static void Alter()
    {
        // The signed corpus, as ConformanceTests exports it. Converting here as well is what
        // lets this class run on its own.
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string from = Path.Combine(RepoLayout.OutputDir(Scenario), "binary");
        string to = Path.Combine(RepoLayout.OutputDir(Tampered), "binary");

        Directory.CreateDirectory(to);

        foreach (string source in Directory.GetFiles(from))
            File.Copy(source, Path.Combine(to, Path.GetFileName(source)), overwrite: true);

        string table = Path.Combine(to, "Vectors.tcb");
        var bytes = File.ReadAllBytes(table);

        Assert.True(bytes.Length > 64, "The corpus table is too small to alter meaningfully.");

        // That the file is signed at all. Without this the whole class would pass on an
        // export that had quietly stopped carrying a MAC.
        Assert.Contains(bytes[22..38], value => value != 0);

        for (int at = 0; at < 4; at++)
            bytes[bytes.Length - 8 + at] ^= 0xFF;

        File.WriteAllBytes(table, bytes);
    }
}
