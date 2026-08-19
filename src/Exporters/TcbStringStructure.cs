using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Tabbit.Exporters;

/// <summary>
/// What a column's distinct strings look like structurally, and how small a dictionary layout
/// that exploited each structure could make them.
/// </summary>
/// <remarks>
/// Measurement only - nothing here reaches a file. It exists so that adding a dictionary
/// layout to the format is a decision taken against a measured ceiling rather than an
/// intuition. A layout costs an encoding number, a decode path in every reader runtime and a
/// corpus column that proves it wins somewhere; that is a price worth paying for bytes which
/// are demonstrably there and not otherwise.
///
/// Every figure is counted in the units the format actually uses - a counter32 costs what a
/// counter32 costs - so an oracle here and a real block are comparable without a correction
/// factor.
/// </remarks>
public static class TcbStringStructure
{
    /// <summary>
    /// A digit run longer than this is left in the literal text.
    /// </summary>
    /// <remarks>
    /// Nine digits is the widest run that certainly fits an int32, which is what a counter32
    /// carries. Extracting a wider one would put a second integer width into the layout to
    /// serve identifiers that are rare, and a run that long is usually a hash rather than a
    /// number that counts.
    /// </remarks>
    private const int MaxExtractedDigits = 9;

    /// <summary>What one column's distinct strings measured.</summary>
    public sealed class Report
    {
        public int DistinctCount { get; init; }

        /// <summary>The distinct strings' UTF-8 bytes, with nothing shared.</summary>
        public int PlainBytes { get; init; }

        /// <summary>What the dictionary block of the front-coded layout costs today.</summary>
        public int FrontCodedBytes { get; init; }

        /// <summary>
        /// Deflate over the same bytes - the floor no substring scheme can pass.
        /// </summary>
        /// <remarks>
        /// Not a candidate: a general compressor in the format would mean a dependency in
        /// the runtimes. It is here as the ceiling on what the schemes below could ever
        /// be worth, so that a layout measuring close to front coding can be dropped without
        /// building it.
        /// </remarks>
        public int DeflateBytes { get; init; }

        /// <summary>Segments the separator split produced.</summary>
        public int SegmentCount { get; init; }

        /// <summary>Segment references across every entry.</summary>
        public int SegmentPieces { get; init; }

        /// <summary>What a dictionary of segment references would cost.</summary>
        public int SegmentBytes { get; init; }

        /// <summary>Distinct templates once digit runs are lifted out.</summary>
        public int TemplateCount { get; init; }

        /// <summary>Digit runs lifted out across every entry.</summary>
        public int TemplateHoles { get; init; }

        /// <summary>What a dictionary of templates and their numbers would cost.</summary>
        public int TemplateBytes { get; init; }

        /// <summary>The same, with each hole stated as its step from the entry before.</summary>
        public int TemplateDeltaBytes { get; init; }

        /// <summary>The smallest of the layouts measured, front coding included.</summary>
        public int BestBytes => Math.Min(
            FrontCodedBytes, Math.Min(SegmentBytes, Math.Min(TemplateBytes, TemplateDeltaBytes)));

        /// <summary>
        /// Which layout that was. A tie goes to front coding, which is the one already in the
        /// format and so the one that costs nothing to keep.
        /// </summary>
        public string BestLayout
        {
            get
            {
                if (FrontCodedBytes <= BestBytes)
                    return "front";

                if (SegmentBytes <= BestBytes)
                    return "segment";

                return TemplateBytes <= BestBytes ? "template" : "delta";
            }
        }
    }

