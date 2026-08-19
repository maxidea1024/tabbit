-- Tabbit's binary reader.
--
-- Copied in beside the generated accessor so the emitted code needs nothing installed.
-- Edit it in the Tabbit repository.
--
-- Reads the .tcb files Tabbit's binary exporter writes. One of several readers of one
-- format the exporter defines; the conformance corpus is what keeps them agreeing.
--
-- Two runtimes read this file: LuaJIT 2.1 and Lua 5.3+. They spell low-level numeric
-- work differently - 64-bit values are FFI cdata on one and native integers on the
-- other, and 5.3's operator syntax does not parse under LuaJIT - so those operations
-- live in a backend module chosen at load time (tcb_ops_jit.lua / tcb_ops_53.lua) and
-- everything in this file is syntax both runtimes share. Plain Lua 5.1 and 5.2 are not
-- supported: their numbers are doubles, and a bigint or a datetime's ticks does not
-- survive one.
--
-- Nothing here needs anything beyond the standard library, with one exception: an
-- encrypted or MAC-checked file needs the tabbit.native module, compiled from
-- tabbit/native/tabbit_native.c. Byte-at-a-time Lua is too slow to run a keystream
-- over a table file, so that path is C. The require happens inside the call that uses
-- it, so a project whose files are neither encrypted nor signed never meets it.

-- "tabbit.tcb_reader" -> "tabbit.", so the backend is found wherever the consumer
-- mounted this directory, without a configured prefix.
local _prefix = (...):match("^(.-)[^%.]*$")

local ops
if type(jit) == "table" then
  ops = require(_prefix .. "tcb_ops_jit")
elseif math.type ~= nil then
  ops = require(_prefix .. "tcb_ops_53")
else
  error("this reader needs LuaJIT 2.1 or Lua 5.3+ - plain Lua " .. _VERSION ..
    " has no lossless 64-bit integer", 0)
end

local byte = string.byte
local sub = string.sub
local format = string.format
local floor = math.floor

local tcb = {}

-- Stamped at the head of every table file by the exporter.
tcb.FORMAT_VERSION = 106

-- The wire's element types and kinds, as a column descriptor spells them.
tcb.ELEMENT_VARINT = 0
tcb.ELEMENT_BOOL = 1
tcb.ELEMENT_I32 = 2
tcb.ELEMENT_I64 = 3
tcb.ELEMENT_F32 = 4
tcb.ELEMENT_F64 = 5
tcb.ELEMENT_STRING = 6
tcb.ELEMENT_UUID = 7

tcb.KIND_SCALAR = 0
tcb.KIND_FIXED_ARRAY = 1
tcb.KIND_VAR_ARRAY = 2

-- How a block's values are laid out. spec/tcb-v102-column-encoding.md is the contract.
tcb.ENCODING_RAW = 0
tcb.ENCODING_VARINT = 1
tcb.ENCODING_DELTA = 2
tcb.ENCODING_RLE = 3
tcb.ENCODING_DELTA_RLE = 4
tcb.ENCODING_DICT = 5
tcb.ENCODING_DICT_RLE = 6
tcb.ENCODING_DICT_FRONT = 7
tcb.ENCODING_DICT_FRONT_RLE = 8
tcb.ENCODING_ARRAY = 9
tcb.ENCODING_WHOLE = 10
tcb.ENCODING_DICT_SEGMENT = 11
tcb.ENCODING_DICT_SEGMENT_RLE = 12
tcb.ENCODING_BITPACK = 13

-- The file header, at fixed offsets whether or not the file is encrypted and whether
-- or not it carries a MAC. spec/tcb-mac-and-signature.md.
tcb.MAGIC_OFFSET = 0
tcb.VERSION_OFFSET = 4
tcb.FLAGS_OFFSET = 8
tcb.CIPHER_OFFSET = 9
tcb.NONCE_OFFSET = 10
tcb.MAC_OFFSET = 22
tcb.KEY_CHECK_OFFSET = 38
tcb.HEADER_SIZE = 42
tcb.NONCE_SIZE = 12
tcb.MAC_SIZE = 16

-- The four bytes every table file starts with.
tcb.MAGIC = "TCB\0"

tcb.FLAG_ENCRYPTED = 0x01
tcb.CIPHER_NONE = 0
tcb.CIPHER_CHACHA20 = 1

local ENCODING_RAW = tcb.ENCODING_RAW
local ENCODING_VARINT = tcb.ENCODING_VARINT
local ENCODING_DELTA = tcb.ENCODING_DELTA
local ENCODING_RLE = tcb.ENCODING_RLE
local ENCODING_DELTA_RLE = tcb.ENCODING_DELTA_RLE
local ENCODING_DICT = tcb.ENCODING_DICT
local ENCODING_DICT_RLE = tcb.ENCODING_DICT_RLE
local ENCODING_DICT_FRONT = tcb.ENCODING_DICT_FRONT
local ENCODING_DICT_FRONT_RLE = tcb.ENCODING_DICT_FRONT_RLE
local ENCODING_ARRAY = tcb.ENCODING_ARRAY
local ENCODING_WHOLE = tcb.ENCODING_WHOLE
local ENCODING_DICT_SEGMENT = tcb.ENCODING_DICT_SEGMENT
local ENCODING_DICT_SEGMENT_RLE = tcb.ENCODING_DICT_SEGMENT_RLE
local ENCODING_BITPACK = tcb.ENCODING_BITPACK

