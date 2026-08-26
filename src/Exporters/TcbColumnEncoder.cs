using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Tabbit.Exporters;

/// <summary>
/// Chooses how a stream of values is laid out, by encoding it every way that applies and
/// keeping the smallest.
/// </summary>
/// <remarks>
/// No statistics and no heuristics. Encode time is the one resource this format's design does
/// not care about - a file is written once by a build and read on every start of every client
/// - and a measured byte count is the one selector that is never wrong about which of two
/// layouts is smaller.
///
/// What it operates on is a flat stream of one element type, not a column. That is what lets
/// an array column reach the same choice: its elements are a flat stream of the same element
/// type, so the array encoding is a composition of two of these rather than a tenth layout
/// with a second dimension in it.
/// </remarks>
internal static class TcbColumnEncoder
{
    /// <summary>
    /// One column's values, flattened, in whichever forms the candidates need them.
    /// </summary>
    /// <remarks>
    /// The typed arrays are built once and shared between candidates, because a column of a
    /// hundred thousand rows is walked by every one of them and converting per candidate
    /// would be the only expensive thing here.
    ///
    /// <see cref="Raw"/> is the authority on what a value's bytes are: it is written through
    /// the same path the unencoded block uses, and the fixed-width entries are sliced back
    /// out of it. So a dictionary entry cannot disagree with what a raw block would have
    /// held, because there is no second encoding path for it to disagree with.
    /// </remarks>
    internal sealed record Stream
    {
        public byte Element { get; init; }

        /// <summary>How many values, across every row.</summary>
        public int Count { get; init; }

        /// <summary>The unencoded stream, which is always a candidate and always the baseline.</summary>
        public required TcbWriter Raw { get; init; }

        /// <summary>Set for i32, varint and bool.</summary>
        public int[]? Integers { get; init; }

        /// <summary>
        /// Set for every element that is an integer - i32, i64, varint and bool.
        /// </summary>
        /// <remarks>
        /// Widened to 64 bits because the bit-width layout is one candidate over all four,
        /// and its base is a value of the column's own type: an i64 column's base does not
        /// fit in an int32, and narrowing it here would make that column's candidate wrong
        /// rather than absent.
        /// </remarks>
        public long[]? Longs { get; init; }

        /// <summary>Set for string.</summary>
        public string[]? Strings { get; init; }

        /// <summary>Set for the fixed-width elements: each value's own bytes.</summary>
        public byte[][]? Fixed { get; init; }

        /// <summary>Set for f32 and f64.</summary>
        public double[]? Numbers { get; init; }
    }

    /// <summary>One stream's layout: the encoding chosen, and the bytes it produced.</summary>
    internal readonly struct Block
    {
        public Block(byte encoding, TcbWriter payload)
        {
            Encoding = encoding;
            Payload = payload;
        }

        public byte Encoding { get; }
        public TcbWriter Payload { get; }
    }

    /// <summary>
    /// The candidates offered for one stream, the smallest of them, and what each measured.
    /// </summary>
    /// <remarks>
    /// A challenger has to be strictly smaller to take the place, and candidates are offered
    /// in ascending encoding order, so a tie keeps the lower number. That is what keeps the
    /// choice deterministic, and determinism is what the golden trees and the format's own
    /// fixed test rest on.
    ///
    /// The sizes of the candidates that lost are kept as well. They cost two numbers each and
    /// they are the only record of what the alternatives came to, which is what the encoding
    /// report is made of.
    /// </remarks>
    internal sealed class Selection
    {
        private Block _best;

        public Selection(TcbWriter raw)
        {
            _best = new Block(TcbFormat.EncodingRaw, raw);
            Measured.Add((TcbFormat.EncodingRaw, raw.Length));
        }

        public List<(byte Encoding, int Bytes)> Measured { get; } = new();

        public Block Best => _best;

        public void Offer(byte encoding, TcbWriter challenger)
        {
            Measured.Add((encoding, challenger.Length));

            if (challenger.Length < _best.Payload.Length)
                _best = new Block(encoding, challenger);
        }
    }

    // ------------------------------------------------------------- the choice

