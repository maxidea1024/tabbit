using System;

using Tabbit.Models;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Exporters;

/// <summary>
/// The constants of the Tcb format: what a column descriptor's wire byte means,
/// and how a model field maps onto it.
/// </summary>
/// <remarks>
/// The format is column-oriented and self-describing. The header carries one descriptor per
/// column - tag, wire, element count, byte length - and the data follows as one contiguous
/// block per column. That layout is what makes schema evolution safe to the point of being
/// boring: a reader that does not know a column advances past its block in one call, with
/// no per-type skip logic to get wrong, and a column is identified by its tag rather than
/// its position, so adding, removing, renaming and reordering columns are all invisible to
/// a reader built from a different generation of the model.
///
/// The wire byte packs two facts: the low four bits are the element type, the next two are
/// the kind. Element types are semantic, not just sizes - i32 and f32 are both four bytes,
/// but a reader promoting a value needs to know which interpretation it is widening.
///
/// Every reader carries the same table of constants. This file is the writer's copy and the
/// authoritative one; a change here is a format change and has to be made in the twelve
/// reader runtimes as well, which the conformance corpus and the format golden are there to
/// enforce.
/// </remarks>
public static class TcbFormat
{
    /// <summary>
    /// The format version stamped at the head of every table file.
    ///
    /// One version exists, and a reader that meets any other stops rather than guessing.
    /// There is no compatibility path to an older layout and none is planned: a file this
    /// build cannot read is a file to write again, not one to interpret.
    ///
    /// 102 replaced 101 outright - a descriptor gained its encoding byte - before any
    /// 101 file had shipped. 103 replaced 102 the same way: a column can now say which of
    /// its rows have a value, and a reader that does not check bit 6 of the wire byte would
    /// read the presence bitmap as values. Refusing by version beats reading it wrong.
    ///
    /// 104 replaces 103. Four encodings were added and the flags byte gained a meaning, and
    /// a 103 reader meeting any of them would either refuse the column or, for the flags,
    /// refuse the file - so nothing would be misread either way. The version still moves,
    /// because a file this build cannot read should say so by version rather than by
    /// whichever check happens to catch it first.
    ///
    /// 105 replaces 104. One encoding was added, and with it the first sixty-four bit
    /// variable-length integer the format has carried. The presence bitmap gained an
    /// encoding byte of its own in the same revision, which is a change a 104 reader would
    /// misread rather than refuse - it would take that byte for the first byte of the
    /// bitmap. Nothing shipped under 104, so the version is a record of the change rather
    /// than a compatibility boundary.
    ///
    /// 106 replaces 105. A column can now say which of an array's elements have a value,
    /// and it says so with the last bit the wire byte had left. A 105 reader ignores bit 7,
    /// so it would take the element bitmap for the head of the value block and read the
    /// column wrong rather than refuse it - which is the same reason 103 moved.
    /// spec/nullable-array-elements.md.
    /// </summary>
    public const uint Version = 106;

    // -------------------------------------------------------- file header
    //
    // Forty-two bytes, in the same places whether or not the file is encrypted and
    // whether or not it carries a MAC. The alternative - fields that appear only when
    // they are used - is four header shapes and an offset calculation in each of the
    // the runtimes, for the thirty-seven bytes a plain file spends on zeros.
    // spec/tcb-mac-and-signature.md.

    /// <summary>
    /// The four bytes every table file starts with, encrypted or not.
    /// </summary>
    /// <remarks>
    /// A file format signature, in the place a signature goes: at offset zero, where a
    /// tool that has never heard of this format can find it. It used to be the first four
    /// bytes of the ciphertext, which meant a plain file had no signature at all and an
    /// encrypted one had it eighteen bytes in.
    /// </remarks>
    public static ReadOnlySpan<byte> Magic => "TCB\0"u8;

    public const int MagicOffset = 0;
    public const int VersionOffset = 4;
    public const int FlagsOffset = 8;
    public const int CipherOffset = 9;
    public const int NonceOffset = 10;
    public const int MacOffset = 22;

