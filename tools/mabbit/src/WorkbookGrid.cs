using System;
using System.Collections.Generic;
using System.Linq;

namespace Mabbit;

/// <summary>
/// A workbook read as text, keeping the coordinates each cell actually has in its sheet.
/// </summary>
/// <remarks>
/// Nothing is trimmed, padded or squared off. A merge has to write back to the cell a value
/// came from, so the coordinates have to stay the sheet's own; and where a table starts is
/// the schema's decision, not the reader's.
///
/// Values are text and nothing else. A merge compares three files read by this same code in
/// one run, so there is nothing for a canonical form to reconcile - and interpreting a value
/// would mean resolving the types, which needs workbooks other than the one being merged.
/// spec/import/workbook-merge.md section 4.1.
/// </remarks>
internal sealed class WorkbookGrid
{
    private readonly Dictionary<string, GridSheet> _byName;

    private WorkbookGrid(string name, IReadOnlyList<GridSheet> sheets)
    {
        Name = name;
        Sheets = sheets;

        // Sheet names are case insensitive to Excel, and a workbook cannot hold two that
        // differ only in case - so matching them that way cannot introduce an ambiguity
        // and does answer for a sheet somebody recased.
        _byName = sheets.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What reports call this file. The path it was read from, unless told otherwise.</summary>
    public string Name { get; }

    public IReadOnlyList<GridSheet> Sheets { get; }

    public GridSheet? Sheet(string name)
        => _byName.TryGetValue(name, out var sheet) ? sheet : null;

    /// <param name="path">The file to read.</param>
    /// <param name="formatFrom">
    /// A name whose extension says what format the file is in, for a file that does not
    /// arrive under its own name.
    /// </param>
    /// <param name="reportAs">What to call this file in reports. The path, when left out.</param>
    public static WorkbookGrid Read(string path, string? formatFrom = null, string? reportAs = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var sheets = new List<GridSheet>();

        using var reader = WorkbookReader.Open(path, formatFrom);

        while (reader.MoveToNextSheet())
        {
            string sheetName = reader.SheetName.Trim();
            var rows = new Dictionary<int, string[]>();

            while (reader.ReadRow())
            {
                int columnCount = reader.ColumnCount;
                if (columnCount == 0)
                    continue;

                var cells = new string[columnCount];

                for (int column = 0; column < columnCount; column++)
                    cells[column] = reader.Text(column);

                rows[reader.RowIndex] = cells;
            }

            sheets.Add(new GridSheet(sheetName, rows));
        }

        return new WorkbookGrid(reportAs ?? path, sheets);
    }

    /// <summary>Builds a workbook from literal rows, for the tests.</summary>
    public static WorkbookGrid Of(string name, params (string Sheet, string[][] Rows)[] sheets)
    {
        var built = new List<GridSheet>();

        foreach (var (sheet, rows) in sheets)
        {
            var byIndex = new Dictionary<int, string[]>();

            for (int row = 0; row < rows.Length; row++)
                byIndex[row] = rows[row];

            built.Add(new GridSheet(sheet, byIndex));
        }

        return new WorkbookGrid(name, built);
    }
}

/// <summary>One sheet's cells, addressed by the row and column they occupy in the sheet.</summary>
internal sealed class GridSheet
{
    private readonly Dictionary<int, string[]> _rows;

    internal GridSheet(string name, Dictionary<int, string[]> rows)
    {
        Name = name;
        _rows = rows;

        FirstRow = -1;
        LastRow = -1;
        FirstColumn = -1;
        LastColumn = -1;

        foreach (var (row, cells) in rows)
        {
            for (int column = 0; column < cells.Length; column++)
            {
                if (cells[column].Length == 0)
                    continue;

                if (FirstRow < 0 || row < FirstRow) FirstRow = row;
                if (row > LastRow) LastRow = row;
                if (FirstColumn < 0 || column < FirstColumn) FirstColumn = column;
                if (column > LastColumn) LastColumn = column;
            }
        }
    }

    public string Name { get; }

    /// <summary>
    /// The rectangle that holds every cell with something in it, or all -1 for an empty sheet.
    /// </summary>
    /// <remarks>
    /// Reported rather than applied: what part of a sheet is a table is the schema's decision,
    /// and this is the one fact about the sheet that every schema starts from.
    /// </remarks>
    public int FirstRow { get; }
    public int LastRow { get; }
    public int FirstColumn { get; }
    public int LastColumn { get; }

    public bool IsEmpty => FirstRow < 0;

    /// <summary>The cell's text, or an empty string where the sheet holds nothing.</summary>
    public string Cell(int row, int column)
    {
        if (!_rows.TryGetValue(row, out var cells))
            return "";

        return column >= 0 && column < cells.Length ? cells[column] : "";
    }

    /// <summary>
    /// Every cell the sheet actually holds something in, in a fixed order.
    /// </summary>
    /// <remarks>
    /// Ordered so that the same sheet always produces the same sequence: this feeds the
    /// fingerprint of what lies outside the tables, and a hash over an unordered walk would
    /// differ between two runs over identical files.
    /// </remarks>
    public IEnumerable<(int Row, int Column, string Value)> NonEmptyCells()
    {
        foreach (int row in _rows.Keys.Order())
        {
            var cells = _rows[row];

            for (int column = 0; column < cells.Length; column++)
            {
                if (cells[column].Length != 0)
                    yield return (row, column, cells[column]);
            }
        }
    }

    /// <summary>Whether every cell of a row within the given columns is empty.</summary>
    public bool IsBlankRow(int row, int firstColumn, int lastColumn)
    {
        for (int column = firstColumn; column <= lastColumn; column++)
        {
            if (Cell(row, column).Length != 0)
                return false;
        }

        return true;
    }
}
