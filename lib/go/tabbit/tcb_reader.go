// Tabbit's binary reader.
//
// Copied in beside the generated accessor so the emitted code needs nothing
// installed. Edit it in the Tabbit repository.
//
//
// Reads the .tcb files Tabbit's binary exporter writes:
//
//	fixed8      one byte
//	fixed32     four bytes, little endian
//	fixed64     eight bytes, little endian
//	varint32    seven bits per byte, high bit set while more bytes follow,
//	            at most five bytes
//	counter32   zig-zag encoded int32 written as a varint32
//	string      counter32 byte length, then that many UTF-8 bytes
//
// One of several readers of one format the exporter defines. The others are the
// emitted C#, C++, TypeScript and Rust ones, and the conformance corpus is what
// keeps them agreeing.
//
// Go needs none of the care the other languages do here: int64 is int64, float32 is
// float32, and uint32 shifts the way varint decoding wants. That is the reason to
// add it first.

package tabbit

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/binary"
	"fmt"
	"math"
	"math/bits"
	"os"
	"time"
)

// FormatVersion is stamped at the head of every table file by the exporter.
//
// 101 was column-oriented and self-describing; it replaced 100 outright, before the
// tool fed anything live. 102 replaced 101 the same way - a descriptor gained its
// encoding byte - before any 101 file had shipped. 104 is the current one: four
// encodings joined the nine, and the flags byte gained a meaning.
const FormatVersion uint32 = 106

// The wire's element types and kinds, as a column descriptor spells them.
const (
	ElementVarint uint8 = 0
	ElementBool   uint8 = 1
	ElementI32    uint8 = 2
	ElementI64    uint8 = 3
	ElementF32    uint8 = 4
	ElementF64    uint8 = 5
	ElementString uint8 = 6
	ElementUUID   uint8 = 7

	KindScalar     uint8 = 0
	KindFixedArray uint8 = 1
	KindVarArray   uint8 = 2

	// How a block's values are laid out. Raw is the layout 101 had; the others
	// compress a column that repeats itself. spec/tcb-v102-column-encoding.md is
	// the contract.
	EncodingRaw          uint8 = 0
	EncodingVarint       uint8 = 1
	EncodingDelta        uint8 = 2
	EncodingRle          uint8 = 3
	EncodingDeltaRle     uint8 = 4
	EncodingDict         uint8 = 5
	EncodingDictRle      uint8 = 6
	EncodingDictFront    uint8 = 7
	EncodingDictFrontRle uint8 = 8

	// Composition rather than layout. An array block names an encoding for its
	// elements and one for its rows' lengths, and a whole-number float block names
	// the integer encoding its values travel under - so both are decoded by the
	// cursors that already exist, one level down.
	EncodingArray uint8 = 9
	EncodingWhole uint8 = 10

	// A dictionary whose entries are built from a shared table of the pieces they are
	// made of, which reaches what two values share in the middle and at the end where
	// front coding can only reach what they share at the front.
	EncodingDictSegment    uint8 = 11
	EncodingDictSegmentRle uint8 = 12

	// EncodingBitpack carries an integer stream at the width its own range
	// needs, over a base subtracted from every value.
	EncodingBitpack uint8 = 13

	// The file header, at fixed offsets whether or not the file is encrypted and
	// whether or not it carries a MAC. spec/tcb-mac-and-signature.md.
	MagicOffset    = 0
	VersionOffset  = 4
	FlagsOffset    = 8
	CipherOffset   = 9
	NonceOffset    = 10
	MacOffset      = 22
	KeyCheckOffset = 38

	// HeaderSize is where the body begins. The header before it is always this long.
	HeaderSize = 42

	NonceSize = 12
	MacSize   = 16

	// Magic is the signature, as the fixed32 it is on disk: 'S' 'C' 'B' 0, little
	// endian.
	//
	// The same four bytes serve twice. At offset zero they are the file format
	// signature, in the clear whether or not the file is encrypted. At the key check
	// they are under the key, so a file that decrypts to something else was written
	// with a different key - which is the one thing no structural check can tell from
	// damage.
	Magic uint32 = 0x00424354

	// FlagEncrypted is bit 0 of the flags byte: from the key check on, the file is
	// ciphertext.
	FlagEncrypted uint8 = 0x01

	// CipherNone is the cipher byte of a file that is not encrypted.
	CipherNone uint8 = 0

	// CipherChaCha20 is the only cipher the format defines.
	CipherChaCha20 uint8 = 1
)

// Column is one column as the file describes it.
type Column struct {
	// Tag identifies the column, instead of its position.
	Tag int32
	Element uint8
	Kind    uint8
	// Encoding says how the block's values are laid out: one of the Encoding* constants.
	Encoding uint8
	// Count is the elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one.
	Count int32
	// ByteLength is the column block's total bytes - what a skip advances by.
	ByteLength int32
	// Nullable says the block begins with one presence bit per row, low bit first.
	//
	// Part of the column's shape rather than a detail of its contents: a reader that
	// does not expect the bitmap reads it as values, so CheckColumn refuses a
	// disagreement the same way it refuses a changed kind.
	Nullable bool

	// ElementNullable says the block states, per element, which of an array's places hold
	// a value. Independent of Nullable: a column may say either, or both.
	// spec/nullable-array-elements.md.
	ElementNullable bool
}

// ticksPerSecond is the .NET tick, 100 nanoseconds.
const ticksPerSecond int64 = 10000000

// unixEpochTicks is 1970-01-01 in .NET ticks, which count from 0001-01-01.
const unixEpochTicks int64 = 621355968000000000

// Reader is a sequential reader over a table file's bytes.
//
// Every read either advances the cursor or returns an error, so a caller that checks
// once at the end of a record has checked every field in it.
type Reader struct {
	data []byte
	pos  int
	err  error
}

// NewReader returns a reader over data, which it does not copy.
func NewReader(data []byte) *Reader {
	return &Reader{data: data}
}

// Err reports the first failure, if any. A reader that has failed stays failed and
// every subsequent read is a no-op, so the caller need not check between fields.
func (r *Reader) Err() error { return r.err }

// Position is the number of bytes consumed so far.
func (r *Reader) Position() int { return r.pos }

// Remaining is the number of bytes left to read.
func (r *Reader) Remaining() int { return len(r.data) - r.pos }

func (r *Reader) take(count int) []byte {
	if r.err != nil {
		return nil
	}

	if r.Remaining() < count {
		r.err = fmt.Errorf(
			"tabbit: table data ended after %d of %d bytes while %d more were expected",
			r.pos, len(r.data), count)
		return nil
	}

	slice := r.data[r.pos : r.pos+count]
	r.pos += count

	return slice
}

// ReadUint8 reads one byte.
func (r *Reader) ReadUint8() uint8 {
	b := r.take(1)
	if b == nil {
		return 0
	}
	return b[0]
}

// ReadBool reads one byte, non-zero being true.
func (r *Reader) ReadBool() bool { return r.ReadUint8() != 0 }

// ReadInt32 reads four bytes, little endian.
func (r *Reader) ReadInt32() int32 {
	b := r.take(4)
	if b == nil {
		return 0
	}
	return int32(binary.LittleEndian.Uint32(b))
}

// ReadUint32 reads four bytes, little endian.
func (r *Reader) ReadUint32() uint32 {
	b := r.take(4)
	if b == nil {
		return 0
	}
	return binary.LittleEndian.Uint32(b)
}

