using System;
using System.Collections.Generic;
using Tabbit.Schema;

namespace Tabbit.Lsp;

/// <summary>
/// Where the underline under a report should stop.
/// </summary>
/// <remarks>
/// **<see cref="Models.Location"/> holds a starting point and nothing else**, and it is not
/// extended to hold an end: it is serialized into history records, into the comments of
/// generated code and into build reports, so widening it moves output that has nothing to do
/// with an editor. Section 6.1 of spec/ops/lsp.md.
///
/// Instead the file is run through the lexer a second time, with its reports thrown away, and
/// the token that begins where the report begins says where it ends. Reports are made about
/// tokens, so this finds one nearly always; the exceptions are the reports about a blank line
/// or about the end of the file, which get the line instead.
/// </remarks>
internal sealed class TokenRanges
{
    private readonly Dictionary<(int Line, int Character), int> _ends = [];
    private readonly string[] _lines;

    private TokenRanges(string text, string path)
    {
        _lines = text.Replace("\r\n", "\n").Split('\n');

        // The lexer's own reports are already collected by the round that parsed this file.
        // Collecting them again here would publish every one of them twice.
        var ignored = new Diagnostics();

        foreach (var token in SchemaLexer.Read(text, path, ignored))
        {
            // Tokens count from one and the protocol counts from zero.
            var starts = (token.Line - 1, token.Column - 1);

            // The first token to claim a position keeps it. Two never share one.
            if (!_ends.ContainsKey(starts))
                _ends[starts] = token.EndColumn - 1;
        }
    }

    public static TokenRanges Of(string text, string path) => new(text, path);

    /// <summary>The range to underline for a report made at this place.</summary>
    public LspRange RangeAt(Models.Location where)
    {
        int line = Math.Max(0, where.Row);
        int character = Math.Max(0, where.Column);

        if (_ends.TryGetValue((line, character), out int end) && end > character)
            return new LspRange(new Position(line, character), new Position(line, end));

        // Nothing starts here. The rest of the line is the smallest thing that certainly
        // contains what the report is about.
        int width = line >= 0 && line < _lines.Length ? _lines[line].Length : character;

        return new LspRange(
            new Position(line, character),
            new Position(line, Math.Max(character + 1, width)));
    }
}
