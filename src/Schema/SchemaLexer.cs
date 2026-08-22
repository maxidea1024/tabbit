using System.Collections.Generic;
using Tabbit.Messages;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// Turns the text of a schema file into tokens.
/// </summary>
/// <remarks>
/// **The lexer has no state beyond its position.** Whether a `(` opens metadata or a `?` says
/// a value may be absent is a question about where in a declaration it stands, and that is
/// the parser's to answer - which is what keeps a token's meaning from depending on the
/// tokens before it. The one exception is a quoted string, and it exists so that a metadata
/// value may hold a comma: `(regex="^a,b$")`. Section 4.2 of the design says why that is the
/// whole of the escaping rule.
///
/// A word is anything that is not whitespace and not one of the eleven punctuation
/// characters, so `1..3`, `a;b;c` and `-1.5` all arrive as one token and nothing here has to
/// know which of them is a number. What a word means is settled where it is read.
///
/// Reports are collected rather than thrown. A file with two unterminated strings in it
/// should say so twice.
/// </remarks>
internal static class SchemaLexer
{
    /// <summary>Reads a file's text, reporting anything it cannot make a token of.</summary>
    public static List<SchemaToken> Read(string source, string path, Diagnostics diagnostics)
    {
        var tokens = new List<SchemaToken>();

        int at = 0;
        int line = 1;
        int lineStart = 0;

        while (at < source.Length)
        {
            char here = source[at];

            if (here == '\r')
            {
                at++;
                continue;
            }

            if (here == '\n')
            {
                tokens.Add(EmptyAt(SchemaTokenKind.EndOfLine, line, at - lineStart + 1));
                at++;
                line++;
                lineStart = at;
                continue;
            }

            if (here == ' ' || here == '\t')
            {
                at++;
                continue;
            }

            if (here == '/' && at + 1 < source.Length && source[at + 1] == '/')
            {
                // Three slashes is a doc comment and two is a note to whoever is editing the
                // file. Only the first survives this program, which is section 5 of the
                // design: an implementation note has no business in generated code.
                bool isDoc = at + 2 < source.Length && source[at + 2] == '/'
                             && (at + 3 >= source.Length || source[at + 3] != '/');

                int textStart = at + (isDoc ? 3 : 2);
                int end = textStart;
                while (end < source.Length && source[end] != '\n' && source[end] != '\r')
                    end++;

                if (isDoc)
                {
                    // One leading space is the space after the marker rather than indentation
                    // of the prose, so it goes. Any further space is the author's.
                    string text = source.Substring(textStart, end - textStart);
                    if (text.StartsWith(" "))
                        text = text.Substring(1);

                    tokens.Add(new SchemaToken(
                        SchemaTokenKind.DocComment, text.TrimEnd(),
                        line, at - lineStart + 1, end - lineStart + 1));
                }

                at = end;
                continue;
            }

            if (here == '/' && at + 1 < source.Length && source[at + 1] == '*')
            {
                int openLine = line;
                int openColumn = at - lineStart + 1;

                at += 2;
                bool closed = false;

                while (at < source.Length)
                {
                    if (source[at] == '*' && at + 1 < source.Length && source[at + 1] == '/')
                    {
                        at += 2;
                        closed = true;
                        break;
                    }

                    if (source[at] == '\n')
                    {
                        // A declaration is a line, so a comment that crosses a newline does
                        // not join the lines either side of it. Ending the line here is what
                        // keeps that true - and what makes a `/*` somebody forgot to close
                        // report as an unterminated comment rather than as one enormous
                        // declaration.
                        tokens.Add(EmptyAt(SchemaTokenKind.EndOfLine, line, at - lineStart + 1));
                        line++;
                        lineStart = at + 1;
                    }

                    at++;
                }

                if (!closed)
                {
                    diagnostics.Error(
                        Location.OfTextFile(path, openLine, openColumn),
                        Message.Of(SchemaMessages.BlockCommentUnterminated));
                }

                continue;
            }

            if (here == '"')
            {
                int openColumn = at - lineStart + 1;
                var text = new System.Text.StringBuilder();

                at++;
                bool closed = false;

                while (at < source.Length && source[at] != '\n' && source[at] != '\r')
                {
                    if (source[at] == '\\' && at + 1 < source.Length
                        && (source[at + 1] == '"' || source[at + 1] == '\\'))
                    {
                        text.Append(source[at + 1]);
                        at += 2;
                        continue;
                    }

                    if (source[at] == '"')
                    {
                        at++;
                        closed = true;
                        break;
                    }

                    text.Append(source[at]);
                    at++;
                }

                if (!closed)
                {
                    diagnostics.Error(
                        Location.OfTextFile(path, line, openColumn),
                        Message.Of(SchemaMessages.StringUnterminated));
                }

                tokens.Add(new SchemaToken(
                    SchemaTokenKind.String, text.ToString(),
                    line, openColumn, at - lineStart + 1));
                continue;
            }

            var punctuation = PunctuationOf(here);
            if (punctuation is { } kind)
            {
                tokens.Add(new SchemaToken(
                    kind, "", line, at - lineStart + 1, at - lineStart + 2));
                at++;
                continue;
            }

            if (IsWordCharacter(source, at))
            {
                int start = at;
                while (at < source.Length && IsWordCharacter(source, at))
                    at++;

                tokens.Add(new SchemaToken(
                    SchemaTokenKind.Word,
                    source.Substring(start, at - start),
                    line,
                    start - lineStart + 1,
                    at - lineStart + 1));
                continue;
            }

            diagnostics.Error(
                Location.OfTextFile(path, line, at - lineStart + 1),
                Message.Of(SchemaMessages.CharacterUnexpected, ("Character", here.ToString())));
            at++;
        }

        // Always closed, whether or not the file ends in a newline, so the parser can read a
        // declaration the same way wherever it sits in the file.
        tokens.Add(EmptyAt(SchemaTokenKind.EndOfLine, line, at - lineStart + 1));
        tokens.Add(EmptyAt(SchemaTokenKind.EndOfFile, line, at - lineStart + 1));

        return tokens;
    }

