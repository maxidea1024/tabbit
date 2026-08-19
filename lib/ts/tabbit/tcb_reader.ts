// ---------------------------------------------------------------------------
// Tabbit Tcb reader for TypeScript.
//
// Reads the .tcb files produced by Tabbit's binary exporter. The format is
// defined by the C# writer in
// lib/Unity/TabbitForUnity/Assets/Plugins/Tabbit.Runtime, and this is the
// third implementation of the reading half of it, alongside the C# original and
// lib/cpp/tabbit/tcb_reader.h:
//
//   fixed8      one byte
//   fixed32     four bytes, little endian
//   fixed64     eight bytes, little endian
//   varint32    seven bits per byte, high bit set while more bytes follow,
//               at most five bytes
//   counter32   zig-zag encoded int32 written as a varint32
//   string      counter32 byte length, then that many UTF-8 bytes
//   int32          fixed32
//   int64          fixed64
//   bool           fixed8, zero meaning false
//   float/double   fixed32 / fixed64 holding the IEEE-754 bit pattern
//   datetime       fixed64 of .NET ticks: 100 ns units since 0001-01-01
//   timespan       fixed64 of .NET ticks
//   uuid           sixteen bytes in .NET Guid layout
//
// Values are surfaced exactly as the JSON export renders them, so a generated
// table reads the same whichever source it was loaded from. That is why dates
// and durations come back as strings rather than ticks: the JSON export writes
// them that way, and one API is worth more than a marginally richer type on one
// of the two paths.
//
// No dependencies. Works over a Uint8Array, so it runs in Node and in a browser
// alike; only the convenience file read needs `fs`.
// ---------------------------------------------------------------------------

/** Thrown when a table file is truncated, malformed, or not a table file. */
export class TcbError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'TcbError'
  }
}

/**
 * Version stamped at the head of every table file by the exporter. 102 replaced 101
 * outright - a descriptor gained its encoding byte - before any 101 file had shipped.
 * 104 is the current one: four encodings joined the nine, and the flags byte gained a
 * meaning.
 */
export const BINARY_FILE_FORMAT_VERSION = 106

// The wire's element types and kinds, as a column descriptor spells them.
export const ELEMENT_VARINT = 0
export const ELEMENT_BOOL = 1
export const ELEMENT_I32 = 2
export const ELEMENT_I64 = 3
export const ELEMENT_F32 = 4
export const ELEMENT_F64 = 5
export const ELEMENT_STRING = 6
export const ELEMENT_UUID = 7

export const KIND_SCALAR = 0
export const KIND_FIXED_ARRAY = 1
export const KIND_VAR_ARRAY = 2

// How a block's values are laid out. Raw is the layout 101 had; the others compress
// a column that repeats itself. spec/tcb-v102-column-encoding.md is the contract.
export const ENCODING_RAW = 0
export const ENCODING_VARINT = 1
export const ENCODING_DELTA = 2
export const ENCODING_RLE = 3
export const ENCODING_DELTA_RLE = 4
export const ENCODING_DICT = 5
export const ENCODING_DICT_RLE = 6
export const ENCODING_DICT_FRONT = 7
export const ENCODING_DICT_FRONT_RLE = 8

// Composition rather than layout. An array block names an encoding for its elements and
// one for its rows' lengths, and a whole-number float block names the integer encoding its
// values travel under - so both are decoded by the cursors that already exist, one level
// down, and neither adds a decode step anywhere.
export const ENCODING_ARRAY = 9
export const ENCODING_WHOLE = 10

// A dictionary whose entries are built from a shared table of the pieces they are made of,
// which reaches what two values share in the middle and at the end where front coding can
// only reach what they share at the front.
export const ENCODING_DICT_SEGMENT = 11
export const ENCODING_DICT_SEGMENT_RLE = 12

/** An integer stream at the width its own range needs, over a base. */
export const ENCODING_BITPACK = 13

// The file header, at fixed offsets whether or not the file is encrypted and whether or not
// it carries a MAC. spec/tcb-mac-and-signature.md.
export const MAGIC_OFFSET = 0
export const VERSION_OFFSET = 4
export const FLAGS_OFFSET = 8
export const CIPHER_OFFSET = 9
export const NONCE_OFFSET = 10
export const MAC_OFFSET = 22
export const KEY_CHECK_OFFSET = 38

/** Where the body begins. The header before it is always this long. */
export const HEADER_SIZE = 42

export const NONCE_SIZE = 12
export const MAC_SIZE = 16

/**
 * The signature, as the fixed32 it is on disk: 'S' 'C' 'B' 0, little endian.
 *
 * The same four bytes serve twice. At offset zero they are the file format signature, in
 * the clear whether or not the file is encrypted. At the key check they are under the key,
 * so a file that decrypts to something else was written with a different key - which is the
 * one thing no structural check can tell from damage.
 */
export const MAGIC = 0x00424354

/** Bit 0 of the flags byte: from the key check on, the file is ciphertext. */
export const FLAG_ENCRYPTED = 0x01

/** The cipher byte of a file that is not encrypted. */
export const CIPHER_NONE = 0

/** The only cipher the format defines. */
export const CIPHER_CHACHA20 = 1

/** One column as the file describes it. */
export interface TcbColumn {
  /** What identifies the column, instead of its position. */
  tag: number
  element: number
  kind: number
  /**
   * Whether the block begins with one presence bit per row, low bit first.
   *
   * Set only where the sheet marked the column optional. The values are still written for
   * every row - a row without one carries the type's empty value - so the bitmap says
   * which of those to believe and nothing about the layout after it.
   */
  nullable: boolean
  /** How the block's values are laid out: one of the ENCODING_* constants. */
  encoding: number
  /** Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one. */
  count: number
  /** Total bytes of the column's block - what a skip advances by. */
  byteLength: number
}

/** Ticks between 0001-01-01 and the Unix epoch. */
const UNIX_EPOCH_TICKS = 621355968000000000n

/** .NET ticks per second. A tick is 100 ns. */
const TICKS_PER_SECOND = 10000000n

const TICKS_PER_DAY = 864000000000n
const TICKS_PER_HOUR = 36000000000n
const TICKS_PER_MINUTE = 600000000n

/** Sequential reader over a table file's bytes. */
export class TcbReader {
  private readonly data: Uint8Array
  private readonly view: DataView
  private offset = 0

  constructor(data: Uint8Array) {
    this.data = data
    this.view = new DataView(data.buffer, data.byteOffset, data.byteLength)
  }

  get position(): number { return this.offset }

  /**
   * Advances past bytes without interpreting them: an unknown column's whole block.
   * The column-oriented layout is what makes this one call the entirety of skipping.
   */
  skip(byteCount: number): void {
    if (byteCount < 0 || byteCount > this.remaining) {
      throw new TcbError(`cannot skip ${byteCount} bytes with ${this.remaining} remaining`)
    }
    this.offset += byteCount
  }

  // Promotions: a member reading a file element narrower than itself. Only the
  // mathematically lossless directions exist; the column check already refused the rest.

  /** An int32 member from i32 or varint. */
  readI32As(element: number): number {
    return element === ELEMENT_I32 ? this.readInt32() : this.readCounter32()
  }

  /** An int64 member from i64, i32 or varint. Always a bigint, as int64 is here. */
  readI64As(element: number): bigint {
    if (element === ELEMENT_I64) return this.readInt64()
    if (element === ELEMENT_I32) return BigInt(this.readInt32())
    return BigInt(this.readCounter32())
  }

  /** A double member from f64, f32 or i32 - all exact in a double. */
  readF64As(element: number): number {
    if (element === ELEMENT_F64) return this.readDouble()
    if (element === ELEMENT_F32) return this.readFloat()
    return this.readInt32()
  }
  get remaining(): number { return this.data.length - this.offset }

  readFixed8(): number {
    this.require(1)
    return this.data[this.offset++]
  }

  readFixed32(): number {
    this.require(4)
    const value = this.view.getUint32(this.offset, true)
    this.offset += 4
    return value
  }

