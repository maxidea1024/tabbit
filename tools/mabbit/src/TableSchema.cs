using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mabbit;

/// <summary>
/// One table's boundaries within a sheet, and which of its columns identifies a row.
/// </summary>
/// <remarks>
/// Everything the merge needs to know about a workbook's structure, and nothing else. What a
/// column's type is, what an enum label means, what a reference points at - none of that
/// changes which row is which, and resolving any of it needs workbooks other than the one
/// being merged. spec/import/workbook-merge.md section 4.1.
/// </remarks>
internal sealed record TableRegion(
    string Name,
    string Sheet,
    int HeaderRow,
    int FirstDataRow,
    int LastDataRow,
    int FirstColumn,
    int LastColumn,
    int KeyColumn);

/// <summary>
/// Where the tables of a workbook are.
/// </summary>
/// <remarks>
/// An interface with more than one implementation on purpose. The accurate answer comes from
/// the recipe that the conversion already reads, and the tool has to work without one - on a
/// workbook from outside this repository, or before anybody has written a recipe for it. The
/// merge itself sees only this, so neither case is a special path through it.
/// </remarks>
internal interface ITableSchema
{
    IReadOnlyList<TableRegion> TablesIn(WorkbookGrid workbook);
}

/// <summary>
/// Takes each sheet as one table: the first row that holds anything is the header, the
/// column below the first heading identifies the row.
/// </summary>
/// <remarks>
/// What somebody would assume looking at a sheet, which is what makes it the right guess to
/// make in the absence of a recipe. It is a guess all the same, so the key column can be said
/// outright - and a merge that matched rows by the wrong column would report every row as
/// changed, which is loud rather than quiet. That is the failure this can afford to have.
/// </remarks>
internal sealed class HeuristicSchema : ITableSchema
{
    private readonly IReadOnlyDictionary<string, string> _keyColumns;

    /// <param name="keyColumns">
    /// Sheet name to the column that identifies a row, as a heading or a column letter.
    /// </param>
    public HeuristicSchema(IReadOnlyDictionary<string, string>? keyColumns = null)
    {
        _keyColumns = keyColumns ?? new Dictionary<string, string>();
    }

    public IReadOnlyList<TableRegion> TablesIn(WorkbookGrid workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var regions = new List<TableRegion>();

        foreach (var sheet in workbook.Sheets)
        {
            // A sheet with nothing in it is not a table with no rows: there is no header to
            // say what its columns are, so there is nothing to compare against a sheet that
            // does have one.
            if (sheet.IsEmpty)
                continue;

            int headerRow = sheet.FirstRow;

            regions.Add(new TableRegion(
                Name: sheet.Name,
                Sheet: sheet.Name,
                HeaderRow: headerRow,
                FirstDataRow: headerRow + 1,
                LastDataRow: sheet.LastRow,
                FirstColumn: sheet.FirstColumn,
                LastColumn: sheet.LastColumn,
                KeyColumn: KeyColumnOf(sheet, headerRow)));
        }

        return regions;
    }

    private int KeyColumnOf(GridSheet sheet, int headerRow)
    {
        if (!_keyColumns.TryGetValue(sheet.Name, out string? asked))
            return sheet.FirstColumn;

        // A column letter first, because that is unambiguous and a heading could be one.
        int? byLetter = CellRef.ColumnOf(asked);
        if (byLetter is int letter && letter >= sheet.FirstColumn && letter <= sheet.LastColumn)
            return letter;

        for (int column = sheet.FirstColumn; column <= sheet.LastColumn; column++)
        {
            if (string.Equals(sheet.Cell(headerRow, column), asked, StringComparison.OrdinalIgnoreCase))
                return column;
        }

        throw new MabbitException(
            $"Sheet `{sheet.Name}` has no column `{asked}` to identify its rows by. "
            + $"Its headings are on row {headerRow + 1}.");
    }
}

/// <summary>Column letters, both ways.</summary>
internal static class CellRef
{
    /// <summary>A cell as a spreadsheet writes it, from the zero based coordinates held here.</summary>
    public static string A1(string sheet, int row, int column)
        => $"{sheet}!{ColumnName(column)}{(row + 1).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>`0` to `A`, `26` to `AA`.</summary>
    public static string ColumnName(int column)
    {
        if (column < 0)
            return "?";

        string name = "";

        for (int value = column + 1; value > 0; value = (value - 1) / 26)
            name = (char)('A' + ((value - 1) % 26)) + name;

        return name;
    }

    /// <summary>`A` to `0`, or null when the text is not a column letter at all.</summary>
    public static int? ColumnOf(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        int column = 0;

        foreach (char c in text)
        {
            char upper = char.ToUpperInvariant(c);
            if (upper is < 'A' or > 'Z')
                return null;

            column = (column * 26) + (upper - 'A' + 1);
        }

        return column - 1;
    }
}

/// <summary>What this tool refuses to do, said in words a person can act on.</summary>
internal sealed class MabbitException : Exception
{
    public MabbitException(string message) : base(message) { }
}
