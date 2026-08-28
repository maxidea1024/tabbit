using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Tabbit.Lsp;

/// <summary>
/// Answers what the editor asks about `.tbs` files.
/// </summary>
/// <remarks>
/// **The parser is the one in <see cref="Schema"/>, unchanged.** Nothing here judges a schema;
/// it moves what that parser already reports into the shapes the protocol names. A second
/// judgement living in an editor is exactly what section 27 of notes/struct-dsl-review.md
/// refused.
///
/// Split from <see cref="JsonRpcConnection"/> by an <see cref="Action{T}"/> so that a test can
/// hand it messages and collect what it sends without a stream in between.
/// </remarks>
internal sealed class LspServer : IDisposable
{
    private readonly Action<object> _send;
    private readonly DocumentStore _documents = new();
    private readonly SchemaWorkspace _workspace;

    public LspServer(Action<object> send, int debounceMilliseconds = 250)
    {
        _send = send;
        _workspace = new SchemaWorkspace(_documents, PublishDiagnostics, debounceMilliseconds);
    }

    /// <summary>False once the client has said `exit`.</summary>
    public bool Running { get; private set; } = true;

    /// <summary>Takes one message and does what it asks.</summary>
    public void Handle(JsonDocument message)
    {
        var root = message.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return;

        // No method means this is an answer to something we asked. We ask nothing, so there
        // is nothing this could be an answer to.
        if (!root.TryGetProperty("method", out var named) || named.GetString() is not string method)
            return;

        bool isRequest = root.TryGetProperty("id", out var id);
        var arguments = root.TryGetProperty("params", out var given) ? given : default;

        switch (method)
        {
            case "initialize":
                Respond(id, Capabilities());
                break;

            case "shutdown":
                Respond(id, null);
                break;

            case "exit":
                Running = false;
                break;

            case "initialized":
                break;

            case "textDocument/didOpen":
                DidOpen(arguments);
                break;

            case "textDocument/didChange":
                DidChange(arguments);
                break;

            case "textDocument/didSave":
                Touch(arguments, "textDocument");
                break;

            case "textDocument/didClose":
                DidClose(arguments);
                break;

            case "workspace/didChangeWatchedFiles":
                DidChangeWatchedFiles(arguments);
                break;

            default:
                // A request has to be answered even when the answer is "no such method". A
                // notification must not be, and `$/` ones the specification says to drop.
                if (isRequest)
                    RespondError(id, -32601, $"Unhandled method: {method}");

                break;
        }
    }

    /// <summary>What this server can do, which is what `initialize` asks.</summary>
    private static object Capabilities() => new
    {
        capabilities = new
        {
            // 1 is full synchronization: every change carries the whole document. The parser
            // reads whole files anyway, so keeping an incremental copy would buy nothing and
            // cost the offset arithmetic. Section 4.1 of spec/ops/lsp.md.
            textDocumentSync = 1,
        },
        serverInfo = new { name = "tabbit", version = ToolVersion.Current },
    };

    private void DidOpen(JsonElement arguments)
    {
        if (!TryDocument(arguments, "textDocument", out string uri))
            return;

        string text = arguments.GetProperty("textDocument").TryGetProperty("text", out var held)
            ? held.GetString() ?? ""
            : "";

        _documents.Open(uri, text);
        _workspace.Touched(DocumentStore.PathOf(uri), immediate: true);
    }

    /// <summary>
    /// Takes the whole new text of a document.
    /// </summary>
    /// <remarks>
    /// Full synchronization means one change carrying everything, so the last entry is the
    /// document. Read as the last rather than the only one so that a client sending it as a
    /// single-element list with a range attached is still understood.
    /// </remarks>
    private void DidChange(JsonElement arguments)
    {
        if (!TryDocument(arguments, "textDocument", out string uri))
            return;

        if (!arguments.TryGetProperty("contentChanges", out var changes)
            || changes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        string? text = null;

        foreach (var change in changes.EnumerateArray())
        {
            if (change.TryGetProperty("text", out var held))
                text = held.GetString();
        }

        if (text is null)
            return;

        _documents.Open(uri, text);
        _workspace.Touched(DocumentStore.PathOf(uri), immediate: false);
    }

    private void DidClose(JsonElement arguments)
    {
        if (!TryDocument(arguments, "textDocument", out string uri))
            return;

        // Dropped from the open set first, so the round below reads what is on disk - which
        // is what everybody else's build will read.
        _documents.Close(uri);
        _workspace.Touched(DocumentStore.PathOf(uri), immediate: true);
    }

    private void Touch(JsonElement arguments, string member)
    {
        if (TryDocument(arguments, member, out string uri))
            _workspace.Touched(DocumentStore.PathOf(uri), immediate: true);
    }

    /// <summary>A `.tbs` file changed outside the editor - a branch switch, another tool.</summary>
    private void DidChangeWatchedFiles(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("changes", out var changes)
            || changes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes.EnumerateArray())
        {
            if (!change.TryGetProperty("uri", out var named) || named.GetString() is not string uri)
                continue;

            try
            {
                directories.Add(DocumentStore.PathOf(uri));
            }
            catch (UriFormatException)
            {
                // Not something this server can name a file from. Nothing to re-read.
            }
        }

        foreach (string path in directories)
            _workspace.Touched(path, immediate: true);
    }

    private static bool TryDocument(JsonElement arguments, string member, out string uri)
    {
        uri = "";

        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(member, out var document)
            || !document.TryGetProperty("uri", out var named)
            || named.GetString() is not string found)
        {
            return false;
        }

        uri = found;
        return true;
    }

    private void PublishDiagnostics(string uri, IReadOnlyList<LspDiagnostic> reports)
        => _send(new NotificationMessage
        {
            Method = "textDocument/publishDiagnostics",
            Params = new PublishDiagnosticsParams(uri, reports),
        });

    private void Respond(JsonElement id, object? result)
        => _send(new ResponseMessage { Id = id.Clone(), Result = result });

    private void RespondError(JsonElement id, int code, string message)
        => _send(new ErrorResponseMessage
        {
            Id = id.Clone(),
            Error = new ResponseError(code, message),
        });

    public void Dispose() => _workspace.Dispose();
}