// ReadInt64 reads eight bytes, little endian.
func (r *Reader) ReadInt64() int64 {
	b := r.take(8)
	if b == nil {
		return 0
	}
	return int64(binary.LittleEndian.Uint64(b))
}

// ReadFloat32 reads a single-precision value as its stored bit pattern, so the value
// survives exactly rather than through a decimal rendering.
func (r *Reader) ReadFloat32() float32 {
	b := r.take(4)
	if b == nil {
		return 0
	}
	return math.Float32frombits(binary.LittleEndian.Uint32(b))
}

// ReadFloat64 reads a double-precision value as its stored bit pattern.
func (r *Reader) ReadFloat64() float64 {
	b := r.take(8)
	if b == nil {
		return 0
	}
	return math.Float64frombits(binary.LittleEndian.Uint64(b))
}

// ReadString reads a length-prefixed UTF-8 string.
func (r *Reader) ReadString() string {
	length := int(r.ReadCounter32())
	if r.err != nil {
		return ""
	}

	if length < 0 {
		r.err = fmt.Errorf("tabbit: string length is negative")
		return ""
	}

	if length == 0 {
		return ""
	}

	b := r.take(length)
	if b == nil {
		return ""
	}

	return string(b)
}

// ReadDateTimeTicks reads a timestamp as .NET ticks: 100 ns units since 0001-01-01.
//
// Ticks rather than a time.Time, because the conversion loses the years before 1970
// that Go's zero time cannot express, and a caller that only passes the value through
// should not pay for it. ReadTime is there for one that wants it.
func (r *Reader) ReadDateTimeTicks() int64 { return r.ReadInt64() }

// ReadTime reads a timestamp as a time.Time in UTC.
func (r *Reader) ReadTime() time.Time {
	ticks := r.ReadDateTimeTicks()
	if r.err != nil {
		return time.Time{}
	}

	return time.Unix(0, (ticks-unixEpochTicks)*100).UTC()
}

// ReadDurationTicks reads a duration as .NET ticks.
func (r *Reader) ReadDurationTicks() int64 { return r.ReadInt64() }

// ReadDuration reads a duration as a time.Duration.
//
// A tick is 100 ns and a Duration counts nanoseconds, so nothing is lost either way
// for any value a sheet can hold.
func (r *Reader) ReadDuration() time.Duration {
	return time.Duration(r.ReadDurationTicks() * 100)
}

// UUID is a 128-bit identifier in .NET Guid byte order.
//
// That order is not plain big-endian: the first three components are little endian and
// the trailing eight bytes are not, which is what String has to account for.
type UUID [16]byte

// guidOrder maps output position to byte index, matching .NET's Guid.ToString("D").
var guidOrder = [16]int{3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15}

// String renders the uuid the way .NET's Guid.ToString("D") does.
func (u UUID) String() string {
	const hex = "0123456789abcdef"

	out := make([]byte, 0, 36)

	for i, index := range guidOrder {
		if i == 4 || i == 6 || i == 8 || i == 10 {
			out = append(out, '-')
		}

		b := u[index]
		out = append(out, hex[b>>4], hex[b&0x0F])
	}

	return string(out)
}

// ReadUUID reads the sixteen bytes of a uuid's .NET layout.
func (r *Reader) ReadUUID() UUID {
	var result UUID

	b := r.take(16)
	if b == nil {
		return result
	}

	copy(result[:], b)
	return result
}

// ReadOptimalInt32 reads an int32 written in as few bytes as its magnitude needed,
// either sign.
// Skip advances past bytes without interpreting them: an unknown column's whole block.
// The column-oriented layout is what makes this one call the entirety of skipping.
func (r *Reader) Skip(byteCount int32) {
	if r.err != nil {
		return
	}
	if byteCount < 0 || int(byteCount) > r.Remaining() {
		r.err = fmt.Errorf("tabbit: cannot skip %d bytes with %d remaining", byteCount, r.Remaining())
		return
	}
	r.pos += int(byteCount)
}

// The promotions: a member reading a file element narrower than itself. Only the
// mathematically lossless directions exist; CheckColumn already refused the rest.

// ReadI32As reads an int32 member from i32 or varint.
func (r *Reader) ReadI32As(element uint8) int32 {
	if element == ElementI32 {
		return r.ReadInt32()
	}
	return r.ReadOptimalInt32()
}

// ReadI64As reads an int64 member from i64, i32 or varint.
func (r *Reader) ReadI64As(element uint8) int64 {
	switch element {
	case ElementI64:
		return r.ReadInt64()
	case ElementI32:
		return int64(r.ReadInt32())
	default:
		return int64(r.ReadOptimalInt32())
	}
}

// ReadF64As reads a float64 member from f64, f32 or i32 - all exact in a float64.
func (r *Reader) ReadF64As(element uint8) float64 {
	switch element {
	case ElementF64:
		return r.ReadFloat64()
	case ElementF32:
		return float64(r.ReadFloat32())
	default:
		return float64(r.ReadInt32())
	}
}

func (r *Reader) ReadOptimalInt32() int32 {
	encoded := r.readVarint32()
	if r.err != nil {
		return 0
	}

	// Undoes the zig-zag fold: the low bit carried the sign. A shift would not do -
	// the value is unsigned, so a right shift brings in a zero where the sign has to
	// come from the low bit instead.
	return int32(encoded>>1) ^ -int32(encoded&1)
}

// ReadCounter32 reads a count, in the same encoding as ReadOptimalInt32.
func (r *Reader) ReadCounter32() int32 { return r.ReadOptimalInt32() }

// ReadCounter64 reads an int64 written in as few bytes as its magnitude needed.
//
// The base of a bit-packed block, which is a value of the column's own element type -
// an i64 column's base does not fit in thirty-two bits. One byte when it is zero.
func (r *Reader) ReadCounter64() int64 {
	var encoded uint64
	var shift uint

	for {
		piece := r.ReadUint8()
		if r.err != nil {
			return 0
		}

		encoded |= uint64(piece&0x7F) << shift

		if piece&0x80 == 0 {
			break
		}

		shift += 7
		if shift > 63 {
			r.err = fmt.Errorf("tabbit: a 64-bit variable length integer runs past ten bytes")
			return 0
		}
	}

	return int64(encoded>>1) ^ -int64(encoded&1)
}

