using System;
using System.Globalization;
using System.Text;

namespace Mabbit;

/// <summary>
/// The judgement as a page to open in a browser.
/// </summary>
/// <remarks>
/// spec/import/workbook-merge.md section 6. The shape a conflict has is three values and a cell, and
/// on a terminal those three lines are the same size as everything else on the screen. Here
/// they are the thing the page is made of, and what the merge decided on its own is folded
/// underneath.
///
/// One file, no stylesheet, no script, no font, no image. A page that fetches anything is a
/// page that does not load on a closed network, and this is opened from a working copy on
/// whatever machine somebody happens to be resolving a conflict on.
/// </remarks>
internal static class HtmlReport
{
    public static string Of(MergePlan plan, MergeWriter.WritePlan? write = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var page = new StringBuilder();

        page.AppendLine("<!doctype html>");
        page.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        page.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        page.Append("<title>mabbit - ").Append(Escaped(plan.MineName)).AppendLine("</title>");
        page.Append("<style>").Append(Style).AppendLine("</style>");
        page.AppendLine("</head><body>");

        Header(page, plan, write);

        foreach (var table in plan.Tables)
            Table(page, table);

        Outside(page, plan);
        Notes(page, plan);

        page.AppendLine("</body></html>");

        return page.ToString();
    }

    private static void Header(StringBuilder page, MergePlan plan, MergeWriter.WritePlan? write)
    {
        page.AppendLine("<header>");
        page.Append("<h1>").Append(Count(plan.ConflictCount)).Append(" conflict(s), ")
            .Append(Count(plan.ActionCount)).AppendLine(" change(s) to take</h1>");

        page.AppendLine("<dl class=\"sides\">");
        Side(page, "base", plan.BaseName);
        Side(page, "mine", plan.MineName);
        Side(page, "theirs", plan.TheirsName);
        page.AppendLine("</dl>");

        if (write is null)
        {
            page.AppendLine("<p class=\"outcome\">Nothing was written: no result was asked for.</p>");
        }
        else if (write.CanWrite)
        {
            page.Append("<p class=\"outcome ok\">Wrote ").Append(Count(write.Edits.Count))
                .AppendLine(" cell(s) into a copy of mine. Everything else is its bytes unchanged.</p>");
        }
        else
        {
            page.AppendLine("<p class=\"outcome bad\">Nothing was written, because:</p><ul class=\"why\">");

            foreach (var refusal in write.Refusals)
                page.Append("<li>").Append(Escaped(refusal.Reason)).AppendLine("</li>");

            page.AppendLine("</ul>");
        }

        page.AppendLine("</header>");
    }

    private static void Side(StringBuilder page, string name, string value)
        => page.Append("<dt>").Append(name).Append("</dt><dd>").Append(Escaped(value)).AppendLine("</dd>");

    private static void Table(StringBuilder page, TableMerge table)
    {
        page.Append("<section><h2>").Append(Escaped(table.Name))
            .Append(" <span class=\"verdict\">").Append(table.Verdict.ToString().ToLowerInvariant())
            .AppendLine("</span></h2>");

        if (table.Conflict is not null)
            page.Append("<p class=\"bad\">").Append(Escaped(table.Conflict)).AppendLine("</p>");

        foreach (var column in table.Columns)
        {
            page.Append("<p class=\"").Append(column.Verdict == ColumnVerdict.Conflict ? "bad" : "take")
                .Append("\">column <b>").Append(Escaped(column.Name)).Append("</b> ")
                .Append(Escaped(column.Conflict ?? column.Verdict.ToString())).AppendLine("</p>");
        }

        foreach (var row in table.Rows)
            Row(page, row);

        page.AppendLine("</section>");
    }

    private static void Row(StringBuilder page, RowMerge row)
    {
        if (row.Conflict is not null)
        {
            page.Append("<div class=\"conflict\"><h3>row <b>").Append(Escaped(row.Key))
                .Append("</b> <code>").Append(Escaped(row.Location)).AppendLine("</code></h3>");

            page.Append("<p class=\"bad\">").Append(Escaped(row.Conflict)).AppendLine("</p></div>");

            return;
        }

        foreach (var cell in row.Cells)
        {
            if (cell.Verdict == CellVerdict.Conflict)
            {
                page.Append("<div class=\"conflict\"><h3>row <b>").Append(Escaped(row.Key))
                    .Append("</b>, column <b>").Append(Escaped(cell.Column))
                    .Append("</b> <code>").Append(Escaped(cell.Location)).AppendLine("</code></h3>");

                // The three side by side, because choosing between them is a comparison and
                // a person cannot make it down a list.
                page.AppendLine("<div class=\"three\">");
                Value(page, "base", cell.Base);
                Value(page, "mine", cell.Mine);
                Value(page, "theirs", cell.Theirs);
                page.AppendLine("</div></div>");

                continue;
            }

            if (cell.Verdict != CellVerdict.TakeTheirs)
                continue;

            page.Append("<p class=\"take\">row <b>").Append(Escaped(row.Key))
                .Append("</b> <b>").Append(Escaped(cell.Column)).Append("</b> ")
                .Append("<s>").Append(Escaped(Shown(cell.Mine))).Append("</s> ")
                .Append(Escaped(Shown(cell.Theirs)))
                .Append(" <code>").Append(Escaped(cell.Location)).AppendLine("</code></p>");
        }

        if (row.Cells.Count == 0)
        {
            page.Append("<p class=\"take\">row <b>").Append(Escaped(row.Key)).Append("</b> ")
                .Append(Escaped(row.Verdict.ToString()))
                .Append(" <code>").Append(Escaped(row.Location)).AppendLine("</code></p>");
        }
    }

