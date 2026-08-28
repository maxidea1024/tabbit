using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tabbit.Lsp;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What the server answers, from `initialize` to `exit`.
/// </summary>
/// <remarks>
/// Driven through <see cref="LspServer.Handle"/> rather than over a stream: the framing is
/// settled in <see cref="LspFramingTests"/>, and what is left to say here is which message
/// produces which answer.
///
/// **The waiting is turned off.** A quarter of a second between a keystroke and a report is
/// right for a person and is nothing but a source of flakiness for a test, so these run with
/// the directory read again on the spot.
/// </remarks>
public class LspServerTests : IDisposable
{
    private const string Broken = "strcut Reward\n";
    private const string Whole = "struct Reward\n    field itemId int\n";

    private readonly string _directory;
    private readonly List<JsonDocument> _sent = [];

    public LspServerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "tabbit-lsp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        foreach (var message in _sent)
            message.Dispose();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot tidy up after itself is still a test that ran.
        }
    }

    private LspServer Server()
        => new(message => _sent.Add(JsonDocument.Parse(
            JsonSerializer.SerializeToUtf8Bytes(message, JsonRpcConnection.Json))),
            debounceMilliseconds: 0);

    /// <summary>
    /// Hands the server one message, built rather than written out.
    /// </summary>
    /// <remarks>
    /// Serialized from an object so that the schema text inside - which is full of newlines
    /// and quotes - is escaped by something that knows how, instead of by hand.
    /// </remarks>
    private static void Send(LspServer server, object message)
    {
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(message));
        server.Handle(parsed);
    }

    private static void Open(LspServer server, string uri, string text) => Send(server, new
    {
        jsonrpc = "2.0",
        method = "textDocument/didOpen",
        @params = new { textDocument = new { uri, languageId = "tbs", version = 1, text } },
    });

    private static void Change(LspServer server, string uri, string text) => Send(server, new
    {
        jsonrpc = "2.0",
        method = "textDocument/didChange",
        @params = new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new[] { new { text } },
        },
    });

    private static void Close(LspServer server, string uri) => Send(server, new
    {
        jsonrpc = "2.0",
        method = "textDocument/didClose",
        @params = new { textDocument = new { uri } },
    });

    /// <summary>
    /// Writes a file and answers the URI a client would name it by.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="Uri.AbsoluteUri"/>, which spells a drive letter `C:` where an
    /// editor spells it `c%3A`. A file the client opens must be published back under the
    /// spelling the client used, and this is what proves it - the file it never opened is
    /// checked against the other spelling by <see cref="UriForUnopened"/>.
    /// </remarks>
    private string Write(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>The URI a file nobody opened is published under.</summary>
    private string UriForUnopened(string name)
        => DocumentStore.UriFor(Path.Combine(_directory, name));

    /// <summary>Every report published about one file, from the last message that named it.</summary>
    private JsonElement ReportsAbout(string uri)
    {
        var last = _sent
            .Where(message => message.RootElement.TryGetProperty("method", out var method)
                && method.GetString() == "textDocument/publishDiagnostics"
                && message.RootElement.GetProperty("params").GetProperty("uri").GetString() == uri)
            .LastOrDefault();

        Assert.True(last is not null, $"Nothing was published about {uri}.");
        return last!.RootElement.GetProperty("params").GetProperty("diagnostics");
    }

    private static IEnumerable<string> CodesIn(JsonElement reports)
        => reports.EnumerateArray().Select(report => report.GetProperty("code").GetString() ?? "");

    // ------------------------------------------------------------------ paths and URIs

    [Fact]
    public void A_drive_letter_is_understood_however_the_client_spells_it()
    {
        string path = DocumentStore.Normalize(Path.Combine(_directory, "spelled.tbs"));

        // The two spellings a client may send. `Uri.LocalPath` reads the second as `/c:/...`
        // and resolves it against the current drive - which is how every file in a workspace
        // came out as `C:\c:\...` and no directory could be read at all.
        Assert.Equal(path, DocumentStore.PathOf(new Uri(path).AbsoluteUri));
        Assert.Equal(path, DocumentStore.PathOf(DocumentStore.UriFor(path)));
    }

    [Fact]
    public void A_file_the_editor_opened_is_published_under_the_clients_own_spelling()
    {
        string uri = Write("echoed.tbs", Broken);

        using var server = Server();
        Open(server, uri, Broken);

        // Answered with the URI that was sent, not with this server's way of writing one. A
        // URI that differs by a character is a file the client does not recognise.
        Assert.NotEmpty(ReportsAbout(uri).EnumerateArray());
    }

    // ------------------------------------------------------------------ lifecycle

    [Fact]
    public void Initialize_says_what_the_server_can_do()
    {
        using var server = Server();

        Send(server, new { jsonrpc = "2.0", id = 1, method = "initialize" });

        var answer = _sent.Single().RootElement;

        Assert.Equal(1, answer.GetProperty("id").GetInt32());

        // 1 is full synchronization, which is what the document store is written for.
        Assert.Equal(1, answer.GetProperty("result")
            .GetProperty("capabilities").GetProperty("textDocumentSync").GetInt32());
    }

    [Fact]
    public void Shutdown_is_answered_and_exit_stops_the_loop()
    {
        using var server = Server();

        Assert.True(server.Running);

        Send(server, new { jsonrpc = "2.0", id = 2, method = "shutdown" });

        // A successful answer of nothing, which still has to be written as one.
        Assert.Equal(JsonValueKind.Null, _sent.Single().RootElement.GetProperty("result").ValueKind);

        Send(server, new { jsonrpc = "2.0", method = "exit" });
        Assert.False(server.Running);
    }

    [Fact]
    public void A_request_nobody_handles_is_refused_and_a_notification_is_dropped()
    {
        using var server = Server();

        Send(server, new { jsonrpc = "2.0", id = 3, method = "textDocument/rename" });

        Assert.Equal(-32601, _sent.Single().RootElement
            .GetProperty("error").GetProperty("code").GetInt32());

        // A notification is never answered, whatever it asks for - answering one is itself a
        // protocol mistake.
        Send(server, new { jsonrpc = "2.0", method = "$/setTrace" });
        Assert.Single(_sent);
    }

    // ------------------------------------------------------------------ diagnostics

    [Fact]
    public void Opening_a_file_reports_what_is_wrong_with_it()
    {
        string uri = Write("broken.tbs", Broken);

        using var server = Server();
        Open(server, uri, Broken);

        var only = Assert.Single(ReportsAbout(uri).EnumerateArray());

        Assert.Equal("schema.unknown-keyword", only.GetProperty("code").GetString());
        Assert.Equal(1, only.GetProperty("severity").GetInt32());
        Assert.Equal("tabbit", only.GetProperty("source").GetString());
    }

    [Fact]
    public void The_word_is_underlined_and_not_the_line()
    {
        string uri = Write("ranged.tbs", Broken);

        using var server = Server();
        Open(server, uri, Broken);

        var range = Assert.Single(ReportsAbout(uri).EnumerateArray()).GetProperty("range");

        // `strcut` is six characters at the start of the first line. Both ends of the range
        // come from the token, which is what section 6.1 of the spec is about.
        Assert.Equal(0, range.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(0, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(0, range.GetProperty("end").GetProperty("line").GetInt32());
        Assert.Equal(6, range.GetProperty("end").GetProperty("character").GetInt32());
    }

    [Fact]
    public void Fixing_the_text_publishes_an_empty_list()
    {
        string uri = Write("mending.tbs", Broken);

        using var server = Server();
        Open(server, uri, Broken);

        Assert.NotEmpty(ReportsAbout(uri).EnumerateArray());

        Change(server, uri, Whole);

        // Told that there is nothing left, rather than left with the old underline in place.
        Assert.Empty(ReportsAbout(uri).EnumerateArray());
    }

    [Fact]
    public void The_unsaved_buffer_is_what_is_read()
    {
        string uri = Write("buffer.tbs", Whole);

        using var server = Server();
        Open(server, uri, Broken);

        // What is on disk parses. What the author is looking at does not, and that is the one
        // worth reporting.
        Assert.Contains("schema.unknown-keyword", CodesIn(ReportsAbout(uri)));
    }

    [Fact]
    public void Closing_a_file_goes_back_to_what_is_on_disk()
    {
        string uri = Write("closing.tbs", Whole);

        using var server = Server();
        Open(server, uri, Broken);

        Assert.NotEmpty(ReportsAbout(uri).EnumerateArray());

        Close(server, uri);
        Assert.Empty(ReportsAbout(uri).EnumerateArray());
    }

    // ------------------------------------------------------------- the unit of checking

    [Fact]
    public void A_name_declared_in_two_files_of_one_directory_is_reported()
    {
        string first = Write("first.tbs", Whole);
        Write("second.tbs", "struct Reward\n    field count int\n");

        using var server = Server();
        Open(server, first, Whole);

        // The folder is one set, so the clash is found without the second file being opened -
        // and it is reported against the file that declared the name second.
        Assert.Contains("schema.declared-twice", CodesIn(ReportsAbout(UriForUnopened("second.tbs"))));
    }

    [Fact]
    public void A_directory_is_checked_apart_from_its_neighbour()
    {
        string here = Write("here.tbs", Whole);

        string elsewhere = Path.Combine(_directory, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(
            Path.Combine(elsewhere, "there.tbs"), "struct Reward\n    field count int\n");

        using var server = Server();
        Open(server, here, Whole);

        // Two folders are two recipes' worth of declarations. Reading them as one set is what
        // would report this pair as a name declared twice, and it is not one - section 4.2 of
        // spec/ops/lsp.md.
        Assert.Empty(ReportsAbout(here).EnumerateArray());
    }
}
