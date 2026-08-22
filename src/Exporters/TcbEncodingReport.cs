using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tabbit.Exporters;

/// <summary>
/// What every column of an export measured, and why it is stored the way it is.
/// </summary>
/// <remarks>
/// The writer already encodes every applicable candidate in full and keeps the smallest, so
/// the sizes this reports are measurements and not estimates - they are the same numbers the
/// choice was made on. Collecting them costs an entry per column and changes nothing about
/// what is written; the report is off unless an export asks for it.
///
/// It exists because "the encoding is chosen automatically" is only worth having if the
/// choice can be inspected. Without this, a column that stayed raw and a column that saved
/// ninety percent look the same from outside, and the question of whether another layout
/// would be worth its cost in every reader runtime has no evidence behind it.
/// </remarks>
public sealed class TcbEncodingReport
{
    /// <summary>How many columns the per-column listing names before it stops.</summary>
    /// <remarks>
    /// The listing is sorted by size, so the bytes that are worth acting on are at the top
    /// and a long tail of columns costing a few bytes each would only make the report harder
    /// to read. The totals above it are over every column regardless.
    /// </remarks>
    private const int ListedColumns = 40;

    /// <summary>One column's decision, as measured.</summary>
    public sealed class ColumnEntry
    {
        public required string Table { get; init; }
        public required string Column { get; init; }
        public byte Element { get; init; }
        public byte Kind { get; init; }
        public bool Nullable { get; init; }
        public int Rows { get; init; }

        /// <summary>The encoding that won, and what its block came to.</summary>
        public byte Encoding { get; init; }
        public int Bytes { get; init; }

        /// <summary>Every candidate that was measured, in the order they were tried.</summary>
        public IReadOnlyList<(byte Encoding, int Bytes)> Candidates { get; init; }
            = Array.Empty<(byte, int)>();

        /// <summary>How the distinct values are structured, for a string column.</summary>
        public required TcbStringStructure.Report? Structure { get; init; }

        /// <summary>What a general compressor makes of the block that was chosen.</summary>
        public int DeflatedBytes { get; init; }

        /// <summary>What the column would cost if its elements were encoded rather than raw.</summary>
        public required LayerEntry? Layers { get; init; }

        /// <summary>
        /// The presence bitmap's share of <see cref="Bytes"/>, which no hypothetical layout
        /// here would remove.
        /// </summary>
        /// <remarks>
        /// <see cref="Bytes"/> is the whole block, bitmap included, because that is what the
        /// file holds. Every measured layout in this report produces a **value block** and
        /// says nothing about presence, so comparing one against the other credits the
        /// hypothetical with bytes it never touches - and on a nullable column of six
        /// thousand rows the bitmap is seven hundred and fifty of them, which is most of what
        /// a well-encoded boolean column costs.
        ///
        /// So the comparisons add this back. Getting it wrong the first time is what makes it
        /// worth a name: a bit-packed boolean column looked thirty times smaller than the one
        /// it replaced, and it was the bitmap on both sides of the ledger.
        /// </remarks>
        public int PresenceBytes => Nullable ? (Rows + 7) / 8 : 0;

        /// <summary>And what it would come to if the bitmap were encoded rather than raw.</summary>
        public int PresenceEncodedBytes { get; init; }

        /// <summary>What the values came to before any encoding was applied.</summary>
        public int RawBytes
        {
            get
            {
                foreach (var candidate in Candidates)
                {
                    if (candidate.Encoding == TcbFormat.EncodingRaw)
                        return candidate.Bytes;
                }

                return Bytes;
            }
        }
    }

    /// <summary>
    /// A column split the way an encoded array would be: how long each row is in one stream,
    /// the elements themselves in another, each measured under the encodings it allows.
    /// </summary>
    public sealed class LayerEntry
    {
        /// <summary>Elements across every row.</summary>
        public int Elements { get; init; }

