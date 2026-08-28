using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabbit.Lsp;

/// <summary>
/// The pieces of the protocol this server sends, and only those.
/// </summary>
/// <remarks>
/// Written out rather than taken from a package. What is needed is a dozen shapes with no
/// behaviour, and a package that supplies them brings a dependency tree with it - which is
/// what section 1 of spec/ops/lsp.md decided against.
/// </remarks>
internal sealed record Position(int Line, int Character);

internal sealed record LspRange(Position Start, Position End);

/// <summary>A place in a file, which is what "go to definition" answers with.</summary>
internal sealed record LspLocation(string Uri, LspRange Range);

/// <summary>One report, as the editor underlines it.</summary>
internal sealed record LspDiagnostic
{
    public required LspRange Range { get; init; }

    /// <summary>1 is an error, 2 a warning, 3 information.</summary>
    public required int Severity { get; init; }

    /// <summary>The report's stable id - `schema.something` - or null for the few without one.</summary>
    public string? Code { get; init; }

    public string Source { get; init; } = "tabbit";

    public required string Message { get; init; }
}

internal sealed record PublishDiagnosticsParams(
    string Uri, IReadOnlyList<LspDiagnostic> Diagnostics);

internal sealed record MarkupContent(string Kind, string Value);

internal sealed record LspHover(MarkupContent Contents, LspRange? Range);

/// <summary>A message the server sends without being asked.</summary>
internal sealed record NotificationMessage
{
    public string Jsonrpc { get; init; } = "2.0";

    public required string Method { get; init; }

    public required object Params { get; init; }
}

/// <summary>An answer to a request.</summary>
internal sealed record ResponseMessage
{
    public string Jsonrpc { get; init; } = "2.0";

    public required JsonElement Id { get; init; }

    /// <summary>
    /// Written even when it is null, which is why it says so here.
    /// </summary>
    /// <remarks>
    /// "There is no definition at that position" is a successful answer of null. Leaving the
    /// member out instead produces a response with neither a result nor an error, which some
    /// clients treat as a protocol failure and stop talking after.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Result { get; init; }
}

/// <summary>A refusal of a request. Separate from the answer so the two never travel together.</summary>
internal sealed record ErrorResponseMessage
{
    public string Jsonrpc { get; init; } = "2.0";

    public required JsonElement Id { get; init; }

    public required ResponseError Error { get; init; }
}

internal sealed record ResponseError(int Code, string Message);