// ReadByteStream reads a stream of bytes under one of the integer encodings, which is
// what a packed block and a presence bitmap both end in.
//
// One reader for both, so a bitmap and a packed value block cannot disagree about the
// same bits. The count is known before the call in both cases, so nothing here reads a
// length.
func (r *Reader) ReadByteStream(encoding uint8, count int, fieldName string) []byte {
	if r.err != nil {
		return nil
	}

	out := make([]byte, count)

	if encoding == EncodingRaw {
		for at := range out {
			out[at] = r.ReadUint8()
		}

		return out
	}

	if encoding > EncodingDeltaRle {
		r.err = fmt.Errorf(
			"tabbit: %s: encoding %d cannot carry a packed byte stream", fieldName, encoding)
		return out
	}

	walking := encoding == EncodingDelta || encoding == EncodingDeltaRle

	filled := 0
	previous := int32(0)

	// The first value of a delta stream is written outright; the rest are steps from it.
	// A run in a delta stream repeats the step, not the value, so it walks.
	if count > 0 && walking {
		previous = r.asByte(r.ReadOptimalInt32(), fieldName)
		out[filled] = byte(previous)
		filled++
	}

	for filled < count && r.err == nil {
		run := int32(1)
		step := int32(0)
		value := int32(0)

		switch encoding {
		case EncodingVarint:
			value = r.asByte(r.ReadOptimalInt32(), fieldName)
		case EncodingDelta:
			step = r.ReadOptimalInt32()
		case EncodingRle:
			run = r.ReadCounter32()
			value = r.asByte(r.ReadOptimalInt32(), fieldName)
		default: // EncodingDeltaRle
			run = r.ReadCounter32()
			step = r.ReadOptimalInt32()
		}

		if r.err != nil {
			return out
		}

		if run < 1 || int(run) > count-filled {
			r.err = fmt.Errorf(
				"tabbit: %s: a run of %d cannot cover the %d bytes left",
				fieldName, run, count-filled)
			return out
		}

		for at := int32(0); at < run; at++ {
			if walking {
				previous = r.asByte(previous+step, fieldName)
				out[filled] = byte(previous)
			} else {
				out[filled] = byte(value)
			}

			filled++
		}
	}

	return out
}

// asByte reports a decoded value that has to be a byte, or records that it is not.
func (r *Reader) asByte(value int32, fieldName string) int32 {
	if r.err == nil && (value < 0 || value > 255) {
		r.err = fmt.Errorf("tabbit: %s: %d is not a byte", fieldName, value)
		return 0
	}

	return value
}

// ReadEnum reads an enum value, which travels zig-zag encoded rather than fixed width.
func (r *Reader) ReadEnum() int32 { return r.ReadOptimalInt32() }

func (r *Reader) readVarint32() uint32 {
	var value uint32

	for shift := 0; shift < 35; shift += 7 {
		b := r.ReadUint8()
		if r.err != nil {
			return 0
		}

		value |= uint32(b&0x7F) << shift

		if b&0x80 == 0 {
			return value
		}
	}

	r.err = fmt.Errorf("tabbit: varint32 is longer than five bytes")
	return 0
}

// Open returns a file's plaintext bytes, checked against its MAC on the way.
//
// Call this on the bytes before handing them to a reader. A file that is neither
// encrypted nor authenticated comes back untouched, so the call belongs in the load path
// whether or not the project uses either.
//
// The order is verify, then decrypt. The tag covers the file as it is stored, so an
// altered file is refused before the key is used on it, and the header - the flags, the
// cipher byte, the nonce - is covered along with the body.
//
// Decryption happens in place, and what comes back is a window onto the same slice rather
// than a copy of it. The fields it consumes are returned to what a plain file has in them,
// so calling it twice on the same slice is the same as calling it once.
//
// What the two layers are and are not for: both keys ship inside the client that reads
// the file. Encryption stops a data file being read in an editor; the MAC stops an edited
// one loading. Neither stops anyone who can take the keys out of the client, and no
// format does.
//
// macKey is nil when the project does not sign its files. A reader that has one refuses a
// file that carries no MAC: the field being zero is how a file says it is unauthenticated,
// so accepting that from a project that signs its files would put the check sixteen zero
// bytes away from being removed.
//
// verifyMac false skips the check. For tools and for measuring load time - and no weaker
// than it looks, because anyone who can flip this flag in a shipped binary can read the
// key out of the same binary.
func Open(data []byte, key []byte, macKey []byte, verifyMac bool) ([]byte, error) {
	if len(data) < HeaderSize {
		return nil, fmt.Errorf("tabbit: the file is too short to be a table")
	}

	if binary.LittleEndian.Uint32(data[MagicOffset:]) != Magic {
		return nil, fmt.Errorf(
			"tabbit: the file does not begin with the table file signature")
	}

	if verifyMac {
		if err := checkMac(data, macKey); err != nil {
			return nil, err
		}
	}

	if data[FlagsOffset]&FlagEncrypted == 0 {
		return data, nil
	}

	if data[CipherOffset] != CipherChaCha20 {
		return nil, fmt.Errorf(
			"tabbit: the file uses cipher %d, which this reader does not know",
			data[CipherOffset])
	}

	if len(key) != 32 {
		return nil, fmt.Errorf(
			"tabbit: the file is encrypted and no key, or a key that is not 32 bytes, was given")
	}

	chacha20Apply(key, data[NonceOffset:NonceOffset+NonceSize], data[KeyCheckOffset:])

	if binary.LittleEndian.Uint32(data[KeyCheckOffset:]) != Magic {
		return nil, fmt.Errorf(
			"tabbit: the file did not decrypt to a table - the key is not the one it was written with")
	}

	// Back to what a plain file holds in these bytes, so that a second call over the same
	// slice passes it through instead of decrypting it again.
	data[FlagsOffset] &^= FlagEncrypted
	data[CipherOffset] = CipherNone

	for at := 0; at < NonceSize; at++ {
		data[NonceOffset+at] = 0
	}

	return data, nil
}

// checkMac holds the MAC field against the file's own bytes, and against whether a key
// was given.
//
// The tag is HMAC-SHA-256 over every byte but the sixteen it lives in, truncated to those
// sixteen. Two writes rather than one, because the bytes a tag is written into cannot be
// part of what produces it; skipping them is the same as zeroing them and cheaper by a
// copy of the file.
//
// What it catches is what the structural checks cannot. A block length that does not add
// up is a malformed file and the reader says so; four other bytes in an f32 column is a
// well-formed file holding a different number, and no check over a file's shape can tell
// that from data that was always there.
func checkMac(data []byte, macKey []byte) error {
	// Nothing to check with. A file that carries a tag is read anyway rather than
	// refused: this reader has no way to tell whether the tag is good, and a client
	// built before the project turned MACs on is one this format has promised can still
	// read what it is sent.
	if len(macKey) == 0 {
		return nil
	}

	if len(macKey) != 32 {
		return fmt.Errorf("tabbit: the MAC key given is not 32 bytes")
	}

	present := false

	for at := 0; at < MacSize && !present; at++ {
		present = data[MacOffset+at] != 0
	}

	if !present {
		return fmt.Errorf(
			"tabbit: the file carries no MAC and this build expects one - it was " +
				"exported without a MAC key, or the field was cleared after it was written")
	}

	tag := hmac.New(sha256.New, macKey)

	tag.Write(data[:MacOffset])
	tag.Write(data[KeyCheckOffset:])

	if !hmac.Equal(tag.Sum(nil)[:MacSize], data[MacOffset:MacOffset+MacSize]) {
		return fmt.Errorf(
			"tabbit: the file does not match its MAC - it was altered after it was " +
				"exported, or it was signed with a different key")
	}

	return nil
}

