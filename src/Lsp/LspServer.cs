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

            case "textDocument/definition":
                Respond(id, Definition(arguments));
                break;

            case "textDocument/hover":
                Respond(id, Hover(arguments));
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
            definitionProvider = true,
            hoverProvider = true,
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

    /// <summary>Where the name under the cursor was declared, or nothing.</summary>
    private object? Definition(JsonElement arguments)
    {
        if (!TryPosition(arguments, out var found, out var index))
            return null;

        var declared = index.DefinitionOf(found);

        return declared is null
            ? null
            : new LspLocation(_documents.UriOf(declared.Value.Path), declared.Value.Range);
    }

    /// <summary>What to show beside the cursor, or nothing.</summary>
    private object? Hover(JsonElement arguments)
    {
        if (!TryPosition(arguments, out var found, out var index))
            return null;

        string? said = index.HoverOf(found);

        return said is null ? null : new LspHover(new MarkupContent("markdown", said), found.Range);
    }

    /// <summary>
    /// Finds what a request is pointing at.
    /// </summary>
    /// <remarks>
    /// The directory is read again here if a keystroke is still waiting, so that the answer is
    /// about the text on the screen rather than the text of a moment ago.
    /// </remarks>
    private bool TryPosition(JsonElement arguments, out Occurrence found, out SchemaIndex index)
    {
        found = null!;
        index = null!;

        if (!TryDocument(arguments, "textDocument", out string uri)
            || !arguments.TryGetProperty("position", out var position)
            || !position.TryGetProperty("line", out var line)
            || !position.TryGetProperty("character", out var character))
        {
            return false;
        }

        string path = DocumentStore.PathOf(uri);
        index = _workspace.AnalysisFor(path).Index;

        var written = index.At(path, line.GetInt32(), character.GetInt32());

        if (written is null)
            return false;

        found = written;
        return true;
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

    /// <summary>
    /// Answers a request, and says nothing to anything that was not one.
    /// </summary>
    /// <remarks>
    /// A message with no id is a notification however it is spelled - `initialize` sent
    /// without one included. Answering it would be a protocol mistake, and cloning the id
    /// that is not there would throw.
    /// </remarks>
    private void Respond(JsonElement id, object? result)
    {
        if (id.ValueKind != JsonValueKind.Undefined)
            _send(new ResponseMessage { Id = id.Clone(), Result = result });
    }

    private void RespondError(JsonElement id, int code, string message)
    {
        if (id.ValueKind != JsonValueKind.Undefined)
        {
            _send(new ErrorResponseMessage
            {
                Id = id.Clone(),
                Error = new ResponseError(code, message),
            });
        }
    }

    public void Dispose() => _workspace.Dispose();
}
