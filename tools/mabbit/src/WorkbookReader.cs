using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sylvan.Data.Excel;

namespace Mabbit;

/// <summary>
/// Reads a workbook a sheet at a time, a row at a time.
/// </summary>
/// <remarks>
/// Streaming rather than an object model, because a merge is asked for at the moment somebody
/// is blocked at a prompt and the workbooks it is asked about are the large ones - the files
/// two people are both editing are the files the project keeps its data in.
///
/// Values come back as text. What a cell means - a number, a date, a label of an enum - is a
/// question a merge never has to answer: it compares three files read here, in one run, by
/// this same code, so any consistent reading of a cell gives the same answer about whether it
/// changed.
/// </remarks>
internal sealed class WorkbookReader : IDisposable
{
    private readonly Stream _file;
    private readonly ExcelDataReader _reader;

    private WorkbookReader(Stream file, ExcelDataReader reader)
    {
        _file = file;
        _reader = reader;
    }

    /// <param name="path">The file to open.</param>
    /// <param name="formatFrom">
    /// A name whose extension says what format the file is in, for a file that did not arrive
    /// under its own name.
    /// </param>
    /// <remarks>
    /// The second parameter is what makes this usable as a merge driver at all. Git hands its
    /// tools the two sides of a conflict as temporary files - `.merge_file_a1b2c3`, no
    /// extension - and the format has to come from the path the repository knows the file by.
    /// </remarks>
    public static WorkbookReader Open(string path, string? formatFrom = null)
    {
        string namesTheFormat = string.IsNullOrEmpty(formatFrom) ? path : formatFrom;

        var type = WorkbookTypeOf(namesTheFormat);

        if (type == ExcelWorkbookType.Unknown)
        {
            throw new MabbitException(
                $"`{namesTheFormat}` is not a workbook this tool can read. It reads "
                + "`.xlsx`, `.xlsm`, `.xlsb` and `.xls`. Pass `--path` with the name the "
                + "repository knows the file by when the file itself has no extension.");
        }

        var options = new ExcelDataReaderOptions
        {
            // Every row is data. The reader's default takes the first row of a sheet as
            // column names, which would hide whichever row a table's headings are on - and
            // where the headings are is the schema's decision, not the reader's.
            Schema = ExcelSchema.NoHeaders,

            // An error cell arrives as an error so it can be read as the text a spreadsheet
            // shows for it. Left alone it would arrive as an empty cell, and clearing a cell
            // is a change a merge must not invent.
#pragma warning disable CS0618 // Deprecated in favour of FormulaErrorHandling, which is undocumented.
            GetErrorAsNull = false,
#pragma warning restore CS0618

            // A hidden sheet is still a sheet, and two people can conflict in one.
            ReadHiddenWorksheets = true,

            OwnsStream = false,
        };

        // `FileShare.ReadWrite`, because the person resolving the conflict may well have the
        // workbook open in Excel while they do it. Excel holds its own lock, and without this
        // the merge would fail on the file they are looking at.
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        try
        {
            return new WorkbookReader(file, ExcelDataReader.Create(file, type, options));
        }
        catch (Exception error)
        {
            file.Dispose();

            throw new MabbitException($"`{path}` could not be read as a workbook: {error.Message}");
        }
    }

    private static ExcelWorkbookType WorkbookTypeOf(string name)
        => Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" => ExcelWorkbookType.ExcelXml,
            ".xlsb" => ExcelWorkbookType.ExcelBinary,
            ".xls" => ExcelWorkbookType.Excel,
            _ => ExcelWorkbookType.Unknown,
        };

    public string SheetName => _reader.WorksheetName ?? "";

    /// <summary>
    /// Moves to the next sheet, or answers false when there are none left.
    /// </summary>
    /// <remarks>
    /// The reader is already positioned on the first sheet when it is created, so the first
    /// call must not advance - doing so skips sheet one, which is invisible in a workbook of
    /// twenty and total in a workbook of one.
    /// </remarks>
    public bool MoveToNextSheet()
    {
        if (_beforeFirstSheet)
        {
            _beforeFirstSheet = false;
            return _reader.WorksheetCount > 0;
        }

        return _reader.NextResult();
    }

    private bool _beforeFirstSheet = true;

    public bool ReadRow() => _reader.Read();

    /// <summary>The row's position in the sheet, counted from zero.</summary>
    public int RowIndex => _reader.RowNumber - 1;

    public int ColumnCount => _reader.RowFieldCount;

    /// <summary>
    /// The cell as text.
    /// </summary>
    /// <remarks>
    /// A number is rendered through the invariant culture, so the same workbook reads the
    /// same way on a machine whose decimal separator is a comma. Getting that wrong would not
    /// fail - it would report every number in the file as changed, on one colleague's machine
    /// and not the other's.
    /// </remarks>
    public string Text(int column)
    {
        if (column >= _reader.RowFieldCount)
            return "";

        if (_reader.GetExcelDataType(column) == ExcelDataType.Error)
            return ErrorText(_reader.GetFormulaError(column));

        return _reader.GetExcelDataType(column) switch
        {
            ExcelDataType.Null => "",
            ExcelDataType.Numeric => NumericText(column),
            ExcelDataType.Boolean => _reader.GetBoolean(column) ? "TRUE" : "FALSE",
            _ => _reader.GetString(column) ?? "",
        };
    }

    /// <summary>
    /// A numeric cell as text.
    /// </summary>
    /// <remarks>
    /// Round-trip formatting, so a value is never widened or narrowed on the way into a
    /// comparison. A fixed number of decimal places would make two different cells read the
    /// same, and a merge would then keep whichever it happened to see first.
    /// </remarks>
    private string NumericText(int column)
    {
        double value = _reader.GetDouble(column);

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>What a spreadsheet shows in a cell whose formula did not work out.</summary>
    private static string ErrorText(ExcelErrorCode code) => code switch
    {
        ExcelErrorCode.Null => "#NULL!",
        ExcelErrorCode.DivideByZero => "#DIV/0!",
        ExcelErrorCode.Value => "#VALUE!",
        ExcelErrorCode.Reference => "#REF!",
        ExcelErrorCode.Name => "#NAME?",
        ExcelErrorCode.Number => "#NUM!",
        ExcelErrorCode.NotAvailable => "#N/A",
        _ => "#ERR!",
    };

    public void Dispose()
    {
        _reader.Dispose();
        _file.Dispose();
    }
}
