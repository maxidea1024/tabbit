using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tabbit.Tests;

internal sealed class RunResult
{
    public int ExitCode;
    public string StdOut;
    public string StdErr;

    public bool Succeeded => ExitCode == 0;

    public string Describe()
        => $"exit code {ExitCode}{Environment.NewLine}--- stdout ---{Environment.NewLine}{StdOut}{Environment.NewLine}--- stderr ---{Environment.NewLine}{StdErr}";

    /// <summary>
    /// Standard output as lines, with the log gutter taken off each one.
    /// </summary>
    /// <remarks>
    /// Every console line opens with a level and a category - `[F] [Cooking   ] `. That is
    /// for a person watching the run, and a test looking at the shape of what a message
    /// says should not have to know about it: without this, a line indented under its
    /// message no longer starts with what it used to start with.
    ///
    /// Only the gutter comes off. What follows, indentation included, is untouched.
    /// </remarks>
    public string[] MessageLines()
        => StdOut.Split('\n')
                 .Select(line => LogGutter.Replace(line.TrimEnd('\r'), "", 1))
                 .ToArray();

    private static readonly Regex LogGutter = new(@"^\[[A-Z]\] \[[A-Za-z]+ *\] ", RegexOptions.Compiled);
}

/// <summary>
/// Drives the Tabbit CLI as a subprocess.
///
/// Running out of process rather than calling into the code directly is
/// deliberate: Tabbit keeps conversion state in statics (Model.Current,
/// RecipeModel.Current, StagingFiles), so two in-process conversions in the same
/// test run would contaminate each other. A subprocess also exercises the real
/// entry point, including argument parsing and exit codes.
/// </summary>
internal static class TabbitRunner
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <summary>
    /// The CLI as an executable, built once for the whole test run.
    /// </summary>
    /// <remarks>
    /// Every conversion used to go through `dotnet run --project`, which evaluates the
    /// project and checks the build before it starts the program - two to four seconds
    /// each, on a suite that converts more than thirty times. That was most of a minute of
    /// MSBuild doing nothing.
    ///
    /// Built once into a directory of its own instead, and then invoked directly. The
    /// subprocess is still a subprocess, which is the point of running it this way at all:
    /// Tabbit keeps conversion state in statics, so two in-process conversions in one
    /// test run would contaminate each other.
    ///
    /// Lazy rather than a fixture, because xunit runs collections in parallel and a
    /// fixture would have to be depended on by every class that converts - including the
    /// ones whose only interest in the CLI is that it exists.
    /// </remarks>
    private static readonly Lazy<string> Executable = new Lazy<string>(Build);

    /// <summary>
    /// The same executable, for a test that starts the CLI itself rather than converting.
    /// </summary>
    /// <remarks>
    /// The history server is started as a long-running process rather than run to completion, so
    /// it cannot go through <see cref="Convert"/> - but it must go through the same binary. A test
    /// that reached for `dotnet run` instead would build the project again, and alternating
    /// between that build and this one invalidates both: each writes to an output path the other
    /// did not use, so every switch is a full rebuild.
    /// </remarks>
    public static string CliExecutable => Executable.Value;

    private static string Build()
    {
        string outputDir = RepoLayout.OutputDir("_cli");

        var built = Run(null, "dotnet",
            "build", RepoLayout.CliProject, "--nologo", "-v", "quiet", "-o", outputDir);

        if (!built.Succeeded)
            throw new InvalidOperationException($"Could not build the CLI.{Environment.NewLine}{built.Describe()}");

        string path = Path.Combine(outputDir, OnWindows ? "tabbit.exe" : "tabbit");

        if (!File.Exists(path))
            throw new InvalidOperationException($"The CLI build produced no executable at {path}.");

        return path;
    }

    /// <param name="extraArgs">
    /// Further command line arguments, for the options whose whole purpose is to change
    /// what a run produces from an unchanged recipe.
    /// </param>
    /// <summary>
    /// Converts a scenario, or hands back the answer from the first time this run did.
    /// </summary>
    /// <remarks>
    /// **The suite converts 73 scenarios from 214 call sites**, because a class asks for one
    /// per `[Fact]` and several classes ask for the same one. The other 141 runs recompute an
    /// answer that cannot have changed: the workbooks, the recipe and the tool are the same
    /// file on disk for the length of a test run, and the conversion is deterministic - which
    /// the golden tree is the standing proof of.
    ///
    /// So the plain form is shared. A call that names environment variables or extra
    /// arguments is asking for a particular run and gets one - and takes the shared answer
    /// away with it, because such a run leaves a different tree behind than the one the
    /// shared answer described.
    ///
    /// <see cref="ConvertFresh"/> is for the caller that needs the output tree itself to be
    /// untouched, rather than just the result.
    /// </remarks>
    public static RunResult Convert(string scenario,
                                    IReadOnlyDictionary<string, string> environment = null,
                                    params string[] extraArgs)
    {
        bool plain = environment is null && (extraArgs is null || extraArgs.Length == 0);

        if (plain)
        {
            return Shared.GetOrAdd(scenario, key => new Lazy<RunResult>(
                () => ConvertOnce(key, null, Array.Empty<string>()),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        var particular = ConvertOnce(scenario, environment, extraArgs);

        // This run wrote a tree the shared answer no longer describes, so the next plain
        // caller converts again rather than being told about output that is not there.
        Shared.TryRemove(scenario, out _);

        return particular;
    }

    /// <summary>
    /// Converts a scenario with its output tree rebuilt from nothing, whatever this run has
    /// already done.
    /// </summary>
    /// <remarks>
    /// **For a test that walks the output tree and judges every file in it**, rather than
    /// opening the ones it named. The conformance harness builds Go, Rust, Java, Kotlin,
    /// Dart and Python **inside** the directory each was generated into, so a tree that has
    /// been compiled in holds `go.sum`, a Dart package config, a Cargo target directory -
    /// files a walker will find and take for output. Converting fresh is what says "nothing
    /// has built in here since this was written".
    ///
    /// Three tests need it, and they are the three that walk: the golden comparison, the
    /// file-ending gate and the generated-marker gate. **A new test that walks a tree needs
    /// this too** - the shared conversion is for a test that reads the files it asked for.
    ///
    /// The answer replaces the shared one rather than clearing it: the tree it just wrote is
    /// the same tree, so a later caller is not made to convert a third time.
    /// </remarks>
    public static RunResult ConvertFresh(string scenario)
    {
        var result = ConvertOnce(scenario, null, Array.Empty<string>());

        Shared[scenario] = new Lazy<RunResult>(result);

        return result;
    }

    /// <summary>One conversion per scenario per test run, unless somebody asks otherwise.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<RunResult>>
        Shared = new(StringComparer.Ordinal);

    private static RunResult ConvertOnce(string scenario,
                                         IReadOnlyDictionary<string, string> environment,
                                         string[] extraArgs)
    {
        // Each scenario owns its output tree, and it is rebuilt from scratch so a
        // file that stops being generated shows up as a deletion rather than
        // lingering from a previous run.
        ClearOutput(RepoLayout.OutputDir(scenario));

        // And its cache with it. A conversion the suite asks for has to happen: a test that
        // compares output against a golden tree is not testing anything if the run it drove
        // decided the previous run's answer would do.
        ClearOutput(RepoLayout.CacheDir(scenario));

        // --debug: makes Tabbit print the call stack when it throws. Successful runs are
        // unaffected, and it lets the defect tests assert on stack frames instead of
        // framework exception text, which the runtime localizes.
        //
        // No --no-launch-profile any more: launchSettings.json is `dotnet run`'s business
        // and the executable does not read it.
        var args = new List<string>
        {
            "--recipe", RepoLayout.Recipe(scenario),
            "--debug",

            // Its own cache, so the suite does not leave one in the checkout and two
            // scenarios sharing a recipe name do not share a seal.
            "--cache-dir", RepoLayout.CacheDir(scenario),
        };

        args.AddRange(extraArgs ?? Array.Empty<string>());

        return Run(environment, Executable.Value, args.ToArray());
    }

    /// <summary>
    /// Empties a scenario's output tree, retrying a file another process is still holding.
    /// </summary>
    /// <remarks>
    /// Two test classes can convert the same scenario, and xUnit runs classes in parallel:
    /// one compiles the generated C or Unreal module out of the tree while the other is
    /// deleting it, and the compiler still has a source file open for a moment after it
    /// exits. The delete then fails, and the test that fails is whichever one got there
    /// second - which is a flake, not a finding.
    ///
    /// Retried rather than serialized, because the lock lasts milliseconds and serializing
    /// the suite costs minutes on every run.
    /// </remarks>
    private static void ClearOutput(string outputDir)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, recursive: true);

                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>
    /// Invokes the CLI with arbitrary arguments and no scenario.
    ///
    /// For the options that do not convert anything - `--new-recipe`, `--help` - where
    /// there is no output tree to clear and no recipe to point at.
    /// </summary>
    public static RunResult Invoke(params string[] arguments) => Invoke(null, arguments);

    /// <summary>
    /// The same, with an environment.
    ///
    /// The reading side of the history takes its connection from a recipe whose
    /// `${...}` placeholders come from the environment, exactly as a conversion's does.
    /// </summary>
    public static RunResult Invoke(IReadOnlyDictionary<string, string> environment, params string[] arguments)
        => Run(environment, Executable.Value, arguments);

    private static RunResult Run(
        IReadOnlyDictionary<string, string> environment, string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepoLayout.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // UTF-8 explicitly. Without it the subprocess's output is decoded as the
            // system codepage, which on Windows turns every non-ASCII author name and
            // cell value in a report into question marks.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Database connection strings in the recipes carry `${...}` placeholders
        // that the converter resolves from its own environment, so secrets stay out
        // of committed files. The values have to reach the subprocess.
        if (environment != null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using (var process = new Process { StartInfo = psi })
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(milliseconds: 300_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Tabbit did not finish within 5 minutes.");
            }

            // Flushes any output still buffered after the process exits.
            process.WaitForExit();

            return new RunResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString(),
            };
        }
    }
}
