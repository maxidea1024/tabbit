using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Mabbit;

/// <summary>What a merge decided about one cell.</summary>
internal enum CellVerdict
{
    /// <summary>Both sides agree, or only this side changed it. Nothing to do.</summary>
    Unchanged,

    /// <summary>Only the other side changed it. Its value is the merged one.</summary>
    TakeTheirs,

    /// <summary>Both sides changed it, differently.</summary>
    Conflict,
}

internal enum RowVerdict { Unchanged, UpdateCells, AddFromTheirs, RemoveFromMine, Conflict }

internal enum ColumnVerdict { Unchanged, AddFromTheirs, RemoveFromMine, Conflict }

internal enum TableVerdict { Unchanged, Changed, AddFromTheirs, RemoveFromMine, Conflict }

internal sealed record CellMerge(
    string Column, string Base, string Mine, string Theirs, CellVerdict Verdict, string Location);

internal sealed record RowMerge(
    string Key, RowVerdict Verdict, string Location, IReadOnlyList<CellMerge> Cells, string? Conflict);

internal sealed record ColumnMerge(string Name, ColumnVerdict Verdict, string? Conflict);

internal sealed class TableMerge
{
    public required string Name { get; init; }
    public required TableVerdict Verdict { get; init; }
    public IReadOnlyList<ColumnMerge> Columns { get; init; } = [];
    public IReadOnlyList<RowMerge> Rows { get; init; } = [];
    public string? Conflict { get; init; }

    /// <summary>Rows the table holds, for one that arrives or leaves whole.</summary>
    public int RowCount { get; init; }
}

/// <summary>
/// A change outside every table, which the merge can see but cannot carry.
/// </summary>
/// <remarks>
/// spec/workbook-merge.md section 4.5. Detecting these is the difference between a merge
/// that is incomplete and one that silently drops somebody's work.
/// </remarks>
internal sealed record OutsideChange(string Sheet, string Reason);

internal sealed class MergePlan
{
    public required string BaseName { get; init; }
    public required string MineName { get; init; }
    public required string TheirsName { get; init; }

    public IReadOnlyList<TableMerge> Tables { get; init; } = [];
    public IReadOnlyList<TableNote> Notes { get; init; } = [];
    public IReadOnlyList<OutsideChange> Outside { get; init; } = [];

    public int ConflictCount =>
        Outside.Count
        + Tables.Count(t => t.Verdict == TableVerdict.Conflict)
        + Tables.Sum(t => t.Columns.Count(c => c.Verdict == ColumnVerdict.Conflict))
        + Tables.Sum(t => t.Rows.Count(r => r.Verdict == RowVerdict.Conflict))
        + Tables.Sum(t => t.Rows.Sum(r => r.Cells.Count(c => c.Verdict == CellVerdict.Conflict)));

    /// <summary>How many things the merge would carry over from the other side.</summary>
    public int ActionCount =>
        Tables.Count(t => t.Verdict is TableVerdict.AddFromTheirs or TableVerdict.RemoveFromMine)
        + Tables.Sum(t => t.Columns.Count(c => c.Verdict != ColumnVerdict.Unchanged
                                               && c.Verdict != ColumnVerdict.Conflict))
        + Tables.Sum(t => t.Rows.Count(r => r.Verdict is RowVerdict.AddFromTheirs
                                                      or RowVerdict.RemoveFromMine))
        + Tables.Sum(t => t.Rows.Sum(r => r.Cells.Count(c => c.Verdict == CellVerdict.TakeTheirs)));

    public bool HasConflicts => ConflictCount > 0;
}

/// <summary>
/// The three-way judgement: what the merged workbook should hold, and where it cannot be
/// decided.
/// </summary>
/// <remarks>
/// Judges only. Nothing here writes a file, and nothing here knows how one would be written -
/// which is deliberate, because the judgement is the part that has to be right and the part
/// that can be checked without risking anybody's workbook.
///
/// The rules are spec/workbook-merge.md sections 4.3 to 4.5, and they are all one shape: a
/// side that did not change something has nothing to say about it, and two sides that changed
/// the same thing differently is the only case a program cannot settle.
/// </remarks>
internal static class WorkbookMerge
{
    public static MergePlan Judge(
        string baseName, IReadOnlyList<TableView> baseSide,
        string mineName, IReadOnlyList<TableView> mine,
        string theirsName, IReadOnlyList<TableView> theirs)
    {
        ArgumentNullException.ThrowIfNull(baseSide);
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);