    /// <summary>
    /// Four known bytes at the head of the ciphertext: the half of the old magic that
    /// answered "is this the right key".
    /// </summary>
    /// <remarks>
    /// The same four bytes as the signature, rather than a second constant, because what
    /// both need is only that the reader knows the value in advance. A file that decrypts
    /// to something else was written with a different key, and saying that is the whole of
    /// what this field is for - it tells a wrong key apart from a damaged file, which no
    /// structural check can do.
    ///
    /// Still needed when a file carries a MAC. The MAC key and the encryption key are
    /// different keys, so a MAC that verifies says the file was not altered, not that the
    /// key about to decrypt it is the one it was written with.
    /// </remarks>
    public const int KeyCheckOffset = 38;

    /// <summary>Where the body begins: row count, column count, descriptors, blocks.</summary>
    public const int HeaderSize = 42;

    /// <summary>The nonce, as RFC 8439 fixes its length. Zero when the file is plain.</summary>
    public const int NonceSize = 12;

    /// <summary>
    /// The tag, truncated from HMAC-SHA-256's thirty-two bytes. All zero means the file
    /// carries none.
    /// </summary>
    public const int MacSize = 16;

    /// <summary>Bit 0 of the flags byte: everything from the key check on is ciphertext.</summary>
    public const byte FlagEncrypted = 0x01;

    /// <summary>The cipher byte of a file that is not encrypted.</summary>
    public const byte CipherNone = 0;

    /// <summary>The only cipher the format defines. Any other value is refused.</summary>
    public const byte CipherChaCha20 = 1;

