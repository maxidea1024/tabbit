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
/// The PHP updater, run by a real interpreter against a real HTTP server.
///
/// The file under test is the shipped one - lib/php/tabbit/TabbitUpdater.php -
/// copied into a work directory and required exactly as a consumer's program would
/// require it. The C# updater has the fuller suite; this one checks that another
/// implementation of the same design behaves the same way, which is the thing that
/// goes wrong when a design is written more than once.
/// </summary>
public class PhpUpdaterTests : IDisposable
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
    /// A first run fetches everything, and a second fetches nothing but the manifest.
    /// </summary>
    [Fact]
    public void An_update_downloads_what_changed_and_nothing_else()
    {
        using var server = Serve(out string served);

        string work = Stage();
        string cache = TemporaryDirectory("cache");

        var first = Run(work, server.BaseUrl, cache);

        Assert.True(first.GetProperty("succeeded").GetBoolean(),
            first.GetProperty("error").ToString());

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

        string work = Stage();
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
    /// A server that refuses twice and then answers.
    /// </summary>
    [Fact]
    public void A_transient_failure_is_retried()
    {
        using var server = Serve(out _);

        string work = Stage();
        string cache = TemporaryDirectory("cache");

        server.FailNext(2, HttpStatusCode.ServiceUnavailable);

        var result = Run(work, server.BaseUrl, cache);

        Assert.True(result.GetProperty("succeeded").GetBoolean(),
            result.GetProperty("error").ToString());

        Assert.True(server.Requests.Count >= 3, $"Only {server.Requests.Count} request(s) were made.");
    }

    /// <summary>
    /// A 404 is an answer, not a hiccup: one request, and the reason says so.
    /// </summary>
    [Fact]
    public void A_permanent_failure_is_not_retried()
    {
        using var server = Serve(out _);

        string work = Stage();
        string cache = TemporaryDirectory("cache");

        server.FailNext(1, HttpStatusCode.NotFound);

        var result = Run(work, server.BaseUrl, cache);

        Assert.False(result.GetProperty("succeeded").GetBoolean());
        Assert.Single(server.Requests);
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// Copies the shipped updater and the driver into a work directory.
    /// </summary>
    private string Stage()
    {
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"A PHP interpreter is required to run the PHP updater. {why}");

        string work = TemporaryDirectory("php");

        Directory.CreateDirectory(Path.Combine(work, "tabbit"));

        File.Copy(Path.Combine(RepoLayout.Root, "lib", "php", "tabbit", "TabbitUpdater.php"),
                  Path.Combine(work, "tabbit", "TabbitUpdater.php"), overwrite: true);

        File.Copy(Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "php-updater", "main.php"),
                  Path.Combine(work, "main.php"), overwrite: true);

        return work;
    }

    /// <summary>Runs one update and reads the result it prints.</summary>
    private static JsonElement Run(string work, string baseUrl, string cache)
    {
        var run = ConformanceHarness.RunPhpScript(work, "main.php", baseUrl, cache);

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
            RepoLayout.OutputDir("_phpupdater"),
            name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        Directory.CreateDirectory(directory);
        _temporary.Add(directory);

        return directory;
    }
}