  readFixed64(): bigint {
    this.require(8)
    const value = this.view.getBigUint64(this.offset, true)
    this.offset += 8
    return value
  }

  readVarint32(): number {
    let value = 0

    for (let shift = 0; shift < 35; shift += 7) {
      const byte = this.readFixed8()

      // Shifting with `<<` is a 32-bit signed operation in JS, so the top
      // bits of a five-byte varint would land in the sign. Multiplying keeps
      // the arithmetic in the double range, where 32 bits fit exactly.
      value += (byte & 0x7f) * Math.pow(2, shift)

      if ((byte & 0x80) === 0) return value
    }

    throw new TcbError('varint32 is longer than five bytes')
  }

  /**
   * Zig-zag decoded int32: the encoding used for lengths and enum values, so
   * small negatives cost as little as small positives.
   */
  readCounter32(): number {
    const encoded = this.readVarint32()

    // `>>> 1` then a conditional negate, rather than the usual xor: the xor
    // form relies on 32-bit two's complement, which JS bitwise operators
    // provide only for values that already fit in a signed 32-bit int.
    const magnitude = Math.floor(encoded / 2)
    return (encoded & 1) === 1 ? -(magnitude + 1) : magnitude
  }

  readBool(): boolean {
    return this.readFixed8() !== 0
  }

  readInt32(): number {
    this.require(4)
    const value = this.view.getInt32(this.offset, true)
    this.offset += 4
    return value
  }

  /**
   * A 64-bit integer, as a BigInt.
   *
   * Not a `number`: a double holds only 53 bits of mantissa, so anything past
   * 2^53 comes back quietly wrong - which is exactly the class of corruption
   * the writer itself once had.
   */
  readInt64(): bigint {
    this.require(8)
    const value = this.view.getBigInt64(this.offset, true)
    this.offset += 8
    return value
  }

  readFloat(): number {
    this.require(4)
    const value = this.view.getFloat32(this.offset, true)
    this.offset += 4
    return value
  }

  readDouble(): number {
    this.require(8)
    const value = this.view.getFloat64(this.offset, true)
    this.offset += 8
    return value
  }

  /**
   * Advances past bytes and hands them back uninterpreted.
   *
   * A view onto the same buffer rather than a copy, so a dictionary of fixed-width
   * entries costs nothing to keep: the bytes are already in memory and nothing
   * mutates them.
   */
  /**
   * An int64 written in as few bytes as its magnitude needed, either sign.
   *
   * The base of a bit-packed block, which is a value of the column's own element type -
   * an i64 column's base does not fit in thirty-two bits. One byte when it is zero.
   */
  readCounter64(): bigint {
    let encoded = 0n
    let shift = 0n

    for (;;) {
      const byte = this.readFixed8()
      encoded |= BigInt(byte & 0x7f) << shift

      if ((byte & 0x80) === 0) break

      shift += 7n
      if (shift > 63n)
        throw new TcbError('a 64-bit variable length integer runs past ten bytes')
    }

    return BigInt.asIntN(64, (encoded >> 1n) ^ -(encoded & 1n))
  }

  /**
   * A stream of bytes under one of the integer encodings, which is what a packed block and
   * a presence bitmap both end in.
   *
   * One reader for both, so a bitmap and a packed value block cannot disagree about the
   * same bits. The count is known before the call in both cases, so nothing here reads a
   * length.
   */
  readByteStream(encoding: number, count: number, fieldName: string): Uint8Array {
    if (encoding === ENCODING_RAW) return this.readBytes(count)

    if (encoding > ENCODING_DELTA_RLE)
      throw new TcbError(`${fieldName}: encoding ${encoding} cannot carry a packed byte stream`)

    const bytes = new Uint8Array(count)
    const walking = encoding === ENCODING_DELTA || encoding === ENCODING_DELTA_RLE

    let filled = 0
    let previous = 0

    // The first value of a delta stream is written outright; the rest are steps from it. A
    // run in a delta stream repeats the step, not the value, so it walks.
    if (count > 0 && walking) {
      previous = asByte(this.readCounter32(), fieldName)
      bytes[filled++] = previous
    }

    while (filled < count) {
      let run = 1
      let step = 0
      let value = 0

      switch (encoding) {
        case ENCODING_VARINT:
          value = asByte(this.readCounter32(), fieldName)
          break

        case ENCODING_DELTA:
          step = this.readCounter32()
          break

        case ENCODING_RLE:
          run = this.readCounter32()
          value = asByte(this.readCounter32(), fieldName)
          break

        default: // ENCODING_DELTA_RLE
          run = this.readCounter32()
          step = this.readCounter32()
          break
      }

      if (run < 1 || run > count - filled)
        throw new TcbError(`${fieldName}: a run of ${run} cannot cover the ${count - filled} bytes left`)

      for (let at = 0; at < run; at++) {
        if (walking) {
          previous = asByte((previous + step) | 0, fieldName)
          bytes[filled++] = previous
        } else {
          bytes[filled++] = value
        }
      }
    }

    return bytes
  }

  readBytes(count: number): Uint8Array {
    if (count < 0) throw new TcbError(`cannot read ${count} bytes`)

    this.require(count)

    const bytes = this.data.subarray(this.offset, this.offset + count)
    this.offset += count

    return bytes
  }

  readString(): string {
    const length = this.readCounter32()
    if (length < 0) throw new TcbError('string length is negative')

    return decodeUtf8(this.readBytes(length))
  }

  /**
   * A date, formatted the way the JSON export writes one, so both read paths
   * of a generated table yield the same string.
   */
  readDateTime(): string {
    return formatDateTimeTicks(this.readFixed64())
  }

  /**
   * A duration, formatted the way the JSON export writes one.
   *
   * Read signed, unlike a date: a duration may be negative.
   */
  readTimeSpan(): string {
    return formatTimeSpanTicks(this.readInt64())
  }

  /** A uuid in its canonical text form. */
  readUuid(): string {
    return formatUuid(this.readBytes(16))
  }

  /** An enum value, which travels zig-zag encoded rather than fixed width. */
  readEnum(): number {
    return this.readCounter32()
  }

  private require(count: number): void {
    if (this.remaining < count) {
      throw new TcbError(
        `table data ended after ${this.offset} of ${this.data.length} bytes ` +
        `while ${count} more were expected`)
    }
  }
}

/**
 * A sorted dictionary whose entries state only what they do not share with the
 * entry before them.
 *
 * Decoded into whole strings here rather than kept folded, because a row wants a
 * string and the folding was only ever about the bytes on disk. The scratch buffer
 * grows to the longest entry and is reused, so the allocations are the strings
 * themselves - one per distinct value, which is the point.
 */
function readFrontCodedDictionary(
  reader: TcbReader, count: number, fieldName: string): string[] {
  const entries: string[] = []
  let scratch = new Uint8Array(64)
  let previousLength = 0

  for (let at = 0; at < count; at++) {
    const shared = reader.readCounter32()
    const rest = reader.readCounter32()

    if (shared < 0 || rest < 0 || shared > previousLength) {
      throw new TcbError(
        `${fieldName}: dictionary entry ${at} shares ${shared} bytes with an entry ` +
        `of ${previousLength}`)
    }

    const length = shared + rest

    if (length > scratch.length) {
      let capacity = scratch.length
      while (capacity < length) capacity *= 2

      const grown = new Uint8Array(capacity)
      grown.set(scratch)
      scratch = grown
    }

    if (rest > 0) scratch.set(reader.readBytes(rest), shared)

    entries.push(length === 0 ? '' : decodeUtf8(scratch.subarray(0, length)))
    previousLength = length
  }

  return entries
}

/**
 * The lengths of an array column's rows, as their own encoded stream.
 *
 * A varint stream, so what may be chosen for it is what may be chosen for any varint
 * column - each length as a counter32, or runs of them. Most columns have rows that are
 * all the same length, which is one run.
 */
