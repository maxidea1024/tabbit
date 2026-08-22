using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tabbit.Importers.Xlsx;

/// <summary>
/// Gets back the cells the binary workbook's cell reader drops.
/// </summary>
/// <remarks>
/// The reader hands over some rows shorter than they are: the file holds a value at a
/// column, the row header declares the row reaches it, Excel itself reads it, and the reader
/// reports a field count that stops before it. Asking for that column returns an empty value
/// rather than an error, so a caller cannot tell a dropped cell from a blank one.
///
/// Measured on the sample project: 2,226 rows of 718,764, and at least 126,468 cells. Small
/// as a share and not as a consequence - four dropped cells emptied a `required` column,
/// which took a table of 79,181 rows out of the conversion.
///
/// **What it does not do is rebuild every cell.** The reader is right about 99.7% of rows,
/// and re-rendering those would mean reimplementing number formats and shared-string runs -
/// a rewrite whose output would differ from the reader's in ways nobody could attribute.
/// Only the short rows are read from the file.
///
/// spec/xlsb-short-row-repair.md.
/// </remarks>
internal sealed class XlsbRowRepair : IDisposable
{
    private readonly ZipArchive _zip;

    /// <summary>Sheet name to the part holding it, as the workbook orders them.</summary>
    private readonly Dictionary<string, string> _parts;

    /// <summary>The part of the sheet being read, and how far each of its rows reaches.</summary>
    private string? _sheetPart;
    private Dictionary<int, int>? _lastValueColumn;

    /// <summary>Rows of the current sheet the reader gave short, and how short.</summary>
    private readonly Dictionary<int, int> _damaged = new Dictionary<int, int>();

    /// <summary>How many rows this workbook needed repairing, for the run to report.</summary>
    public int RepairedRows { get; private set; }

    private XlsbRowRepair(ZipArchive zip, Dictionary<string, string> parts)
    {
        _zip = zip;
        _parts = parts;
    }