        /// <summary>What the per-row lengths cost, for a column whose rows differ in length.</summary>
        public int LengthBytes { get; init; }

        /// <summary>What the flattened elements cost with nothing applied.</summary>
        public int RawBytes { get; set; }

        /// <summary>And what they cost under the smallest encoding their type allows.</summary>
        public int ElementBytes { get; set; }

        /// <summary>Whether the elements are integers a bit-width layout could carry.</summary>
        /// <remarks>
        /// Measured, not in the format. spec/bitset.md's sixth section says why the question
        /// is being asked; what settles it is this column and every other one.
        /// </remarks>
        public bool BitpackApplies { get; set; }

        /// <summary>Bits per value, if they were packed.</summary>
        public int BitpackWidth { get; set; }

        /// <summary>Subtracted from every value before packing.</summary>
        public long BitpackBase { get; set; }

        /// <summary>What the packed block would cost, inner encoding included.</summary>
        public int BitpackBytes { get; set; }

        /// <summary>Which encoding carried the packed bytes.</summary>
        public byte BitpackInner { get; set; }

        /// <summary>Whether every value of a float column is a whole number.</summary>
        public bool WholeNumbers { get; set; }

        /// <summary>If they are, what they cost as integers instead of as float bit patterns.</summary>
        public int WholeBytes { get; set; }

        /// <summary>The smallest this column could be made, over both streams.</summary>
        public int BestBytes => LengthBytes
            + (WholeNumbers && WholeBytes > 0 ? Math.Min(ElementBytes, WholeBytes) : ElementBytes);
    }

    private readonly List<ColumnEntry> _columns = new();

    public void Add(ColumnEntry entry) => _columns.Add(entry);

    public int ColumnCount => _columns.Count;

    /// <summary>The report as text, ready to be written beside the exported tables.</summary>
    public string Render()
    {
        var text = new StringBuilder();

        text.AppendLine("TCB encoding report");
        text.AppendLine("===================");
        text.AppendLine();

        RenderTotals(text);
        RenderByEncoding(text);
        RenderByElement(text);
        RenderColumns(text);
        RenderLayerHeadroom(text);
        RenderBitpackHeadroom(text);
        RenderPresenceHeadroom(text);
        RenderStringHeadroom(text);

        return text.ToString();
    }

    private void RenderTotals(StringBuilder text)
    {
        int bytes = _columns.Sum(column => column.Bytes);
        int raw = _columns.Sum(column => column.RawBytes);
        int tables = _columns.Select(column => column.Table).Distinct().Count();

        int deflated = _columns.Sum(column => column.DeflatedBytes);

        text.AppendLine("Totals");
        text.AppendLine($"  tables    {tables}");
        text.AppendLine($"  columns   {_columns.Count}");
        text.AppendLine($"  unencoded {Bytes(raw)}");
        text.AppendLine($"  encoded   {Bytes(bytes)}  ({Share(bytes, raw)} of unencoded)");
        text.AppendLine(
            $"  deflated  {Bytes(deflated)}  ({Share(deflated, bytes)} of encoded)"
            + "  - what a compression layer over the chosen blocks would come to");
        text.AppendLine();
    }

    private void RenderByEncoding(StringBuilder text)
    {
        int total = _columns.Sum(column => column.Bytes);

        text.AppendLine("By encoding");
        text.AppendLine("  encoding        columns        bytes    share");

        var groups = _columns
            .GroupBy(column => column.Encoding)
            .Select(group => (Encoding: group.Key, Columns: group.Count(), Bytes: group.Sum(c => c.Bytes)))
            .OrderByDescending(group => group.Bytes)
            .ThenBy(group => group.Encoding);

        foreach (var group in groups)
        {
            text.AppendLine(
                $"  {TcbFormat.EncodingName(group.Encoding),-14}{group.Columns,8}{Bytes(group.Bytes),13}"
                + $"{Share(group.Bytes, total),9}");
        }

        text.AppendLine();
    }