        var byName = (IReadOnlyList<TableView> views)
            => views.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var o = byName(baseSide);
        var a = byName(mine);
        var b = byName(theirs);

        var tables = new List<TableMerge>();

        foreach (string name in o.Keys.Concat(a.Keys).Concat(b.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            o.TryGetValue(name, out var inBase);
            a.TryGetValue(name, out var inMine);
            b.TryGetValue(name, out var inTheirs);

            var judged = JudgeTable(name, inBase, inMine, inTheirs);

            if (judged is not null)
                tables.Add(judged);
        }

        return new MergePlan
        {
            BaseName = baseName,
            MineName = mineName,
            TheirsName = theirsName,
            Tables = tables,
            Notes = mine.SelectMany(WorkbookDiff.NotesOf)
                .Concat(theirs.SelectMany(WorkbookDiff.NotesOf))
                .ToList(),
        };
    }

    /// <summary>Null when there is nothing to say about the table.</summary>
    private static TableMerge? JudgeTable(
        string name, TableView? inBase, TableView? mine, TableView? theirs)
    {
        // Neither side has it any more, or only this side ever had it. Both are settled.
        if (mine is null && theirs is null)
            return null;

        if (inBase is null)
        {
            if (mine is not null && theirs is null)
                return null;

            if (mine is null && theirs is not null)
            {
                return new TableMerge
                {
                    Name = name,
                    Verdict = TableVerdict.AddFromTheirs,
                    RowCount = theirs.Rows.Count,
                };
            }

            // Both added a table of the same name. Identical content is not a conflict -
            // two people exporting the same new sheet is a thing that happens.
            return SameContent(mine!, theirs!)
                ? null
                : new TableMerge
                {
                    Name = name,
                    Verdict = TableVerdict.Conflict,
                    Conflict = "both sides added this table, with different contents",
                };
        }

        if (mine is null && theirs is null)
            return null;

        if (theirs is null)
        {
            // The other side deleted the table. Deleting something this side edited is not
            // something a program may settle.
            return SameContent(inBase, mine!)
                ? new TableMerge { Name = name, Verdict = TableVerdict.RemoveFromMine, RowCount = mine!.Rows.Count }
                : new TableMerge
                {
                    Name = name,
                    Verdict = TableVerdict.Conflict,
                    Conflict = "the other side deleted this table and this side changed it",
                };
        }

        if (mine is null)
        {
            return SameContent(inBase, theirs)
                ? null
                : new TableMerge
                {
                    Name = name,
                    Verdict = TableVerdict.Conflict,
                    Conflict = "this side deleted this table and the other side changed it",
                };
        }

        var columns = JudgeColumns(inBase, mine, theirs);
        var rows = JudgeRows(inBase, mine, theirs);

        if (columns.Count == 0 && rows.Count == 0)
            return null;

        bool conflicted = columns.Any(c => c.Verdict == ColumnVerdict.Conflict)
                          || rows.Any(r => r.Verdict == RowVerdict.Conflict
                                           || r.Cells.Any(c => c.Verdict == CellVerdict.Conflict));

        return new TableMerge
        {
            Name = name,
            Verdict = conflicted ? TableVerdict.Conflict : TableVerdict.Changed,
            Columns = columns,
            Rows = rows,
        };
    }

    private static IReadOnlyList<ColumnMerge> JudgeColumns(
        TableView inBase, TableView mine, TableView theirs)
    {
        var changes = new List<ColumnMerge>();

        foreach (string column in mine.Columns.Concat(theirs.Columns).Concat(inBase.Columns)
                     .Distinct(StringComparer.Ordinal))
        {
            bool wasThere = inBase.Columns.Contains(column, StringComparer.Ordinal);
            bool hasIt = mine.Columns.Contains(column, StringComparer.Ordinal);
            bool theyHaveIt = theirs.Columns.Contains(column, StringComparer.Ordinal);

            if (hasIt && theyHaveIt)
                continue;

            if (!wasThere && theyHaveIt)
            {
                changes.Add(new ColumnMerge(column, ColumnVerdict.AddFromTheirs, null));
                continue;
            }

            if (!wasThere)
                continue;

            // A column that was there and is gone from one side. Renaming a column reads as
            // exactly this - a removal and an addition - so an edit to it on the other side
            // is the rename-versus-edit case and cannot be settled.
            if (!theyHaveIt && hasIt)
            {
                changes.Add(ColumnEdited(inBase, mine, column)
                    ? new ColumnMerge(column, ColumnVerdict.Conflict,
                        "the other side removed this column and this side changed a value in it")
                    : new ColumnMerge(column, ColumnVerdict.RemoveFromMine, null));

                continue;
            }

            if (!hasIt && theyHaveIt)
            {
                changes.Add(new ColumnMerge(column, ColumnVerdict.Conflict,
                    "this side removed this column and the other side changed a value in it"));
            }
        }

        return changes;
    }

