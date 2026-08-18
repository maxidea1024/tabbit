using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tabbit.Importers.Xlsx;

/// <summary>
/// Reads a binary workbook's defined names out of `xl/workbook.bin`.
/// </summary>
/// <remarks>
/// The binary package holds no `xl/workbook.xml`, so the XML path comes back with nothing -
/// silently, which for a layout that finds its tables by name means a run that succeeds with
/// zero tables. The part that replaces it is a stream of records, each `[type][length][body]`
/// with the type and length as 7-bit variable-length integers; the records this reader cares
/// about are the ones that carry what `xl/workbook.xml` spells as elements. The format is
/// Microsoft's published MS-XLSB, and the design note is spec/xlsb-defined-names.md.
///
/// Where the XML path parses a reference string like `'Ocean Zone'!$A$1:$IP$100`, here the
/// reference arrives already tokenized: one `PtgArea3d` token is one rectangle, with the
/// rows and columns as integers. The shapes the string path filters out by their spelling -
/// a union, a whole column, another workbook - are different tokens or different lengths,
/// so the same decisions fall out of the token instead.
///
/// One indirection has no XML counterpart: the token names its sheet through the XTI table
/// (`BrtExternSheet`), whose entries point at a supporting-workbook record and a sheet range.
/// Only an entry that points at `BrtSupSelf` with a one-sheet range names a sheet of this
/// workbook; the index is into the sheets in `BrtBundleSh` order. Treating the token's index
/// as a sheet number instead reads the wrong sheet - a 140-sheet workbook was measured
/// holding an XTI of 190 entries.
/// </remarks>
internal static class BinaryDefinedNames
{
    private const int BrtName = 39;
    private const int BrtBundleSh = 156;
    private const int BrtSupBookSrc = 355;
    private const int BrtSupSelf = 357;
    private const int BrtSupSame = 358;
    private const int BrtSupTabs = 359;
    private const int BrtExternSheet = 362;

    // A `Ptg` token's bits 5-6 carry its value class, so each of these has a ref, a value
    // and an array spelling - and the class cannot be masked off blindly, because the codes
    // below 0x20 are different tokens rather than declassified ones. Excel writes a plain
    // range name with the ref spelling and a deleted one with the value spelling, so all
    // three are accepted for each.
    private static readonly byte[] PtgRef3d = [0x3A, 0x5A, 0x7A];
    private static readonly byte[] PtgArea3d = [0x3B, 0x5B, 0x7B];
    private static readonly byte[] PtgRefErr3d = [0x3C, 0x5C, 0x7C];
    private static readonly byte[] PtgAreaErr3d = [0x3D, 0x5D, 0x7D];

    /// <summary>The last row and column a sheet can have, zero-based.</summary>
    /// <remarks>
    /// A whole-column reference (`A:A`) is a rectangle whose rows span the entire sheet, and
    /// a whole-row one spans every column. The string path rejects both because half of each
    /// corner is missing; here the full span is what says so.
    /// </remarks>
    private const int LastRow = 1048575;
    private const int LastColumn = 16383;

    /// <summary>A name record held until the whole part is read.</summary>
    /// <remarks>
    /// Resolving a name needs the sheet list and the XTI table, and the format does not
    /// promise those records come first. Collected, then resolved once the stream ends.
    /// </remarks>
    private sealed record PendingName(string Name, byte[] Rgce);

    public static void Read(
        Stream part, Func<string, bool> acceptName,
        List<WorkbookPackage.DefinedName> resolved, List<WorkbookPackage.SkippedName> skipped)
    {
        var sheets = new List<string>();
        var supportingLinks = new List<int>();
        var xti = new List<(int SupportingLink, int TabFirst, int TabLast)>();
        var names = new List<PendingName>();

        while (TryReadRecord(part, out int type, out byte[] body))
        {
            switch (type)
            {
                case BrtBundleSh:
                    ReadSheet(body, sheets);
                    break;

                case BrtSupBookSrc:
                case BrtSupSelf:
                case BrtSupSame:
                case BrtSupTabs:
                    // The XTI table indexes supporting workbooks by their order of
                    // appearance, whichever kinds they are.
                    supportingLinks.Add(type);
                    break;

                case BrtExternSheet:
                    ReadXti(body, xti);
                    break;

                case BrtName:
                    ReadName(body, acceptName, names);
                    break;
            }
        }

        foreach (var name in names)
            Resolve(name, sheets, supportingLinks, xti, resolved, skipped);
    }

