using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Lays the emitted Go out the way `gofmt` would.
/// </summary>
/// <remarks>
/// Go is the one target whose formatting is not a matter of taste. `gofmt` has no options,
/// every editor runs it on save, and a repository that checks formatting in CI fails on a
/// file this tool wrote. Emitting one space where `gofmt` wants a column meant the generated
/// tree came out already dirty - 93 files of it in one sample - and the consumer's first
/// save rewrote files they are told not to edit.
///
/// A template cannot do this. Alignment needs the width of the widest name in a run, and a
/// run is not known until it ends; Scriban renders forward one line at a time. So the text
/// is laid out after rendering, in one pass over the lines.
///
/// What `gofmt` aligns is narrower than it looks, and the difference matters - aligning a
/// statement would be a change no formatter asked for:
///
///   - field lists inside `struct { }`
///   - specifications inside `const ( )` and `var ( )`
///   - keyed elements of a composite literal, `Name: value,`
///
/// Runs of ordinary statements are left alone, which is why the field cases are recognised
/// by the block they are in rather than by their shape. A blank line or a comment line ends
/// a run, and so does a line with a different number of cells - both are `text/tabwriter`'s
/// rules, which is what `gofmt` formats through.
/// </remarks>
internal static class GoLayout
{
    /// <summary>
    /// The emitted text, aligned and with its blank lines collapsed.
    /// </summary>
    public static string Formatted(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        List<string> lines = Collapsed(text.Split('\n'));

        Align(lines);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// What kind of block a line sits in, which is what says whether it can be aligned.
    /// </summary>
    private enum Block
    {
        /// <summary>Anything else - a function body, the file itself.</summary>
        Other,

        /// <summary>Between `struct {` and its `}`.</summary>
        Struct,

        /// <summary>Between `const (` or `var (` and its `)`.</summary>
        Spec,
    }

    /// <summary>
    /// One line broken into the cells a formatter would align, or null where it has none.
    /// </summary>
    private sealed record Cells(string Indent, string[] Parts);

    // ------------------------------------------------------------------ blank lines

    /// <summary>
    /// Collapses runs of blank lines to one.
    /// </summary>
    /// <remarks>
    /// `gofmt` keeps at most one blank line between declarations, and the templates leave
    /// two wherever an optional section did not render. The last line is left alone: the
    /// file ends with a newline, so splitting gives a trailing empty entry that is the
    /// ending rather than a blank line.
    /// </remarks>
    private static List<string> Collapsed(string[] lines)
    {
        var kept = new List<string>(lines.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            bool last = i == lines.Length - 1;

            if (!last
                && lines[i].Length == 0
                && kept.Count > 0
                && kept[^1].Length == 0)
            {
                continue;
            }

            kept.Add(lines[i]);
        }

        return kept;
    }

    // ------------------------------------------------------------------ alignment

    /// <summary>
    /// Pads every alignable run in place.
    /// </summary>
    private static void Align(List<string> lines)
    {
        var blocks = new Stack<Block>();
        var run = new List<(int Line, Cells Cells)>();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            // A closing bracket leaves its block before the line is judged, an opening one
            // enters after - so the line that opens a struct is not itself a field.
            bool closes = Closes(trimmed, Innermost(blocks));

            if (closes)
                blocks.Pop();

            Cells? cells = closes ? null : CellsOf(line, trimmed, Innermost(blocks));

            if (cells is null || run.Count > 0 && !SameShape(run[0].Cells, cells))
            {
                Pad(lines, run);
                run.Clear();
            }

            if (cells is not null)
                run.Add((i, cells));

            Block? opened = Opened(trimmed);

            if (opened is not null)
                blocks.Push(opened.Value);
        }

        Pad(lines, run);
    }

    /// <summary>
    /// Whether a line closes the block it is in.
    /// </summary>
    /// <remarks>
    /// A parenthesis closes only a specification block, because only `const (` and `var (`
    /// open one. Nothing else does: a call broken over lines ends with an open parenthesis
    /// that no later line begins with, and counting those would leave the stack describing
    /// a block the reader is no longer in.
    /// </remarks>
    private static bool Closes(string trimmed, Block block)
    {
        if (trimmed.StartsWith('}'))
            return block is Block.Struct or Block.Other;

        return trimmed.StartsWith(')') && block is Block.Spec;
    }

