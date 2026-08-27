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
}
