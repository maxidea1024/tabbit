using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tabbit.Binary;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The updater, against a real HTTP server on a real port.
///
/// The file under test is the one consumers get - lib/cs/tabbit/TabbitUpdater.cs is
/// compiled into this assembly - so what is asserted here is the shipped behaviour rather
/// than a copy of it.
///
/// A patcher is one of the few pieces of a data pipeline that runs on somebody's phone on
/// a train, so what these tests are mostly about is the ways it can go wrong: a body that
/// arrived truncated, a server that is briefly refusing, a cache somebody cleaned out. The
/// rule it holds to in all of them is that the data already on disk stays readable.
/// </summary>
public class TabbitUpdaterTests : IDisposable
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
                // A directory a virus scanner still holds is not a test failure.
            }
        }
    }

    // ------------------------------------------------------------------ the happy path

    /// <summary>
    /// A first run fetches everything the manifest names, byte for byte.
    /// </summary>
    [Fact]
    public async Task A_first_update_downloads_every_file()
    {
        using var server = Serve(out string served);
        string cache = TemporaryDirectory("cache");

        var result = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.UpToDate);

        foreach (string path in Directory.GetFiles(served, "*.tcb"))
        {
            string local = Path.Combine(cache, Path.GetFileName(path));

            Assert.True(File.Exists(local), $"{Path.GetFileName(path)} was not downloaded.");
            Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(local));
        }

        Assert.Equal(Directory.GetFiles(served, "*.tcb").Length, result.DownloadedCount);

        // The manifest is cached too - it is what the next run compares against.
        Assert.True(File.Exists(Path.Combine(cache, "manifest-binary.json")));
    }

    /// <summary>
    /// And a second one fetches nothing. This is the case that runs on every launch, so
    /// "nothing changed" has to cost one request rather than a full download.
    /// </summary>
    [Fact]
    public async Task An_update_with_nothing_new_downloads_nothing()
    {
        using var server = Serve(out _);
        string cache = TemporaryDirectory("cache");

        Assert.True((await TabbitUpdater.UpdateAsync(server.BaseUrl, cache)).Succeeded);

        server.Requests.Clear();

        var second = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache);

        Assert.True(second.Succeeded, second.Error);
        Assert.True(second.UpToDate);
        Assert.Equal(0, second.DownloadedCount);

        // The manifest, and not one byte more.
        Assert.Single(server.Requests);
    }

    /// <summary>
    /// One table changed on the server: one file downloaded, and the rest left alone.
    /// </summary>
    [Fact]
    public async Task Only_the_changed_file_is_fetched()
    {
        using var server = Serve(out string served);
        string cache = TemporaryDirectory("cache");

        Assert.True((await TabbitUpdater.UpdateAsync(server.BaseUrl, cache)).Succeeded);

        Republish(served, "Item.tcb", Encoding.UTF8.GetBytes("a different Item table"));
        server.Requests.Clear();

        var second = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache);

        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(1, second.DownloadedCount);

        Assert.Equal("a different Item table",
            File.ReadAllText(Path.Combine(cache, "Item.tcb")));

        // The manifest and that one file.
        Assert.Equal(2, server.Requests.Count);
        Assert.Contains(server.Requests, path => path.EndsWith("Item.tcb", StringComparison.Ordinal));
    }

    /// <summary>
    /// A table that left the manifest leaves the cache with it. Otherwise a cache grows
    /// forever and keeps serving a table the schema dropped.
    /// </summary>
    [Fact]
    public async Task A_file_the_manifest_dropped_is_deleted()
    {
        using var server = Serve(out string served);
        string cache = TemporaryDirectory("cache");

        Assert.True((await TabbitUpdater.UpdateAsync(server.BaseUrl, cache)).Succeeded);
        Assert.True(File.Exists(Path.Combine(cache, "Item.tcb")));

        Unpublish(served, "Item.tcb");

        var second = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache);

        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(1, second.DeletedCount);
        Assert.False(File.Exists(Path.Combine(cache, "Item.tcb")));
    }

    /// <summary>
    /// A cache somebody emptied by hand refills, even though the manifest says it is
    /// current. The manifest is a record of what was downloaded, not proof it is there.
    /// </summary>
    [Fact]
    public async Task A_missing_local_file_is_fetched_again()
    {
        using var server = Serve(out _);
        string cache = TemporaryDirectory("cache");

        Assert.True((await TabbitUpdater.UpdateAsync(server.BaseUrl, cache)).Succeeded);

        File.Delete(Path.Combine(cache, "Item.tcb"));

        var second = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache);

        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(1, second.DownloadedCount);
        Assert.True(File.Exists(Path.Combine(cache, "Item.tcb")));
    }

    // ------------------------------------------------------------------ going wrong

    /// <summary>
    /// A body that does not match the hash the manifest gives for it.
    ///
    /// This is what a truncated transfer looks like when a proxy reported success, and it
    /// is the reason the hash is checked at all. The update fails, and - the part that
    /// matters - the data already on disk is exactly as it was.
    /// </summary>
    [Fact]
    public async Task A_corrupt_download_fails_and_leaves_the_cache_alone()
    {
        using var server = Serve(out string served);
        string cache = TemporaryDirectory("cache");

        Assert.True((await TabbitUpdater.UpdateAsync(server.BaseUrl, cache)).Succeeded);

        var before = Snapshot(cache);

        // The manifest says one thing and the file says another, which is what a
        // half-finished upload looks like from a client.
        Republish(served, "Item.tcb", Encoding.UTF8.GetBytes("new"));
        File.WriteAllBytes(Path.Combine(served, "Item.tcb"), Encoding.UTF8.GetBytes("corrupt"));

        var second = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache);

        Assert.False(second.Succeeded);
        Assert.Contains("Item.tcb", second.Error);
        Assert.Contains("manifest says", second.Error);

        Assert.Equal(before, Snapshot(cache));
    }

    /// <summary>
    /// A server that fails twice and then answers.
    ///
    /// A phone changing cell towers gets exactly this, and giving up on the first refusal
    /// would mean a patch that fails for a reason that had already gone away.
    /// </summary>
    [Fact]
    public async Task A_transient_failure_is_retried()
    {
        using var server = Serve(out _);
        string cache = TemporaryDirectory("cache");

        server.FailNext(2, HttpStatusCode.ServiceUnavailable);

        var result = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache,
            new TabbitUpdateOptions { RetryDelay = TimeSpan.FromMilliseconds(1) });

        Assert.True(result.Succeeded, result.Error);

        // Two refusals and the answer, for the manifest alone.
        Assert.True(server.Requests.Count >= 3, $"Only {server.Requests.Count} request(s) were made.");
    }

    /// <summary>
    /// And one that refuses more times than the attempts allow. The failure names the
    /// status, and nothing was written.
    /// </summary>
    [Fact]
    public async Task Retries_are_bounded()
    {
        using var server = Serve(out _);
        string cache = TemporaryDirectory("cache");

        server.FailNext(10, HttpStatusCode.ServiceUnavailable);

        var result = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache,
            new TabbitUpdateOptions { MaxAttempts = 3, RetryDelay = TimeSpan.FromMilliseconds(1) });

        Assert.False(result.Succeeded);
        Assert.Contains("503", result.Error);

        Assert.Equal(3, server.Requests.Count);
        Assert.False(File.Exists(Path.Combine(cache, "manifest-binary.json")));
    }

    /// <summary>
    /// A 404 is not retried. The server has answered: retrying costs the client three
    /// round trips to be told the same thing.
    /// </summary>
    [Fact]
    public async Task A_missing_file_is_not_retried()
    {
        using var server = Serve(out _);
        string cache = TemporaryDirectory("cache");

        var result = await TabbitUpdater.UpdateAsync(server.BaseUrl, cache,
            new TabbitUpdateOptions
            {
                ManifestFileName = "no-such-manifest.json",
                RetryDelay = TimeSpan.FromMilliseconds(1),
            });

        Assert.False(result.Succeeded);
        Assert.Contains("404", result.Error);
        Assert.Single(server.Requests);
    }

    /// <summary>
    /// A cancelled update stops and says so, rather than finishing what it started.
    /// </summary>
    [Fact]
    public async Task A_cancelled_update_stops()
    {
        using var server = Serve(out _);
        string cache = TemporaryDirectory("cache");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await TabbitUpdater.UpdateAsync(
            server.BaseUrl, cache, null, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Contains("cancelled", result.Error);
    }

    // ------------------------------------------------------------------ the manifest

    /// <summary>
    /// The hand-written parser against the manifest the exporter really wrote.
    ///
    /// It is hand-written because the generated code has to compile with nothing
    /// installed, which means this is the one place where a change to how the manifest is
    /// serialized would go unnoticed - so it is read here from the file the converter
    /// produced rather than from a literal in a test.
    /// </summary>
    [Fact]
    public void The_manifest_parser_reads_what_the_exporter_writes()
    {
        TabbitRunner.Convert(Scenario);

        string path = Path.Combine(RepoLayout.OutputDir(Scenario), "binary", "manifest-binary.json");
        var manifest = TabbitManifest.Parse(File.ReadAllText(path));

        Assert.NotEmpty(manifest.MasterHash);
        Assert.NotEmpty(manifest.Entries);

        var item = manifest.Find("Item.tcb");

        Assert.NotNull(item);
        Assert.Equal(new FileInfo(Path.Combine(Path.GetDirectoryName(path), "Item.tcb")).Length, item.Size);
        Assert.Equal(32, item.Hash.Length);
    }

    // ------------------------------------------------------------------ the fixture

    /// <summary>A copy of the `core` scenario's binary export, served over HTTP.</summary>
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

    /// <summary>
    /// Replaces a served file and rewrites the manifest entry to match - what the exporter
    /// does when a table changed.
    /// </summary>
    private static void Republish(string served, string name, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(served, name), bytes);
        RewriteManifest(served);
    }

    /// <summary>Removes a served file and its manifest entry.</summary>
    private static void Unpublish(string served, string name)
    {
        File.Delete(Path.Combine(served, name));
        RewriteManifest(served);
    }

    /// <summary>
    /// Rebuilds the manifest from whatever is in the directory, in the shape the exporter
    /// writes.
    /// </summary>
    private static void RewriteManifest(string served)
    {
        var json = new StringBuilder();

        json.Append("{\n  \"LastUpdatedDate\": \"2026-01-01T00:00:00.0000000+09:00\",\n");
        json.Append("  \"MasterHash\": \"").Append(Guid.NewGuid().ToString("N")).Append("\",\n");
        json.Append("  \"TotalSize\": 0,\n  \"Items\": [\n");

        bool first = true;

        foreach (string path in Directory.GetFiles(served, "*.tcb"))
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (!first)
                json.Append(",\n");

            json.Append("    {\n");
            json.Append("      \"Name\": \"").Append(Path.GetFileName(path)).Append("\",\n");
            json.Append("      \"Size\": ").Append(bytes.Length).Append(",\n");
            json.Append("      \"Hash\": \"").Append(Md5(bytes)).Append("\",\n");
            json.Append("      \"LastUpdatedDate\": \"2026-01-01T00:00:00.0000000+09:00\"\n");
            json.Append("    }");

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

    /// <summary>Every file in a directory and its bytes, for comparing before and after.</summary>
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
            RepoLayout.OutputDir("_updater"), name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        Directory.CreateDirectory(directory);
        _temporary.Add(directory);

        return directory;
    }
}