    /// <summary>Whether a side changed any cell of one column against the base.</summary>
    private static bool ColumnEdited(TableView inBase, TableView side, string column)
    {
        int here = IndexOf(side.Columns, column);
        int was = IndexOf(inBase.Columns, column);

        if (here < 0 || was < 0)
            return false;

        foreach (var row in side.Rows)
        {
            var before = inBase.ByKey(row.Key);
            if (before is null)
                continue;

            if (!string.Equals(before.Cells[was], row.Cells[here], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<RowMerge> JudgeRows(
        TableView inBase, TableView mine, TableView theirs)
    {
        // Every column either side holds, paired to its position in each of the three. A
        // column one side lacks reads as an empty cell there, which is what it is.
        var columns = mine.Columns.Concat(theirs.Columns)
            .Distinct(StringComparer.Ordinal)
            .Select(name => (
                Name: name,
                Base: IndexOf(inBase.Columns, name),
                Mine: IndexOf(mine.Columns, name),
                Theirs: IndexOf(theirs.Columns, name)))
            .ToList();

        var rows = new List<RowMerge>();

        foreach (string key in mine.Rows.Select(r => r.Key)
                     .Concat(theirs.Rows.Select(r => r.Key))
                     .Concat(inBase.Rows.Select(r => r.Key))
                     .Distinct(StringComparer.Ordinal))
        {
            var was = inBase.ByKey(key);
            var here = mine.ByKey(key);
            var there = theirs.ByKey(key);

            var judged = JudgeRow(key, was, here, there, columns, mine, theirs);

            if (judged is not null)
                rows.Add(judged);
        }

        return rows;
    }

    private static RowMerge? JudgeRow(
        string key, RowView? was, RowView? here, RowView? there,
        List<(string Name, int Base, int Mine, int Theirs)> columns,
        TableView mine, TableView theirs)
    {
        if (was is null)
        {
            if (here is not null && there is null)
                return null;

            if (here is null && there is not null)
            {
                return new RowMerge(
                    key, RowVerdict.AddFromTheirs, Where(theirs, there), [], null);
            }

            // Both added a row under the same key. This is the accident two people appending
            // to the same table produce, and it is exactly what a merge has to catch.
            return SameRow(here!, there!, columns)
                ? null
                : new RowMerge(key, RowVerdict.Conflict, Where(mine, here!), [],
                    "both sides added a row with this key, holding different values");
        }

        if (here is null && there is null)
            return null;

        if (there is null)
        {
            return SameRow(was, here!, columns, useBaseForTheirs: true)
                ? new RowMerge(key, RowVerdict.RemoveFromMine, Where(mine, here!), [], null)
                : new RowMerge(key, RowVerdict.Conflict, Where(mine, here!), [],
                    "the other side deleted this row and this side changed it");
        }

        if (here is null)
        {
            return SameRow(was, there, columns, useBaseForTheirs: false, theirsIsSecond: true)
                ? null
                : new RowMerge(key, RowVerdict.Conflict, Where(theirs, there), [],
                    "this side deleted this row and the other side changed it");
        }

        var cells = new List<CellMerge>();

        foreach (var (name, inBase, inMine, inTheirs) in columns)
        {
            string o = At(was, inBase);
            string a = At(here, inMine);
            string b = At(there, inTheirs);

            var verdict = Verdict(o, a, b);

            if (verdict == CellVerdict.Unchanged)
                continue;

            cells.Add(new CellMerge(name, o, a, b, verdict,
                CellRef.A1(mine.Region.Sheet, here.RowIndex, mine.Region.FirstColumn + Math.Max(inMine, 0))));
        }

        if (cells.Count == 0)
            return null;

        var rowVerdict = cells.Any(c => c.Verdict == CellVerdict.Conflict)
            ? RowVerdict.Conflict
            : RowVerdict.UpdateCells;

        return new RowMerge(key, rowVerdict, Where(mine, here), cells, null);
    }

    /// <summary>The three-way rule, which is the whole of section 4.3.</summary>
    private static CellVerdict Verdict(string o, string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return CellVerdict.Unchanged;

        if (string.Equals(a, o, StringComparison.Ordinal))
            return CellVerdict.TakeTheirs;

        if (string.Equals(b, o, StringComparison.Ordinal))
            return CellVerdict.Unchanged;

        return CellVerdict.Conflict;
    }

    private static string At(RowView row, int column)
        => column >= 0 && column < row.Cells.Length ? row.Cells[column] : "";

    private static bool SameRow(
        RowView left, RowView right,
        List<(string Name, int Base, int Mine, int Theirs)> columns,
        bool useBaseForTheirs = false, bool theirsIsSecond = false)
    {
        foreach (var (_, inBase, inMine, inTheirs) in columns)
        {
            int leftAt = useBaseForTheirs || theirsIsSecond ? inBase : inMine;
            int rightAt = theirsIsSecond ? inTheirs : useBaseForTheirs ? inMine : inTheirs;

            if (!string.Equals(At(left, leftAt), At(right, rightAt), StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool SameContent(TableView left, TableView right)
    {
        if (left.Rows.Count != right.Rows.Count)
            return false;

        if (!left.Columns.SequenceEqual(right.Columns, StringComparer.Ordinal))
            return false;

        foreach (var row in left.Rows)
        {
            var counterpart = right.ByKey(row.Key);

            if (counterpart is null || !row.Cells.SequenceEqual(counterpart.Cells, StringComparer.Ordinal))
                return false;
        }

        return true;
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

    private static string Where(TableView table, RowView row)
        => CellRef.A1(table.Region.Sheet, row.RowIndex, table.Region.KeyColumn);

    /// <summary>
    /// Whether anything changed outside the tables, and on which side.
    /// </summary>
    /// <remarks>
    /// spec/workbook-merge.md sections 4.5 and 4.6. A merge writes inside table rectangles
    /// and nowhere else, so a change the other side made outside one cannot be carried - and
    /// a merge that reported success while dropping it would be worse than one that refuses.
    /// Both "both sides changed it" and "only the other side changed it" are therefore
    /// conflicts, and they read differently in the report because they are different
    /// situations.
    /// </remarks>
    public static IReadOnlyList<OutsideChange> OutsideTables(
        WorkbookGrid inBase, WorkbookGrid mine, WorkbookGrid theirs, ITableSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var changes = new List<OutsideChange>();

        var o = Fingerprints(inBase, schema);
        var a = Fingerprints(mine, schema);
        var b = Fingerprints(theirs, schema);

        foreach (string sheet in a.Keys.Concat(b.Keys).Concat(o.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.Ordinal))
        {
            string was = o.GetValueOrDefault(sheet, "");
            string here = a.GetValueOrDefault(sheet, "");
            string there = b.GetValueOrDefault(sheet, "");

            if (string.Equals(here, there, StringComparison.Ordinal))
                continue;

            if (string.Equals(there, was, StringComparison.Ordinal))
                continue;

            changes.Add(new OutsideChange(sheet,
                string.Equals(here, was, StringComparison.Ordinal)
                    ? "the other side changed cells outside every table, which a merge does not write"
                    : "both sides changed cells outside every table, differently"));
        }

        return changes;
    }

    private static Dictionary<string, string> Fingerprints(WorkbookGrid workbook, ITableSchema schema)
    {
        var regions = schema.TablesIn(workbook)
            .GroupBy(r => r.Sheet, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var bySheet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Sheets)
        {
            var covering = regions.GetValueOrDefault(sheet.Name, []);
            var text = new StringBuilder();

            foreach (var (row, column, value) in sheet.NonEmptyCells())
            {
                if (covering.Any(r => row >= r.HeaderRow && row <= r.LastDataRow
                                      && column >= r.FirstColumn && column <= r.LastColumn))
                {
                    continue;
                }

                text.Append(row.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(column.ToString(CultureInfo.InvariantCulture)).Append('=')
                    .Append(value).Append('\n');
            }

            bySheet[sheet.Name] = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        return bySheet;
    }
}