function readLengths(
  reader: TcbReader, encoding: number, rowCount: number, fieldName: string): number[] {
  const lengths = new Array<number>(rowCount)

  if (encoding === ENCODING_RAW) {
    for (let at = 0; at < rowCount; at++) {
      lengths[at] = reader.readCounter32()

      if (lengths[at] < 0)
        throw new TcbError(`${fieldName}: row ${at} declares ${lengths[at]} elements`)
    }

    return lengths
  }

  if (encoding !== ENCODING_RLE) {
    throw new TcbError(
      `${fieldName}: encoding ${encoding} cannot carry an array column's row lengths`)
  }

  let filled = 0

  while (filled < rowCount) {
    const run = reader.readCounter32()
    const value = reader.readCounter32()

    if (run < 1 || run > rowCount - filled) {
      throw new TcbError(
        `${fieldName}: a run of ${run} lengths cannot cover the ${rowCount - filled} ` +
        'rows left in the column')
    }

    if (value < 0) throw new TcbError(`${fieldName}: a row declares ${value} elements`)

    for (let at = 0; at < run; at++) lengths[filled++] = value
  }

  return lengths
}

/**
 * A dictionary whose entries are lists of references into a table of the pieces they are
 * built from.
 *
 * Two reads and a concatenation: the table, which is front coded because its own entries
 * share their fronts, and then each value as the pieces it is made of. The result is the
 * same array of whole strings every other dictionary produces, so nothing downstream of
 * here knows which kind it came from.
 */
function readSegmentDictionary(reader: TcbReader, fieldName: string): string[] {
  const segmentCount = reader.readCounter32()

  if (segmentCount < 0) throw new TcbError(`${fieldName}: the segment count is negative`)

  if (segmentCount > reader.remaining) {
    throw new TcbError(
      `${fieldName}: a segment table of ${segmentCount} entries is larger than the file can hold`)
  }

  const segments: Uint8Array[] = []
  let previousLength = 0

  for (let at = 0; at < segmentCount; at++) {
    const shared = reader.readCounter32()
    const rest = reader.readCounter32()

    if (shared < 0 || rest < 0 || shared > previousLength) {
      throw new TcbError(
        `${fieldName}: segment ${at} shares ${shared} bytes with an entry of ${previousLength}`)
    }

    const segment = new Uint8Array(shared + rest)

    if (shared > 0) segment.set(segments[at - 1].subarray(0, shared))
    if (rest > 0) segment.set(reader.readBytes(rest), shared)

    segments.push(segment)
    previousLength = segment.length
  }

  const count = reader.readCounter32()

  if (count < 0) throw new TcbError(`${fieldName}: the dictionary entry count is negative`)

  if (count > reader.remaining) {
    throw new TcbError(
      `${fieldName}: a dictionary of ${count} entries is larger than the file can hold`)
  }

  const entries: string[] = []
  let scratch = new Uint8Array(64)

  for (let at = 0; at < count; at++) {
    const pieces = reader.readCounter32()

    if (pieces < 0)
      throw new TcbError(`${fieldName}: dictionary entry ${at} declares ${pieces} pieces`)

    let length = 0

    for (let piece = 0; piece < pieces; piece++) {
      const index = reader.readCounter32()

      if (index < 0 || index >= segmentCount) {
        throw new TcbError(
          `${fieldName}: segment index ${index} is out of range - the table holds ` +
          `${segmentCount} entries`)
      }

      const segment = segments[index]

      if (length + segment.length > scratch.length) {
        let capacity = scratch.length
        while (capacity < length + segment.length) capacity *= 2

        const grown = new Uint8Array(capacity)
        grown.set(scratch)
        scratch = grown
      }

      scratch.set(segment, length)
      length += segment.length
    }

    entries.push(length === 0 ? '' : decodeUtf8(scratch.subarray(0, length)))
  }

  return entries
}

/**
 * Reads one scalar column's values in row order, whatever the block's encoding.
 *
 * The generated row loop stays a row loop; this is the one place that knows how
 * a delta accumulates, how long a run has left, or that a dictionary index is a
 * reference into strings decoded once. That last one matters beyond file size: a
 * hundred-thousand-row column with three distinct strings allocates three strings,
 * not a hundred thousand.
 *
 * checkColumn has already refused any (element, encoding) pair the spec does not
 * define, so the switches here do not re-litigate that.
 */
export class TcbColumnCursor {
  private readonly reader: TcbReader
  private readonly fieldName: string
  private readonly element: number
  private readonly encoding: number

  /**
   * The block's dictionary, decoded once and handed out per row.
   *
   * One of the two is filled when the block has a dictionary at all, chosen by the
   * element: strings are decoded to instances that rows then share, and a
   * fixed-width element keeps its raw bytes so the value is reconstructed exactly
   * as the raw layout would have read it.
   */
  private readonly dictionary: string[] = []

  private readonly valueDictionary: DataView | null = null
  private readonly valueWidth: number = 0

  // A run-length family's current run: what remains of it, and its value - which
  // is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
  private runRemaining = 0
  private runValue = 0

  // The delta family's accumulator, once started.
  private previous = 0
  private started = false

  // Values not yet handed out. A run that claims more than this is corrupt, and
  // catching it here names the field instead of leaving it to the block-end check.
  // For an array column this counts elements, not rows.
  private rowsRemaining: number

  /**
   * How many elements each row holds, decoded up front for an encoded array column.
   *
   * Up front because the element stream follows the length stream in the block, so every
   * length has been read by the time the first element is. Null for a raw array, whose
   * lengths are interleaved with its elements and read as they are reached.
   */
  private readonly lengths: number[] | null = null

  private lengthAt = 0

  /** Whether a float column's values are travelling as integers. */
  private readonly wholeNumbers: boolean = false

  /**
   * A bit-packed column's bytes, decoded up front, and where in them the next value is.
   *
   * Up front because the bytes are themselves under an encoding and a value can cross a
   * byte boundary, so handing values out one at a time would mean carrying a decoder and a
   * bit offset that disagree about where they are.
   */
  private packed: Uint8Array | null = null
  private packedWidth = 0
  private packedBase = 0n
  private packedBit = 0

  constructor(reader: TcbReader, column: TcbColumn, rowCount: number, fieldName: string) {
    this.reader = reader
    this.fieldName = fieldName
    this.element = column.element
    this.encoding = column.encoding
    this.rowsRemaining = rowCount

    // An array column's block names an encoding for its elements and, where its rows
    // differ in length, one for the lengths. Both are encodings that already exist, so all
    // this does is read them and then go on being the element stream's cursor.
    if (this.encoding === ENCODING_ARRAY) {
      this.encoding = reader.readFixed8()

      if (column.kind === KIND_VAR_ARRAY) {
        const lengthEncoding = reader.readFixed8()
        this.lengths = readLengths(reader, lengthEncoding, rowCount, fieldName)

        let elements = 0
        for (const length of this.lengths) elements += length

        // The same ceiling every other runtime holds the count to, so a file that trips
        // this trips it everywhere rather than only where an int is 32 bits wide.
        if (elements > 0x7fffffff)
          throw new TcbError(`${fieldName}: the column declares more elements than can be held`)

        this.rowsRemaining = elements
      } else {
        this.rowsRemaining = rowCount * column.count
      }
    }

    // A bit-packed column states the width its range needs, the base subtracted from every
    // value, and which encoding carries the packed bytes. Decoded here so that handing
    // values out is a shift and an add.
    if (this.encoding === ENCODING_BITPACK) {
      const width = reader.readFixed8()
      const base = reader.readCounter64()
      const inner = reader.readFixed8()

      if (width < 1 || width > 64)
        throw new TcbError(`${fieldName}: a bit width of ${width} is not between 1 and 64`)

      this.packedWidth = width
      this.packedBase = base

      const bits = this.rowsRemaining * width
      const bytes = Math.ceil(bits / 8)

      if (bytes > 0x7fffffff)
        throw new TcbError(`${fieldName}: the packed stream is larger than can be held`)

      this.packed = reader.readByteStream(inner, bytes, fieldName)

      return
    }

    // A float column whose values are all whole numbers carries them as integers and says
    // which integer encoding they travel under. From here down it is that encoding's
    // cursor, and only the handing out converts back.
    if (this.encoding === ENCODING_WHOLE) {
      const inner = reader.readFixed8()

      if (inner < ENCODING_VARINT || inner > ENCODING_DELTA_RLE) {
        throw new TcbError(
          `${fieldName}: encoding ${inner} cannot carry a whole-number column's values`)
      }

      this.encoding = inner
      this.wholeNumbers = true
    }

    // A segment dictionary is built once, here, and from then on the block is a dictionary
    // with an index stream like any other - so the row-by-row paths below need to know
    // nothing about it.
    if (this.encoding === ENCODING_DICT_SEGMENT || this.encoding === ENCODING_DICT_SEGMENT_RLE) {
      this.dictionary = readSegmentDictionary(reader, fieldName)

      this.encoding =
        this.encoding === ENCODING_DICT_SEGMENT ? ENCODING_DICT : ENCODING_DICT_RLE

      return
    }

    const plainDictionary =
      this.encoding === ENCODING_DICT || this.encoding === ENCODING_DICT_RLE

    const frontDictionary =
      this.encoding === ENCODING_DICT_FRONT || this.encoding === ENCODING_DICT_FRONT_RLE

    if (!plainDictionary && !frontDictionary) return

    const count = reader.readCounter32()
    if (count < 0) throw new TcbError(`${fieldName}: the dictionary entry count is negative`)

    if (frontDictionary) {
      this.dictionary = readFrontCodedDictionary(reader, count, fieldName)
      return
    }

    if (this.element === ELEMENT_STRING) {
      for (let at = 0; at < count; at++)
        this.dictionary.push(reader.readString())

      return
    }

    // A fixed-width element: the entries are the value's own bytes, laid out one
    // after another, so they are taken as bytes and turned into values only when a
    // row asks for one.
    this.valueWidth = this.element === ELEMENT_F32 ? 4 : 8

    const bytes = reader.readBytes(count * this.valueWidth)
    this.valueDictionary = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  }

