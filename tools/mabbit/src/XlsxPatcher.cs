using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Mabbit;

/// <summary>One cell to write, in the sheet's own coordinates.</summary>
internal sealed record CellEdit(string Sheet, int Row, int Column, string Value);

/// <summary>
/// Writes cells into a copy of a workbook, leaving everything else as the bytes it was.
/// </summary>
/// <remarks>
/// A workbook is a zip of XML parts. This copies every part across untouched and rewrites
/// only the sheets that have a cell to change - so formatting, charts, conditional formats,
/// macros, merged cells, defined names and every sheet nobody edited survive exactly, because
/// nothing here has an opinion about them.
///
/// That is also what makes it checkable: outside the parts it rewrites, the output is the
/// input byte for byte, and a test can say so. A library that opened the workbook and saved
/// it again would rewrite all of it, and whatever that library does not model would go
/// missing with nothing to notice.
///
/// New string values are written inline rather than into the shared string table. Adding an
/// entry there renumbers an index every other sheet refers to; an inline string is local to
/// the cell and a spreadsheet reads it the same way.
/// </remarks>
internal static class XlsxPatcher
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace PackageRels =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XNamespace DocRels =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> with the edits applied.
    /// </summary>
    public static void Apply(string source, string destination, IReadOnlyList<CellEdit> edits)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(edits);

        if (!IsPackage(source))
        {
            throw new MabbitException(
                $"`{source}` is not a package this tool can write. It writes `.xlsx` and "
                + "`.xlsm`; anything else is reported as a conflict rather than written wrongly.");
        }

        // Written beside the destination and moved into place, so a run that fails partway
        // leaves the file it was told to write untouched rather than half a workbook.
        string staging = destination + ".mabbit-staging";

        try
        {
            // The input is closed before the move, and that is not tidiness: a merge driver
            // is told to write its result over the file it was given as this side, so the
            // source and the destination are routinely the same path. Moving onto a file
            // still open for reading fails on Windows.
            using (var input = ZipFile.OpenRead(source))
            {
                RefuseUnlessXml(source, input);

                var byPart = EditsByPart(source, input, edits);

                using var output = new FileStream(staging, FileMode.Create, FileAccess.Write);
                using var archive = new ZipArchive(output, ZipArchiveMode.Create);

                foreach (var entry in input.Entries)
                {
                    // The cached order in which a spreadsheet recalculates. Every cached
                    // result it refers to is now suspect, and the file is optional - a
                    // spreadsheet rebuilds it. Keeping a stale one is what makes a
                    // recalculation put old values back.
                    if (string.Equals(entry.FullName, "xl/calcChain.xml", StringComparison.Ordinal))
                        continue;

                    if (byPart.TryGetValue(entry.FullName, out var forSheet))
                    {
                        WriteSheet(archive, entry, forSheet);
                        continue;
                    }

                    if (string.Equals(entry.FullName, "xl/workbook.xml", StringComparison.Ordinal)
                        && edits.Count > 0)
                    {
                        WriteWorkbook(archive, entry);
                        continue;
                    }

                    Copy(archive, entry);
                }
            }

            File.Move(staging, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }

    private static Dictionary<string, List<CellEdit>> EditsByPart(
        string source, ZipArchive input, IReadOnlyList<CellEdit> edits)
    {
        var parts = SheetParts(input);
        var byPart = new Dictionary<string, List<CellEdit>>(StringComparer.Ordinal);

        foreach (var edit in edits)
        {
            if (!parts.TryGetValue(edit.Sheet, out string? part))
                throw new MabbitException($"`{source}` has no sheet `{edit.Sheet}` to write into.");

            if (!byPart.TryGetValue(part, out var forPart))
                byPart[part] = forPart = [];

            forPart.Add(edit);
        }

        return byPart;
    }

    /// <summary>
    /// Refuses a package whose parts are the binary format rather than XML.
    /// </summary>
    /// <remarks>
    /// A `.xlsb` is the same zip container as a `.xlsx` with its parts written as record
    /// streams instead of XML, so the two cannot be told apart by looking at the outside of
    /// the file - which is what makes this worth a check of its own rather than an extension
    /// test. Reading one works; writing one is a different format and is not implemented, so
    /// it is refused by name instead of being half done.
    /// </remarks>
    private static void RefuseUnlessXml(string source, ZipArchive archive)
    {
        if (archive.GetEntry("xl/workbook.xml") is not null)
            return;

        throw new MabbitException(
            archive.GetEntry("xl/workbook.bin") is not null
                ? $"`{source}` is a binary `.xlsb` workbook. Reading one works, but writing "
                  + "one is a different format from `.xlsx` and is not implemented - so this "
                  + "is reported as a conflict to settle by hand rather than written wrongly."
                : $"`{source}` has no workbook part, so it is not a workbook.");
    }

    /// <summary>Whether the file is a zip package at all, which is what the writer handles.</summary>
    public static bool IsPackage(string path)
    {
        using var file = File.OpenRead(path);

        // `PK`, the two bytes every zip starts with. Checked rather than assumed from the
        // extension, because a merge driver is handed files under names that say nothing.
        return file.Length >= 2 && file.ReadByte() == 'P' && file.ReadByte() == 'K';
    }

    private static void Copy(ZipArchive archive, ZipArchiveEntry entry)
    {
        var written = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
        written.LastWriteTime = entry.LastWriteTime;

        using var from = entry.Open();
        using var to = written.Open();

        from.CopyTo(to);
    }

    /// <summary>
    /// The workbook part, with a recalculation asked for the next time it is opened.
    /// </summary>
    /// <remarks>
    /// Changing a cell makes every cached formula result that depended on it wrong. The
    /// values are still in the file and a spreadsheet will show them until it recalculates,
    /// so this asks it to do that on load.
    /// </remarks>
    private static void WriteWorkbook(ZipArchive archive, ZipArchiveEntry entry)
    {
        XDocument document;

        using (var stream = entry.Open())
            document = XDocument.Load(stream);

        var root = document.Root
            ?? throw new MabbitException("The workbook part of this file is empty.");

        var calc = root.Element(Main + "calcPr");

        if (calc is null)
        {
            calc = new XElement(Main + "calcPr");

            // After the sheets, which is where the schema puts it. A spreadsheet refuses a
            // workbook whose elements are out of order.
            var sheets = root.Element(Main + "sheets");

            if (sheets is null)
                root.Add(calc);
            else
                sheets.AddAfterSelf(calc);
        }

        calc.SetAttributeValue("fullCalcOnLoad", "1");

        var written = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);

        using var to = written.Open();
        document.Save(to, SaveOptions.DisableFormatting);
    }

    /// <summary>Sheet name to the part that holds it.</summary>
    private static Dictionary<string, string> SheetParts(ZipArchive archive)
    {
        var workbook = archive.GetEntry("xl/workbook.xml")
            ?? throw new MabbitException("This file has no workbook part, so it is not a workbook.");

        var relationships = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new MabbitException("This workbook lists no parts, so its sheets cannot be found.");

        Dictionary<string, string> targets;

        using (var stream = relationships.Open())
        {
            targets = XDocument.Load(stream).Root!
                .Elements(PackageRels + "Relationship")
                .ToDictionary(
                    r => (string)r.Attribute("Id")!,
                    r => "xl/" + ((string)r.Attribute("Target")!).TrimStart('/'),
                    StringComparer.Ordinal);
        }

        using var book = workbook.Open();

        return XDocument.Load(book).Root!
            .Element(Main + "sheets")!
            .Elements(Main + "sheet")
            .Where(s => s.Attribute(DocRels + "id") is not null)
            .ToDictionary(
                s => ((string)s.Attribute("name")!).Trim(),
                s => targets[(string)s.Attribute(DocRels + "id")!],
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One sheet, streamed through with its edited cells replaced.
    /// </summary>
    /// <remarks>
    /// Streamed rather than loaded, because the sheets this is asked about are the large
    /// ones. Each cell is read as its own small element so its contents can be looked at -
    /// a cell holding a formula has to be refused rather than overwritten - and everything
    /// else is copied node by node.
    /// </remarks>
    private static void WriteSheet(
        ZipArchive archive, ZipArchiveEntry entry, List<CellEdit> edits)
    {
        var byRow = edits.GroupBy(e => e.Row)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Column).ToList());

        var written = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);

        using var from = entry.Open();
        using var to = written.Open();

        var readerSettings = new XmlReaderSettings { IgnoreWhitespace = false, CloseInput = false };
        var writerSettings = new XmlWriterSettings { Indent = false, CloseOutput = false };

        using var reader = XmlReader.Create(from, readerSettings);
        using var writer = XmlWriter.Create(to, writerSettings);

        int row = 0;
        List<CellEdit>? pending = null;

        // Which row numbers the sheet actually carries. Anything left over at the end is a
        // row that has to be created rather than edited.
        var seen = new HashSet<int>();

        // `Skip` and `ReadFrom` both leave the reader on the next node already, so a plain
        // `while (Read())` would step over it. This carries that fact instead.
        bool standing = false;

        while (standing || reader.Read())
        {
            standing = false;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "dimension")
            {
                // The rectangle the sheet claims to occupy. A cell written outside it would
                // make the claim false, and a spreadsheet works the real one out when this
                // is absent.
                reader.Skip();
                standing = !reader.EOF;

                continue;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
            {
                WriteCell(reader, writer, ref pending);
                standing = !reader.EOF;

                continue;
            }

            WriteCurrent(reader, writer, ref row, ref pending, byRow, seen);
        }
    }

    /// <summary>
    /// One cell, read whole so its contents can be looked at before it is written on.
    /// </summary>
    private static void WriteCell(XmlReader reader, XmlWriter writer, ref List<CellEdit>? pending)
    {
        string ns = reader.NamespaceURI;
        var cell = (XElement)XNode.ReadFrom(reader);

        int column = ColumnOf((string?)cell.Attribute("r"));

        // Cells have to stay in column order, so anything being inserted to the left of this
        // one goes first.
        FlushPending(writer, ref pending, column, ns);

        var edit = Take(ref pending, column);

        if (edit is not null)
            Rewrite(cell, edit.Value, ns);

        cell.WriteTo(writer);
    }

    private static void WriteCurrent(
        XmlReader reader, XmlWriter writer, ref int row,
        ref List<CellEdit>? pending, Dictionary<int, List<CellEdit>> byRow, HashSet<int> seen)
    {
        switch (reader.NodeType)
        {
            case XmlNodeType.Element when reader.LocalName == "row":
            {
                bool empty = reader.IsEmptyElement;
                row = int.Parse(reader.GetAttribute("r") ?? "0", CultureInfo.InvariantCulture) - 1;

                // Rows the sheet does not have yet, which belong above this one. A row
                // arriving from the other side is exactly this: cells for a row number no
                // element in the file carries.
                WriteMissingRows(writer, byRow, seen, before: row, ns: reader.NamespaceURI);

                seen.Add(row);
                pending = byRow.TryGetValue(row, out var forRow) ? [.. forRow] : null;

                writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);

                for (int i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);

                    // The first and last column the row holds. Adding a cell outside it makes
                    // it wrong, and it is an optimisation a spreadsheet does without.
                    if (reader.LocalName == "spans")
                        continue;

                    writer.WriteAttributeString(
                        reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value);
                }

                reader.MoveToElement();

                if (empty)
                {
                    FlushPending(writer, ref pending, int.MaxValue, reader.NamespaceURI);
                    writer.WriteFullEndElement();
                }

                return;
            }

            case XmlNodeType.EndElement when reader.LocalName == "row":
                FlushPending(writer, ref pending, int.MaxValue, reader.NamespaceURI);
                writer.WriteFullEndElement();
                return;

            case XmlNodeType.EndElement when reader.LocalName == "sheetData":
                WriteMissingRows(writer, byRow, seen, before: int.MaxValue, ns: reader.NamespaceURI);
                writer.WriteFullEndElement();
                return;

            case XmlNodeType.Element:
            {
                bool empty = reader.IsEmptyElement;

                writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                writer.WriteAttributes(reader, defattr: false);

                if (empty)
                    writer.WriteEndElement();

                return;
            }

            case XmlNodeType.EndElement:
                writer.WriteFullEndElement();
                return;

            case XmlNodeType.Text:
                writer.WriteString(reader.Value);
                return;

            case XmlNodeType.SignificantWhitespace:
            case XmlNodeType.Whitespace:
                writer.WriteWhitespace(reader.Value);
                return;

            case XmlNodeType.CDATA:
                writer.WriteCData(reader.Value);
                return;

            case XmlNodeType.Comment:
                writer.WriteComment(reader.Value);
                return;

            case XmlNodeType.XmlDeclaration:
                writer.WriteStartDocument(standalone: true);
                return;

            case XmlNodeType.ProcessingInstruction:
                writer.WriteProcessingInstruction(reader.Name, reader.Value);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Writes the rows the sheet does not have, in order, up to the row about to be written.
    /// </summary>
    /// <remarks>
    /// Rows have to appear in ascending order for a spreadsheet to accept the sheet, so a row
    /// being added is emitted at the point its number reaches - not appended at the end.
    /// </remarks>
    private static void WriteMissingRows(
        XmlWriter writer, Dictionary<int, List<CellEdit>> byRow, HashSet<int> seen, int before, string ns)
    {
        foreach (int row in byRow.Keys.Where(r => r < before && !seen.Contains(r)).Order().ToList())
        {
            seen.Add(row);

            writer.WriteStartElement("row", ns);
            writer.WriteAttributeString("r", (row + 1).ToString(CultureInfo.InvariantCulture));

            foreach (var edit in byRow[row].OrderBy(e => e.Column))
            {
                var cell = new XElement(XName.Get("c", ns));
                cell.SetAttributeValue("r", Reference(edit.Row, edit.Column));

                Rewrite(cell, edit.Value, ns);
                cell.WriteTo(writer);
            }

            writer.WriteFullEndElement();
        }
    }

    /// <summary>Writes any pending edit whose column comes before the one about to be written.</summary>
    private static void FlushPending(
        XmlWriter writer, ref List<CellEdit>? pending, int before, string ns)
    {
        if (pending is null)
            return;

        while (pending.Count > 0 && pending[0].Column < before)
        {
            var edit = pending[0];
            pending.RemoveAt(0);

            var cell = new XElement(XName.Get("c", ns));
            cell.SetAttributeValue("r", Reference(edit.Row, edit.Column));

            Rewrite(cell, edit.Value, ns);
            cell.WriteTo(writer);
        }

        if (pending.Count == 0)
            pending = null;
    }

    private static CellEdit? Take(ref List<CellEdit>? pending, int column)
    {
        if (pending is null)
            return null;

        for (int i = 0; i < pending.Count; i++)
        {
            if (pending[i].Column != column)
                continue;

            var edit = pending[i];
            pending.RemoveAt(i);

            if (pending.Count == 0)
                pending = null;

            return edit;
        }

        return null;
    }

    /// <summary>
    /// Puts a value into a cell, keeping its formatting and nothing else.
    /// </summary>
    /// <remarks>
    /// The style attribute stays, because how a cell is formatted is not what a merge is
    /// deciding. Everything else about the cell goes: its old value, its old type, and its
    /// cached formula result if it had one.
    /// </remarks>
    private static void Rewrite(XElement cell, string value, string ns)
    {
        if (cell.Element(XName.Get("f", ns)) is not null)
        {
            throw new MabbitException(
                $"Cell {(string?)cell.Attribute("r")} holds a formula, and writing a value "
                + "into it would be undone the next time the sheet recalculates. Settle this "
                + "one by hand.");
        }

        string? style = (string?)cell.Attribute("s");

        cell.RemoveNodes();
        cell.RemoveAttributes();

        cell.SetAttributeValue("r", (string?)cell.Attribute("r") ?? "");

        if (style is not null)
            cell.SetAttributeValue("s", style);

        if (value.Length == 0)
            return;

        if (LooksNumeric(value))
        {
            // No type attribute at all is what a spreadsheet writes for a number.
            cell.Add(new XElement(XName.Get("v", ns), value));
            return;
        }

        cell.SetAttributeValue("t", "inlineStr");
        cell.Add(new XElement(XName.Get("is", ns), new XElement(XName.Get("t", ns), value)));
    }

    /// <summary>
    /// Whether a value should be written as a number rather than as text.
    /// </summary>
    /// <remarks>
    /// It has to round-trip: a value that arrived as a number and goes back as text would
    /// read differently the next time, and the merge that wrote it would then see a
    /// difference it created itself.
    /// </remarks>
    private static bool LooksNumeric(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
           && string.Equals(parsed.ToString("R", CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    /// <summary>`B7` to its zero based column, or -1 when there is no reference.</summary>
    private static int ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
            return -1;

        int column = 0;

        foreach (char c in reference)
        {
            char upper = char.ToUpperInvariant(c);

            if (upper is < 'A' or > 'Z')
                break;

            column = (column * 26) + (upper - 'A' + 1);
        }

        return column - 1;
    }

    private static string Reference(int row, int column)
        => CellRef.ColumnName(column) + (row + 1).ToString(CultureInfo.InvariantCulture);
}