-- Errors raised here are plain strings under this prefix, so a caller can tell a table
-- file's refusal from an unrelated failure without a class hierarchy the language does
-- not have.
local function fail(message, ...)
  error("tcb: " .. format(message, ...), 0)
end

tcb.fail = fail

-- A decoded value that has to be a byte, or the block is corrupt.
local function as_byte(value, field_name)
  if value < 0 or value > 255 then
    fail("%s: %d is not a byte", field_name, value)
  end

  return value
end

-- An int64 - cdata on LuaJIT, a native integer on 5.3+ - as decimal digits.
tcb.int64String = ops.i64string

---------------------------------------------------------------------------------------
-- Reader: sequential reads over a table file's bytes.
---------------------------------------------------------------------------------------

local Reader = {}
Reader.__index = Reader

function tcb.newReader(data)
  return setmetatable({ data = data, position = 0 }, Reader)
end

function Reader:remaining()
  return #self.data - self.position
end

-- Bounds-checks `count` bytes and returns the 0-based offset they start at.
function Reader:take(count)
  local remaining = #self.data - self.position

  if remaining < count then
    fail("table data ended after %d of %d bytes while %d more were expected",
      self.position, #self.data, count)
  end

  local start = self.position
  self.position = self.position + count

  return start
end

function Reader:readU8()
  local at = self:take(1)
  return byte(self.data, at + 1)
end

function Reader:readBool()
  return self:readU8() ~= 0
end

function Reader:readI32()
  return ops.i32(self.data, self:take(4))
end

function Reader:readU32()
  return ops.u32(self.data, self:take(4))
end

function Reader:readI64()
  return ops.i64(self.data, self:take(8))
end

-- A single-precision value, widened to Lua's double. The value is exactly the one that
-- was stored; printing it at double precision shows digits the original 32 bits never
-- carried, which is why the conformance comparison narrows before comparing.
function Reader:readF32()
  return ops.f32(self.data, self:take(4))
end

function Reader:readF64()
  return ops.f64(self.data, self:take(8))
end

function Reader:readString()
  local length = self:readCounter32()

  if length < 0 then
    fail("string length is negative")
  end

  if length == 0 then
    return ""
  end

  local at = self:take(length)

  return sub(self.data, at + 1, at + length)
end

-- An int32 written in as few bytes as its magnitude needed, either sign.
--
-- The accumulation is plain arithmetic rather than backend bit work: five 7-bit pieces
-- reach 2^35 at most, which a double holds exactly, and (encoded - bit) / 2 is the
-- zig-zag unfold without a shift. math.floor keeps 5.3's result an integer.
function Reader:readOptimalInt32()
  local encoded = 0
  local scale = 1

  for _ = 1, 5 do
    local piece = self:readU8()
    encoded = encoded + (piece % 0x80) * scale

    if piece < 0x80 then
      local half = floor(encoded / 2)

      if encoded % 2 == 1 then
        return -half - 1
      end

      return half
    end

    scale = scale * 0x80
  end

  fail("varint32 is longer than five bytes")
end

-- A count, in the same encoding as readOptimalInt32.
Reader.readCounter32 = Reader.readOptimalInt32

-- An enum value, which travels zig-zag encoded rather than fixed width.
Reader.readEnum = Reader.readOptimalInt32

-- An int64 written in as few bytes as its magnitude needed, either sign. The base of a
-- bit-packed block. The unfold needs true 64-bit work, so the pieces go to the backend.
function Reader:readCounter64()
  local pieces = {}

  for i = 1, 10 do
    local piece = self:readU8()
    pieces[i] = piece % 0x80

    if piece < 0x80 then
      return ops.dezig64(pieces)
    end
  end

  fail("a 64-bit variable length integer runs past ten bytes")
end

-- A stream of bytes under one of the integer encodings, as a 1-based array of byte
-- values - which is what a packed block and a presence bitmap both end in. One reader
-- for both, so a bitmap and a packed value block cannot disagree about the same bits.
function Reader:readByteStream(encoding, count, field_name)
  local out = {}

  if encoding == ENCODING_RAW then
    local at = self:take(count)
    local data = self.data

    for i = 1, count do
      out[i] = byte(data, at + i)
    end

    return out
  end

  if encoding > ENCODING_DELTA_RLE then
    fail("%s: encoding %d cannot carry a packed byte stream", field_name, encoding)
  end

  local walking = encoding == ENCODING_DELTA or encoding == ENCODING_DELTA_RLE
  local filled = 0
  local previous = 0

  -- The first value of a delta stream is written outright; the rest are steps from it.
  -- A run in a delta stream repeats the step, not the value, so it walks.
  if count > 0 and walking then
    previous = as_byte(self:readOptimalInt32(), field_name)
    filled = 1
    out[1] = previous
  end

  while filled < count do
    local run = 1
    local step = 0
    local value = 0

    if encoding == ENCODING_VARINT then
      value = as_byte(self:readOptimalInt32(), field_name)
    elseif encoding == ENCODING_DELTA then
      step = self:readOptimalInt32()
    elseif encoding == ENCODING_RLE then
      run = self:readCounter32()
      value = as_byte(self:readOptimalInt32(), field_name)
    else -- ENCODING_DELTA_RLE
      run = self:readCounter32()
      step = self:readOptimalInt32()
    end

    if run < 1 or run > count - filled then
      fail("%s: a run of %d cannot cover the %d bytes left", field_name, run, count - filled)
    end

    for _ = 1, run do
      filled = filled + 1

      if walking then
        previous = as_byte(ops.add32(previous, step), field_name)
        out[filled] = previous
      else
        out[filled] = value
      end
    end
  end

  return out
end

-- The next `count` bytes, uninterpreted, as a string. What a fixed-width dictionary
-- entry is kept as, so that turning one into a value reconstructs exactly what the raw
-- layout would have read.
function Reader:readBytes(count)
  local at = self:take(count)
  return sub(self.data, at + 1, at + count)
end

-- Advances past bytes without interpreting them: an unknown column's block.
function Reader:skip(count)
  if count < 0 or count > self:remaining() then
    fail("cannot skip %d bytes with %d remaining", count, self:remaining())
  end

  self.position = self.position + count
end

-- Promotions: a member reading a file element narrower than itself. Only the
-- mathematically lossless directions exist; checkColumn already refused the rest.

function Reader:readI32As(element)
  if element == tcb.ELEMENT_I32 then
    return self:readI32()
  end

  return self:readCounter32()
end

function Reader:readI64As(element)
  if element == tcb.ELEMENT_I64 then
    return self:readI64()
  end

  if element == tcb.ELEMENT_I32 then
    return self:readI32()
  end

  return self:readCounter32()
end

function Reader:readF64As(element)
  if element == tcb.ELEMENT_F64 then
    return self:readF64()
  end

  if element == tcb.ELEMENT_F32 then
    return self:readF32()
  end

  return ops.tofloat(self:readI32())
end

-- A timestamp as .NET ticks: 100 ns units since 0001-01-01. Ticks rather than a date
-- value, because Lua has no date type and a tick count survives the round trip exactly.
Reader.readDateTimeTicks = Reader.readI64

-- A duration as .NET ticks.
Reader.readDurationTicks = Reader.readI64

-- The order matching .NET's Guid.ToString("D"): the first three components little
-- endian, the trailing eight bytes not.
local UUID_ORDER = { 4, 3, 2, 1, 6, 5, 8, 7, 9, 10, 11, 12, 13, 14, 15, 16 }

-- A uuid as its canonical lower-case hyphenated string. A string rather than a type of
-- its own: it is the one Lua value that is hashable and comparable by value, which the
-- key indexes and the constants need. spec/lua-language-support.md.
function Reader:readUuid()
  local at = self:take(16)
  local data = self.data
  local out = {}

  for i = 1, 16 do
    if i == 5 or i == 7 or i == 9 or i == 11 then
      out[#out + 1] = "-"
    end

    out[#out + 1] = format("%02x", byte(data, at + UUID_ORDER[i]))
  end

  return table.concat(out)
end

---------------------------------------------------------------------------------------
-- Dictionaries and array lengths, decoded once per block.
---------------------------------------------------------------------------------------

-- A sorted dictionary whose entries state only what they do not share with the entry
-- before them. The shared count is a count of UTF-8 bytes, which can land inside a
-- character - substrings carry that exactly.
local function read_front_coded_dictionary(reader, count, field_name)
  local entries = {}
  local previous = ""

  for at = 1, count do
    local shared = reader:readCounter32()
    local rest = reader:readCounter32()

    if shared < 0 or rest < 0 or shared > #previous then
      fail("%s: dictionary entry %d shares %d bytes with an entry of %d",
        field_name, at - 1, shared, #previous)
    end

    previous = sub(previous, 1, shared) .. reader:readBytes(rest)
    entries[at] = previous
  end

  return entries
end

-- The lengths of an array column's rows, as their own encoded stream.
local function read_lengths(reader, encoding, row_count, field_name)
  local lengths = {}

  if encoding == ENCODING_RAW then
    for at = 1, row_count do
      local length = reader:readCounter32()

      if length < 0 then
        fail("%s: row %d declares %d elements", field_name, at - 1, length)
      end

      lengths[at] = length
    end

    return lengths
  end

  if encoding ~= ENCODING_RLE then
    fail("%s: encoding %d cannot carry an array column's row lengths", field_name, encoding)
  end

  local filled = 0

  while filled < row_count do
    local run = reader:readCounter32()
    local value = reader:readOptimalInt32()

    if run < 1 or run > row_count - filled then
      fail("%s: a run of %d lengths cannot cover the %d rows left in the column",
        field_name, run, row_count - filled)
    end

    if value < 0 then
      fail("%s: a row declares %d elements", field_name, value)
    end

    for _ = 1, run do
      filled = filled + 1
      lengths[filled] = value
    end
  end

  return lengths
end

-- A dictionary whose entries are lists of references into a table of the pieces they
-- are built from. The result is the same list of whole strings every other dictionary
-- produces, so nothing downstream knows which kind it came from.
local function read_segment_dictionary(reader, field_name)
  local segment_count = reader:readCounter32()

  if segment_count < 0 then
    fail("%s: the segment count is negative", field_name)
  end

  if segment_count > reader:remaining() then
    fail("%s: a segment table of %d entries is larger than the file can hold",
      field_name, segment_count)
  end

  local segments = {}
  local previous = ""

  for at = 1, segment_count do
    local shared = reader:readCounter32()
    local rest = reader:readCounter32()

    if shared < 0 or rest < 0 or shared > #previous then
      fail("%s: segment %d shares %d bytes with an entry of %d",
        field_name, at - 1, shared, #previous)
    end

    previous = sub(previous, 1, shared) .. reader:readBytes(rest)
    segments[at] = previous
  end

  local count = reader:readCounter32()

  if count < 0 then
    fail("%s: the dictionary entry count is negative", field_name)
  end

  if count > reader:remaining() then
    fail("%s: a dictionary of %d entries is larger than the file can hold", field_name, count)
  end

  local entries = {}

  for at = 1, count do
    local pieces = reader:readCounter32()

    if pieces < 0 then
      fail("%s: dictionary entry %d declares %d pieces", field_name, at - 1, pieces)
    end

    local value = {}

    for i = 1, pieces do
      local index = reader:readCounter32()

      if index < 0 or index >= segment_count then
        fail("%s: segment index %d is out of range - the table holds %d entries",
          field_name, index, segment_count)
      end

      value[i] = segments[index + 1]
    end

    entries[at] = table.concat(value)
  end

  return entries
end

---------------------------------------------------------------------------------------
-- ColumnCursor: one scalar column's values in row order, whatever the block's encoding.
---------------------------------------------------------------------------------------

local Cursor = {}
Cursor.__index = Cursor

-- checkColumn has already refused any (element, encoding) pair the spec does not
-- define, so the branches here do not re-litigate that.
function tcb.newCursor(reader, column, row_count, field_name)
  local cursor = setmetatable({
    reader = reader,
    fieldName = field_name,
    element = column.element,
    encoding = column.encoding,

    -- A run-length family's current run: what remains of it, and its value - a plain
    -- value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
    runRemaining = 0,
    runValue = 0,

    -- The delta family's accumulator, once started.
    previous = 0,
    started = false,

    -- Values not yet handed out. For an array column this counts elements, not rows.
    rowsRemaining = row_count,

    dictionary = nil,
    valueDictionary = nil,

    lengths = nil,
    lengthAt = 0,

    -- Whether a float column's values are travelling as integers.
    wholeNumbers = false,

    packed = nil,
    packedWidth = 0,
    packedBase = 0,
    packedBit = 0,
  }, Cursor)

  -- An array column's block names an encoding for its elements and, where its rows
  -- differ in length, one for the lengths.
  if cursor.encoding == ENCODING_ARRAY then
    cursor.encoding = reader:readU8()

    if column.kind == tcb.KIND_VAR_ARRAY then
      cursor.lengths = read_lengths(reader, reader:readU8(), row_count, field_name)

      local total = 0
      for i = 1, row_count do
        total = total + cursor.lengths[i]
      end

      cursor.rowsRemaining = total
    else
      cursor.rowsRemaining = row_count * column.count
    end
  end

  -- A bit-packed column states the width its range needs, the base subtracted from
  -- every value, and which encoding carries the packed bytes.
  if cursor.encoding == ENCODING_BITPACK then
    local width = reader:readU8()
    local base = reader:readCounter64()
    local inner = reader:readU8()

    if width < 1 or width > 64 then
      fail("%s: a bit width of %d is not between 1 and 64", field_name, width)
    end

    cursor.packedWidth = width
    cursor.packedBase = base
    cursor.packed = reader:readByteStream(
      inner, floor((cursor.rowsRemaining * width + 7) / 8), field_name)

    return cursor
  end

  -- A float column whose values are all whole numbers carries them as integers and says
  -- which integer encoding they travel under.
  if cursor.encoding == ENCODING_WHOLE then
    local inner = reader:readU8()

    if inner < ENCODING_VARINT or inner > ENCODING_DELTA_RLE then
      fail("%s: encoding %d cannot carry a whole-number column's values", field_name, inner)
    end

    cursor.encoding = inner
    cursor.wholeNumbers = true
  end

  -- A segment dictionary is built once, here, and from then on the block is a
  -- dictionary with an index stream like any other.
  if cursor.encoding == ENCODING_DICT_SEGMENT or cursor.encoding == ENCODING_DICT_SEGMENT_RLE then
    cursor.dictionary = read_segment_dictionary(reader, field_name)
    cursor.encoding = cursor.encoding == ENCODING_DICT_SEGMENT
      and ENCODING_DICT or ENCODING_DICT_RLE

    return cursor
  end

  local plain = cursor.encoding == ENCODING_DICT or cursor.encoding == ENCODING_DICT_RLE
  local front = cursor.encoding == ENCODING_DICT_FRONT
    or cursor.encoding == ENCODING_DICT_FRONT_RLE

  if not plain and not front then
    return cursor
  end

  local count = reader:readCounter32()

  if count < 0 then
    fail("%s: the dictionary entry count is negative", field_name)
  end

  if front then
    cursor.dictionary = read_front_coded_dictionary(reader, count, field_name)
  elseif cursor.element == tcb.ELEMENT_STRING then
    local entries = {}

    for i = 1, count do
      entries[i] = reader:readString()
    end

    cursor.dictionary = entries
  else
    -- A fixed-width element: an entry is the value's own bytes, taken as a substring
    -- and turned into a value only when a row asks for one.
    local width = cursor.element == tcb.ELEMENT_F32 and 4 or 8
    local entries = {}

    for i = 1, count do
      entries[i] = reader:readBytes(width)
    end

    cursor.valueDictionary = entries
  end

  return cursor
end

-- How many elements the next row of an array column holds. An encoded array decoded
-- every length before the first element was read; a raw one states each row's length in
-- front of that row's elements.
function Cursor:nextLength()
  if self.lengths ~= nil then
    if self.lengthAt >= #self.lengths then
      fail("%s: the column has no more rows to read", self.fieldName)
    end

    self.lengthAt = self.lengthAt + 1

    return self.lengths[self.lengthAt]
  end

  local length = self.reader:readCounter32()

  if length < 0 then
    fail("%s: a row declares %d elements", self.fieldName, length)
  end

  return length
end

-- The next value of a bit-packed stream: the packed bits, over the block's base. A
-- value may cross a byte boundary, so this walks bits rather than bytes.
function Cursor:nextPacked()
  local slot = ops.slot0
  local packed = self.packed
  local bit_at = self.packedBit

  for at = 0, self.packedWidth - 1 do
    if ops.bittest(packed[floor(bit_at / 8) + 1], bit_at % 8) then
      slot = ops.setbit(slot, at)
    end

    bit_at = bit_at + 1
  end

  self.packedBit = bit_at

  -- The addition wraps, mirroring the writer's wrapping subtraction.
  return ops.addbase64(self.packedBase, slot)
end

local function read_run(self)
  local length = self.reader:readCounter32()

  -- + 1 because the row this run was read for is already counted out of rowsRemaining
  -- by its next* call.
  if length < 1 or length > self.rowsRemaining + 1 then
    fail("%s: a run of %d values cannot cover the %d rows left in the column",
      self.fieldName, length, self.rowsRemaining + 1)
  end

  self.runRemaining = length
  self.runValue = self.reader:readOptimalInt32()
end

local function dictionary_entry(self, entries, index)
  if index < 0 or index >= #entries then
    fail("%s: dictionary index %d is out of range - the dictionary holds %d entries",
      self.fieldName, index, #entries)
  end

  return entries[index + 1]
end

-- The next int32 - which also serves enums, and reference indexes.
function Cursor:nextI32()
  self.rowsRemaining = self.rowsRemaining - 1
  local encoding = self.encoding

  if encoding == ENCODING_BITPACK then
    return ops.to32(self:nextPacked())
  end

  if encoding == ENCODING_RAW then
    if self.element == tcb.ELEMENT_I32 then
      return self.reader:readI32()
    end

    return self.reader:readOptimalInt32()
  end

  if encoding == ENCODING_VARINT then
    return self.reader:readOptimalInt32()
  end

  if encoding == ENCODING_DELTA then
    -- The addition wraps on purpose, mirroring the writer's wrapping subtraction;
    -- together they are exact for every int32 pair.
    if self.started then
      self.previous = ops.add32(self.previous, self.reader:readOptimalInt32())
    else
      self.previous = self.reader:readOptimalInt32()
      self.started = true
    end

    return self.previous
  end

  if encoding == ENCODING_RLE then
    if self.runRemaining == 0 then
      read_run(self)
    end

    self.runRemaining = self.runRemaining - 1

    return self.runValue
  end

  -- ENCODING_DELTA_RLE; checkColumn refused everything else.
  if not self.started then
    self.previous = self.reader:readOptimalInt32()
    self.started = true

    return self.previous
  end

  if self.runRemaining == 0 then
    read_run(self)
  end

  self.runRemaining = self.runRemaining - 1
  self.previous = ops.add32(self.previous, self.runValue)

  return self.previous
end

-- An int64 member: from an i64 column raw or through its dictionary, and from anything
-- narrower by decoding an int32 and widening it.
function Cursor:nextI64()
  if self.element ~= tcb.ELEMENT_I64 then
    return self:nextI32()
  end

  if self.encoding == ENCODING_BITPACK then
    self.rowsRemaining = self.rowsRemaining - 1

    return self:nextPacked()
  end

  if self.valueDictionary ~= nil then
    return ops.i64(self:nextValueEntry(), 0)
  end

  self.rowsRemaining = self.rowsRemaining - 1

  return self.reader:readI64()
end

-- A float member: raw, the dictionary entry's exact bit pattern, or a whole number.
function Cursor:nextF32()
  if self.wholeNumbers then
    return ops.tofloat(self:nextI32())
  end

  if self.valueDictionary ~= nil then
    return ops.f32(self:nextValueEntry(), 0)
  end

  self.rowsRemaining = self.rowsRemaining - 1

  return self.reader:readF32()
end

-- A double member: from f64 or f32 - either of them raw or dictionary encoded - and
-- from an i32 column by decoding and widening.
function Cursor:nextF64()
  if self.wholeNumbers then
    return ops.tofloat(self:nextI32())
  end

  if self.element == tcb.ELEMENT_F64 then
    if self.valueDictionary ~= nil then
      return ops.f64(self:nextValueEntry(), 0)
    end

    self.rowsRemaining = self.rowsRemaining - 1

    return self.reader:readF64()
  end

  if self.element == tcb.ELEMENT_F32 then
    return self:nextF32()
  end

  return ops.tofloat(self:nextI32())
end

-- A bool member: one byte raw, a run of them, or one packed bit.
function Cursor:nextBool()
  if self.encoding == ENCODING_RLE or self.encoding == ENCODING_BITPACK then
    return self:nextI32() ~= 0
  end

  self.rowsRemaining = self.rowsRemaining - 1

  return self.reader:readBool()
end

-- The bytes of the next row's dictionary entry, for a fixed-width element.
function Cursor:nextValueEntry()
  self.rowsRemaining = self.rowsRemaining - 1

  local index

  if self.encoding == ENCODING_DICT then
    index = self.reader:readCounter32()
  else
    -- ENCODING_DICT_RLE; a fixed-width element reaches no other encoding here.
    if self.runRemaining == 0 then
      read_run(self)
    end

    self.runRemaining = self.runRemaining - 1
    index = self.runValue
  end

  return dictionary_entry(self, self.valueDictionary, index)
end

-- The next string - the dictionary's instance where the block has one.
function Cursor:nextString()
  self.rowsRemaining = self.rowsRemaining - 1

  if self.encoding == ENCODING_RAW then
    return self.reader:readString()
  end

  if self.encoding == ENCODING_DICT or self.encoding == ENCODING_DICT_FRONT then
    return dictionary_entry(self, self.dictionary, self.reader:readCounter32())
  end

  -- ENCODING_DICT_RLE and ENCODING_DICT_FRONT_RLE, whose dictionaries differ only in
  -- how they were written down.
  if self.runRemaining == 0 then
    read_run(self)
  end

  self.runRemaining = self.runRemaining - 1

  return dictionary_entry(self, self.dictionary, self.runValue)
end

-- Up to `limit` rows that all hold the next value, as (count, value). Always at least
-- 1. This is what makes a run cost one call instead of one per row; an encoding that
-- cannot promise sameness cheaply answers 1.
function Cursor:nextSameI32(limit)
  local encoding = self.encoding

  if encoding == ENCODING_RLE then
    self.rowsRemaining = self.rowsRemaining - 1

    if self.runRemaining == 0 then
      read_run(self)
    end

    local n = self.runRemaining < limit and self.runRemaining or limit
    self.runRemaining = self.runRemaining - n
    self.rowsRemaining = self.rowsRemaining - (n - 1)

    return n, self.runValue
  end

  if encoding == ENCODING_DELTA_RLE and self.started then
    self.rowsRemaining = self.rowsRemaining - 1

    if self.runRemaining == 0 then
      read_run(self)
    end

    if self.runValue == 0 then
      -- A zero-delta run is a run of one value.
      local n = self.runRemaining < limit and self.runRemaining or limit
      self.runRemaining = self.runRemaining - n
      self.rowsRemaining = self.rowsRemaining - (n - 1)

      return n, self.previous
    end

    self.runRemaining = self.runRemaining - 1
    self.previous = ops.add32(self.previous, self.runValue)

    return 1, self.previous
  end

  return 1, self:nextI32()
end

-- The string counterpart of nextSameI32.
function Cursor:nextSameString(limit)
  if self.encoding == ENCODING_DICT_RLE or self.encoding == ENCODING_DICT_FRONT_RLE then
    self.rowsRemaining = self.rowsRemaining - 1

    if self.runRemaining == 0 then
      read_run(self)
    end

    local n = self.runRemaining < limit and self.runRemaining or limit
    self.runRemaining = self.runRemaining - n
    self.rowsRemaining = self.rowsRemaining - (n - 1)

    return n, dictionary_entry(self, self.dictionary, self.runValue)
  end

  return 1, self:nextString()
end

---------------------------------------------------------------------------------------
-- The table header, presence bitmaps, and the checks the generated code calls.
---------------------------------------------------------------------------------------

-- Reads and checks a table file's header. Returns (row_count, columns): the column
-- descriptors the data blocks follow.
function tcb.readTableHeader(reader)
  -- Checked again here rather than only in `open`, because a reader can be handed bytes
  -- that never went through it.
  if reader:readU32() ~= 0x00424354 then
    fail("the file does not begin with the table file signature")
  end

  local version = reader:readU32()

  if version ~= tcb.FORMAT_VERSION then
    fail("table format version %d is not supported (expected %d)", version, tcb.FORMAT_VERSION)
  end

  -- Bit 0 included, not only the bits above it. `open` clears it on a file it has
  -- decrypted, so meeting it set here means the bytes reached the reader without the
  -- key. Any other bit is a feature this build was written before.
  local flags = reader:readU8()

  if flags % 2 == 1 then
    fail("the table is encrypted and was not decrypted - pass the bytes through open first")
  end

  if flags ~= 0 then
    fail("table declares unsupported features")
  end

  -- The cipher byte, the nonce, the MAC and the key check. `open` has dealt with all
  -- four by now; what is left is to be standing at the body.
  reader:skip(tcb.HEADER_SIZE - tcb.CIPHER_OFFSET)

  local count = reader:readCounter32()

  if count < 0 then
    fail("table row count is negative")
  end

  local column_count = reader:readCounter32()

  if column_count < 0 then
    fail("table column count is negative")
  end

  local columns = {}

  for i = 1, column_count do
    local tag = reader:readCounter32()
    local wire = reader:readU8()
    local encoding = reader:readU8()
    local element_count = reader:readCounter32()
    local byte_length = reader:readU32()

    columns[i] = {
      tag = tag,
      element = wire % 16,
      kind = floor(wire / 16) % 4,
      encoding = encoding,
      count = element_count,
      byteLength = byte_length,

      -- Whether the block begins with one presence bit per row, low bit first - part of
      -- the column's shape rather than a detail of its contents.
      nullable = floor(wire / 64) % 2 == 1,

      -- Whether the block states, per element, which of an array's places hold a value.
      -- Independent of `nullable`. spec/nullable-array-elements.md.
      elementNullable = floor(wire / 128) % 2 == 1,
    }
  end

  -- What the descriptors say about the file, checked before anybody allocates for the
  -- row count. The blocks are all that follows the header, so their declared lengths
  -- have to add up to the bytes left. A raw block also costs at least one byte per row,
  -- so a larger row count is one the exporter could not have written.
  local available = reader:remaining()
  local declared = 0

  for i = 1, column_count do
    local column = columns[i]

    if column.byteLength < 0 or column.byteLength > available - declared then
      fail("column tag %d declares %d bytes, which the file cannot hold",
        column.tag, column.byteLength)
    end

    declared = declared + column.byteLength

    if column.encoding == ENCODING_RAW and count > column.byteLength then
      fail("the row count %d is larger than column tag %d can hold in its %d bytes",
        count, column.tag, column.byteLength)
    end

    -- The same floor for the element count, which the read now allocates for: a fixed
    -- array's length is the file's rather than the generated code's. Only with rows to
    -- read - an empty table writes its columns' counts into a block of no bytes.
    if column.encoding == ENCODING_RAW and count > 0 and column.count > column.byteLength then
      fail("column tag %d says each row holds %d elements, which its %d bytes cannot hold",
        column.tag, column.count, column.byteLength)
    end
  end

  if declared ~= available then
    fail("the columns declare %d bytes but %d follow the header", declared, available)
  end

  return count, columns
end

-- A nullable column's presence bitmap, which sits at the front of its block. Nil for a
-- column that is not optional, which is what lets the generated code call isPresent
-- without testing first.
function tcb.readPresence(reader, column, row_count)
  if not column.nullable then
    return nil
  end

  -- The bitmap is a bit-packed boolean column of width one, so it carries an encoding
  -- byte and is laid out by the same choice a packed value block uses.
  local encoding = reader:readU8()

  return reader:readByteStream(encoding, floor((row_count + 7) / 8), "a presence bitmap")
end

-- A column's element bitmap, behind the row bitmap and in front of the values. Its
-- length is written ahead of it, because a variable-length column's total is the sum of
-- its row lengths and those live inside the value block.
function tcb.readElementPresence(reader, column)
  if not column.elementNullable then
    return nil
  end

  local elements = reader:readCounter32()
  local encoding = reader:readU8()

  return reader:readByteStream(
    encoding, floor((elements + 7) / 8), "an element presence bitmap")
end

-- Whether a row has a value, for a column that says which do. `row` is 0-based, being a
-- wire position rather than a Lua index. A nil bitmap means the column is not optional,
-- and then every row has one.
function tcb.isPresent(presence, row)
  if presence == nil then
    return true
  end

  return ops.bittest(presence[floor(row / 8) + 1], row % 8)
end

local ELEMENT_NAMES = {
  [0] = "varint", "bool", "i32", "i64", "f32", "f64", "string", "uuid",
}

-- The (element, encoding) pairs the spec defines.
local function encoding_supported(column)
  local encoding = column.encoding

  if encoding == ENCODING_RAW then
    return true
  end

  -- An array's block says what its elements use, and the element encoding is checked as
  -- it is read rather than here.
  if column.kind ~= tcb.KIND_SCALAR then
    return encoding == ENCODING_ARRAY
  end

  local element = column.element

  if element == tcb.ELEMENT_BOOL or element == tcb.ELEMENT_VARINT then
    return encoding == ENCODING_RLE or encoding == ENCODING_BITPACK
  end

  if element == tcb.ELEMENT_I32 then
    return (encoding >= ENCODING_VARINT and encoding <= ENCODING_DELTA_RLE)
      or encoding == ENCODING_BITPACK
  end

  if element == tcb.ELEMENT_I64 then
    return encoding == ENCODING_DICT or encoding == ENCODING_DICT_RLE
      or encoding == ENCODING_BITPACK
  end

  if element == tcb.ELEMENT_F32 or element == tcb.ELEMENT_F64 then
    return encoding == ENCODING_DICT or encoding == ENCODING_DICT_RLE
      or encoding == ENCODING_WHOLE
  end

  if element == tcb.ELEMENT_STRING then
    return (encoding >= ENCODING_DICT and encoding <= ENCODING_DICT_FRONT_RLE)
      or encoding == ENCODING_DICT_SEGMENT or encoding == ENCODING_DICT_SEGMENT_RLE
  end

  return false
end

-- That a column is what the generated member expects, or a lossless promotion.
-- Refusal is by name and both types, never by reading anyway. `accepted` is an array of
-- element constants.
function tcb.checkColumn(column, field_name, kind, count, nullable, accepted, element_nullable)
  if column.elementNullable ~= (element_nullable or false) then
    fail("%s: the file and the generated member disagree about whether this column's " ..
      "elements are optional. The schema changed; regenerate the code or rebuild the data.",
      field_name)
  end

  -- Nullability is part of the shape: a file that says optional puts a presence bitmap
  -- in front of the block, and code not expecting one would read the bitmap as values.
  if column.nullable ~= nullable then
    fail("%s: the file and the generated member disagree about whether this column is " ..
      "optional. The schema changed; regenerate the code or rebuild the data.", field_name)
  end

  -- A negative count says the member claims no length: how many elements a row holds is
  -- what the file states. The kind is still the member's claim.
  if column.kind ~= kind
    or (kind ~= tcb.KIND_VAR_ARRAY and count >= 0 and column.count ~= count) then
    fail("%s: the file's column (kind %d, count %d) does not match the generated member " ..
      "(kind %d, count %d). The schema changed shape; regenerate the code or rebuild the data.",
      field_name, column.kind, column.count, kind, count)
  end

  if not encoding_supported(column) then
    fail("%s: the file's column uses encoding %d, which this reader cannot decode for " ..
      "its element type. Regenerate the code or rebuild the data.", field_name, column.encoding)
  end

  for i = 1, #accepted do
    if column.element == accepted[i] then
      return
    end
  end

  local names = {}
  for i = 1, #accepted do
    names[i] = ELEMENT_NAMES[accepted[i]]
  end

  fail("%s: the file carries element type %d, which this member cannot read (accepts %s). " ..
    "The column changed type incompatibly; regenerate the code or rebuild the data.",
    field_name, column.element, table.concat(names, ", "))
end

-- That a block was consumed exactly. A mismatch is a format disagreement, and stopping
-- here names the column instead of corrupting the next.
function tcb.checkBlockEnd(reader, column, expected_end)
  if reader.position ~= expected_end then
    fail("column tag %d: its block declared %d bytes but the read ended %d bytes short " ..
      "of its boundary", column.tag, column.byteLength, expected_end - reader.position)
  end
end

---------------------------------------------------------------------------------------
-- The envelope: signature, MAC, decryption.
---------------------------------------------------------------------------------------

-- The native module, required only when a file actually needs it - the Lua counterpart
-- of the Python reader importing `cryptography` inside the function that decrypts.
local function native_module(what)
  local ok, native = pcall(require, _prefix .. "native")

  if ok then
    return native
  end

  fail("%s needs the tabbit.native module, and it is not loadable - compile " ..
    "tabbit/native/tabbit_native.c into your host or build it as a Lua module", what)
end

-- A file's plaintext bytes, checked against its MAC on the way. Call this on the bytes
-- before handing them to a reader; a file that is neither encrypted nor authenticated
-- comes back untouched, so the call belongs in the load path either way.
--
-- The heavy work - HMAC over the file, the ChaCha20 keystream - is C, in tabbit.native:
-- a byte-at-a-time Lua loop cannot afford either pass. What stays here is the decision
-- of whether that work is needed at all, so a project using neither feature never loads
-- the module.
function tcb.open(data, key, mac_key, verify_mac)
  if verify_mac == nil then
    verify_mac = true
  end

  if #data < tcb.HEADER_SIZE then
    fail("the file is too short to be a table")
  end

  if sub(data, 1, 4) ~= tcb.MAGIC then
    fail("the file does not begin with the table file signature")
  end

  local checking = verify_mac and mac_key ~= nil and #mac_key > 0
  local encrypted = byte(data, tcb.FLAGS_OFFSET + 1) % 2 == 1

  if not checking and not encrypted then
    return data
  end

  local what = encrypted and "this file is encrypted, and decrypting one"
    or "this project sets a MAC key, and verifying a file"

  return native_module(what).open(data, key, mac_key, verify_mac)
end

-- Reads a whole file into memory.
function tcb.readAllBytes(filename)
  local handle, err = io.open(filename, "rb")

  if handle == nil then
    fail("cannot open %s: %s", filename, err or "unknown error")
  end

  local data = handle:read("*a")
  handle:close()

  if data == nil then
    fail("cannot read %s", filename)
  end

  return data
end

return tcb