  /**
   * How many elements the next row of an array column holds.
   *
   * One call whichever way the block is laid out. An encoded array decoded every length
   * before the first element was read, so this hands out what it already has; a raw one
   * states each row's length in front of that row's elements, so this reads it where it
   * stands.
   */
  nextLength(): number {
    if (this.lengths !== null) {
      if (this.lengthAt >= this.lengths.length)
        throw new TcbError(`${this.fieldName}: the column has no more rows to read`)

      return this.lengths[this.lengthAt++]
    }

    const length = this.reader.readCounter32()

    if (length < 0) throw new TcbError(`${this.fieldName}: a row declares ${length} elements`)

    return length
  }

  /** The next int32 - which also serves enums, and reference indexes. */
  /**
   * The next value of a bit-packed stream: the packed bits, over the block's base.
   *
   * A value may cross a byte boundary, so this walks bits rather than bytes. The addition
   * wraps, mirroring the writer's wrapping subtraction.
   */
  private nextPacked(): bigint {
    let slot = 0n

    for (let at = 0; at < this.packedWidth; at++, this.packedBit++) {
      if ((this.packed![this.packedBit >> 3] >> (this.packedBit & 7) & 1) !== 0)
        slot |= 1n << BigInt(at)
    }

    return BigInt.asIntN(64, this.packedBase + slot)
  }

  nextI32(): number {
    this.rowsRemaining--

    if (this.encoding === ENCODING_BITPACK)
      return Number(BigInt.asIntN(32, this.nextPacked()))

    switch (this.encoding) {
      case ENCODING_RAW:
        return this.element === ELEMENT_I32 ? this.reader.readInt32() : this.reader.readCounter32()

      case ENCODING_VARINT:
        return this.reader.readCounter32()

      case ENCODING_DELTA: {
        // The addition wraps on purpose, mirroring the writer's wrapping
        // subtraction; together they are exact for every int32 pair. `| 0`
        // is the wrap: it folds the double-range sum back into an int32.
        if (this.started) {
          this.previous = (this.previous + this.reader.readCounter32()) | 0
        } else {
          this.previous = this.reader.readCounter32()
          this.started = true
        }

        return this.previous
      }

      case ENCODING_RLE: {
        if (this.runRemaining === 0) this.readRun()

        this.runRemaining--
        return this.runValue
      }

      default: { // ENCODING_DELTA_RLE; checkColumn refused everything else.
        if (!this.started) {
          this.previous = this.reader.readCounter32()
          this.started = true
          return this.previous
        }

        if (this.runRemaining === 0) this.readRun()

        this.runRemaining--
        this.previous = (this.previous + this.runValue) | 0
        return this.previous
      }
    }
  }

  /**
   * An int64 member: from an i64 column raw or through its dictionary, and from
   * anything narrower by decoding an int32 and widening it.
   *
   * A dictionary entry is the eight bytes the raw layout would have carried, so it
   * is read back as a little-endian BigInt exactly as readInt64 does.
   */
  nextI64(): bigint {
    if (this.element !== ELEMENT_I64) return BigInt(this.nextI32())

    if (this.encoding === ENCODING_BITPACK) {
      this.rowsRemaining--
      return this.nextPacked()
    }

    if (this.valueDictionary !== null)
      return this.valueDictionary.getBigInt64(this.nextValueEntry(), true)

    this.rowsRemaining--
    return this.reader.readInt64()
  }

  /**
   * A float member: raw, the dictionary entry's exact bit pattern, or a whole number.
   */
  nextF32(): number {
    if (this.wholeNumbers) return this.nextI32()

    if (this.valueDictionary !== null)
      return this.valueDictionary.getFloat32(this.nextValueEntry(), true)

    this.rowsRemaining--
    return this.reader.readFloat()
  }

  /**
   * A double member: from f64 or f32 - either of them raw or dictionary-encoded -
   * and from an i32 column by decoding and widening.
   */
  nextF64(): number {
    if (this.wholeNumbers) return this.nextI32()

    if (this.element === ELEMENT_F64) {
      if (this.valueDictionary !== null)
        return this.valueDictionary.getFloat64(this.nextValueEntry(), true)

      this.rowsRemaining--
      return this.reader.readDouble()
    }

    if (this.element === ELEMENT_F32) return this.nextF32()

    return this.nextI32()
  }

  /** A bool member: one byte raw, or a run of them. */
  nextBool(): boolean {
    if (this.encoding === ENCODING_RLE || this.encoding === ENCODING_BITPACK)
      return this.nextI32() !== 0

    this.rowsRemaining--
    return this.reader.readBool()
  }

  /**
   * Where the next row's dictionary entry starts, for a fixed-width element: a byte
   * offset into the entries kept as they were written.
   */
  private nextValueEntry(): number {
    this.rowsRemaining--

    let index: number

    if (this.encoding === ENCODING_DICT) {
      index = this.reader.readCounter32()
    } else {
      if (this.runRemaining === 0) this.readRun()

      this.runRemaining--
      index = this.runValue
    }

    const count = this.valueDictionary!.byteLength / this.valueWidth

    if (index < 0 || index >= count) {
      throw new TcbError(
        `${this.fieldName}: dictionary index ${index} is out of range - the ` +
        `dictionary holds ${count} entries`)
    }

    return index * this.valueWidth
  }

  /** The next string - the dictionary's instance where the block has one. */
  nextString(): string {
    this.rowsRemaining--

    switch (this.encoding) {
      case ENCODING_RAW:
        return this.reader.readString()

      case ENCODING_DICT:
      case ENCODING_DICT_FRONT:
        return this.dictionaryEntry(this.reader.readCounter32())

      default: { // ENCODING_DICT_RLE and ENCODING_DICT_FRONT_RLE
        if (this.runRemaining === 0) this.readRun()

        this.runRemaining--
        return this.dictionaryEntry(this.runValue)
      }
    }
  }

