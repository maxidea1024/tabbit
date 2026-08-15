using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The history over HTTP.
///
/// Against the real process on a real port, not an in-memory test host: what is being
/// checked includes the option parsing, the bind address and the refusal to expose the
/// data without a token, and a test host would skip exactly those.
///
/// The important one is
/// <see cref="The_api_and_the_command_line_answer_the_same_bytes"/>. Everything else
/// here is plumbing; that one is the promise the whole design rests on.
/// </summary>
[Collection("databases")]
public class HistoryServerTests : IDisposable
{
    private const string Recipe = "test/fixtures/recipes/history.json";
    private const string Project = "tabbit-endtoend";
    private const string Branch = "endtoend";

    private readonly List<Process> _started = new List<Process>();
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already gone */ }

            process.Dispose();
        }

        _http.Dispose();
    }

    /// <summary>
    /// A port nothing else has. Asking the OS for one and closing it immediately leaves
    /// a number that is free now, which is the best any test can do.
    /// </summary>
    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    /// <summary>Makes sure the history holds something to serve.</summary>
    private void Recorded()
    {
        HistoryTestBed.EnsureDatabase();

        var result = TabbitRunner.Convert("history", DatabaseFixture.ConverterEnvironment,
            "--commit", "serve-" + Guid.NewGuid().ToString("N"),
            "--branch", Branch,
            "--commit-author", "테스터 <tester@example.com>",
            "--commit-date", "2026-08-03T11:00:00+09:00",
            "--repository", Path.GetTempPath());

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");
    }

    private async Task<string> Serve(string token = null, string bind = null)
    {
        HistoryTestBed.EnsureDatabase();

        int port = FreePort();

        // The built executable, the same one every conversion goes through, rather than
        // `dotnet run`. Two reasons, and the second is why this changed: starting a program is
        // faster than evaluating a project and checking its build, and - because the suite's
        // build writes to an output directory of its own - a `dotnet run` here would build to a
        // different path, leaving each of the two to invalidate the other. Every switch was then
        // a full rebuild, which is what made the first server of a run take longer to appear than
        // the test was willing to wait.
        var psi = new ProcessStartInfo(TabbitRunner.CliExecutable)
        {
            WorkingDirectory = RepoLayout.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in new[]
                 {
                     "--recipe", Recipe, "--serve", "--port", port.ToString(),
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        if (bind != null)
        {
            psi.ArgumentList.Add("--bind");
            psi.ArgumentList.Add(bind);
        }

        foreach (var pair in DatabaseFixture.ConverterEnvironment)
            psi.Environment[pair.Key] = pair.Value;

        if (token != null)
            psi.Environment["TABBIT_SERVE_TOKEN"] = token;

        var process = Process.Start(psi);
        _started.Add(process);

        string root = $"http://127.0.0.1:{port}";

        await WaitUntilUp(root, process, token);

        return root;
    }

    private async Task WaitUntilUp(string root, Process process, string token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "The server exited before it started listening:" + Environment.NewLine
                    + await process.StandardOutput.ReadToEndAsync());
            }

            try
            {
                var response = await _http.SendAsync(Request(root + "/api/v1/healthz", token));

                // A token-protected server answers 401 until the token is sent, which
                // still means it is listening.
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not up yet.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("The server did not start listening within 90 seconds.");
    }

    private static HttpRequestMessage Request(string url, string token = null, string etag = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (token != null)
            request.Headers.Add("Authorization", "Bearer " + token);

        if (etag != null)
            request.Headers.Add("If-None-Match", etag);

        return request;
    }

    // ---------------------------------------------------------------- tests

    /// <summary>
    /// The gate the whole design rests on: the API and the command line do not compute
    /// anything, they serialise what one query layer returned. If these two ever differ,
    /// a number on the page and the same number in a terminal disagree, and there is no
    /// way for a reader to tell which is right.
    ///
    /// Everything but the timestamp of the answer itself, which is the one field that
    /// legitimately differs between two calls.
    /// </summary>
    [Fact]
    public async Task The_api_and_the_command_line_answer_the_same_bytes()
    {
        Recorded();

        string root = await Serve();

        string served = await _http.GetStringAsync(
            $"{root}/api/v1/diff?project={Project}&branch={Branch}&limit=50");

        var printed = TabbitRunner.Invoke(DatabaseFixture.ConverterEnvironment,
            "--recipe", Recipe, "--history", "--branch", Branch, "--limit", "50");

        Assert.True(printed.Succeeded, printed.Describe());

        // The CLI writes a `dotnet run` banner before the JSON.
        string json = printed.StdOut.Substring(printed.StdOut.IndexOf('{'));

        Assert.Equal(Without(json, "generatedAt"), Without(served, "generatedAt"));
    }

    private static string Without(string json, string property)
    {
        using var document = JsonDocument.Parse(json);

        var node = System.Text.Json.Nodes.JsonNode.Parse(json);

        Erase(node, property);

        return node.ToJsonString();
    }

    private static void Erase(System.Text.Json.Nodes.JsonNode node, string property)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            if (obj.ContainsKey(property))
                obj[property] = null;

            foreach (var pair in obj)
                Erase(pair.Value, property);
        }
        else if (node is System.Text.Json.Nodes.JsonArray array)
        {
            foreach (var item in array)
                Erase(item, property);
        }
    }

    [Fact]
    public async Task The_page_and_its_assets_are_served_as_what_they_are()
    {
        Recorded();

        string root = await Serve();

        var page = await _http.GetAsync(root + "/");
        Assert.Equal("text/html", page.Content.Headers.ContentType.MediaType);
        Assert.Contains("history.js", await page.Content.ReadAsStringAsync());

        var css = await _http.GetAsync(root + "/history.css");
        Assert.Equal("text/css", css.Content.Headers.ContentType.MediaType);

        var js = await _http.GetAsync(root + "/history.js");
        Assert.Equal("text/javascript", js.Content.Headers.ContentType.MediaType);
    }

    /// <summary>
    /// Snapshots never change once written, so an answer about a range is good for ever
    /// - and a page that reloads asks the same questions over and over.
    /// </summary>
    [Fact]
    public async Task An_unchanged_answer_is_not_sent_twice()
    {
        Recorded();

        string root = await Serve();

        string url = $"{root}/api/v1/stats?project={Project}&branch={Branch}";

        var first = await _http.GetAsync(url);
        string tag = first.Headers.ETag.ToString();

        Assert.False(string.IsNullOrEmpty(tag), "No entity tag was sent.");

        var again = await _http.SendAsync(Request(url, etag: tag));

        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
    }

    /// <summary>
    /// Whether this process is up, without asking the database.
    ///
    /// A load balancer that restarted the server because MySQL blinked would take the
    /// one thing that could have explained the outage off the air with it.
    /// </summary>
    [Fact]
    public async Task Health_says_only_whether_the_server_is_up()
    {
        string root = await Serve();

        Assert.Equal("ok", (await _http.GetStringAsync(root + "/api/v1/healthz")).Trim());
    }

    // --------------------------------------------------------------- failures

    /// <summary>
    /// A request the caller got wrong is answered with 400 and the reason.
    ///
    /// It used to be a 500 with an empty body, and nothing in the log either: Kestrel's
    /// logging providers are cleared so that Serilog is the only log, so ASP.NET's report
    /// of the unhandled exception went nowhere. A caller saw a bare 500 and the operator
    /// saw nothing at all - for input the command line answers with a plain sentence.
    /// </summary>
    [Fact]
    public async Task A_commit_the_history_does_not_hold_is_a_bad_request()
    {
        Recorded();

        string root = await Serve();

        var response = await _http.GetAsync(
            $"{root}/api/v1/diff?branch={Branch}&to=0000000000000000000000000000000000000000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // The same sentence `--history` prints, rather than a status code on its own.
        Assert.Contains("no snapshot", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// And a parameter that is not a number, which is the other thing a caller mistypes.
    /// </summary>
    [Fact]
    public async Task A_limit_that_is_not_a_number_is_a_bad_request()
    {
        Recorded();

        string root = await Serve();

        var response = await _http.GetAsync($"{root}/api/v1/snapshots?branch={Branch}&limit=lots");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Contains("is not a number", document.RootElement.GetProperty("error").GetString());
    }

    // ------------------------------------------------------------------ auth

    /// <summary>
    /// A token in the query string is moved into a cookie and taken out of the URL.
    ///
    /// A query string reaches an access log, a `Referer` and the address bar - which is
    /// where a secret gets copied into a chat window from. It is still accepted, because a
    /// browser cannot be pointed at a URL and send a header, but it survives exactly one
    /// request.
    /// </summary>
    [Fact]
    public async Task A_token_in_the_url_becomes_a_cookie()
    {
        Recorded();

        string root = await Serve(token: "s3cret");

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        var response = await client.GetAsync($"{root}/?token=s3cret");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        // Where it is sent has no token on it any more.
        Assert.DoesNotContain("token", response.Headers.Location.ToString());

        string cookie = string.Join(" ", response.Headers.GetValues("Set-Cookie"));

        Assert.Contains("tabbit_token=s3cret", cookie);
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());

        // And the cookie alone gets the page, which is what the redirect relies on.
        var followed = await client.GetAsync(root + "/");

        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    /// <summary>
    /// An API call with a query token is answered rather than redirected.
    ///
    /// That one is a script or a curl line, and a 302 to a URL needing a cookie would
    /// break it for no gain - the address bar is not the concern there.
    /// </summary>
    [Fact]
    public async Task An_api_call_with_a_query_token_is_answered_directly()
    {
        Recorded();

        string root = await Serve(token: "s3cret");

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        var response = await client.GetAsync($"{root}/api/v1/projects?token=s3cret");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Project, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_the_token_nothing_is_served()
    {
        Recorded();

        string root = await Serve(token: "s3cret");

        var response = await _http.GetAsync($"{root}/api/v1/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The page too. It carries no data, but a page that loads invites somebody to
        // conclude the port is open to them.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync(root + "/")).StatusCode);
    }

    [Fact]
    public async Task With_the_token_everything_is()
    {
        Recorded();

        string root = await Serve(token: "s3cret");

        var response = await _http.SendAsync(Request($"{root}/api/v1/projects", "s3cret"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Project, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_wrong_token_is_no_better_than_none()
    {
        string root = await Serve(token: "s3cret");

        var response = await _http.SendAsync(Request($"{root}/api/v1/projects", "s3cre"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Opening a port and forgetting the authentication is the ordinary way a tool like
    /// this leaks, and what leaks is every value in the project's design data plus the
    /// name of everyone who touched it. So it is refused, not warned about.
    /// </summary>
    [Fact]
    public void Serving_beyond_this_machine_without_a_token_is_refused()
    {
        HistoryTestBed.EnsureDatabase();

        var result = TabbitRunner.Invoke(DatabaseFixture.ConverterEnvironment,
            "--recipe", Recipe, "--serve", "--bind", "0.0.0.0", "--port", FreePort().ToString());

        Assert.False(result.Succeeded, "An unprotected public bind was accepted.");
        Assert.Contains("TABBIT_SERVE_TOKEN", result.StdOut);
    }

    [Fact]
    public void An_address_that_is_not_an_address_is_refused()
    {
        HistoryTestBed.EnsureDatabase();

        var environment = new Dictionary<string, string>(DatabaseFixture.ConverterEnvironment)
        {
            ["TABBIT_SERVE_TOKEN"] = "s3cret",
        };

        var result = TabbitRunner.Invoke(environment,
            "--recipe", Recipe, "--serve", "--bind", "not-an-address", "--port", FreePort().ToString());

        Assert.False(result.Succeeded, "A meaningless bind address was accepted.");
        Assert.Contains("is not an address", result.StdOut);
    }
}