    private void RenderByElement(StringBuilder text)
    {
        int total = _columns.Sum(column => column.Bytes);

        text.AppendLine("By element");
        text.AppendLine("  element         columns    unencoded        bytes    share");

        var groups = _columns
            .GroupBy(column => column.Element)
            .Select(group => (
                Element: group.Key,
                Columns: group.Count(),
                Raw: group.Sum(c => c.RawBytes),
                Bytes: group.Sum(c => c.Bytes)))
            .OrderByDescending(group => group.Bytes)
            .ThenBy(group => group.Element);

        foreach (var group in groups)
        {
            text.AppendLine(
                $"  {TcbFormat.ElementName(group.Element),-14}{group.Columns,8}{Bytes(group.Raw),13}"
                + $"{Bytes(group.Bytes),13}{Share(group.Bytes, total),9}");
        }

        text.AppendLine();
    }

    private void RenderColumns(StringBuilder text)
    {
        var largest = _columns.OrderByDescending(column => column.Bytes)
            .ThenBy(column => column.Table, StringComparer.Ordinal)
            .ThenBy(column => column.Column, StringComparer.Ordinal)
            .Take(ListedColumns)
            .ToList();

        text.AppendLine($"Largest columns ({largest.Count} of {_columns.Count})");
        text.AppendLine();

        foreach (var column in largest)
        {
            string shape = TcbFormat.ElementName(column.Element) + KindSuffix(column.Kind)
                + (column.Nullable ? "?" : string.Empty);

            text.AppendLine(
                $"  {column.Table}.{column.Column}  [{shape}]  {column.Rows} rows");
            text.AppendLine(
                $"    chosen {TcbFormat.EncodingName(column.Encoding)}  {Bytes(column.Bytes)}"
                + $"  ({Share(column.Bytes, column.RawBytes)} of unencoded)");

            if (column.Candidates.Count > 1)
            {
                var measured = column.Candidates
                    .Select(candidate =>
                        $"{TcbFormat.EncodingName(candidate.Encoding)} {Bytes(candidate.Bytes)}");

                text.AppendLine($"    candidates {string.Join("  ·  ", measured)}");
            }

            text.AppendLine();
        }
    }

    /// <summary>
    /// What the columns no encoding reaches would come to if one did.
    /// </summary>
    /// <remarks>
    /// The encodings apply to scalar columns only, and to a float column only through a
    /// dictionary. Both of those were decided against a dataset where arrays held 1.8 percent
    /// of the bytes and floats repeated themselves; neither is a property of the format, and
    /// this is where a dataset says so if it differs.
    /// </remarks>
    private void RenderLayerHeadroom(StringBuilder text)
    {
        var layered = _columns.Where(column => column.Layers != null).ToList();

        if (layered.Count == 0)
            return;

        var arrays = layered.Where(column => column.Kind != TcbFormat.KindScalar).ToList();
        var floats = layered
            .Where(column => column.Element is TcbFormat.ElementF32 or TcbFormat.ElementF64)
            .ToList();

        text.AppendLine("Headroom outside the encodings");
        text.AppendLine("  What a column would come to if its rows' lengths and its elements were");
        text.AppendLine("  each encoded, against what it costs today. Nothing here is in the format.");
        text.AppendLine();

        RenderLayerGroup(text, "Array columns (always raw today)", arrays);
        RenderLayerGroup(text, "Float columns (dictionary only today)", floats);

        var whole = floats.Where(column => column.Layers!.WholeNumbers).ToList();

        if (whole.Count > 0)
        {
            int today = whole.Sum(column => column.Bytes);
            int asIntegers = whole.Sum(column => column.Layers!.BestBytes + column.PresenceEncodedBytes);

            text.AppendLine(
                $"  Of the float columns, {whole.Count} hold whole numbers in every row.");
            text.AppendLine(
                $"    today {Bytes(today)} · as integers {Bytes(asIntegers)}"
                + $" · saved {Bytes(today - asIntegers)}");
            text.AppendLine();
        }
    }