// ReadTableHeader reads and checks a table file's header, returning the row count and
// the column descriptors the data blocks follow.
func ReadTableHeader(r *Reader) (int32, []Column) {
	// Checked again here rather than only in Open, because a reader can be handed bytes
	// that never went through it.
	signature := r.ReadUint32()
	if r.err != nil {
		return 0, nil
	}

	if signature != Magic {
		r.err = fmt.Errorf(
			"tabbit: the file does not begin with the table file signature")
		return 0, nil
	}

	version := r.ReadUint32()
	if r.err != nil {
		return 0, nil
	}

	if version != FormatVersion {
		r.err = fmt.Errorf(
			"tabbit: table format version %d is not supported (expected %d)",
			version, FormatVersion)
		return 0, nil
	}

	// Bit 0 included, not only the bits above it. Open writes a plaintext header with the
	// bit cleared over the envelope, so a reader that still meets it set was handed the
	// ciphertext without the key - and saying so beats letting the block lengths make what
	// they can of it. Every other bit is a feature this build does not have.
	flags := r.ReadUint8()
	if flags&FlagEncrypted != 0 && r.err == nil {
		r.err = fmt.Errorf("tabbit: the table is encrypted and was not decrypted - " +
			"pass the key through Open first")
	}

	if flags != 0 && r.err == nil {
		r.err = fmt.Errorf("tabbit: table declares unsupported features")
	}

	// The cipher byte, the nonce, the MAC and the key check. Open has dealt with all four
	// by now; what is left is to be standing at the body.
	r.Skip(HeaderSize - CipherOffset)

	count := r.ReadCounter32()
	if count < 0 && r.err == nil {
		r.err = fmt.Errorf("tabbit: table row count is negative")
	}

	columnCount := r.ReadCounter32()
	if columnCount < 0 && r.err == nil {
		r.err = fmt.Errorf("tabbit: table column count is negative")
	}

	if r.err != nil {
		return 0, nil
	}

	columns := make([]Column, columnCount)
	for at := range columns {
		columns[at].Tag = r.ReadCounter32()
		wire := r.ReadUint8()
		columns[at].Element = wire & 0x0f
		columns[at].Kind = (wire >> 4) & 0x03
		columns[at].Nullable = wire&0x40 != 0
		columns[at].ElementNullable = wire&0x80 != 0
		columns[at].Encoding = r.ReadUint8()
		columns[at].Count = r.ReadCounter32()
		columns[at].ByteLength = int32(r.ReadUint32())
	}

	if r.err != nil {
		return 0, nil
	}

	// What the descriptors say about the file, checked before anybody allocates for the
	// row count. The blocks are all that follows the header, so their declared lengths have
	// to add up to the bytes left. A raw block also costs at least one byte per row - a
	// varint's shortest form, an empty string's length prefix, a variable array's counter -
	// so a larger row count is one the exporter could not have written. An encoded block
	// has no such floor; its decode checks run sums and dictionary bounds instead.
	available := int32(r.Remaining())
	declared := int32(0)

	for _, column := range columns {
		if column.ByteLength < 0 || column.ByteLength > available-declared {
			r.err = fmt.Errorf(
				"tabbit: column tag %d declares %d bytes, which the file cannot hold",
				column.Tag, column.ByteLength)

			return 0, nil
		}

		declared += column.ByteLength

		if column.Encoding == EncodingRaw && count > column.ByteLength {
			r.err = fmt.Errorf(
				"tabbit: the row count %d is larger than column tag %d can hold in its %d bytes",
				count, column.Tag, column.ByteLength)

			return 0, nil
		}

		// The same floor for the element count, which the read now allocates for: a fixed
		// array's length is the file's rather than the generated code's. Only with rows to
		// read - an empty table writes its columns' counts into a block of no bytes, and
		// that is well-formed.
		if column.Encoding == EncodingRaw && count > 0 && column.Count > column.ByteLength {
			r.err = fmt.Errorf(
				"tabbit: column tag %d says each row holds %d elements, which its %d bytes "+
					"cannot hold", column.Tag, column.Count, column.ByteLength)

			return 0, nil
		}
	}

	if declared != available {
		r.err = fmt.Errorf(
			"tabbit: the columns declare %d bytes but %d follow the header", declared, available)

		return 0, nil
	}

	return count, columns
}

// ReadPresence reads a nullable column's presence bitmap, which sits at the front of
// its block. It returns nil for a column that is not optional, which is what lets the
// generated code call IsPresent without testing first.
func ReadPresence(r *Reader, col Column, rowCount int32) []byte {
	if !col.Nullable || r.err != nil {
		return nil
	}

	// The bitmap is a bit-packed boolean column of width one, so it carries an encoding
	// byte and is laid out by the same choice a packed value block uses. Its width and
	// base are known in advance, which is why it does not carry them.
	encoding := r.ReadUint8()

	return r.ReadByteStream(encoding, int((rowCount+7)/8), "a presence bitmap")
}

// ReadElementPresence reads a column's element bitmap, which sits behind the row bitmap
// and in front of the values. It returns nil for a column that does not carry one.
//
// Its length is written ahead of it as a counter32: a variable-length column's total is the
// sum of its row lengths, and those live inside the value block - a reader meeting the
// bitmap first would have nothing to size it by. One bit per element written, in the order
// the block wrote them. spec/nullable-array-elements.md.
func ReadElementPresence(r *Reader, col Column) []byte {
	if !col.ElementNullable || r.err != nil {
		return nil
	}

	elements := r.ReadCounter32()
	encoding := r.ReadUint8()

	return r.ReadByteStream(encoding, int((elements+7)/8), "an element presence bitmap")
}

// IsPresent reports whether a row has a value, for a column that says which do.
//
// A nil bitmap means the column is not optional, and then every row has one.
func IsPresent(presence []byte, row int32) bool {
	return presence == nil || presence[row>>3]&(1<<uint(row&7)) != 0
}

// CheckColumn verifies a column is what the generated member expects, or a lossless
// promotion of it. Refusal is by name and both types, never by reading anyway.
func CheckColumn(r *Reader, col Column, fieldName string, kind uint8, count int32, nullable bool, accepted ...uint8) bool {
	return checkColumn(r, col, fieldName, kind, count, nullable, false, accepted...)
}

// CheckColumnWithElements is CheckColumn for a member whose array elements may be absent.
func CheckColumnWithElements(r *Reader, col Column, fieldName string, kind uint8, count int32, nullable bool, accepted ...uint8) bool {
	return checkColumn(r, col, fieldName, kind, count, nullable, true, accepted...)
}

func checkColumn(r *Reader, col Column, fieldName string, kind uint8, count int32, nullable bool, elementNullable bool, accepted ...uint8) bool {
	if r.err != nil {
		return false
	}

	// The same statement about the other bitmap: code not expecting one would read it as
	// values. spec/nullable-array-elements.md.
	if col.ElementNullable != elementNullable {
		r.err = fmt.Errorf(
			"tabbit: %s: the file and the generated member disagree about whether this "+
				"column's elements are optional; the schema changed, regenerate the code or rebuild the data",
			fieldName)
		return false
	}

	// Nullability is part of the shape: a file that says optional puts a presence bitmap
	// in front of the block, and code not expecting one reads the bitmap as values. So
	// adding or removing a `?` is a schema change like any other, caught here rather than
	// in whatever the misread bytes happened to mean.
	if col.Nullable != nullable {
		r.err = fmt.Errorf(
			"tabbit: %s: the file and the generated member disagree about whether this "+
				"column is optional; the schema changed, regenerate the code or rebuild the data",
			fieldName)
		return false
	}

	// A negative count says the member claims no length: how many elements a row holds is
	// what the file states. The kind is still the member's claim.
	// spec/nullable-array-elements.md.
	if col.Kind != kind || (kind != KindVarArray && count >= 0 && col.Count != count) {
		r.err = fmt.Errorf(
			"tabbit: %s: the file's column (kind %d, count %d) does not match the generated "+
				"member (kind %d, count %d); the schema changed shape, regenerate the code or "+
				"rebuild the data", fieldName, col.Kind, col.Count, kind, count)
		return false
	}

	// An encoding this build cannot decode - or one the spec does not define for this
	// element - is refused by name, exactly like an element it cannot read. An unknown
	// column's encoding never gets here - a skip is a skip whatever the block's layout.
	if !encodingSupported(col) {
		r.err = fmt.Errorf(
			"tabbit: %s: the file's column uses encoding %d, which this reader cannot "+
				"decode for its element type; regenerate the code or rebuild the data",
			fieldName, col.Encoding)
		return false
	}

	for _, e := range accepted {
		if col.Element == e {
			return true
		}
	}

	r.err = fmt.Errorf(
		"tabbit: %s: the file carries element type %d, which this member cannot read "+
			"(accepts %v); the column changed type incompatibly, regenerate the code or "+
			"rebuild the data", fieldName, col.Element, accepted)
	return false
}