    /// <summary>
    /// Measures every dictionary layout over one column's distinct strings.
    /// </summary>
    /// <remarks>
    /// The distinct set rather than the rows, because the row stream is an index per row in
    /// every one of these layouts and so cancels out of the comparison. What differs between
    /// them is only how the distinct values themselves are held.
    /// </remarks>
    public static Report Measure(IReadOnlyList<string> distinct)
    {
        var entries = new List<byte[]>(distinct.Count);

        foreach (string value in distinct)
            entries.Add(Encoding.UTF8.GetBytes(value));

        entries.Sort(CompareBytes);

        var segments = MeasureSegments(entries);
        var templates = MeasureTemplates(entries);

        int plain = 0;
        foreach (var entry in entries)
            plain += Counter32Size(entry.Length) + entry.Length;

        return new Report
        {
            DistinctCount = entries.Count,
            PlainBytes = Counter32Size(entries.Count) + plain,
            FrontCodedBytes = Counter32Size(entries.Count) + FrontCodedSize(entries),
            DeflateBytes = DeflatedSize(entries),

            SegmentCount = segments.SegmentCount,
            SegmentPieces = segments.Pieces,
            SegmentBytes = segments.Bytes,

            TemplateCount = templates.TemplateCount,
            TemplateHoles = templates.Holes,
            TemplateBytes = templates.Bytes,
            TemplateDeltaBytes = templates.DeltaBytes,
        };
    }

    // ------------------------------------------------------------- segments

    private readonly struct SegmentMeasurement
    {
        public SegmentMeasurement(int segmentCount, int pieces, int bytes)
        {
            SegmentCount = segmentCount;
            Pieces = pieces;
            Bytes = bytes;
        }

        public int SegmentCount { get; }
        public int Pieces { get; }
        public int Bytes { get; }
    }

    /// <summary>
    /// What it would cost to hold each entry as references into a table of the pieces the
    /// entries are built out of.
    /// </summary>
    /// <remarks>
    /// Front coding can only share what two neighbours have in common at the front. Names
    /// assembled from parts share their parts everywhere else as well - the same middle
    /// section, the same tail - and a reference per piece reaches all of it. What it costs is
    /// a piece count and an index per piece on every entry, which is why the measurement is
    /// worth taking before the layout is worth having.
    /// </remarks>
    private static SegmentMeasurement MeasureSegments(List<byte[]> entries)
    {
        var index = new Dictionary<string, int>();
        var table = new List<byte[]>();
        var pieceLists = new List<List<int>>(entries.Count);

        foreach (var entry in entries)
        {
            var pieces = new List<int>();

            foreach (var piece in Split(entry))
            {
                string key = Convert.ToBase64String(piece);

                if (!index.TryGetValue(key, out int at))
                {
                    at = table.Count;
                    index.Add(key, at);
                    table.Add(piece);
                }

                pieces.Add(at);
            }

            pieceLists.Add(pieces);
        }

        // Sorted for the same reason the front-coded dictionary is: the table is itself a set
        // of strings, and the ones that came from neighbouring entries share their fronts.
        var sorted = new List<byte[]>(table);
        sorted.Sort(CompareBytes);

        var position = new Dictionary<string, int>(sorted.Count);
        for (int at = 0; at < sorted.Count; at++)
            position[Convert.ToBase64String(sorted[at])] = at;

        var remap = new int[table.Count];
        for (int at = 0; at < table.Count; at++)
            remap[at] = position[Convert.ToBase64String(table[at])];

        int bytes = Counter32Size(sorted.Count) + FrontCodedSize(sorted);
        int pieceTotal = 0;

        bytes += Counter32Size(entries.Count);

        foreach (var pieces in pieceLists)
        {
            bytes += Counter32Size(pieces.Count);
            pieceTotal += pieces.Count;

            foreach (int piece in pieces)
                bytes += Counter32Size(remap[piece]);
        }

        return new SegmentMeasurement(sorted.Count, pieceTotal, bytes);
    }

