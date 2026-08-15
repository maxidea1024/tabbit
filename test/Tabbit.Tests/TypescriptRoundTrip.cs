using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Tabbit.Tests;

internal sealed class RoundTripResult
{
    public bool Succeeded;
    public string StdOut;
    public string Output;
}

/// <summary>
/// Builds and runs the TypeScript round-trip check.
///
/// The check loads the same tables from the JSON export and from the binary export and
/// reports any field the two disagree on. That is the property worth testing: a
/// generated table exposes one API, so both read paths have to yield the same values,
/// and only running them side by side shows whether they do.
///
/// It found two real disagreements when first written - a 64-bit integer rounded by
/// JSON's single numeric type, and a float whose 32-bit precision the JSON path had
/// discarded - neither of which any amount of reading would have surfaced.
/// </summary>
internal static class TypescriptRoundTrip
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <param name="driver">
    /// Which driver to compile in, under test/fixtures/tools. Each names the tables of its
    /// own fixture, so a scenario with different tables needs its own.
    /// </param>
    public static RoundTripResult Run(string scenario, string driver = "ts-check")
    {
        string workDir = Path.Combine(RepoLayout.OutputDir("_tsroundtrip"), scenario);

        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        Directory.CreateDirectory(workDir);

        // The generated output is self-contained - the generator writes the reader
        // into it - so copying the directory is all the setup there is.
        CopyDirectory(Path.Combine(RepoLayout.OutputDir(scenario), "typescript"),
                      Path.Combine(workDir, "generated"));

        File.Copy(Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", driver, "main.ts"),
                  Path.Combine(workDir, "main.ts"));

        // Ambient declarations instead of @types/node.
        //
        // Only the shapes the check actually touches are needed, and declaring them
        // keeps the test from depending on an npm install succeeding - which on CI is
        // one more network call that can fail for reasons having nothing to do with
        // the code under test. Types are `any` here; what this test is checking is
        // the values, and the dedicated tsc gate does the type checking.
        File.WriteAllText(Path.Combine(workDir, "ambient.d.ts"), @"declare module 'fs'
declare module 'path'
declare module 'crypto'
declare function fetch(url: string, init?: any): Promise<any>
declare function setTimeout(handler: any, timeout?: number): any
declare function clearTimeout(handle: any): void
declare class AbortController { signal: any; abort(): void }
declare class TextDecoder { decode(input?: any): string }
declare const process: any
declare function require(name: string): any
");

        File.WriteAllText(Path.Combine(workDir, "tsconfig.json"), @"{
  ""compilerOptions"": {
""target"": ""es2020"",
""module"": ""commonjs"",
""moduleResolution"": ""node"",
""strict"": false,
""skipLibCheck"": true,
""types"": [],
""outDir"": ""out""
  },
  ""include"": [""main.ts"", ""ambient.d.ts"", ""generated/**/*.ts""]
}");

        var build = RunTool(workDir, "npx", "tsc", "--project", "tsconfig.json");
        if (!build.Succeeded)
            return build;

        return RunTool(workDir, "node", "out/main.js",
                       Path.Combine(RepoLayout.OutputDir(scenario), "json-named"),
                       Path.Combine(RepoLayout.OutputDir(scenario), "binary"));
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    /// <summary>
    /// Runs a Node tool. On Windows both npx and node arrive as batch wrappers that
    /// need a command interpreter, so the call goes through cmd.
    /// </summary>
    private static RoundTripResult RunTool(string workDir, string tool, params string[] args)
    {
        if (OnWindows)
        {
            var wrapped = new string[args.Length + 3];
            wrapped[0] = "/c";
            wrapped[1] = tool;
            Array.Copy(args, 0, wrapped, 2, args.Length);
            wrapped[wrapped.Length - 1] = "";

            return Execute("cmd.exe", workDir, Trim(wrapped));
        }

        return Execute(tool, workDir, args);
    }

    private static string[] Trim(string[] values)
    {
        int length = values.Length;
        while (length > 0 && string.IsNullOrEmpty(values[length - 1]))
            length--;

        var result = new string[length];
        Array.Copy(values, result, length);
        return result;
    }

    private static RoundTripResult Execute(string fileName, string workingDirectory, params string[] args)
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

        return new RoundTripResult
        {
            Succeeded = process.ExitCode == 0,
            StdOut = stdout.ToString(),
            Output = combined.ToString(),
        };
    }
}