// encodingSupported reports whether (element, encoding) is a pair the spec defines.
// Integers take the integer encodings, strings the dictionary ones, and an array takes
// the composition that applies all of those to its elements.
func encodingSupported(col Column) bool {
	if col.Encoding == EncodingRaw {
		return true
	}

	// An array's block says what its elements use, and the element encoding is checked
	// as it is read rather than here - the descriptor carries only the outer one, so
	// this is as far as the descriptor can be checked.
	if col.Kind != KindScalar {
		return col.Encoding == EncodingArray
	}

	switch col.Element {
	case ElementBool, ElementVarint:
		return col.Encoding == EncodingRle || col.Encoding == EncodingBitpack

	case ElementI32:
		return (col.Encoding >= EncodingVarint && col.Encoding <= EncodingDeltaRle) ||
			col.Encoding == EncodingBitpack

	// The dictionary is parameterized by element, so these reach it with entries that
	// are simply their own raw bytes.
	case ElementI64:
		return col.Encoding == EncodingDict || col.Encoding == EncodingDictRle ||
			col.Encoding == EncodingBitpack

	// A float column additionally reaches the integer encodings, through the block
	// that says its values are whole numbers.
	case ElementF32, ElementF64:
		return col.Encoding == EncodingDict || col.Encoding == EncodingDictRle ||
			col.Encoding == EncodingWhole

	// And a string dictionary can be front coded or built from segments, both of which
	// are meaningless for a fixed-width element and refused for one.
	case ElementString:
		return (col.Encoding >= EncodingDict && col.Encoding <= EncodingDictFrontRle) ||
			col.Encoding == EncodingDictSegment || col.Encoding == EncodingDictSegmentRle

	default:
		return false
	}
}

// ColumnCursor reads one scalar column's values in row order, whatever the block's
// encoding.
//
// The generated row loop stays a row loop; this is the one place that knows how a
// delta accumulates, how long a run has left, or that a dictionary index is a
// reference into strings decoded once. That last one matters beyond file size: a
// hundred-thousand-row column with three distinct strings allocates three strings,
// not a hundred thousand.
//
// CheckColumn has already refused any (element, encoding) pair the spec does not
// define, so the switches here do not re-litigate that. Failures land in the
// reader's sticky error, like every other read.
type ColumnCursor struct {
	reader    *Reader
	fieldName string
	element   uint8
	encoding  uint8

	// The block's dictionary, decoded once and handed out per row. Which of the two
	// is filled in depends on the element: a string block decodes to instances that
	// rows then share, and a fixed-width one keeps the entries' own bytes so a value
	// is reconstructed exactly as the raw layout would have read it.
	dictionary []string

	// valueDictionary holds the fixed-width entries end to end, valueWidth bytes
	// apiece. valueWidth is zero when the block has no dictionary of that kind, and
	// is what the reads test rather than the slice, which is non-nil even empty.
	valueDictionary []byte
	valueWidth      int

	// A run-length family's current run: what remains of it, and its value - which
	// is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
	runRemaining int32
	runValue     int32

	// The delta family's accumulator, once started.
	previous int32
	started  bool

	// Rows not yet handed out. A run that claims more than this is corrupt, and
	// catching it here names the field instead of leaving it to the block-end check.
	// For an array column this counts elements, not rows.
	rowsRemaining int32

	// How many elements each row holds, decoded up front for an encoded array column.
	//
	// Up front because the element stream follows the length stream in the block, so
	// every length has been read by the time the first element is. Nil for a raw array,
	// whose lengths are interleaved with its elements and read as they are reached.
	lengths  []int32
	lengthAt int

	// Whether a float column's values are travelling as integers.
	wholeNumbers bool

	// A bit-packed column's bytes, decoded up front, and where in them the next value
	// is. Up front because the bytes are themselves under an encoding and a value can
	// cross a byte boundary, so handing values out one at a time would mean carrying a
	// decoder and a bit offset that disagree about where they are.
	packed      []byte
	packedWidth int
	packedBase  int64
	packedBit   int
}