    /// <summary>A token that spans nothing - the end of a line or of the file.</summary>
    private static SchemaToken EmptyAt(SchemaTokenKind kind, int line, int column)
        => new SchemaToken(kind, "", line, column, column);

    private static SchemaTokenKind? PunctuationOf(char character) => character switch
    {
        '@' => SchemaTokenKind.At,
        '=' => SchemaTokenKind.Equals,
        ',' => SchemaTokenKind.Comma,
        '?' => SchemaTokenKind.Question,
        '|' => SchemaTokenKind.Pipe,
        '(' => SchemaTokenKind.OpenParen,
        ')' => SchemaTokenKind.CloseParen,
        '[' => SchemaTokenKind.OpenBracket,
        ']' => SchemaTokenKind.CloseBracket,
        '<' => SchemaTokenKind.OpenAngle,
        '>' => SchemaTokenKind.CloseAngle,
        _ => null,
    };

    /// <summary>
    /// Whether the character at this position belongs to a word.
    /// </summary>
    /// <remarks>
    /// Everything that is not whitespace, not punctuation and not the start of a comment.
    /// A slash is a word character right up until it is the first of a comment marker, so a
    /// metadata value may hold one - `(x.path=art/icons)` - without the notation needing a
    /// quote for it.
    /// </remarks>
    private static bool IsWordCharacter(string source, int at)
    {
        char character = source[at];

        if (character == ' ' || character == '\t' || character == '\n' || character == '\r')
            return false;

        if (character == '"' || PunctuationOf(character) is not null)
            return false;

        if (character == '/' && at + 1 < source.Length
            && (source[at + 1] == '/' || source[at + 1] == '*'))
            return false;

        return true;
    }
}