    /// <summary>
    /// The header of a plain file: the signature, the version, and zeros where the
    /// envelope's fields go.
    /// </summary>
    /// <remarks>
    /// Written by the encoder rather than by the envelope, so that the file is a whole
    /// file before any layer is applied to it. Sealing and authenticating then work over
    /// the finished bytes, filling in fields that are already there rather than building
    /// a second file around the first.
    /// </remarks>
    public static void WriteHeader(TcbWriter writer)
    {
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)0);                  // flags: no encryption, no compression
        writer.Write(CipherNone);
        writer.WriteZeros(NonceSize);
        writer.WriteZeros(MacSize);
        writer.Write(Magic);                    // the key check, in the clear until sealed
    }

    // ------------------------------------------------------- element types

    /// <summary>Zig-zag varint, at most five bytes. Enums travel this way.</summary>
    public const byte ElementVarint = 0;

    public const byte ElementBool = 1;

    /// <summary>Four bytes little endian, interpreted as a signed integer.</summary>
    public const byte ElementI32 = 2;

    /// <summary>Eight bytes little endian: bigint, and datetime/timespan ticks.</summary>
    public const byte ElementI64 = 3;

    /// <summary>Four bytes, an IEEE-754 single's bit pattern.</summary>
    public const byte ElementF32 = 4;

    /// <summary>Eight bytes, an IEEE-754 double's bit pattern.</summary>
    public const byte ElementF64 = 5;

    /// <summary>A counter32 byte length followed by that many UTF-8 bytes.</summary>
    public const byte ElementString = 6;

    /// <summary>Sixteen bytes in .NET's Guid layout.</summary>
    public const byte ElementUuid = 7;

    // --------------------------------------------------------------- kinds

    /// <summary>One value per row.</summary>
    public const byte KindScalar = 0;

    /// <summary>A fixed number of elements per row; the count is in the descriptor.</summary>
    public const byte KindFixedArray = 1;

    /// <summary>Each row carries its own counter32 length ahead of its elements.</summary>
    public const byte KindVarArray = 2;

    // ----------------------------------------------------------- encodings
    //
    // How a column block's values are laid out, chosen per column by measuring every
    // applicable candidate and keeping the smallest (ties go to the lowest number).
    // The spec is spec/tcb-v102-column-encoding.md; the reason is that a static
    // table's columns repeat themselves - the same string thousands of times, ids
    // that step by one - and one byte per column is all it costs to say so.

    /// <summary>The value stream as v101 wrote it. The only encoding for arrays.</summary>
    public const byte EncodingRaw = 0;

    /// <summary>Each value as a counter32. i32 scalars whose values are small.</summary>
    public const byte EncodingVarint = 1;

    /// <summary>First value, then counter32 deltas (32-bit wrapping). i32 scalars.</summary>
    public const byte EncodingDelta = 2;

    /// <summary>(counter32 run length, counter32 value) pairs. i32, varint and bool scalars.</summary>
    public const byte EncodingRle = 3;

    /// <summary>First value, then the delta stream run-length encoded. i32 scalars.</summary>
    public const byte EncodingDeltaRle = 4;

    /// <summary>
    /// A dictionary of the distinct values, then a counter32 index per row.
    /// </summary>
    /// <remarks>
    /// Parameterized by element: an entry is the value in its raw form, so a string
    /// dictionary holds length-prefixed UTF-8 and an f32 dictionary holds four bytes.
    /// That is why the dictionary reaches past strings without costing another encoding
    /// number - and it needs to, because a column of floats in design data is a handful
    /// of values repeated, whatever a single float looks like.
    /// </remarks>
    public const byte EncodingDict = 5;

    /// <summary>The dictionary, then the index stream run-length encoded.</summary>
    public const byte EncodingDictRle = 6;

    /// <summary>
    /// A sorted string dictionary whose entries state only what they do not share with
    /// the entry before, then a counter32 index per row.
    /// </summary>
    /// <remarks>
    /// Because design-data strings are rarely duplicates of each other and very often
    /// neighbours: `02_CRI_DAMAGE_FLOAT` beside `02_CRI_INT`, one skill tier beside the
    /// next. A dictionary still has to hold every one of them, but not every byte of
    /// every one - and on real data that is where most of the remaining bytes were.
    /// </remarks>
    public const byte EncodingDictFront = 7;

    /// <summary>The front-coded dictionary, then the index stream run-length encoded.</summary>
    public const byte EncodingDictFrontRle = 8;

    /// <summary>
    /// An array column, split into the stream of its rows' lengths and the stream of its
    /// elements, each encoded by the rules that already apply to a column of that shape.
    /// </summary>
    /// <remarks>
    /// Not a tenth way of laying out values - a way of saying that the nine already there
    /// apply one level down. The block names an encoding for the elements and, where rows
    /// differ in length, one for the lengths; a reader decodes them with the same two cursors
    /// it already has and hands out `length` elements per row. No new decode step exists
    /// anywhere for this.
    ///
    /// Arrays were left raw when the dataset that settled the encodings held 1.8 percent of
    /// its bytes in them. Another one holds sixty percent, and nothing about the format made
    /// that the case either way - it is a property of the sheets. Composition is what lets
    /// the answer follow the data instead of the other way round.
    /// </remarks>
    public const byte EncodingArray = 9;

    /// <summary>
    /// A float column whose every value is a whole number, carried as integers.
    /// </summary>
    /// <remarks>
    /// A spreadsheet has one kind of number, so a column of counts, tiers and identifiers
    /// arrives as floating point and is written as eight bytes apiece. Stated as the integers
    /// they are, the integer encodings reach them: a column that steps by one becomes a run,
    /// and one that repeats becomes a dictionary of nothing.
    ///
    /// The block names which integer encoding it used, so this composes the same way the
    /// array encoding does rather than duplicating four layouts. The writer only offers it
    /// when every value survives the round trip exactly, which for a single-precision column
    /// is a real restriction and is checked rather than assumed.
    /// </remarks>
    public const byte EncodingWhole = 10;

    /// <summary>
    /// A dictionary whose entries are built out of a shared table of the pieces they are
    /// made of, then a counter32 index per row.
    /// </summary>
    /// <remarks>
    /// Front coding shares what two neighbouring entries have in common at the front, and
    /// that is the whole of what it can share. Values assembled from parts - a path, a name
    /// built from words, an identifier in sections - repeat those parts in the middle and at
    /// the end as well, where front coding has to write them out again on every entry.
    ///
    /// It does not replace front coding; on hierarchical values front coding is still
    /// smaller. Both are offered and measured, which is what the choice has always been.
    /// </remarks>
    public const byte EncodingDictSegment = 11;

    /// <summary>The segment dictionary, then the index stream run-length encoded.</summary>
    public const byte EncodingDictSegmentRle = 12;

    /// <summary>
    /// An integer stream carried at the width its own range needs, over a base.
    /// </summary>
    /// <remarks>
    /// `bool` is the reason. It is the one element whose raw form is eight times its
    /// information content **by definition** rather than by what the data happens to look
    /// like, and until this the only thing that could reach it was run-length encoding -
    /// which costs two to three bytes a run, so it ties with a bit a row at a run length of
    /// about sixteen and loses below that. A column of flags that alternates is eight times
    /// the size it needs to be, and whether it alternates is decided by how the sheet was
    /// sorted, which this tool does not control.
    ///
    /// Not a bool encoding, though. The same block carries an enum column using three of its
    /// values, a flag set using five of its sixty-four bits, and any integer column whose
    /// range is narrower than its width - the base is what makes the last one work, since a
    /// column of levels numbered from five hundred varies over eight bits and occupies
    /// forty-one.
    ///
    /// The packed bytes then go through an integer encoding of their own, and that is where
    /// most of the gain is rather than in the packing: packing turns bit-level structure
    /// into byte-level structure, so a mostly-false column becomes a run of zero bytes.
    /// Measured on two datasets that disagree completely about which inner encoding wins,
    /// which is why it is a choice and not a fixed one. spec/tcb-v105-bit-width-packing.md.
    /// </remarks>
    public const byte EncodingBitpack = 13;

    /// <summary>What an encoding is called in a diagnostic and in the encoding report.</summary>
    /// <remarks>
    /// The names the spec uses, so a report and a spec table can be read against each other
    /// without a translation step.
    /// </remarks>
    public static string EncodingName(byte encoding) => encoding switch
    {
        EncodingRaw => "RAW",
        EncodingVarint => "VARINT",
        EncodingDelta => "DELTA",
        EncodingRle => "RLE",
        EncodingDeltaRle => "DELTA_RLE",
        EncodingDict => "DICT",
        EncodingDictRle => "DICT_RLE",
        EncodingDictFront => "DICT_FRONT",
        EncodingDictFrontRle => "DICT_FRONT_RLE",
        EncodingArray => "ARRAY",
        EncodingWhole => "WHOLE",
        EncodingDictSegment => "DICT_SEG",
        EncodingDictSegmentRle => "DICT_SEG_RLE",
        EncodingBitpack => "BITPACK",
        _ => $"UNKNOWN({encoding})",
    };

    /// <summary>What an element type is called in a diagnostic and in the encoding report.</summary>
    public static string ElementName(byte element) => element switch
    {
        ElementVarint => "varint",
        ElementBool => "bool",
        ElementI32 => "i32",
        ElementI64 => "i64",
        ElementF32 => "f32",
        ElementF64 => "f64",
        ElementString => "string",
        ElementUuid => "uuid",
        _ => $"unknown({element})",
    };

    // ------------------------------------------------------------- the flag

    /// <summary>
    /// Bit 6 of the wire byte: the column carries a presence bit per row ahead of its values.
    /// </summary>
    /// <remarks>
    /// A flag rather than a kind, because nullability is **orthogonal** to shape - a scalar,
    /// a fixed array and a variable array can each be optional. Kind is two bits with one
    /// value left; spending it here would use the last one and still not express the
    /// combinations. The last kind stays free for a genuinely new shape.
    ///
    /// Since v105 the bitmap carries an encoding byte ahead of it and is laid out by the
    /// same choice a bit-packed value block uses. v103 left it raw on the ground that a
    /// bitmap whose presence varies is close to incompressible; measuring it said otherwise
    /// by an order of magnitude, because most optional columns are almost entirely present
    /// or almost entirely absent and a bitmap of those is one run.
    ///
    /// spec/optional-fields.md has the layout and why the value block is left alone;
    /// spec/tcb-v105-bit-width-packing.md has the encoding and the measurement.
    /// </remarks>
    public const byte WireNullable = 0x40;

    /// <summary>
    /// Bit 7: the block carries a second bitmap, one bit per element written.
    /// </summary>
    /// <remarks>
    /// Orthogonal to <see cref="WireNullable"/> and set with it where both are true: an
    /// array may be absent, its elements may be, and `int?[]?` says both. The bitmap sits
    /// between the row bitmap and the values, carries its own encoding byte as the row one
    /// does, and is as long as the elements the block actually wrote - which is
    /// `rowCount x count` for a fixed array and the sum of the row lengths for a variable
    /// one.
    ///
    /// The last bit of the byte, which is why the kind stayed two bits: nullability of a row
    /// and of an element are both orthogonal to the kind, and neither would have fitted in
    /// the one kind value left. spec/nullable-array-elements.md.
    /// </remarks>
    public const byte WireElementNullable = 0x80;

    /// <summary>
    /// The wire byte: element in the low four bits, kind in the next two, nullability of the
    /// value in bit 6 and of its elements in bit 7.
    /// </summary>
    public static byte Wire(
        byte element, byte kind, bool nullable = false, bool elementNullable = false)
        => (byte)(element | (kind << 4)
            | (nullable ? WireNullable : 0)
            | (elementNullable ? WireElementNullable : 0));

    public static byte ElementOf(byte wire) => (byte)(wire & 0x0F);
    public static byte KindOf(byte wire) => (byte)((wire >> 4) & 0x03);
    public static bool NullableOf(byte wire) => (wire & WireNullable) != 0;
    public static bool ElementNullableOf(byte wire) => (wire & WireElementNullable) != 0;

    /// <summary>
    /// Whether a column states which of its rows have a value.
    /// </summary>
    /// <remarks>
    /// Only where the sheet asked for it. A required column has a value in every row by
    /// definition, so a bitmap of all ones would be a bit per row saying nothing.
    /// </remarks>
    public static bool NullableFor(WireColumn column) => column.IsNullable;

    /// <summary>
    /// Whether a column states which of an array's elements have a value.
    /// </summary>
    /// <remarks>
    /// Only where the sheet wrote the marker inside the brackets, for the reason the row
    /// bitmap is only written where the column is optional: an array whose elements are all
    /// there would spend a bit per element saying so.
    /// </remarks>
    public static bool ElementNullableFor(WireColumn column) => column.HasOptionalElements;

    // ------------------------------------------------------------- mapping

    /// <summary>The element type a column's values travel as.</summary>
    public static byte ElementFor(WireColumn column)
    {
        // A reference is stored as the target's primary index, so its element is that key's
        // rather than the record type the column presents. Answered by asking the column
        // instead of returning `ElementI32`, which is what kept a table keyed by anything
        // else from being pointed at. spec/reference-key-types.md.
        switch (column.IsRef ? column.RefKeyType : column.ElementType)
        {
            case ValueType.String: return ElementString;
            case ValueType.Bool: return ElementBool;
            case ValueType.Int32: return ElementI32;
            case ValueType.Int64: return ElementI64;
            case ValueType.Float: return ElementF32;
            case ValueType.Double: return ElementF64;

            // Both are .NET ticks, an i64 on the wire.
            case ValueType.DateTime: return ElementI64;
            case ValueType.TimeSpan: return ElementI64;

            case ValueType.Uuid: return ElementUuid;
            case ValueType.Enum: return ElementVarint;

            default:
                throw new TabbitException(
                    $"The binary exporter cannot map type `{column.Type}` onto a wire element.");
        }
    }

    /// <summary>The kind of a column, mirroring what the generators emit.</summary>
    public static byte KindFor(WireColumn column)
    {
        if (column.IsVariableLengthArray)
            return KindVarArray;

        return column.IsFixedArray ? KindFixedArray : KindScalar;
    }

    /// <summary>
    /// The descriptor's element count: 1 for a scalar, the element count for a fixed
    /// array, and 0 for a variable one, whose rows carry their own.
    /// </summary>
    public static int CountFor(WireColumn column)
    {
        if (column.IsVariableLengthArray)
            return 0;

        return column.Cells.Count;
    }
}
