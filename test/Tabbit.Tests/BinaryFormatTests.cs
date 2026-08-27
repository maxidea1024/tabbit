using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The binary format, pinned byte for byte.
///
/// The golden trees already compare every exported .tcb byte for byte, but they are
/// recorded from the converter's own output: a change to the layout re-records them and
/// the readers are regenerated to match, so the whole gate can move together
/// and still agree with itself. The expectation below is written out here instead, from
/// the specification rather than from the output, and moving it means editing this file
/// on purpose.
///
/// What that protects is not this repository. It is every .tcb file already written by
/// a build that shipped: they are read by the layout this test spells out, and a silent
/// change to it is a silent change to what those files mean.
/// </summary>
[Collection("conformance-tree")]
public class BinaryFormatTests
{
    /// <summary>
    /// The smallest table in the corpus: three scalar columns, one row.
    ///
    /// `layout-edge` is a workbook whose sheets start away from A1, which is beside the
    /// point here - it is used because SecondTable is small enough to be accounted for
    /// one byte at a time.
    /// </summary>
    private const string Scenario = "layout-edge";

    /// <summary>
    /// Every byte of a whole table file, assembled from the specification.
    ///
    /// Written as segments rather than one hex blob so that a mismatch names the part of
    /// the format that moved, and so that reading the test is a way of reading the
    /// format. The row is index 1, label "gamma", amount 30.
    /// </summary>
    [Fact]
    public void A_table_file_is_byte_for_byte_what_the_format_specifies()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var expected = new Segments();

        // ---------------------------------------------------------------- header
        //
        // Forty-two bytes, and the same forty-two whether or not the file is encrypted or
        // signed: the fields those layers write are reserved here as zeros rather than
        // appearing when they are used. What that costs a plain file is the thirty-seven
        // bytes below; what it buys is one header shape for the readers to agree on.
        // spec/wire/tcb-mac-and-signature.md.
        expected.Add("signature", 0x54, 0x43, 0x42, 0x00);   // "TCB\0", at offset zero
        expected.Add("version", 0x6b, 0x00, 0x00, 0x00);     // 107, fixed32
        expected.Add("flags", 0x00);                         // no compression, no encryption
        expected.Add("cipher", 0x00);                        // not encrypted

        expected.Add("nonce",
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);

        // All zero: this file carries no MAC, which is how a file says it is unauthenticated.
        expected.Add("mac",
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);

        // The signature again, under the key when there is one. This is what tells a wrong
        // key apart from a damaged file.
        expected.Add("key check", 0x54, 0x43, 0x42, 0x00);

        expected.Add("row count", 0x02);                     // counter32: zig-zag of 1
        expected.Add("column count", 0x06);                  // counter32: zig-zag of 3

        // ----------------------------------------------------------- descriptors
        //
        // Four fields each: the tag, the wire byte (element in the low nibble, kind in
        // bits 4-5, nullability in bit 6), the encoding byte, and the block's length in
        // bytes. An elements-per-row counter sat between the last two until v107, where
        // the fixed-length array kind went and left it saying only what the kind already
        // said. The length is a plain fixed32 rather than a counter because the writer
        // states it before the block, when a varint's size could not be known yet.
        // The encodings show the writer's measure-and-keep-the-smallest selection at
        // work even on one row: an i32 whose value fits a byte travels as a varint
        // (1 byte beats raw's fixed 4), while a one-row string column stays raw - a
        // dictionary of one entry would cost more than the string it deduplicates.
        expected.Add("index: tag", 0x02);                    // counter32: zig-zag of 1
        expected.Add("index: wire", 0x02);                   // element i32, kind scalar
        expected.Add("index: encoding", 0x01);               // varint
        expected.Add("index: block length", 0x01, 0x00, 0x00, 0x00);

        expected.Add("label: tag", 0x04);                    // zig-zag of 2
        expected.Add("label: wire", 0x06);                   // element string, kind scalar
        expected.Add("label: encoding", 0x00);               // raw
        expected.Add("label: block length", 0x06, 0x00, 0x00, 0x00);

        expected.Add("amount: tag", 0x06);                   // zig-zag of 3
        expected.Add("amount: wire", 0x02);                  // element i32, kind scalar
        expected.Add("amount: encoding", 0x01);              // varint
        expected.Add("amount: block length", 0x01, 0x00, 0x00, 0x00);

        // ---------------------------------------------------------------- blocks
        //
        // One contiguous block per column, in descriptor order. This is the whole of
        // what makes an unknown column skippable in a single advance.
        expected.Add("index block: row 1", 0x02);            // counter32: zig-zag of 1

