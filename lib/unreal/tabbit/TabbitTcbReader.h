// ---------------------------------------------------------------------------
// Tabbit Tcb reader for Unreal Engine 4.x and 5.x.
//
// Reads the .tcb files produced by Tabbit's binary exporter. The format is
// defined by the C# writer in src/Exporters/TcbWriter.cs, and this is a
// deliberate re-implementation of the reading half of it:
//
//   fixed8      one byte
//   fixed32     four bytes, little endian
//   fixed64     eight bytes, little endian
//   varint32    seven bits per byte, high bit set while more bytes follow,
//               at most five bytes
//   counter32   zig-zag encoded int32 written as a varint32, so small values
//               of either sign cost one byte
//   string      counter32 byte length, then that many UTF-8 bytes
//   int32/uint32   fixed32
//   int64          fixed64
//   bool           fixed8, zero meaning false
//   float/double   fixed32 / fixed64 holding the IEEE-754 bit pattern
//   datetime       fixed64 of .NET ticks: 100 ns units since 0001-01-01
//   timespan       fixed64 of .NET ticks
//   uuid           sixteen bytes in .NET Guid layout
//
// A separate reader from lib/cpp rather than that one wrapped, for two reasons.
//
// The engine already has every type this needs. Going through std::string and a
// Tabbit Uuid struct meant two allocations for every string cell and a text
// parse for every uuid - building what FString and FGuid already are, only to
// convert back.
//
// And an Unreal module is built with exceptions disabled by default, so the
// throwing reader could not report a malformed file: the throw was not a
// recoverable failure but a termination, in a function whose signature promised
// a bool. Failure here is a sticky flag instead.
//
// Nothing from the standard library appears below. Nothing outside Core is
// needed - not even for reading the file, which the generated accessor does with
// FFileHelper.
// ---------------------------------------------------------------------------

#pragma once

#include "CoreMinimal.h"

namespace Tabbit
{
    /**
     * Version stamped at the head of every table file by the exporter. 102 replaced 101
     * outright - a descriptor gained its encoding byte - before any 101 file had shipped.
     * 104 is the current one: four encodings joined the nine, and the flags byte gained a
     * meaning.
     */
    static constexpr uint32 BinaryFileFormatVersion = 107;

    // The wire element types and kinds, as a column descriptor spells them.
    static constexpr uint8 ElementVarint = 0;
    static constexpr uint8 ElementBool = 1;
    static constexpr uint8 ElementI32 = 2;
    static constexpr uint8 ElementI64 = 3;
    static constexpr uint8 ElementF32 = 4;
    static constexpr uint8 ElementF64 = 5;
    static constexpr uint8 ElementString = 6;
    static constexpr uint8 ElementUuid = 7;

    /**
     * One element type as a bit, so the set a member accepts is one argument.
     *
     * A set rather than a container because the generated code has to spell it inline, and
     * an engine module has no business reaching for std::initializer_list to do it.
     */
    constexpr uint32 ElementMask(uint8 Element) { return 1u << Element; }

    static constexpr uint8 KindScalar = 0;
    static constexpr uint8 KindArray = 1;

    // How a block's values are laid out. Raw is the layout 101 had; the others compress
    // a column that repeats itself. spec/wire/tcb-v102-column-encoding.md is the contract.
    static constexpr uint8 EncodingRaw = 0;
    static constexpr uint8 EncodingVarint = 1;
    static constexpr uint8 EncodingDelta = 2;
    static constexpr uint8 EncodingRle = 3;
    static constexpr uint8 EncodingDeltaRle = 4;
    static constexpr uint8 EncodingDict = 5;
    static constexpr uint8 EncodingDictRle = 6;
    static constexpr uint8 EncodingDictFront = 7;
    static constexpr uint8 EncodingDictFrontRle = 8;

    // Composition rather than layout. An array block names an encoding for its elements and
    // one for its rows' lengths, and a whole-number float block names the integer encoding
    // its values travel under - so both are decoded by the cursor that already exists, one
    // level down, and neither adds a decode step anywhere.
    static constexpr uint8 EncodingArray = 9;
    static constexpr uint8 EncodingWhole = 10;

    // A dictionary whose entries are built from a shared table of the pieces they are made
    // of, which reaches what two values share in the middle and at the end where front
    // coding can only reach what they share at the front.
    static constexpr uint8 EncodingDictSegment = 11;
    static constexpr uint8 EncodingDictSegmentRle = 12;

    /** An integer stream at the width its own range needs, over a base. */
    static constexpr uint8 EncodingBitpack = 13;

    // The file header, at fixed offsets whether or not the file is encrypted and whether or
    // not it carries a MAC. spec/wire/tcb-mac-and-signature.md.
    static constexpr int32 MagicOffset = 0;
    static constexpr int32 VersionOffset = 4;
    static constexpr int32 FlagsOffset = 8;
    static constexpr int32 CipherOffset = 9;
    static constexpr int32 NonceOffset = 10;
    static constexpr int32 MacOffset = 22;
    static constexpr int32 KeyCheckOffset = 38;

    /** Where the body begins. The header before it is always this long. */
    static constexpr int32 HeaderSize = 42;

    static constexpr int32 NonceSize = 12;
    static constexpr int32 MacSize = 16;

    /**
     * The signature, as the fixed32 it is on disk: 'T' 'C' 'B' 0, little endian.
     *
     * The same four bytes serve twice. At offset zero they are the file format signature, in
     * the clear whether or not the file is encrypted. At the key check they are under the
     * key, so a file that decrypts to something else was written with a different key -
     * which is the one thing no structural check can tell from damage.
     */
    static constexpr uint32 Magic = 0x00424354u;

    /** Bit 0 of the flags byte: from the key check on, the file is ciphertext. */
    static constexpr uint8 FlagEncrypted = 0x01;

    /** The cipher byte of a file that is not encrypted. */
    static constexpr uint8 CipherNone = 0;

    /** The only cipher the format defines. */
    static constexpr uint8 CipherChaCha20 = 1;

    /** One column as the file describes it. */
    struct FTabbitColumn
    {
        /** What identifies the column, instead of its position. */
        int32 Tag = 0;

        uint8 Element = 0;
        uint8 Kind = 0;

        /**
         * Whether the block begins with one presence bit per row, low bit first.
         *
         * Set only where the sheet marked the column optional. The values are still written
         * for every row - a row without one carries the type's empty value - so the bitmap
         * says which of those to believe and nothing about the layout after it.
         */
        bool bNullable = false;

        /**
         * Whether the block states, per element, which of an array's places hold a value.
         *
         * Independent of bNullable: a column may say either, or both.
         * spec/types/nullable-array-elements.md.
         */
        bool bElementNullable = false;

        /** How the block's values are laid out: one of the Encoding* constants. */
        uint8 Encoding = 0;


        /** Total bytes of the column block - what a skip advances by. */
        int32 ByteLength = 0;
    };

    /** A parsed header: the row count and the column descriptors that follow it. */
    struct FTabbitTableHeader
    {
        int32 RowCount = 0;
        TArray<FTabbitColumn> Columns;
    };

    /**
     * Sequential reader over a table file's bytes.
     *
     * Non-owning: the buffer has to outlive the reader.
     *
     * Failure is sticky. The first read that runs out of data records why and every
     * read after it does nothing, which is what lets the generated code read a
     * record's twenty fields in a row and ask once, at the end, whether any of it
     * worked. Values left behind by a failed read keep whatever they held, so a
     * half-read record holds defaults rather than debris.
     */
    class FTabbitBinaryReader
    {
    public:
        explicit FTabbitBinaryReader(TArrayView<const uint8> InData)
            : Data(InData)
        {
        }

        /** Whether any read so far has run out of data or found the file malformed. */
        bool HasFailed() const { return bFailed; }

        /** Why the first failure happened. Empty while nothing has gone wrong. */
        const FString& GetError() const { return Error; }

        int32 Tell() const { return Position; }
        int32 Remaining() const { return Data.Num() - Position; }

        // ------------------------------------------------------------- primitives

        bool Read(bool& Out)
        {
            uint8 Byte = 0;
            if (!ReadFixed8(Byte))
            {
                return false;
            }

            Out = Byte != 0;
            return true;
        }

        bool Read(int32& Out)
        {
            uint32 Bits = 0;
            if (!ReadFixed32(Bits))
            {
                return false;
            }

            Out = static_cast<int32>(Bits);
            return true;
        }

        bool Read(uint32& Out) { return ReadFixed32(Out); }

        /** A single byte as itself: the wire byte in a column descriptor. */
        bool Read(uint8& Out) { return ReadFixed8(Out); }

        bool Read(int64& Out)
        {
            uint64 Bits = 0;
            if (!ReadFixed64(Bits))
            {
                return false;
            }

            Out = static_cast<int64>(Bits);
            return true;
        }

        bool Read(float& Out)
        {
            uint32 Bits = 0;
            if (!ReadFixed32(Bits))
            {
                return false;
            }

            FMemory::Memcpy(&Out, &Bits, sizeof(Out));
            return true;
        }

        bool Read(double& Out)
        {
            uint64 Bits = 0;
            if (!ReadFixed64(Bits))
            {
                return false;
            }

            FMemory::Memcpy(&Out, &Bits, sizeof(Out));
            return true;
        }

        /**
         * UTF-8 bytes straight into an FString.
         *
         * FUTF8ToTCHAR with an explicit length rather than the UTF8_TO_TCHAR macro:
         * the bytes in a table file are not null terminated, and the macro requires
         * that they are.
         *
         * The cast is to UTF8CHAR rather than ANSICHAR because UE 5.3 made UTF8CHAR a
         * distinct type; before that the two were the same, so this spelling is the one
         * that compiles on 4.x and 5.x alike.
         */
        bool Read(FString& Out)
        {
            int32 Length = 0;
            if (!ReadCounter32(Length))
            {
                return false;
            }

            if (Length < 0)
            {
                return Fail(TEXT("string length is negative"));
            }

            if (!Require(Length))
            {
                return false;
            }

            if (Length == 0)
            {
                Out.Reset();
                Position += Length;
                return true;
            }

            const FUTF8ToTCHAR Converted(
                reinterpret_cast<const UTF8CHAR*>(Data.GetData() + Position), Length);

            Out = FString(Converted.Length(), Converted.Get());

            Position += Length;
            return true;
        }

        /**
         * Ticks into an FDateTime.
         *
         * Both sides count 100 nanosecond ticks from 0001-01-01, so there is nothing to
         * convert - only to check. A tick count outside the range FDateTime accepts
         * would assert inside the engine on some versions, which is exactly the kind of
         * failure this reader exists to turn into a message.
         */
        bool Read(FDateTime& Out)
        {
            int64 Ticks = 0;
            if (!Read(Ticks))
            {
                return false;
            }

            if (Ticks < 0 || Ticks > FDateTime::MaxValue().GetTicks())
            {
                return Fail(FString::Printf(
                    TEXT("datetime tick count %lld is outside what FDateTime can hold"), Ticks));
            }

            Out = FDateTime(Ticks);
            return true;
        }

