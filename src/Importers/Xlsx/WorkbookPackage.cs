using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Tabbit.Importers.Xlsx;

/// <summary>
/// The two things a workbook holds that are not cells: its defined names, and the notes
/// attached to cells.
/// </summary>
/// <remarks>
/// Read straight out of the package rather than through a spreadsheet library, because a
/// streaming cell reader does not report either one - and because both live in parts small
/// enough that reading them costs nothing beside the sheets. In the largest workbook of the
/// sample set that is 25 KB of names and 59 KB of notes, against 61 MiB of sheets.
///
/// Checked against what the object model reports for the same workbooks: names, references
/// and note text are identical across all 29 of the sample set.
/// </remarks>
internal sealed class WorkbookPackage
{
    private const string Main = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}";
    private const string DocRels = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}";
    private const string PackageRels = "{http://schemas.openxmlformats.org/package/2006/relationships}";
    private const string CommentsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";

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

    private readonly Dictionary<(string Sheet, int Row, int Column), string> _notes;

    private WorkbookPackage(
        List<DefinedName> definedNames,
        List<SkippedName> skippedNames,
        Dictionary<(string, int, int), string> notes,
        bool hasUnreadNotes = false)
    {
        DefinedNames = definedNames;
        SkippedNames = skippedNames;
        _notes = notes;
        HasUnreadNotes = hasUnreadNotes;
    }

    /// <summary>Workbook-scoped defined names that resolve to one rectangle. Empty when not asked for.</summary>
    public List<DefinedName> DefinedNames { get; }

    /// <summary>Names that were asked for but could not be resolved, for the caller to report.</summary>
    public List<SkippedName> SkippedNames { get; }

    /// <summary>Whether any note was found, so a caller can skip the per-cell lookup entirely.</summary>
    public bool HasNotes => _notes.Count > 0;

    /// <summary>
    /// Whether the workbook holds notes this reader does not read - a binary workbook's,
    /// which live in binary parts of their own. True so the caller can say so, rather than
    /// letting them come back as zero notes indistinguishable from a workbook that has none.
    /// </summary>
    public bool HasUnreadNotes { get; }

    /// <summary>The note on a cell, or an empty string when it has none.</summary>
    public string Note(string sheetName, int row, int column)
        => _notes.TryGetValue((sheetName, row, column), out string? note) ? note : "";

    /// <summary>
    /// Reads a workbook's names and notes.
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
        var notes = new Dictionary<(string, int, int), string>();

        // Opened through a stream of our own rather than ZipFile.OpenRead, which asks for
        // FileShare.Read and so fails on a workbook somebody has open in Excel. The cell
        // reader takes the same care, and both would be pointless if this one refused.
        using var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        var workbook = Part(zip, "xl/workbook.xml");
        if (workbook is null)
            return ReadBinary(zip, acceptName, definedNames, skippedNames, notes);

        if (acceptName is not null)
            ReadDefinedNames(workbook, acceptName, definedNames, skippedNames);

        ReadNotes(zip, workbook, notes);

        return new WorkbookPackage(definedNames, skippedNames, notes);
    }

    /// <summary>
    /// The `.xlsb` half of <see cref="Read"/>: the same names out of `xl/workbook.bin`.
    /// </summary>
    /// <remarks>
    /// Notes are not read from a binary workbook - no consumer of a note reads one from
    /// these - but their presence is noticed, so the caller can say they were left behind
    /// instead of them coming back as silently zero.
    /// </remarks>
    private static WorkbookPackage ReadBinary(
        ZipArchive zip, Func<string, bool>? acceptName,
        List<DefinedName> definedNames, List<SkippedName> skippedNames,
        Dictionary<(string, int, int), string> notes)
    {
        var entry = zip.GetEntry("xl/workbook.bin");
        if (entry is null)
            return new WorkbookPackage(definedNames, skippedNames, notes);

        if (acceptName is not null)
        {
            using var stream = entry.Open();
            BinaryDefinedNames.Read(stream, acceptName, definedNames, skippedNames);
        }

        bool hasUnreadNotes = zip.Entries.Any(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.Ordinal)
            && e.FullName.EndsWith(".bin", StringComparison.Ordinal));

        return new WorkbookPackage(definedNames, skippedNames, notes, hasUnreadNotes);
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

    /// <summary>
    /// Reads every sheet's notes, keyed by the sheet name and cell they are attached to.
    /// </summary>
    /// <remarks>
    /// The sheet a notes part belongs to is only knowable through the relationships: the
    /// workbook names its sheets and points at their parts by relationship id, and each
    /// sheet part points at its own notes part the same way. Nothing in a notes part says
    /// which sheet it is for.
    /// </remarks>
    private static void ReadNotes(
        ZipArchive zip, XDocument workbook, Dictionary<(string, int, int), string> notes)
    {
        // Walked through the relationships even though it is one small read per sheet, and
        // even though a notes part is conventionally named `xl/commentsN.xml` so the entry
        // list would answer faster. That name is a convention rather than a rule - the
        // relationship is what actually says where the part is - and a workbook whose
        // producer named it otherwise would lose every note in silence.
        var workbookRels = Relationships(zip, "xl/workbook.xml");
        if (workbookRels.Count == 0) return;

        foreach (var sheet in workbook.Descendants(Main + "sheet"))
        {
            string sheetName = (sheet.Attribute("name")?.Value ?? "").Trim();
            string relId = sheet.Attribute(DocRels + "id")?.Value ?? "";
            if (sheetName!.Length == 0 || relId.Length == 0) continue;

            if (!workbookRels.TryGetValue(relId, out var sheetRel)) continue;

            string sheetPart = Resolve("xl/workbook.xml", sheetRel.Target);
            var sheetRels = Relationships(zip, sheetPart);

            foreach (var rel in sheetRels)
            {
                if (rel.Value.Type != CommentsRelType) continue;

                var comments = Part(zip, Resolve(sheetPart, rel.Value.Target));
                if (comments is null) continue;

                foreach (var comment in comments.Descendants(Main + "comment"))
                {
                    string cell = comment.Attribute("ref")?.Value ?? "";
                    if (!TryParseCell(cell.Replace("$", ""), out int row, out int column))
                        continue;

                    string text = StripAuthorPrefix(
                        Unescape(string.Concat(comment.Descendants(Main + "t").Select(t => t.Value))));

                    if (text.Length > 0)
                        notes[(sheetName, row, column)] = text;
                }
            }
        }
    }

    /// <summary>
    /// Removes the author prefix that a spreadsheet program puts at the head of a note.
    /// </summary>
    /// <remarks>
    /// What is left becomes the doc comment of whatever the cell defines, and the name of
    /// whoever typed it is not part of that. Recognised by shape - a run of text, a colon,
    /// a line break - because the note itself does not say where the author's name ends.
    /// </remarks>
    internal static string StripAuthorPrefix(string text)
    {
        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            string prefix = text.Substring(0, colon) + ":" + "\n";
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                text = text.Substring(colon + 2);
        }

        return text.Trim();
    }

    /// <summary>
    /// Turns `_xHHHH_` back into the character it stands for.
    /// </summary>
    /// <remarks>
    /// The format spells characters that XML cannot carry - a carriage return is the one
    /// that actually occurs - as that escape. Left alone, a note holding a line break would
    /// reach generated code as the literal text `_x000D_`.
    /// </remarks>
    internal static string Unescape(string text)
    {
        int at = text.IndexOf("_x", StringComparison.Ordinal);
        if (at < 0) return text;

        var result = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '_' && i + 6 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X')
                && text[i + 6] == '_'
                && ushort.TryParse(text.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber,
                                   System.Globalization.CultureInfo.InvariantCulture, out ushort code))
            {
                result.Append((char)code);
                i += 7;
                continue;
            }

            result.Append(text[i]);
            i++;
        }

        return result.ToString();
    }

    private static XDocument? Part(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, (string Type, string Target)> Relationships(
        ZipArchive zip, string partPath)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        string directory = Directory(partPath);
        string relsPath = directory.Length == 0
            ? $"_rels/{Path.GetFileName(partPath)}.rels"
            : $"{directory}/_rels/{Path.GetFileName(partPath)}.rels";

        var rels = Part(zip, relsPath);
        if (rels is null) return result;

        foreach (var rel in rels.Descendants(PackageRels + "Relationship"))
        {
            string id = rel.Attribute("Id")?.Value ?? "";
            if (id.Length == 0) continue;

            result[id] = (rel.Attribute("Type")?.Value ?? "", rel.Attribute("Target")?.Value ?? "");
        }

        return result;
    }

    /// <summary>Resolves a relationship target against the part that declared it.</summary>
    private static string Resolve(string fromPart, string target)
    {
        if (target.StartsWith('/')) return target.Substring(1);

        var segments = new List<string>(Directory(fromPart).Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (string segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;

            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string Directory(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path.Substring(0, slash);
    }
}
