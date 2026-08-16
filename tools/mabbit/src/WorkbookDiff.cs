using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mabbit;

internal enum ChangeKind
{
    Added,
    Removed,
    Modified,
}

internal sealed record CellChange(string Column, string Before, string After, string Location);

internal sealed record RowChange(
    ChangeKind Kind, string Key, string Location, IReadOnlyList<CellChange> Cells);

internal sealed record ColumnChange(ChangeKind Kind, string Name);

/// <summary>Something about a table that the comparison could not follow, said rather than dropped.</summary>
internal sealed record TableNote(string Table, string Text);

internal sealed class TableChange
{
    public required string Name { get; init; }
    public required ChangeKind Kind { get; init; }
    public IReadOnlyList<ColumnChange> Columns { get; init; } = [];
    public IReadOnlyList<RowChange> Rows { get; init; } = [];

    /// <summary>How many rows the table holds, for a table that was added or removed whole.</summary>
    public int RowCount { get; init; }
}

internal sealed class DiffResult
{
    public required string BaseName { get; init; }
    public required string OtherName { get; init; }
    public IReadOnlyList<TableChange> Tables { get; init; } = [];

    /// <summary>
    /// What the second file holds that a comparison cannot follow: rows with no key, and
    /// keys used twice.
    /// </summary>
    /// <remarks>
    /// Held apart from the changes, and not counted as one. These describe one file rather
    /// than a difference between two - a file compared against itself has every one of them
    /// and differs in nothing. Counting them made it report a table as changed, which is the
    /// one answer a comparison must never give wrongly.
    ///
    /// Still reported, because a key that does not identify a row is what makes a later
    /// merge unable to say anything about it.
    /// </remarks>
    public IReadOnlyList<TableNote> Notes { get; init; } = [];

    public bool IsEmpty => Tables.Count == 0;
}

/// <summary>
/// What changed between two workbooks, by table, row key and column.
/// </summary>
/// <remarks>
/// Rows are matched by their key rather than by where they sit, which is the whole reason
/// this exists rather than a text diff of the sheets. Inserting a row in the middle of a
/// table moves every row below it, and a position-based comparison reports all of them; this
/// reports the one row that arrived.
/// </remarks>
internal static class WorkbookDiff
{
    public static DiffResult Compare(
        string baseName, IReadOnlyList<TableView> before,
        string otherName, IReadOnlyList<TableView> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeByName = before.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var afterByName = after.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var changes = new List<TableChange>();

        // In the order the second file holds them, then whatever only the first had. A
        // report is read against the file somebody is looking at.
        foreach (var table in after)
        {
            if (beforeByName.TryGetValue(table.Name, out var counterpart))
            {
                var change = CompareTables(counterpart, table);
                if (change is not null)
                    changes.Add(change);

                continue;
            }

            changes.Add(new TableChange
            {
                Name = table.Name,
                Kind = ChangeKind.Added,
                RowCount = table.Rows.Count,
            });
        }

        foreach (var table in before)
        {
            if (afterByName.ContainsKey(table.Name))
                continue;

            changes.Add(new TableChange
            {
                Name = table.Name,
                Kind = ChangeKind.Removed,
                RowCount = table.Rows.Count,
            });
        }

        return new DiffResult
        {
            BaseName = baseName,
            OtherName = otherName,
            Tables = changes,
            Notes = after.SelectMany(NotesOf).ToList(),
        };
    }

    /// <summary>Null when the two hold the same thing.</summary>
    private static TableChange? CompareTables(TableView before, TableView after)
    {
        var columns = CompareColumns(before, after);
        var rows = CompareRows(before, after);

        if (columns.Count == 0 && rows.Count == 0)
            return null;

        return new TableChange
        {
            Name = after.Name,
            Kind = ChangeKind.Modified,
            Columns = columns,
            Rows = rows,
        };
    }

    private static IReadOnlyList<ColumnChange> CompareColumns(TableView before, TableView after)
    {
        var changes = new List<ColumnChange>();

        foreach (string column in after.Columns)
        {
            if (!before.Columns.Contains(column, StringComparer.Ordinal))
                changes.Add(new ColumnChange(ChangeKind.Added, column));
        }

        foreach (string column in before.Columns)
        {
            if (!after.Columns.Contains(column, StringComparer.Ordinal))
                changes.Add(new ColumnChange(ChangeKind.Removed, column));
        }

        return changes;
    }

    private static IReadOnlyList<RowChange> CompareRows(TableView before, TableView after)
    {
        var changes = new List<RowChange>();

        // The columns both sides have, paired by name once rather than looked up per row.
        // A column only one side has is reported as a column change; reporting every one of
        // its cells as well would say the same thing once per row.
        var shared = new List<(string Column, int Before, int After)>();

        for (int i = 0; i < after.Columns.Count; i++)
        {
            int inBefore = IndexOf(before.Columns, after.Columns[i]);
            if (inBefore >= 0)
                shared.Add((after.Columns[i], inBefore, i));
        }

        foreach (var row in after.Rows)
        {
            var counterpart = before.ByKey(row.Key);

            if (counterpart is null)
            {
                changes.Add(new RowChange(
                    ChangeKind.Added, row.Key, LocationOf(after, row), []));

                continue;
            }

            var cells = new List<CellChange>();

            foreach (var (column, inBefore, inAfter) in shared)
            {
                string was = counterpart.Cells[inBefore];
                string now = row.Cells[inAfter];

                if (string.Equals(was, now, StringComparison.Ordinal))
                    continue;

                cells.Add(new CellChange(
                    column, was, now,
                    CellRef.A1(after.Region.Sheet, row.RowIndex, after.Region.FirstColumn + inAfter)));
            }

            if (cells.Count > 0)
                changes.Add(new RowChange(ChangeKind.Modified, row.Key, LocationOf(after, row), cells));
        }

        foreach (var row in before.Rows)
        {
            if (after.ByKey(row.Key) is null)
                changes.Add(new RowChange(
                    ChangeKind.Removed, row.Key, LocationOf(before, row), []));
        }

        return changes;
    }

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static string LocationOf(TableView table, RowView row)
        => CellRef.A1(table.Region.Sheet, row.RowIndex, table.Region.KeyColumn);

    internal static IReadOnlyList<TableNote> NotesOf(TableView table)
    {
        var notes = new List<TableNote>();

        // Grouped rather than one note per row. A sheet that is not a keyed table at all -
        // a working list somebody keeps beside the data - produces one of these per row,
        // and a hundred lines saying the same thing is a hundred lines nobody reads.
        if (table.Duplicates.Count > 0)
        {
            notes.Add(new TableNote(table.Name,
                $"{Count(table.Duplicates.Count)} row(s) repeat a key an earlier row already "
                + $"used, so only the first of each was followed: {Where(table, table.Duplicates)}."));
        }

        if (table.Unkeyed.Count > 0)
        {
            notes.Add(new TableNote(table.Name,
                $"{Count(table.Unkeyed.Count)} row(s) have nothing in the column that identifies "
                + $"a row, so they cannot be followed from one file to the other: "
                + $"{Where(table, table.Unkeyed)}."));
        }

        return notes;
    }

    /// <summary>The first few cells of a group, and how many more there were.</summary>
    private static string Where(TableView table, IReadOnlyList<RowView> rows)
    {
        string where = string.Join(", ", rows
            .Take(5)
            .Select(r => CellRef.A1(table.Region.Sheet, r.RowIndex, table.Region.KeyColumn)));

        return rows.Count > 5 ? $"{where} and {Count(rows.Count - 5)} more" : where;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