    /// <summary>
    /// What a fixed bit width would take off the integer columns.
    /// </summary>
    /// <remarks>
    /// Not in the format. The question this answers is whether it should be, and the answer
    /// is per column: a `bool` column with long runs is already smaller than a bit a row,
    /// while one that alternates is eight times what it needs to be, and run-length encoding
    /// cannot reach it because its runs are too short to pay for themselves. A flag set using
    /// five of its sixty-four bits is eight bytes a row today.
    ///
    /// `width` is what the column's own range needs and `base` is subtracted before packing,
    /// so a column of labels numbered from a hundred is measured by how much it varies rather
    /// than by how large it is. `inner` names the encoding that carried the packed bytes,
    /// which is what says whether the composition earns its place or whether the bytes as
    /// they are would do.
    /// </remarks>
    private void RenderBitpackHeadroom(StringBuilder text)
    {
        var packable = _columns
            .Where(column => column.Layers?.BitpackApplies == true)
            .ToList();

        if (packable.Count == 0)
            return;

        text.AppendLine("Bit-width packing headroom");
        text.AppendLine("  What the integer columns would come to at the width their own range");
        text.AppendLine("  needs, against what they cost today. Nothing here is in the format.");
        text.AppendLine();

        text.AppendLine($"  {"element",-10}{"columns",9}{"today",13}{"packed",13}{"saved",13}");

        foreach (var group in packable
            .GroupBy(column => column.Element)
            .OrderByDescending(group => group.Sum(column => column.Bytes - Packed(column))))
        {
            int today = group.Sum(column => column.Bytes);
            int packed = group.Sum(Packed);

            text.AppendLine(
                $"  {TcbFormat.ElementName(group.Key),-10}{group.Count(),9}"
                + $"{Bytes(today),13}{Bytes(packed),13}{Bytes(today - packed),13}");
        }

        text.AppendLine();

        var wins = packable
            .Where(column => Packed(column) < column.Bytes)
            .OrderByDescending(column => column.Bytes - Packed(column))
            .ToList();

        int totalToday = packable.Sum(column => column.Bytes);
        int totalBest = packable.Sum(column => Math.Min(column.Bytes, Packed(column)));

        // The selector keeps whichever is smaller, so this is what the format would actually
        // save - not the column-by-column sum above, which charges the losses as well.
        text.AppendLine(
            $"  Keeping the smaller of the two per column: {Bytes(totalToday)} → {Bytes(totalBest)}"
            + $" · saved {Bytes(totalToday - totalBest)} over {wins.Count} of {packable.Count} columns.");
        text.AppendLine();

        if (wins.Count == 0)
            return;

        text.AppendLine($"  {"width",7}{"base",16}{"today",13}{"packed",13}  {"inner",-14}column");

        foreach (var column in wins.Take(ListedColumns))
        {
            var layers = column.Layers;

            text.AppendLine(
                $"  {layers!.BitpackWidth,7}{layers!.BitpackBase,16}{Bytes(column.Bytes),13}"
                + $"{Bytes(Packed(column)),13}  {TcbFormat.EncodingName(layers!.BitpackInner),-14}"
                + $"{column.Table}.{column.Column}");
        }

        text.AppendLine();
    }

