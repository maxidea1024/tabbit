using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mabbit;

/// <summary>
/// Turns a judgement into cells to write, or says why it cannot.
/// </summary>
/// <remarks>
/// The judgement decides what the merged workbook should hold; this decides whether that can
/// be reached by writing cells into the rectangles the tables already occupy. Anything that
/// would change a table's shape - a row arriving, a column arriving, a whole table - cannot,
/// because those move the cells below and beside them and take the defined names, merged
/// ranges and formulas that point at them along. That is stage 4 of spec/import/workbook-merge.md.
///
/// Refusing is a result, not a failure. A merge that reports success having applied part of
/// what it decided is the one outcome nobody can recover from.
/// </remarks>
internal static class MergeWriter
{
    /// <summary>What stands between a plan and a written workbook.</summary>
    internal sealed record Refusal(string Reason);

    internal sealed record WritePlan(IReadOnlyList<CellEdit> Edits, IReadOnlyList<Refusal> Refusals)
    {
        public bool CanWrite => Refusals.Count == 0;
    }

    public static WritePlan Prepare(MergePlan plan, IReadOnlyList<TableView> mine, WorkbookGrid? grid = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(mine);

        var refusals = new List<Refusal>();
        var edits = new List<CellEdit>();

        if (plan.HasConflicts)
        {
            refusals.Add(new Refusal(
                $"{plan.ConflictCount.ToString(CultureInfo.InvariantCulture)} conflict(s) have "
                + "to be settled first."));
        }

        var byName = mine.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var table in plan.Tables)
        {
            switch (table.Verdict)
            {
                case TableVerdict.AddFromTheirs:
                    refusals.Add(new Refusal(
                        $"table `{table.Name}` arrives whole from the other side, which needs a "
                        + "sheet to be created."));
                    continue;

                case TableVerdict.RemoveFromMine:
                    refusals.Add(new Refusal(
                        $"table `{table.Name}` was deleted on the other side, which needs a "
                        + "sheet to be removed."));
                    continue;
            }

            foreach (var column in table.Columns)
            {
                if (column.Verdict is ColumnVerdict.Unchanged or ColumnVerdict.Conflict)
                    continue;

                refusals.Add(new Refusal(
                    $"column `{column.Name}` of `{table.Name}` "
                    + (column.Verdict == ColumnVerdict.AddFromTheirs ? "arrives" : "goes")
                    + ", which changes the table's shape."));
            }

            if (!byName.TryGetValue(table.Name, out var view))
            {
                refusals.Add(new Refusal($"this side has no table `{table.Name}` to write into."));
                continue;
            }

            // Where a row arriving from the other side goes: after the last one the table
            // holds. Rows are not sorted into place, because the order of a table is the
            // order somebody put it in and moving a row is not a merge's decision.
            int appendAt = view.Region.LastDataRow + 1;

            foreach (var row in table.Rows)
            {
                switch (row.Verdict)
                {
                    case RowVerdict.AddFromTheirs:
                    {
                        string? why = WhyNoRoom(grid, view);

                        if (why is not null)
                        {
                            refusals.Add(new Refusal(
                                $"row `{row.Key}` of `{table.Name}` arrives from the other side "
                                + $"and there is no room for it: {why}"));

                            continue;
                        }

                        edits.AddRange(Arriving(plan, table, row, view, appendAt));
                        appendAt++;

                        continue;
                    }

                    case RowVerdict.RemoveFromMine:
                    {
                        var going = view.ByKey(row.Key);

                        if (going is null)
                            continue;

                        // Cleared rather than cut out. Removing the row would move every row
                        // under it, and with them the merged ranges, the defined names and
                        // every formula that points at one - which is the cost section 5.4 is
                        // about. A row left blank inside a table is a row the reader skips,
                        // so the data is right either way.
                        for (int column = 0; column < view.Columns.Count; column++)
                        {
                            edits.Add(new CellEdit(
                                view.Region.Sheet, going.RowIndex,
                                view.Region.FirstColumn + column, ""));
                        }

                        continue;
                    }
                }

                var here = view.ByKey(row.Key);

                if (here is null)
                    continue;

                foreach (var cell in row.Cells)
                {
                    if (cell.Verdict != CellVerdict.TakeTheirs)
                        continue;

                    int column = IndexOf(view.Columns, cell.Column);

                    if (column < 0)
                    {
                        refusals.Add(new Refusal(
                            $"column `{cell.Column}` of `{table.Name}` is not in this side's "
                            + "table, so there is no cell to write."));

                        continue;
                    }

                    edits.Add(new CellEdit(
                        view.Region.Sheet,
                        here.RowIndex,
                        view.Region.FirstColumn + column,
                        cell.Theirs));
                }
            }
        }

        return new WritePlan(edits, refusals);
    }

    /// <summary>
    /// Why an arriving row cannot go below the table, or null when it can.
    /// </summary>
    /// <remarks>
    /// A row is only ever appended, never inserted, and appending is safe exactly when the
    /// space below the table is empty. Anything down there would be written over - and moving
    /// it out of the way is the expensive half of this, because a shift takes the merged
    /// ranges, the defined names and the formulas that point at them along with it.
    /// </remarks>
    private static string? WhyNoRoom(WorkbookGrid? grid, TableView view)
    {
        if (grid is null)
            return "this side's workbook was not given, so what is below the table is unknown.";

        var sheet = grid.Sheet(view.Region.Sheet);

        if (sheet is null)
            return $"this side has no sheet `{view.Region.Sheet}`.";

        foreach (var (row, column, _) in sheet.NonEmptyCells())
        {
            if (row > view.Region.LastDataRow)
            {
                return $"`{CellRef.A1(sheet.Name, row, column)}` sits below the table, and "
                    + "appending would write over it.";
            }
        }

        return null;
    }

    /// <summary>The cells of a row arriving from the other side, in this side's column order.</summary>
    private static IEnumerable<CellEdit> Arriving(
        MergePlan plan, TableMerge table, RowMerge row, TableView view, int at)
    {
        // The values come from the judgement, which already read them out of the other side
        // through the columns both sides share.
        var values = row.Cells.ToDictionary(c => c.Column, c => c.Theirs, StringComparer.Ordinal);

        int keyColumn = view.Region.KeyColumn - view.Region.FirstColumn;

        for (int column = 0; column < view.Columns.Count; column++)
        {
            // The key is written from the key rather than looked up, so a row cannot arrive
            // without the value that identifies it.
            string value = column == keyColumn
                ? row.Key
                : values.GetValueOrDefault(view.Columns[column], "");

            yield return new CellEdit(
                view.Region.Sheet, at, view.Region.FirstColumn + column, value);
        }
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
}
