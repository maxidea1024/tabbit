using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// What one piece of a schema file is.
/// </summary>
/// <remarks>
/// Small on purpose. The grammar has no expressions, so there is nothing here for operators
/// or precedence to be about - every member below is either a name, a value, or one of the
/// eleven characters the notation spells structure with.
///
/// <see cref="EndOfLine"/> is a token rather than skipped whitespace because a declaration is
/// a line: what ends one is what says the next word begins a new declaration rather than
/// continuing this one. That is also what keeps the parser from needing to look ahead.
/// </remarks>
internal enum SchemaTokenKind
{
    /// <summary>A run of characters that is not punctuation - a keyword, a name, a number.</summary>
    Word,

    /// <summary>The contents of a quoted string, quotes removed and escapes resolved.</summary>
    String,

    /// <summary>One `///` line, the marker and one following space removed.</summary>
    DocComment,

    /// <summary>`@`, which introduces a wire tag.</summary>
    At,

    /// <summary>`=`, which introduces a value.</summary>
    Equals,

    /// <summary>`,`, between metadata entries and between a map's key and value types.</summary>
    Comma,

    /// <summary>`?`, which says a value may be absent.</summary>
    Question,

    /// <summary>`|`, between the tables a reference may point at.</summary>
    Pipe,

    OpenParen,
    CloseParen,

    /// <summary>`[`, which with its closer says a type is an array.</summary>
    OpenBracket,
    CloseBracket,

    /// <summary>`&lt;`, which opens a container type's arguments.</summary>
    OpenAngle,
    CloseAngle,

    /// <summary>The end of a declaration.</summary>
    EndOfLine,

    /// <summary>The end of the file. Always the last token, and always present.</summary>
    EndOfFile,
}

/// <summary>
/// One token and where it was written.
/// </summary>
/// <remarks>
/// The position is carried as a line and a column rather than as a <see cref="Location"/>,
/// so that lexing a file allocates one object per file rather than one per token. The parser
/// builds the location for the tokens it actually reports about.
/// </remarks>
internal readonly struct SchemaToken
{
    public SchemaToken(SchemaTokenKind kind, string text, int line, int column, int endColumn)
    {
        Kind = kind;
        Text = text;
        Line = line;
        Column = column;
        EndColumn = endColumn;
    }

    public SchemaTokenKind Kind { get; }

    /// <summary>
    /// What was written, for the kinds that carry text. Empty for punctuation, which is
    /// already said by <see cref="Kind"/>.
    /// </summary>
    public string Text { get; }

    /// <summary>Line it starts on, counted from one as an editor counts.</summary>
    public int Line { get; }

    /// <summary>Column it starts at, counted from one.</summary>
    public int Column { get; }

    /// <summary>
    /// The column just past this token, so that two tokens are adjacent when one ends where
    /// the next begins.
    /// </summary>
    /// <remarks>
    /// Carried for one reason: a metadata value is written unquoted up to a comma, a closing
    /// bracket or a space, and several of the notations that value may hold - `A|B`,
    /// `^[a-z]+$` - are more than one token here. Rebuilding one means knowing whether the
    /// tokens were written touching, because a space in the middle is the end of the value
    /// and the start of a mistake.
    /// </remarks>
    public int EndColumn { get; }

    public override string ToString() => Kind switch
    {
        SchemaTokenKind.Word => Text,
        SchemaTokenKind.String => "\"" + Text + "\"",
        SchemaTokenKind.DocComment => "///" + Text,
        SchemaTokenKind.At => "@",
        SchemaTokenKind.Equals => "=",
        SchemaTokenKind.Comma => ",",
        SchemaTokenKind.Question => "?",
        SchemaTokenKind.Pipe => "|",
        SchemaTokenKind.OpenParen => "(",
        SchemaTokenKind.CloseParen => ")",
        SchemaTokenKind.OpenBracket => "[",
        SchemaTokenKind.CloseBracket => "]",
        SchemaTokenKind.OpenAngle => "<",
        SchemaTokenKind.CloseAngle => ">",
        SchemaTokenKind.EndOfLine => "end of line",
        _ => "end of file",
    };
}