    private static void Value(StringBuilder page, string side, string value)
        => page.Append("<div class=\"v ").Append(side).Append("\"><span>").Append(side)
            .Append("</span><pre>").Append(Escaped(Shown(value))).AppendLine("</pre></div>");

    private static void Outside(StringBuilder page, MergePlan plan)
    {
        if (plan.Outside.Count == 0)
            return;

        page.AppendLine("<section><h2>Outside the tables</h2>");

        foreach (var change in plan.Outside)
        {
            page.Append("<p class=\"bad\"><b>").Append(Escaped(change.Sheet)).Append("</b> ")
                .Append(Escaped(change.Reason)).AppendLine("</p>");
        }

        page.AppendLine("</section>");
    }

    private static void Notes(StringBuilder page, MergePlan plan)
    {
        if (plan.Notes.Count == 0)
            return;

        page.AppendLine("<section><h2>Rows this merge could not follow</h2>");

        foreach (var note in plan.Notes)
        {
            page.Append("<p class=\"note\"><b>").Append(Escaped(note.Side)).Append("</b> ")
                .Append(Escaped(note.Table)).Append(": ").Append(Escaped(note.Text)).AppendLine("</p>");
        }

        page.AppendLine("</section>");
    }

    private static string Shown(string value) => value.Length == 0 ? "(empty)" : value;

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Escaped(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    /// <summary>
    /// The page's own styling, in the colours the rest of this project uses.
    /// </summary>
    /// <remarks>
    /// Follows the reader's own light or dark setting rather than picking one. Somebody opens
    /// this in the middle of resolving a conflict, and a page that arrives white in a dark
    /// room is one more thing to deal with.
    /// </remarks>
    private const string Style = """
        :root {
          --ink: #24186c; --violet: #846cf0; --pink: #fc8490; --amber: #fcc024;
          --bg: #ffffff; --fg: #171426; --line: #e4e1f2; --panel: #f7f6fd; --dim: #6b6684;
        }
        @media (prefers-color-scheme: dark) {
          :root { --bg: #14102e; --fg: #eceaf6; --line: #2e2857; --panel: #1b1640; --dim: #9c96bb; }
        }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 2rem 1.25rem 4rem; background: var(--bg); color: var(--fg);
          font: 15px/1.6 ui-sans-serif, system-ui, "Segoe UI", sans-serif;
          max-width: 60rem; margin-inline: auto; }
        header { border-bottom: 2px solid var(--line); padding-bottom: 1rem; margin-bottom: 1rem; }
        h1 { font-size: 1.35rem; margin: 0 0 .75rem; }
        h2 { font-size: 1.05rem; margin: 2rem 0 .5rem; border-bottom: 1px solid var(--line);
          padding-bottom: .3rem; }
        h3 { font-size: .92rem; margin: 0 0 .5rem; font-weight: 600; }
        .verdict { font-size: .72rem; font-weight: 500; color: var(--dim);
          border: 1px solid var(--line); border-radius: 999px; padding: .1rem .5rem; }
        .sides { display: grid; grid-template-columns: max-content 1fr; gap: .1rem .75rem; margin: 0; }
        .sides dt { color: var(--dim); font-size: .8rem; text-transform: uppercase;
          letter-spacing: .04em; }
        .sides dd { margin: 0; word-break: break-all; }
        .outcome { margin: .75rem 0 0; font-weight: 600; }
        .outcome.ok { color: #1a8a5a; }
        .outcome.bad, .bad { color: var(--pink); }
        .why { margin: .25rem 0 0; }
        p { margin: .3rem 0; }
        .take { color: var(--dim); }
        .take b, .note b { color: var(--fg); }
        .take s { opacity: .55; }
        .conflict { border-left: 3px solid var(--pink); background: var(--panel);
          padding: .75rem 1rem; margin: .75rem 0; border-radius: 0 .4rem .4rem 0; }
        .three { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: .5rem; }
        @media (max-width: 40rem) { .three { grid-template-columns: 1fr; } }
        .v { border: 1px solid var(--line); border-radius: .35rem; background: var(--bg);
          overflow: hidden; }
        .v span { display: block; padding: .2rem .5rem; font-size: .7rem; letter-spacing: .04em;
          text-transform: uppercase; color: var(--dim); border-bottom: 1px solid var(--line); }
        .v.mine span { color: var(--violet); }
        .v.theirs span { color: var(--amber); }
        .v pre { margin: 0; padding: .45rem .5rem; white-space: pre-wrap; word-break: break-word;
          font: 13px/1.5 ui-monospace, "Cascadia Code", Consolas, monospace; }
        code { font: 12px ui-monospace, "Cascadia Code", Consolas, monospace; color: var(--dim); }
        .note { color: var(--dim); }
        """;
}
