using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The generated C# for a `set` and a `map`: that it is a program, and that it reads back.
/// </summary>
/// <remarks>
/// **Two gates, because either alone passes against the wrong thing.** A compile says the
/// page is legal C# and nothing about what it reads; the exported JSON says what the arrays
/// hold and nothing about the lookups, which are the half of the surface that a plain array
/// would not have given. So the driver reads the binary through the generated reader and
/// asks the lookups questions.
///
/// spec/types/set-and-map.md sections 7 and 9. This is the shape every other language's gate
/// takes as it opts in.
/// </remarks>
public class CsContainerTests
{
    private const string Scenario = "containers-target";

    /// <summary>
    /// A record with a set, a map of scalars and a map of structs is legal C#.
    /// </summary>
    [Fact]
    public void The_generated_containers_compile()
    {
        Convert();

        var result = CsToolchain.Compile(Scenario, "ContainersAccessor");

        Assert.True(result.Succeeded,
            $"The generated C# for `{Scenario}` does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// And reads the binary back into both layers of the surface.
    /// </summary>
    /// <remarks>
    /// The arrays are in the file's order, which is the sheet's - nothing sorts them, so a
    /// row's second tag is the second one somebody typed. The lookups answer about the keys
    /// that row holds and not about the ones it does not, which is what says the dictionary
    /// was built from this row's column rather than from another's or from nothing.
    /// </remarks>
    [Fact]
    public void The_generated_reader_fills_both_layers()
    {
        var rows = RunGeneratedReader();

        var first = rows[0];

        Assert.Equal(1, first.GetProperty("index").GetInt32());
        Assert.Equal("[\"new\",\"sale\"]", Compact(first.GetProperty("tags")));

        // The set answers about what this row holds, and not about what it does not.
        Assert.True(first.GetProperty("hasSale").GetBoolean());
        Assert.False(first.GetProperty("hasGone").GetBoolean());

        Assert.Equal("[10,11]", Compact(first.GetProperty("priceKeys")));
        Assert.Equal("[100,120]", Compact(first.GetProperty("priceValues")));
        Assert.Equal(2, first.GetProperty("priceCount").GetInt32());

        // A map of scalars answers with the value.
        Assert.Equal(120, first.GetProperty("priceOf11").GetInt32());

        // A map of structs answers with the entry's position, and the members are read at it.
        Assert.Equal(102, first.GetProperty("dropItemAt2").GetInt32());
        Assert.Equal(3, first.GetProperty("dropCountAt2").GetInt32());
    }

    /// <summary>
    /// An empty cell is a container of no entries, and its lookups answer nothing rather
    /// than throwing.
    /// </summary>
    /// <remarks>
    /// The row that holds nothing is where a reader that allocated on a length it never read
    /// shows up - and where a lookup built from a null array would.
    /// </remarks>
    [Fact]
    public void A_row_with_no_entries_reads_as_empty()
    {
        var rows = RunGeneratedReader();
        var third = rows[2];

        Assert.Equal("[]", Compact(third.GetProperty("tags")));
        Assert.False(third.GetProperty("hasSale").GetBoolean());
        Assert.Equal(0, third.GetProperty("priceCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, third.GetProperty("priceOf11").ValueKind);
        Assert.Equal(JsonValueKind.Null, third.GetProperty("dropItemAt2").ValueKind);
    }

    /// <summary>
    /// And the values agree with what the JSON exporter wrote for the same workbook.
    /// </summary>
    /// <remarks>
    /// Two independent paths to the same numbers: the exporter reads the model and the
    /// generated reader reads the file. A writer and a reader that were wrong in the same
    /// way would agree, and these two do not share the code that would let them be.
    /// </remarks>
    [Fact]
    public void What_the_reader_holds_is_what_the_exporter_wrote()
    {
        var rows = RunGeneratedReader();

        string exported = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "json-named", "Shop.json"));

        using var document = JsonDocument.Parse(exported);

        for (int at = 0; at < document.RootElement.GetArrayLength(); at++)
        {
            var bag = document.RootElement[at].GetProperty("bag");

            Assert.Equal(
                Compact(bag.GetProperty("tags")),
                Compact(rows[at].GetProperty("tags")));

            Assert.Equal(
                Compact(bag.GetProperty("prices").GetProperty("key")),
                Compact(rows[at].GetProperty("priceKeys")));

            Assert.Equal(
                Compact(bag.GetProperty("prices").GetProperty("value")),
                Compact(rows[at].GetProperty("priceValues")));

            Assert.Equal(
                Compact(bag.GetProperty("drops").GetProperty("key")),
                Compact(rows[at].GetProperty("dropKeys")));
        }
    }

    private static string Compact(JsonElement element)
        => element.GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", "");

    private static void Convert(string scenario = Scenario)
    {
        var conversion = TabbitRunner.Convert(scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }

    /// <summary>
    /// Compiles the generated code with the driver beside it and runs it over the binary.
    /// </summary>
    private static JsonElement RunGeneratedReader()
    {
        Convert();

        string workDir = RepoLayout.WorkDir("_cscheck", Scenario + "-read");
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        Directory.CreateDirectory(workDir);

        string generatedDir = Path.Combine(RepoLayout.OutputDir(Scenario), "csharp");

        var build = Execute("dotnet", RepoLayout.Root,
            "build",
            CsToolchain.ProjectCopy(workDir, "cs-containers-check"),
            "--nologo",
            $"-p:GeneratedDir={generatedDir}",
            "-o", workDir);

        Assert.True(build.Succeeded,
            $"The generated C# and its driver did not compile.{Environment.NewLine}{build.Output}");

        var run = Execute(
            Path.Combine(workDir, OperatingSystem.IsWindows()
                ? "cs-containers-check.exe"
                : "cs-containers-check"),
            workDir,
            Path.Combine(RepoLayout.OutputDir(Scenario), "binary"));

        Assert.True(run.Succeeded,
            $"The generated C# did not read the binary.{Environment.NewLine}{run.Output}");

        return JsonDocument.Parse(run.StdOut).RootElement.Clone();
    }

    private static (bool Succeeded, string Output, string StdOut) Execute(
        string program, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)!;

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return (process.ExitCode == 0, stdout + stderr, stdout);
    }
}
