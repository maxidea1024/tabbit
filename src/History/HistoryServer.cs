using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Tabbit.Exporters;
using Tabbit.Recipe;

namespace Tabbit.History;

/// <summary>
/// The history, over HTTP: a JSON API and the page that draws it.
///
/// Every endpoint calls <see cref="HistoryQuery"/> and serialises what it returns with
/// the same serialiser the command line uses. That is not tidiness - it is the reason a
/// number on the page cannot disagree with the same number from `--history`, and a test
/// compares the two byte for byte.
///
/// Read-only, throughout. Nothing here writes, and the account it connects with need
/// not be able to; only a conversion adds to the history. A server that could modify
/// what it serves is a server that can corrupt it.
/// </summary>
internal static class HistoryServer
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Recording;

    /// <summary>Where the token is read from, when one is needed.</summary>
    public const string TokenVariable = "TABBIT_SERVE_TOKEN";

    private const string ApiPrefix = "/api/v1";

    public static int Run(Options options, RecipeModel recipe)
    {
        var (connectionString, projectKey) = HistoryCommand.Connection(options, recipe);

        // Worked out once, at start-up: a tag can only be resolved by a working copy,
        // and asking git on every request would spawn a process per query. A server on
        // a machine with no checkout simply cannot resolve tags, and says so.
        string? repository = CommitInfo.Resolve(options, recipe).RepositoryPath;

        string bind = string.IsNullOrWhiteSpace(options.Bind) ? "127.0.0.1" : options.Bind.Trim();
        int port = options.Port <= 0 ? 8080 : options.Port;

        string? token = Environment.GetEnvironmentVariable(TokenVariable);

        RefuseUnprotectedExposure(bind, token);

        var builder = WebApplication.CreateSlimBuilder();

        // Kestrel's own logging duplicates what Serilog already reports, in a different
        // shape. One log.
        //
        // Which is why `Report` below writes its own line for anything that fails. With
        // the providers cleared, ASP.NET's report of an unhandled exception goes nowhere,
        // and for a while nothing else wrote one either.
        builder.Logging.ClearProviders();

        // Configured on Kestrel rather than through a URL string: an address that does
        // not parse should be reported here rather than accepted and silently turned
        // into a listener on something else.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(Address(bind), port));

        var app = builder.Build();

        // Before anything else, so it wraps every route below.
        app.Use(Report);

        Map(app, connectionString, projectKey, token, repository);

        Log.Information($"Serving the history of `{projectKey}` on http://{bind}:{port}/");

        if (token is not null)
            Log.Information($"A bearer token is required; it comes from ${TokenVariable}.");

        Log.Information(repository is null
            ? "No working copy was found, so a range can only be asked for by commit hash."
            : $"Tags and revisions will be resolved against `{repository}`.");

        app.Run();

        return 0;
    }

    /// <summary>
    /// The address to listen on.
    ///
    /// `localhost` and `*` are spelled the way people expect rather than as the numbers
    /// Kestrel wants; anything else has to be an address, and is rejected here rather
    /// than turning into a listener somewhere unintended.
    /// </summary>
    private static System.Net.IPAddress Address(string bind)
    {
        if (string.Equals(bind, "localhost", StringComparison.OrdinalIgnoreCase))
            return System.Net.IPAddress.Loopback;

        if (bind == "*" || bind == "0.0.0.0")
            return System.Net.IPAddress.Any;

        if (System.Net.IPAddress.TryParse(bind, out var address))
            return address;

        throw new TabbitException(
            $"--bind {bind} is not an address. Use an IP, `localhost`, or `0.0.0.0` for every " +
            $"interface.");
    }

    /// <summary>
    /// Refuses to listen beyond this machine without a token.
    ///
    /// Opening a port and forgetting the authentication is the ordinary way a tool like
    /// this leaks, and what leaks here is every value in the project's design data plus
    /// the name of everyone who touched it. Loopback needs no token; anything else does,
    /// and the refusal is up front rather than a warning nobody reads.
    /// </summary>
    private static void RefuseUnprotectedExposure(string bind, string? token)
    {
        if (!string.IsNullOrEmpty(token))
            return;

        bool loopback = bind == "127.0.0.1" || bind == "::1" || bind == "localhost";

        if (loopback)
            return;

        throw new TabbitException(
            $"--bind {bind} would serve the history to anything that can reach this machine, " +
            $"and no token is set. Set ${TokenVariable} to a secret and send it as " +
            $"`Authorization: Bearer <token>`, or leave --bind at 127.0.0.1.");
    }

    // ----------------------------------------------------------------- routes

    private static void Map(
        WebApplication app, string connectionString, string project, string? token, string? repository)
    {
        if (token is not null)
            app.Use((context, next) => Authorize(context, token, next));

        app.MapGet("/", () => Html(HistoryView.Live()));

        app.MapGet("/history.css", () => Asset("history.css"));
        app.MapGet("/history.js", () => Asset("history.js"));

        // Says whether this process is up. Deliberately does not touch the database:
        // a load balancer restarting the server because MySQL blinked would take the
        // one thing that could have explained the outage off the air with it.
        app.MapGet(ApiPrefix + "/healthz", () => Results.Text("ok", "text/plain; charset=utf-8"));

        Query(app, "/projects", (q, r, _) => q.Projects());
        Query(app, "/branches", (q, r, p) => q.Branches(p));
        Query(app, "/tables", (q, r, p) => q.Tables(p, Branch(q, r, p)));

        Query(app, "/snapshots", (q, r, p) => q.Snapshots(p, Branch(q, r, p), Int(r, "limit", 100)));

        Query(app, "/stats", (q, r, p) => q.Stats(p, Branch(q, r, p), Str(r, "at")));

        Query(app, "/trend", (q, r, p) => q.Trend(
            p, Branch(q, r, p), Str(r, "metric") ?? "rows", Str(r, "table"), Int(r, "limit", 500)));

        Query(app, "/authors", (q, r, p) => q.Authors(
            p, Branch(q, r, p), Str(r, "from"), Str(r, "to")));

        Query(app, "/cell", (q, r, p) => q.CellHistory(
            p, Branch(q, r, p), Str(r, "table"), Str(r, "row"), Str(r, "field"), Int(r, "limit", 200)));

        Query(app, "/diff", (q, r, p) => q.Diff(
            p, Branch(q, r, p), Str(r, "from"), Str(r, "to"),
            Str(r, "table"), Str(r, "field"), Str(r, "author"),
            Int(r, "limit", HistoryQuery.DefaultLimit)));

        Query(app, "/dashboard", (q, r, p) => q.Dashboard(
            p, Branch(q, r, p), Str(r, "from"), Str(r, "to"),
            Str(r, "table"), Str(r, "field"), Str(r, "author"),
            Int(r, "limit", HistoryQuery.DefaultLimit)));

        void Query(WebApplication host, string path, Func<HistoryQuery, HttpRequest, string, object?> answer)
        {
            host.MapGet(ApiPrefix + path, (HttpContext context) =>
            {
                // A connection per request. HistoryQuery holds one and MySQL connections
                // are not concurrent; the pool makes this cheap.
                using var query = HistoryQuery.Open(connectionString);

                query.RepositoryPath = repository;

                string asked = Str(context.Request, "project") ?? project;

                return Json(context, HistoryCommand.Serialize(answer(query, context.Request, asked)!));
            });
        }
    }

    /// <summary>
    /// Turns a failed request into an answer, and writes it down.
    ///
    /// Without this the server was silent in both directions. An unhandled exception -
    /// `--from` naming a commit the history does not hold, say - became a 500 with an
    /// empty body, and because Kestrel's logging providers are cleared so that Serilog is
    /// the only log, ASP.NET's own report of it went nowhere either. So a caller saw a
    /// bare 500 and the operator saw nothing at all.
    ///
    /// A <see cref="TabbitException"/> is the caller's mistake and says so with its
    /// message: an unknown commit, an ambiguous prefix, a range the wrong way round. The
    /// command line prints exactly those sentences, and there is no reason the API should
    /// be less use than `--history` about the same input.
    ///
    /// Anything else is this program's own fault, and the body says only that plus an id
    /// to find it by. What the exception actually said goes to the log, not to the
    /// response - a stack frame or a connection string in an HTTP body is how a read-only
    /// server starts leaking.
    /// </summary>
    private static async Task Report(HttpContext context, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (TabbitException ex)
        {
            Log.Warning($"{context.Request.Path}{context.Request.QueryString}: {ex.Message}");

            await Fail(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            // Enough to match a response to a log line, and nothing an attacker learns
            // anything from.
            string incident = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(
                    context.TraceIdentifier))).Substring(0, 8).ToLowerInvariant();

            Log.Error($"{context.Request.Path}{context.Request.QueryString} failed " +
                      $"[{incident}]: {ex}");

            await Fail(context, StatusCodes.Status500InternalServerError,
                $"The server could not answer this. The log records it as {incident}.");
        }
    }

    /// <summary>
    /// Writes a failure as the same JSON shape every other answer uses.
    /// </summary>
    /// <remarks>
    /// A page fetching these reads one shape whether the answer arrived or not, and a
    /// person with curl gets a sentence rather than a status code on its own.
    ///
    /// Nothing is written when the response has already started, which is the one case
    /// this cannot rescue: the body is part-way out and appending an error object to it
    /// would produce something that parses as neither.
    /// </remarks>
    private static async Task Fail(HttpContext context, int status, string message)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(
            HistoryCommand.Serialize(new { error = message, status }));
    }

    /// <summary>Cookie the browser carries once a query token has been accepted.</summary>
    private const string TokenCookie = "tabbit_token";

    /// <summary>
    /// Checks the token, three ways.
    ///
    /// `Authorization: Bearer` is the one to use, and the only one that leaves no trace:
    /// a header is not written to an access log, does not reach a `Referer`, and is not in
    /// the address bar to be copied into a chat window.
    ///
    /// A `?token=` is accepted because a browser cannot send a header by being pointed at
    /// a URL, and the page has to be openable. It is the worst of the three for exactly
    /// the reasons above, so when one arrives and is right it is moved into an HttpOnly
    /// cookie and the reader is redirected to the same URL without it - the address bar is
    /// clean from the second request on, and the page's own fetches carry the cookie.
    ///
    /// The cookie is the third. Session-scoped, HttpOnly so script cannot read it, and
    /// SameSite=Strict so another site cannot cause a request that uses it.
    ///
    /// None of this is a substitute for TLS. Every one of the three is readable by
    /// anything on the path; put the server behind a reverse proxy that terminates HTTPS
    /// if the network between is not one you own.
    /// </summary>
    private static async Task Authorize(HttpContext context, string token, Func<Task> next)
    {
        // The page and its assets are behind the token too. They carry no data, but a
        // reachable page invites somebody to conclude the port is open to them.
        string header = context.Request.Headers.Authorization.ToString();

        string? fromQuery = context.Request.Query["token"].ToString();

        string? presented =
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header.Substring("Bearer ".Length).Trim()
                : !string.IsNullOrEmpty(fromQuery)
                    ? fromQuery
                    : context.Request.Cookies[TokenCookie];

        if (!FixedTimeEquals(presented, token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.ContentType = "application/json; charset=utf-8";

            await context.Response.WriteAsync(HistoryCommand.Serialize(new
            {
                error = "A bearer token is required. Send it as `Authorization: Bearer <token>`, " +
                        "or open the page once with `?token=<token>`.",
                status = StatusCodes.Status401Unauthorized,
            }));

            return;
        }

        if (!string.IsNullOrEmpty(fromQuery))
        {
            context.Response.Cookies.Append(TokenCookie, fromQuery, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,

                // Not Secure: this listens on plain HTTP, and a Secure cookie would never
                // come back. A deployment behind a TLS proxy wants one, which is a reason
                // to be behind a proxy rather than a reason to set it here and break.
                Secure = false,
            });

            // Only the page is redirected. An API call with `?token=` is somebody's curl
            // line or script, and answering it with a 302 to a URL that then needs the
            // cookie would break it for no gain - the address bar is not the concern
            // there. The token still reaches an access log that way, which is why the
            // header is the documented route.
            if (IsPage(context.Request.Path))
            {
                context.Response.Redirect(WithoutToken(context.Request), permanent: false);
                return;
            }
        }

        await next();
    }

    /// <summary>Whether this path is something a person opened rather than a call.</summary>
    private static bool IsPage(PathString path)
        => !path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The same request's URL with `token` removed from the query.</summary>
    private static string WithoutToken(HttpRequest request)
    {
        var kept = request.Query
                          .Where(pair => !string.Equals(pair.Key, "token", StringComparison.OrdinalIgnoreCase))
                          .SelectMany(pair => pair.Value.Select(value => (pair.Key, Value: value)))
                          .ToList();

        string query = kept.Count == 0
            ? ""
            : "?" + string.Join("&", kept.Select(pair =>
                  Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value ?? "")));

        return request.PathBase + request.Path + query;
    }

    /// <summary>
    /// Compares in time that does not depend on how much of the token matched.
    ///
    /// A plain comparison returns sooner the earlier it finds a difference, which over
    /// enough attempts tells an attacker the token one character at a time.
    /// </summary>
    private static bool FixedTimeEquals(string? presented, string? expected)
    {
        if (string.IsNullOrEmpty(presented))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? "")));
    }

    // -------------------------------------------------------------- responses

    /// <summary>
    /// A JSON answer, with an entity tag.
    ///
    /// Snapshots never change once written, so an answer about a closed range is good
    /// for ever - and the ranges a page asks about again and again are exactly those.
    /// </summary>
    private static IResult Json(HttpContext context, string body)
    {
        string tag = "\"" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(body))).Substring(0, 32).ToLowerInvariant() + "\"";

        if (context.Request.Headers.IfNoneMatch.ToString() == tag)
            return Results.StatusCode(StatusCodes.Status304NotModified);

        context.Response.Headers.ETag = tag;

        return Results.Text(body, "application/json; charset=utf-8");
    }

    private static IResult Html(string body) => Results.Text(body, "text/html; charset=utf-8");

    private static IResult Asset(string name)
        => Results.Text(HistoryView.Asset(name), HistoryView.ContentTypeOf(name));

    // ---------------------------------------------------------------- reading

    private static string? Str(HttpRequest request, string name)
    {
        string value = request.Query[name].ToString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int Int(HttpRequest request, string name, int fallback)
    {
        string? value = Str(request, name);

        if (value is null)
            return fallback;

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw new TabbitException($"`{name}={value}` is not a number.");

        return parsed;
    }

    private static string Branch(HistoryQuery query, HttpRequest request, string project)
        => Str(request, "branch") ?? query.DefaultBranch(project) ?? "";
}
