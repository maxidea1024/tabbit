using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mabbit;

/// <summary>
/// The comparison, written for somebody to read or for something to parse.
/// </summary>
/// <remarks>
/// Every change carries the cell it is at, spelled the way a spreadsheet spells one. That is
/// what makes the text form usable on its own while there is no interface to click through:
/// the answer to "where" is in the line rather than somewhere to go and look for.
/// </remarks>
internal static class DiffReport
{
    public static string Text(DiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder();

        text.Append(result.BaseName).Append("  ->  ").AppendLine(result.OtherName);

        if (result.IsEmpty)
        {
            text.AppendLine().AppendLine("No difference in any table.");
            AppendNotes(text, result);

            return text.ToString();
        }

        foreach (var table in result.Tables)
        {
            text.AppendLine();
            text.Append(table.Name).Append("    (").Append(Word(table.Kind));

            if (table.Kind != ChangeKind.Modified)
            {
                text.Append(", ")
                    .Append(table.RowCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" row(s)");
            }

            text.AppendLine(")");

            foreach (var column in table.Columns)
                text.Append("  ").Append(Sign(column.Kind)).Append(" column  ").AppendLine(column.Name);

            foreach (var row in table.Rows)
                AppendRow(text, row);
        }

        text.AppendLine();
        text.AppendLine(Summary(result));

        AppendNotes(text, result);

        return text.ToString();
    }

    /// <summary>
    /// What the second file holds that no comparison can follow.
    /// </summary>
    /// <remarks>
    /// Below the changes and outside the count, because these are not differences. A file
    /// compared against itself reaches here with every one of them and the line above still
    /// reads "No difference".
    /// </remarks>
    private static void AppendNotes(StringBuilder text, DiffResult result)
    {
        if (result.Notes.Count == 0)
            return;

        text.AppendLine();
        text.Append("Rows this comparison could not follow, in ").Append(result.OtherName).AppendLine(":");

        foreach (var note in result.Notes)
            text.Append("  ! ").Append(note.Table).Append(": ").AppendLine(note.Text);
    }

    public static string Json(DiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static void AppendRow(StringBuilder text, RowChange row)
    {
        text.Append("  ").Append(Sign(row.Kind)).Append(" row     ").Append(row.Key);

        if (row.Cells.Count == 0)
        {
            text.Append("    ").AppendLine(row.Location);
            return;
        }

        text.AppendLine();

        foreach (var cell in row.Cells)
        {
            text.Append("      ")
                .Append(cell.Column)
                .Append(":  ")
                .Append(Shown(cell.Before))
                .Append("  ->  ")
                .Append(Shown(cell.After))
                .Append("    ")
                .AppendLine(cell.Location);
        }
    }

    /// <summary>
    /// A value as the report shows it.
    /// </summary>
    /// <remarks>
    /// An empty cell reads as `(empty)` rather than as nothing, because an arrow with one
    /// side missing is a line whose meaning depends on noticing the gap - and clearing a
    /// cell is a change worth being able to see.
    /// </remarks>
    private static string Shown(string value)
        => value.Length == 0 ? "(empty)" : value;

    private static string Summary(DiffResult result)
    {
        int modified = result.Tables.Count(t => t.Kind == ChangeKind.Modified);
        int added = result.Tables.Count(t => t.Kind == ChangeKind.Added);
        int removed = result.Tables.Count(t => t.Kind == ChangeKind.Removed);

        int rows = result.Tables.Sum(t => t.Rows.Count);
        int cells = result.Tables.Sum(t => t.Rows.Sum(r => r.Cells.Count));

        var parts = new List<string>();

        if (modified > 0) parts.Add($"{Count(modified)} changed");
        if (added > 0) parts.Add($"{Count(added)} added");
        if (removed > 0) parts.Add($"{Count(removed)} removed");

        return $"{Count(result.Tables.Count)} table(s): {string.Join(", ", parts)}. "
            + $"{Count(rows)} row(s), {Count(cells)} cell(s).";
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Word(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Removed => "removed",
        _ => "changed",
    };

    private static string Sign(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "+",
        ChangeKind.Removed => "-",
        _ => "~",
    };
}