  /**
   * Up to `limit` rows that all hold the next value, and that value. The count is
   * always at least 1.
   *
   * This is what makes a run cost one call instead of one per row: the generated loop
   * asks once, then assigns the value that many times. An encoding that cannot promise
   * sameness cheaply answers 1, so the caller's loop is correct over every encoding and
   * only faster over runs.
   */
  nextSameI32(limit: number): { n: number; value: number } {
    if (this.encoding === ENCODING_RLE) {
      this.rowsRemaining--
      if (this.runRemaining === 0) this.readRun()

      const n = this.runRemaining < limit ? this.runRemaining : limit
      this.runRemaining -= n
      this.rowsRemaining -= n - 1

      return { n, value: this.runValue }
    }

    if (this.encoding === ENCODING_DELTA_RLE && this.started) {
      this.rowsRemaining--
      if (this.runRemaining === 0) this.readRun()

      if (this.runValue === 0) {
        // A zero-delta run is a run of one value.
        const n = this.runRemaining < limit ? this.runRemaining : limit
        this.runRemaining -= n
        this.rowsRemaining -= n - 1

        return { n, value: this.previous }
      }

      this.runRemaining--
      this.previous = (this.previous + this.runValue) | 0

      return { n: 1, value: this.previous }
    }

    return { n: 1, value: this.nextI32() }
  }

  /** The string counterpart of {@link nextSameI32}. */
  nextSameString(limit: number): { n: number; value: string } {
    if (this.encoding === ENCODING_DICT_RLE || this.encoding === ENCODING_DICT_FRONT_RLE) {
      this.rowsRemaining--
      if (this.runRemaining === 0) this.readRun()

      const n = this.runRemaining < limit ? this.runRemaining : limit
      this.runRemaining -= n
      this.rowsRemaining -= n - 1

      return { n, value: this.dictionaryEntry(this.runValue) }
    }

    return { n: 1, value: this.nextString() }
  }

  private readRun(): void {
    const length = this.reader.readCounter32()

    // + 1 because the row this run was read for is already counted out of
    // rowsRemaining by its next call.
    if (length < 1 || length > this.rowsRemaining + 1) {
      throw new TcbError(
        `${this.fieldName}: a run of ${length} values cannot cover the ` +
        `${this.rowsRemaining + 1} rows left in the column`)
    }

    this.runRemaining = length
    this.runValue = this.reader.readCounter32()
  }

  private dictionaryEntry(index: number): string {
    if (index < 0 || index >= this.dictionary.length) {
      throw new TcbError(
        `${this.fieldName}: dictionary index ${index} is out of range - the ` +
        `dictionary holds ${this.dictionary.length} entries`)
    }

    return this.dictionary[index]
  }
}

/**
 * A file's plaintext bytes, checked against its MAC on the way.
 *
 * Call this on the bytes before handing them to a reader. A file that is neither encrypted
 * nor authenticated comes back untouched, so the call belongs in the load path whether or
 * not the project uses either.
 *
 * The order is verify, then decrypt. The tag covers the file as it is stored, so an altered
 * file is refused before the key is used on it, and the header - the flags, the cipher byte,
 * the nonce - is covered along with the body.
 *
 * Decryption happens in place, and what comes back is a window onto the same array rather
 * than a copy of it. The fields this consumes are returned to what a plain file has in them,
 * so calling it twice on the same array is the same as calling it once.
 *
 * What the two layers are and are not for: both keys ship inside the client that reads the
 * file. Encryption stops a data file being read in an editor; the MAC stops an edited one
 * loading. Neither stops anyone who can take the keys out of the client, and no format does.
 *
 * @param macKey The key the files were signed with, or null when the project does not use
 * one. A reader that has one refuses a file that carries no MAC: the field being zero is how
 * a file says it is unauthenticated, so accepting that from a project that signs its files
 * would put the check sixteen zero bytes away from being removed.
 * @param verifyMac False skips the check. For tools and for measuring load time - and no
 * weaker than it looks, because anyone who can flip this flag in a shipped binary can read
 * the key out of the same binary.
 */
export function open(
  data: Uint8Array,
  key: Uint8Array | null = null,
  macKey: Uint8Array | null = null,
  verifyMac = true,
): Uint8Array {
  if (data.length < HEADER_SIZE) throw new TcbError('the file is too short to be a table')

  if (readMagic(data, MAGIC_OFFSET) !== MAGIC)
    throw new TcbError('the file does not begin with the table file signature')

  if (verifyMac) checkMac(data, macKey)

  if ((data[FLAGS_OFFSET] & FLAG_ENCRYPTED) === 0) return data

  if (data[CIPHER_OFFSET] !== CIPHER_CHACHA20) {
    throw new TcbError(
      `the file uses cipher ${data[CIPHER_OFFSET]}, which this reader does not know`)
  }

  if (key === null || key.length !== 32) {
    throw new TcbError(
      'the file is encrypted and no key, or a key that is not 32 bytes, was given')
  }

  // Subarrays rather than copies: the nonce is read where it lies and the body is
  // exclusive-ored where it lies, which is what makes this in place.
  chacha20(
    key,
    data.subarray(NONCE_OFFSET, NONCE_OFFSET + NONCE_SIZE),
    data.subarray(KEY_CHECK_OFFSET))

  if (readMagic(data, KEY_CHECK_OFFSET) !== MAGIC) {
    throw new TcbError(
      'the file did not decrypt to a table - the key is not the one it was written with')
  }

  // Back to what a plain file holds in these bytes, so that a second call over the same
  // array passes it through instead of decrypting it again.
  data[FLAGS_OFFSET] &= ~FLAG_ENCRYPTED
  data[CIPHER_OFFSET] = CIPHER_NONE
  data.fill(0, NONCE_OFFSET, NONCE_OFFSET + NONCE_SIZE)

  return data
}

/** Four bytes as the fixed32 the signature and the key check are compared as. */
function readMagic(data: Uint8Array, at: number): number {
  return (data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24) >>> 0
}

/** The MAC field against the file's own bytes, and against whether a key was given. */
function checkMac(data: Uint8Array, macKey: Uint8Array | null): void {
  let present = false
  for (let at = 0; at < MAC_SIZE && !present; ++at) present = data[MAC_OFFSET + at] !== 0

  // Nothing to check with. A file that carries a tag is read anyway rather than refused:
  // this reader has no way to tell whether the tag is good, and a client built before the
  // project turned MACs on is one this format has promised can still read what it is sent.
  if (macKey === null) return

  if (macKey.length !== 32) throw new TcbError('the MAC key given is not 32 bytes')

  if (!present) {
    throw new TcbError(
      'the file carries no MAC and this build expects one - it was exported without a MAC ' +
      'key, or the field was cleared after it was written')
  }

  if (!verifyTag(data, macKey)) {
    throw new TcbError(
      'the file does not match its MAC - it was altered after it was exported, or it was ' +
      'signed with a different key')
  }
}

/**
 * Reads and checks the file header, returning the row count that follows it.
 *
 * The flags byte says what was done to the body, and no bit of it is accepted here. Bit 0 -
 * encryption - is undone by {@link open}, which puts a plaintext header of its own in front
 * of what it hands back, so a reader still seeing it was given the ciphertext without the
 * key, and saying that beats letting the block lengths make what they can of it. Every other
 * bit means the file needs handling this build does not have.
 */