// NewColumnCursor opens a column at the head of its block, after its CheckColumn
// passed. A dictionary encoding decodes its dictionary here, once.
func NewColumnCursor(r *Reader, column Column, rowCount int32, fieldName string) *ColumnCursor {
	c := &ColumnCursor{
		reader:        r,
		fieldName:     fieldName,
		element:       column.Element,
		encoding:      column.Encoding,
		rowsRemaining: rowCount,
	}

	// An array column's block names an encoding for its elements and, where its rows
	// differ in length, one for the lengths. Both are encodings that already exist, so
	// all this does is read them and then go on being the element stream's cursor.
	if c.encoding == EncodingArray {
		c.encoding = r.ReadUint8()

		if column.Kind == KindVarArray {
			lengthEncoding := r.ReadUint8()
			c.readLengths(lengthEncoding, rowCount)

			elements := int64(0)
			for _, length := range c.lengths {
				elements += int64(length)
			}

			if elements > math.MaxInt32 {
				r.err = fmt.Errorf(
					"tabbit: %s: the column declares more elements than can be held", fieldName)
				return c
			}

			c.rowsRemaining = int32(elements)
		} else {
			c.rowsRemaining = rowCount * column.Count
		}
	}

	// A bit-packed column states the width its range needs, the base subtracted from
	// every value, and which encoding carries the packed bytes. Decoded here so that
	// handing values out is a shift and an add.
	if c.encoding == EncodingBitpack {
		width := r.ReadUint8()
		base := r.ReadCounter64()
		inner := r.ReadUint8()

		if width < 1 || width > 64 {
			if r.err == nil {
				r.err = fmt.Errorf(
					"tabbit: %s: a bit width of %d is not between 1 and 64", fieldName, width)
			}

			return c
		}

		c.packedWidth = int(width)
		c.packedBase = base

		bits := int64(c.rowsRemaining) * int64(width)
		if (bits+7)/8 > math.MaxInt32 {
			r.err = fmt.Errorf(
				"tabbit: %s: the packed stream is larger than can be held", fieldName)
			return c
		}

		c.packed = r.ReadByteStream(inner, int((bits+7)/8), fieldName)

		return c
	}

	// A float column whose values are all whole numbers carries them as integers and
	// says which integer encoding they travel under. From here down it is that
	// encoding's cursor, and only the handing out converts back.
	if c.encoding == EncodingWhole {
		inner := r.ReadUint8()

		if inner < EncodingVarint || inner > EncodingDeltaRle {
			if r.err == nil {
				r.err = fmt.Errorf(
					"tabbit: %s: encoding %d cannot carry a whole-number column's values",
					fieldName, inner)
			}

			return c
		}

		c.encoding = inner
		c.wholeNumbers = true
	}

	// A segment dictionary is built once, here, and from then on the block is a
	// dictionary with an index stream like any other - so the row-by-row paths below
	// need to know nothing about it.
	if c.encoding == EncodingDictSegment || c.encoding == EncodingDictSegmentRle {
		c.readSegmentDictionary()

		if c.encoding == EncodingDictSegment {
			c.encoding = EncodingDict
		} else {
			c.encoding = EncodingDictRle
		}

		return c
	}

	plainDictionary := c.encoding == EncodingDict || c.encoding == EncodingDictRle
	frontDictionary := c.encoding == EncodingDictFront ||
		c.encoding == EncodingDictFrontRle

	if !plainDictionary && !frontDictionary {
		return c
	}

	count := r.ReadCounter32()
	if count < 0 && r.err == nil {
		r.err = fmt.Errorf(
			"tabbit: %s: the dictionary entry count is negative", fieldName)
	}

	if r.err != nil {
		return c
	}

	if frontDictionary {
		c.readFrontCodedDictionary(count)
		return c
	}

	if c.element == ElementString {
		c.dictionary = make([]string, count)

		for at := range c.dictionary {
			c.dictionary[at] = r.ReadString()
		}

		return c
	}

	// A fixed-width element: the entries are the value's own bytes, so they are taken
	// as bytes and turned into values only when a row asks for one.
	c.valueWidth = 8
	if c.element == ElementF32 {
		c.valueWidth = 4
	}

	c.valueDictionary = make([]byte, int(count)*c.valueWidth)

	for at := 0; at < int(count); at++ {
		entry := r.take(c.valueWidth)
		if entry == nil {
			return c
		}

		copy(c.valueDictionary[at*c.valueWidth:], entry)
	}

	return c
}

// readFrontCodedDictionary decodes a sorted dictionary whose entries state only what
// they do not share with the entry before them.
//
// Decoded into whole strings here rather than kept folded, because a row wants a string
// and the folding was only ever about the bytes on disk. The scratch buffer grows to the
// longest entry and is reused, so the allocations are the strings themselves - one per
// distinct value, which is the point.
func (c *ColumnCursor) readFrontCodedDictionary(count int32) {
	r := c.reader

	entries := make([]string, count)
	scratch := make([]byte, 0, 64)
	previousLength := 0

	for at := range entries {
		shared := int(r.ReadCounter32())
		rest := int(r.ReadCounter32())
		if r.err != nil {
			return
		}

		if shared < 0 || rest < 0 || shared > previousLength {
			r.err = fmt.Errorf(
				"tabbit: %s: dictionary entry %d shares %d bytes with an entry of %d",
				c.fieldName, at, shared, previousLength)
			return
		}

		tail := r.take(rest)
		if r.err != nil {
			return
		}

		// The shared prefix is already at the head of the scratch buffer, being what
		// the previous entry left there.
		scratch = append(scratch[:shared], tail...)

		entries[at] = string(scratch)
		previousLength = len(scratch)
	}

	c.dictionary = entries
}

// NextLength returns how many elements the next row of an array column holds.
//
// One call whichever way the block is laid out. An encoded array decoded every length
// before the first element was read, so this hands out what it already has; a raw one
// states each row's length in front of that row's elements, so this reads it where it
// stands.
func (c *ColumnCursor) NextLength() int32 {
	if c.reader.err != nil {
		return 0
	}

	if c.lengths != nil {
		if c.lengthAt >= len(c.lengths) {
			c.reader.err = fmt.Errorf(
				"tabbit: %s: the column has no more rows to read", c.fieldName)
			return 0
		}

		length := c.lengths[c.lengthAt]
		c.lengthAt++

		return length
	}

	length := c.reader.ReadCounter32()
	if c.reader.err != nil {
		return 0
	}

	if length < 0 {
		c.reader.err = fmt.Errorf(
			"tabbit: %s: a row declares %d elements", c.fieldName, length)
		return 0
	}

	return length
}

// readLengths decodes an array column's row lengths, which are their own stream at the
// front of the block.
//
// A varint stream, so what may be chosen for it is what may be chosen for any varint
// column - each length as a counter32, or runs of them. Most columns have rows that are
// all the same length, which is one run.
func (c *ColumnCursor) readLengths(encoding uint8, rowCount int32) {
	r := c.reader
	lengths := make([]int32, rowCount)

	if encoding == EncodingRaw {
		for at := range lengths {
			lengths[at] = r.ReadCounter32()
			if r.err != nil {
				return
			}

			if lengths[at] < 0 {
				r.err = fmt.Errorf(
					"tabbit: %s: row %d declares %d elements", c.fieldName, at, lengths[at])
				return
			}
		}

		c.lengths = lengths
		return
	}

	if encoding != EncodingRle {
		if r.err == nil {
			r.err = fmt.Errorf(
				"tabbit: %s: encoding %d cannot carry an array column's row lengths",
				c.fieldName, encoding)
		}

		return
	}

	filled := int32(0)

	for filled < rowCount {
		run := r.ReadCounter32()
		value := r.ReadOptimalInt32()
		if r.err != nil {
			return
		}

		if run < 1 || run > rowCount-filled {
			r.err = fmt.Errorf(
				"tabbit: %s: a run of %d lengths cannot cover the %d rows left in the column",
				c.fieldName, run, rowCount-filled)
			return
		}

		if value < 0 {
			r.err = fmt.Errorf("tabbit: %s: a row declares %d elements", c.fieldName, value)
			return
		}

		for at := int32(0); at < run; at++ {
			lengths[filled] = value
			filled++
		}
	}

	c.lengths = lengths
}

