using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mabbit;

/// <summary>
/// A workbook's tables, as the comparison sees them.
/// </summary>
/// <remarks>
/// The one place a schema's answer becomes something to compare. Both sides of a comparison
/// go through it, so a schema that names a sheet neither file has fails once and says which
/// table it was looking for.
/// </remarks>
internal static class TableViews
{
    public static List<TableView> Of(WorkbookGrid workbook, ITableSchema schema)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(schema);

        var views = new List<TableView>();

        foreach (var region in schema.TablesIn(workbook))
        {
            var sheet = workbook.Sheet(region.Sheet)
                ?? throw new MabbitException(
                    $"`{workbook.Name}` has no sheet `{region.Sheet}`, which the schema names "
                    + $"as holding table `{region.Name}`.");

            views.Add(TableView.Of(sheet, region));
        }

        return views;
    }
}

/// <summary>One row, under the key that follows it from one file to another.</summary>
internal sealed class RowView
{
    internal RowView(string key, int rowIndex, string[] cells)
    {
        Key = key;
        RowIndex = rowIndex;
        Cells = cells;
    }

    /// <summary>The key column's text. What identifies this row, rather than where it sits.</summary>
    public string Key { get; }

    /// <summary>Where the row is in the sheet, for the report and for writing back.</summary>
    public int RowIndex { get; }

    /// <summary>The row's cells, one per column of the table, in the table's column order.</summary>
    public string[] Cells { get; }
}

/// <summary>
/// A table as the merge sees it: named columns, and rows addressed by key.
/// </summary>
/// <remarks>
/// The step that turns a rectangle into something two files can be compared through. Nothing
/// here interprets a value; what it does is decide what a column is called and which row is
/// which, because those are the two things both sides have to agree on before any comparison
/// means anything.
/// </remarks>
internal sealed class TableView
{
    private readonly Dictionary<string, RowView> _byKey;

    private TableView(
        TableRegion region,
        IReadOnlyList<string> columns,
        IReadOnlyList<RowView> rows,
        Dictionary<string, RowView> byKey,
        IReadOnlyList<RowView> unkeyed,
        IReadOnlyList<RowView> duplicates)
    {
        Region = region;
        Columns = columns;
        Rows = rows;
        Unkeyed = unkeyed;
        Duplicates = duplicates;
        _byKey = byKey;
    }

    public TableRegion Region { get; }

    public string Name => Region.Name;

    /// <summary>The column headings, in sheet order.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The rows that have a key, in sheet order.</summary>
    public IReadOnlyList<RowView> Rows { get; }

    /// <summary>
    /// Rows whose key cell is empty.
    /// </summary>
    /// <remarks>
    /// Kept rather than dropped. A row with no key cannot be followed from one file to
    /// another, so the merge cannot say anything about it - and a merge that quietly ignored
    /// a row would be the failure this tool exists to prevent. They are reported instead.
    /// </remarks>
    public IReadOnlyList<RowView> Unkeyed { get; }

    /// <summary>Rows repeating a key an earlier row already used.</summary>
    public IReadOnlyList<RowView> Duplicates { get; }

    public RowView? ByKey(string key)
        => _byKey.TryGetValue(key, out var row) ? row : null;

    public static TableView Of(GridSheet sheet, TableRegion region)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(region);

        var columns = ColumnsOf(sheet, region);

        int width = region.LastColumn - region.FirstColumn + 1;
        int keyOffset = region.KeyColumn - region.FirstColumn;

        var rows = new List<RowView>();
        var unkeyed = new List<RowView>();
        var duplicates = new List<RowView>();
        var byKey = new Dictionary<string, RowView>(StringComparer.Ordinal);

        for (int row = region.FirstDataRow; row <= region.LastDataRow; row++)
        {
            // A blank row inside a table is a spacer somebody left, not a row with every
            // cell cleared: it has no key either way, and reporting one per blank line
            // would bury the rows that do need attention.
            if (sheet.IsBlankRow(row, region.FirstColumn, region.LastColumn))
                continue;

            var cells = new string[width];
            for (int i = 0; i < width; i++)
                cells[i] = sheet.Cell(row, region.FirstColumn + i);

            var view = new RowView(cells[keyOffset], row, cells);

            if (view.Key.Length == 0)
            {
                unkeyed.Add(view);
                continue;
            }

            // The first row wins, and the rest are reported. Which one to keep is not a
            // decision this can make correctly - that is what the report is for - and
            // picking one keeps the comparison running so the person sees everything else
            // that changed as well.
            if (!byKey.TryAdd(view.Key, view))
            {
                duplicates.Add(view);
                continue;
            }

            rows.Add(view);
        }

        return new TableView(region, columns, rows, byKey, unkeyed, duplicates);
    }

    /// <summary>
    /// What each column is called, which is what the two sides match columns by.
    /// </summary>
    /// <remarks>
    /// A blank heading becomes its column letter and a repeated one is numbered, because a
    /// name has to address exactly one column for a cell change to be able to say which
    /// column it is in. Both are written the way a person would read them back off the
    /// sheet - `(C)` is the third column, `price #2` is the second column headed `price`.
    /// </remarks>
    private static IReadOnlyList<string> ColumnsOf(GridSheet sheet, TableRegion region)
    {
        var columns = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int column = region.FirstColumn; column <= region.LastColumn; column++)
        {
            string heading = sheet.Cell(region.HeaderRow, column).Trim();

            if (heading.Length == 0)
                heading = $"({CellRef.ColumnName(column)})";

            if (seen.TryGetValue(heading, out int count))
            {
                seen[heading] = count + 1;
                heading = $"{heading} #{(count + 1).ToString(CultureInfo.InvariantCulture)}";
            }
            else
            {
                seen[heading] = 1;
            }

            columns.Add(heading);
        }

        return columns;
    }
}
