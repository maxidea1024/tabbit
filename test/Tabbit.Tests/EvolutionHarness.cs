using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Tabbit.Tests;

/// <summary>
/// Builds the skew harness against one generation's generated code and runs it against
/// a directory of data written by either.
///
/// Separate from ConformanceHarness because the question is different: that one builds
/// every language against the schema that wrote the data, this one builds one
/// language against a schema that did not.
/// </summary>
internal static class EvolutionHarness
{
    private static bool OnWindows => OperatingSystem.IsWindows();

    public static ToolResult RunCsharp(string generation, string dataDir, string table)
        => Run(generation, dataDir, table);

    /// <summary>
    /// The same build, reading a table it has already loaded - a refresh.
    /// </summary>
    public static ToolResult RefreshCsharp(
        string generation, string firstDataDir, string secondDataDir, string table)
        => Run(generation, "--refresh", firstDataDir, secondDataDir, table);

    private static ToolResult Run(string generation, params string[] arguments)
    {
        string workDir = Path.Combine(RepoLayout.OutputDir("_evolution"), generation + "-csharp");

        var build = Execute("dotnet", RepoLayout.Root,
            "build",
            Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "evolution", "csharp",
                "evolution-csharp.csproj"),
            "--nologo",
            $"-p:GeneratedDir={Path.Combine(RepoLayout.OutputDir(generation), "csharp")}",
            "-o", workDir);

        if (!build.Succeeded)
            return build;

        return Execute(
            Path.Combine(workDir, OnWindows ? "evolution-csharp.exe" : "evolution-csharp"),
            workDir, arguments);
    }

    private static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        var stdout = new StringBuilder();
        var combined = new StringBuilder();

        using var process = new Process { StartInfo = psi };

        // Two streams, two threads, one StringBuilder. Locked because it is not safe
        // for that, and the failure is not a garbled line - it is an exception from
        // inside AppendLine, on a thread pool worker where nothing catches it.
        var writing = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            lock (writing)
            {
                stdout.AppendLine(e.Data);
                combined.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            lock (writing)
                combined.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(milliseconds: 300_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"`{fileName}` did not finish within 5 minutes.");
        }

        process.WaitForExit();

        return new ToolResult
        {
            Succeeded = process.ExitCode == 0,
            StdOut = stdout.ToString(),
            Output = combined.ToString(),
        };
    }
}