export function readTableHeader(reader: TcbReader): { rowCount: number, columns: TcbColumn[] } {
  // Checked again here rather than only in open, because a reader can be handed bytes that
  // never went through it.
  if (reader.readFixed32() !== MAGIC)
    throw new TcbError('the file does not begin with the table file signature')

  const version = reader.readFixed32()
  if (version !== BINARY_FILE_FORMAT_VERSION) {
    throw new TcbError(
      `table format version ${version} is not supported ` +
      `(expected ${BINARY_FILE_FORMAT_VERSION})`)
  }

  const flags = reader.readFixed8()
  if ((flags & FLAG_ENCRYPTED) !== 0) {
    throw new TcbError(
      'the table is encrypted and was not decrypted - pass the key through open first')
  }

  if (flags !== 0) throw new TcbError('table declares unsupported features')

  // The cipher byte, the nonce, the MAC and the key check. open has dealt with all four by
  // now; what is left is to be standing at the body.
  reader.skip(HEADER_SIZE - CIPHER_OFFSET)

  const rowCount = reader.readCounter32()
  if (rowCount < 0) throw new TcbError('table row count is negative')

  const columnCount = reader.readCounter32()
  if (columnCount < 0) throw new TcbError('table column count is negative')

  const columns: TcbColumn[] = []
  for (let at = 0; at < columnCount; ++at) {
    const tag = reader.readCounter32()
    const wire = reader.readFixed8()
    const encoding = reader.readFixed8()
    const count = reader.readCounter32()
    const byteLength = reader.readFixed32()
    columns.push({
      tag,
      element: wire & 0x0f,
      kind: (wire >> 4) & 0x03,
      nullable: (wire & 0x40) !== 0,
      encoding,
      count,
      byteLength,
    })
  }

  // What the descriptors say about the file, checked before anybody allocates for the
  // row count. The blocks are all that follows the header, so their declared lengths have
  // to add up to the bytes left. A raw block also costs at least one byte per row - a
  // varint's shortest form, an empty string's length prefix, a variable array's counter -
  // so a larger row count is one the exporter could not have written. An encoded block
  // has no such floor; its decode checks run sums and dictionary bounds instead.

  const available = reader.remaining
  let declared = 0

  for (const column of columns) {
    if (column.byteLength < 0 || column.byteLength > available - declared) {
      throw new TcbError(
        `column tag ${column.tag} declares ${column.byteLength} bytes, which the file cannot hold`)
    }

    declared += column.byteLength

    if (column.encoding === ENCODING_RAW && rowCount > column.byteLength) {
      throw new TcbError(
        `the row count ${rowCount} is larger than column tag ${column.tag} can hold in ` +
        `its ${column.byteLength} bytes`)
    }
  }

  if (declared !== available) {
    throw new TcbError(
      `the columns declare ${declared} bytes but ${available} follow the header`)
  }

  return { rowCount, columns }
}

/**
 * That a column is what the generated member expects, or a lossless promotion of it.
 * Refusal is by name and both types, never by reading anyway.
 */
export function checkColumn(
  column: TcbColumn, fieldName: string, kind: number, count: number, nullable: boolean,
  accepted: number[]): void {
  // Nullability is part of the shape: a file that says optional puts a presence bitmap at
  // the front of the block, and generated code not expecting one would read the bitmap as
  // values. Adding or removing a `?` is a schema change like any other.
  if (column.nullable !== nullable) {
    throw new TcbError(
      `${fieldName}: the file and the generated member disagree about whether this column is optional` +
      ` (file: ${column.nullable ? 'optional' : 'required'}, ` +
      `member: ${nullable ? 'optional' : 'required'}). ` +
      'The schema changed; regenerate the code or rebuild the data.')
  }
  if (column.kind !== kind || (kind !== KIND_VAR_ARRAY && column.count !== count)) {
    throw new TcbError(
      `${fieldName}: the file's column (kind ${column.kind}, count ${column.count}) does not ` +
      `match the generated member (kind ${kind}, count ${count}). The schema changed shape; ` +
      'regenerate the code or rebuild the data.')
  }
  // An encoding this build cannot decode - or one the spec does not define for this
  // element - is refused by name, exactly like an element it cannot read. An unknown
  // column's encoding never gets here - a skip is a skip whatever the block's layout.
  if (!encodingSupported(column)) {
    throw new TcbError(
      `${fieldName}: the file's column uses encoding ${column.encoding}, which this ` +
      'reader cannot decode for its element type. Regenerate the code or rebuild the data.')
  }
  if (!accepted.includes(column.element)) {
    throw new TcbError(
      `${fieldName}: the file carries element type ${column.element}, which this member ` +
      `cannot read (accepts: ${accepted.join(', ')}). The column changed type incompatibly; ` +
      'regenerate the code or rebuild the data.')
  }
}

/** A decoded value that has to be a byte, or the block is corrupt. */
function asByte(value: number, fieldName: string): number {
  if (value < 0 || value > 255)
    throw new TcbError(`${fieldName}: ${value} is not a byte`)

  return value
}

/**
 * A nullable column's presence bitmap, or null for a column that has none.
 *
 * Called by the generated code before the row loop: the bitmap sits at the front of the
 * block and the values follow it. One bit per row, low bit first, padded to a byte.
 */
export function readPresence(
  reader: TcbReader, column: TcbColumn, rowCount: number): Uint8Array | null {
  if (!column.nullable) return null

  // The bitmap is a bit-packed boolean column of width one, so it carries an encoding byte
  // and is laid out by the same choice a packed value block uses. Its width and base are
  // known in advance, which is why it does not carry them.
  const encoding = reader.readFixed8()

  return reader.readByteStream(encoding, (rowCount + 7) >> 3, 'a presence bitmap')
}

/**
 * Whether a row has a value, for a column that says which do.
 *
 * A null bitmap means the column is not optional and every row has one, so the generated
 * code can call this unconditionally.
 */
export function isPresent(presence: Uint8Array | null, row: number): boolean {
  return presence === null || (presence[row >> 3] & (1 << (row & 7))) !== 0
}

/**
 * The (element, encoding) pairs the spec defines. Integers take the integer encodings,
 * strings the dictionary ones, and an array takes the composition that applies all of
 * those to its elements.
 */
function encodingSupported(column: TcbColumn): boolean {
  if (column.encoding === ENCODING_RAW) return true

  // An array's block says what its elements use, and the element encoding is checked as it
  // is read rather than here - the descriptor carries only the outer one, so this is as far
  // as the descriptor can be checked.
  if (column.kind !== KIND_SCALAR) return column.encoding === ENCODING_ARRAY

  switch (column.element) {
    case ELEMENT_BOOL:
    case ELEMENT_VARINT:
      return column.encoding === ENCODING_RLE || column.encoding === ENCODING_BITPACK

    case ELEMENT_I32:
      return (column.encoding >= ENCODING_VARINT && column.encoding <= ENCODING_DELTA_RLE)
        || column.encoding === ENCODING_BITPACK

    // The dictionary is parameterized by element, so these reach it with entries that are
    // simply their own raw bytes.
    case ELEMENT_I64:
      return column.encoding === ENCODING_DICT || column.encoding === ENCODING_DICT_RLE
        || column.encoding === ENCODING_BITPACK

    // A float column additionally reaches the integer encodings, through the block that
    // says its values are whole numbers.
    case ELEMENT_F32:
    case ELEMENT_F64:
      return column.encoding === ENCODING_DICT || column.encoding === ENCODING_DICT_RLE
        || column.encoding === ENCODING_WHOLE

    // And a string dictionary can be front coded or built from segments, both of which are
    // meaningless for a fixed-width element and refused for one.
    case ELEMENT_STRING:
      return (column.encoding >= ENCODING_DICT && column.encoding <= ENCODING_DICT_FRONT_RLE)
        || column.encoding === ENCODING_DICT_SEGMENT
        || column.encoding === ENCODING_DICT_SEGMENT_RLE

    default:
      return false
  }
}

/**
 * That a block was consumed exactly: a mismatch is a format disagreement, and stopping
 * here names the column instead of corrupting the next.
 */
export function checkBlockEnd(reader: TcbReader, column: TcbColumn, expectedEnd: number): void {
  if (reader.position !== expectedEnd) {
    throw new TcbError(
      `column tag ${column.tag}: its block declared ${column.byteLength} bytes but the read ` +
      `ended ${expectedEnd - reader.position} bytes short of its boundary`)
  }
}

// Declared here rather than pulled from @types/node.
//
// This file is a module, so the declaration is local to it: a consumer that does
// have @types/node is unaffected, and one that does not - a browser project, say -
// is not made to install it for a function they will never call.
declare function require(moduleName: string): any

/**
 * Reads a whole file into memory.
 *
 * Node only, and resolved lazily, so the module still loads in a browser where the
 * binary arrives from fetch rather than the filesystem. Pass the bytes to a table's
 * readBinaryFrom in that case.
 */