    /// <summary>
    /// Reads one record header and body. False at a clean end of the stream.
    /// </summary>
    private static bool TryReadRecord(Stream stream, out int type, out byte[] body)
    {
        type = 0;
        body = [];

        int first = stream.ReadByte();
        if (first < 0)
            return false;

        type = first & 0x7F;
        if ((first & 0x80) != 0)
        {
            int second = stream.ReadByte();
            if (second < 0) return false;
            type |= (second & 0x7F) << 7;
        }

        int length = 0;
        for (int i = 0; i < 4; i++)
        {
            int piece = stream.ReadByte();
            if (piece < 0) return false;

            length |= (piece & 0x7F) << (7 * i);
            if ((piece & 0x80) == 0) break;
        }

        body = new byte[length];
        int at = 0;
        while (at < length)
        {
            int got = stream.Read(body, at, length - at);
            if (got <= 0) return false;
            at += got;
        }

        return true;
    }

    /// <summary>Takes the sheet's name from a `BrtBundleSh`: state, tab id, part id, name.</summary>
    private static void ReadSheet(byte[] body, List<string> sheets)
    {
        int at = 8;

        // The part id is nullable: a length of 0xFFFFFFFF means there is none.
        uint relIdLength = ReadU32(body, ref at);
        if (relIdLength != 0xFFFFFFFF)
            at += (int)relIdLength * 2;

        sheets.Add(ReadUtf16(body, ref at));
    }

    private static void ReadXti(byte[] body, List<(int, int, int)> xti)
    {
        int at = 0;
        uint count = ReadU32(body, ref at);

        for (uint i = 0; i < count && at + 12 <= body.Length; i++)
        {
            int supportingLink = (int)ReadU32(body, ref at);
            int tabFirst = (int)ReadU32(body, ref at);
            int tabLast = (int)ReadU32(body, ref at);
            xti.Add((supportingLink, tabFirst, tabLast));
        }
    }

    /// <summary>
    /// Takes what this reader needs from a `BrtName`: the scope, the name, the reference
    /// tokens. What follows them - comment, help topic - is not read.
    /// </summary>
    private static void ReadName(byte[] body, Func<string, bool> acceptName, List<PendingName> names)
    {
        int at = 4 + 1;

        // The scope: 0xFFFFFFFF is the workbook, anything else one sheet. A sheet-scoped
        // name is a local helper, exactly as `localSheetId` says in the XML - skipped
        // without a word, as the XML path skips it.
        uint itab = ReadU32(body, ref at);
        if (itab != 0xFFFFFFFF)
            return;

        string name = ReadUtf16(body, ref at);
        if (name.Length == 0 || !acceptName(name))
            return;

        uint cce = ReadU32(body, ref at);
        if (at + cce > body.Length)
            return;

        names.Add(new PendingName(name, body[at..(at + (int)cce)]));
    }