    /// <summary>
    /// Cuts an entry where its structure changes: after a separator, and where digits meet
    /// text.
    /// </summary>
    /// <remarks>
    /// A separator stays with the piece it ends, so that a name and the separator that
    /// follows it are one reference rather than two. The cut between digits and text is what
    /// makes the numbered members of a family share everything but their number.
    ///
    /// ASCII only. A multi-byte sequence's bytes are all above 0x7F, so they are neither
    /// separators nor digits here and travel inside whatever piece they fall in - which keeps
    /// every piece a whole sequence of characters and never half of one.
    /// </remarks>
    private static List<byte[]> Split(byte[] entry)
    {
        var pieces = new List<byte[]>();
        int start = 0;

        for (int at = 0; at < entry.Length; at++)
        {
            bool cut = IsSeparator(entry[at])
                || (at + 1 < entry.Length && IsDigit(entry[at]) != IsDigit(entry[at + 1]));

            if (!cut)
                continue;

            pieces.Add(entry[start..(at + 1)]);
            start = at + 1;
        }

        if (start < entry.Length)
            pieces.Add(entry[start..]);

        return pieces;
    }

    private static bool IsSeparator(byte value)
        => value == (byte)'_' || value == (byte)'-' || value == (byte)'/' || value == (byte)'.'
            || value == (byte)'\\' || value == (byte)':' || value == (byte)' ' || value == (byte)'|';

    private static bool IsDigit(byte value) => value >= (byte)'0' && value <= (byte)'9';

    // ------------------------------------------------------------ templates

    private readonly struct TemplateMeasurement
    {
        public TemplateMeasurement(int templateCount, int holes, int bytes, int deltaBytes)
        {
            TemplateCount = templateCount;
            Holes = holes;
            Bytes = bytes;
            DeltaBytes = deltaBytes;
        }

        public int TemplateCount { get; }
        public int Holes { get; }
        public int Bytes { get; }
        public int DeltaBytes { get; }
    }

    /// <summary>
    /// What it would cost to hold each entry as a template and the numbers that fill it.
    /// </summary>
    /// <remarks>
    /// A family of identifiers that differ only in a counter is the case front coding handles
    /// worst: every entry shares its whole front with its neighbour and still pays for the
    /// digits that follow, one byte per digit, on every one of them. Held as a number those
    /// digits cost what the value costs, and a run of consecutive ones costs almost nothing
    /// once each is stated as its step from the last.
    ///
    /// The width is kept beside the value because leading zeros are part of the text: an
    /// identifier padded to six digits and the same number padded to three are different
    /// strings, and a layout that could not tell them apart would not round-trip.
    /// </remarks>
    private static TemplateMeasurement MeasureTemplates(List<byte[]> entries)
    {
        var index = new Dictionary<string, int>();
        var table = new List<byte[]>();

        // One row per entry: the template it uses, and the (width, value) of each hole.
        var shapes = new List<(int Template, List<(int Width, int Value)> Holes)>(entries.Count);

        foreach (var entry in entries)
        {
            var literal = new List<byte>(entry.Length);
            var holes = new List<(int, int)>();

            for (int at = 0; at < entry.Length;)
            {
                if (!IsDigit(entry[at]))
                {
                    literal.Add(entry[at]);
                    at++;
                    continue;
                }

                int end = at;
                while (end < entry.Length && IsDigit(entry[end]))
                    end++;

                int width = end - at;

                if (width > MaxExtractedDigits)
                {
                    // Too wide to be carried as a counter32. Left where it is, which is what
                    // a real layout would have to do as well.
                    for (int digit = at; digit < end; digit++)
                        literal.Add(entry[digit]);

                    at = end;
                    continue;
                }

                int value = 0;
                for (int digit = at; digit < end; digit++)
                    value = (value * 10) + (entry[digit] - (byte)'0');

                // A control byte no design-data text carries, marking where a number was
                // taken out. The measurement only needs the templates to be distinguishable
                // from one another; a real layout would state the chunks separately.
                literal.Add(0x1F);
                holes.Add((width, value));

                at = end;
            }

            var template = literal.ToArray();
            string key = Convert.ToBase64String(template);

            if (!index.TryGetValue(key, out int templateAt))
            {
                templateAt = table.Count;
                index.Add(key, templateAt);
                table.Add(template);
            }

            shapes.Add((templateAt, holes));
        }

        var sorted = new List<byte[]>(table);
        sorted.Sort(CompareBytes);

        var position = new Dictionary<string, int>(sorted.Count);
        for (int at = 0; at < sorted.Count; at++)
            position[Convert.ToBase64String(sorted[at])] = at;

        var remap = new int[table.Count];
        for (int at = 0; at < table.Count; at++)
            remap[at] = position[Convert.ToBase64String(table[at])];

        int header = Counter32Size(sorted.Count) + FrontCodedSize(sorted) + Counter32Size(entries.Count);

        int plain = header;
        int delta = header;
        int holeTotal = 0;

        // The step is taken against the last value seen at the same position under the same
        // template, so that a family numbered in order steps by one however many other
        // families are interleaved with it in the sorted dictionary.
        var previous = new Dictionary<(int Template, int Hole), int>();

        foreach (var (template, holes) in shapes)
        {
            int reference = Counter32Size(remap[template]) + Counter32Size(holes.Count);

            plain += reference;
            delta += reference;
            holeTotal += holes.Count;

            for (int at = 0; at < holes.Count; at++)
            {
                var (width, value) = holes[at];

                plain += Counter32Size(width) + Counter32Size(value);

                var key = (remap[template], at);
                int step = previous.TryGetValue(key, out int last) ? unchecked(value - last) : value;
                previous[key] = value;

                delta += Counter32Size(width) + Counter32Size(step);
            }
        }

        return new TemplateMeasurement(sorted.Count, holeTotal, plain, delta);
    }

