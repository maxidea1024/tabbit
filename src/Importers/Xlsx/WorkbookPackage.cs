using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Tabbit.Importers.Xlsx;

/// <summary>
/// The one thing a workbook holds that is not a cell: its defined names.
/// </summary>
/// <remarks>
/// Read straight out of the package rather than through a spreadsheet library, because a
/// streaming cell reader does not report them - and because they live in a part small enough
/// that reading it costs nothing beside the sheets. In the largest workbook of the sample set
/// that is 25 KB of names against 61 MiB of sheets.
///
/// Checked against what the object model reports for the same workbooks: names and references
/// are identical across all 29 of the sample set.
///
/// **The notes attached to cells were read here too, and are not any more.** That removed the
/// `xl/comments*.xml` parts from every workbook this opens and the entry scan from every
/// `.xlsb` one; the reasoning is written where the field they filled used to be, in
/// <see cref="Models.Raw.RawCell"/>.
/// </remarks>
internal sealed class WorkbookPackage
{
    private const string Main = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}";

    /// <summary>One of the workbook's defined names, resolved to a sheet and a rectangle.</summary>
    internal sealed record DefinedName(
        string Name, string SheetName, string Reference,
        int FirstRow, int FirstColumn, int LastRow, int LastColumn);

    /// <summary>Why a defined name was not usable, so the caller can say so in its own words.</summary>
    internal enum NameProblem
    {
        /// <summary>Its target was deleted, or it names something that is not a range at all.</summary>
        NotARange,

        /// <summary>A union, a whole column, a reference into another workbook.</summary>
        NotOneRectangle,
    }

    internal sealed record SkippedName(string Name, string Reference, NameProblem Problem);

    private WorkbookPackage(List<DefinedName> definedNames, List<SkippedName> skippedNames)
    {
        DefinedNames = definedNames;
        SkippedNames = skippedNames;
    }

    /// <summary>Workbook-scoped defined names that resolve to one rectangle. Empty when not asked for.</summary>
    public List<DefinedName> DefinedNames { get; }

    /// <summary>Names that were asked for but could not be resolved, for the caller to report.</summary>
    public List<SkippedName> SkippedNames { get; }

    /// <summary>
    /// Reads a workbook's defined names.
    /// </summary>
    /// <param name="acceptName">
    /// Which defined names are worth resolving, asked before the reference is parsed - so a
    /// name the caller was never going to use cannot produce a warning about its reference.
    /// Null when the caller wants no names at all, which is every layout that finds its
    /// tables some other way.
    /// </param>
    public static WorkbookPackage Read(string filename, Func<string, bool>? acceptName)
    {
        var definedNames = new List<DefinedName>();
        var skippedNames = new List<SkippedName>();

        // Opened through a stream of our own rather than ZipFile.OpenRead, which asks for
        // FileShare.Read and so fails on a workbook somebody has open in Excel. The cell
        // reader takes the same care, and both would be pointless if this one refused.
        using var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        var workbook = Part(zip, "xl/workbook.xml");
        if (workbook is null)
            return ReadBinary(zip, acceptName, definedNames, skippedNames);

        if (acceptName is not null)
            ReadDefinedNames(workbook, acceptName, definedNames, skippedNames);

        return new WorkbookPackage(definedNames, skippedNames);
    }

    /// <summary>
    /// The `.xlsb` half of <see cref="Read"/>: the same names out of `xl/workbook.bin`.
    /// </summary>
    private static WorkbookPackage ReadBinary(
        ZipArchive zip, Func<string, bool>? acceptName,
        List<DefinedName> definedNames, List<SkippedName> skippedNames)
    {
        var entry = zip.GetEntry("xl/workbook.bin");
        if (entry is null)
            return new WorkbookPackage(definedNames, skippedNames);

        if (acceptName is not null)
        {
            using var stream = entry.Open();
            BinaryDefinedNames.Read(stream, acceptName, definedNames, skippedNames);
        }

        return new WorkbookPackage(definedNames, skippedNames);
    }

    private static void ReadDefinedNames(
        XDocument workbook, Func<string, bool> acceptName,
        List<DefinedName> resolved, List<SkippedName> skipped)
    {
        foreach (var element in workbook.Descendants(Main + "definedName"))
        {
            // A `localSheetId` means the name belongs to one sheet rather than the workbook.
            // A sheet-scoped name is a local helper - a filter range, a chart source - and
            // taking one as a table would convert something the workbook never exported.
            if (element.Attribute("localSheetId") is not null)
                continue;

            string name = element.Attribute("name")?.Value ?? "";
            if (name.Length == 0 || !acceptName(name))
                continue;

            string reference = element.Value ?? "";
            if (reference.Length == 0 || reference.Contains("#REF!", StringComparison.Ordinal))
            {
                skipped.Add(new SkippedName(name, reference, NameProblem.NotARange));
                continue;
            }

            var area = TryParseArea(reference);
            if (area is null)
            {
                skipped.Add(new SkippedName(name, reference, NameProblem.NotOneRectangle));
                continue;
            }

            resolved.Add(area with { Name = name });
        }
    }

    /// <summary>
    /// Reads a reference like `'Ocean Zone'!$A$1:$IP$100` into a sheet name and a rectangle.
    /// </summary>
    /// <remarks>
    /// Null for the shapes that are not one rectangle - a union, a whole column or row, a
    /// reference into another workbook. Those are to be skipped rather than guessed at, and
    /// the caller reports them.
    /// </remarks>
    internal static DefinedName? TryParseArea(string reference)
    {
        // A union of ranges. Not one rectangle, whatever each part is.
        if (reference.Contains(',')) return null;

        if (!TrySplitSheet(reference, out string? sheetName, out string? range))
            return null;

        // `[1]Sheet1` is a reference into another workbook, whose cells are not ours to read.
        if (sheetName!.Length == 0 || sheetName.Contains('[')) return null;

        range = range!.Replace("$", "");

        string[] corners = range.Split(':');
        if (corners.Length is < 1 or > 2) return null;

        if (!TryParseCell(corners[0], out int firstRow, out int firstColumn)) return null;

        int lastRow = firstRow, lastColumn = firstColumn;
        if (corners.Length == 2 && !TryParseCell(corners[1], out lastRow, out lastColumn))
            return null;

        return new DefinedName(
            Name: "",
            SheetName: sheetName,
            Reference: reference,
            FirstRow: Math.Min(firstRow, lastRow),
            FirstColumn: Math.Min(firstColumn, lastColumn),
            LastRow: Math.Max(firstRow, lastRow),
            LastColumn: Math.Max(firstColumn, lastColumn));
    }

    /// <summary>
    /// Splits `Sheet!range`, unquoting the sheet name when it is quoted.
    /// </summary>
    /// <remarks>
    /// A sheet name is quoted whenever it holds a space or punctuation, and inside the quotes
    /// a literal apostrophe is doubled. Splitting on the first `!` would be wrong for a name
    /// that contains one, which Excel permits.
    /// </remarks>
    private static bool TrySplitSheet(string reference, out string? sheetName, out string? range)
    {
        sheetName = "";
        range = "";

        if (reference.StartsWith('\''))
        {
            var name = new StringBuilder();
            int i = 1;
            while (i < reference.Length)
            {
                if (reference[i] == '\'')
                {
                    if (i + 1 < reference.Length && reference[i + 1] == '\'')
                    {
                        name.Append('\'');
                        i += 2;
                        continue;
                    }

                    break;
                }

                name.Append(reference[i]);
                i++;
            }

            // Wants a closing quote followed by `!` and something after it.
            if (i + 1 >= reference.Length || reference[i] != '\'' || reference[i + 1] != '!')
                return false;

            sheetName = name.ToString();
            range = reference.Substring(i + 2);
            return range.Length > 0;
        }

        int bang = reference.IndexOf('!');
        if (bang <= 0 || bang == reference.Length - 1)
            return false;

        sheetName = reference.Substring(0, bang);
        range = reference.Substring(bang + 1);
        return true;
    }

    /// <summary>
    /// Reads `IP100` into a zero-based row and column.
    /// </summary>
    /// <remarks>
    /// Both halves are required, which is what rejects a whole column (`A:A`) and a whole
    /// row (`1:1`) - neither is a rectangle of known extent.
    /// </remarks>
    internal static bool TryParseCell(string cell, out int row, out int column)
    {
        row = 0;
        column = 0;

        int i = 0;
        while (i < cell.Length && char.IsAsciiLetter(cell[i])) i++;
        if (i == 0 || i == cell.Length) return false;

        // Bijective base 26: there is no zero digit, so each place is 1..26.
        long columnNumber = 0;
        for (int c = 0; c < i; c++)
        {
            columnNumber = columnNumber * 26 + (char.ToUpperInvariant(cell[c]) - 'A' + 1);
            if (columnNumber > int.MaxValue) return false;
        }

        for (int c = i; c < cell.Length; c++)
            if (!char.IsAsciiDigit(cell[c])) return false;

        if (!int.TryParse(cell.Substring(i), out int rowNumber) || rowNumber < 1)
            return false;

        column = (int)columnNumber - 1;
        row = rowNumber - 1;
        return true;
    }

    private static XDocument? Part(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