// readSegmentDictionary decodes a dictionary whose entries are lists of references into
// a table of the pieces they are built from.
//
// Two reads and a concatenation: the table, which is front coded because its own entries
// share their fronts, and then each value as the pieces it is made of. The result is the
// same slice of whole strings every other dictionary produces, so nothing downstream of
// here knows which kind it came from.
func (c *ColumnCursor) readSegmentDictionary() {
	r := c.reader

	segmentCount := r.ReadCounter32()
	if r.err != nil {
		return
	}

	if segmentCount < 0 {
		r.err = fmt.Errorf("tabbit: %s: the segment count is negative", c.fieldName)
		return
	}

	if int(segmentCount) > r.Remaining() {
		r.err = fmt.Errorf(
			"tabbit: %s: a segment table of %d entries is larger than the file can hold",
			c.fieldName, segmentCount)
		return
	}

	segments := make([][]byte, segmentCount)
	previousLength := 0

	for at := range segments {
		shared := int(r.ReadCounter32())
		rest := int(r.ReadCounter32())
		if r.err != nil {
			return
		}

		if shared < 0 || rest < 0 || shared > previousLength {
			r.err = fmt.Errorf(
				"tabbit: %s: segment %d shares %d bytes with an entry of %d",
				c.fieldName, at, shared, previousLength)
			return
		}

		tail := r.take(rest)
		if r.err != nil {
			return
		}

		segment := make([]byte, shared+rest)

		// A shared prefix is the previous segment's, and there is one only where a
		// previous segment exists: the check above refused any share against the empty
		// history the first entry has.
		if shared > 0 {
			copy(segment, segments[at-1][:shared])
		}

		copy(segment[shared:], tail)

		segments[at] = segment
		previousLength = len(segment)
	}

	count := r.ReadCounter32()
	if r.err != nil {
		return
	}

	if count < 0 {
		r.err = fmt.Errorf("tabbit: %s: the dictionary entry count is negative", c.fieldName)
		return
	}

	if int(count) > r.Remaining() {
		r.err = fmt.Errorf(
			"tabbit: %s: a dictionary of %d entries is larger than the file can hold",
			c.fieldName, count)
		return
	}

	entries := make([]string, count)
	scratch := make([]byte, 0, 64)

	for at := range entries {
		pieces := r.ReadCounter32()
		if r.err != nil {
			return
		}

		if pieces < 0 {
			r.err = fmt.Errorf(
				"tabbit: %s: dictionary entry %d declares %d pieces", c.fieldName, at, pieces)
			return
		}

		scratch = scratch[:0]

		for piece := int32(0); piece < pieces; piece++ {
			index := r.ReadCounter32()
			if r.err != nil {
				return
			}

			if index < 0 || index >= segmentCount {
				r.err = fmt.Errorf(
					"tabbit: %s: segment index %d is out of range - the table holds %d entries",
					c.fieldName, index, segmentCount)
				return
			}

			scratch = append(scratch, segments[index]...)
		}

		entries[at] = string(scratch)
	}

	c.dictionary = entries
}

// NextI32 returns the next int32 - which also serves enums, and reference indexes.
// nextPacked returns the next value of a bit-packed stream: the packed bits, over the
// block's base.
//
// A value may cross a byte boundary, so this walks bits rather than bytes. The addition
// wraps, mirroring the writer's wrapping subtraction.
func (c *ColumnCursor) nextPacked() int64 {
	var slot uint64

	for at := 0; at < c.packedWidth; at, c.packedBit = at+1, c.packedBit+1 {
		if c.packed[c.packedBit>>3]>>(uint(c.packedBit)&7)&1 != 0 {
			slot |= 1 << uint(at)
		}
	}

	return c.packedBase + int64(slot)
}

func (c *ColumnCursor) NextI32() int32 {
	if c.reader.err != nil {
		return 0
	}

	c.rowsRemaining--

	if c.encoding == EncodingBitpack {
		return int32(c.nextPacked())
	}

	switch c.encoding {
	case EncodingRaw:
		if c.element == ElementI32 {
			return c.reader.ReadInt32()
		}
		return c.reader.ReadOptimalInt32()

	case EncodingVarint:
		return c.reader.ReadOptimalInt32()

	case EncodingDelta:
		// The addition wraps on purpose - Go's int32 arithmetic is two's complement -
		// mirroring the writer's wrapping subtraction; together they are exact for
		// every int32 pair.
		if c.started {
			c.previous += c.reader.ReadOptimalInt32()
		} else {
			c.previous = c.reader.ReadOptimalInt32()
			c.started = true
		}
		return c.previous

	case EncodingRle:
		if c.runRemaining == 0 && !c.readRun() {
			return 0
		}

		c.runRemaining--
		return c.runValue

	default: // EncodingDeltaRle; CheckColumn refused everything else.
		if !c.started {
			c.previous = c.reader.ReadOptimalInt32()
			c.started = true
			return c.previous
		}

		if c.runRemaining == 0 && !c.readRun() {
			return 0
		}

		c.runRemaining--
		c.previous += c.runValue
		return c.previous
	}
}

// NextI64 reads an int64 member: from an i64 column raw or through its dictionary,
// and from anything narrower by decoding an int32 and widening it.
func (c *ColumnCursor) NextI64() int64 {
	if c.element != ElementI64 {
		return int64(c.NextI32())
	}

	if c.reader.err != nil {
		return 0
	}

	if c.encoding == EncodingBitpack {
		c.rowsRemaining--
		return c.nextPacked()
	}

	if c.valueWidth != 0 {
		entry := c.nextValueEntry()
		if entry == nil {
			return 0
		}

		return int64(binary.LittleEndian.Uint64(entry))
	}

	c.rowsRemaining--
	return c.reader.ReadInt64()
}

// NextF32 reads a float32 member: raw, the dictionary entry's exact bit pattern, or a
// whole number.
func (c *ColumnCursor) NextF32() float32 {
	if c.wholeNumbers {
		return float32(c.NextI32())
	}

	if c.reader.err != nil {
		return 0
	}

	if c.valueWidth != 0 {
		entry := c.nextValueEntry()
		if entry == nil {
			return 0
		}

		return math.Float32frombits(binary.LittleEndian.Uint32(entry))
	}

	c.rowsRemaining--
	return c.reader.ReadFloat32()
}

// NextF64 reads a float64 member: from f64 or f32 - either of them raw or
// dictionary-encoded - and from an i32 column by decoding and widening.
func (c *ColumnCursor) NextF64() float64 {
	if c.wholeNumbers {
		return float64(c.NextI32())
	}

	switch c.element {
	case ElementF64:
		if c.reader.err != nil {
			return 0
		}

		if c.valueWidth != 0 {
			entry := c.nextValueEntry()
			if entry == nil {
				return 0
			}

			return math.Float64frombits(binary.LittleEndian.Uint64(entry))
		}

		c.rowsRemaining--
		return c.reader.ReadFloat64()

	case ElementF32:
		return float64(c.NextF32())

	default:
		return float64(c.NextI32())
	}
}

// NextBool reads a bool member: one byte raw, or a run of them.
func (c *ColumnCursor) NextBool() bool {
	if c.encoding == EncodingRle || c.encoding == EncodingBitpack {
		return c.NextI32() != 0
	}

	if c.reader.err != nil {
		return false
	}

	c.rowsRemaining--
	return c.reader.ReadBool()
}

// nextValueEntry returns the bytes of the next row's dictionary entry, for a
// fixed-width element, or nil once the reader has failed.
func (c *ColumnCursor) nextValueEntry() []byte {
	if c.reader.err != nil {
		return nil
	}

	c.rowsRemaining--

	var index int32

	if c.encoding == EncodingDict {
		index = c.reader.ReadCounter32()
		if c.reader.err != nil {
			return nil
		}
	} else {
		if c.runRemaining == 0 && !c.readRun() {
			return nil
		}

		c.runRemaining--
		index = c.runValue
	}

	count := int32(len(c.valueDictionary) / c.valueWidth)

	if index < 0 || index >= count {
		c.reader.err = fmt.Errorf(
			"tabbit: %s: dictionary index %d is out of range - the dictionary holds %d entries",
			c.fieldName, index, count)
		return nil
	}

	at := int(index) * c.valueWidth

	return c.valueDictionary[at : at+c.valueWidth]
}

