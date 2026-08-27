using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Builds and runs one language's container driver, and hands back what it read.
/// </summary>
/// <remarks>
/// **One harness rather than one per language, because the question is the same one.** Every
/// driver reads the same binary and prints the same object: the arrays in the file's order,
/// and what the lookups answer about a key the row holds and one it does not. What differs
/// is the toolchain, which is all that is here.
///
/// The drivers live under `test/fixtures/tools/&lt;lang&gt;-check-containers/`, and each is copied
/// into the generated output rather than importing across directories - the import paths a
/// consumer would write are relative to that output.
///
/// spec/types/set-and-map.md sections 7 and 9.
/// </remarks>
internal static class ContainerHarness
{
    public const string Scenario = "containers-target";

    private static string Generated(string language)
        => Path.Combine(RepoLayout.OutputDir(Scenario), language);

    private static string BinaryDir()
        => Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

    private static string Driver(string language, string file)
        => Path.Combine(RepoLayout.Root, "test", "fixtures", "tools",
                        language + "-check-containers", file);

    /// <summary>Converts the fixture, which every driver reads the output of.</summary>
    public static void Convert()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }

    /// <summary>
    /// What one language's driver read, as the object it printed.
    /// </summary>
    public static JsonElement Run(string language)
    {
        Convert();

        var result = language switch
        {
            "go" => RunGo(),
            "java" => RunJava(),
            "kotlin" => RunKotlin(),
            "dart" => RunDart(),
            "swift" => RunSwift(),
            "rust" => RunRust(),
            "python" => RunPython(),
            "ruby" => RunRuby(),
            "php" => RunPhp(),
            "cpp" => RunCpp(),
            "c" => RunC(),
            "lua" => RunLua(),
            _ => throw new ArgumentException($"No container driver for `{language}`."),
        };

        Assert.True(result.Succeeded,
            $"The generated {language} did not read the binary.{Environment.NewLine}{result.Output}");

        return JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement.Clone();
    }

    /// <summary>
    /// The last line that is an object, so a toolchain's own chatter does not have to be
    /// silenced to read the answer.
    /// </summary>
    private static string LastJsonLine(string stdout)
        => stdout.Split('\n')
            .Select(line => line.Trim())
            .Last(line => line.StartsWith('{'));

    // ------------------------------------------------------------------- go

    private static ToolResult RunGo()
    {
        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"Go toolchain required. {why}");

        string moduleDir = Generated("go");
        string harnessDir = Path.Combine(moduleDir, "harness");

        Directory.CreateDirectory(harnessDir);
        File.Copy(Driver("go", "main.go"), Path.Combine(harnessDir, "main.go"), overwrite: true);

        return ConformanceHarness.Execute("go", moduleDir, "run", "./harness", BinaryDir());
    }

    // ----------------------------------------------------------------- java

    private static ToolResult RunJava()
    {
        Assert.True(ConformanceHarness.JavaIsAvailable(out string why),
            $"Java toolchain required. {why}");

        string root = Generated("java");
        string classes = Path.Combine(root, "classes");

        File.Copy(Driver("java", "Harness.java"),
                  Path.Combine(root, "Harness.java"), overwrite: true);

        Directory.CreateDirectory(classes);

        var arguments = new List<string> { "-encoding", "UTF-8", "-d", classes };
        arguments.AddRange(Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories));

        var build = ConformanceHarness.Execute("javac", root, arguments.ToArray());

        if (!build.Succeeded)
            return build;

        return ConformanceHarness.Execute("java", root, "-cp", classes, "Harness", BinaryDir());
    }

    // --------------------------------------------------------------- kotlin

    private static ToolResult RunKotlin()
    {
        Assert.True(ConformanceHarness.KotlinIsAvailable(out string why),
            $"Kotlin toolchain required. {why}");

        string root = Generated("kotlin");
        string jar = Path.Combine(root, "harness.jar");

        File.Copy(Driver("kotlin", "Harness.kt"),
                  Path.Combine(root, "Harness.kt"), overwrite: true);

        // Through the compiler jar rather than the `kotlinc` launcher, which on Windows is a
        // batch file and cannot be started as a process at all - the conformance harness
        // says the same.
        var arguments = new List<string>
        {
            "-jar", ConformanceHarness.KotlinCompiler(),
            "-nowarn",
            "-include-runtime",
            "-d", jar,
        };

        arguments.AddRange(Directory.EnumerateFiles(root, "*.kt", SearchOption.AllDirectories));

        var build = ConformanceHarness.Execute("java", root, arguments.ToArray());

        if (!build.Succeeded)
            return build;

        return ConformanceHarness.Execute("java", root, "-jar", jar, BinaryDir());
    }

    // ----------------------------------------------------------------- dart

    private static ToolResult RunDart()
    {
        Assert.True(ConformanceHarness.DartIsAvailable(out string why),
            $"Dart toolchain required. {why}");

        // Beside the generated library, whose import of the reader is relative.
        string root = Generated("dart");

        File.Copy(Driver("dart", "harness.dart"),
                  Path.Combine(root, "harness.dart"), overwrite: true);

        return ConformanceHarness.RunDartScript(root, "harness.dart", BinaryDir());
    }

    // ---------------------------------------------------------------- swift

    private static ToolResult RunSwift()
    {
        Assert.True(ConformanceHarness.SwiftIsAvailable(out string why),
            $"Swift toolchain required. {why}");

        string root = Generated("swift");

        // The entry point has to be in a file called `main.swift`; Swift allows top-level
        // statements nowhere else.
        File.Copy(Driver("swift", "main.swift"),
                  Path.Combine(root, "main.swift"), overwrite: true);

        var sources = Directory
            .EnumerateFiles(root, "*.swift", SearchOption.AllDirectories)
            .ToArray();

        var build = ConformanceHarness.CompileSwiftProgram(
            root, "containers-check", sources);

        if (!build.Succeeded)
            return build;

        return ConformanceHarness.RunSwiftProgram(root, "containers-check", BinaryDir());
    }

    // ----------------------------------------------------------------- rust

    private static ToolResult RunRust()
    {
        Assert.True(ConformanceHarness.RustIsAvailable(out string why),
            $"Rust toolchain required. {why}");

        // A binary inside the generated crate, for the same reason the Go driver is a
        // package inside the generated module: that is the only place the generated types
        // are importable from.
        string crateDir = Generated("rust");
        string binDir = Path.Combine(crateDir, "src", "bin");

        Directory.CreateDirectory(binDir);

        File.Copy(Driver("rust", "harness.rs"),
                  Path.Combine(binDir, "harness.rs"), overwrite: true);

        return ConformanceHarness.Execute(
            "cargo", crateDir, "run", "--quiet", "--bin", "harness", "--", BinaryDir());
    }

    // --------------------------------------------------------------- python

    private static ToolResult RunPython()
    {
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"Python interpreter required. {why}");

        // Beside the generated package rather than inside it, so the package's own
        // directory holds only generated files and the import reads as a consumer's would.
        string root = Generated("python");

        File.Copy(Driver("python", "harness.py"),
                  Path.Combine(root, "harness.py"), overwrite: true);

        return ConformanceHarness.RunPythonHere(root, "harness.py", BinaryDir());
    }

    // ----------------------------------------------------------------- ruby

    private static ToolResult RunRuby()
    {
        Assert.True(ConformanceHarness.RubyIsAvailable(out string why),
            $"Ruby interpreter required. {why}");

        // Beside the generated file, because `require_relative` resolves against the
        // requiring file and that is the import a consumer would write.
        string root = Generated("ruby");

        File.Copy(Driver("ruby", "harness.rb"),
                  Path.Combine(root, "harness.rb"), overwrite: true);

        return ConformanceHarness.RunRubyHere(root, "harness.rb", BinaryDir());
    }

    // ------------------------------------------------------------------ php

    private static ToolResult RunPhp()
    {
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"PHP interpreter required. {why}");

        // Beside the generated files, whose `require_once` resolves against the requiring
        // file - which is the import a consumer would write.
        string root = Generated("php");

        File.Copy(Driver("php", "harness.php"),
                  Path.Combine(root, "harness.php"), overwrite: true);

        return ConformanceHarness.RunPhpScript(root, "harness.php", BinaryDir());
    }

    // ------------------------------------------------------------------ c++

    private static ToolResult RunCpp()
    {
        Assert.True(CppToolchain.IsAvailable(out string why),
            $"C++ toolchain required. {why}");

        string includeDir = Generated("cpp");
        string workDir = Path.Combine(RepoLayout.OutputDir("_cppcheck"), "containers");

        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        Directory.CreateDirectory(workDir);

        var build = CppToolchain.CompileHarness(
            workDir, includeDir, Driver("cpp", "main.cpp"), "Tables", "containers-check");

        if (!build.Succeeded)
            return build;

        return CppToolchain.RunHarness(workDir, "containers-check", BinaryDir());
    }

    // -------------------------------------------------------------------- c

    private static ToolResult RunC()
    {
        Assert.True(CToolchain.IsAvailable(out string why),
            $"C toolchain required. {why}");

        string generated = Generated("c");
        string workDir = Path.Combine(RepoLayout.OutputDir("_ccheck"), "containers");

        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        // Every generated .c, not a named one: the target writes a source per table and one
        // for the reader, and a list of names here would quietly stop covering them.
        var build = CToolchain.CompileHarness(
            workDir,
            includeDir: generated,
            source: Driver("c", "main.c"),
            accessorHeader: "ContainersData.h",
            sources: Directory.GetFiles(generated, "*.c", SearchOption.AllDirectories)
                              .OrderBy(path => path).ToArray(),
            exeName: "containers-check");

        if (!build.Succeeded)
            return build;

        return CToolchain.RunHarness(workDir, "containers-check", BinaryDir());
    }

    // ------------------------------------------------------------------ lua

    private static ToolResult RunLua()
    {
        Assert.True(ConformanceHarness.LuaIsAvailable(out string why),
            $"Lua host required. {why}");

        // From the generated output directory, so `require("tables")` resolves through the
        // default package.path; the driver itself stays where it is.
        return ConformanceHarness.Execute(
            LuaToolchain.HostExecutable, Generated("lua"),
            Driver("lua", "harness.lua"), BinaryDir());
    }
}
