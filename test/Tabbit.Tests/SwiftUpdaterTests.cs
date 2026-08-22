using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The Swift updater, compiled by swiftc and run against a real HTTP server.
///
/// The file under test is the shipped one - lib/swift/tabbit/Updater.swift - compiled in a
/// work directory exactly as a consumer's project would compile it, with no package: it
/// takes Foundation and nothing else, and whether that is still true is the first thing this
/// gate asks.
///
/// The second is the arithmetic. Two of the pieces here are written out rather than borrowed
/// - MD5, and the retry with its backoff - and the reserved-word gate only asks whether they
/// compile. A hash that is subtly wrong passes every compile and then refuses every download
/// as corrupt, which is exactly the failure a gate should meet before a consumer does.
/// </summary>
public class SwiftUpdaterTests : IDisposable
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
    /// A body that does not match the manifest's hash is refused, and the data already on
    /// disk is exactly as it was.
    /// </summary>
    /// <remarks>
    /// This is the check that the hand-written MD5 agrees with the exporter's, in the
    /// direction that matters: the first run above would also fail if every hash disagreed,
    /// but only this one says that a hash which *should* differ does.
    /// </remarks>
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

        Assert.True(server.Requests.Count >= 3,
            $"Only {server.Requests.Count} request(s) were made.");
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
    /// Copies the shipped updater and the driver into a work directory and compiles them.
    /// </summary>
    /// <remarks>
    /// The reader comes along because the updater reports its failures as the reader's error
    /// type, which is also how a consuming project has them: the two files ship together.
    /// </remarks>
    private string Stage()
    {
        Assert.True(ConformanceHarness.SwiftIsAvailable(out string why),
            $"A Swift toolchain is required to run the Swift updater. {why}");

        string work = TemporaryDirectory("swift");

        foreach (string name in new[] { "Updater.swift", "TcbReader.swift" })
        {
            File.Copy(Path.Combine(RepoLayout.Root, "lib", "swift", "tabbit", name),
                      Path.Combine(work, name), overwrite: true);
        }

        File.Copy(
            Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "swift-updater", "main.swift"),
            Path.Combine(work, "main.swift"), overwrite: true);

        var built = ConformanceHarness.CompileSwiftProgram(
            work, "updater", "TcbReader.swift", "Updater.swift", "main.swift");

        Assert.True(built.Succeeded,
            $"The updater did not compile.{Environment.NewLine}{built.Output}");

        return work;
    }

    /// <summary>Runs one update and reads the result it prints.</summary>
    private static JsonElement Run(string work, string baseUrl, string cache)
    {
        var run = ConformanceHarness.RunSwiftProgram(work, "updater", baseUrl, cache);

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

        var json = new StringBuilder(
            "{\n  \"MasterHash\": \"" + Guid.NewGuid().ToString("N") + "\",\n  \"Items\": [\n");

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
            RepoLayout.OutputDir("_swiftupdater"),
            name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        Directory.CreateDirectory(directory);
        _temporary.Add(directory);

        return directory;
    }
}