        /** Ticks into an FTimespan. Signed, and every int64 is a valid one. */
        bool Read(FTimespan& Out)
        {
            int64 Ticks = 0;
            if (!Read(Ticks))
            {
                return false;
            }

            Out = FTimespan(Ticks);
            return true;
        }

        /**
         * Sixteen bytes in .NET's Guid layout, straight into an FGuid.
         *
         * That layout is not plain big-endian: the first three components are little
         * endian and the trailing eight bytes are in order. FGuid's four integers are
         * laid out so that A is the first component, B holds the second and third, and
         * C and D hold the remaining eight bytes big-endian - so the same text comes
         * out of FGuid::ToString as out of .NET's Guid.ToString("D").
         *
         * Assembled rather than parsed. The previous route built a 36 character string
         * and handed it to FGuid::Parse, for sixteen bytes that were already there.
         */
        bool Read(FGuid& Out)
        {
            if (!Require(16))
            {
                return false;
            }

            const uint8* Bytes = Data.GetData() + Position;

            const uint32 A = static_cast<uint32>(Bytes[0])
                           | static_cast<uint32>(Bytes[1]) << 8
                           | static_cast<uint32>(Bytes[2]) << 16
                           | static_cast<uint32>(Bytes[3]) << 24;

            const uint32 Data2 = static_cast<uint32>(Bytes[4]) | static_cast<uint32>(Bytes[5]) << 8;
            const uint32 Data3 = static_cast<uint32>(Bytes[6]) | static_cast<uint32>(Bytes[7]) << 8;

            const uint32 B = Data2 << 16 | Data3;

            const uint32 C = static_cast<uint32>(Bytes[8]) << 24
                           | static_cast<uint32>(Bytes[9]) << 16
                           | static_cast<uint32>(Bytes[10]) << 8
                           | static_cast<uint32>(Bytes[11]);

            const uint32 D = static_cast<uint32>(Bytes[12]) << 24
                           | static_cast<uint32>(Bytes[13]) << 16
                           | static_cast<uint32>(Bytes[14]) << 8
                           | static_cast<uint32>(Bytes[15]);

            Out = FGuid(A, B, C, D);

            Position += 16;
            return true;
        }

        /** An enum, as the zig-zag encoded int32 the exporter writes. */
        template <typename TEnum>
        bool ReadEnum(TEnum& Out)
        {
            int32 Value = 0;
            if (!ReadCounter32(Value))
            {
                return false;
            }

            Out = static_cast<TEnum>(Value);
            return true;
        }

        /**
         * The element count in front of a variable length array.
         *
         * Public because the generated code reads it directly to size the TArray, and
         * because a caller wanting to bound that allocation needs to see the number
         * before trusting it.
         */
        /**
         * An int64 written in as few bytes as its magnitude needed, either sign.
         *
         * The base of a bit-packed block, which is a value of the column's own element
         * type - an i64 column's base does not fit in thirty-two bits. One byte when it
         * is zero, which is what most columns carry.
         */
        bool ReadCounter64(int64& Out)
        {
            uint64 Encoded = 0;
            int32 Shift = 0;

            Out = 0;

            for (;;)
            {
                uint8 Piece = 0;
                if (!Read(Piece))
                {
                    return false;
                }

                Encoded |= static_cast<uint64>(Piece & 0x7Fu) << Shift;

                if ((Piece & 0x80u) == 0)
                {
                    break;
                }

                Shift += 7;

                if (Shift > 63)
                {
                    return FailWith(TEXT("A 64-bit variable length integer runs past ten bytes."));
                }
            }

            Out = static_cast<int64>(Encoded >> 1) ^ -static_cast<int64>(Encoded & 1u);
            return true;
        }