    /// <summary>
    /// Every encoding this element type allows, measured, with the smallest kept.
    /// </summary>
    public static Selection Choose(Stream stream)
    {
        var selection = new Selection(stream.Raw);

        switch (stream.Element)
        {
            case TcbFormat.ElementI32:
                OfferIntegers(selection, stream.Integers!);
                OfferBitpack(selection, stream.Longs!);
                break;

            case TcbFormat.ElementVarint:
                // Raw already is a stream of counter32, so of the integer candidates only
                // run-length encoding can say anything raw does not.
                selection.Offer(TcbFormat.EncodingRle, Rle(stream.Integers!));
                OfferBitpack(selection, stream.Longs!);
                break;

            case TcbFormat.ElementBool:
                selection.Offer(TcbFormat.EncodingRle, Rle(stream.Integers!));
                OfferBitpack(selection, stream.Longs!);
                break;

            case TcbFormat.ElementString:
                selection.Offer(TcbFormat.EncodingDict, Dictionary(stream.Strings!, false));
                selection.Offer(TcbFormat.EncodingDictRle, Dictionary(stream.Strings!, true));
                selection.Offer(TcbFormat.EncodingDictFront, DictionaryFront(stream.Strings!, false));
                selection.Offer(TcbFormat.EncodingDictFrontRle, DictionaryFront(stream.Strings!, true));
                selection.Offer(TcbFormat.EncodingDictSegment, DictionarySegment(stream.Strings!, false));
                selection.Offer(TcbFormat.EncodingDictSegmentRle, DictionarySegment(stream.Strings!, true));
                break;

            // The dictionary is parameterized by element, so a column of floats or of ticks
            // reaches it with nothing added to the format: an entry is four or eight bytes
            // rather than a length and some UTF-8.
            case TcbFormat.ElementF32:
            case TcbFormat.ElementF64:
                selection.Offer(TcbFormat.EncodingDict, ValueDictionary(stream.Fixed!, false));
                selection.Offer(TcbFormat.EncodingDictRle, ValueDictionary(stream.Fixed!, true));
                OfferWholeNumbers(selection, stream);
                break;

            case TcbFormat.ElementI64:
                selection.Offer(TcbFormat.EncodingDict, ValueDictionary(stream.Fixed!, false));
                selection.Offer(TcbFormat.EncodingDictRle, ValueDictionary(stream.Fixed!, true));
                OfferBitpack(selection, stream.Longs!);
                break;

            // uuid: sixteen bytes repeated is not a shape design data produces, and a
            // dictionary of them loses unless the repetition is severe.
        }

        return selection;
    }

    /// <summary>
    /// Offers the bit-width layout, where the stream has one at all.
    /// </summary>
    /// <remarks>
    /// Offered last of every element's candidates, because it is the highest number and the
    /// selection keeps the lower one on a tie. A column whose values already fill their
    /// element's width produces a block the size of the raw one plus a header, which loses on
    /// measurement - so nothing has to decide in advance whether the layout suits a column.
    /// </remarks>
    private static void OfferBitpack(Selection selection, long[] values)
    {
        if (values is null || values.Length == 0)
            return;

        var packed = Bitpack(values);

        if (packed.Applies)
            selection.Offer(TcbFormat.EncodingBitpack, packed.Payload);
    }

    private static void OfferIntegers(Selection selection, int[] values)
    {
        selection.Offer(TcbFormat.EncodingVarint, Varint(values));
        selection.Offer(TcbFormat.EncodingDelta, Delta(values));
        selection.Offer(TcbFormat.EncodingRle, Rle(values));
        selection.Offer(TcbFormat.EncodingDeltaRle, DeltaRle(values));
    }