    /// <summary>
    /// What the presence bitmaps cost, and what encoding them would take off.
    /// </summary>
    /// <remarks>
    /// The bitmap is a bit per row and no encoding touches it, which v103 decided on the
    /// ground that a column whose presence varies has a bitmap close to incompressible. That
    /// was a judgement rather than a measurement, and it matters more than it looks: on a
    /// column whose values fold well the bitmap is most of the block, and on one dataset here
    /// it is ninety-nine percent of what the boolean columns cost.
    /// </remarks>
    private void RenderPresenceHeadroom(StringBuilder text)
    {
        var nullable = _columns.Where(column => column.Nullable && column.Rows > 0).ToList();

        if (nullable.Count == 0)
            return;

        int raw = nullable.Sum(column => column.PresenceBytes);
        int encoded = nullable.Sum(column => Math.Min(column.PresenceBytes, column.PresenceEncodedBytes));

        text.AppendLine("Presence bitmaps");
        text.AppendLine("  A bit per row, saying which rows have a value. Encoded by the same choice");
        text.AppendLine("  a bit-packed value block uses; `unencoded` is what a bit a row would cost.");
        text.AppendLine();

        text.AppendLine($"    columns    {nullable.Count}");
        text.AppendLine($"    unencoded  {Bytes(raw)}");
        text.AppendLine(
            $"    encoded    {Bytes(encoded)}  ({Share(encoded, raw)} of unencoded)"
            + $" · saved {Bytes(raw - encoded)}");
        text.AppendLine();

        var wins = nullable
            .Where(column => column.PresenceEncodedBytes < column.PresenceBytes)
            .OrderByDescending(column => column.PresenceBytes - column.PresenceEncodedBytes)
            .ToList();

        text.AppendLine($"  {wins.Count} of {nullable.Count} bitmaps got smaller.");
        text.AppendLine();

        if (wins.Count == 0)
            return;

        text.AppendLine($"  {"rows",9}{"unencoded",13}{"encoded",13}  column");

        foreach (var column in wins.Take(ListedColumns))
        {
            text.AppendLine(
                $"  {column.Rows,9}{Bytes(column.PresenceBytes),13}"
                + $"{Bytes(column.PresenceEncodedBytes),13}  {column.Table}.{column.Column}");
        }

        text.AppendLine();
    }

    /// <summary>
    /// What the bit-packed block would cost as a whole block, presence bitmap included.
    /// </summary>
    /// <remarks>
    /// <see cref="ColumnEntry.PresenceBytes"/> says why the bitmap is added rather than
    /// ignored.
    /// </remarks>
    private static int Packed(ColumnEntry column)
        => column.Layers!.BitpackBytes + column.PresenceEncodedBytes;

    private void RenderLayerGroup(StringBuilder text, string title, List<ColumnEntry> columns)
    {
        if (columns.Count == 0)
            return;

        int today = columns.Sum(column => column.Bytes);
        int encoded = columns.Sum(column => column.Layers!.BestBytes + column.PresenceEncodedBytes);
        int elements = columns.Sum(column => column.Layers!.Elements);

        text.AppendLine($"  {title}");
        text.AppendLine($"    columns  {columns.Count}");
        text.AppendLine($"    elements {Bytes(elements)}");
        text.AppendLine($"    today    {Bytes(today)}");
        text.AppendLine(
            $"    encoded  {Bytes(encoded)}  ({Share(encoded, today)} of today)"
            + $" · saved {Bytes(today - encoded)}");
        text.AppendLine();
    }