export function readAllBytes(filename: string): Uint8Array {
  const fs = require('fs')
  return new Uint8Array(fs.readFileSync(filename))
}

// ---------------------------------------------------------------------- mac
//
// HMAC-SHA-256 over the file, truncated to the sixteen bytes the header keeps for it.
//
// Written out here for the same reason the cipher is: WebCrypto is asynchronous, and Node's
// crypto module does not exist in a browser, so neither can be reached from a load path that
// has to work in both. What that costs is the two hundred lines below; what it buys is that
// this file still has no dependencies.
//
// What the tag catches is what the structural checks cannot. A block length that does not
// add up is a malformed file and the reader says so; four other bytes in an f32 column is a
// well-formed file holding a different number, and no check over a file's shape can tell
// that from data that was always there.

/** Whether the file's MAC field is the tag its own bytes produce under this key. */
function verifyTag(data: Uint8Array, key: Uint8Array): boolean {
  const expected = hmacSha256(key, data)

  // Every byte, always: a comparison that returns early tells the caller how far it got.
  let difference = 0
  for (let at = 0; at < MAC_SIZE; ++at) difference |= expected[at] ^ data[MAC_OFFSET + at]

  return difference === 0
}

/**
 * The tag for a file: HMAC-SHA-256 over every byte but the sixteen the tag lives in.
 *
 * Skipping them is the same as zeroing them and cheaper by a copy of the file.
 */
function hmacSha256(key: Uint8Array, data: Uint8Array): Uint8Array {
  const block = new Uint8Array(64)

  // A key longer than the block is hashed first; ours is thirty-two bytes, but the rule is
  // part of HMAC and leaving it out would make this agree with nothing.
  if (key.length > 64) block.set(sha256([key]))
  else block.set(key)

  const inner = new Uint8Array(64)
  const outer = new Uint8Array(64)

  for (let at = 0; at < 64; ++at) {
    inner[at] = block[at] ^ 0x36
    outer[at] = block[at] ^ 0x5c
  }

  const innerDigest = sha256([
    inner,
    data.subarray(0, MAC_OFFSET),
    data.subarray(KEY_CHECK_OFFSET),
  ])

  return sha256([outer, innerDigest])
}

/** The round constants: the fractional parts of the cube roots of the first 64 primes. */
const SHA256_K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
])

/**
 * SHA-256 of the pieces, hashed as though they were one message.
 *
 * Pieces rather than one array because every call here has more than one and none of them
 * wants the copy that joining would cost: HMAC hashes a pad followed by the message, and the
 * message itself is the file with a hole in the middle of it.
 */
function sha256(pieces: Uint8Array[]): Uint8Array {
  const state = new Uint32Array([
    0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
    0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
  ])

  const schedule = new Uint32Array(64)
  const block = new Uint8Array(64)

  let filled = 0
  let length = 0

  for (const piece of pieces) {
    length += piece.length

    let at = 0

    // The partial block first, then whole blocks straight out of the piece: the copy into
    // `block` is only for the bytes that straddle a boundary.
    while (at < piece.length) {
      if (filled === 0 && piece.length - at >= 64) {
        sha256Block(state, schedule, piece, at)
        at += 64
        continue
      }

      const taking = Math.min(64 - filled, piece.length - at)
      block.set(piece.subarray(at, at + taking), filled)

      filled += taking
      at += taking

      if (filled === 64) {
        sha256Block(state, schedule, block, 0)
        filled = 0
      }
    }
  }

  // The padding: a set bit, zeros, and the message length in bits as a 64-bit big-endian
  // number. Two blocks when the length does not fit in the one that is open.
  const tail = new Uint8Array(filled + 9 > 64 ? 128 : 64)
  tail.set(block.subarray(0, filled))
  tail[filled] = 0x80

  const bits = length * 8

  // Split rather than shifted: JavaScript's bitwise operators are 32-bit, and a file large
  // enough to overflow the low word is one this would otherwise mis-hash.
  const high = Math.floor(bits / 0x100000000)

  tail[tail.length - 8] = (high >>> 24) & 0xff
  tail[tail.length - 7] = (high >>> 16) & 0xff
  tail[tail.length - 6] = (high >>> 8) & 0xff
  tail[tail.length - 5] = high & 0xff
  tail[tail.length - 4] = (bits >>> 24) & 0xff
  tail[tail.length - 3] = (bits >>> 16) & 0xff
  tail[tail.length - 2] = (bits >>> 8) & 0xff
  tail[tail.length - 1] = bits & 0xff

  for (let at = 0; at < tail.length; at += 64) sha256Block(state, schedule, tail, at)

  const digest = new Uint8Array(32)

  for (let at = 0; at < 8; ++at) {
    digest[at * 4] = (state[at] >>> 24) & 0xff
    digest[at * 4 + 1] = (state[at] >>> 16) & 0xff
    digest[at * 4 + 2] = (state[at] >>> 8) & 0xff
    digest[at * 4 + 3] = state[at] & 0xff
  }

  return digest
}

/** One 64-byte block of the compression function. */
function sha256Block(
  state: Uint32Array, schedule: Uint32Array, data: Uint8Array, offset: number,
): void {
  for (let at = 0; at < 16; ++at) {
    schedule[at] = (data[offset + at * 4] << 24
      | data[offset + at * 4 + 1] << 16
      | data[offset + at * 4 + 2] << 8
      | data[offset + at * 4 + 3]) >>> 0
  }

  for (let at = 16; at < 64; ++at) {
    const before = schedule[at - 15]
    const near = schedule[at - 2]

    const s0 = (rotateRight(before, 7) ^ rotateRight(before, 18) ^ before >>> 3) >>> 0
    const s1 = (rotateRight(near, 17) ^ rotateRight(near, 19) ^ near >>> 10) >>> 0

    schedule[at] = (schedule[at - 16] + s0 + schedule[at - 7] + s1) >>> 0
  }

  let a = state[0], b = state[1], c = state[2], d = state[3]
  let e = state[4], f = state[5], g = state[6], h = state[7]

  for (let at = 0; at < 64; ++at) {
    const s1 = (rotateRight(e, 6) ^ rotateRight(e, 11) ^ rotateRight(e, 25)) >>> 0
    const choice = ((e & f) ^ (~e & g)) >>> 0
    const one = (h + s1 + choice + SHA256_K[at] + schedule[at]) >>> 0

    const s0 = (rotateRight(a, 2) ^ rotateRight(a, 13) ^ rotateRight(a, 22)) >>> 0
    const majority = ((a & b) ^ (a & c) ^ (b & c)) >>> 0
    const two = (s0 + majority) >>> 0

    h = g
    g = f
    f = e
    e = (d + one) >>> 0
    d = c
    c = b
    b = a
    a = (one + two) >>> 0
  }

  state[0] = (state[0] + a) >>> 0
  state[1] = (state[1] + b) >>> 0
  state[2] = (state[2] + c) >>> 0
  state[3] = (state[3] + d) >>> 0
  state[4] = (state[4] + e) >>> 0
  state[5] = (state[5] + f) >>> 0
  state[6] = (state[6] + g) >>> 0
  state[7] = (state[7] + h) >>> 0
}

function rotateRight(value: number, count: number): number {
  return ((value >>> count) | (value << (32 - count))) >>> 0
}

// ------------------------------------------------------------------- cipher
//
// The ChaCha20 stream cipher of RFC 8439, as the file envelope uses it.
//
// Here rather than from the platform because what the platform offers - WebCrypto, Node's
// own crypto - is an authenticated construction, which changes the length, and is
// asynchronous besides. This format wants a plain keystream: applying it leaves every byte
// count as it was, so the structural checks - the block lengths that must sum exactly -
// hold over the ciphertext unchanged.
//
// A hundred lines with no dependency, which is what lets the same cipher exist in every
// runtime that has to read one of these files.

/**
 * Exclusive-ors the keystream over `data`, in place.
 *
 * One routine for both directions, which is what a stream cipher is: the keystream depends
 * only on the key, the nonce and the position, so applying it twice returns what went in.
 * The block counter starts at zero.
 */
