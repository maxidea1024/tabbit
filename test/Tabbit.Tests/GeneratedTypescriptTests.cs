using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Type-checks the generated TypeScript with the real compiler.
///
/// Golden comparison only proves the output stopped changing; it cannot tell that
/// the output was never valid to begin with. Three defects had been sitting in the
/// TypeScript generator precisely because nothing ever compiled its output:
///
///   A12  reference setters assigned to a bare `_field` instead of `this._field`
///   A13  the constant-set re-export lost its braces to string interpolation,
///        emitting `export GameConfig from ...`
///   A14  constant-set modules were re-exported but never generated at all
///
/// Any of the three would have been caught here on the first run.
/// </summary>
public class GeneratedTypescriptTests
{
    [Theory]
    [InlineData("core")]
    // A side-filtered build drops whole tables and individual columns, so its
    // imports and record shapes differ from the unfiltered one.
    [InlineData("core-client")]
    [InlineData("core-server")]
    public void Generated_typescript_type_checks(string scenario)
    {
        // Deliberately a hard failure rather than a skip. A gate that quietly
        // turns itself off is worse than no gate: this one exists because three
        // defects survived for years behind output nobody compiled. CI installs
        // Node for exactly this test.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to type-check generated TypeScript. {why}");

        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded, $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string generatedDir = Path.Combine(RepoLayout.OutputDir(scenario), "typescript");

        var check = TypescriptToolchain.TypeCheck(generatedDir);

        Assert.True(check.Succeeded,
            $"Generated TypeScript does not compile.{Environment.NewLine}{check.Output}");
    }
}

internal sealed class TypeCheckResult
{
    public bool Succeeded;

    /// <summary>Standard output on its own, which is what a harness's result is.</summary>
    public string StdOut;

    /// <summary>Both streams together, for reporting a failure.</summary>
    public string Output;
}

internal static class TypescriptToolchain
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static bool IsAvailable(out string reason)
    {
        try
        {
            var probe = RunTsc(RepoLayout.Root, "--version");
            if (probe.Succeeded)
            {
                reason = null;
                return true;
            }

            reason = $"`npx tsc --version` failed.{Environment.NewLine}{probe.Output}";
            return false;
        }
        catch (Exception ex)
        {
            reason = $"No Node toolchain available ({ex.Message}).";
            return false;
        }
    }

    public static TypeCheckResult TypeCheck(string generatedDir)
    {
        // The scaffolding lives outside the generated tree: everything under the
        // scenario's output directory is compared against golden, so writing a
        // tsconfig in there would show up as an unexpected artifact.
        string checkDir = Path.Combine(RepoLayout.OutputDir("_tscheck"));
        Directory.CreateDirectory(checkDir);

        // The generated code imports `fs` and `path`. Declaring them as
        // opaque modules keeps the gate pointed at the generated code's own
        // correctness - unresolved names, bad syntax, wrong types - without
        // requiring an npm install of @types/node on every run.
        File.WriteAllText(Path.Combine(checkDir, "ambient.d.ts"), @"declare module 'fs'
declare module 'path'
declare module 'crypto'
declare function fetch(url: string, init?: any): Promise<any>
declare function setTimeout(handler: any, timeout?: number): any
declare function clearTimeout(handle: any): void
declare class AbortController { signal: any; abort(): void }
declare class TextDecoder { decode(input?: any): string }
");

        string generated = generatedDir.Replace('\\', '/');
        File.WriteAllText(Path.Combine(checkDir, "tsconfig.json"), $@"{{
  ""compilerOptions"": {{
""target"": ""es2020"",
""module"": ""commonjs"",
""moduleResolution"": ""node"",
""strict"": false,
""noEmit"": true,
""skipLibCheck"": true,
""types"": []
  }},
  ""include"": [""ambient.d.ts"", ""{generated}/**/*.ts""]
}}");

        return RunTsc(checkDir, "--project", "tsconfig.json");
    }

    /// <summary>
    /// Invokes the TypeScript compiler through npx.
    ///
    /// On Windows this goes via `cmd /c`: npx ships as `npx.cmd`, a batch wrapper
    /// that needs a command interpreter. Spawning it directly starts the process
    /// but npm then fails to resolve its own internals against the working
    /// directory, which reads like a missing toolchain rather than a launch
    /// problem.
    /// </summary>
    /// <summary>
    /// Compiles a generated tree together with a harness entry point and runs it.
    ///
    /// Compiled to JavaScript and run with node rather than through ts-node, so the run
    /// needs nothing installed that the type-check gate does not already need.
    ///
    /// The tsconfig and the ambient declarations are written into the generated
    /// directory, which is safe here because the conformance scenario has no golden tree
    /// to compare against - the type-check gate writes its scaffolding outside for
    /// exactly that reason.
    /// </summary>
    public static ToolResult RunScript(string entryFile, params string[] scriptArgs)
    {
        string generatedDir = Path.GetDirectoryName(entryFile);
        string outDir = Path.Combine(RepoLayout.OutputDir("_conformance"), "ts-build");

        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);

        Directory.CreateDirectory(outDir);

        File.WriteAllText(Path.Combine(generatedDir, "tabbit-ambient.d.ts"), @"declare module 'fs'
declare module 'path'
");

        string config = Path.Combine(generatedDir, "tsconfig.conformance.json");

        File.WriteAllText(config, $@"{{
  ""compilerOptions"": {{
""target"": ""es2020"",
""module"": ""commonjs"",
""moduleResolution"": ""node"",
""strict"": false,
""skipLibCheck"": true,
""types"": [],
""rootDir"": ""."",
""outDir"": ""{outDir.Replace(Path.DirectorySeparatorChar, '/')}""
  }},
  ""include"": [""**/*.ts""]
}}");

        var build = RunTsc(generatedDir, "--project", "tsconfig.conformance.json");

        if (!build.Succeeded)
            return new ToolResult { Succeeded = false, StdOut = "", Output = build.Output };

        string script = Path.Combine(
            outDir, Path.GetFileNameWithoutExtension(entryFile) + ".js");

        var run = Execute("node", outDir, Prepend(script, scriptArgs));

        return new ToolResult { Succeeded = run.Succeeded, StdOut = run.StdOut, Output = run.Output };
    }

    private static string[] Prepend(string first, string[] rest)
    {
        var all = new string[rest.Length + 1];
        all[0] = first;
        Array.Copy(rest, 0, all, 1, rest.Length);
        return all;
    }

    private static TypeCheckResult RunTsc(string workingDirectory, params string[] args)
    {
        if (OnWindows)
        {
            var cmdArgs = new List<string> { "/c", "npx", "tsc" };
            cmdArgs.AddRange(args);
            return Execute("cmd.exe", workingDirectory, cmdArgs.ToArray());
        }

        var npxArgs = new List<string> { "tsc" };
        npxArgs.AddRange(args);
        return Execute("npx", workingDirectory, npxArgs.ToArray());
    }

    private static TypeCheckResult Execute(string fileName, string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // The harness prints UTF-8. Without this the stream is decoded as the
            // system codepage, and every non-ASCII value in the corpus comes back
            // mangled - which reads exactly like a reader bug.
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var stdout = new StringBuilder();
        var output = new StringBuilder();

        using (var process = new Process { StartInfo = psi })
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stdout.AppendLine(e.Data);
                output.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(milliseconds: 300_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"`{fileName}` did not finish within 5 minutes.");
            }

            process.WaitForExit();

            return new TypeCheckResult
            {
                Succeeded = process.ExitCode == 0,
                StdOut = stdout.ToString(),
                Output = output.ToString(),
            };
        }
    }
}