    private static Block Innermost(Stack<Block> blocks)
        => blocks.Count > 0 ? blocks.Peek() : Block.Other;

    /// <summary>
    /// The block a line opens, or null where it opens none.
    /// </summary>
    /// <remarks>
    /// Read off the end of the line, which is where the generated code puts every opener.
    /// A line that both closes and opens - `}{`, the composite literal that follows an
    /// anonymous struct type - is handled by the caller popping before this is asked.
    /// </remarks>
    private static Block? Opened(string trimmed)
    {
        if (trimmed.EndsWith("struct {", StringComparison.Ordinal))
            return Block.Struct;

        if (trimmed is "const (" or "var (")
            return Block.Spec;

        return trimmed.EndsWith('{') ? Block.Other : null;
    }

    /// <summary>
    /// Whether two lines belong to the same run.
    /// </summary>
    /// <remarks>
    /// Same indent and the same number of cells. `text/tabwriter` ends a column block when a
    /// line has fewer cells than the one before it, which is why a `const` block mixing
    /// `A T = 1` with `B = 2` comes out as two runs rather than one.
    /// </remarks>
    private static bool SameShape(Cells first, Cells next)
        => first.Indent == next.Indent && first.Parts.Length == next.Parts.Length;

    /// <summary>
    /// The cells of one line, or null where the line is not one a formatter aligns.
    /// </summary>
    private static Cells? CellsOf(string line, string trimmed, Block block)
    {
        if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            return null;

        string indent = line[..(line.Length - line.TrimStart().Length)];

        // A keyed element of a composite literal. Recognised by shape rather than by block,
        // because the same shape is aligned wherever it appears - including inside a
        // function, where `gofmt` aligns a map literal but not the statements around it.
        // `:=` is not this: it has no space before the colon.
        if (trimmed.EndsWith(',') && KeyOf(trimmed) is string key)
            return new Cells(indent, [key, trimmed[(key.Length + 1)..].TrimStart()]);

        return block switch
        {
            Block.Struct => Declaration(indent, trimmed),
            Block.Spec => Specification(indent, trimmed),
            _ => null,
        };
    }

    /// <summary>
    /// `Name:` where the line is a keyed element, and null otherwise.
    /// </summary>
    private static string? KeyOf(string trimmed)
    {
        int colon = trimmed.IndexOf(": ", StringComparison.Ordinal);

        if (colon <= 0)
            return null;

        string key = trimmed[..(colon + 1)];

        // The key is one thing, not the front of a statement that happens to contain a
        // colon - a `case` clause, or a label.
        return key.Any(char.IsWhiteSpace) ? null : key;
    }

    /// <summary>
    /// `Name Type`, the shape of a struct field.
    /// </summary>
    private static Cells? Declaration(string indent, string trimmed)
    {
        int space = trimmed.IndexOf(' ');

        if (space <= 0)
            return null;

        return new Cells(indent, [trimmed[..space], trimmed[(space + 1)..].TrimStart()]);
    }

    /// <summary>
    /// `Name Type = Value` or `Name = Value`, the shapes of a specification.
    /// </summary>
    private static Cells? Specification(string indent, string trimmed)
    {
        string[] words = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 3)
            return null;

        if (words[1] == "=")
            return new Cells(indent, [words[0], "= " + words[2]]);

        return words[2].StartsWith("= ", StringComparison.Ordinal)
            ? new Cells(indent, [words[0], words[1], words[2]])
            : null;
    }

    /// <summary>
    /// Rewrites one run with every cell but the last padded to the widest in that column.
    /// </summary>
    private static void Pad(List<string> lines, List<(int Line, Cells Cells)> run)
    {
        if (run.Count < 2)
            return;

        var cells = run.Select(entry => entry.Cells).ToArray();
        int columns = cells[0].Parts.Length;

        var widths = new int[columns - 1];

        for (int column = 0; column < columns - 1; column++)
            widths[column] = cells.Max(c => c.Parts[column].Length) + 1;

        for (int i = 0; i < run.Count; i++)
        {
            var text = new StringBuilder(cells[i].Indent);

            for (int column = 0; column < columns - 1; column++)
                text.Append(cells[i].Parts[column].PadRight(widths[column]));

            text.Append(cells[i].Parts[^1]);

            lines[run[i].Line] = text.ToString();
        }
    }

}
