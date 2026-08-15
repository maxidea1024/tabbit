using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The TypeScript updater, compiled by tsc and run by node against a real HTTP server.
///
/// The file under test is the shipped one - lib/ts/tabbit/updater.ts - copied into a
/// work directory and compiled exactly as a consumer's project would compile it. The C#
/// updater has the fuller suite; this one checks that the second implementation of the
/// same design behaves the same way, which is the thing that goes wrong when a design
/// is written twice.
/// </summary>
public class TypescriptUpdaterTests : IDisposable
{
    private const string Scenario = "core";

    private readonly List<string> _temporary = new List<string>();

    public void Dispose()
    {
        foreach (string directory in _temporary)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// A first run fetches everything, and a second fetches nothing.
    /// </summary>
    [Fact]
    public void An_update_downloads_what_changed_and_nothing_else()
    {
        using var server = Serve(out string served);

        string work = Build();
        string cache = TemporaryDirectory("cache");

        var first = Run(work, server.BaseUrl, cache);

        Assert.True(first.GetProperty("succeeded").GetBoolean(),
            first.TryGetProperty("error", out var why) ? why.GetString() : "no reason given");

        foreach (string path in Directory.GetFiles(served, "*.tcb"))
        {
            string local = Path.Combine(cache, Path.GetFileName(path));

            Assert.True(File.Exists(local), $"{Path.GetFileName(path)} was not downloaded.");
            Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(local));
        }

        server.Requests.Clear();

        var second = Run(work, server.BaseUrl, cache);

        Assert.True(second.GetProperty("succeeded").GetBoolean());
        Assert.True(second.GetProperty("upToDate").GetBoolean());

        // The manifest, and not one byte more.
        Assert.Single(server.Requests);
    }

    /// <summary>
    /// A body that does not match the manifest's hash is refused, and the data already
    /// on disk is exactly as it was.
    /// </summary>
    [Fact]
    public void A_corrupt_download_fails_and_leaves_the_cache_alone()
    {
        using var server = Serve(out string served);

        string work = Build();
        string cache = TemporaryDirectory("cache");

        Assert.True(Run(work, server.BaseUrl, cache).GetProperty("succeeded").GetBoolean());

        var before = Snapshot(cache);

        Republish(served, "Item.tcb", Encoding.UTF8.GetBytes("new"));
        File.WriteAllBytes(Path.Combine(served, "Item.tcb"), Encoding.UTF8.GetBytes("corrupt"));

        var second = Run(work, server.BaseUrl, cache);

        Assert.False(second.GetProperty("succeeded").GetBoolean());
        Assert.Contains("Item.tcb", second.GetProperty("error").GetString());

        Assert.Equal(before, Snapshot(cache));
    }