        /**
         * A stream of bytes under one of the integer encodings, which is what a packed
         * block and a presence bitmap both end in.
         *
         * One reader for both, so a bitmap and a packed value block cannot disagree about
         * the same bits. The count is known before the call in both cases, so nothing here
         * reads a length.
         */
        bool ReadByteStream(uint8 Encoding, int32 Count, TArray<uint8>& Out)
        {
            Out.Reset();
            Out.SetNumZeroed(Count);

            if (Encoding == EncodingRaw)
            {
                for (int32 At = 0; At < Count; ++At)
                {
                    if (!Read(Out[At]))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (Encoding > EncodingDeltaRle)
            {
                return FailWith(TEXT("An encoding that cannot carry a packed byte stream."));
            }

            const bool bWalking = Encoding == EncodingDelta || Encoding == EncodingDeltaRle;

            int32 Filled = 0;
            int32 Previous = 0;

            // The first value of a delta stream is written outright; the rest are steps
            // from it. A run in a delta stream repeats the step, not the value, so it walks.
            if (Count > 0 && bWalking)
            {
                if (!ReadCounter32(Previous) || !AsByte(Previous))
                {
                    return false;
                }

                Out[Filled++] = static_cast<uint8>(Previous);
            }

            while (Filled < Count)
            {
                int32 Run = 1;
                int32 Step = 0;
                int32 Value = 0;

                if (Encoding == EncodingVarint)
                {
                    if (!ReadCounter32(Value) || !AsByte(Value))
                    {
                        return false;
                    }
                }
                else if (Encoding == EncodingDelta)
                {
                    if (!ReadCounter32(Step))
                    {
                        return false;
                    }
                }
                else if (Encoding == EncodingRle)
                {
                    if (!ReadCounter32(Run) || !ReadCounter32(Value) || !AsByte(Value))
                    {
                        return false;
                    }
                }
                else // EncodingDeltaRle
                {
                    if (!ReadCounter32(Run) || !ReadCounter32(Step))
                    {
                        return false;
                    }
                }

                if (Run < 1 || Run > Count - Filled)
                {
                    return FailWith(TEXT("A run cannot cover the bytes left in a packed stream."));
                }

                for (int32 At = 0; At < Run; ++At)
                {
                    if (bWalking)
                    {
                        Previous = static_cast<int32>(
                            static_cast<uint32>(Previous) + static_cast<uint32>(Step));

                        if (!AsByte(Previous))
                        {
                            return false;
                        }

                        Out[Filled++] = static_cast<uint8>(Previous);
                    }
                    else
                    {
                        Out[Filled++] = static_cast<uint8>(Value);
                    }
                }
            }

            return true;
        }

        /** A decoded value that has to be a byte, or the block is corrupt. */
        bool AsByte(int32 Value)
        {
            if (Value < 0 || Value > 255)
            {
                return FailWith(TEXT("A packed byte stream decoded a value that is not a byte."));
            }

            return true;
        }

        bool ReadCounter32(int32& Out)
        {
            uint32 Encoded = 0;
            if (!ReadVarint32(Encoded))
            {
                return false;
            }

            Out = static_cast<int32>(Encoded >> 1) ^ -static_cast<int32>(Encoded & 1);
            return true;
        }

        /**
         * Records a reason of the caller's own and fails the reader.
         *
         * For the column checks, which find the file disagreeing with the generated code
         * rather than running out of it. Sticky like every other failure, and always
         * returns false so it reads as `return Reader.FailWith(...)`.
         */
        bool FailWith(const FString& Why) { return Fail(Why); }

        /**
         * Advances past bytes without interpreting them: a whole column block this build
         * has no member for.
         *
         * One call is the entirety of skipping, because a column declares its own length.
         * That is what the column-oriented layout buys - there is no per-type skip to get
         * wrong, and the readers get it right the same way.
         */
        bool Skip(int32 ByteCount)
        {
            if (bFailed)
            {
                return false;
            }

            if (ByteCount < 0 || ByteCount > Remaining())
            {
                return Fail(FString::Printf(TEXT("cannot skip %d bytes with %d remaining"),
                    ByteCount, Remaining()));
            }

            Position += ByteCount;
            return true;
        }

        // Promotions: a member reading a file element narrower than itself. Only the
        // mathematically lossless directions exist; CheckColumn already refused the rest.
        //
        // Everything else forwards to the overload for its own type, so the generated code
        // has one spelling for every field rather than a promoted spelling and a plain one.

        template <typename T>
        bool ReadAs(uint8 Element, T& Out)
        {
            (void)Element;
            return Read(Out);
        }

        /** An int32 member, from i32 or varint. */
        bool ReadAs(uint8 Element, int32& Out)
        {
            return Element == ElementI32 ? Read(Out) : ReadCounter32(Out);
        }

        /** An int64 member, from i64, i32 or varint. */
        bool ReadAs(uint8 Element, int64& Out)
        {
            if (Element == ElementI64)
            {
                return Read(Out);
            }

            int32 Narrower = 0;
            const bool bOk = Element == ElementI32 ? Read(Narrower) : ReadCounter32(Narrower);

            Out = Narrower;
            return bOk;
        }

        /** A double member, from f64, f32 or i32 - all of them exact in a double. */
        bool ReadAs(uint8 Element, double& Out)
        {
            if (Element == ElementF64)
            {
                return Read(Out);
            }

            if (Element == ElementF32)
            {
                float Single = 0.0f;
                const bool bOk = Read(Single);

                Out = Single;
                return bOk;
            }

            int32 Integer = 0;
            const bool bOk = Read(Integer);

            Out = Integer;
            return bOk;
        }

        /** An enum, which travels as a varint and has nothing to promote. */
        template <typename TEnum>
        bool ReadEnumAs(uint8 Element, TEnum& Out)
        {
            (void)Element;
            return ReadEnum(Out);
        }

    private:
        bool Fail(const FString& Why)
        {
            // The first failure is the informative one; everything after it is a
            // consequence of reading past the end.
            if (!bFailed)
            {
                bFailed = true;
                Error = Why;
            }

            return false;
        }

        bool Require(int32 Count)
        {
            if (bFailed)
            {
                return false;
            }

            if (Remaining() < Count)
            {
                return Fail(FString::Printf(
                    TEXT("table data ended after %d of %d bytes while %d more were expected"),
                    Position, Data.Num(), Count));
            }

            return true;
        }

        bool ReadFixed8(uint8& Out)
        {
            if (!Require(1))
            {
                return false;
            }

            Out = Data[Position++];
            return true;
        }

        bool ReadFixed32(uint32& Out)
        {
            if (!Require(4))
            {
                return false;
            }

            Out = static_cast<uint32>(Data[Position + 0])
                | static_cast<uint32>(Data[Position + 1]) << 8
                | static_cast<uint32>(Data[Position + 2]) << 16
                | static_cast<uint32>(Data[Position + 3]) << 24;

            Position += 4;
            return true;
        }

        bool ReadFixed64(uint64& Out)
        {
            if (!Require(8))
            {
                return false;
            }

            uint64 Value = 0;
            for (int32 Index = 0; Index < 8; ++Index)
            {
                Value |= static_cast<uint64>(Data[Position + Index]) << (8 * Index);
            }

            Out = Value;
            Position += 8;
            return true;
        }

        bool ReadVarint32(uint32& Out)
        {
            uint32 Value = 0;

            for (int32 Shift = 0; Shift < 35; Shift += 7)
            {
                uint8 Byte = 0;
                if (!ReadFixed8(Byte))
                {
                    return false;
                }

                Value |= static_cast<uint32>(Byte & 0x7F) << Shift;

                if ((Byte & 0x80) == 0)
                {
                    Out = Value;
                    return true;
                }
            }

            return Fail(TEXT("varint32 is longer than five bytes"));
        }

        TArrayView<const uint8> Data;
        int32 Position = 0;
        bool bFailed = false;
        FString Error;
    };

    /**
     * The ChaCha20 stream cipher of RFC 8439, as the file envelope uses it.
     *
     * Here rather than from a library because an engine module should not gain a dependency
     * to read its own data, and because what the platform offers is an authenticated
     * construction, which changes the length. This format wants a plain keystream: applying
     * it leaves every byte count as it was, so the structural checks - the block lengths that
     * must sum exactly - hold over the ciphertext unchanged.
     */
    namespace ChaCha20
    {
        inline uint32 RotateLeft(uint32 Value, int32 Count)
        {
            return (Value << Count) | (Value >> (32 - Count));
        }

        inline void QuarterRound(uint32* Block, int32 A, int32 B, int32 C, int32 D)
        {
            Block[A] += Block[B]; Block[D] = RotateLeft(Block[D] ^ Block[A], 16);
            Block[C] += Block[D]; Block[B] = RotateLeft(Block[B] ^ Block[C], 12);
            Block[A] += Block[B]; Block[D] = RotateLeft(Block[D] ^ Block[A], 8);
            Block[C] += Block[D]; Block[B] = RotateLeft(Block[B] ^ Block[C], 7);
        }

        /** One 64-byte keystream block: twenty rounds over a copy of the state. */
        inline void Block(const uint32* State, uint32* Working, uint8* Keystream)
        {
            for (int32 At = 0; At < 16; ++At)
            {
                Working[At] = State[At];
            }

            // Ten double rounds. Each is four column quarter-rounds and four diagonal ones,
            // which between them let every word reach every other.
            for (int32 Round = 0; Round < 10; ++Round)
            {
                QuarterRound(Working, 0, 4, 8, 12);
                QuarterRound(Working, 1, 5, 9, 13);
                QuarterRound(Working, 2, 6, 10, 14);
                QuarterRound(Working, 3, 7, 11, 15);

                QuarterRound(Working, 0, 5, 10, 15);
                QuarterRound(Working, 1, 6, 11, 12);
                QuarterRound(Working, 2, 7, 8, 13);
                QuarterRound(Working, 3, 4, 9, 14);
            }

            // Added back to the state it started from, which is what stops the rounds being
            // reversible and so the keystream being recoverable.
            for (int32 At = 0; At < 16; ++At)
            {
                const uint32 Word = Working[At] + State[At];

                Keystream[At * 4 + 0] = static_cast<uint8>(Word);
                Keystream[At * 4 + 1] = static_cast<uint8>(Word >> 8);
                Keystream[At * 4 + 2] = static_cast<uint8>(Word >> 16);
                Keystream[At * 4 + 3] = static_cast<uint8>(Word >> 24);
            }
        }

        inline uint32 WordAt(const uint8* Bytes)
        {
            return static_cast<uint32>(Bytes[0])
                 | static_cast<uint32>(Bytes[1]) << 8
                 | static_cast<uint32>(Bytes[2]) << 16
                 | static_cast<uint32>(Bytes[3]) << 24;
        }

        /**
         * Exclusive-ors the keystream over the bytes, in place.
         *
         * One routine for both directions, which is what a stream cipher is: the keystream
         * depends only on the key, the nonce and the position, so applying it twice returns
         * what went in. The block counter starts at zero.
         */
        inline void Apply(const uint8* Key, const uint8* Nonce, uint8* Data, int32 Count)
        {
            uint32 State[16];
            uint32 Working[16];
            uint8 Keystream[64];

            // "expand 32-byte k", as four little-endian words.
            State[0] = 0x61707865;
            State[1] = 0x3320646e;
            State[2] = 0x79622d32;
            State[3] = 0x6b206574;

            for (int32 At = 0; At < 8; ++At)
            {
                State[4 + At] = WordAt(Key + At * 4);
            }

            State[12] = 0;

            for (int32 At = 0; At < 3; ++At)
            {
                State[13 + At] = WordAt(Nonce + At * 4);
            }

            for (int32 Offset = 0; Offset < Count; Offset += 64)
            {
                Block(State, Working, Keystream);

                const int32 Taken = Count - Offset < 64 ? Count - Offset : 64;

                for (int32 At = 0; At < Taken; ++At)
                {
                    Data[Offset + At] ^= Keystream[At];
                }

                ++State[12];
            }
        }
    }

    /** Four bytes as the fixed32 the signature and the key check are compared as. */
    inline uint32 ReadMagic(const uint8* At)
    {
        return static_cast<uint32>(At[0])
            | static_cast<uint32>(At[1]) << 8
            | static_cast<uint32>(At[2]) << 16
            | static_cast<uint32>(At[3]) << 24;
    }

    /**
     * HMAC-SHA-256 over the file, truncated to the sixteen bytes the header keeps for it.
     *
     * Written out here for the same reason the cipher is: an engine module should not gain a
     * dependency to read its own data.
     *
     * What the tag catches is what the structural checks cannot. A block length that does
     * not add up is a malformed file and the reader says so; four other bytes in an f32
     * column is a well-formed file holding a different number, and no check over a file's
     * shape can tell that from data that was always there.
     */
    namespace Mac
    {
        inline uint32 RotateRight(uint32 Value, int32 Count)
        {
            return (Value >> Count) | (Value << (32 - Count));
        }

        /** One 64-byte block of the compression function. */
        inline void Block(uint32* State, const uint8* Data)
        {
            /** The fractional parts of the cube roots of the first 64 primes. */
            static const uint32 K[64] = {
                0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
                0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
                0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
                0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
                0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
                0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
                0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
                0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
                0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
                0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
                0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};

            uint32 Schedule[64];

            for (int32 At = 0; At < 16; ++At)
            {
                Schedule[At] = static_cast<uint32>(Data[At * 4]) << 24
                    | static_cast<uint32>(Data[At * 4 + 1]) << 16
                    | static_cast<uint32>(Data[At * 4 + 2]) << 8
                    | static_cast<uint32>(Data[At * 4 + 3]);
            }

            for (int32 At = 16; At < 64; ++At)
            {
                const uint32 Before = Schedule[At - 15];
                const uint32 Near = Schedule[At - 2];

                const uint32 S0 = RotateRight(Before, 7) ^ RotateRight(Before, 18) ^ (Before >> 3);
                const uint32 S1 = RotateRight(Near, 17) ^ RotateRight(Near, 19) ^ (Near >> 10);

                Schedule[At] = Schedule[At - 16] + S0 + Schedule[At - 7] + S1;
            }

            uint32 A = State[0], B = State[1], C = State[2], D = State[3];
            uint32 E = State[4], F = State[5], G = State[6], H = State[7];

            for (int32 At = 0; At < 64; ++At)
            {
                const uint32 S1 = RotateRight(E, 6) ^ RotateRight(E, 11) ^ RotateRight(E, 25);
                const uint32 Choice = (E & F) ^ (~E & G);
                const uint32 One = H + S1 + Choice + K[At] + Schedule[At];

                const uint32 S0 = RotateRight(A, 2) ^ RotateRight(A, 13) ^ RotateRight(A, 22);
                const uint32 Majority = (A & B) ^ (A & C) ^ (B & C);
                const uint32 Two = S0 + Majority;

                H = G;
                G = F;
                F = E;
                E = D + One;
                D = C;
                C = B;
                B = A;
                A = One + Two;
            }

            State[0] += A;
            State[1] += B;
            State[2] += C;
            State[3] += D;
            State[4] += E;
            State[5] += F;
            State[6] += G;
            State[7] += H;
        }

        /** One piece of a message: hashing takes several, and joining them would copy it. */
        struct FPiece
        {
            const uint8* Data;
            int32 Length;
        };

        /** SHA-256 of the pieces, hashed as though they were one message. */
        inline void Sha256(const FPiece* Pieces, int32 Count, uint8* Digest)
        {
            uint32 State[8] = {0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
                               0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19};

            uint8 Partial[64];
            int32 Filled = 0;
            uint64 Length = 0;

            for (int32 Piece = 0; Piece < Count; ++Piece)
            {
                const uint8* Data = Pieces[Piece].Data;
                const int32 Size = Pieces[Piece].Length;

                Length += static_cast<uint64>(Size);

                int32 At = 0;

                // The partial block first, then whole blocks straight out of the piece: the
                // copy into Partial is only for the bytes that straddle a boundary.
                while (At < Size)
                {
                    if (Filled == 0 && Size - At >= 64)
                    {
                        Block(State, Data + At);
                        At += 64;
                        continue;
                    }

                    const int32 Taking = 64 - Filled < Size - At ? 64 - Filled : Size - At;
                    FMemory::Memcpy(Partial + Filled, Data + At, Taking);

                    Filled += Taking;
                    At += Taking;

                    if (Filled == 64)
                    {
                        Block(State, Partial);
                        Filled = 0;
                    }
                }
            }

            // The padding: a set bit, zeros, and the message length in bits as a 64-bit
            // big-endian number. Two blocks when the length does not fit in the open one.
            uint8 Tail[128] = {0};
            const int32 TailLength = Filled + 9 > 64 ? 128 : 64;

            FMemory::Memcpy(Tail, Partial, Filled);
            Tail[Filled] = 0x80;

            const uint64 Bits = Length * 8;

            for (int32 At = 0; At < 8; ++At)
            {
                Tail[TailLength - 1 - At] = static_cast<uint8>(Bits >> (At * 8));
            }

            for (int32 At = 0; At < TailLength; At += 64)
            {
                Block(State, Tail + At);
            }

            for (int32 At = 0; At < 8; ++At)
            {
                Digest[At * 4] = static_cast<uint8>(State[At] >> 24);
                Digest[At * 4 + 1] = static_cast<uint8>(State[At] >> 16);
                Digest[At * 4 + 2] = static_cast<uint8>(State[At] >> 8);
                Digest[At * 4 + 3] = static_cast<uint8>(State[At]);
            }
        }

        /**
         * The tag for a file: HMAC-SHA-256 over every byte but the sixteen the tag lives in.
         *
         * Skipping them is the same as zeroing them and cheaper by a copy of the file.
         */
        inline void Tag(const uint8* Key, int32 KeyLength, const uint8* Data, int32 Length,
            uint8* Out)
        {
            uint8 BlockKey[64] = {0};

            // A key longer than the block is hashed first; ours is thirty-two bytes, but the
            // rule is part of HMAC and leaving it out would make this agree with nothing.
            if (KeyLength > 64)
            {
                const FPiece Whole[1] = {{Key, KeyLength}};
                Sha256(Whole, 1, BlockKey);
            }
            else
            {
                FMemory::Memcpy(BlockKey, Key, KeyLength);
            }

            uint8 Inner[64];
            uint8 Outer[64];

            for (int32 At = 0; At < 64; ++At)
            {
                Inner[At] = static_cast<uint8>(BlockKey[At] ^ 0x36);
                Outer[At] = static_cast<uint8>(BlockKey[At] ^ 0x5c);
            }

            uint8 InnerDigest[32];

            const FPiece Message[3] = {{Inner, 64},
                                       {Data, MacOffset},
                                       {Data + KeyCheckOffset, Length - KeyCheckOffset}};

            Sha256(Message, 3, InnerDigest);

            uint8 Full[32];
            const FPiece OuterMessage[2] = {{Outer, 64}, {InnerDigest, 32}};

            Sha256(OuterMessage, 2, Full);
            FMemory::Memcpy(Out, Full, MacSize);
        }
    }

    /**
     * A file's plaintext bytes, checked against its MAC on the way.
     *
     * Call this on the bytes before handing them to a reader. A file that is neither
     * encrypted nor authenticated comes back untouched, so the call belongs in the load path
     * whether or not the project uses either.
     *
     * The order is verify, then decrypt. The tag covers the file as it is stored, so an
     * altered file is refused before the key is used on it, and the header - the flags, the
     * cipher byte, the nonce - is covered along with the body.
     *
     * Decryption happens in place, and what comes back is a view onto the same array rather
     * than a copy of it. The fields it consumes are returned to what a plain file has in
     * them, so calling it twice on the same array is the same as calling it once.
     *
     * What the two layers are and are not for: both keys ship inside the client that reads
     * the file. Encryption stops a data file being read in an editor; the MAC stops an
     * edited one loading. Neither stops anyone who can take the keys out of the client, and
     * no format does.
     *
     * MacKey is empty when the project does not sign its files. A reader that has one
     * refuses a file that carries no MAC: the field being zero is how a file says it is
     * unauthenticated, so accepting that from a project that signs its files would put the
     * check sixteen zero bytes away from being removed.
     *
     * Failure is a reason rather than a throw, like everything else here - the module is
     * built with exceptions disabled.
     */
    inline bool Open(TArray<uint8>& Data, const TArray<uint8>& Key,
        TArrayView<const uint8>& OutBytes, FString& OutError,
        const TArray<uint8>& MacKey = TArray<uint8>(), bool bVerifyMac = true)
    {
        OutBytes = TArrayView<const uint8>();

        if (Data.Num() < HeaderSize)
        {
            OutError = TEXT("the file is too short to be a table");
            return false;
        }

        if (ReadMagic(Data.GetData() + MagicOffset) != Magic)
        {
            OutError = TEXT("the file does not begin with the table file signature");
            return false;
        }

        // Nothing to check with when the key is empty, and a file that carries a tag is read
        // anyway rather than refused: a client built before the project turned MACs on is
        // one this format has promised can still read what it is sent.
        if (bVerifyMac && MacKey.Num() > 0)
        {
            if (MacKey.Num() != 32)
            {
                OutError = TEXT("the MAC key given is not 32 bytes");
                return false;
            }

            bool bPresent = false;

            for (int32 At = 0; At < MacSize && !bPresent; ++At)
            {
                bPresent = Data[MacOffset + At] != 0;
            }

            if (!bPresent)
            {
                OutError = TEXT("the file carries no MAC and this build expects one - it was ")
                    TEXT("exported without a MAC key, or the field was cleared after it was written");

                return false;
            }

            uint8 Expected[MacSize];
            Mac::Tag(MacKey.GetData(), MacKey.Num(), Data.GetData(), Data.Num(), Expected);

            // Every byte, always: a comparison that returns early tells the caller how far
            // it got.
            uint8 Difference = 0;

            for (int32 At = 0; At < MacSize; ++At)
            {
                Difference |= static_cast<uint8>(Expected[At] ^ Data[MacOffset + At]);
            }

            if (Difference != 0)
            {
                OutError = TEXT("the file does not match its MAC - it was altered after it ")
                    TEXT("was exported, or it was signed with a different key");

                return false;
            }
        }

        if ((Data[FlagsOffset] & FlagEncrypted) == 0)
        {
            OutBytes = TArrayView<const uint8>(Data.GetData(), Data.Num());
            return true;
        }

        if (Data[CipherOffset] != CipherChaCha20)
        {
            OutError = FString::Printf(
                TEXT("the file uses cipher %d, which this reader does not know"),
                Data[CipherOffset]);

            return false;
        }

        if (Key.Num() != 32)
        {
            OutError = TEXT("the file is encrypted and no key, or a key that is not 32 bytes, was given");
            return false;
        }

        ChaCha20::Apply(Key.GetData(), Data.GetData() + NonceOffset,
            Data.GetData() + KeyCheckOffset, Data.Num() - KeyCheckOffset);

        if (ReadMagic(Data.GetData() + KeyCheckOffset) != Magic)
        {
            OutError = TEXT("the file did not decrypt to a table - the key is not the one it was written with");
            return false;
        }

        // Back to what a plain file holds in these bytes, so that a second call over the
        // same array passes it through instead of decrypting it again.
        // The complement as an exclusive-or against 0xFF rather than as ~: the operand
        // promotes to int, so ~ of it is a value that does not fit the byte it goes into.
        Data[FlagsOffset] &= static_cast<uint8>(0xFFu ^ FlagEncrypted);
        Data[CipherOffset] = CipherNone;

        for (int32 At = 0; At < NonceSize; ++At)
        {
            Data[NonceOffset + At] = 0;
        }

        OutBytes = TArrayView<const uint8>(Data.GetData(), Data.Num());
        return true;
    }

    /**
     * Reads and checks the file header, handing back the row count and the column
     * descriptors that follow it.
     *
     * The flags byte is zero by the time a header is read: bit 0 says the body is
     * ciphertext, and Open clears it as it decrypts. Any bit still set here is a layer this
     * build does not have - or a caller who skipped Open.
     *
     * The descriptors are checked against the file's own size before anybody allocates
     * for the row count: the blocks are all that follows the header, so their declared
     * lengths have to add up to the bytes left, and every row costs at least one byte in
     * every block. A row count larger than that is one the exporter could not have
     * written, and finding that out here rather than during the read is what keeps a
     * corrupt count from becoming an allocation of two billion rows.
     */
    inline bool ReadTableHeader(FTabbitBinaryReader& Reader, FTabbitTableHeader& OutHeader)
    {
        OutHeader.RowCount = 0;
        OutHeader.Columns.Empty();

        // Checked again here rather than only in Open, because a reader can be handed bytes
        // that never went through it.
        uint32 Signature = 0;
        if (!Reader.Read(Signature))
        {
            return false;
        }

        if (Signature != Magic)
        {
            return Reader.FailWith(
                TEXT("the file does not begin with the table file signature"));
        }

        uint32 Version = 0;
        if (!Reader.Read(Version))
        {
            return false;
        }

        if (Version != BinaryFileFormatVersion)
        {
            return Reader.FailWith(FString::Printf(
                TEXT("table format version %u is not supported (expected %u)"),
                Version, BinaryFileFormatVersion));
        }

        // As a byte and not as a bool: the bits mean different things, and a bool answers
        // only whether any of them is set. Bit 0 says the body is still ciphertext, which is
        // a file somebody forgot to Open rather than a file this build is too old for - and
        // saying so is the difference between a five minute fix and a bug report.
        uint8 Flags = 0;
        if (!Reader.Read(Flags))
        {
            return false;
        }

        if ((Flags & FlagEncrypted) != 0)
        {
            return Reader.FailWith(
                TEXT("the table is encrypted and these are still its ciphertext bytes - pass ")
                TEXT("them through Tabbit::Open with the key before reading them"));
        }

        if (Flags != 0)
        {
            return Reader.FailWith(TEXT("table declares unsupported features"));
        }

        // The cipher byte, the nonce, the MAC and the key check. Open has dealt with all
        // four by now; what is left is to be standing at the body.
        if (!Reader.Skip(HeaderSize - CipherOffset))
        {
            return false;
        }

        if (!Reader.ReadCounter32(OutHeader.RowCount))
        {
            return false;
        }

        if (OutHeader.RowCount < 0)
        {
            const int32 Bad = OutHeader.RowCount;

            OutHeader.RowCount = 0;
            return Reader.FailWith(
                FString::Printf(TEXT("table row count %d is negative"), Bad));
        }

        int32 ColumnCount = 0;
        if (!Reader.ReadCounter32(ColumnCount))
        {
            return false;
        }

        if (ColumnCount < 0)
        {
            return Reader.FailWith(
                FString::Printf(TEXT("table column count %d is negative"), ColumnCount));
        }

        // Bounded, because the count came out of the file: no file describes more columns
        // than it has bytes left, and a descriptor is several bytes each.
        OutHeader.Columns.Empty(ColumnCount < Reader.Remaining() ? ColumnCount : Reader.Remaining());

        for (int32 At = 0; At < ColumnCount && !Reader.HasFailed(); ++At)
        {
            FTabbitColumn Column;

            Reader.ReadCounter32(Column.Tag);

            uint8 Wire = 0;
            Reader.Read(Wire);
            Column.Element = static_cast<uint8>(Wire & 0x0F);
            Column.bNullable = (Wire & 0x40) != 0;
            Column.bElementNullable = (Wire & 0x80) != 0;
            Column.Kind = static_cast<uint8>((Wire >> 4) & 0x03);

            Reader.Read(Column.Encoding);


            uint32 ByteLength = 0;
            Reader.Read(ByteLength);
            Column.ByteLength = static_cast<int32>(ByteLength);

            OutHeader.Columns.Add(Column);
        }

        if (Reader.HasFailed())
        {
            return false;
        }

        const int32 Remaining = Reader.Remaining();
        int32 Declared = 0;

        for (const FTabbitColumn& Column : OutHeader.Columns)
        {
            if (Column.ByteLength < 0 || Column.ByteLength > Remaining - Declared)
            {
                return Reader.FailWith(FString::Printf(
                    TEXT("column tag %d declares %d bytes, which the file cannot hold"),
                    Column.Tag, Column.ByteLength));
            }

            Declared += Column.ByteLength;

            if (Column.Encoding == EncodingRaw && OutHeader.RowCount > Column.ByteLength)
            {
                const int32 Bad = OutHeader.RowCount;

                OutHeader.RowCount = 0;
                return Reader.FailWith(FString::Printf(
                    TEXT("the row count %d is larger than column tag %d can hold in its %d bytes"),
                    Bad, Column.Tag, Column.ByteLength));
            }
        }

        if (Declared != Remaining)
        {
            return Reader.FailWith(FString::Printf(
                TEXT("the columns declare %d bytes but %d follow the header"),
                Declared, Remaining));
        }

        return true;
    }

    /**
     * The (element, encoding) pairs the spec defines. Integers take the integer encodings,
     * strings the dictionary ones, and an array takes the composition that applies all of
     * those to its elements.
     */
    inline bool EncodingSupported(const FTabbitColumn& Column)
    {
        if (Column.Encoding == EncodingRaw)
        {
            return true;
        }

        // An array's block says what its elements use, and that inner encoding is checked as
        // it is read rather than here - the descriptor carries only the outer one, so this is
        // as far as the descriptor can be checked.
        if (Column.Kind != KindScalar)
        {
            return Column.Encoding == EncodingArray;
        }

        switch (Column.Element)
        {
        case ElementBool:
        case ElementVarint:
            return Column.Encoding == EncodingRle || Column.Encoding == EncodingBitpack;

        case ElementI32:
            return (Column.Encoding >= EncodingVarint && Column.Encoding <= EncodingDeltaRle)
                || Column.Encoding == EncodingBitpack;

        // The dictionary is parameterized by element, so this one reaches it with entries
        // that are simply its own raw bytes.
        case ElementI64:
            return Column.Encoding == EncodingDict || Column.Encoding == EncodingDictRle
                || Column.Encoding == EncodingBitpack;

        // A float column additionally reaches the integer encodings, through the block that
        // says its values are whole numbers.
        case ElementF32:
        case ElementF64:
            return Column.Encoding == EncodingDict || Column.Encoding == EncodingDictRle
                || Column.Encoding == EncodingWhole;

        // And a string dictionary can be front coded or built from segments, both of which
        // are meaningless for a fixed-width element and refused for one.
        case ElementString:
            return (Column.Encoding >= EncodingDict && Column.Encoding <= EncodingDictFrontRle)
                || Column.Encoding == EncodingDictSegment
                || Column.Encoding == EncodingDictSegmentRle;

        default:
            return false;
        }
    }

    /**
     * That a column is what the generated member expects, or a lossless promotion of it.
     *
     * Refusal names the field and both types, because a column whose type changed
     * incompatibly is a schema mistake to fix, not bytes to reinterpret.
     */
    /**
     * Reads a nullable column's presence bitmap, or leaves it empty for a column with none.
     *
     * Called by the generated code before the row loop: the bitmap sits at the front of the
     * block and the values follow it. One bit per row, low bit first, padded to a byte.
     */
    inline void ReadPresence(FTabbitBinaryReader& Reader, const FTabbitColumn& Column,
        int32 RowCount, TArray<uint8>& OutPresence)
    {
        OutPresence.Reset();

        if (!Column.bNullable)
        {
            return;
        }

        // The bitmap is a bit-packed boolean column of width one, so it carries an
        // encoding byte and is laid out by the same choice a packed value block uses. Its
        // width and base are known in advance, which is why it does not carry them.
        uint8 Encoding = 0;
        if (!Reader.Read(Encoding))
        {
            return;
        }

        Reader.ReadByteStream(Encoding, (RowCount + 7) / 8, OutPresence);
    }

    /**
     * A column's element bitmap, behind the row bitmap and in front of the values.
     *
     * Empty for a column that does not carry one. Its length is written ahead of it as a
     * counter32, because a variable-length column's total is the sum of its row lengths and
     * those live inside the value block - a reader meeting the bitmap first would have
     * nothing to size it by. spec/types/nullable-array-elements.md.
     */
    inline void ReadElementPresence(FTabbitBinaryReader& Reader, const FTabbitColumn& Column,
        TArray<uint8>& OutPresence)
    {
        OutPresence.Reset();

        if (!Column.bElementNullable)
        {
            return;
        }

        int32 Elements = 0;
        if (!Reader.ReadCounter32(Elements))
        {
            return;
        }

        uint8 Encoding = 0;
        if (!Reader.Read(Encoding))
        {
            return;
        }

        Reader.ReadByteStream(Encoding, (Elements + 7) / 8, OutPresence);
    }

    /**
     * Whether a row has a value, for a column that says which do.
     *
     * An empty bitmap means the column is not optional and every row has one, so the
     * generated code can call this unconditionally.
     */
    inline bool IsPresent(const TArray<uint8>& Presence, int32 Row)
    {
        return Presence.Num() == 0 || (Presence[Row >> 3] & (1u << (Row & 7))) != 0;
    }

    inline bool CheckColumn(FTabbitBinaryReader& Reader, const FTabbitColumn& Column,
        const TCHAR* FieldName, uint8 Kind, bool bNullable, uint32 Accepted,
        bool bElementNullable = false)
    {
        if (Reader.HasFailed())
        {
            return false;
        }

        // The same statement about the other bitmap: generated code not expecting one would
        // read it as values. spec/types/nullable-array-elements.md.
        if (Column.bElementNullable != bElementNullable)
        {
            return Reader.FailWith(FString::Printf(
                TEXT("%s: the file and the generated member disagree about whether this column's")
                TEXT(" elements are optional. The schema changed; regenerate the code or rebuild")
                TEXT(" the data."),
                FieldName));
        }

        // Nullability is part of the shape: a file that says optional puts a presence bitmap
        // at the front of the block, and generated code not expecting one would read the
        // bitmap as values. Adding or removing a `?` is a schema change like any other.
        if (Column.bNullable != bNullable)
        {
            return Reader.FailWith(FString::Printf(
                TEXT("%s: the file and the generated member disagree about whether this column is optional")
                TEXT(" (file: %d, member: %d). The schema changed; regenerate the code or rebuild the data."),
                FieldName, Column.bNullable ? 1 : 0, bNullable ? 1 : 0));
        }

        // A negative count says the member claims no length: how many elements a row holds
        // is what the file states, row by row, so a group that grew a column is read
        // rather than refused. spec/wire/tcb-v107-dynamic-arrays.md.
        if (Column.Kind != Kind)
        {
            return Reader.FailWith(FString::Printf(
                TEXT("%s: the file column (kind %d) does not match the generated ")
                TEXT("member (kind %d). The schema changed shape; regenerate the ")
                TEXT("code or rebuild the data."),
                FieldName, Column.Kind, Kind));
        }

        // An encoding this build cannot decode - or one the spec does not define for
        // this element - is refused by name, exactly like an element it cannot read.
        // An unknown column's encoding never gets here - a skip is a skip whatever
        // the block's layout.
        if (!EncodingSupported(Column))
        {
            return Reader.FailWith(FString::Printf(
                TEXT("%s: the file's column uses encoding %d, which this reader cannot ")
                TEXT("decode for its element type. Regenerate the code or rebuild the data."),
                FieldName, Column.Encoding));
        }

        if ((Accepted & ElementMask(Column.Element)) != 0)
        {
            return true;
        }

        return Reader.FailWith(FString::Printf(
            TEXT("%s: the file carries element type %d, which this member cannot read. The ")
            TEXT("column changed type incompatibly; regenerate the code or rebuild the data."),
            FieldName, Column.Element));
    }

    /**
     * That a block was consumed exactly.
     *
     * A mismatch means the file and this code disagree about the encoding, and stopping
     * here names the column instead of reading the next one out of the wrong bytes.
     */
    inline bool CheckBlockEnd(FTabbitBinaryReader& Reader, const FTabbitColumn& Column,
        int32 ExpectedEnd)
    {
        if (Reader.HasFailed())
        {
            return false;
        }

        if (Reader.Tell() != ExpectedEnd)
        {
            return Reader.FailWith(FString::Printf(
                TEXT("column tag %d: its block declared %d bytes but the read ended %d bytes ")
                TEXT("short of its boundary"),
                Column.Tag, Column.ByteLength, ExpectedEnd - Reader.Tell()));
        }

        return true;
    }

    /**
     * How much to reserve up front for a row count that came off the wire.
     *
     * A corrupt count of two billion would otherwise be an immediate allocation of
     * that many rows, which fails long before the reader gets to notice the file is
     * short. The array grows past this if the rows really are there.
     */
    inline int32 ReserveBound(int32 Count)
    {
        constexpr int32 MaxUpFront = 65536;

        return FMath::Clamp(Count, 0, MaxUpFront);
    }

    /**
     * Reads one column's values in order, whatever the block's encoding.
     *
     * The generated row loop stays a row loop; this is the one place that knows how
     * a delta accumulates, how long a run has left, or that a dictionary index is a
     * reference into strings decoded once. That last one matters beyond file size: a
     * hundred-thousand-row column with three distinct strings decodes three strings,
     * not a hundred thousand.
     *
     * An array column comes through here too. Its block names an encoding for its elements
     * and, where its rows differ in length, one for the lengths, and both are encodings that
     * already exist - so an array's elements are read exactly the way a scalar column's are,
     * one level down, and the row's length comes from NextLength first.
     *
     * One instance serves a whole table read. The generated switch's cases share a
     * scope - and C++ does not allow a jump past a live constructor - so each
     * encodable column calls Open on the same cursor rather than declaring its own,
     * and Open resets every piece of state.
     *
     * CheckColumn has already refused any (element, encoding) pair the spec does not
     * define, so the switches here do not re-litigate that. Failure is the reader's
     * sticky flag, like every other read: once it is set, every call below does
     * nothing and returns false.
     */
    class FTabbitColumnCursor
    {
    public:
        FTabbitColumnCursor() = default;

        /** Binds the cursor to a column block, decoding the dictionary where it has one. */
        void Open(FTabbitBinaryReader& InReader, const FTabbitColumn& Column,
            int32 RowCount, const TCHAR* InFieldName)
        {
            Reader = &InReader;
            FieldName = InFieldName;
            Element = Column.Element;
            Encoding = Column.Encoding;
            RowsRemaining = RowCount;
            RunRemaining = 0;
            RunValue = 0;
            Previous = 0;
            bStarted = false;
            Dictionary.Empty();
            ValueDictionary.Empty();
            ValueWidth = 0;
            Lengths.Reset();
            LengthAt = 0;
            bHasLengths = false;
            bWholeNumbers = false;
            Packed.Reset();
            PackedWidth = 0;
            PackedBase = 0;
            PackedBit = 0;

            // An array column's block names an encoding for its elements and, where its rows
            // differ in length, one for the lengths. Both are encodings that already exist,
            // so all this does is read them and then go on being the element stream's cursor.
            if (Encoding == EncodingArray)
            {
                uint8 ElementEncoding = 0;
                if (!Reader->Read(ElementEncoding))
                {
                    return;
                }

                Encoding = ElementEncoding;

                if (!OpenElementStream(Column, RowCount))
                {
                    return;
                }
            }

            // A float column whose values are all whole numbers carries them as integers and
            // says which integer encoding they travel under. From here down it is that
            // encoding's cursor, and only the handing out converts back.
            // A bit-packed column states the width its range needs, the base subtracted
            // from every value, and which encoding carries the packed bytes. Decoded here
            // so that handing values out is a shift and an add.
            if (Encoding == EncodingBitpack)
            {
                uint8 Width = 0;
                int64 Base = 0;
                uint8 Inner = 0;

                if (!Reader->Read(Width) || !Reader->ReadCounter64(Base)
                    || !Reader->Read(Inner))
                {
                    return;
                }

                if (Width < 1 || Width > 64)
                {
                    Reader->FailWith(TEXT("A bit width is not between 1 and 64."));
                    return;
                }

                PackedWidth = static_cast<int32>(Width);
                PackedBase = Base;

                const int64 Bits = static_cast<int64>(RowsRemaining) * Width;

                if (!Reader->ReadByteStream(Inner, static_cast<int32>((Bits + 7) / 8), Packed))
                {
                    return;
                }

                // No early exit: the dictionary sections below test for their own encodings
                // and a packed block matches none of them, so falling through leaves them
                // empty.
            }

            if (Encoding == EncodingWhole)
            {
                uint8 Inner = 0;
                if (!Reader->Read(Inner))
                {
                    return;
                }

                if (Inner < EncodingVarint || Inner > EncodingDeltaRle)
                {
                    Reader->FailWith(FString::Printf(
                        TEXT("%s: encoding %d cannot carry a whole-number column's values"),
                        FieldName, Inner));

                    return;
                }

                Encoding = Inner;
                bWholeNumbers = true;
            }

            // A segment dictionary is built once, here, and from then on the block is a
            // dictionary with an index stream like any other - so the row-by-row paths below
            // need to know nothing about it.
            if (Encoding == EncodingDictSegment || Encoding == EncodingDictSegmentRle)
            {
                ReadSegmentDictionary();

                Encoding = Encoding == EncodingDictSegment ? EncodingDict : EncodingDictRle;
                return;
            }

            const bool bPlainDictionary =
                Encoding == EncodingDict || Encoding == EncodingDictRle;

            const bool bFrontDictionary =
                Encoding == EncodingDictFront || Encoding == EncodingDictFrontRle;

            if (!bPlainDictionary && !bFrontDictionary)
            {
                return;
            }

            // The block's dictionary, decoded once here and handed out per row.
            int32 Count = 0;
            if (!Reader->ReadCounter32(Count))
            {
                return;
            }

            if (Count < 0)
            {
                Reader->FailWith(FString::Printf(
                    TEXT("%s: the dictionary entry count is negative"), FieldName));

                return;
            }

            if (bFrontDictionary)
            {
                ReadFrontCodedDictionary(Count);
                return;
            }

            if (Element == ElementString)
            {
                // Bounded, because the count came out of the file. The array grows past
                // this if the entries really are there.
                Dictionary.Empty(ReserveBound(Count));

                while (Dictionary.Num() < Count && !Reader->HasFailed())
                {
                    Reader->Read(Dictionary.AddDefaulted_GetRef());
                }

                return;
            }

            // A fixed-width element: an entry is the value's own bytes, so they are kept
            // as bytes and turned into a value only when a row asks for one. That is what
            // guarantees the value comes out bit for bit as the raw layout would have
            // read it, rather than through a conversion that happens to round-trip.
            ValueWidth = Element == ElementF32 ? 4 : 8;
            ValueDictionary.Empty(ReserveBound(Count) * ValueWidth);

            // Entry by entry rather than byte by byte, so a block that runs out mid-entry
            // leaves whole entries behind rather than a trailing partial one.
            for (int32 Entry = 0; Entry < Count && !Reader->HasFailed(); ++Entry)
            {
                for (int32 At = 0; At < ValueWidth; ++At)
                {
                    Reader->Read(ValueDictionary.AddDefaulted_GetRef());
                }
            }
        }

        /**
         * How many elements the next row of an array column holds.
         *
         * One call whichever way the block is laid out. An encoded array decoded every
         * length before the first element was read, so this hands out what it already has;
         * a raw one states each row's length in front of that row's elements, so this reads
         * it where it stands.
         */
        bool NextLength(int32& Out)
        {
            // Explicit, for the same reason as NextI32: a decoded length reads no byte, so
            // without this a failed reader could keep handing out row lengths.
            if (Reader->HasFailed())
            {
                return false;
            }

            if (bHasLengths)
            {
                if (LengthAt >= Lengths.Num())
                {
                    return Reader->FailWith(FString::Printf(
                        TEXT("%s: the column has no more rows to read"), FieldName));
                }

                Out = Lengths[LengthAt++];
                return true;
            }

            if (!Reader->ReadCounter32(Out))
            {
                return false;
            }

            if (Out < 0)
            {
                const int32 Bad = Out;

                Out = 0;
                return Reader->FailWith(FString::Printf(
                    TEXT("%s: a row declares %d elements"), FieldName, Bad));
            }

            return true;
        }

        /** The next int32 - which also serves enums, and reference indexes. */
        /**
         * The next value of a bit-packed stream: the packed bits, over the block's base.
         *
         * A value may cross a byte boundary, so this walks bits rather than bytes. The
         * addition wraps, mirroring the writer's wrapping subtraction.
         */
        int64 NextPacked()
        {
            uint64 Slot = 0;

            for (int32 At = 0; At < PackedWidth; ++At, ++PackedBit)
            {
                if ((Packed[static_cast<int32>(PackedBit >> 3)] >> (PackedBit & 7)) & 1)
                {
                    Slot |= static_cast<uint64>(1) << At;
                }
            }

            return static_cast<int64>(static_cast<uint64>(PackedBase) + Slot);
        }

        bool NextI32(int32& Out)
        {
            // Explicit, because a run already in progress reads no byte: without this,
            // a failed reader could keep handing out the run's value.
            if (Reader->HasFailed())
            {
                return false;
            }

            --RowsRemaining;

            if (Encoding == EncodingBitpack)
            {
                Out = static_cast<int32>(NextPacked());
                return true;
            }

            switch (Encoding)
            {
            case EncodingRaw:
                return Element == ElementI32 ? Reader->Read(Out) : Reader->ReadCounter32(Out);

            case EncodingVarint:
                return Reader->ReadCounter32(Out);

            case EncodingDelta:
            {
                int32 Step = 0;
                if (!Reader->ReadCounter32(Step))
                {
                    return false;
                }

                // The addition wraps on purpose, mirroring the writer's wrapping
                // subtraction; together they are exact for every int32 pair. Done on
                // uint32, because signed overflow is undefined in C++.
                Previous = bStarted
                    ? static_cast<int32>(static_cast<uint32>(Previous) + static_cast<uint32>(Step))
                    : Step;
                bStarted = true;

                Out = Previous;
                return true;
            }

            case EncodingRle:
            {
                if (RunRemaining == 0 && !ReadRun())
                {
                    return false;
                }

                --RunRemaining;
                Out = RunValue;
                return true;
            }

            default: // EncodingDeltaRle; CheckColumn refused everything else.
            {
                if (!bStarted)
                {
                    if (!Reader->ReadCounter32(Previous))
                    {
                        return false;
                    }

                    bStarted = true;
                    Out = Previous;
                    return true;
                }

                if (RunRemaining == 0 && !ReadRun())
                {
                    return false;
                }

                --RunRemaining;
                Previous = static_cast<int32>(
                    static_cast<uint32>(Previous) + static_cast<uint32>(RunValue));

                Out = Previous;
                return true;
            }
            }
        }

        /**
         * An int64 member: from an i64 column raw or through its dictionary, and from
         * anything narrower by decoding an int32 and widening it.
         */
        bool NextI64(int64& Out)
        {
            if (Element == ElementI64)
            {
                if (Encoding == EncodingBitpack)
                {
                    if (Reader->HasFailed())
                    {
                        return false;
                    }

                    --RowsRemaining;
                    Out = NextPacked();
                    return true;
                }

                if (HasValueDictionary())
                {
                    const uint8* Bytes = nullptr;
                    if (!NextValueEntry(Bytes))
                    {
                        return false;
                    }

                    Out = static_cast<int64>(Fixed64At(Bytes));
                    return true;
                }

                --RowsRemaining;
                return Reader->Read(Out);
            }

            int32 Narrower = 0;
            const bool bOk = NextI32(Narrower);

            Out = Narrower;
            return bOk;
        }

        /**
         * A float member: raw, the dictionary entry's exact bit pattern, or a whole number.
         */
        bool NextF32(float& Out)
        {
            if (bWholeNumbers)
            {
                int32 Integer = 0;
                const bool bOk = NextI32(Integer);

                Out = static_cast<float>(Integer);
                return bOk;
            }

            if (HasValueDictionary())
            {
                const uint8* Bytes = nullptr;
                if (!NextValueEntry(Bytes))
                {
                    return false;
                }

                const uint32 Bits = Fixed32At(Bytes);

                FMemory::Memcpy(&Out, &Bits, sizeof(Out));
                return true;
            }

            --RowsRemaining;
            return Reader->Read(Out);
        }

        /**
         * A double member: from f64 or f32 - either of them raw or dictionary-encoded -
         * and from an i32 column by decoding and widening.
         */
        bool NextF64(double& Out)
        {
            if (bWholeNumbers)
            {
                int32 Integer = 0;
                const bool bOk = NextI32(Integer);

                Out = Integer;
                return bOk;
            }

            if (Element == ElementF64)
            {
                if (HasValueDictionary())
                {
                    const uint8* Bytes = nullptr;
                    if (!NextValueEntry(Bytes))
                    {
                        return false;
                    }

                    const uint64 Bits = Fixed64At(Bytes);

                    FMemory::Memcpy(&Out, &Bits, sizeof(Out));
                    return true;
                }

                --RowsRemaining;
                return Reader->Read(Out);
            }

            if (Element == ElementF32)
            {
                float Single = 0.0f;
                const bool bOk = NextF32(Single);

                Out = Single;
                return bOk;
            }

            int32 Integer = 0;
            const bool bOk = NextI32(Integer);

            Out = Integer;
            return bOk;
        }

        /** A bool member: one byte raw, or a run of them. */
        bool NextBool(bool& Out)
        {
            if (Encoding == EncodingRle || Encoding == EncodingBitpack)
            {
                int32 Value = 0;
                if (!NextI32(Value))
                {
                    return false;
                }

                Out = Value != 0;
                return true;
            }

            --RowsRemaining;
            return Reader->Read(Out);
        }

        /**
         * A datetime member, built from the ticks its i64 column carries.
         *
         * The range check is the one Read(FDateTime&) makes, and for the same reason: a
         * tick count outside what FDateTime holds would assert inside the engine on some
         * versions, which is exactly the kind of failure this reader turns into a message.
         */
        bool NextDateTime(FDateTime& Out)
        {
            int64 Ticks = 0;
            if (!NextI64(Ticks))
            {
                return false;
            }

            if (Ticks < 0 || Ticks > FDateTime::MaxValue().GetTicks())
            {
                return Reader->FailWith(FString::Printf(
                    TEXT("%s: datetime tick count %lld is outside what FDateTime can hold"),
                    FieldName, Ticks));
            }

            Out = FDateTime(Ticks);
            return true;
        }

        /** A timespan member, from the same ticks. Signed, and every int64 is a valid one. */
        bool NextTimespan(FTimespan& Out)
        {
            int64 Ticks = 0;
            const bool bOk = NextI64(Ticks);

            Out = FTimespan(Ticks);
            return bOk;
        }

        /** An enum member: its value travels as an int32, whatever the block's encoding. */
        template <typename TEnum>
        bool NextEnum(TEnum& Out)
        {
            int32 Value = 0;
            if (!NextI32(Value))
            {
                return false;
            }

            Out = static_cast<TEnum>(Value);
            return true;
        }

        /** The next string - the dictionary's entry where the block has one. */
        bool NextString(FString& Out)
        {
            // Explicit, for the same reason as NextI32: a run in progress reads no byte.
            if (Reader->HasFailed())
            {
                return false;
            }

            --RowsRemaining;

            switch (Encoding)
            {
            case EncodingRaw:
                return Reader->Read(Out);

            case EncodingDict:
            case EncodingDictFront:
            {
                int32 Index = 0;
                if (!Reader->ReadCounter32(Index))
                {
                    return false;
                }

                return DictionaryEntry(Index, Out);
            }

            default: // EncodingDictRle and EncodingDictFrontRle.
            {
                if (RunRemaining == 0 && !ReadRun())
                {
                    return false;
                }

                --RunRemaining;
                return DictionaryEntry(RunValue, Out);
            }
            }
        }

        // The reader's ReadAs family, one level down. An array's elements read through the
        // cursor exactly as a scalar column's row does, and these are what let the generated
        // code spell that read the same way for both - the member's own type picks the
        // decode, as it does everywhere else in this reader.
        //
        // The element argument is taken and ignored: the cursor was given the column and
        // already holds it. It is in the signature so that one generated line serves a
        // column whether it reads through a cursor or straight from the reader.
        //
        // No template fallback, deliberately. A uuid has no encoding and so no cursor path,
        // and a generator that sent one here should not compile.

        bool NextAs(uint8 InElement, int32& Out) { (void)InElement; return NextI32(Out); }
        bool NextAs(uint8 InElement, int64& Out) { (void)InElement; return NextI64(Out); }
        bool NextAs(uint8 InElement, float& Out) { (void)InElement; return NextF32(Out); }
        bool NextAs(uint8 InElement, double& Out) { (void)InElement; return NextF64(Out); }
        bool NextAs(uint8 InElement, bool& Out) { (void)InElement; return NextBool(Out); }
        bool NextAs(uint8 InElement, FString& Out) { (void)InElement; return NextString(Out); }

        bool NextAs(uint8 InElement, FDateTime& Out)
        {
            (void)InElement;
            return NextDateTime(Out);
        }

        bool NextAs(uint8 InElement, FTimespan& Out)
        {
            (void)InElement;
            return NextTimespan(Out);
        }

        /** An enum, which travels as an int32 and has nothing to promote. */
        template <typename TEnum>
        bool NextEnumAs(uint8 InElement, TEnum& Out)
        {
            (void)InElement;
            return NextEnum(Out);
        }

        /**
         * Up to `Limit` rows that all hold the next value. `OutCount` is how many, always
         * at least 1, and `Out` is the value.
         *
         * This is what makes a run cost one call instead of one per row: the generated
         * loop asks once, then assigns the value that many times. An encoding that cannot
         * promise sameness cheaply answers 1, so the caller's loop is correct over every
         * encoding and only faster over runs.
         */
        bool NextSameI32(int32 Limit, int32& OutCount, int32& Out)
        {
            OutCount = 1;

            if (Reader->HasFailed())
            {
                return false;
            }

            if (Encoding == EncodingRle)
            {
                --RowsRemaining;

                if (RunRemaining == 0 && !ReadRun())
                {
                    return false;
                }

                const int32 Taken = RunRemaining < Limit ? RunRemaining : Limit;
                RunRemaining -= Taken;
                RowsRemaining -= Taken - 1;

                OutCount = Taken;
                Out = RunValue;
                return true;
            }

            if (Encoding == EncodingDeltaRle && bStarted)
            {
                --RowsRemaining;

                if (RunRemaining == 0 && !ReadRun())
                {
                    return false;
                }

                if (RunValue == 0)
                {
                    // A zero-delta run is a run of one value.
                    const int32 Taken = RunRemaining < Limit ? RunRemaining : Limit;
                    RunRemaining -= Taken;
                    RowsRemaining -= Taken - 1;

                    OutCount = Taken;
                    Out = Previous;
                    return true;
                }

                --RunRemaining;
                Previous = static_cast<int32>(
                    static_cast<uint32>(Previous) + static_cast<uint32>(RunValue));

                Out = Previous;
                return true;
            }

            return NextI32(Out);
        }

        /** The string counterpart of NextSameI32. */
        bool NextSameString(int32 Limit, int32& OutCount, FString& Out)
        {
            OutCount = 1;

            if (Reader->HasFailed())
            {
                return false;
            }

            if (Encoding == EncodingDictRle || Encoding == EncodingDictFrontRle)
            {
                --RowsRemaining;

                if (RunRemaining == 0 && !ReadRun())
                {
                    return false;
                }

                const int32 Taken = RunRemaining < Limit ? RunRemaining : Limit;
                RunRemaining -= Taken;
                RowsRemaining -= Taken - 1;

                OutCount = Taken;
                return DictionaryEntry(RunValue, Out);
            }

            return NextString(Out);
        }

    private:
        /**
         * How many elements the block's element stream holds, once an array block has named
         * the encoding they travel under.
         *
         * A variable array states its rows' lengths as a stream of its own in front of the
         * elements, so every one of them is decoded here - which also leaves the reader
         * exactly where the first element is. A fixed array states nothing: the count is in
         * the descriptor, and writing it per row would be the format repeating itself.
         */
        bool OpenElementStream(const FTabbitColumn& Column, int32 RowCount)
        {
            // Counted in an int64 first, because both products come off the wire and a
            // column claiming more elements than can be held is a file to refuse rather
            // than a multiplication to let wrap.
            int64 Elements = 0;

            if (Column.Kind == KindArray)
            {
                uint8 LengthEncoding = 0;
                if (!Reader->Read(LengthEncoding))
                {
                    return false;
                }

                if (!ReadLengths(LengthEncoding, RowCount))
                {
                    return false;
                }

                for (const int32 Length : Lengths)
                {
                    Elements += Length;
                }
            }

            constexpr int64 Holdable = 0x7FFFFFFF;

            if (Elements > Holdable)
            {
                return Reader->FailWith(FString::Printf(
                    TEXT("%s: the column declares more elements than can be held"), FieldName));
            }

            RowsRemaining = static_cast<int32>(Elements);
            return true;
        }

        /**
         * The lengths of an array column's rows, as their own encoded stream.
         *
         * A varint stream, so what may be chosen for it is what may be chosen for any varint
         * column - each length as a counter32, or runs of them. Most columns have rows that
         * are all the same length, which is one run.
         */
        bool ReadLengths(uint8 LengthEncoding, int32 RowCount)
        {
            Lengths.SetNumZeroed(RowCount);
            bHasLengths = true;

            if (LengthEncoding == EncodingRaw)
            {
                for (int32 At = 0; At < RowCount; ++At)
                {
                    if (!Reader->ReadCounter32(Lengths[At]))
                    {
                        return false;
                    }

                    if (Lengths[At] < 0)
                    {
                        return Reader->FailWith(FString::Printf(
                            TEXT("%s: row %d declares %d elements"),
                            FieldName, At, Lengths[At]));
                    }
                }

                return true;
            }

            if (LengthEncoding != EncodingRle)
            {
                return Reader->FailWith(FString::Printf(
                    TEXT("%s: encoding %d cannot carry an array column's row lengths"),
                    FieldName, LengthEncoding));
            }

            int32 Filled = 0;

            while (Filled < RowCount)
            {
                int32 Run = 0;
                int32 Value = 0;

                if (!Reader->ReadCounter32(Run) || !Reader->ReadCounter32(Value))
                {
                    return false;
                }

                if (Run < 1 || Run > RowCount - Filled)
                {
                    return Reader->FailWith(FString::Printf(
                        TEXT("%s: a run of %d lengths cannot cover the %d rows left in the column"),
                        FieldName, Run, RowCount - Filled));
                }

                if (Value < 0)
                {
                    return Reader->FailWith(FString::Printf(
                        TEXT("%s: a row declares %d elements"), FieldName, Value));
                }

                for (int32 At = 0; At < Run; ++At)
                {
                    Lengths[Filled++] = Value;
                }
            }

            return true;
        }

        /**
         * A dictionary whose entries are lists of references into a table of the pieces they
         * are built from.
         *
         * Two reads and a concatenation: the table, which is front coded because its own
         * entries share their fronts, and then each value as the pieces it is made of. The
         * result is the same array of whole strings every other dictionary produces, so
         * nothing downstream of here knows which kind it came from.
         */
        void ReadSegmentDictionary()
        {
            int32 SegmentCount = 0;
            if (!Reader->ReadCounter32(SegmentCount))
            {
                return;
            }

            // Bounded, because the count came out of the file: no file describes more
            // segments than it has bytes left, and an entry is at least two bytes.
            if (SegmentCount < 0 || SegmentCount > Reader->Remaining())
            {
                Reader->FailWith(FString::Printf(
                    TEXT("%s: a segment table of %d entries is larger than the file can hold"),
                    FieldName, SegmentCount));

                return;
            }

            TArray<TArray<uint8>> Segments;
            Segments.Empty(ReserveBound(SegmentCount));

            int32 PreviousLength = 0;

            for (int32 At = 0; At < SegmentCount; ++At)
            {
                int32 Shared = 0;
                int32 Rest = 0;

                if (!Reader->ReadCounter32(Shared) || !Reader->ReadCounter32(Rest))
                {
                    return;
                }

                if (Shared < 0 || Rest < 0 || Shared > PreviousLength)
                {
                    Reader->FailWith(FString::Printf(
                        TEXT("%s: segment %d shares %d bytes with an entry of %d"),
                        FieldName, At, Shared, PreviousLength));

                    return;
                }

                TArray<uint8>& Segment = Segments.AddDefaulted_GetRef();
                Segment.SetNumZeroed(Shared + Rest);

                for (int32 Byte = 0; Byte < Shared; ++Byte)
                {
                    Segment[Byte] = Segments[At - 1][Byte];
                }

                for (int32 Byte = 0; Byte < Rest; ++Byte)
                {
                    if (!Reader->Read(Segment[Shared + Byte]))
                    {
                        return;
                    }
                }

                PreviousLength = Shared + Rest;
            }

            int32 Count = 0;
            if (!Reader->ReadCounter32(Count))
            {
                return;
            }

            if (Count < 0 || Count > Reader->Remaining())
            {
                Reader->FailWith(FString::Printf(
                    TEXT("%s: a dictionary of %d entries is larger than the file can hold"),
                    FieldName, Count));

                return;
            }

            Dictionary.Empty(ReserveBound(Count));

            // Only ever grows, to the longest entry, and is reused - so the allocations are
            // the strings themselves, one per distinct value, which is the point.
            TArray<uint8> Scratch;

            for (int32 At = 0; At < Count; ++At)
            {
                int32 Pieces = 0;
                if (!Reader->ReadCounter32(Pieces))
                {
                    return;
                }

                if (Pieces < 0)
                {
                    Reader->FailWith(FString::Printf(
                        TEXT("%s: dictionary entry %d declares %d pieces"),
                        FieldName, At, Pieces));

                    return;
                }

                int32 Length = 0;

                for (int32 Piece = 0; Piece < Pieces; ++Piece)
                {
                    int32 Index = 0;
                    if (!Reader->ReadCounter32(Index))
                    {
                        return;
                    }

                    if (Index < 0 || Index >= Segments.Num())
                    {
                        Reader->FailWith(FString::Printf(
                            TEXT("%s: segment index %d is out of range - the table holds %d entries"),
                            FieldName, Index, Segments.Num()));

                        return;
                    }

                    const TArray<uint8>& Segment = Segments[Index];

                    // Nothing bounds how often an entry may name the same segment, so the
                    // running length is the one number here that a file could drive past
                    // what an int32 holds.
                    if (Segment.Num() > 0x7FFFFFFF - Length)
                    {
                        Reader->FailWith(FString::Printf(
                            TEXT("%s: dictionary entry %d is longer than can be held"),
                            FieldName, At));

                        return;
                    }

                    // Only grows: what the pieces before this one wrote is already where it
                    // needs to be, so this must keep it rather than start the buffer over.
                    if (Scratch.Num() < Length + Segment.Num())
                    {
                        Scratch.SetNum(Length + Segment.Num());
                    }

                    for (int32 Byte = 0; Byte < Segment.Num(); ++Byte)
                    {
                        Scratch[Length + Byte] = Segment[Byte];
                    }

                    Length += Segment.Num();
                }

                // An empty entry is an ordinary one, and the default-constructed FString is
                // already what it decodes to.
                FString& Entry = Dictionary.AddDefaulted_GetRef();

                if (Length > 0)
                {
                    const FUTF8ToTCHAR Converted(
                        reinterpret_cast<const UTF8CHAR*>(Scratch.GetData()), Length);

                    Entry = FString(Converted.Length(), Converted.Get());
                }
            }
        }

        /**
         * A sorted dictionary whose entries state only what they do not share with the
         * entry before them.
         *
         * Decoded into whole strings here rather than kept folded, because a row wants a
         * string and the folding was only ever about the bytes on disk. The scratch buffer
         * only ever grows, to the longest entry, and is reused - so the allocations are the
         * strings themselves, one per distinct value, which is the point.
         */
        void ReadFrontCodedDictionary(int32 Count)
        {
            // Bounded, because the count came out of the file. The array grows past this
            // if the entries really are there.
            Dictionary.Empty(ReserveBound(Count));

            TArray<uint8> Scratch;
            int32 PreviousLength = 0;

            while (Dictionary.Num() < Count && !Reader->HasFailed())
            {
                int32 Shared = 0;
                int32 Rest = 0;

                if (!Reader->ReadCounter32(Shared) || !Reader->ReadCounter32(Rest))
                {
                    return;
                }

                if (Shared < 0 || Rest < 0 || Shared > PreviousLength)
                {
                    Reader->FailWith(FString::Printf(
                        TEXT("%s: dictionary entry %d shares %d bytes with an entry of %d"),
                        FieldName, Dictionary.Num(), Shared, PreviousLength));

                    return;
                }

                const int32 Length = Shared + Rest;

                // Only grows: the first Shared bytes are the previous entry's and are
                // already where they need to be.
                if (Scratch.Num() < Length)
                {
                    Scratch.SetNum(Length);
                }

                for (int32 At = 0; At < Rest; ++At)
                {
                    if (!Reader->Read(Scratch[Shared + At]))
                    {
                        return;
                    }
                }

                // An empty entry is an ordinary one, and the default-constructed FString
                // is already what it decodes to.
                FString& Entry = Dictionary.AddDefaulted_GetRef();

                if (Length > 0)
                {
                    const FUTF8ToTCHAR Converted(
                        reinterpret_cast<const UTF8CHAR*>(Scratch.GetData()), Length);

                    Entry = FString(Converted.Length(), Converted.Get());
                }

                PreviousLength = Length;
            }
        }

        /** The bytes of the next row's dictionary entry, for a fixed-width element. */
        bool NextValueEntry(const uint8*& OutBytes)
        {
            // Explicit, for the same reason as NextI32: a run already in progress reads
            // no byte, so without this a failed reader could keep handing out its index.
            if (Reader->HasFailed())
            {
                return false;
            }

            --RowsRemaining;

            int32 Index = 0;

            if (Encoding == EncodingDict)
            {
                if (!Reader->ReadCounter32(Index))
                {
                    return false;
                }
            }
            else // EncodingDictRle; CheckColumn refused everything else.
            {
                if (RunRemaining == 0 && !ReadRun())
                {
                    return false;
                }

                --RunRemaining;
                Index = RunValue;
            }

            const int32 Count = ValueDictionary.Num() / ValueWidth;

            if (Index < 0 || Index >= Count)
            {
                return Reader->FailWith(FString::Printf(
                    TEXT("%s: dictionary index %d is out of range - the dictionary ")
                    TEXT("holds %d entries"),
                    FieldName, Index, Count));
            }

            OutBytes = ValueDictionary.GetData() + Index * ValueWidth;
            return true;
        }

        /** Whether this block's dictionary is one of fixed-width values rather than strings. */
        bool HasValueDictionary() const { return ValueWidth != 0; }

        static uint32 Fixed32At(const uint8* Bytes)
        {
            return static_cast<uint32>(Bytes[0])
                 | static_cast<uint32>(Bytes[1]) << 8
                 | static_cast<uint32>(Bytes[2]) << 16
                 | static_cast<uint32>(Bytes[3]) << 24;
        }

        static uint64 Fixed64At(const uint8* Bytes)
        {
            uint64 Value = 0;
            for (int32 Index = 0; Index < 8; ++Index)
            {
                Value |= static_cast<uint64>(Bytes[Index]) << (8 * Index);
            }

            return Value;
        }

        bool ReadRun()
        {
            int32 Length = 0;
            if (!Reader->ReadCounter32(Length))
            {
                return false;
            }

            // + 1 because the row this run was read for is already counted out of
            // RowsRemaining by its Next call.
            if (Length < 1 || Length > RowsRemaining + 1)
            {
                return Reader->FailWith(FString::Printf(
                    TEXT("%s: a run of %d values cannot cover the %d rows left in the column"),
                    FieldName, Length, RowsRemaining + 1));
            }

            if (!Reader->ReadCounter32(RunValue))
            {
                return false;
            }

            // Committed only once both reads landed, so a block truncated between the
            // length and the value cannot leave a half-read run to hand out.
            RunRemaining = Length;
            return true;
        }

        bool DictionaryEntry(int32 Index, FString& Out)
        {
            if (Index < 0 || Index >= Dictionary.Num())
            {
                return Reader->FailWith(FString::Printf(
                    TEXT("%s: dictionary index %d is out of range - the dictionary ")
                    TEXT("holds %d entries"),
                    FieldName, Index, Dictionary.Num()));
            }

            Out = Dictionary[Index];
            return true;
        }

        FTabbitBinaryReader* Reader = nullptr;
        const TCHAR* FieldName = TEXT("");
        uint8 Element = 0;
        uint8 Encoding = 0;

        /**
         * The block's dictionary, decoded once by Open and handed out per row.
         *
         * One of these two is filled when the block has a dictionary at all, chosen by the
         * element: strings are decoded to instances that rows then share, and a fixed-width
         * element keeps its raw bytes so the value is reconstructed exactly as the raw
         * layout would have read it.
         */
        TArray<FString> Dictionary;

        TArray<uint8> ValueDictionary;

        /** Bytes per fixed-width dictionary entry: 4 for f32, 8 for i64 and f64, 0 for none. */
        int32 ValueWidth = 0;

        // A run-length family's current run: what remains of it, and its value - which
        // is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
        int32 RunRemaining = 0;
        int32 RunValue = 0;

        // The delta family's accumulator, once bStarted.
        int32 Previous = 0;
        bool bStarted = false;

        // Values not yet handed out. A run that claims more than this is corrupt, and
        // catching it here names the field instead of leaving it to the block-end check.
        // For an array column this counts elements, not rows.
        int32 RowsRemaining = 0;

        /**
         * How many elements each row holds, decoded up front for an encoded array column.
         *
         * Up front because the element stream follows the length stream in the block, so
         * every length has been read by the time the first element is. Not filled for a raw
         * array, whose lengths are interleaved with its elements and read as they are
         * reached - which is what the flag beside it says, since a table of no rows decodes
         * no lengths either.
         */
        TArray<int32> Lengths;

        int32 LengthAt = 0;
        bool bHasLengths = false;

        /** Whether a float column's values are travelling as integers. */
        bool bWholeNumbers = false;

        /**
         * A bit-packed column's bytes, decoded up front, and where in them the next value
         * is.
         *
         * Up front because the bytes are themselves under an encoding and a value can
         * cross a byte boundary, so handing values out one at a time would mean carrying a
         * decoder and a bit offset that disagree about where they are.
         */
        TArray<uint8> Packed;
        int32 PackedWidth = 0;
        int64 PackedBase = 0;
        int64 PackedBit = 0;
    };
}