function chacha20(key: Uint8Array, nonce: Uint8Array, data: Uint8Array): void {
  const state = new Uint32Array(16)
  const working = new Uint32Array(16)
  const keystream = new Uint8Array(64)

  // "expand 32-byte k", as four little-endian words.
  state[0] = 0x61707865
  state[1] = 0x3320646e
  state[2] = 0x79622d32
  state[3] = 0x6b206574

  for (let at = 0; at < 8; at++) state[4 + at] = littleEndianWord(key, at * 4)

  state[12] = 0

  for (let at = 0; at < 3; at++) state[13 + at] = littleEndianWord(nonce, at * 4)

  for (let offset = 0; offset < data.length; offset += 64) {
    chacha20Block(state, working, keystream)

    const count = Math.min(64, data.length - offset)

    for (let at = 0; at < count; at++) data[offset + at] ^= keystream[at]

    state[12]++
  }
}

/** One 64-byte keystream block: twenty rounds over a copy of the state. */
function chacha20Block(state: Uint32Array, working: Uint32Array, keystream: Uint8Array): void {
  working.set(state)

  // Ten double rounds. Each is four column quarter-rounds and four diagonal ones, which
  // between them let every word reach every other.
  for (let round = 0; round < 10; round++) {
    quarterRound(working, 0, 4, 8, 12)
    quarterRound(working, 1, 5, 9, 13)
    quarterRound(working, 2, 6, 10, 14)
    quarterRound(working, 3, 7, 11, 15)

    quarterRound(working, 0, 5, 10, 15)
    quarterRound(working, 1, 6, 11, 12)
    quarterRound(working, 2, 7, 8, 13)
    quarterRound(working, 3, 4, 9, 14)
  }

  // Added back to the state it started from, which is what stops the rounds being
  // reversible and so the keystream being recoverable.
  for (let at = 0; at < 16; at++) {
    // Written back through the Uint32Array, which is where the wrap at 2^32 happens: the
    // sum of two words is a double, and a double holds thirty-three bits exactly.
    working[at] = working[at] + state[at]

    const word = working[at]

    keystream[at * 4] = word & 0xff
    keystream[at * 4 + 1] = (word >>> 8) & 0xff
    keystream[at * 4 + 2] = (word >>> 16) & 0xff
    keystream[at * 4 + 3] = (word >>> 24) & 0xff
  }
}

function quarterRound(block: Uint32Array, a: number, b: number, c: number, d: number): void {
  // `>>> 0` after every addition and rotation: JavaScript's arithmetic is on doubles and
  // its bitwise operators yield signed 32-bit values, so an unsigned word has to be put
  // back to unsigned at each step rather than only at the end.
  block[a] = (block[a] + block[b]) >>> 0
  block[d] = rotateLeft(block[d] ^ block[a], 16)
  block[c] = (block[c] + block[d]) >>> 0
  block[b] = rotateLeft(block[b] ^ block[c], 12)
  block[a] = (block[a] + block[b]) >>> 0
  block[d] = rotateLeft(block[d] ^ block[a], 8)
  block[c] = (block[c] + block[d]) >>> 0
  block[b] = rotateLeft(block[b] ^ block[c], 7)
}

function rotateLeft(value: number, count: number): number {
  return ((value << count) | (value >>> (32 - count))) >>> 0
}

function littleEndianWord(bytes: Uint8Array, at: number): number {
  return (bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24)) >>> 0
}

// --------------------------------------------------------------- formatting

/**
 * Decodes UTF-8.
 *
 * TextDecoder where it exists, which is everywhere modern, with a hand-rolled
 * fallback so the reader does not depend on the host providing it.
 */
function decodeUtf8(bytes: Uint8Array): string {
  if (typeof TextDecoder !== 'undefined') {
    return new TextDecoder('utf-8').decode(bytes)
  }

  let out = ''
  let i = 0

  while (i < bytes.length) {
    const b0 = bytes[i++]

    if (b0 < 0x80) {
      out += String.fromCharCode(b0)
    } else if (b0 < 0xe0) {
      out += String.fromCharCode(((b0 & 0x1f) << 6) | (bytes[i++] & 0x3f))
    } else if (b0 < 0xf0) {
      out += String.fromCharCode(
        ((b0 & 0x0f) << 12) | ((bytes[i++] & 0x3f) << 6) | (bytes[i++] & 0x3f))
    } else {
      const codePoint =
        ((b0 & 0x07) << 18) | ((bytes[i++] & 0x3f) << 12) |
        ((bytes[i++] & 0x3f) << 6) | (bytes[i++] & 0x3f)
      out += String.fromCodePoint(codePoint)
    }
  }

  return out
}

function pad(value: number, width: number): string {
  return value.toString().padStart(width, '0')
}

/**
 * Formats .NET ticks as the ISO 8601 text the JSON export produces.
 *
 * The stored value has no time zone - the sheet said nothing about one - so it is
 * rendered as a local-looking timestamp with no offset, matching what the JSON
 * export writes for a DateTime of unspecified kind.
 *
 * Exported because a date column is an i64 one, so it can arrive encoded and its
 * ticks then come from the cursor rather than from a direct read.
 */
export function formatDateTimeTicks(ticks: bigint): string {
  const sinceEpoch = ticks - UNIX_EPOCH_TICKS

  // Split before converting, so the sub-second part keeps full tick resolution
  // rather than being rounded into a millisecond.
  let seconds = sinceEpoch / TICKS_PER_SECOND
  let subTicks = sinceEpoch % TICKS_PER_SECOND

  if (subTicks < 0n) {
    subTicks += TICKS_PER_SECOND
    seconds -= 1n
  }

  // Read the calendar fields in UTC: the value carries no offset, so treating it
  // as UTC and reading it back the same way round-trips the wall clock exactly.
  const date = new Date(Number(seconds) * 1000)

  const text =
    `${pad(date.getUTCFullYear(), 4)}-${pad(date.getUTCMonth() + 1, 2)}-${pad(date.getUTCDate(), 2)}` +
    `T${pad(date.getUTCHours(), 2)}:${pad(date.getUTCMinutes(), 2)}:${pad(date.getUTCSeconds(), 2)}`

  if (subTicks === 0n) return text

  // Seven digits with trailing zeros trimmed, which is how .NET renders a
  // fractional second.
  return `${text}.${subTicks.toString().padStart(7, '0').replace(/0+$/, '')}`
}

/**
 * Formats .NET ticks as the duration text the JSON export produces:
 * `[-][d.]hh:mm:ss[.fffffff]`.
 *
 * Exported for the same reason as the date one above.
 */
export function formatTimeSpanTicks(ticks: bigint): string {
  const negative = ticks < 0n
  let remaining = negative ? -ticks : ticks

  const days = remaining / TICKS_PER_DAY
  remaining %= TICKS_PER_DAY

  const hours = remaining / TICKS_PER_HOUR
  remaining %= TICKS_PER_HOUR

  const minutes = remaining / TICKS_PER_MINUTE
  remaining %= TICKS_PER_MINUTE

  const seconds = remaining / TICKS_PER_SECOND
  const subTicks = remaining % TICKS_PER_SECOND

  let text = `${pad(Number(hours), 2)}:${pad(Number(minutes), 2)}:${pad(Number(seconds), 2)}`

  // Days and the fraction are both omitted when zero, as .NET does.
  if (days !== 0n) text = `${days}.${text}`
  if (subTicks !== 0n) text += `.${subTicks.toString().padStart(7, '0').replace(/0+$/, '')}`

  return negative ? `-${text}` : text
}

/**
 * Formats sixteen bytes in .NET Guid layout as canonical text.
 *
 * That layout is not plain big-endian: the first three components are little
 * endian and the trailing eight bytes are not, which is what the index order
 * below accounts for.
 */
function formatUuid(bytes: Uint8Array): string {
  const order = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]

  let out = ''

  for (let i = 0; i < 16; i++) {
    if (i === 4 || i === 6 || i === 8 || i === 10) out += '-'
    out += bytes[order[i]].toString(16).padStart(2, '0')
  }

  return out
}
