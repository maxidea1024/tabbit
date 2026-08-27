using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Tabbit.Tests;

/// <summary>
/// Compiles a scenario's generated C# on its own.
///
/// The round-trip gate in CsGeneratorTests builds cs-check, which links a Program that
/// reads the `core` fixture's tables by name and so only works for that scenario. This
/// builds cs-compile-check instead: no source of its own, just the generated files, for
/// the scenarios where the question is only whether the output is valid C#.
/// </summary>
internal static class CsToolchain
{
    /// <summary>
    /// Compiles a scenario's generated C# the way a plain .NET consumer would.
    /// </summary>
    /// <summary>
    /// A copy of one of the check projects, inside the work directory that will build it.
    /// </summary>
    /// <remarks>
    /// **Because the project is shared and what it compiles is not.** Every one of these
    /// builds names one `.csproj` with a different `GeneratedDir`, and MSBuild keeps a
    /// project's intermediate output beside the project - so two builds at once write one
    /// another's assembly lists and one reads half of the other's. Serial that gap never
    /// opened; it is what failed the first time the suite ran in parallel.
    ///
    /// A copy rather than a redirected `obj`: the intermediate path is settled at restore
    /// time, so moving it on the build command line leaves the restore output where it was
    /// and the build cannot find it. The projects are a file or two, which is what makes
    /// copying the cheap answer.
    ///
    /// The same move the language harnesses make for the same reason - build in a copy, not
    /// in the tree. doc/roadmap.md, the suite-parallelism entry.
    /// </remarks>
    internal static string ProjectCopy(string workDir, string tool)
        => ProjectCopy(workDir,
                       Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", tool),
                       tool + ".csproj");

    /// <summary>
    /// The same, for a project that is not named after the folder it sits in.
    /// </summary>
    internal static string ProjectCopy(string workDir, string from, string projectFile)
    {
        // **Beside the output directory, not inside it.** These builds pass `-o <workDir>`,
        // and the SDK excludes everything under the output path from the default compile
        // items - a project below it compiles the generated sources and not its own entry
        // point, which is a linker error about a missing `Main` and nothing about the copy.
        string to = workDir + "-project";

        Directory.CreateDirectory(to);

        // The sources and the project file, and nothing a previous build left: `obj` and
        // `bin` are exactly what this exists to stop sharing.
        foreach (string path in Directory.GetFiles(from))
        {
            string name = Path.GetFileName(path);

            if (name.EndsWith(".cs") || name.EndsWith(".csproj"))
                File.Copy(path, Path.Combine(to, name), overwrite: true);
        }

        return Path.Combine(to, projectFile);
    }

    public static ToolResult Compile(string scenario, string accessorName)
        => Compile(scenario, accessorName, unitySymbols: null);

    /// <summary>
    /// Compiles it with a set of Unity's own symbols defined, so the branches Unity
    /// takes are checked rather than assumed.
    /// </summary>
    /// <param name="unitySymbols">
    /// Semicolon separated, as Unity would have them - `UNITY_5_3_OR_NEWER` on its own
    /// for the old API level, plus `UNITY_2021_2_OR_NEWER` for the current one. Null
    /// compiles the plain path.
    /// </param>
    public static ToolResult Compile(string scenario, string accessorName, string unitySymbols)
    {
        string label = unitySymbols == null
            ? "-compile"
            : "-compile-" + unitySymbols.Replace(';', '-').ToLowerInvariant();

        string workDir = RepoLayout.WorkDir("_cscheck", scenario + label);
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        Directory.CreateDirectory(workDir);

        string generatedDir = Path.Combine(RepoLayout.OutputDir(scenario), "csharp");

        if (!File.Exists(Path.Combine(generatedDir, accessorName + ".cs")))
        {
            return new ToolResult
            {
                Succeeded = false,
                Output = $"No generated accessor at {Path.Combine(generatedDir, accessorName + ".cs")}.",
            };
        }

        var arguments = new System.Collections.Generic.List<string>
        {
            "build",
            ProjectCopy(workDir, "cs-compile-check"),
            "--nologo",
            $"-p:GeneratedDir={generatedDir}",
        };

        if (unitySymbols != null)
        {
            // %3B, because MSBuild splits a property value on a literal semicolon and
            // would take the second symbol for another target to build.
            arguments.Add($"-p:UnitySymbols={unitySymbols.Replace(";", "%3B")}");
        }

        arguments.Add("-o");
        arguments.Add(workDir);

        return Execute("dotnet", RepoLayout.Root, arguments.ToArray());
    }

    /// <summary>
    /// Builds one of the read-back harnesses against a scenario's generated C# and runs it on
    /// the binary the exporter wrote, returning what it printed.
    /// </summary>
    /// <remarks>
    /// The question a compile cannot answer: the declaration and the file have to agree about
    /// which column holds which member. They are written by two halves of the tool that no
    /// longer share code, so reading back is what settles it.
    ///
    /// The harness is named rather than derived, because each one names the tables of the
    /// fixture it reads - there is no generic driver, and a generated accessor is not
    /// reflectable enough to make one worth the indirection.
    /// </remarks>
    /// <param name="extraArgs">
    /// Further arguments for the harness, after the directory. For a harness whose subject
    /// is something the load path is given rather than something it reads - a key.
    /// </param>
    public static ToolResult ReadBack(string scenario, string harness, params string[] extraArgs)
    {
        string workDir = RepoLayout.WorkDir("_cscheck", scenario + "-readback");
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        Directory.CreateDirectory(workDir);

        string generatedDir = Path.Combine(RepoLayout.OutputDir(scenario), "csharp");

        var build = Execute("dotnet", RepoLayout.Root,
            "build",
            ProjectCopy(workDir, harness),
            "--nologo",
            $"-p:GeneratedDir={generatedDir}",
            "-o", workDir);

        if (!build.Succeeded)
            return build;

        bool onWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        var arguments = new System.Collections.Generic.List<string>
        {
            Path.Combine(RepoLayout.OutputDir(scenario), "binary"),
        };

        arguments.AddRange(extraArgs ?? Array.Empty<string>());

        return Execute(Path.Combine(workDir, onWindows ? harness + ".exe" : harness),
                       workDir,
                       arguments.ToArray());
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

        foreach (var arg in args)
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