    /// <summary>
    /// Offers the whole-number layout, when every value of a float stream survives being one.
    /// </summary>
    /// <remarks>
    /// The test is not that the value has no fractional part. It is that writing the integer
    /// back out produces the same bytes the raw block would have held - which additionally
    /// rules out negative zero, whose bit pattern differs from zero's, the values a single
    /// cannot represent exactly above its twenty-four bits of mantissa, and the infinities
    /// and NaNs that have no integer at all.
    ///
    /// Checked rather than assumed because the failure it prevents is silent: a column would
    /// read back with values that are nearly right, and nothing downstream would say so.
    /// </remarks>
    private static void OfferWholeNumbers(Selection selection, Stream stream)
    {
        var whole = new int[stream.Count];
        bool single = stream.Element == TcbFormat.ElementF32;

        // One buffer for the loop below, which compares the bytes of every value in the
        // column. Outside the loop because a stackalloc inside one grows the frame per
        // iteration.
        Span<byte> scratch = stackalloc byte[8];

        for (int at = 0; at < stream.Count; at++)
        {
            double value = stream.Numbers![at];

            if (!(value >= int.MinValue && value <= int.MaxValue) || value != Math.Floor(value))
                return;

            int integer = (int)value;

            // Written into the stack buffer rather than through a TcbWriter. This runs once
            // per value of every float column, and a writer starts at 64 KB - so the
            // measurement was allocating that much to compare four bytes.
            int width;

            if (single)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    scratch, BitConverter.SingleToInt32Bits((float)integer));
                width = 4;
            }
            else
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    scratch, BitConverter.DoubleToInt64Bits((double)integer));
                width = 8;
            }

            if (!scratch.Slice(0, width).SequenceEqual(stream.Fixed![at]))
                return;

            whole[at] = integer;
        }

        // The integers, laid out by whichever integer encoding is smallest, with the block
        // saying which one that was. Composition rather than four more encoding numbers.
        var inner = new Selection(Varint(whole));

        inner.Offer(TcbFormat.EncodingDelta, Delta(whole));
        inner.Offer(TcbFormat.EncodingRle, Rle(whole));
        inner.Offer(TcbFormat.EncodingDeltaRle, DeltaRle(whole));

        // The baseline of the inner selection is VARINT rather than RAW: a raw integer stream
        // is four fixed bytes a value, which is what the float block already costs.
        byte innerEncoding = inner.Best.Encoding == TcbFormat.EncodingRaw
            ? TcbFormat.EncodingVarint
            : inner.Best.Encoding;

        var payload = new TcbWriter();

        payload.Write(innerEncoding);
        payload.Write(inner.Best.Payload.WrittenSpan);

        selection.Offer(TcbFormat.EncodingWhole, payload);
    }

    /// <summary>
    /// An array column: the lengths of its rows and its elements, each laid out by the same
    /// rules, with the block naming both.
    /// </summary>
    /// <remarks>
    /// The lengths travel as a varint stream, so what may be chosen for them is what may be
    /// chosen for any varint column - raw, which is a counter32 apiece, or runs. A column
    /// whose rows are all the same length, which is most of them, becomes one run.
    ///
    /// That last sentence is why v107 could drop the fixed-length kind. Its whole saving was
    /// the length stream, and the length stream of a column that never varies is a single
    /// run - so the format was carrying a second array shape, and a count in every
    /// descriptor, to save a handful of bytes per column.
    /// </remarks>
    public static TcbWriter EncodeArray(Stream elements, int[] lengths)
    {
        var chosen = Choose(elements);


        var payload = new TcbWriter();
        payload.Write(chosen.Best.Encoding);

        var lengthRaw = new TcbWriter();
        foreach (int length in lengths)
            lengthRaw.WriteCounter32(length);

        var lengthChoice = new Selection(lengthRaw);
        lengthChoice.Offer(TcbFormat.EncodingRle, Rle(lengths));

        payload.Write(lengthChoice.Best.Encoding);
        payload.Write(lengthChoice.Best.Payload.WrittenSpan);
        payload.Write(chosen.Best.Payload.WrittenSpan);

        return payload;
    }

    // ------------------------------------------------- bit-width packing (measured)

    /// <summary>
    /// What a fixed-bit-width layout would cost a stream of integers.
    /// </summary>
    /// <remarks>
    /// **Not in the format.** This is the measurement that decides whether it should be, and
    /// it is here rather than in the report so that the layout is described once - a
    /// candidate measured by one definition and written by another is a candidate whose
    /// numbers mean nothing.
    ///
    /// Two parameters, both of which the block would have to carry. The **base** is
    /// subtracted from every value, so a column whose flags start at bit 8, or whose enum
    /// labels are numbered from 100, is measured by how much it varies rather than by how
    /// large it is. The **width** is what the remainder needs. Together they are the standard
    /// frame-of-reference pairing, and what makes them worth carrying is that a column using
    /// five flags is eight bytes a row today.
    ///
    /// The packed bytes are then offered to the integer encodings, because packing turns
    /// bit-level structure into byte-level structure: a column that is mostly zero becomes a
    /// run of zero bytes, which run-length encoding takes the rest of the way. Which inner
    /// candidate actually earns its place is the other thing this measures.
    /// </remarks>
    internal readonly struct BitpackMeasure
    {
        /// <summary>Whether a narrower width than the element's own exists at all.</summary>
        public bool Applies { get; init; }

        /// <summary>Bits per value, 1 to 64.</summary>
        public int Width { get; init; }

        /// <summary>Subtracted from every value before packing.</summary>
        public long Base { get; init; }

        /// <summary>The block, ready to be offered as a candidate.</summary>
        public TcbWriter Payload { get; init; }

        /// <summary>What it came to, inner encoding and header included.</summary>
        public int BestBytes { get; init; }

        /// <summary>Which inner encoding carried the packed bytes.</summary>
        public byte BestInner { get; init; }
    }

    /// <summary>Lays out a stream of integers at the width its range needs.</summary>
    internal static BitpackMeasure Bitpack(long[] values)
    {
        if (values.Length == 0)
            return default;

        long low = values[0];
        long high = values[0];

        foreach (long value in values)
        {
            if (value < low) low = value;
            if (value > high) high = value;
        }

        // Wrapping on purpose: two int64s can be further apart than an int64 holds, and the
        // subtraction is exact as an unsigned span either way.
        ulong span = unchecked((ulong)high - (ulong)low);
        int width = span == 0 ? 1 : 64 - System.Numerics.BitOperations.LeadingZeroCount(span);

        // A continuous bit stream, low bit first, a value free to cross a byte boundary.
        // Padding is at most seven bits over the whole column rather than seven per row.
        var packed = new byte[(int)(((long)values.Length * width + 7) / 8)];
        long bit = 0;

        foreach (long value in values)
        {
            ulong slot = unchecked((ulong)value - (ulong)low);

            for (int at = 0; at < width; at++, bit++)
            {
                if ((slot >> at & 1) != 0)
                    packed[bit >> 3] |= (byte)(1 << (int)(bit & 7));
            }
        }

        var (inner, encoded) = EncodeByteStream(packed);

        // The base travels as a zig-zag varint, so a column based at zero pays one byte for
        // it. That matters more than it looks: a column of fifty rows is decided by the
        // header, and most columns are based at zero.
        var payload = new TcbWriter();

        payload.Write((byte)width);
        payload.WriteOptimalInt64(low);
        payload.Write(inner);
        payload.Write(encoded.WrittenSpan);

        return new BitpackMeasure
        {
            Applies = true,
            Width = width,
            Base = low,
            Payload = payload,
            BestBytes = payload.Length,
            BestInner = inner,
        };
    }

    /// <summary>
    /// The smallest layout of a stream of bytes, and which encoding that was.
    /// </summary>
    /// <remarks>
    /// **Two callers, one definition.** A bit-packed value block ends in a stream of bytes,
    /// and so does a presence bitmap - the bitmap *is* a bit-packed boolean column of width
    /// one, and the only reason it does not go through <see cref="Bitpack"/> is that its
    /// width and base are known in advance and would be two bytes of the block restating
    /// what the format already says.
    ///
    /// Raw is the baseline and is the bytes themselves. The four integer encodings then read
    /// those bytes as the stream of small numbers they are, which is where the gain is:
    /// packing turns bit-level structure into byte-level structure, so a column that is
    /// mostly one value becomes a run of identical bytes.
    ///
    /// Ascending order and strictly-smaller, so a tie keeps the lower number - the same rule
    /// the outer selection follows, for the same reason.
    /// </remarks>
    internal static (byte Encoding, TcbWriter Payload) EncodeByteStream(byte[] packed)
    {
        var bytes = new int[packed.Length];
        for (int at = 0; at < packed.Length; at++)
            bytes[at] = packed[at];

        var raw = new TcbWriter();
        raw.Write(packed);

        byte best = TcbFormat.EncodingRaw;
        var chosen = raw;

        foreach (var (encoding, writer) in new (byte, TcbWriter)[]
        {
            (TcbFormat.EncodingVarint, Varint(bytes)),
            (TcbFormat.EncodingDelta, Delta(bytes)),
            (TcbFormat.EncodingRle, Rle(bytes)),
            (TcbFormat.EncodingDeltaRle, DeltaRle(bytes)),
        })
        {
            if (writer.Length < chosen.Length)
            {
                best = encoding;
                chosen = writer;
            }
        }

        return (best, chosen);
    }

    // -------------------------------------------------------------- integers

    /// <summary>
    /// A buffer for one candidate encoding, sized from how many values it will hold.
    /// </summary>
    /// <remarks>
    /// **Because a column of ten values was being measured in a 64 KB buffer.** Every
    /// candidate needs a buffer of its own - that is what "encode it every way and keep the
    /// smallest" means - and <see cref="TcbWriter"/>'s own default is the size of a table's
    /// output rather than of a column's candidate. With four to eight candidates per column
    /// and tens of thousands of columns, the default was most of what the export allocated.
    ///
    /// An estimate, not a limit: the writer grows by doubling, so a short guess costs a copy
    /// and a long one costs nothing but the slack. The floor keeps a tiny column from
    /// paying for a resize on its first value.
    /// spec/ops/conversion-time.md section 4.
    /// </remarks>
    private static TcbWriter Candidate(int values, int bytesPerValue = 5)
        => new TcbWriter(Math.Clamp(values * bytesPerValue + 16, 256, 64 * 1024));

    public static TcbWriter Varint(int[] values)
    {
        var payload = Candidate(values.Length);

        foreach (int value in values)
            payload.WriteOptimalInt32(value);

        return payload;
    }

    /// <summary>
    /// The first value, then each step from its predecessor.
    /// </summary>
    /// <remarks>
    /// The subtraction wraps on purpose: two int32s can be further apart than an int32 holds,
    /// and two's-complement wrapping makes the round trip exact for every pair anyway.
    /// Readers add the delta back with the same wrapping.
    /// </remarks>
    public static TcbWriter Delta(int[] values)
    {
        var payload = Candidate(values.Length);

        if (values.Length == 0)
            return payload;

        payload.WriteOptimalInt32(values[0]);

        for (int at = 1; at < values.Length; at++)
            payload.WriteOptimalInt32(unchecked(values[at] - values[at - 1]));

        return payload;
    }

    /// <summary>(run length, value) pairs whose run lengths sum to the value count.</summary>
    public static TcbWriter Rle(int[] values)
    {
        // A run is a count and a value, so the worst case - no repeats at all - is wider
        // than the plain varint stream rather than narrower.
        var payload = Candidate(values.Length, bytesPerValue: 10);

        for (int at = 0; at < values.Length;)
        {
            int run = 1;
            while (at + run < values.Length && values[at + run] == values[at])
                run++;

            payload.WriteCounter32(run);
            payload.WriteOptimalInt32(values[at]);

            at += run;
        }

        return payload;
    }

    /// <summary>
    /// The first value, then the delta stream run-length encoded - which is what flattens an
    /// identifier column stepping by one into a few bytes.
    /// </summary>
    public static TcbWriter DeltaRle(int[] values)
    {
        var payload = Candidate(values.Length, bytesPerValue: 10);

        if (values.Length == 0)
            return payload;

        payload.WriteOptimalInt32(values[0]);

        var deltas = new int[values.Length - 1];
        for (int at = 0; at < deltas.Length; at++)
            deltas[at] = unchecked(values[at + 1] - values[at]);

        for (int at = 0; at < deltas.Length;)
        {
            int run = 1;
            while (at + run < deltas.Length && deltas[at + run] == deltas[at])
                run++;

            payload.WriteCounter32(run);
            payload.WriteOptimalInt32(deltas[at]);

            at += run;
        }

        return payload;
    }

    // ----------------------------------------------------------- dictionaries

    /// <summary>
    /// The distinct strings once, in first-appearance order, then an index per value.
    /// </summary>
    /// <remarks>
    /// First-appearance order rather than sorted: one pass builds it, and the output stays
    /// deterministic without the format having to say anything about collation.
    /// </remarks>
    public static TcbWriter Dictionary(string[] values, bool runLength)
    {
        var payload = Candidate(values.Length);

        var seen = new Dictionary<string, int>();
        var entries = new List<string>();
        var indexes = new int[values.Length];

        for (int at = 0; at < values.Length; at++)
        {
            if (!seen.TryGetValue(values[at], out int index))
            {
                index = entries.Count;
                seen.Add(values[at], index);
                entries.Add(values[at]);
            }

            indexes[at] = index;
        }

        payload.WriteCounter32(entries.Count);

        foreach (string entry in entries)
            payload.Write(entry);

        WriteIndexes(payload, indexes, runLength);

        return payload;
    }

    /// <summary>
    /// A dictionary of fixed-width values, indexes plain or run-length encoded.
    /// </summary>
    /// <remarks>
    /// The same shape as the string dictionary; only an entry's bytes differ, which is the
    /// whole of what "parameterized by element" means here.
    /// </remarks>
    public static TcbWriter ValueDictionary(byte[][] values, bool runLength)
    {
        var payload = Candidate(values.Length);

        var seen = new Dictionary<string, int>();
        var entries = new List<byte[]>();
        var indexes = new int[values.Length];

        for (int at = 0; at < values.Length; at++)
        {
            // Keyed by the bytes themselves, so two values are the same entry exactly when
            // they were written the same - which for a float is what equality has to mean
            // here, NaN and negative zero included.
            string key = Convert.ToBase64String(values[at]);

            if (!seen.TryGetValue(key, out int index))
            {
                index = entries.Count;
                seen.Add(key, index);
                entries.Add(values[at]);
            }

            indexes[at] = index;
        }

        payload.WriteCounter32(entries.Count);

        foreach (var entry in entries)
            payload.Write(entry);

        WriteIndexes(payload, indexes, runLength);

        return payload;
    }

    /// <summary>
    /// A sorted string dictionary, each entry stating only what it does not share with the
    /// entry before it.
    /// </summary>
    /// <remarks>
    /// Sorted by UTF-8 bytes rather than by anything a locale has an opinion about, so every
    /// language's writer would produce the same order from the same values.
    /// </remarks>
    public static TcbWriter DictionaryFront(string[] values, bool runLength)
    {
        var (entries, indexes) = SortedDistinct(values);

        var payload = new TcbWriter();
        payload.WriteCounter32(entries.Count);

        WriteFrontCoded(payload, entries);
        WriteIndexes(payload, indexes, runLength);

        return payload;
    }

    /// <summary>
    /// A dictionary whose entries are lists of references into a shared table of the pieces
    /// they are built from.
    /// </summary>
    /// <remarks>
    /// What front coding cannot reach. Two entries that share a middle section or a tail
    /// share nothing under front coding, which can only take what neighbours have in common
    /// at the front; here they share a table row.
    ///
    /// The pieces are not searched for. They are where the values change character - after a
    /// separator, and where digits meet text - which is deterministic, costs one pass, and on
    /// the data this was measured against found what a search would have.
    ///
    /// Nothing prunes the table. A table full of pieces that earn nothing makes this
    /// candidate large, and a large candidate loses to front coding on measurement, which is
    /// the same answer pruning would have reached by a longer route.
    /// </remarks>
    public static TcbWriter DictionarySegment(string[] values, bool runLength)
    {
        var seen = new Dictionary<string, int>();
        var entries = new List<byte[]>();
        var indexes = new int[values.Length];

        for (int at = 0; at < values.Length; at++)
        {
            if (!seen.TryGetValue(values[at], out int index))
            {
                index = entries.Count;
                seen.Add(values[at], index);
                entries.Add(Encoding.UTF8.GetBytes(values[at]));
            }

            indexes[at] = index;
        }

        // The table, and each entry as the pieces it is made of.
        var table = new Dictionary<string, int>();
        var segments = new List<byte[]>();
        var pieceLists = new List<List<int>>(entries.Count);

        foreach (var entry in entries)
        {
            var pieces = new List<int>();

            foreach (var piece in Segments(entry))
            {
                string key = Convert.ToBase64String(piece);

                if (!table.TryGetValue(key, out int at))
                {
                    at = segments.Count;
                    table.Add(key, at);
                    segments.Add(piece);
                }

                pieces.Add(at);
            }

            pieceLists.Add(pieces);
        }

        // The table is itself a set of strings, and the pieces of neighbouring values share
        // their fronts, so it is held the way the front-coded dictionary holds its entries.
        var sorted = new List<byte[]>(segments);
        sorted.Sort(CompareBytes);

        var position = new Dictionary<string, int>(sorted.Count);
        for (int at = 0; at < sorted.Count; at++)
            position[Convert.ToBase64String(sorted[at])] = at;

        var remap = new int[segments.Count];
        for (int at = 0; at < segments.Count; at++)
            remap[at] = position[Convert.ToBase64String(segments[at])];

        var payload = new TcbWriter();

        payload.WriteCounter32(sorted.Count);
        WriteFrontCoded(payload, sorted);

        payload.WriteCounter32(entries.Count);

        foreach (var pieces in pieceLists)
        {
            payload.WriteCounter32(pieces.Count);

            foreach (int piece in pieces)
                payload.WriteCounter32(remap[piece]);
        }

        WriteIndexes(payload, indexes, runLength);

        return payload;
    }

    /// <summary>
    /// Cuts a value where its structure changes: after a separator, and where digits meet
    /// text.
    /// </summary>
    /// <remarks>
    /// A separator stays with the piece it ends, so a name and the separator after it are one
    /// reference rather than two. The cut between digits and text is what makes the numbered
    /// members of a family share everything but their number.
    ///
    /// ASCII only. Every byte of a multi-byte sequence is above 0x7F, so none of them is a
    /// separator or a digit here and each sequence travels whole inside whichever piece it
    /// falls in - a piece is never half of a character.
    ///
    /// One definition, used by the encoder and by the measurement that decides whether the
    /// encoder is worth having. Two would eventually disagree.
    /// </remarks>
    public static List<byte[]> Segments(byte[] entry)
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

    // ------------------------------------------------------------- the parts

    /// <summary>The distinct values sorted by their bytes, and each value's place in them.</summary>
    private static (List<byte[]> Entries, int[] Indexes) SortedDistinct(string[] values)
    {
        var encoded = new Dictionary<string, byte[]>();

        foreach (string value in values)
        {
            if (!encoded.ContainsKey(value))
                encoded.Add(value, Encoding.UTF8.GetBytes(value));
        }

        var entries = new List<byte[]>(encoded.Values);
        entries.Sort(CompareBytes);

        var position = new Dictionary<string, int>(entries.Count);
        for (int at = 0; at < entries.Count; at++)
            position[Convert.ToBase64String(entries[at])] = at;

        var order = new Dictionary<string, int>(encoded.Count);
        foreach (var pair in encoded)
            order[pair.Key] = position[Convert.ToBase64String(pair.Value)];

        var indexes = new int[values.Length];
        for (int at = 0; at < values.Length; at++)
            indexes[at] = order[values[at]];

        return (entries, indexes);
    }

    /// <summary>Entries in order, each stating only what it adds to the one before.</summary>
    private static void WriteFrontCoded(TcbWriter payload, List<byte[]> entries)
    {
        var previous = Array.Empty<byte>();

        foreach (var entry in entries)
        {
            int shared = 0;
            int limit = Math.Min(previous.Length, entry.Length);

            while (shared < limit && previous[shared] == entry[shared])
                shared++;

            payload.WriteCounter32(shared);
            payload.WriteCounter32(entry.Length - shared);
            payload.Write(entry.AsSpan(shared));

            previous = entry;
        }
    }

    /// <summary>An index stream, plainly or as runs, shared by every dictionary encoding.</summary>
    private static void WriteIndexes(TcbWriter payload, int[] indexes, bool runLength)
    {
        if (!runLength)
        {
            foreach (int index in indexes)
                payload.WriteCounter32(index);

            return;
        }

        for (int at = 0; at < indexes.Length;)
        {
            int run = 1;
            while (at + run < indexes.Length && indexes[at + run] == indexes[at])
                run++;

            payload.WriteCounter32(run);
            payload.WriteCounter32(indexes[at]);

            at += run;
        }
    }

    /// <summary>
    /// Orders two entries by their bytes.
    /// </summary>
    /// <remarks>
    /// By the bytes and not by the string, because C#'s ordinal comparison orders UTF-16 code
    /// units: a surrogate pair sorts below U+E000 there and above it in UTF-8. The format
    /// says the order is the bytes', so that is what this compares - and every other
    /// language's writer reaches the same order without being told about UTF-16 at all.
    /// </remarks>
    public static int CompareBytes(byte[] left, byte[] right)
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
