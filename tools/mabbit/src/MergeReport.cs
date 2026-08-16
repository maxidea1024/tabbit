using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mabbit;

/// <summary>
/// The judgement, written for somebody who has to act on it.
/// </summary>
/// <remarks>
/// Two things have to come across. What the merge would carry over, so it can be checked
/// rather than trusted; and every place it could not decide, with all three values and the
/// cell they are in - because that is what the person needs in front of them to settle it.
/// </remarks>
internal static class MergeReport
{
    public static string Text(MergePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var text = new StringBuilder();

        text.Append("base    ").AppendLine(plan.BaseName);
        text.Append("mine    ").AppendLine(plan.MineName);
        text.Append("theirs  ").AppendLine(plan.TheirsName);

        foreach (var table in plan.Tables)
        {
            text.AppendLine();
            text.Append(table.Name).Append("    (").Append(Word(table.Verdict));

            if (table.RowCount > 0)
            {
                text.Append(", ").Append(Count(table.RowCount)).Append(" row(s)");
            }

            text.AppendLine(")");

            if (table.Conflict is not null)
                text.Append("  CONFLICT  ").AppendLine(table.Conflict);

            foreach (var column in table.Columns)
                AppendColumn(text, column);

            foreach (var row in table.Rows)
                AppendRow(text, row);
        }

        AppendOutside(text, plan);

        text.AppendLine();
        text.Append(Count(plan.ActionCount)).Append(" change(s) to take from theirs, ")
            .Append(Count(plan.ConflictCount)).AppendLine(" conflict(s).");

        // Said every time, not only when it matters. A tool that writes sometimes and not
        // others is one nobody can predict, and this build never writes.
        text.AppendLine("Nothing was written: this build judges only.");

        AppendNotes(text, plan);

        return text.ToString();
    }

    public static string Json(MergePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return JsonSerializer.Serialize(
            new
            {
                plan.BaseName,
                plan.MineName,
                plan.TheirsName,
                plan.Tables,
                plan.Outside,
                plan.Notes,
                plan.ActionCount,
                plan.ConflictCount,
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AppendColumn(StringBuilder text, ColumnMerge column)
    {
        if (column.Verdict == ColumnVerdict.Conflict)
        {
            text.Append("  CONFLICT  column ").Append(column.Name).Append("  - ")
                .AppendLine(column.Conflict);

            return;
        }

        text.Append("  ").Append(Verb(column.Verdict)).Append("  column ").AppendLine(column.Name);
    }

    private static void AppendRow(StringBuilder text, RowMerge row)
    {
        if (row.Conflict is not null)
        {
            text.Append("  CONFLICT  row ").Append(row.Key).Append("  - ").Append(row.Conflict)
                .Append("    ").AppendLine(row.Location);

            return;
        }

        if (row.Cells.Count == 0)
        {
            text.Append("  ").Append(Verb(row.Verdict)).Append("  row ").Append(row.Key)
                .Append("    ").AppendLine(row.Location);

            return;
        }

        foreach (var cell in row.Cells)
        {
            if (cell.Verdict == CellVerdict.Conflict)
            {
                text.Append("  CONFLICT  row ").Append(row.Key).Append("  column ")
                    .Append(cell.Column).Append("    ").AppendLine(cell.Location);

                // All three, one per line. Which one is right is the person's call, and they
                // cannot make it without seeing what it was before either side touched it.
                text.Append("      base    ").AppendLine(Shown(cell.Base));
                text.Append("      mine    ").AppendLine(Shown(cell.Mine));
                text.Append("      theirs  ").AppendLine(Shown(cell.Theirs));

                continue;
            }

            text.Append("  take      row ").Append(row.Key).Append("  ").Append(cell.Column)
                .Append(": ").Append(Shown(cell.Mine)).Append(" -> ").Append(Shown(cell.Theirs))
                .Append("    ").AppendLine(cell.Location);
        }
    }

    private static void AppendOutside(StringBuilder text, MergePlan plan)
    {
        if (plan.Outside.Count == 0)
            return;

        text.AppendLine();
        text.AppendLine("Outside the tables");

        foreach (var change in plan.Outside)
            text.Append("  CONFLICT  ").Append(change.Sheet).Append("  - ").AppendLine(change.Reason);
    }

    private static void AppendNotes(StringBuilder text, MergePlan plan)
    {
        if (plan.Notes.Count == 0)
            return;

        text.AppendLine();
        text.AppendLine("Rows this merge could not follow:");

        foreach (var note in plan.Notes)
            text.Append("  ! ").Append(note.Table).Append(": ").AppendLine(note.Text);
    }

    private static string Shown(string value) => value.Length == 0 ? "(empty)" : value;

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Word(TableVerdict verdict) => verdict switch
    {
        TableVerdict.AddFromTheirs => "add from theirs",
        TableVerdict.RemoveFromMine => "remove",
        TableVerdict.Conflict => "conflict",
        TableVerdict.Unchanged => "unchanged",
        _ => "changed",
    };

    private static string Verb(ColumnVerdict verdict) => verdict switch
    {
        ColumnVerdict.AddFromTheirs => "take    ",
        ColumnVerdict.RemoveFromMine => "remove  ",
        _ => "        ",
    };

    private static string Verb(RowVerdict verdict) => verdict switch
    {
        RowVerdict.AddFromTheirs => "take    ",
        RowVerdict.RemoveFromMine => "remove  ",
        _ => "        ",
    };
}