    // ------------------------------------------------------------- measures

    /// <summary>What the entries cost when each states only what it adds to the one before.</summary>
    private static int FrontCodedSize(List<byte[]> entries)
    {
        int bytes = 0;
        var previous = Array.Empty<byte>();

        foreach (var entry in entries)
        {
            int shared = 0;
            int limit = Math.Min(previous.Length, entry.Length);

            while (shared < limit && previous[shared] == entry[shared])
                shared++;

            bytes += Counter32Size(shared) + Counter32Size(entry.Length - shared) + (entry.Length - shared);
            previous = entry;
        }

        return bytes;
    }

    /// <summary>
    /// What a general compressor makes of a block that is already encoded.
    /// </summary>
    /// <remarks>
    /// The measurement behind the compression flag the header reserves. A layer there would
    /// cost every reader runtime a decompressor, which is a large thing to carry; this says
    /// what it would be worth, against the encodings that are already doing the work.
    /// </remarks>
    public static int Deflate(ReadOnlySpan<byte> block)
    {
        using var output = new MemoryStream();

        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(block);

        return (int)output.Length;
    }

    private static int DeflatedSize(List<byte[]> entries)
    {
        using var output = new MemoryStream();

        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var length = new byte[4];

            foreach (var entry in entries)
            {
                // Length-prefixed, so the compressor is given the same job the format has -
                // recovering the boundaries as well as the bytes.
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, entry.Length);

                deflate.Write(length);
                deflate.Write(entry);
            }
        }

        return (int)output.Length;
    }

    /// <summary>How many bytes a counter32 of this value occupies.</summary>
    private static int Counter32Size(int value)
    {
        uint encoded = unchecked((uint)((value << 1) ^ (value >> 31)));

        int bytes = 1;
        while (encoded >= 0x80)
        {
            encoded >>= 7;
            bytes++;
        }

        return bytes;
    }

    private static int CompareBytes(byte[] left, byte[] right)
    {
        int limit = Math.Min(left.Length, right.Length);

        for (int at = 0; at < limit; at++)
        {
            if (left[at] != right[at])
                return left[at] < right[at] ? -1 : 1;
        }

        return left.Length.CompareTo(right.Length);
    }
}
