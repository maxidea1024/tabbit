using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sylvan.Data.Excel;

namespace Tabbit.Importers.Xlsx;

/// <summary>
/// A workbook's cells, one row at a time, rendered as the text the cooker will parse.
/// </summary>
/// <remarks>
/// Streaming rather than an object model. A workbook is a sequence of sheets, each a
/// sequence of rows, and nothing before the current row is kept - which is what keeps one
/// 61 MiB workbook of 4.9 million cells inside 126 MB rather than 6,982 MB.
///
/// The rendering rules live here so that the two things this reader is asked for - a cell's
/// text and whether it is a formula error - cannot be answered differently in two places.
/// Grounds and measurements: `spec/streaming-workbook-reader.md`.
/// </remarks>
internal sealed class SheetGridReader : IDisposable
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Stream _file;
    private readonly ExcelDataReader _reader;

    /// <summary>The reader opens positioned on the first sheet, so the first move is not one.</summary>
    private bool _beforeFirstSheet = true;

    private SheetGridReader(Stream file, ExcelDataReader reader)
    {
        _file = file;
        _reader = reader;
    }

    /// <summary>
    /// Opens a workbook for reading.
    /// </summary>
    /// <remarks>
    /// The stream is ours rather than the reader's, for two reasons. `FileShare.ReadWrite`
    /// so a workbook somebody has open in Excel still reads - Excel holds its own lock, and
    /// without this a run failed on whichever workbook the designer happened to be looking
    /// at. And the format comes from the extension, because the reader cannot be asked to
    /// guess it from a stream.
    /// </remarks>
    public static SheetGridReader Open(string filename)
    {
        var type = WorkbookTypeOf(filename);
        if (type == ExcelWorkbookType.Unknown)
        {
            throw new TabbitException(
                $"`{filename}` is not a workbook this tool can read. "
                + "It reads `.xlsx`, `.xlsm`, `.xlsb` and `.xls`.");
        }

        var options = new ExcelDataReaderOptions
        {
            // Every row is data. The reader's default is to take the first row of a sheet as
            // column names, which would silently drop whichever row a layout reads first.
            Schema = ExcelSchema.NoHeaders,

            // An error cell has to arrive as an error, because what becomes of it is the
            // source entry's decision - see `OnFormulaError`. Left on, it would arrive as
            // an empty cell and that decision would never be reached.
            //
            // The reader deprecates this in favour of `FormulaErrorHandling`, and the move
            // waits on knowing which of that enum's members means what this does - the
            // package ships no documentation for it. Suppressed here rather than repository
            // wide, so the one line that needs the old name is the one line that says so.
#pragma warning disable CS0618 // Deprecated in favour of FormulaErrorHandling.
            GetErrorAsNull = false,
#pragma warning restore CS0618

            // A hidden sheet is still a sheet. Whether to read one is the recipe's call,
            // through `IncludeSheets`, and it cannot make that call about a sheet it is
            // never shown.
            ReadHiddenWorksheets = true,

            OwnsStream = false,
        };

        var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return new SheetGridReader(file, ExcelDataReader.Create(file, type, options));
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static ExcelWorkbookType WorkbookTypeOf(string filename)
        => Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" => ExcelWorkbookType.ExcelXml,
            ".xlsb" => ExcelWorkbookType.ExcelBinary,
            ".xls" => ExcelWorkbookType.Excel,
            _ => ExcelWorkbookType.Unknown,
        };

    /// <summary>
    /// Every sheet's name, in the order the workbook holds them.
    /// </summary>
    /// <remarks>
    /// Available before any sheet is read, which is what lets a sheet be declined without
    /// being parsed - the whole point of streaming, for a workbook holding a working sheet
    /// that no table covers.
    /// </remarks>
    public IEnumerable<string> SheetNames => _reader.WorksheetNames;

    /// <summary>Name of the sheet the reader is on.</summary>
    public string SheetName => _reader.WorksheetName ?? "";

    /// <summary>Moves to the next sheet, or returns false when there are none left.</summary>
    public bool MoveToNextSheet()
    {
        if (_beforeFirstSheet)
        {
            _beforeFirstSheet = false;
            return _reader.WorksheetCount > 0;
        }

        return _reader.NextResult();
    }

    /// <summary>Moves to the next row of the current sheet.</summary>
    public bool ReadRow() => _reader.Read();

    /// <summary>
    /// Which row of the sheet this is, counted from zero.
    /// </summary>
    /// <remarks>
    /// The sheet's own numbering, not a running count, and it is load-bearing: rows that
    /// hold nothing do not arrive, and <see cref="Tabbit.Models.Raw.RawSheet.Optimize"/>
    /// restores those gaps by the distance between the rows that did. The reader numbers
    /// from one as a spreadsheet does; everything downstream counts from zero.
    /// </remarks>
    public int RowIndex => _reader.RowNumber - 1;

    /// <summary>How many cells the current row has.</summary>
    public int ColumnCount => _reader.RowFieldCount;

    /// <summary>
    /// Whether a cell holds a formula error, and what Excel shows in it.
    /// </summary>
    /// <remarks>
    /// Reported rather than rendered, because refusing is the default: a `#REF!` reaching
    /// the game as a value is the whole point of checking. The caller decides, and it is
    /// the caller that knows the cell's location.
    /// </remarks>
    public bool IsFormulaError(int column, out string excelText)
    {
        if (_reader.GetExcelDataType(column) != ExcelDataType.Error)
        {
            excelText = "";
            return false;
        }

        excelText = ExcelTextOf(_reader.GetFormulaError(column));
        return true;
    }

    /// <summary>Describes an error cell the way Excel shows it, so a message names what the author sees.</summary>
    private static string ExcelTextOf(ExcelErrorCode code)
        => code switch
        {
            ExcelErrorCode.Null => "#NULL!",
            ExcelErrorCode.DivideByZero => "#DIV/0!",
            ExcelErrorCode.Value => "#VALUE!",
            ExcelErrorCode.Reference => "#REF!",
            ExcelErrorCode.Name => "#NAME?",
            ExcelErrorCode.Number => "#NUM!",
            ExcelErrorCode.NotAvailable => "#N/A",
            _ => $"error code {(int)code}",
        };

    /// <summary>
    /// A cell as the text the cooker will parse.
    /// </summary>
    /// <remarks>
    /// A formula needs no arm of its own: what the file carries is the cached result, and
    /// that is what the reader reports the type of. This tool does not evaluate formulas.
    /// </remarks>
    public string Text(int column)
    {
        switch (_reader.GetExcelDataType(column))
        {
            case ExcelDataType.Null:
                return "";

            case ExcelDataType.String:
                return _reader.GetString(column).Trim();

            case ExcelDataType.Boolean:
                return _reader.GetBoolean(column).ToString();

            case ExcelDataType.Numeric:
                return NumericText(column);

            // An error, which the caller asks about separately and decides about.
            default:
                return "";
        }
    }

    /// <summary>
    /// Renders a numeric cell, which is where a date hides.
    /// </summary>
    /// <remarks>
    /// Excel has no date type: a date is a number carrying a date format, so a cell showing
    /// 2022-01-24 10:30:00 stores 44585.4375. Feeding that through would mean `datetime`
    /// columns could never be authored as actual dates.
    ///
    /// The reader reports such a cell as numeric - the storage type is a number either way -
    /// but renders it as ISO-8601, and a plain number never comes out looking like one. So
    /// "stored as a number, reads back as ISO" is what identifies a date. A cell that really
    /// holds the text `2021-12-29` reports String and never reaches here.
    ///
    /// Plain numbers are round-trip and invariant. The default ToString() follows the
    /// machine's locale, so a comma decimal separator would reach a parse that expects a
    /// dot, and drops to scientific notation for large magnitudes, which no integer parse
    /// accepts.
    /// </remarks>
    private string NumericText(int column)
    {
        string rendered = _reader.GetString(column);

        if (LooksLikeIsoDate(rendered))
            return _reader.GetDateTime(column).ToString("yyyy-MM-dd HH:mm:ss", Inv);

        return _reader.GetDouble(column).ToString("R", Inv).Trim();
    }

    /// <summary>Whether a rendering is `yyyy-MM-dd`, with or without a time after it.</summary>
    private static bool LooksLikeIsoDate(string text)
        => text is not null
           && text.Length >= 10
           && text[4] == '-' && text[7] == '-'
           && char.IsAsciiDigit(text[0]) && char.IsAsciiDigit(text[5]) && char.IsAsciiDigit(text[8]);

    public void Dispose()
    {
        _reader.Dispose();
        _file.Dispose();
    }
}