    /// <summary>
    /// A server that refuses twice and then answers. The same retry the C# one does, in
    /// a language where the failure arrives as a rejected promise rather than an
    /// exception - which is exactly the sort of place a second implementation drifts.
    /// </summary>
    [Fact]
    public void A_transient_failure_is_retried()
    {
        using var server = Serve(out _);

        string work = Build();
        string cache = TemporaryDirectory("cache");

        server.FailNext(2, HttpStatusCode.ServiceUnavailable);

        var result = Run(work, server.BaseUrl, cache);

        Assert.True(result.GetProperty("succeeded").GetBoolean(),
            result.TryGetProperty("error", out var why) ? why.GetString() : "no reason given");

        Assert.True(server.Requests.Count >= 3, $"Only {server.Requests.Count} request(s) were made.");
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// Compiles the shipped updater and the driver beside it, and hands back the work
    /// directory holding the JavaScript.
    /// </summary>
    private string Build()
    {
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript updater. {why}");

        string work = TemporaryDirectory("ts");

        Directory.CreateDirectory(Path.Combine(work, "tabbit"));

        File.Copy(Path.Combine(RepoLayout.Root, "lib", "ts", "tabbit", "updater.ts"),
                  Path.Combine(work, "tabbit", "updater.ts"));

        File.Copy(Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "ts-updater", "main.ts"),
                  Path.Combine(work, "main.ts"));

        // Ambient declarations rather than @types/node, as the other TypeScript gates do:
        // what is being checked is the updater, not whether an npm install succeeded.
        File.WriteAllText(Path.Combine(work, "ambient.d.ts"), @"declare module 'fs'
declare module 'path'
declare module 'crypto'
declare const process: any
declare function fetch(url: string, init?: any): Promise<any>
declare function setTimeout(handler: any, timeout?: number): any
declare function clearTimeout(handle: any): void
declare class AbortController { signal: any; abort(): void }
declare class TextDecoder { decode(input?: any): string }
");

        File.WriteAllText(Path.Combine(work, "tsconfig.json"), @"{
  ""compilerOptions"": {
""target"": ""es2020"",
""module"": ""commonjs"",
""moduleResolution"": ""node"",
""strict"": false,
""skipLibCheck"": true,
""types"": [],
""outDir"": ""out""
  },
  ""include"": [""main.ts"", ""ambient.d.ts"", ""tabbit/**/*.ts""]
}");

        var built = Execute(work, "npx", "tsc", "--project", "tsconfig.json");

        Assert.True(built.Succeeded, $"The updater did not compile.{Environment.NewLine}{built.Output}");

        return work;
    }

    /// <summary>Runs one update and reads the result it prints.</summary>
    private static JsonElement Run(string work, string baseUrl, string cache)
    {
        var run = Execute(work, "node", "out/main.js", baseUrl, cache);

        Assert.True(run.Succeeded, $"The updater did not run.{Environment.NewLine}{run.Output}");

        string last = null;

        foreach (string line in run.StdOut.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
                last = trimmed;
        }

        Assert.NotNull(last);

        return JsonDocument.Parse(last).RootElement.Clone();
    }

    private UpdaterTestServer Serve(out string servedDirectory)
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        string source = Path.Combine(RepoLayout.OutputDir(Scenario), "binary");
        string served = TemporaryDirectory("served");

        foreach (string path in Directory.GetFiles(source))
            File.Copy(path, Path.Combine(served, Path.GetFileName(path)), overwrite: true);

        servedDirectory = served;
        return new UpdaterTestServer(served);
    }

    /// <summary>Replaces a served file and rewrites the manifest entry to match.</summary>
    private static void Republish(string served, string name, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(served, name), bytes);

        var json = new StringBuilder("{\n  \"MasterHash\": \"" + Guid.NewGuid().ToString("N") + "\",\n  \"Items\": [\n");
        bool first = true;

        foreach (string path in Directory.GetFiles(served, "*.tcb"))
        {
            byte[] content = File.ReadAllBytes(path);

            if (!first)
                json.Append(",\n");

            json.Append("    { \"Name\": \"").Append(Path.GetFileName(path))
                .Append("\", \"Size\": ").Append(content.Length)
                .Append(", \"Hash\": \"").Append(Md5(content)).Append("\" }");

            first = false;
        }

        json.Append("\n  ]\n}\n");

        File.WriteAllText(Path.Combine(served, "manifest-binary.json"), json.ToString());
    }

    private static string Md5(byte[] bytes)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();

        var text = new StringBuilder();

        foreach (byte b in md5.ComputeHash(bytes))
            text.Append(b.ToString("x2"));

        return text.ToString();
    }

    private static Dictionary<string, string> Snapshot(string directory)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            files[Path.GetRelativePath(directory, path)] = Md5(File.ReadAllBytes(path));

        return files;
    }

    private string TemporaryDirectory(string name)
    {
        string directory = Path.Combine(
            RepoLayout.OutputDir("_tsupdater"), name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        Directory.CreateDirectory(directory);
        _temporary.Add(directory);

        return directory;
    }

    private static ToolResult Execute(string workingDirectory, string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // npx and node are batch files on Windows, which only cmd can start.
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(fileName);
        }

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