        expected.Add("label block: row 1 length", 0x0a);     // counter32: zig-zag of 5
        expected.Add("label block: row 1 bytes",
            Encoding.UTF8.GetBytes("gamma"));

        expected.Add("amount block: row 1", 0x3c);           // counter32: zig-zag of 30

        byte[] produced = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir(Scenario), "binary", "SecondTable.tcb"));

        expected.AssertMatches(produced);
    }

    /// <summary>
    /// Which encoding the writer picks for each conformance column, pinned by name.
    ///
    /// The corpus data is shaped so that every encoding of the spec wins somewhere -
    /// that is what makes the conformance harnesses cover every decode path,
    /// not just the ones their data happened to trigger. This test is the other half
    /// of that arrangement: if the writer's selection drifts (a tweak to a candidate,
    /// a change in the data), the coverage does not silently narrow - this fails,
    /// naming the column that moved.
    /// </summary>
    [Fact]
    public void The_conformance_corpus_exercises_every_encoding()
    {
        var conversion = TabbitRunner.Convert("conformance");
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string binaryDir = Path.Combine(RepoLayout.OutputDir("conformance"), "binary");

        // (tag, encoding) per column, in descriptor order. Encodings by number:
        // 0 raw, 1 varint, 2 delta, 3 rle, 4 delta-rle, 5 dict, 6 dict-rle,
        // 7 dict-front, 8 dict-front-rle, 9 array, 10 whole, 11 dict-seg,
        // 12 dict-seg-rle, 13 bitpack.
        var vectors = new byte[]
        {
            4,      // index:     ascending by one     -> delta-rle
            2,      // intVal:    varying small steps  -> delta
            6,      // bigVal:    three values in runs -> dict-rle over i64 entries
            5,      // floatVal:  four values, no runs -> dict over f32 entries
            6,      // doubleVal: two long runs        -> dict-rle over f64 entries
            8,      // text:      shared prefixes, runs -> dict-front-rle
            13,     // flag:      two long runs, but one bit a row is smaller -> bitpack
            6,      // when:      ticks repeat         -> dict-rle over i64 entries
            6,      // span
            0,      // uid:       sixteen-byte entries stay raw by spec
            3,      // label:     two long runs        -> rle
            9, 9, 9,  // ints, strs, labels: an array's lengths and elements, each encoded
            0,      // uids:      a uuid array has no cursor to read through, so it stays raw
            1,      // owner:     small and irregular, and one id far from the rest,
                    //            which is a span no bit width pays for -> varint
            13,     // tier:      the same values without the distant one -> bitpack
            9,      // owners:    an array's lengths and elements, each encoded - and the
                    //            one whose elements are a target's keys rather than values
            10,     // count:     whole numbers        -> the integer encodings, one level down
            11,     // route:     shared pieces, no runs -> dict-seg
            12,     // zone:      the same, in runs    -> dict-seg-rle
        };

        var owners = new byte[]
        {
            4,      // index:     ascending by one     -> delta-rle
            7,      // name:      shared prefixes, no runs -> dict-front
            4,      // rank:      ascending by ten     -> delta-rle
        };

        AssertEncodings(Path.Combine(binaryDir, "Vectors.tcb"), vectors);
        AssertEncodings(Path.Combine(binaryDir, "Owners.tcb"), owners);

        // And that between them they leave nothing untried. The point of shaping the
        // corpus this way is that the harnesses exercise every decode path,
        // which only holds while every encoding is actually reached.
        var reached = new HashSet<byte>(vectors);
        reached.UnionWith(owners);

        for (byte encoding = 0; encoding <= 13; encoding++)
        {
            Assert.True(reached.Contains(encoding),
                $"No conformance column uses encoding {encoding}, so no reader is ever " +
                "run against it.");
        }
    }

    private static void AssertEncodings(string path, byte[] expected)
    {
        var reader = new FormatWalker(File.ReadAllBytes(path));

        reader.Skip(42);                                 // the fixed header, whole
        reader.ReadCounter32();                          // row count
        int columnCount = reader.ReadCounter32();

        Assert.Equal(expected.Length, columnCount);

        for (int at = 0; at < columnCount; at++)
        {
            reader.ReadCounter32();                      // tag
            reader.ReadByte();                           // wire
            byte encoding = reader.ReadByte();
            reader.ReadFixed32();                        // block length

            Assert.True(expected[at] == encoding,
                $"{Path.GetFileName(path)}: column {at} uses encoding {encoding}, " +
                $"expected {expected[at]}.");
        }
    }

    /// <summary>
    /// The invariant every reader checks before it allocates: the blocks are all that
    /// follows the header, so their declared lengths add up to the bytes left, and no
    /// row costs less than one byte in any raw block. An encoded block has no such
    /// floor - one run can cover any number of rows - so the floor applies to raw only.
    ///
    /// Asserted over every table in every scenario's golden tree, because a writer that
    /// gets this wrong writes a file no reader will take - and there is no reason to
    /// discover that one target at a time.
    /// </summary>
    [Fact]
    public void Every_committed_table_declares_lengths_that_add_up()
    {
        var tables = Directory
            .EnumerateFiles(Path.Combine(RepoLayout.Root, "test", "fixtures", "golden"),
                "*.tcb", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(tables);

        var failures = new List<string>();

        foreach (string path in tables)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string relative = Path.GetRelativePath(RepoLayout.Root, path);

            var reader = new FormatWalker(bytes);

            // 'S' 'C' 'B' 0 as a fixed32: every table file starts with it, encrypted or not.
            Assert.Equal(0x00424354u, reader.ReadFixed32());
            Assert.Equal(Tabbit.Exporters.TcbFormat.Version, reader.ReadFixed32());
            Assert.Equal(0, reader.ReadByte());

            // The cipher byte, the nonce and the MAC - zero in a committed golden, which is
            // neither encrypted nor signed - and then the key check.
            reader.Skip(1 + 12 + 16);
            Assert.Equal(0x00424354u, reader.ReadFixed32());

            int rowCount = reader.ReadCounter32();
            int columnCount = reader.ReadCounter32();

            int declared = 0;

            for (int at = 0; at < columnCount; at++)
            {
                reader.ReadCounter32();                      // tag
                byte wire = reader.ReadByte();
                byte encoding = reader.ReadByte();
                int byteLength = (int)reader.ReadFixed32();

                int kind = (wire >> 4) & 0x03;

                if (kind > 1)
                    failures.Add($"{relative}: column {at} declares kind {kind}");

                if (encoding > 13)
                    failures.Add($"{relative}: column {at} declares encoding {encoding}");

                if (encoding == 0 && rowCount > byteLength)
                {
                    failures.Add(
                        $"{relative}: column {at} holds {byteLength} bytes for {rowCount} rows");
                }

                declared += byteLength;
            }

            if (declared != bytes.Length - reader.Position)
            {
                failures.Add($"{relative}: columns declare {declared} bytes but " +
                             $"{bytes.Length - reader.Position} follow the header");
            }
        }

        Assert.True(failures.Count == 0,
            "Committed table files disagree with their own descriptors:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Just enough of a reader to walk a header: the four primitives it is made of.
    /// </summary>
    private sealed class FormatWalker
    {
        private readonly byte[] _bytes;

        public FormatWalker(byte[] bytes) => _bytes = bytes;

        public int Position { get; private set; }

        public byte ReadByte() => _bytes[Position++];

        public void Skip(int count) => Position += count;

        public uint ReadFixed32()
        {
            uint value = (uint)(_bytes[Position]
                | _bytes[Position + 1] << 8
                | _bytes[Position + 2] << 16
                | _bytes[Position + 3] << 24);

            Position += 4;
            return value;
        }

        /// <summary>A zig-zag folded varint, which is how every count travels.</summary>
        public int ReadCounter32()
        {
            uint value = 0;

            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = ReadByte();
                value |= (uint)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                    break;
            }

            return (int)(value >> 1) ^ -(int)(value & 1);
        }
    }

    /// <summary>
    /// A byte sequence built out of named pieces, so a mismatch reports which piece.
    /// </summary>
    private sealed class Segments
    {
        private readonly List<(string Name, byte[] Bytes)> _segments =
            new List<(string, byte[])>();

        public void Add(string name, params byte[] bytes) => _segments.Add((name, bytes));

        public void AssertMatches(byte[] produced)
        {
            int at = 0;

            foreach (var (name, bytes) in _segments)
            {
                Assert.True(at + bytes.Length <= produced.Length,
                    $"The file ends before `{name}`: {produced.Length} bytes in all, " +
                    $"{at + bytes.Length} needed by this point.");

                var slice = produced.Skip(at).Take(bytes.Length).ToArray();

                Assert.True(slice.SequenceEqual(bytes),
                    $"`{name}` at offset {at} is {Hex(slice)}, expected {Hex(bytes)}.");

                at += bytes.Length;
            }

            Assert.True(at == produced.Length,
                $"The file is {produced.Length} bytes and the format accounts for {at}. " +
                $"Trailing bytes: {Hex(produced.Skip(at).ToArray())}.");
        }

        private static string Hex(byte[] bytes)
            => bytes.Length == 0 ? "<nothing>" : string.Join(" ", bytes.Select(b => b.ToString("x2")));
    }
}