    private static void Resolve(
        PendingName name,
        List<string> sheets,
        List<int> supportingLinks,
        List<(int SupportingLink, int TabFirst, int TabLast)> xti,
        List<WorkbookPackage.DefinedName> resolved,
        List<WorkbookPackage.SkippedName> skipped)
    {
        var rgce = name.Rgce;

        // Nothing at all, or a reference whose target was deleted. The XML path sees these
        // as an empty string or `#REF!`, and calls both not-a-range.
        if (rgce.Length == 0)
        {
            skipped.Add(new WorkbookPackage.SkippedName(name.Name, "", WorkbookPackage.NameProblem.NotARange));
            return;
        }

        byte ptg = rgce[0];

        if (Array.IndexOf(PtgRefErr3d, ptg) >= 0 || Array.IndexOf(PtgAreaErr3d, ptg) >= 0)
        {
            skipped.Add(new WorkbookPackage.SkippedName(name.Name, "#REF!", WorkbookPackage.NameProblem.NotARange));
            return;
        }

        // One rectangle is exactly one token: an area of 15 bytes, or a single cell of 9.
        // Anything longer is a formula - a union among them - and anything else is not a
        // reference into this workbook's cells.
        int firstRow, lastRow, firstColumn, lastColumn;
        if (Array.IndexOf(PtgArea3d, ptg) >= 0 && rgce.Length == 15)
        {
            firstRow = (int)BitConverter.ToUInt32(rgce, 3);
            lastRow = (int)BitConverter.ToUInt32(rgce, 7);
            firstColumn = BitConverter.ToUInt16(rgce, 11) & 0x3FFF;
            lastColumn = BitConverter.ToUInt16(rgce, 13) & 0x3FFF;
        }
        else if (Array.IndexOf(PtgRef3d, ptg) >= 0 && rgce.Length == 9)
        {
            firstRow = lastRow = (int)BitConverter.ToUInt32(rgce, 3);
            firstColumn = lastColumn = BitConverter.ToUInt16(rgce, 7) & 0x3FFF;
        }
        else
        {
            skipped.Add(NotOneRectangle(name.Name));
            return;
        }

        // A whole column or row spells its rectangle across the entire sheet.
        if ((firstRow == 0 && lastRow == LastRow) || (firstColumn == 0 && lastColumn == LastColumn))
        {
            skipped.Add(NotOneRectangle(name.Name));
            return;
        }

        // The token's sheet, through the XTI table. Only an entry that points at this
        // workbook itself and covers exactly one sheet can be read; an entry into another
        // workbook is the binary spelling of `[1]Sheet1`.
        int ixti = BitConverter.ToUInt16(rgce, 1);
        if (ixti >= xti.Count)
        {
            skipped.Add(NotOneRectangle(name.Name));
            return;
        }

        var (supportingLink, tabFirst, tabLast) = xti[ixti];

        // A deleted sheet leaves the token whole and voids the XTI entry instead: both tab
        // ends become -1. The XML twin spells the same state `#REF!`.
        if (tabFirst == -1 && tabLast == -1)
        {
            skipped.Add(new WorkbookPackage.SkippedName(name.Name, "#REF!", WorkbookPackage.NameProblem.NotARange));
            return;
        }

        if (supportingLink < 0 || supportingLink >= supportingLinks.Count
            || supportingLinks[supportingLink] != BrtSupSelf
            || tabFirst != tabLast || tabFirst < 0 || tabFirst >= sheets.Count)
        {
            skipped.Add(NotOneRectangle(name.Name));
            return;
        }

        string sheet = sheets[tabFirst];

        resolved.Add(new WorkbookPackage.DefinedName(
            Name: name.Name,
            SheetName: sheet,
            Reference: Reference(sheet, firstRow, firstColumn, lastRow, lastColumn),
            FirstRow: Math.Min(firstRow, lastRow),
            FirstColumn: Math.Min(firstColumn, lastColumn),
            LastRow: Math.Max(firstRow, lastRow),
            LastColumn: Math.Max(firstColumn, lastColumn)));
    }

    private static WorkbookPackage.SkippedName NotOneRectangle(string name)
        => new(name, "(a formula, a union, or another workbook)",
               WorkbookPackage.NameProblem.NotOneRectangle);

    /// <summary>
    /// Spells the rectangle back out as `'Sheet'!$A$1:$B$5`, for diagnostics that would
    /// otherwise have nothing to show - the binary part never held the string.
    /// </summary>
    private static string Reference(string sheet, int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        string from = $"${ColumnLetters(firstColumn)}${firstRow + 1}";
        string to = $"${ColumnLetters(lastColumn)}${lastRow + 1}";
        string range = from == to ? from : $"{from}:{to}";
        return $"'{sheet.Replace("'", "''")}'!{range}";
    }

    private static string ColumnLetters(int column)
    {
        var letters = new StringBuilder();
        for (int n = column + 1; n > 0; n = (n - 1) / 26)
            letters.Insert(0, (char)('A' + (n - 1) % 26));
        return letters.ToString();
    }

    private static uint ReadU32(byte[] body, ref int at)
    {
        uint value = BitConverter.ToUInt32(body, at);
        at += 4;
        return value;
    }

    private static string ReadUtf16(byte[] body, ref int at)
    {
        uint characters = ReadU32(body, ref at);
        string text = Encoding.Unicode.GetString(body, at, (int)characters * 2);
        at += (int)characters * 2;
        return text;
    }
}
