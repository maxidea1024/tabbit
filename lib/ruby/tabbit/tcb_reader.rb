# Tabbit's binary reader.
#
# Copied in beside the generated accessor so the emitted code needs nothing
# installed. Edit it in the Tabbit repository.
#
#
# Reads the .tcb files Tabbit's binary exporter writes:
#
#   fixed8      one byte
#   fixed32     four bytes, little endian
#   fixed64     eight bytes, little endian
#   varint32    seven bits per byte, high bit set while more bytes follow,
#               at most five bytes
#   counter32   zig-zag encoded int32 written as a varint32
#   string      counter32 byte length, then that many UTF-8 bytes
#
# One of several readers of one format the exporter defines. The conformance corpus is
# what keeps them agreeing.
#
# Ruby's Integer is arbitrary precision, so a 64-bit value needs no special handling on
# the way in. It has no single-precision float, so a float32 read widens to a Float,
# which is a double - the value is exactly the one stored, held in a wider type.

module Tabbit
  # Stamped at the head of every table file by the exporter.
  # The format is column-oriented and self-describing: the header names every column
  # and how long its block is, and a reader that meets a version it does not know stops
  # rather than guessing.
  # 102 replaced 101 outright - a descriptor gained its encoding byte - before any
  # 101 file had shipped. 104 is the current one: four encodings joined the nine, and
  # the flags byte gained a meaning.
  FORMAT_VERSION = 107

  # The wire element types and kinds, as a column descriptor spells them.
  ELEMENT_VARINT = 0
  ELEMENT_BOOL = 1
  ELEMENT_I32 = 2
  ELEMENT_I64 = 3
  ELEMENT_F32 = 4
  ELEMENT_F64 = 5
  ELEMENT_STRING = 6
  ELEMENT_UUID = 7

  KIND_SCALAR = 0
  KIND_ARRAY = 1

  # How a block's values are laid out. Raw is the layout 101 had; the others compress
  # a column that repeats itself. spec/wire/tcb-v102-column-encoding.md is the contract.
  ENCODING_RAW = 0
  ENCODING_VARINT = 1
  ENCODING_DELTA = 2
  ENCODING_RLE = 3
  ENCODING_DELTA_RLE = 4
  ENCODING_DICT = 5
  ENCODING_DICT_RLE = 6
  ENCODING_DICT_FRONT = 7
  ENCODING_DICT_FRONT_RLE = 8

  # Composition rather than layout. An array block names an encoding for its elements and
  # one for its rows' lengths, and a whole-number float block names the integer encoding
  # its values travel under - so both are decoded by the cursor that already exists, one
  # level down, and neither adds a decode step anywhere.
  ENCODING_ARRAY = 9
  ENCODING_WHOLE = 10

  # A dictionary whose entries are built from a shared table of the pieces they are made
  # of, which reaches what two values share in the middle and at the end where front
  # coding can only reach what they share at the front.
  ENCODING_DICT_SEGMENT = 11
  ENCODING_DICT_SEGMENT_RLE = 12

  # An integer stream at the width its own range needs, over a base.
  ENCODING_BITPACK = 13

  # The file header, at fixed offsets whether or not the file is encrypted and whether or
  # not it carries a MAC. spec/wire/tcb-mac-and-signature.md.
  MAGIC_OFFSET = 0
  VERSION_OFFSET = 4
  FLAGS_OFFSET = 8
  CIPHER_OFFSET = 9
  NONCE_OFFSET = 10
  MAC_OFFSET = 22
  KEY_CHECK_OFFSET = 38

  # Where the body begins. The header before it is always this long.
  HEADER_SIZE = 42

  NONCE_SIZE = 12
  MAC_SIZE = 16

  # The four bytes every table file starts with, and the key check under the cipher.
  #
  # The same four bytes serve twice. At offset zero they are the file format signature, in
  # the clear whether or not the file is encrypted. At the key check they are under the key,
  # so a file that decrypts to something else was written with a different key - which is
  # the one thing no structural check can tell from damage.
  MAGIC = "TCB\0".b.freeze

  # Bit 0 of the flags byte: from the key check on, the file is ciphertext.
  FLAG_ENCRYPTED = 0x01

  # The cipher byte of a file that is not encrypted.
  CIPHER_NONE = 0

  # The only cipher the format defines.
  CIPHER_CHACHA20 = 1

  # One column as the file describes it.
  # `nullable` says the block begins with one presence bit per row, low bit first. Part of
  # the column's shape rather than a detail of its contents: a reader that does not expect
  # the bitmap reads it as values, so check_column refuses a disagreement the same way it
  # refuses a changed kind.
  # `element_nullable` says the block states, per element, which of an array's places hold
  # a value. Independent of `nullable`: a column may say either, or both.
  # spec/types/nullable-array-elements.md.
  Column = Struct.new(:tag, :element, :kind, :encoding, :byte_length, :nullable,
                      :element_nullable)

  # A table file is truncated, malformed, or not a table file.
  class TcbError < StandardError; end

  # A lookup for a key no row carries.
  #
  # Raised by the generated `get_by_*_or_throw` lookups, which is where a caller has
  # said the key has to be there. `find_by_*` answers the same question with nil.
  #
  # Its own class rather than TcbError: nothing is wrong with the file, and a
  # caller rescuing one of these is not rescuing the other.
  class RecordNotFoundError < StandardError; end

  # A 128 bit identifier, stored in .NET Guid byte order.
  #
  # That order is not plain big-endian: the first three components are little endian and
  # the trailing eight bytes are not, which is what to_s has to account for.
  class Uuid
    # Component order matching .NET's Guid.ToString("D").
    ORDER = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15].freeze

    attr_reader :bytes

    def initialize(bytes = ("\0" * 16).b)
      @bytes = bytes.b
    end

    def to_s
      out = +''

      ORDER.each_with_index do |index, position|
        out << '-' if [4, 6, 8, 10].include?(position)
        out << format('%02x', @bytes.getbyte(index))
      end

      out
    end

    def ==(other)
      other.is_a?(Uuid) && other.bytes == @bytes
    end

    alias eql? ==

    def hash
      @bytes.hash
    end
  end

  # Sequential reader over a table file's bytes.
  #
  # Every read either advances the cursor or raises, so a caller need not check a return
  # value.
  class Reader
    attr_reader :position

    # Advances past bytes without interpreting them: an unknown column whole block.
    # The column-oriented layout is what makes this one call the entirety of skipping.
    def skip(byte_count)
      if byte_count.negative? || byte_count > remaining
        raise TcbError, "cannot skip #{byte_count} bytes with #{remaining} remaining"
      end

      @position += byte_count
    end

    # Promotions: a member reading a file element narrower than itself. Only the
    # mathematically lossless directions exist; check_column already refused the rest.

    # An int member from i32 or varint.
    def read_i32_as(element)
      element == ELEMENT_I32 ? read_int32 : read_counter32
    end

    # A 64-bit member from i64, i32 or varint.
    def read_i64_as(element)
      case element
      when ELEMENT_I64 then read_int64
      when ELEMENT_I32 then read_int32
      else read_counter32
      end
    end

    # A float member from f64, f32 or i32 - all exact in a Ruby Float.
    def read_f64_as(element)
      case element
      when ELEMENT_F64 then read_double
      when ELEMENT_F32 then read_float
      else read_int32
      end
    end

    def initialize(data)
      @data = data.b
      @position = 0
    end

    # Bytes left to read.
    def remaining
      @data.bytesize - @position
    end

    def read_uint8
      take(1).getbyte(0)
    end

    def read_bool
      read_uint8 != 0
    end

    def read_int32
      take(4).unpack1('l<')
    end

    def read_uint32
      take(4).unpack1('L<')
    end

    def read_int64
      take(8).unpack1('q<')
    end

    # A single-precision value.
    #
    # Read as its stored bit pattern and widened to a Float, which is a double. Printing
    # it shows digits the original 32 bits never carried, which is why the conformance
    # comparison narrows before comparing.
    def read_float
      take(4).unpack1('e')
    end

    def read_double
      take(8).unpack1('E')
    end

    # Bytes, uninterpreted.
    #
    # What a column cursor reads a dictionary with: a fixed-width entry it keeps as the
    # value's own bytes, and the bytes a front-coded entry states for itself. Bounds
    # checked like every other read, because it goes through the same one place.
    # An int64 written in as few bytes as its magnitude needed, either sign.
    #
    # The base of a bit-packed block, which is a value of the column's own element type -
    # an i64 column's base does not fit in thirty-two bits. One byte when it is zero.
    def read_counter64
      encoded = 0
      shift = 0

      loop do
        piece = read_uint8
        encoded |= (piece & 0x7F) << shift
        break if (piece & 0x80).zero?

        shift += 7
        raise TcbError, 'a 64-bit variable length integer runs past ten bytes' if shift > 63
      end

      value = (encoded >> 1) ^ -(encoded & 1)
      value >= 0x8000_0000_0000_0000 ? value - 0x1_0000_0000_0000_0000 : value
    end

    # A stream of bytes under one of the integer encodings, which is what a packed block
    # and a presence bitmap both end in.
    #
    # One reader for both, so a bitmap and a packed value block cannot disagree about the
    # same bits. The count is known before the call in both cases, so nothing here reads a
    # length.
    def read_byte_stream(encoding, count, field_name)
      return Array.new(count) { read_uint8 } if encoding == ENCODING_RAW

      if encoding > ENCODING_DELTA_RLE
        raise TcbError, "#{field_name}: encoding #{encoding} cannot carry a packed byte stream"
      end

      walking = [ENCODING_DELTA, ENCODING_DELTA_RLE].include?(encoding)
      out = Array.new(count, 0)

      filled = 0
      previous = 0

      # The first value of a delta stream is written outright; the rest are steps from it.
      # A run in a delta stream repeats the step, not the value, so it walks.
      if count.positive? && walking
        previous = as_byte(read_optimal_int32, field_name)
        out[filled] = previous
        filled += 1
      end

      while filled < count
        run = 1
        step = 0
        value = 0

        case encoding
        when ENCODING_VARINT
          value = as_byte(read_optimal_int32, field_name)
        when ENCODING_DELTA
          step = read_optimal_int32
        when ENCODING_RLE
          run = read_counter32
          value = as_byte(read_optimal_int32, field_name)
        else # ENCODING_DELTA_RLE
          run = read_counter32
          step = read_optimal_int32
        end

        if run < 1 || run > count - filled
          raise TcbError,
                "#{field_name}: a run of #{run} cannot cover the #{count - filled} bytes left"
        end

        run.times do
          if walking
            previous = as_byte(wrap32(previous + step), field_name)
            out[filled] = previous
          else
            out[filled] = value
          end

          filled += 1
        end
      end

      out
    end

    # A decoded value that has to be a byte, or the block is corrupt.
    def as_byte(value, field_name)
      raise TcbError, "#{field_name}: #{value} is not a byte" if value.negative? || value > 255

      value
    end

    # A 32-bit wrapping sum, spelled out because Ruby's Integer never overflows.
    def wrap32(value)
      value &= 0xFFFFFFFF
      value >= 0x8000_0000 ? value - 0x1_0000_0000 : value
    end

    def read_bytes(count)
      take(count)
    end

    # A length-prefixed UTF-8 string.
    def read_string
      length = read_counter32
      raise TcbError, 'string length is negative' if length.negative?
      return '' if length.zero?

      take(length).force_encoding(Encoding::UTF_8)
    end

    # A timestamp as .NET ticks: 100 ns units since 0001-01-01.
    #
    # Ticks rather than a Time: a tick is finer than what Time keeps, and the corpus
    # reaches both 0001-01-01 and 9999-12-31.
    def read_datetime_ticks
      read_int64
    end

    # A duration as .NET ticks.
    def read_duration_ticks
      read_int64
    end

    def read_uuid
      Uuid.new(take(16))
    end

    # An int32 written in as few bytes as its magnitude needed, either sign.
    def read_optimal_int32
      encoded = read_varint32

      # Undoes the zig-zag fold: the low bit carried the sign.
      (encoded >> 1) ^ -(encoded & 1)
    end

    # A count, in the same encoding as read_optimal_int32.
    def read_counter32
      read_optimal_int32
    end

    # An enum value, which travels zig-zag encoded rather than fixed width.
    def read_enum
      read_optimal_int32
    end

    private

    def take(count)
      if remaining < count
        raise TcbError,
              "table data ended after #{@position} of #{@data.bytesize} bytes " \
              "while #{count} more were expected"
      end

      slice = @data.byteslice(@position, count)
      @position += count

      slice
    end

    def read_varint32
      value = 0

      shift = 0
      while shift < 35
        byte = read_uint8
        value |= (byte & 0x7F) << shift

        return value if (byte & 0x80).zero?

        shift += 7
      end

      raise TcbError, 'varint32 is longer than five bytes'
    end
  end

  # Reads one scalar column's values in row order, whatever the block's encoding.
  #
  # The generated row loop stays a row loop; this is the one place that knows how a
  # delta accumulates, how long a run has left, or that a dictionary index is a
  # reference into strings decoded once. That last one matters beyond file size: a
  # hundred-thousand-row column with three distinct strings allocates three strings,
  # not a hundred thousand - and the rows share them, which is safe because a record
  # only ever reads what it was handed.
  #
  # check_column has already refused any (element, encoding) pair the spec does not
  # define, so the dispatches here do not re-litigate that.
  class ColumnCursor
    def initialize(reader, column, row_count, field_name)
      @reader = reader
      @field_name = field_name
      @element = column.element
      @encoding = column.encoding

      # A run-length family's current run: what remains of it, and its value - which
      # is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
      @run_remaining = 0
      @run_value = 0

      # The delta family's accumulator, once @started.
      @previous = 0
      @started = false

      # Values not yet handed out. A run that claims more than this is corrupt, and
      # catching it here names the field instead of leaving it to the block-end check.
      # For an array column this counts elements, not rows.
      @rows_remaining = row_count

      # The block's dictionary, decoded once and handed out per row.
      #
      # One of the two is set when the block has a dictionary at all, chosen by the
      # element: strings are decoded to instances that rows then share, and a
      # fixed-width element keeps its raw bytes so the value is reconstructed exactly
      # as the raw layout would have read it.
      @dictionary = nil
      @value_dictionary = nil

      # How many elements each row holds, decoded up front for an encoded array column.
      #
      # Up front because the element stream follows the length stream in the block, so every
      # length has been read by the time the first element is. Nil for a raw array, whose
      # lengths are interleaved with its elements and read as they are reached.
      @lengths = nil
      @length_at = 0

      # Whether a float column's values are travelling as integers.
      @whole_numbers = false

      # A bit-packed column's bytes, decoded up front, and where in them the next value is.
      # Up front because the bytes are themselves under an encoding and a value can cross a
      # byte boundary, so handing values out one at a time would mean carrying a decoder and
      # a bit offset that disagree about where they are.
      @packed = nil
      @packed_width = 0
      @packed_base = 0
      @packed_bit = 0

      # An array column's block names an encoding for its elements and, where its rows
      # differ in length, one for the lengths. Both are encodings that already exist, so all
      # this does is read them and then go on being the element stream's cursor.
      if @encoding == ENCODING_ARRAY
        @encoding = reader.read_uint8

        if column.kind == KIND_ARRAY
          length_encoding = reader.read_uint8
          @lengths = read_lengths(reader, length_encoding, row_count, field_name)
          @rows_remaining = @lengths.sum
        end
      end

      # A bit-packed column states the width its range needs, the base subtracted from
      # every value, and which encoding carries the packed bytes. Decoded here so that
      # handing values out is a shift and an add.
      if @encoding == ENCODING_BITPACK
        width = reader.read_uint8
        base = reader.read_counter64
        inner = reader.read_uint8

        unless width.between?(1, 64)
          raise TcbError, "#{field_name}: a bit width of #{width} is not between 1 and 64"
        end

        @packed_width = width
        @packed_base = base
        @packed = reader.read_byte_stream(
          inner, (@rows_remaining * width + 7) / 8, field_name
        )

        return
      end

      # A float column whose values are all whole numbers carries them as integers and says
      # which integer encoding they travel under. From here down it is that encoding's
      # cursor, and only the handing out converts back.
      if @encoding == ENCODING_WHOLE
        inner = reader.read_uint8

        if inner < ENCODING_VARINT || inner > ENCODING_DELTA_RLE
          raise TcbError,
                "#{field_name}: encoding #{inner} cannot carry a whole-number column's values"
        end

        @encoding = inner
        @whole_numbers = true
      end

      # A segment dictionary is built once, here, and from then on the block is a dictionary
      # with an index stream like any other - so the row-by-row paths below need to know
      # nothing about it.
      if @encoding == ENCODING_DICT_SEGMENT || @encoding == ENCODING_DICT_SEGMENT_RLE
        @dictionary = read_segment_dictionary(reader, field_name)
        @encoding = @encoding == ENCODING_DICT_SEGMENT ? ENCODING_DICT : ENCODING_DICT_RLE

        return
      end

      plain_dictionary = @encoding == ENCODING_DICT || @encoding == ENCODING_DICT_RLE
      front_dictionary = @encoding == ENCODING_DICT_FRONT || @encoding == ENCODING_DICT_FRONT_RLE

      return unless plain_dictionary || front_dictionary

      count = reader.read_counter32
      raise TcbError, "#{field_name}: the dictionary entry count is negative" if count.negative?

      if front_dictionary
        @dictionary = read_front_coded_dictionary(reader, count, field_name)
      elsif @element == ELEMENT_STRING
        @dictionary = Array.new(count) { reader.read_string }
      else
        # A fixed-width element: the entries are the value's own bytes, so they are
        # taken as bytes and turned into values only when a row asks for one.
        width = @element == ELEMENT_F32 ? 4 : 8
        @value_dictionary = Array.new(count) { reader.read_bytes(width) }
      end
    end

    # How many elements the next row of an array column holds.
    #
    # One call whichever way the block is laid out. An encoded array decoded every length
    # before the first element was read, so this hands out what it already has; a raw one
    # states each row's length in front of that row's elements, so this reads it where it
    # stands.
    def next_length
      if @lengths
        if @length_at >= @lengths.length
          raise TcbError, "#{@field_name}: the column has no more rows to read"
        end

        length = @lengths[@length_at]
        @length_at += 1

        return length
      end

      length = @reader.read_counter32
      raise TcbError, "#{@field_name}: a row declares #{length} elements" if length.negative?

      length
    end

    # The next int32 - which also serves enums, and reference indexes.
    # The next value of a bit-packed stream: the packed bits, over the block's base.
    #
    # A value may cross a byte boundary, so this walks bits rather than bytes. The addition
    # wraps, mirroring the writer's wrapping subtraction.
    def next_packed
      slot = 0

      @packed_width.times do |at|
        slot |= 1 << at unless (@packed[@packed_bit >> 3] >> (@packed_bit & 7) & 1).zero?
        @packed_bit += 1
      end

      value = (@packed_base + slot) & 0xFFFF_FFFF_FFFF_FFFF
      value >= 0x8000_0000_0000_0000 ? value - 0x1_0000_0000_0000_0000 : value
    end

    def next_i32
      @rows_remaining -= 1

      return wrap_int32(next_packed) if @encoding == ENCODING_BITPACK

      case @encoding
      when ENCODING_RAW
        @element == ELEMENT_I32 ? @reader.read_int32 : @reader.read_optimal_int32
      when ENCODING_VARINT
        @reader.read_optimal_int32
      when ENCODING_DELTA
        # The addition wraps on purpose, mirroring the writer's wrapping subtraction;
        # together they are exact for every int32 pair.
        if @started
          @previous = wrap_int32(@previous + @reader.read_optimal_int32)
        else
          @previous = @reader.read_optimal_int32
          @started = true
        end

        @previous
      when ENCODING_RLE
        read_run if @run_remaining.zero?

        @run_remaining -= 1
        @run_value
      else # ENCODING_DELTA_RLE; check_column refused everything else.
        unless @started
          @previous = @reader.read_optimal_int32
          @started = true
          return @previous
        end

        read_run if @run_remaining.zero?

        @run_remaining -= 1
        @previous = wrap_int32(@previous + @run_value)
      end
    end

    # A 64-bit member: from an i64 column raw or through its dictionary, and from
    # anything narrower by decoding an int32 and widening it.
    def next_i64
      return next_i32 unless @element == ELEMENT_I64

      if @encoding == ENCODING_BITPACK
        @rows_remaining -= 1
        return next_packed
      end

      return next_value_entry.unpack1('q<') if @value_dictionary

      @rows_remaining -= 1
      @reader.read_int64
    end

    # A single-precision member: raw, the dictionary entry's exact bit pattern, or a
    # whole number.
    #
    # Where the block is raw or dictionary encoded the 32 stored bits widen to a Float,
    # which is a double - the value is the one stored, held in a wider type. A whole-number
    # block hands back the Integer it carried, as the promotion from an i32 column below
    # already does.
    def next_f32
      return next_i32 if @whole_numbers
      return next_value_entry.unpack1('e') if @value_dictionary

      @rows_remaining -= 1
      @reader.read_float
    end

    # A float member: from f64 or f32 - either of them raw or dictionary-encoded - and
    # from an i32 column by decoding and widening.
    def next_f64
      return next_i32 if @whole_numbers

      case @element
      when ELEMENT_F64
        return next_value_entry.unpack1('E') if @value_dictionary

        @rows_remaining -= 1
        @reader.read_double
      when ELEMENT_F32
        next_f32
      else
        next_i32
      end
    end

    # A bool member: one byte raw, or a run of them.
    def next_bool
      return next_i32 != 0 if [ENCODING_RLE, ENCODING_BITPACK].include?(@encoding)

      @rows_remaining -= 1
      @reader.read_bool
    end

    # The next string - the dictionary's instance where the block has one.
    def next_string
      @rows_remaining -= 1

      case @encoding
      when ENCODING_RAW
        @reader.read_string
      when ENCODING_DICT, ENCODING_DICT_FRONT
        dictionary_entry(@reader.read_counter32)
      else # ENCODING_DICT_RLE and ENCODING_DICT_FRONT_RLE
        read_run if @run_remaining.zero?

        @run_remaining -= 1
        dictionary_entry(@run_value)
      end
    end

    # Up to +limit+ rows that all hold the next value, as <tt>[count, value]</tt>.
    # Always at least 1.
    #
    # This is what makes a run cost one call instead of one per row: the generated loop
    # asks once, then assigns the value that many times. An encoding that cannot promise
    # sameness cheaply answers 1, so the caller's loop is correct over every encoding and
    # only faster over runs.
    def next_same_i32(limit)
      if @encoding == ENCODING_RLE
        @rows_remaining -= 1
        read_run if @run_remaining.zero?

        n = @run_remaining < limit ? @run_remaining : limit
        @run_remaining -= n
        @rows_remaining -= n - 1

        return [n, @run_value]
      end

      if @encoding == ENCODING_DELTA_RLE && @started
        @rows_remaining -= 1
        read_run if @run_remaining.zero?

        if @run_value.zero?
          # A zero-delta run is a run of one value.
          n = @run_remaining < limit ? @run_remaining : limit
          @run_remaining -= n
          @rows_remaining -= n - 1

          return [n, @previous]
        end

        @run_remaining -= 1
        @previous = wrap_int32(@previous + @run_value)

        return [1, @previous]
      end

      [1, next_i32]
    end

    # The string counterpart of #next_same_i32.
    def next_same_string(limit)
      if @encoding == ENCODING_DICT_RLE || @encoding == ENCODING_DICT_FRONT_RLE
        @rows_remaining -= 1
        read_run if @run_remaining.zero?

        n = @run_remaining < limit ? @run_remaining : limit
        @run_remaining -= n
        @rows_remaining -= n - 1

        return [n, dictionary_entry(@run_value)]
      end

      [1, next_string]
    end

    private

    # A sorted dictionary whose entries state only what they do not share with the
    # entry before them.
    #
    # Decoded into whole strings here rather than kept folded, because a row wants a
    # string and the folding was only ever about the bytes on disk. The allocations
    # are the strings themselves - one per distinct value, which is the point.
    def read_front_coded_dictionary(reader, count, field_name)
      previous = ''.b

      Array.new(count) do |at|
        shared = reader.read_counter32
        rest = reader.read_counter32

        if shared.negative? || rest.negative? || shared > previous.bytesize
          raise TcbError,
                "#{field_name}: dictionary entry #{at} shares #{shared} bytes with an " \
                "entry of #{previous.bytesize}"
        end

        # The bytes shared with the entry before, then the ones this entry states.
        entry = previous.byteslice(0, shared)
        entry << reader.read_bytes(rest) if rest.positive?
        previous = entry

        entry.dup.force_encoding(Encoding::UTF_8)
      end
    end

    # The lengths of an array column's rows, as their own encoded stream.
    #
    # A varint stream, so what may be chosen for it is what may be chosen for any varint
    # column - each length as a counter32, or runs of them. Most columns have rows that are
    # all the same length, which is one run.
    def read_lengths(reader, encoding, row_count, field_name)
      if encoding == ENCODING_RAW
        return Array.new(row_count) do |at|
          length = reader.read_counter32
          raise TcbError, "#{field_name}: row #{at} declares #{length} elements" if length.negative?

          length
        end
      end

      unless encoding == ENCODING_RLE
        raise TcbError,
              "#{field_name}: encoding #{encoding} cannot carry an array column's row lengths"
      end

      lengths = []

      while lengths.length < row_count
        run = reader.read_counter32
        value = reader.read_optimal_int32

        if run < 1 || run > row_count - lengths.length
          raise TcbError,
                "#{field_name}: a run of #{run} lengths cannot cover the " \
                "#{row_count - lengths.length} rows left in the column"
        end

        raise TcbError, "#{field_name}: a row declares #{value} elements" if value.negative?

        lengths.concat(Array.new(run, value))
      end

      lengths
    end

    # A dictionary whose entries are lists of references into a table of the pieces they
    # are built from.
    #
    # Two reads and a concatenation: the table, which is front coded because its own entries
    # share their fronts, and then each value as the pieces it is made of. The result is the
    # same array of whole strings every other dictionary produces, so nothing downstream of
    # here knows which kind it came from.
    def read_segment_dictionary(reader, field_name)
      segment_count = reader.read_counter32
      raise TcbError, "#{field_name}: the segment count is negative" if segment_count.negative?

      if segment_count > reader.remaining
        raise TcbError,
              "#{field_name}: a segment table of #{segment_count} entries is larger than " \
              'the file can hold'
      end

      previous = ''.b

      segments = Array.new(segment_count) do |at|
        shared = reader.read_counter32
        rest = reader.read_counter32

        if shared.negative? || rest.negative? || shared > previous.bytesize
          raise TcbError,
                "#{field_name}: segment #{at} shares #{shared} bytes with an entry of " \
                "#{previous.bytesize}"
        end

        # The bytes shared with the segment before, then the ones this one states.
        segment = previous.byteslice(0, shared)
        segment << reader.read_bytes(rest) if rest.positive?
        previous = segment

        segment
      end

      count = reader.read_counter32
      raise TcbError, "#{field_name}: the dictionary entry count is negative" if count.negative?

      if count > reader.remaining
        raise TcbError,
              "#{field_name}: a dictionary of #{count} entries is larger than the file can hold"
      end

      Array.new(count) do |at|
        pieces = reader.read_counter32

        if pieces.negative?
          raise TcbError, "#{field_name}: dictionary entry #{at} declares #{pieces} pieces"
        end

        entry = ''.b

        pieces.times do
          index = reader.read_counter32

          if index.negative? || index >= segment_count
            raise TcbError,
                  "#{field_name}: segment index #{index} is out of range - the table " \
                  "holds #{segment_count} entries"
          end

          entry << segments[index]
        end

        # The pieces are bytes; what a row wants is the string they spell.
        entry.force_encoding(Encoding::UTF_8)
      end
    end

    # The bytes of the next row's dictionary entry, for a fixed-width element.
    def next_value_entry
      @rows_remaining -= 1

      index =
        if @encoding == ENCODING_DICT
          @reader.read_counter32
        else
          read_run if @run_remaining.zero?

          @run_remaining -= 1
          @run_value
        end

      if index.negative? || index >= @value_dictionary.length
        raise TcbError,
              "#{@field_name}: dictionary index #{index} is out of range - the " \
              "dictionary holds #{@value_dictionary.length} entries"
      end

      @value_dictionary[index]
    end

    def read_run
      length = @reader.read_counter32

      # + 1 because the row this run was read for is already counted out of
      # @rows_remaining by its next_* call.
      if length < 1 || length > @rows_remaining + 1
        raise TcbError,
              "#{@field_name}: a run of #{length} values cannot cover the " \
              "#{@rows_remaining + 1} rows left in the column"
      end

      @run_remaining = length
      @run_value = @reader.read_optimal_int32
    end

    def dictionary_entry(index)
      if index.negative? || index >= @dictionary.length
        raise TcbError,
              "#{@field_name}: dictionary index #{index} is out of range - the " \
              "dictionary holds #{@dictionary.length} entries"
      end

      @dictionary[index]
    end

    # A 32-bit wrapping sum. Ruby's Integer never overflows on its own, so the wrap
    # the format asks for is spelled out: keep the low 32 bits and sign-extend.
    def wrap_int32(value)
      value &= 0xFFFFFFFF
      value >= 0x80000000 ? value - 0x100000000 : value
    end
  end

  # Reads and checks a table file's header, returning the row count that follows it.
  #
  # The flags byte says what the bytes after it need before they are a table, and anything
  # set here is handling this build does not have - the encryption bit included. `open`
  # hands back a header with that bit cleared, so a reader still seeing it was given the
  # ciphertext without the key, and saying so beats letting the block lengths make what they
  # can of it.
  def self.read_table_header(reader)
    # Checked again here rather than only in `open`, because a reader can be handed bytes
    # that never went through it.
    unless reader.read_uint32 == MAGIC.unpack1('V')
      raise TcbError, 'the file does not begin with the table file signature'
    end

    version = reader.read_uint32

    unless version == FORMAT_VERSION
      raise TcbError,
            "table format version #{version} is not supported (expected #{FORMAT_VERSION})"
    end

    flags = reader.read_uint8

    unless (flags & FLAG_ENCRYPTED).zero?
      raise TcbError,
            'the table is encrypted and was not decrypted - pass the key through open first'
    end

    raise TcbError, 'table declares unsupported features' unless flags.zero?

    # The cipher byte, the nonce, the MAC and the key check. `open` has dealt with all four
    # by now; what is left is to be standing at the body.
    reader.skip(HEADER_SIZE - CIPHER_OFFSET)

    count = reader.read_counter32
    raise TcbError, 'table row count is negative' if count.negative?

    column_count = reader.read_counter32
    raise TcbError, 'table column count is negative' if column_count.negative?

    columns = Array.new(column_count) do
      tag = reader.read_counter32
      wire = reader.read_uint8
      encoding = reader.read_uint8
      byte_length = reader.read_uint32
      Column.new(tag, wire & 0x0F, (wire >> 4) & 0x03, encoding, byte_length,
                 (wire & 0x40) != 0, (wire & 0x80) != 0)
    end

    # What the descriptors say about the file, checked before anybody allocates for the
    # row count. The blocks are all that follows the header, so their declared lengths have
    # to add up to the bytes left. A raw block also costs at least one byte per
    # row - a varint's shortest form, an empty string's length prefix, a variable
    # array's counter - so a larger row count is one the exporter could not have
    # written. An encoded block has no such floor; its decode checks run sums and
    # dictionary bounds instead.

    available = reader.remaining
    declared = 0

    columns.each do |column|
      if column.byte_length.negative? || column.byte_length > available - declared
        raise TcbError,
              "column tag #{column.tag} declares #{column.byte_length} bytes, which the file " \
              'cannot hold'
      end

      declared += column.byte_length

      if column.encoding == ENCODING_RAW && count > column.byte_length
        raise TcbError,
              "the row count #{count} is larger than column tag #{column.tag} can hold in its " \
              "#{column.byte_length} bytes"
      end
    end

    if declared != available
      raise TcbError,
            "the columns declare #{declared} bytes but #{available} follow the header"
    end

    [count, columns]
  end

  # A nullable column's presence bitmap, which sits at the front of its block.
  #
  # Empty for a column that is not optional, which is what lets the generated code call
  # `present?` without testing first.
  def self.read_presence(reader, column, row_count)
    return [] unless column.nullable

    # The bitmap is a bit-packed boolean column of width one, so it carries an encoding
    # byte and is laid out by the same choice a packed value block uses. Its width and
    # base are known in advance, which is why it does not carry them.
    encoding = reader.read_uint8

    reader.read_byte_stream(encoding, (row_count + 7) / 8, 'a presence bitmap')
  end

  # A column's element bitmap, behind the row bitmap and in front of the values.
  #
  # Empty for a column that does not carry one. Its length is written ahead of it as a
  # counter32, because a variable-length column's total is the sum of its row lengths and
  # those live inside the value block - a reader meeting the bitmap first would have nothing
  # to size it by. spec/types/nullable-array-elements.md.
  def self.read_element_presence(reader, column)
    return [] unless column.element_nullable

    elements = reader.read_counter32
    encoding = reader.read_uint8

    reader.read_byte_stream(encoding, (elements + 7) / 8, 'an element presence bitmap')
  end

  # Whether a row has a value, for a column that says which do.
  #
  # An empty bitmap means the column is not optional, and then every row has one.
  def self.present?(presence, row)
    presence.empty? || (presence[row >> 3] & (1 << (row & 7))) != 0
  end

  # That a column is what the generated member expects, or a lossless promotion of it.
  # Refusal is by name and both types, never by reading anyway.
  def self.check_column(column, field_name, kind, nullable, accepted,
                        element_nullable = false)
    # The same statement about the other bitmap: code not expecting one would read it as
    # values. spec/types/nullable-array-elements.md.
    if column.element_nullable != element_nullable
      raise TcbError,
            "#{field_name}: the file and the generated member disagree about whether this " \
            "column's elements are optional. The schema changed; regenerate the code or " \
            'rebuild the data.'
    end

    # Nullability is part of the shape: a file that says optional puts a presence bitmap in
    # front of the block, and code not expecting one would read the bitmap as values. So
    # adding or removing a `?` is a schema change like any other, caught here rather than in
    # whatever the misread bytes happened to mean.
    if column.nullable != nullable
      raise TcbError,
            "#{field_name}: the file and the generated member disagree about whether this " \
            'column is optional. The schema changed; regenerate the code or rebuild the data.'
    end

    # A negative count says the member claims no length: how many elements a row holds is
    # what the file states. The kind is still the member's claim.
    # spec/types/nullable-array-elements.md.
    if column.kind != kind
      raise TcbError,
            "#{field_name}: the file column (kind #{column.kind}) does not match the " \
            "generated member (kind #{kind}). The schema changed shape; regenerate the " \
            'code or rebuild the data.'
    end

    # An encoding this build cannot decode - or one the spec does not define for
    # this element - is refused by name, exactly like an element it cannot read.
    # An unknown column's encoding never gets here - a skip is a skip whatever the
    # block's layout.
    unless encoding_supported?(column)
      raise TcbError,
            "#{field_name}: the file's column uses encoding #{column.encoding}, which this " \
            'reader cannot decode for its element type. Regenerate the code or rebuild the data.'
    end

    return if accepted.include?(column.element)

    raise TcbError,
          "#{field_name}: the file carries element type #{column.element}, which this member " \
          "cannot read (accepts #{accepted}). The column changed type incompatibly; " \
          'regenerate the code or rebuild the data.'
  end

  # The (element, encoding) pairs the spec defines. Integers take the integer encodings,
  # strings the dictionary ones, and an array takes the composition that applies all of
  # those to its elements.
  def self.encoding_supported?(column)
    return true if column.encoding == ENCODING_RAW

    # An array's block says what its elements use, and the element encoding is checked as it
    # is read rather than here - the descriptor carries only the outer one, so this is as far
    # as the descriptor can be checked.
    return column.encoding == ENCODING_ARRAY unless column.kind == KIND_SCALAR

    case column.element
    when ELEMENT_BOOL, ELEMENT_VARINT
      column.encoding == ENCODING_RLE || column.encoding == ENCODING_BITPACK
    when ELEMENT_I32
      (column.encoding >= ENCODING_VARINT && column.encoding <= ENCODING_DELTA_RLE) ||
        column.encoding == ENCODING_BITPACK
    # The dictionary is parameterized by element, so this one reaches it with entries
    # that are simply their own raw bytes.
    when ELEMENT_I64
      column.encoding == ENCODING_DICT || column.encoding == ENCODING_DICT_RLE ||
        column.encoding == ENCODING_BITPACK
    # A float column additionally reaches the integer encodings, through the block that
    # says its values are whole numbers.
    when ELEMENT_F32, ELEMENT_F64
      column.encoding == ENCODING_DICT || column.encoding == ENCODING_DICT_RLE ||
        column.encoding == ENCODING_WHOLE
    # And a string dictionary can be front coded or built from segments, both of which
    # are meaningless for a fixed-width element and refused for one.
    when ELEMENT_STRING
      (column.encoding >= ENCODING_DICT && column.encoding <= ENCODING_DICT_FRONT_RLE) ||
        column.encoding == ENCODING_DICT_SEGMENT ||
        column.encoding == ENCODING_DICT_SEGMENT_RLE
    else
      false
    end
  end

  # That a block was consumed exactly: a mismatch is a format disagreement, and stopping
  # here names the column instead of corrupting the next.
  def self.check_block_end(reader, column, expected_end)
    return if reader.position == expected_end

    raise TcbError,
          "column tag #{column.tag}: its block declared #{column.byte_length} bytes but the " \
          "read ended #{expected_end - reader.position} bytes short of its boundary"
  end

  # Reads a whole file into memory.
  def self.read_all_bytes(filename)
    File.binread(filename)
  end

  # A file's plaintext bytes: the file itself when it is not encrypted, and what it
  # decrypts to when it is.
  #
  # Call this on the bytes before handing them to a reader. An unencrypted file comes back
  # untouched, so the call belongs in the load path whether or not the project uses a key.
  #
  # What comes back is a fresh string rather than a window onto the one that went in - Ruby
  # has no way to decrypt in place through the cipher this uses, so the copy the other
  # runtimes avoid is one this one makes. The envelope's own header - the cipher byte, the
  # nonce, the magic - has been read and checked by then, so what is put in front of the
  # body is the five bytes a reader expects: the version as it arrived, and flags with
  # nothing left in them to act on.
  #
  # What the encryption is and is not for: the key ships inside the client that reads the
  # file, so this stops a data file being read in an editor and stops an edited one loading.
  # It does not stop anyone who can take the key out of the client.
  def self.open(data, key = nil, mac_key = nil, verify_mac: true)
    raise TcbError, 'the file is too short to be a table' if data.bytesize < HEADER_SIZE

    unless data.byteslice(MAGIC_OFFSET, 4) == MAGIC
      raise TcbError, 'the file does not begin with the table file signature'
    end

    check_mac(data, mac_key) if verify_mac

    return data if (data.getbyte(FLAGS_OFFSET) & FLAG_ENCRYPTED).zero?

    cipher = data.getbyte(CIPHER_OFFSET)

    unless cipher == CIPHER_CHACHA20
      raise TcbError, "the file uses cipher #{cipher}, which this reader does not know"
    end

    if key.nil? || key.bytesize != 32
      raise TcbError,
            'the file is encrypted and no key, or a key that is not 32 bytes, was given'
    end

    plaintext = decrypt(
      key.b, data.byteslice(NONCE_OFFSET, NONCE_SIZE).b,
      data.byteslice(KEY_CHECK_OFFSET, data.bytesize - KEY_CHECK_OFFSET).b)

    unless plaintext.byteslice(0, 4) == MAGIC
      raise TcbError,
            'the file did not decrypt to a table - the key is not the one it was written with'
    end

    # Back to what a plain file holds in the fields the envelope wrote, so that a second
    # call over what this returns passes it through instead of decrypting it again.
    header = data.byteslice(0, KEY_CHECK_OFFSET).b

    header.setbyte(FLAGS_OFFSET, header.getbyte(FLAGS_OFFSET) & ~FLAG_ENCRYPTED)
    header.setbyte(CIPHER_OFFSET, CIPHER_NONE)

    NONCE_SIZE.times { |at| header.setbyte(NONCE_OFFSET + at, 0) }

    header << plaintext
  end

  # The MAC field against the file's own bytes, and against whether a key was given.
  #
  # The tag is HMAC-SHA-256 over every byte but the sixteen it lives in, truncated to those
  # sixteen. Through openssl, like the cipher - it is in the standard library, so this costs
  # a project nothing.
  #
  # What it catches is what the structural checks cannot. A block length that does not add up
  # is a malformed file and the reader says so; four other bytes in an f32 column is a
  # well-formed file holding a different number, and no check over a file's shape can tell
  # that from data that was always there.
  def self.check_mac(data, mac_key)
    # Nothing to check with. A file that carries a tag is read anyway rather than refused:
    # this reader has no way to tell whether the tag is good, and a client built before the
    # project turned MACs on is one this format has promised can still read what it is sent.
    return if mac_key.nil? || mac_key.bytesize.zero?

    raise TcbError, 'the MAC key given is not 32 bytes' unless mac_key.bytesize == 32

    carried = data.byteslice(MAC_OFFSET, MAC_SIZE).b

    if carried == ("\0" * MAC_SIZE).b
      raise TcbError,
            'the file carries no MAC and this build expects one - it was exported without ' \
            'a MAC key, or the field was cleared after it was written'
    end

    require 'openssl'

    tag = OpenSSL::HMAC.new(mac_key.b, OpenSSL::Digest.new('SHA256'))

    # Two updates: the sixteen bytes the tag lives in cannot be part of what produces it.
    # Skipping them is zeroing them without copying the file.
    tag << data.byteslice(0, MAC_OFFSET).b
    tag << data.byteslice(KEY_CHECK_OFFSET, data.bytesize - KEY_CHECK_OFFSET).b

    return if OpenSSL.secure_compare(tag.digest.byteslice(0, MAC_SIZE), carried)

    raise TcbError,
          'the file does not match its MAC - it was altered after it was exported, or it ' \
          'was signed with a different key'
  end

  # The keystream applied over the ciphertext, which for a stream cipher is also how it
  # was applied over the plaintext.
  #
  # Through openssl rather than written out here, which is the one place this format asks a
  # runtime to take what the platform has: Ruby's byte-at-a-time loop does not carry the
  # megabytes a table file is. Required where it is used rather than at the top of the file,
  # so a project that exports no encrypted data pays nothing for the layer it does not use.
  def self.decrypt(key, nonce, ciphertext)
    begin
      require 'openssl'
    rescue LoadError => e
      raise TcbError,
            'the file is encrypted, and reading it needs the openssl library, which this ' \
            "Ruby cannot load: #{e.message}"
    end

    begin
      cipher = OpenSSL::Cipher.new('chacha20')
    rescue RuntimeError => e
      raise TcbError,
            'the file is encrypted, and reading it needs ChaCha20, which this build of ' \
            "openssl does not offer: #{e.message}"
    end

    cipher.decrypt
    cipher.key = key

    # OpenSSL wants a 16 byte IV: a four byte little-endian block counter and then the
    # nonce. This format starts the counter at zero.
    cipher.iv = "\x00\x00\x00\x00".b + nonce

    cipher.update(ciphertext) + cipher.final
  end

  private_class_method :decrypt
end