    /// <summary>
    /// How much room is left in the string columns, and which structure it is held by.
    /// </summary>
    /// <remarks>
    /// The layouts named here are not in the format. They are measured so the question of
    /// adding one is answered by a number: each costs an encoding number, a decode path in
    /// every reader runtime and a corpus column proving it wins, and the deflate column is
    /// there to say how much any of them could possibly be worth before the first one is
    /// built.
    /// </remarks>
    private void RenderStringHeadroom(StringBuilder text)
    {
        var strings = _columns
            .Where(column => column.Structure != null && column.Structure!.DistinctCount > 0)
            .OrderByDescending(column => column.Structure!.FrontCodedBytes)
            .ThenBy(column => column.Table, StringComparer.Ordinal)
            .ThenBy(column => column.Column, StringComparer.Ordinal)
            .ToList();

        if (strings.Count == 0)
            return;

        text.AppendLine("String dictionary headroom");
        text.AppendLine("  What the distinct values of a string column cost under each layout. Only");
        text.AppendLine("  `front` is in the format; the rest are measurements, and `deflate` is the");
        text.AppendLine("  floor none of them can pass.");
        text.AppendLine();
        text.AppendLine(
            "  distinct        plain        front      segment     template        delta      deflate  column");

        int plainTotal = 0, frontTotal = 0, segmentTotal = 0;
        int templateTotal = 0, deltaTotal = 0, deflateTotal = 0;

        foreach (var column in strings)
        {
            var structure = column.Structure;

            plainTotal += structure!.PlainBytes;
            frontTotal += structure!.FrontCodedBytes;
            segmentTotal += structure!.SegmentBytes;
            templateTotal += structure!.TemplateBytes;
            deltaTotal += structure!.TemplateDeltaBytes;
            deflateTotal += structure!.DeflateBytes;
        }

        foreach (var column in strings.Take(ListedColumns))
        {
            var structure = column.Structure;

            text.AppendLine(
                $"  {structure!.DistinctCount,8}{Bytes(structure!.PlainBytes),13}"
                + $"{Bytes(structure!.FrontCodedBytes),13}{Bytes(structure!.SegmentBytes),13}"
                + $"{Bytes(structure!.TemplateBytes),13}{Bytes(structure!.TemplateDeltaBytes),13}"
                + $"{Bytes(structure!.DeflateBytes),13}  {column.Table}.{column.Column}");
        }

        text.AppendLine();
        text.AppendLine($"  Over all {strings.Count} string columns:");
        text.AppendLine($"    plain      {Bytes(plainTotal)}");
        text.AppendLine($"    front      {Bytes(frontTotal)}  ({Share(frontTotal, plainTotal)} of plain)");
        text.AppendLine($"    segment    {Bytes(segmentTotal)}  ({Share(segmentTotal, frontTotal)} of front)");
        text.AppendLine($"    template   {Bytes(templateTotal)}  ({Share(templateTotal, frontTotal)} of front)");
        text.AppendLine($"    delta      {Bytes(deltaTotal)}  ({Share(deltaTotal, frontTotal)} of front)");
        text.AppendLine($"    deflate    {Bytes(deflateTotal)}  ({Share(deflateTotal, frontTotal)} of front)");
        text.AppendLine();

        int best = strings.Sum(column => column.Structure!.BestBytes);

        text.AppendLine(
            $"  Best per column, layout chosen per column: {Bytes(best)}"
            + $"  ({Share(best, frontTotal)} of front)");
        text.AppendLine(
            $"  Headroom against what is in the format today: {Bytes(frontTotal - best)}");
        text.AppendLine();

        // Which layout wins where, and by how much. A layout that wins a great many columns
        // by a few bytes each is not worth an encoding number; one that wins few columns by a
        // lot may be. The sum of what a layout saves where it wins is the figure that decides
        // it, and neither the column count nor the total on its own says that.
        text.AppendLine("  Where each layout wins, and what it saves against front coding");
        text.AppendLine("    layout      columns       saved");

        var winners = strings
            .GroupBy(column => column.Structure!.BestLayout)
            .Select(group => (
                Layout: group.Key,
                Columns: group.Count(),
                Saved: group.Sum(c => c.Structure!.FrontCodedBytes - c.Structure!.BestBytes)))
            .OrderByDescending(group => group.Saved)
            .ThenBy(group => group.Layout, StringComparer.Ordinal);

        foreach (var winner in winners)
            text.AppendLine($"    {winner.Layout,-10}{winner.Columns,9}{Bytes(winner.Saved),12}");

        text.AppendLine();
    }

    private static string KindSuffix(byte kind) => kind switch
    {
        TcbFormat.KindArray => "[]",
        _ => string.Empty,
    };

    private static string Bytes(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Share(int part, int whole)
        => whole == 0
            ? "-"
            : ((double)part / whole).ToString("P1", CultureInfo.InvariantCulture);
}
