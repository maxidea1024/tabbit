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
/// ranges and formulas that point at them along. That is stage 4 of spec/workbook-merge.md.
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

    public static WritePlan Prepare(MergePlan plan, IReadOnlyList<TableView> mine)
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

            foreach (var row in table.Rows)
            {
                switch (row.Verdict)
                {
                    case RowVerdict.AddFromTheirs:
                        refusals.Add(new Refusal(
                            $"row `{row.Key}` of `{table.Name}` arrives from the other side, "
                            + "which needs a row to be inserted."));
                        continue;

                    case RowVerdict.RemoveFromMine:
                        refusals.Add(new Refusal(
                            $"row `{row.Key}` of `{table.Name}` was deleted on the other side, "
                            + "which needs a row to be removed."));
                        continue;
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