    /// <summary>
    /// Opens a binary workbook for repair, or returns null when it is not one this can help.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw for anything unexpected: the repair is a correction to
    /// another component's output, and a workbook it cannot map is a workbook that reads
    /// exactly as well as it did before this existed.
    /// </remarks>
    public static XlsbRowRepair? TryOpen(string filename)
    {
        if (!string.Equals(Path.GetExtension(filename), ".xlsb", StringComparison.OrdinalIgnoreCase))
            return null;

        FileStream? file = null;
        try
        {
            file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var zip = new ZipArchive(file, ZipArchiveMode.Read);

            var parts = MapSheetsToParts(zip);
            if (parts.Count == 0)
            {
                zip.Dispose();
                return null;
            }

            return new XlsbRowRepair(zip, parts);
        }
        catch (Exception)
        {
            file?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Which part holds which sheet, through the workbook's sheet list and its relationships.
    /// </summary>
    /// <remarks>
    /// The same two steps the XML side takes: the workbook names its sheets in order and
    /// points at each one by relationship id, and the relationships say where the part is.
    /// Neither half can be guessed from the part names, which are only conventionally in
    /// sheet order.
    /// </remarks>
    private static Dictionary<string, string> MapSheetsToParts(ZipArchive zip)
    {
        var byRelationship = new Dictionary<string, string>(StringComparer.Ordinal);

        var relsEntry = zip.GetEntry("xl/_rels/workbook.bin.rels");
        if (relsEntry is not null)
        {
            using var stream = relsEntry.Open();
            using var text = new StreamReader(stream);
            string xml = text.ReadToEnd();

            foreach (Match match in Regex.Matches(
                xml, "Id=\"([^\"]+)\"[^>]*Target=\"([^\"]+)\"", RegexOptions.CultureInvariant))
            {
                byRelationship[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        var parts = new Dictionary<string, string>(StringComparer.Ordinal);

        var workbook = zip.GetEntry("xl/workbook.bin");
        if (workbook is null)
            return parts;

        byte[] bytes;
        using (var stream = workbook.Open())
            bytes = XlsbRecords.Read(stream, workbook.Length);

        foreach (var (type, body) in XlsbRecords.Walk(bytes))
        {
            if (type != 156 || body.Count < 12)   // BrtBundleSh
                continue;

            int at = 8;
            uint relLength = XlsbRecords.U32(body, at);

            string relationship = relLength == 0xFFFFFFFF ? "" : XlsbRecords.WideString(body, at);
            at += 4 + (relLength == 0xFFFFFFFF ? 0 : (int)relLength * 2);

            string sheet = XlsbRecords.WideString(body, at);

            if (sheet.Length == 0 || !byRelationship.TryGetValue(relationship, out string? target))
                continue;

            parts[sheet.Trim()] = "xl/" + target.TrimStart('/');
        }

        return parts;
    }

    /// <summary>
    /// Learns how far each row of a sheet reaches, ahead of reading its cells.
    /// </summary>
    /// <remarks>
    /// Column numbers only - the bodies are skipped by their length - so this is one pass
    /// over the part with no values decoded. Measured over the sample project's binary
    /// workbooks: one second against the 1.6 the cell reader takes, and that is the whole
    /// cost when nothing turns out to be damaged.
    /// </remarks>
    public void BeginSheet(string sheetName)
    {
        _sheetPart = null;
        _lastValueColumn = null;
        _damaged.Clear();

        if (!_parts.TryGetValue(sheetName.Trim(), out string? part))
            return;

        var entry = _zip.GetEntry(part);
        if (entry is null)
            return;

        _sheetPart = part;
        _lastValueColumn = new Dictionary<int, int>();

        byte[] bytes;
        using (var stream = entry.Open())
            bytes = XlsbRecords.Read(stream, entry.Length);

        int row = -1;
        int last = -1;

        foreach (var (type, body) in XlsbRecords.Walk(bytes))
        {
            if (type == XlsbRecords.RowHeader && body.Count >= 4)
            {
                if (row >= 0 && last >= 0)
                    _lastValueColumn[row] = last;

                row = (int)XlsbRecords.U32(body, 0);
                last = -1;
            }
            else if (XlsbRecords.IsValueCell(type) && body.Count >= 4)
            {
                int column = XlsbRecords.ColumnOf(body);
                if (column > last) last = column;
            }
        }

        if (row >= 0 && last >= 0)
            _lastValueColumn[row] = last;
    }

    /// <summary>
    /// How many columns a row really has, given what the reader said it has.
    /// </summary>
    /// <remarks>
    /// The reader's own count whenever it reaches as far as the file does, which is nearly
    /// always. A row the reader gave short is remembered here and read from the file when
    /// the sheet ends.
    /// </remarks>
    /// <param name="rowIndex">The sheet's own row number, counted from zero.</param>
    public int ColumnCount(int rowIndex, int reported)
    {
        if (_lastValueColumn is null
            || !_lastValueColumn.TryGetValue(rowIndex, out int last)
            || last < reported)
        {
            return reported;
        }

        _damaged[rowIndex] = reported;
        return last + 1;
    }

    /// <summary>Whether anything in this sheet has to be read back from the file.</summary>
    public bool SheetIsDamaged => _damaged.Count > 0;

    /// <summary>
    /// The cells the reader dropped, keyed by row and column.
    /// </summary>
    /// <remarks>
    /// Two more passes, and only when a sheet turned out to be damaged: one over the sheet
    /// decoding the rows in question, and one over the shared strings resolving whichever
    /// of them those rows referred to. A sheet with nothing wrong pays for neither.
    /// </remarks>
    public IReadOnlyDictionary<(int Row, int Column), XlsbCell> Recover()
    {
        var recovered = new Dictionary<(int, int), XlsbCell>();

        if (_sheetPart is null || _damaged.Count == 0)
            return recovered;

        var entry = _zip.GetEntry(_sheetPart);
        if (entry is null)
            return recovered;

        byte[] bytes;
        using (var stream = entry.Open())
            bytes = XlsbRecords.Read(stream, entry.Length);

        var wanted = new HashSet<uint>();
        int row = -1;
        bool interesting = false;
        int from = 0;

        foreach (var (type, body) in XlsbRecords.Walk(bytes))
        {
            if (type == XlsbRecords.RowHeader && body.Count >= 4)
            {
                row = (int)XlsbRecords.U32(body, 0);
                interesting = _damaged.TryGetValue(row, out from);
                continue;
            }

            if (!interesting || body.Count < 4)
                continue;

            int column = XlsbRecords.ColumnOf(body);

            // Only the columns the reader did not reach. The ones it did are its own, and
            // taking those from here would be a second rendering of the same cell.
            if (column < from)
                continue;

            var cell = Decode(type, body, wanted);
            if (cell is not null)
                recovered[(row, column)] = cell.Value;
        }

        if (wanted.Count > 0)
            ResolveSharedStrings(wanted, recovered);

        RepairedRows += _damaged.Count;
        return recovered;
    }

    /// <summary>One cell record as a value, or null when the record carries none.</summary>
    private static XlsbCell? Decode(int type, ArraySegment<byte> body, HashSet<uint> wantedStrings)
    {
        switch (type)
        {
            case XlsbRecords.CellRk when body.Count >= 12:
                return XlsbCell.Number(XlsbRecords.Rk(XlsbRecords.U32(body, 8)));

            case XlsbRecords.CellReal when body.Count >= 16:
            case XlsbRecords.FormulaNum when body.Count >= 16:
                return XlsbCell.Number(BitConverter.ToDouble(body.Array!, body.Offset + 8));

            case XlsbRecords.CellBool when body.Count >= 9:
            case XlsbRecords.FormulaBool when body.Count >= 9:
                return XlsbCell.Boolean(body.Array![body.Offset + 8] != 0);

            case XlsbRecords.CellError when body.Count >= 9:
            case XlsbRecords.FormulaError when body.Count >= 9:
                return XlsbCell.Error(body.Array![body.Offset + 8]);

            case XlsbRecords.CellSt when body.Count >= 12:
            case XlsbRecords.FormulaString when body.Count >= 12:
                return XlsbCell.Text(XlsbRecords.WideString(body, 8));

            case XlsbRecords.CellIsst when body.Count >= 12:
            {
                uint index = XlsbRecords.U32(body, 8);
                wantedStrings.Add(index);
                return XlsbCell.SharedString(index);
            }

            // A blank cell is a cell with formatting and no value, which is what the reader
            // would have given here anyway.
            default:
                return null;
        }
    }

    /// <summary>
    /// Fills in the strings the recovered cells pointed at.
    /// </summary>
    /// <remarks>
    /// One pass over the table, keeping only the entries asked for. The table of the largest
    /// sample workbook holds 141,279 of them and a repair wants a handful, so it is walked
    /// rather than held.
    /// </remarks>
    private void ResolveSharedStrings(
        HashSet<uint> wanted, Dictionary<(int, int), XlsbCell> recovered)
    {
        var entry = _zip.GetEntry("xl/sharedStrings.bin");
        if (entry is null)
            return;

        byte[] bytes;
        using (var stream = entry.Open())
            bytes = XlsbRecords.Read(stream, entry.Length);

        var text = new Dictionary<uint, string>();
        uint at = 0;

        foreach (var (type, body) in XlsbRecords.Walk(bytes))
        {
            if (type != XlsbRecords.SharedStringItem)
                continue;

            if (wanted.Contains(at) && body.Count >= 5)
                text[at] = XlsbRecords.WideString(body, 1);

            at++;
            if (text.Count == wanted.Count)
                break;
        }

        foreach (var key in recovered.Keys.ToList())
        {
            var cell = recovered[key];
            if (cell.Kind == XlsbCellKind.SharedString)
            {
                recovered[key] = text.TryGetValue(cell.StringIndex, out string? found)
                    ? XlsbCell.Text(found)
                    : XlsbCell.Text("");
            }
        }
    }

    public void Dispose() => _zip.Dispose();
}

internal enum XlsbCellKind
{
    Text,
    Number,
    Boolean,
    Error,
    SharedString,
}

/// <summary>A cell read back out of the file, before it is rendered as text.</summary>
internal readonly struct XlsbCell
{
    public XlsbCellKind Kind { get; private init; }
    public string String { get; private init; }
    public double Value { get; private init; }
    public uint StringIndex { get; private init; }

    public static XlsbCell Text(string text)
        => new XlsbCell { Kind = XlsbCellKind.Text, String = text };

    public static XlsbCell Number(double value)
        => new XlsbCell { Kind = XlsbCellKind.Number, Value = value };

    public static XlsbCell Boolean(bool value)
        => new XlsbCell { Kind = XlsbCellKind.Boolean, Value = value ? 1 : 0 };

    public static XlsbCell Error(byte code)
        => new XlsbCell { Kind = XlsbCellKind.Error, Value = code };

    public static XlsbCell SharedString(uint index)
        => new XlsbCell { Kind = XlsbCellKind.SharedString, StringIndex = index };
}