// NextString returns the next string - the dictionary's instance where the block
// has one.
func (c *ColumnCursor) NextString() string {
	if c.reader.err != nil {
		return ""
	}

	c.rowsRemaining--

	switch c.encoding {
	case EncodingRaw:
		return c.reader.ReadString()

	case EncodingDict, EncodingDictFront:
		return c.dictionaryEntry(c.reader.ReadCounter32())

	default: // EncodingDictRle and EncodingDictFrontRle
		if c.runRemaining == 0 && !c.readRun() {
			return ""
		}

		c.runRemaining--
		return c.dictionaryEntry(c.runValue)
	}
}

// NextSameI32 returns up to limit rows that all hold the next value, and that value.
// The count is always at least 1.
//
// This is what makes a run cost one call instead of one per row: the generated loop
// asks once, then assigns the value that many times. An encoding that cannot promise
// sameness cheaply answers 1, so the caller's loop is correct over every encoding and
// only faster over runs.
func (c *ColumnCursor) NextSameI32(limit int32) (int32, int32) {
	if c.reader.err != nil {
		return 1, 0
	}

	if c.encoding == EncodingRle {
		c.rowsRemaining--
		if c.runRemaining == 0 && !c.readRun() {
			return 1, 0
		}

		n := c.runRemaining
		if n > limit {
			n = limit
		}

		c.runRemaining -= n
		c.rowsRemaining -= n - 1

		return n, c.runValue
	}

	if c.encoding == EncodingDeltaRle && c.started {
		c.rowsRemaining--
		if c.runRemaining == 0 && !c.readRun() {
			return 1, 0
		}

		if c.runValue == 0 {
			// A zero-delta run is a run of one value.
			n := c.runRemaining
			if n > limit {
				n = limit
			}

			c.runRemaining -= n
			c.rowsRemaining -= n - 1

			return n, c.previous
		}

		c.runRemaining--
		c.previous += c.runValue

		return 1, c.previous
	}

	return 1, c.NextI32()
}

// NextSameString is the string counterpart of NextSameI32.
func (c *ColumnCursor) NextSameString(limit int32) (int32, string) {
	if c.reader.err != nil {
		return 1, ""
	}

	if c.encoding == EncodingDictRle || c.encoding == EncodingDictFrontRle {
		c.rowsRemaining--
		if c.runRemaining == 0 && !c.readRun() {
			return 1, ""
		}

		n := c.runRemaining
		if n > limit {
			n = limit
		}

		c.runRemaining -= n
		c.rowsRemaining -= n - 1

		return n, c.dictionaryEntry(c.runValue)
	}

	return 1, c.NextString()
}

func (c *ColumnCursor) readRun() bool {
	length := c.reader.ReadCounter32()
	if c.reader.err != nil {
		return false
	}

	// + 1 because the row this run was read for is already counted out of
	// rowsRemaining by its Next call.
	if length < 1 || length > c.rowsRemaining+1 {
		c.reader.err = fmt.Errorf(
			"tabbit: %s: a run of %d values cannot cover the %d rows left in the column",
			c.fieldName, length, c.rowsRemaining+1)
		return false
	}

	c.runRemaining = length
	c.runValue = c.reader.ReadOptimalInt32()

	return c.reader.err == nil
}

func (c *ColumnCursor) dictionaryEntry(index int32) string {
	if c.reader.err != nil {
		return ""
	}

	if index < 0 || int(index) >= len(c.dictionary) {
		c.reader.err = fmt.Errorf(
			"tabbit: %s: dictionary index %d is out of range - the dictionary holds %d entries",
			c.fieldName, index, len(c.dictionary))
		return ""
	}

	return c.dictionary[index]
}

// CheckBlockEnd verifies a block was consumed exactly: a mismatch is a format
// disagreement, and stopping here names the column instead of corrupting the next.
func CheckBlockEnd(r *Reader, col Column, expectedEnd int) {
	if r.err == nil && r.Position() != expectedEnd {
		r.err = fmt.Errorf(
			"tabbit: column tag %d: its block declared %d bytes but the read ended %d bytes "+
				"short of its boundary", col.Tag, col.ByteLength, expectedEnd-r.Position())
	}
}

// ReadAllBytes reads a whole file into memory.
func ReadAllBytes(filename string) ([]byte, error) { return os.ReadFile(filename) }

// The ChaCha20 stream cipher of RFC 8439, as the file envelope uses it.
//
// Here rather than from a package because the ones on offer are authenticated
// constructions, which change the length. This format wants a plain keystream: applying
// it leaves every byte count as it was, so the structural checks - the block lengths
// that must sum exactly - hold over the ciphertext unchanged.
//
// Under two hundred lines with no dependency, which is what lets the same cipher exist
// in every runtime that has to read one of these files.

// chacha20Apply exclusive-ors the keystream over data, in place.
//
// One routine for both directions, which is what a stream cipher is: the keystream
// depends only on the key, the nonce and the position, so applying it twice returns what
// went in. The block counter starts at zero.
func chacha20Apply(key []byte, nonce []byte, data []byte) {
	var state [16]uint32
	var working [16]uint32
	var keystream [64]byte

	// "expand 32-byte k", as four little-endian words.
	state[0] = 0x61707865
	state[1] = 0x3320646e
	state[2] = 0x79622d32
	state[3] = 0x6b206574

	for at := 0; at < 8; at++ {
		state[4+at] = binary.LittleEndian.Uint32(key[at*4:])
	}

	state[12] = 0

	for at := 0; at < 3; at++ {
		state[13+at] = binary.LittleEndian.Uint32(nonce[at*4:])
	}

	for offset := 0; offset < len(data); offset += 64 {
		chacha20Block(&state, &working, &keystream)

		count := len(data) - offset
		if count > 64 {
			count = 64
		}

		for at := 0; at < count; at++ {
			data[offset+at] ^= keystream[at]
		}

		state[12]++
	}
}

// chacha20Block produces one 64-byte keystream block: twenty rounds over a copy of the
// state.
func chacha20Block(state *[16]uint32, working *[16]uint32, keystream *[64]byte) {
	*working = *state

	// Ten double rounds. Each is four column quarter-rounds and four diagonal ones,
	// which between them let every word reach every other.
	for round := 0; round < 10; round++ {
		chacha20QuarterRound(working, 0, 4, 8, 12)
		chacha20QuarterRound(working, 1, 5, 9, 13)
		chacha20QuarterRound(working, 2, 6, 10, 14)
		chacha20QuarterRound(working, 3, 7, 11, 15)

		chacha20QuarterRound(working, 0, 5, 10, 15)
		chacha20QuarterRound(working, 1, 6, 11, 12)
		chacha20QuarterRound(working, 2, 7, 8, 13)
		chacha20QuarterRound(working, 3, 4, 9, 14)
	}

	// Added back to the state it started from, which is what stops the rounds being
	// reversible and so the keystream being recoverable.
	for at := 0; at < 16; at++ {
		binary.LittleEndian.PutUint32(keystream[at*4:], working[at]+state[at])
	}
}

func chacha20QuarterRound(block *[16]uint32, a, b, c, d int) {
	block[a] += block[b]
	block[d] = bits.RotateLeft32(block[d]^block[a], 16)
	block[c] += block[d]
	block[b] = bits.RotateLeft32(block[b]^block[c], 12)
	block[a] += block[b]
	block[d] = bits.RotateLeft32(block[d]^block[a], 8)
	block[c] += block[d]
	block[b] = bits.RotateLeft32(block[b]^block[c], 7)
}
