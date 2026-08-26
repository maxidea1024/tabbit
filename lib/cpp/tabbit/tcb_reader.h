// ---------------------------------------------------------------------------
// Tabbit Tcb reader for C++17.
//
// Reads the .tcb files produced by Tabbit's binary exporter. The format is
// defined by the C# writer in
// lib/Unity/TabbitForUnity/Assets/Plugins/Tabbit.Runtime, and this is a
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
// Header only, no dependencies beyond the standard library.
// ---------------------------------------------------------------------------

#ifndef TABBIT_TCB_READER_H
#define TABBIT_TCB_READER_H

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <fstream>
// For `std::hash`, which the bottom of this file specializes so a `uuid`-keyed table's
// lookup can be an unordered_map.
#include <functional>
#include <ios>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace tabbit {
/// Thrown when a table file is truncated, malformed, or not a table file.
class TcbError : public std::runtime_error {
 public:
  explicit TcbError(const std::string& what) : std::runtime_error(what) {}
};

/// Thrown by a lookup for a key no row carries.
///
/// Raised by the generated get_by_*_or_throw lookups, which is where a caller has said
/// the key has to be there. find_by_* answers the same question with nullptr.
///
/// Its own type rather than TcbError: nothing is wrong with the file, and a
/// caller catching one of these is not catching the other.
class RecordNotFound : public std::runtime_error {
 public:
  explicit RecordNotFound(const std::string& what) : std::runtime_error(what) {}
};

/// A duration of .NET ticks: one tick is 100 nanoseconds.
///
/// The wire carries ticks, so this is the period that loses nothing. std::chrono
/// converts to anything coarser for free - `std::chrono::duration_cast<std::chrono::
/// seconds>(row.cooldown)` - and refuses the conversions that would silently round,
/// which is the reason for using it rather than a bare integer.
///
/// Not std::chrono::nanoseconds: TimeSpan's own maximum is 9.2e18 ticks, and that
/// many nanoseconds overflows a 64-bit count.
using TimeSpan = std::chrono::duration<std::int64_t, std::ratio<1, 10000000>>;

/// A point in time, in ticks, on the system clock.
///
/// The wire carries .NET ticks since 0001-01-01; this counts from the Unix epoch,
/// which is what every C++ clock and every C library function agrees on. The shift
/// happens once, in the reader.
///
/// It converts to `std::chrono::system_clock::time_point` with a `time_point_cast`,
/// so `std::chrono::system_clock::to_time_t` and the rest of the standard library
/// are one call away.
using DateTime = std::chrono::time_point<std::chrono::system_clock, TimeSpan>;

/// Ticks between 0001-01-01 and the Unix epoch, which is the whole of the
/// difference between .NET's zero and everybody else's.
constexpr std::int64_t kUnixEpochTicks = 621355968000000000LL;

/// The .NET tick count of a DateTime, for talking back to something that wants one.
inline std::int64_t to_net_ticks(DateTime value) {
  return value.time_since_epoch().count() + kUnixEpochTicks;
}

/// And the other way, for a caller building a value rather than reading one.
inline DateTime from_net_ticks(std::int64_t ticks) {
  return DateTime(TimeSpan(ticks - kUnixEpochTicks));
}

/// A 128 bit identifier, stored in .NET Guid byte order.
///
/// That order is not plain big-endian: the first three components are little
/// endian and the trailing eight bytes are not, which is what to_string has to
/// account for.
struct Uuid {
  std::array<std::uint8_t, 16> bytes{};

  std::string to_string() const {
    static const char* kHex = "0123456789abcdef";

    // Component order matching .NET's Guid.ToString("D").
    static const int kOrder[16] = {3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15};

    std::string out;
    out.reserve(36);

    for (int i = 0; i < 16; ++i) {
      if (i == 4 || i == 6 || i == 8 || i == 10) out.push_back('-');

      const std::uint8_t b = bytes[static_cast<std::size_t>(kOrder[i])];
      out.push_back(kHex[b >> 4]);
      out.push_back(kHex[b & 0x0F]);
    }

    return out;
  }

  friend bool operator==(const Uuid& a, const Uuid& b) { return a.bytes == b.bytes; }
  friend bool operator!=(const Uuid& a, const Uuid& b) { return !(a == b); }
};

/// Sequential reader over a table file's bytes.
///
/// Non-owning: the buffer has to outlive the reader. Every read either advances
/// the cursor or throws, so callers never have to check a return value.
// The wire element types and kinds, as a column descriptor spells them.
constexpr std::uint8_t kElementVarint = 0;
constexpr std::uint8_t kElementBool = 1;
constexpr std::uint8_t kElementI32 = 2;
constexpr std::uint8_t kElementI64 = 3;
constexpr std::uint8_t kElementF32 = 4;
constexpr std::uint8_t kElementF64 = 5;
constexpr std::uint8_t kElementString = 6;
constexpr std::uint8_t kElementUuid = 7;

constexpr std::uint8_t kKindScalar = 0;
// The only array kind: every row states its own length. A fixed-length kind sat at 1 until
// v107 and was removed rather than kept, because a length stated once is a length the
// generated code bakes in - and then a column added to a group needs the consumer rebuilt.
constexpr std::uint8_t kKindArray = 1;

/// The little-endian scalars, read out of bytes the caller already holds.
///
/// Free functions rather than reader members because a dictionary entry is read
/// long after the cursor moved past it: the bytes are kept and turned into a value
/// only when a row asks. TcbReader's own reads are these plus a bounds check and an
/// advance, so a value taken from a dictionary and one taken from a raw block go
/// through the same arithmetic.
inline std::uint32_t load_fixed32(const std::uint8_t* at) {
  return static_cast<std::uint32_t>(at[0]) | (static_cast<std::uint32_t>(at[1]) << 8) |
         (static_cast<std::uint32_t>(at[2]) << 16) | (static_cast<std::uint32_t>(at[3]) << 24);
}

inline std::uint64_t load_fixed64(const std::uint8_t* at) {
  std::uint64_t value = 0;
  for (int i = 0; i < 8; ++i)
    value |= static_cast<std::uint64_t>(at[static_cast<std::size_t>(i)]) << (8 * i);

  return value;
}

class TcbReader {
 public:
  TcbReader(const std::uint8_t* data, std::size_t length)
      : data_(data), length_(length), position_(0) {}

  explicit TcbReader(const std::vector<std::uint8_t>& buffer)
      : TcbReader(buffer.data(), buffer.size()) {}

  std::size_t position() const { return position_; }
  std::size_t remaining() const { return length_ - position_; }

  std::uint8_t read_fixed8() {
    require(1);
    return data_[position_++];
  }

  std::uint32_t read_fixed32() {
    require(4);

    const std::uint32_t value = load_fixed32(data_ + position_);
    position_ += 4;
    return value;
  }

  std::uint64_t read_fixed64() {
    require(8);

    const std::uint64_t value = load_fixed64(data_ + position_);
    position_ += 8;
    return value;
  }

  /// Copies bytes out without interpreting them: a fixed-width dictionary entry, or
  /// the tail of a front-coded one. The caller decides later what they mean.
  void read_bytes(std::uint8_t* destination, std::size_t count) {
    require(count);

    if (count > 0) std::memcpy(destination, data_ + position_, count);
    position_ += count;
  }

  std::uint32_t read_varint32() {
    std::uint32_t value = 0;

    for (int shift = 0; shift < 35; shift += 7) {
      const std::uint8_t byte = read_fixed8();
      value |= static_cast<std::uint32_t>(byte & 0x7F) << shift;

      if ((byte & 0x80) == 0) return value;
    }

    throw TcbError("varint32 is longer than five bytes");
  }

  /// Zig-zag decoded int32: the encoding used for lengths and enum values, so
  /// that small negatives cost as little as small positives.
  // An int64 written in as few bytes as its magnitude needed, either sign.
  //
  // The base of a bit-packed block, which is a value of the column's own element type -
  // an i64 column's base does not fit in thirty-two bits. One byte when it is zero.
  std::int64_t read_counter64() {
    std::uint64_t encoded = 0;
    int shift = 0;

    for (;;) {
      const std::uint8_t piece = read_fixed8();
      encoded |= static_cast<std::uint64_t>(piece & 0x7Fu) << shift;

      if ((piece & 0x80u) == 0) break;

      shift += 7;

      if (shift > 63)
        throw TcbError("a 64-bit variable length integer runs past ten bytes");
    }

    return static_cast<std::int64_t>(encoded >> 1) ^ -static_cast<std::int64_t>(encoded & 1u);
  }

  std::int32_t read_counter32() {
    const std::uint32_t encoded = read_varint32();
    return static_cast<std::int32_t>(encoded >> 1) ^ -static_cast<std::int32_t>(encoded & 1);
  }

  // Advances past bytes without interpreting them: an unknown column's whole block.
  // The column-oriented layout is what makes this one call the entirety of skipping.
  void skip(std::int32_t byte_count) {
    if (byte_count < 0 || static_cast<std::size_t>(byte_count) > remaining()) {
      throw TcbError("cannot skip " + std::to_string(byte_count) + " bytes with " +
                            std::to_string(remaining()) + " remaining");
    }
    position_ += static_cast<std::size_t>(byte_count);
  }

  // Promotions: a member reading a file element narrower than itself. Only the
  // mathematically lossless directions exist; check_column already refused the rest.

  // An int32 member from i32 or varint.
  void read_i32_as(std::uint8_t element, std::int32_t& value) {
    if (element == kElementI32) {
      read(value);
    } else {
      value = read_counter32();
    }
  }

  // An int64 member from i64, i32 or varint.
  void read_i64_as(std::uint8_t element, std::int64_t& value) {
    if (element == kElementI64) {
      read(value);
    } else if (element == kElementI32) {
      std::int32_t narrower = 0;
      read(narrower);
      value = narrower;
    } else {
      value = read_counter32();
    }
  }

  // A double member from f64, f32 or i32 - all exact in a double.
  void read_f64_as(std::uint8_t element, double& value) {
    if (element == kElementF64) {
      read(value);
    } else if (element == kElementF32) {
      float single = 0.0f;
      read(single);
      value = single;
    } else {
      std::int32_t integer = 0;
      read(integer);
      value = integer;
    }
  }

  void read(bool& value) { value = read_fixed8() != 0; }
  void read(std::int32_t& value) { value = static_cast<std::int32_t>(read_fixed32()); }
  void read(std::uint32_t& value) { value = read_fixed32(); }
  void read(std::int64_t& value) { value = static_cast<std::int64_t>(read_fixed64()); }

  void read(float& value) {
    const std::uint32_t bits = read_fixed32();
    std::memcpy(&value, &bits, sizeof(value));
  }

  void read(double& value) {
    const std::uint64_t bits = read_fixed64();
    std::memcpy(&value, &bits, sizeof(value));
  }

  void read(std::string& value) {
    const std::int32_t length = read_counter32();
    if (length < 0) throw TcbError("string length is negative");

    require(static_cast<std::size_t>(length));

    value.assign(reinterpret_cast<const char*>(data_ + position_), static_cast<std::size_t>(length));
    position_ += static_cast<std::size_t>(length);
  }

  /// Ticks off the wire, shifted onto the Unix epoch as they arrive.
  void read(DateTime& value) {
    value = from_net_ticks(static_cast<std::int64_t>(read_fixed64()));
  }

  void read(TimeSpan& value) {
    value = TimeSpan(static_cast<std::int64_t>(read_fixed64()));
  }

  void read(Uuid& value) {
    require(16);
    std::memcpy(value.bytes.data(), data_ + position_, 16);
    position_ += 16;
  }

  /// Reads an enum as the underlying zig-zag encoded int32 the exporter writes.
  template <typename TEnum>
  void read_enum(TEnum& value) {
    value = static_cast<TEnum>(read_counter32());
  }

 private:
  void require(std::size_t count) const {
    if (remaining() < count) {
      throw TcbError("table data ended after " + std::to_string(position_) + " of " +
                            std::to_string(length_) + " bytes while " + std::to_string(count) +
                            " more were expected");
    }
  }

  const std::uint8_t* data_;
  std::size_t length_;
  std::size_t position_;
};

/// Reads a whole file into memory.
inline std::vector<std::uint8_t> read_all_bytes(const std::string& filename) {
  std::ifstream stream(filename, std::ios::binary | std::ios::ate);
  if (!stream) throw TcbError("cannot open `" + filename + "`");

  const std::streamsize size = stream.tellg();
  stream.seekg(0, std::ios::beg);

  std::vector<std::uint8_t> buffer(static_cast<std::size_t>(size));
  if (size > 0 && !stream.read(reinterpret_cast<char*>(buffer.data()), size))
    throw TcbError("cannot read `" + filename + "`");

  return buffer;
}

/// Version stamped at the head of every table file by the exporter.
// The format is column-oriented and self-describing: the header names every column
// and how long its block is, and a reader that meets a version it does not know stops
// rather than guessing. 102 replaced 101 outright - a descriptor gained its encoding
// byte - before any 101 file had shipped. 104 is the current one: four encodings joined
// the nine, and the flags byte gained a meaning.
constexpr std::uint32_t kBinaryFileFormatVersion = 107;

// How a block's values are laid out. Raw is the layout 101 had; the others compress
// a column that repeats itself. spec/wire/tcb-v102-column-encoding.md is the contract.
constexpr std::uint8_t kEncodingRaw = 0;
constexpr std::uint8_t kEncodingVarint = 1;
constexpr std::uint8_t kEncodingDelta = 2;
constexpr std::uint8_t kEncodingRle = 3;
constexpr std::uint8_t kEncodingDeltaRle = 4;
constexpr std::uint8_t kEncodingDict = 5;
constexpr std::uint8_t kEncodingDictRle = 6;
constexpr std::uint8_t kEncodingDictFront = 7;
constexpr std::uint8_t kEncodingDictFrontRle = 8;

// Composition rather than layout. An array block names an encoding for its elements and
// one for its rows' lengths, and a whole-number float block names the integer encoding its
// values travel under - so both are decoded by the cursors that already exist, one level
// down, and neither adds a decode step anywhere.
constexpr std::uint8_t kEncodingArray = 9;
constexpr std::uint8_t kEncodingWhole = 10;

// A dictionary whose entries are built from a shared table of the pieces they are made
// of, which reaches what two values share in the middle and at the end where front coding
// can only reach what they share at the front.
constexpr std::uint8_t kEncodingDictSegment = 11;
constexpr std::uint8_t kEncodingDictSegmentRle = 12;

// An integer stream at the width its own range needs, over a base.
constexpr std::uint8_t kEncodingBitpack = 13;

// The file header, at fixed offsets whether or not the file is encrypted and whether or not
// it carries a MAC. spec/wire/tcb-mac-and-signature.md.
constexpr std::size_t kMagicOffset = 0;
constexpr std::size_t kVersionOffset = 4;
constexpr std::size_t kFlagsOffset = 8;
constexpr std::size_t kCipherOffset = 9;
constexpr std::size_t kNonceOffset = 10;
constexpr std::size_t kMacOffset = 22;
constexpr std::size_t kKeyCheckOffset = 38;

/// Where the body begins. The header before it is always this long.
constexpr std::size_t kHeaderSize = 42;

constexpr std::size_t kNonceSize = 12;
constexpr std::size_t kMacSize = 16;

/// The signature, as the fixed32 it is on disk: 'S' 'C' 'B' 0, little endian.
///
/// The same four bytes serve twice. At offset zero they are the file format signature, in
/// the clear whether or not the file is encrypted. At the key check they are under the key,
/// so a file that decrypts to something else was written with a different key - which is the
/// one thing no structural check can tell from damage.
constexpr std::uint32_t kMagic = 0x00424354u;

/// Bit 0 of the flags byte: from the key check on, the file is ciphertext.
constexpr std::uint8_t kFlagEncrypted = 0x01;

/// The cipher byte of a file that is not encrypted.
constexpr std::uint8_t kCipherNone = 0;

/// The only cipher the format defines.
constexpr std::uint8_t kCipherChaCha20 = 1;

/// The ChaCha20 stream cipher of RFC 8439, as the file envelope uses it.
///
/// Here rather than from a library because the usual offering is an authenticated
/// construction, which changes the length. This format wants a plain keystream: applying
/// it leaves every byte count as it was, so the structural checks - the block lengths that
/// must sum exactly - hold over the ciphertext unchanged.
///
/// Under two hundred lines with no dependency, which is what lets the same cipher exist in
/// every runtime that has to read one of these files.
namespace chacha20 {
inline std::uint32_t rotate_left(std::uint32_t value, int count) {
  return (value << count) | (value >> (32 - count));
}

inline void quarter_round(std::uint32_t* block, int a, int b, int c, int d) {
  block[a] += block[b]; block[d] = rotate_left(block[d] ^ block[a], 16);
  block[c] += block[d]; block[b] = rotate_left(block[b] ^ block[c], 12);
  block[a] += block[b]; block[d] = rotate_left(block[d] ^ block[a], 8);
  block[c] += block[d]; block[b] = rotate_left(block[b] ^ block[c], 7);
}

/// One 64-byte keystream block: twenty rounds over a copy of the state.
inline void block(const std::uint32_t* state, std::uint8_t* keystream) {
  std::uint32_t working[16];
  std::memcpy(working, state, sizeof(working));

  // Ten double rounds. Each is four column quarter-rounds and four diagonal ones, which
  // between them let every word reach every other.
  for (int round = 0; round < 10; ++round) {
    quarter_round(working, 0, 4, 8, 12);
    quarter_round(working, 1, 5, 9, 13);
    quarter_round(working, 2, 6, 10, 14);
    quarter_round(working, 3, 7, 11, 15);

    quarter_round(working, 0, 5, 10, 15);
    quarter_round(working, 1, 6, 11, 12);
    quarter_round(working, 2, 7, 8, 13);
    quarter_round(working, 3, 4, 9, 14);
  }

  // Added back to the state it started from, which is what stops the rounds being
  // reversible and so the keystream being recoverable.
  for (int at = 0; at < 16; ++at) {
    const std::uint32_t word = working[at] + state[at];

    keystream[at * 4] = static_cast<std::uint8_t>(word);
    keystream[at * 4 + 1] = static_cast<std::uint8_t>(word >> 8);
    keystream[at * 4 + 2] = static_cast<std::uint8_t>(word >> 16);
    keystream[at * 4 + 3] = static_cast<std::uint8_t>(word >> 24);
  }
}

/// Exclusive-ors the keystream over `data`, in place.
///
/// One routine for both directions, which is what a stream cipher is: the keystream
/// depends only on the key, the nonce and the position, so applying it twice returns what
/// went in. The block counter starts at zero.
inline void apply(const std::uint8_t* key, const std::uint8_t* nonce, std::uint8_t* data,
                  std::size_t length) {
  std::uint32_t state[16];

  // "expand 32-byte k", as four little-endian words.
  state[0] = 0x61707865;
  state[1] = 0x3320646e;
  state[2] = 0x79622d32;
  state[3] = 0x6b206574;

  for (int at = 0; at < 8; ++at) state[4 + at] = load_fixed32(key + at * 4);

  state[12] = 0;

  for (int at = 0; at < 3; ++at) state[13 + at] = load_fixed32(nonce + at * 4);

  std::uint8_t keystream[64];

  for (std::size_t offset = 0; offset < length; offset += 64) {
    block(state, keystream);

    const std::size_t count = length - offset < 64 ? length - offset : 64;

    for (std::size_t at = 0; at < count; ++at) data[offset + at] ^= keystream[at];

    ++state[12];
  }
}
}  // namespace chacha20

/// HMAC-SHA-256 over the file, truncated to the sixteen bytes the header keeps for it.
///
/// Written out here for the same reason the cipher is: there is no SHA-256 in the standard
/// library, and this runtime is a single header with no dependencies.
///
/// What the tag catches is what the structural checks cannot. A block length that does not
/// add up is a malformed file and the reader says so; four other bytes in an f32 column is a
/// well-formed file holding a different number, and no check over a file's shape can tell
/// that from data that was always there.
namespace mac {
/// The round constants: the fractional parts of the cube roots of the first 64 primes.
inline const std::uint32_t* constants() {
  static const std::uint32_t k[64] = {
      0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4,
      0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe,
      0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f,
      0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
      0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc,
      0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
      0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116,
      0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
      0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7,
      0xc67178f2};

  return k;
}

inline std::uint32_t rotate_right(std::uint32_t value, int count) {
  return (value >> count) | (value << (32 - count));
}

/// One 64-byte block of the compression function.
inline void block(std::uint32_t* state, const std::uint8_t* data) {
  const std::uint32_t* k = constants();

  std::uint32_t schedule[64];

  for (int at = 0; at < 16; ++at) {
    schedule[at] = static_cast<std::uint32_t>(data[at * 4]) << 24 |
                   static_cast<std::uint32_t>(data[at * 4 + 1]) << 16 |
                   static_cast<std::uint32_t>(data[at * 4 + 2]) << 8 |
                   static_cast<std::uint32_t>(data[at * 4 + 3]);
  }

  for (int at = 16; at < 64; ++at) {
    const std::uint32_t before = schedule[at - 15];
    const std::uint32_t near = schedule[at - 2];

    const std::uint32_t s0 =
        rotate_right(before, 7) ^ rotate_right(before, 18) ^ (before >> 3);
    const std::uint32_t s1 = rotate_right(near, 17) ^ rotate_right(near, 19) ^ (near >> 10);

    schedule[at] = schedule[at - 16] + s0 + schedule[at - 7] + s1;
  }

  std::uint32_t a = state[0], b = state[1], c = state[2], d = state[3];
  std::uint32_t e = state[4], f = state[5], g = state[6], h = state[7];

  for (int at = 0; at < 64; ++at) {
    const std::uint32_t s1 = rotate_right(e, 6) ^ rotate_right(e, 11) ^ rotate_right(e, 25);
    const std::uint32_t choice = (e & f) ^ (~e & g);
    const std::uint32_t one = h + s1 + choice + k[at] + schedule[at];

    const std::uint32_t s0 = rotate_right(a, 2) ^ rotate_right(a, 13) ^ rotate_right(a, 22);
    const std::uint32_t majority = (a & b) ^ (a & c) ^ (b & c);
    const std::uint32_t two = s0 + majority;

    h = g;
    g = f;
    f = e;
    e = d + one;
    d = c;
    c = b;
    b = a;
    a = one + two;
  }

  state[0] += a;
  state[1] += b;
  state[2] += c;
  state[3] += d;
  state[4] += e;
  state[5] += f;
  state[6] += g;
  state[7] += h;
}

/// One piece of a message: hashing takes several, and joining them would copy the file.
struct Piece {
  const std::uint8_t* data;
  std::size_t length;
};

/// SHA-256 of the pieces, hashed as though they were one message.
inline void sha256(const Piece* pieces, std::size_t count, std::uint8_t* digest) {
  std::uint32_t state[8] = {0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
                            0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19};

  std::uint8_t partial[64];
  std::size_t filled = 0;
  std::uint64_t length = 0;

  for (std::size_t piece = 0; piece < count; ++piece) {
    const std::uint8_t* data = pieces[piece].data;
    const std::size_t size = pieces[piece].length;

    length += size;

    std::size_t at = 0;

    // The partial block first, then whole blocks straight out of the piece: the copy into
    // `partial` is only for the bytes that straddle a boundary.
    while (at < size) {
      if (filled == 0 && size - at >= 64) {
        block(state, data + at);
        at += 64;
        continue;
      }

      const std::size_t taking = 64 - filled < size - at ? 64 - filled : size - at;
      std::memcpy(partial + filled, data + at, taking);

      filled += taking;
      at += taking;

      if (filled == 64) {
        block(state, partial);
        filled = 0;
      }
    }
  }

  // The padding: a set bit, zeros, and the message length in bits as a 64-bit big-endian
  // number. Two blocks when the length does not fit in the one that is open.
  std::uint8_t tail[128] = {0};
  const std::size_t tail_length = filled + 9 > 64 ? 128 : 64;

  std::memcpy(tail, partial, filled);
  tail[filled] = 0x80;

  const std::uint64_t bits = length * 8;

  for (int at = 0; at < 8; ++at)
    tail[tail_length - 1 - at] = static_cast<std::uint8_t>(bits >> (at * 8));

  for (std::size_t at = 0; at < tail_length; at += 64) block(state, tail + at);

  for (int at = 0; at < 8; ++at) {
    digest[at * 4] = static_cast<std::uint8_t>(state[at] >> 24);
    digest[at * 4 + 1] = static_cast<std::uint8_t>(state[at] >> 16);
    digest[at * 4 + 2] = static_cast<std::uint8_t>(state[at] >> 8);
    digest[at * 4 + 3] = static_cast<std::uint8_t>(state[at]);
  }
}

/// The tag for a file: HMAC-SHA-256 over every byte but the sixteen the tag lives in.
///
/// Skipping them is the same as zeroing them and cheaper by a copy of the file.
inline void tag(const std::uint8_t* key, std::size_t key_length, const std::uint8_t* data,
                std::size_t length, std::uint8_t* out) {
  std::uint8_t block_key[64] = {0};

  // A key longer than the block is hashed first; ours is thirty-two bytes, but the rule is
  // part of HMAC and leaving it out would make this agree with nothing.
  if (key_length > 64) {
    const Piece whole[1] = {{key, key_length}};
    sha256(whole, 1, block_key);
  } else {
    std::memcpy(block_key, key, key_length);
  }

  std::uint8_t inner[64];
  std::uint8_t outer[64];

  for (int at = 0; at < 64; ++at) {
    inner[at] = static_cast<std::uint8_t>(block_key[at] ^ 0x36);
    outer[at] = static_cast<std::uint8_t>(block_key[at] ^ 0x5c);
  }

  std::uint8_t inner_digest[32];

  const Piece message[3] = {{inner, 64},
                            {data, kMacOffset},
                            {data + kKeyCheckOffset, length - kKeyCheckOffset}};

  sha256(message, 3, inner_digest);

  std::uint8_t full[32];
  const Piece outer_message[2] = {{outer, 64}, {inner_digest, 32}};

  sha256(outer_message, 2, full);
  std::memcpy(out, full, kMacSize);
}
}  // namespace mac

/// A file's plaintext bytes, checked against its MAC on the way.
///
/// Call this on the bytes before handing them to a reader. A file that is neither encrypted
/// nor authenticated comes back untouched, so the call belongs in the load path whether or
/// not the project uses either.
///
/// The order is verify, then decrypt. The tag covers the file as it is stored, so an altered
/// file is refused before the key is used on it, and the header - the flags, the cipher
/// byte, the nonce - is covered along with the body.
///
/// Decryption happens in place, and what comes back points into the caller's own buffer
/// rather than a copy of it - so that buffer has to outlive the reading. The fields it
/// consumes are returned to what a plain file has in them, so calling it twice on the same
/// buffer is the same as calling it once.
///
/// What the two layers are and are not for: both keys ship inside the client that reads the
/// file. Encryption stops a data file being read in an editor; the MAC stops an edited one
/// loading. Neither stops anyone who can take the keys out of the client, and no format does.
///
/// `mac_key` is empty when the project does not sign its files. A reader that has one
/// refuses a file that carries no MAC: the field being zero is how a file says it is
/// unauthenticated, so accepting that from a project that signs its files would put the
/// check sixteen zero bytes away from being removed.
///
/// `verify_mac` false skips the check. For tools and for measuring load time - and no weaker
/// than it looks, because anyone who can flip this flag in a shipped binary can read the key
/// out of the same binary.
inline std::pair<const std::uint8_t*, std::size_t> open(
    std::vector<std::uint8_t>& data, const std::vector<std::uint8_t>& key,
    const std::vector<std::uint8_t>& mac_key = {}, bool verify_mac = true) {
  if (data.size() < kHeaderSize) throw TcbError("the file is too short to be a table");

  if (load_fixed32(data.data() + kMagicOffset) != kMagic)
    throw TcbError("the file does not begin with the table file signature");

  if (verify_mac && !mac_key.empty()) {
    // Nothing to check with when the key is empty, and a file that carries a tag is read
    // anyway rather than refused: a client built before the project turned MACs on is one
    // this format has promised can still read what it is sent.
    if (mac_key.size() != 32) throw TcbError("the MAC key given is not 32 bytes");

    bool present = false;
    for (std::size_t at = 0; at < kMacSize && !present; ++at)
      present = data[kMacOffset + at] != 0;

    if (!present) {
      throw TcbError(
          "the file carries no MAC and this build expects one - it was exported without a "
          "MAC key, or the field was cleared after it was written");
    }

    std::uint8_t expected[kMacSize];
    mac::tag(mac_key.data(), mac_key.size(), data.data(), data.size(), expected);

    // Every byte, always: a comparison that returns early tells the caller how far it got.
    std::uint8_t difference = 0;
    for (std::size_t at = 0; at < kMacSize; ++at)
      difference |= static_cast<std::uint8_t>(expected[at] ^ data[kMacOffset + at]);

    if (difference != 0) {
      throw TcbError(
          "the file does not match its MAC - it was altered after it was exported, or it "
          "was signed with a different key");
    }
  }

  if ((data[kFlagsOffset] & kFlagEncrypted) == 0) return {data.data(), data.size()};

  if (data[kCipherOffset] != kCipherChaCha20) {
    throw TcbError("the file uses cipher " + std::to_string(data[kCipherOffset]) +
                   ", which this reader does not know");
  }

  if (key.size() != 32) {
    throw TcbError("the file is encrypted and no key, or a key that is not 32 bytes, was given");
  }

  chacha20::apply(key.data(), data.data() + kNonceOffset, data.data() + kKeyCheckOffset,
                  data.size() - kKeyCheckOffset);

  if (load_fixed32(data.data() + kKeyCheckOffset) != kMagic) {
    throw TcbError("the file did not decrypt to a table - the key is not the one it was written with");
  }

  // Back to what a plain file holds in these bytes, so that a second call over the same
  // buffer passes it through instead of decrypting it again.
  // The complement as an exclusive-or against 0xFF rather than as ~: the operand promotes to
  // int, so ~ of it is a value that does not fit the byte it is assigned to.
  data[kFlagsOffset] &= static_cast<std::uint8_t>(0xFFu ^ kFlagEncrypted);
  data[kCipherOffset] = kCipherNone;

  for (std::size_t at = 0; at < kNonceSize; ++at) data[kNonceOffset + at] = 0;

  return {data.data(), data.size()};
}

// One column as the file describes it.
struct Column {
  // What identifies the column, instead of its position.
  std::int32_t tag;
  std::uint8_t element;
  std::uint8_t kind;
  // Whether the block begins with one presence bit per row, low bit first.
  //
  // Set only where the sheet marked the column optional. The values are still written for
  // every row - a row without one carries the type's empty value - so the bitmap says which
  // of those to believe and nothing about the layout after it.
  bool nullable;

  // Whether the block states, per element, which of an array's places hold a value.
  // Independent of `nullable`: a column may say either, or both.
  // spec/types/nullable-array-elements.md.
  bool element_nullable;
  // How the block's values are laid out: one of the kEncoding* constants.
  std::uint8_t encoding;
  // Total bytes of the column block - what a skip advances by.
  std::int32_t byte_length;
};

// A nullable column's presence bitmap, or an empty vector for a column that has none.
//
// Called by the generated code before the row loop: the bitmap sits at the front of the
// block and the values follow it. One bit per row, low bit first, padded to a byte.
// A decoded value that has to be a byte, or the block is corrupt.
inline std::int32_t as_byte(std::int32_t value, const char* field_name);

// A stream of bytes under one of the integer encodings, which is what a packed block and
// a presence bitmap both end in.
//
// One reader for both, so a bitmap and a packed value block cannot disagree about the
// same bits. The count is known before the call in both cases, so nothing here reads a
// length.
inline std::vector<std::uint8_t> read_byte_stream(
  TcbReader& reader, std::uint8_t encoding, std::size_t count,
  const char* field_name) {
  std::vector<std::uint8_t> out(count);

  if (encoding == kEncodingRaw) {
    for (std::size_t at = 0; at < count; ++at) out[at] = reader.read_fixed8();

    return out;
  }

  if (encoding > kEncodingDeltaRle) {
    throw TcbError(std::string(field_name) + ": encoding " + std::to_string(encoding) +
                   " cannot carry a packed byte stream");
  }

  const bool walking = encoding == kEncodingDelta || encoding == kEncodingDeltaRle;

  std::size_t filled = 0;
  std::int32_t previous = 0;

  // The first value of a delta stream is written outright; the rest are steps from it. A
  // run in a delta stream repeats the step, not the value, so it walks.
  if (count > 0 && walking) {
    previous = as_byte(reader.read_counter32(), field_name);
    out[filled++] = static_cast<std::uint8_t>(previous);
  }

  while (filled < count) {
    std::int32_t run = 1;
    std::int32_t step = 0;
    std::int32_t value = 0;

    switch (encoding) {
      case kEncodingVarint:
        value = as_byte(reader.read_counter32(), field_name);
        break;

      case kEncodingDelta:
        step = reader.read_counter32();
        break;

      case kEncodingRle:
        run = reader.read_counter32();
        value = as_byte(reader.read_counter32(), field_name);
        break;

      default:  // kEncodingDeltaRle
        run = reader.read_counter32();
        step = reader.read_counter32();
        break;
    }

    if (run < 1 || static_cast<std::size_t>(run) > count - filled) {
      throw TcbError(std::string(field_name) + ": a run of " + std::to_string(run) +
                     " cannot cover the " + std::to_string(count - filled) +
                     " bytes left");
    }

    for (std::int32_t at = 0; at < run; ++at) {
      if (walking) {
        previous = as_byte(static_cast<std::int32_t>(
                               static_cast<std::uint32_t>(previous) +
                               static_cast<std::uint32_t>(step)),
                           field_name);
        out[filled++] = static_cast<std::uint8_t>(previous);
      } else {
        out[filled++] = static_cast<std::uint8_t>(value);
      }
    }
  }

  return out;
}

// A decoded value that has to be a byte, or the block is corrupt.
inline std::int32_t as_byte(std::int32_t value, const char* field_name) {
  if (value < 0 || value > 255) {
    throw TcbError(std::string(field_name) + ": " + std::to_string(value) +
                   " is not a byte");
  }

  return value;
}

inline std::vector<std::uint8_t> read_presence(
    TcbReader& reader, const Column& column, std::size_t row_count);

inline std::vector<std::uint8_t> read_element_presence(
    TcbReader& reader, const Column& column);

// Whether a row has a value, for a column that says which do.
//
// An empty bitmap means the column is not optional and every row has one, so the generated
// code can call this unconditionally.
inline bool is_present(const std::vector<std::uint8_t>& presence, std::size_t row) {
  return presence.empty() || (presence[row >> 3] & (1u << (row & 7u))) != 0;
}

// A parsed header: the row count and the column descriptors that follow it.
struct Header {
  std::int32_t row_count;
  std::vector<Column> columns;
};

/// Reads and checks the file header, returning the row count that follows it.
///
/// Of the flags byte, only the encryption bit has a meaning today; every other bit is where
/// compression would go, and a file that sets one needs handling this build does not have.
/// The encryption bit is refused rather than accepted: `open` clears it on the plaintext it
/// returns, so a reader meeting it set was handed the ciphertext without the key, and saying
/// that beats letting the block lengths make what they can of it.
inline Header read_table_header(TcbReader& reader) {
  // Checked again here rather than only in open, because a reader can be handed bytes that
  // never went through it.
  if (reader.read_fixed32() != kMagic)
    throw TcbError("the file does not begin with the table file signature");

  const std::uint32_t version = reader.read_fixed32();
  if (version != kBinaryFileFormatVersion) {
    throw TcbError("table format version " + std::to_string(version) + " is not supported (expected " +
                          std::to_string(kBinaryFileFormatVersion) + ")");
  }

  const std::uint8_t flags = reader.read_fixed8();
  if ((flags & kFlagEncrypted) != 0) {
    throw TcbError(
        "the table is encrypted and was not decrypted - pass the key through open first");
  }

  if (flags != 0) throw TcbError("table declares unsupported features");

  // The cipher byte, the nonce, the MAC and the key check. `open` has dealt with all four by
  // now; what is left is to be standing at the body.
  reader.skip(static_cast<std::int32_t>(kHeaderSize - kCipherOffset));

  Header header;
  header.row_count = reader.read_counter32();
  if (header.row_count < 0) throw TcbError("table row count is negative");

  const std::int32_t column_count = reader.read_counter32();
  if (column_count < 0) throw TcbError("table column count is negative");

  header.columns.reserve(static_cast<std::size_t>(column_count));

  for (std::int32_t at = 0; at < column_count; ++at) {
    Column column;
    column.tag = reader.read_counter32();

    const std::uint8_t wire = reader.read_fixed8();
    column.element = static_cast<std::uint8_t>(wire & 0x0f);
    column.kind = static_cast<std::uint8_t>((wire >> 4) & 0x03);
    column.nullable = (wire & 0x40) != 0;
    column.element_nullable = (wire & 0x80) != 0;

    column.encoding = reader.read_fixed8();

    column.byte_length = static_cast<std::int32_t>(reader.read_fixed32());

    header.columns.push_back(column);
  }

  // What the descriptors say about the file, checked before anybody allocates for the
  // row count. The blocks are all that follows the header, so their declared lengths have
  // to add up to the bytes left. A raw block also costs at least one byte per row - a
  // varint's shortest form, an empty string's length prefix, a variable array's counter -
  // so a larger row count is one the exporter could not have written. An encoded block
  // has no such floor; its decode checks run sums and dictionary bounds instead.

  const std::int32_t available = static_cast<std::int32_t>(reader.remaining());
  std::int32_t declared = 0;

  for (const Column& column : header.columns) {
    if (column.byte_length < 0 || column.byte_length > available - declared) {
      throw TcbError("column tag " + std::to_string(column.tag) + " declares " +
                            std::to_string(column.byte_length) +
                            " bytes, which the file cannot hold");
    }

    declared += column.byte_length;

    if (column.encoding == kEncodingRaw && header.row_count > column.byte_length) {
      throw TcbError("the row count " + std::to_string(header.row_count) +
                            " is larger than column tag " + std::to_string(column.tag) +
                            " can hold in its " + std::to_string(column.byte_length) + " bytes");
    }

  }

  if (declared != available) {
    throw TcbError("the columns declare " + std::to_string(declared) + " bytes but " +
                          std::to_string(available) + " follow the header");
  }

  return header;
}

// The (element, encoding) pairs the spec defines. Integers take the integer encodings,
// strings the dictionary ones, and an array takes the composition that applies all of
// those to its elements.
inline bool encoding_supported(const Column& column) {
  if (column.encoding == kEncodingRaw) return true;

  // An array's block says what its elements use, and the element encoding is checked as it
  // is read rather than here - the descriptor carries only the outer one, so this is as far
  // as the descriptor can be checked.
  if (column.kind != kKindScalar) return column.encoding == kEncodingArray;

  switch (column.element) {
    case kElementBool:
    case kElementVarint:
      return column.encoding == kEncodingRle || column.encoding == kEncodingBitpack;

    case kElementI32:
      return (column.encoding >= kEncodingVarint && column.encoding <= kEncodingDeltaRle) ||
             column.encoding == kEncodingBitpack;

    // The dictionary is parameterized by element, so these three reach it with
    // entries that are simply their own raw bytes.
    case kElementI64:
      return column.encoding == kEncodingDict || column.encoding == kEncodingDictRle ||
             column.encoding == kEncodingBitpack;

    // A float column additionally reaches the integer encodings, through the block that
    // says its values are whole numbers.
    case kElementF32:
    case kElementF64:
      return column.encoding == kEncodingDict || column.encoding == kEncodingDictRle ||
             column.encoding == kEncodingWhole;

    // And a string dictionary can be front coded or built from segments, both of which are
    // meaningless for a fixed-width element and refused for one.
    case kElementString:
      return (column.encoding >= kEncodingDict && column.encoding <= kEncodingDictFrontRle) ||
             column.encoding == kEncodingDictSegment ||
             column.encoding == kEncodingDictSegmentRle;

    default:
      return false;
  }
}

// That a column is what the generated member expects, or a lossless promotion of it.
// Refusal is by name and both types, never by reading anyway.
inline void check_column(const Column& column, const char* field_name, std::uint8_t kind,
                         bool nullable,
                         std::initializer_list<std::uint8_t> accepted,
                         bool element_nullable = false) {
  // The same statement about the other bitmap: generated code not expecting one would read
  // it as values. spec/types/nullable-array-elements.md.
  if (column.element_nullable != element_nullable) {
    throw TcbError(std::string(field_name) +
                   ": the file and the generated member disagree about whether this column's"
                   " elements are optional. The schema changed; regenerate the code or"
                   " rebuild the data.");
  }

  // Nullability is part of the shape: a file that says optional puts a presence bitmap at
  // the front of the block, and generated code not expecting one would read the bitmap as
  // values. Adding or removing a `?` is a schema change like any other.
  if (column.nullable != nullable) {
    throw TcbError(std::string(field_name) +
                   ": the file and the generated member disagree about whether this column is optional"
                   "; the schema changed, regenerate the code or rebuild the data");
  }

  // Shape is the kind alone since v107. How many elements a row holds is what the file
  // states, row by row, so a group that grew a column is read rather than refused.
  if (column.kind != kind) {
    throw TcbError(std::string(field_name) +
                          ": the file's column does not match the generated member's shape; "
                          "the schema changed shape, regenerate the code or rebuild the data");
  }

  // An encoding this build cannot decode - or one the spec does not define for this
  // element - is refused by name, exactly like an element it cannot read. An unknown
  // column's encoding never gets here - a skip is a skip whatever the block's layout.
  if (!encoding_supported(column)) {
    throw TcbError(std::string(field_name) + ": the file's column uses encoding " +
                          std::to_string(column.encoding) +
                          ", which this reader cannot decode for its element type; "
                          "regenerate the code or rebuild the data");
  }

  for (const std::uint8_t candidate : accepted) {
    if (column.element == candidate) return;
  }

  throw TcbError(std::string(field_name) + ": the file carries element type " +
                        std::to_string(column.element) +
                        ", which this member cannot read; the column changed type "
                        "incompatibly, regenerate the code or rebuild the data");
}

/// Reads one scalar column's values in row order, whatever the block's encoding.
///
/// The generated row loop stays a row loop; this is the one place that knows how a
/// delta accumulates, how long a run has left, or that a dictionary index is a
/// reference into strings decoded once. That last one matters beyond file size: a
/// hundred-thousand-row column with three distinct strings decodes three strings,
/// not a hundred thousand.
///
/// check_column has already refused any (element, encoding) pair the spec does not
/// define, so the switches here do not re-litigate that.
class TcbColumnCursor {
 public:
  TcbColumnCursor(TcbReader& reader, const Column& column, std::int32_t row_count,
                  const char* field_name)
      : reader_(reader),
        field_name_(field_name),
        element_(column.element),
        encoding_(column.encoding),
        rows_remaining_(row_count) {
    // An array column's block names an encoding for its elements and, where its rows differ
    // in length, one for the lengths. Both are encodings that already exist, so all this
    // does is read them and then go on being the element stream's cursor.
    if (encoding_ == kEncodingArray) {
      encoding_ = reader.read_fixed8();

      const std::uint8_t length_encoding = reader.read_fixed8();
      read_lengths(reader, length_encoding, row_count, field_name);

      std::int64_t elements = 0;
      for (const std::int32_t length : lengths_) elements += length;

      if (elements > static_cast<std::int64_t>(INT32_MAX)) {
        throw TcbError(std::string(field_name) +
                       ": the column declares more elements than can be held");
      }

      rows_remaining_ = static_cast<std::int32_t>(elements);
    }

    // A bit-packed column states the width its range needs, the base subtracted from
    // every value, and which encoding carries the packed bytes. Decoded here so that
    // handing values out is a shift and an add.
    if (encoding_ == kEncodingBitpack) {
      const std::uint8_t width = reader.read_fixed8();
      const std::int64_t base = reader.read_counter64();
      const std::uint8_t inner = reader.read_fixed8();

      if (width < 1 || width > 64) {
        throw TcbError(std::string(field_name) + ": a bit width of " +
                       std::to_string(width) + " is not between 1 and 64");
      }

      packed_width_ = width;
      packed_base_ = base;
      packed_bit_ = 0;

      const std::int64_t bits = static_cast<std::int64_t>(rows_remaining_) * width;
      packed_ = read_byte_stream(
          reader, inner, static_cast<std::size_t>((bits + 7) / 8), field_name);

      // No early exit: the dictionary sections below test for their own encodings and a
      // packed block matches none of them, so falling through leaves them empty.
    }

    // A float column whose values are all whole numbers carries them as integers and says
    // which integer encoding they travel under. From here down it is that encoding's
    // cursor, and only the handing out converts back.
    if (encoding_ == kEncodingWhole) {
      const std::uint8_t inner = reader.read_fixed8();

      if (inner < kEncodingVarint || inner > kEncodingDeltaRle) {
        throw TcbError(std::string(field_name) + ": encoding " + std::to_string(inner) +
                       " cannot carry a whole-number column's values");
      }

      encoding_ = inner;
      whole_numbers_ = true;
    }

    // A segment dictionary is built once, here, and from then on the block is a dictionary
    // with an index stream like any other - so the row-by-row paths below need to know
    // nothing about it.
    if (encoding_ == kEncodingDictSegment || encoding_ == kEncodingDictSegmentRle) {
      read_segment_dictionary(reader, field_name);

      encoding_ = encoding_ == kEncodingDictSegment ? kEncodingDict : kEncodingDictRle;
      return;
    }

    const bool plain_dictionary = encoding_ == kEncodingDict || encoding_ == kEncodingDictRle;

    const bool front_dictionary =
        encoding_ == kEncodingDictFront || encoding_ == kEncodingDictFrontRle;

    if (!plain_dictionary && !front_dictionary) return;

    const std::int32_t count = reader.read_counter32();
    if (count < 0) {
      throw TcbError(std::string(field_name) + ": the dictionary entry count is negative");
    }

    if (front_dictionary) {
      read_front_coded_dictionary(reader, count, field_name);
      return;
    }

    if (element_ == kElementString) {
      dictionary_.resize(static_cast<std::size_t>(count));

      for (std::int32_t at = 0; at < count; ++at)
        reader.read(dictionary_[static_cast<std::size_t>(at)]);

      return;
    }

    // A fixed-width element: the entries are the value's own bytes, so they are taken
    // as bytes and turned into values only when a row asks for one - which is what
    // makes a dictionary value identical to the one a raw block would have handed back.
    value_width_ = element_ == kElementF32 ? 4 : 8;
    value_dictionary_.resize(static_cast<std::size_t>(count) * value_width_);

    // Read in one go: the entries are adjacent and fixed width, so the block is the
    // concatenation of them.
    reader.read_bytes(value_dictionary_.data(), value_dictionary_.size());
  }

  /// How many elements the next row of an array column holds.
  ///
  /// One call whichever way the block is laid out. An encoded array decoded every length
  /// before the first element was read, so this hands out what it already has; a raw one
  /// states each row's length in front of that row's elements, so this reads it where it
  /// stands.
  std::int32_t next_length() {
    if (has_lengths_) {
      if (length_at_ >= lengths_.size()) {
        throw TcbError(std::string(field_name_) + ": the column has no more rows to read");
      }

      return lengths_[length_at_++];
    }

    const std::int32_t length = reader_.read_counter32();

    if (length < 0) {
      throw TcbError(std::string(field_name_) + ": a row declares " + std::to_string(length) +
                     " elements");
    }

    return length;
  }

  /// The next int32 - which also serves enums, and reference indexes.
  // The next value of a bit-packed stream: the packed bits, over the block's base.
  //
  // A value may cross a byte boundary, so this walks bits rather than bytes. The addition
  // wraps, mirroring the writer's wrapping subtraction.
  std::int64_t next_packed() {
    std::uint64_t slot = 0;

    for (std::int32_t at = 0; at < packed_width_; ++at, ++packed_bit_) {
      if ((packed_[static_cast<std::size_t>(packed_bit_ >> 3)] >> (packed_bit_ & 7)) & 1)
        slot |= static_cast<std::uint64_t>(1) << at;
    }

    return static_cast<std::int64_t>(static_cast<std::uint64_t>(packed_base_) + slot);
  }

  std::int32_t next_i32() {
    --rows_remaining_;

    if (encoding_ == kEncodingBitpack) return static_cast<std::int32_t>(next_packed());

    switch (encoding_) {
      case kEncodingRaw: {
        if (element_ == kElementI32) {
          std::int32_t exact = 0;
          reader_.read(exact);
          return exact;
        }

        return reader_.read_counter32();
      }

      case kEncodingVarint:
        return reader_.read_counter32();

      case kEncodingDelta: {
        // The addition wraps on purpose, mirroring the writer's wrapping subtraction;
        // together they are exact for every int32 pair. Done in unsigned arithmetic,
        // because signed overflow is undefined and unsigned wraps.
        if (started_) {
          previous_ = wrapping_add(previous_, reader_.read_counter32());
        } else {
          previous_ = reader_.read_counter32();
          started_ = true;
        }

        return previous_;
      }

      case kEncodingRle: {
        if (run_remaining_ == 0) read_run();

        --run_remaining_;
        return run_value_;
      }

      default: {  // kEncodingDeltaRle; check_column refused everything else.
        if (!started_) {
          previous_ = reader_.read_counter32();
          started_ = true;
          return previous_;
        }

        if (run_remaining_ == 0) read_run();

        --run_remaining_;
        previous_ = wrapping_add(previous_, run_value_);
        return previous_;
      }
    }
  }

  /// An int64 member: from an i64 column raw or through its dictionary, and from
  /// anything narrower by decoding an int32 and widening it.
  std::int64_t next_i64() {
    if (element_ != kElementI64) return next_i32();

    if (encoding_ == kEncodingBitpack) {
      --rows_remaining_;
      return next_packed();
    }

    if (has_value_dictionary())
      return static_cast<std::int64_t>(load_fixed64(next_value_entry()));

    --rows_remaining_;

    std::int64_t exact = 0;
    reader_.read(exact);
    return exact;
  }

  /// A float member: raw, the dictionary entry's exact bit pattern, or a whole number.
  float next_f32() {
    if (whole_numbers_) return static_cast<float>(next_i32());

    if (has_value_dictionary()) {
      const std::uint32_t bits = load_fixed32(next_value_entry());

      float value = 0.0f;
      std::memcpy(&value, &bits, sizeof(value));
      return value;
    }

    --rows_remaining_;

    float value = 0.0f;
    reader_.read(value);
    return value;
  }

  /// A double member: from f64 or f32 - either of them raw or dictionary-encoded -
  /// and from an i32 column by decoding and widening.
  double next_f64() {
    if (whole_numbers_) return static_cast<double>(next_i32());

    if (element_ == kElementF64) {
      if (has_value_dictionary()) {
        const std::uint64_t bits = load_fixed64(next_value_entry());

        double exact = 0.0;
        std::memcpy(&exact, &bits, sizeof(exact));
        return exact;
      }

      --rows_remaining_;

      double exact = 0.0;
      reader_.read(exact);
      return exact;
    }

    if (element_ == kElementF32) return next_f32();

    return next_i32();
  }

  /// A bool member: one byte raw, or a run of them.
  bool next_bool() {
    if (encoding_ == kEncodingRle || encoding_ == kEncodingBitpack)
      return next_i32() != 0;

    --rows_remaining_;

    bool value = false;
    reader_.read(value);
    return value;
  }

  /// The next string - a copy of the dictionary's entry where the block has one.
  std::string next_string() {
    --rows_remaining_;

    switch (encoding_) {
      case kEncodingRaw: {
        std::string value;
        reader_.read(value);
        return value;
      }

      case kEncodingDict:
      case kEncodingDictFront:
        return dictionary_entry(reader_.read_counter32());

      default: {  // kEncodingDictRle and kEncodingDictFrontRle
        if (run_remaining_ == 0) read_run();

        --run_remaining_;
        return dictionary_entry(run_value_);
      }
    }
  }

  /// Up to `limit` rows that all hold the next value, and that value. The count is
  /// always at least 1.
  ///
  /// This is what makes a run cost one call instead of one per row: the generated loop
  /// asks once, then assigns the value that many times. An encoding that cannot promise
  /// sameness cheaply answers 1, so the caller's loop is correct over every encoding and
  /// only faster over runs.
  std::int32_t next_same_i32(std::int32_t limit, std::int32_t& value) {
    if (encoding_ == kEncodingRle) {
      rows_remaining_--;
      if (run_remaining_ == 0) read_run();

      std::int32_t n = run_remaining_ < limit ? run_remaining_ : limit;
      run_remaining_ -= n;
      rows_remaining_ -= n - 1;
      value = run_value_;

      return n;
    }

    if (encoding_ == kEncodingDeltaRle && started_) {
      rows_remaining_--;
      if (run_remaining_ == 0) read_run();

      if (run_value_ == 0) {
        // A zero-delta run is a run of one value.
        std::int32_t n = run_remaining_ < limit ? run_remaining_ : limit;
        run_remaining_ -= n;
        rows_remaining_ -= n - 1;
        value = previous_;

        return n;
      }

      run_remaining_--;
      previous_ = wrapping_add(previous_, run_value_);
      value = previous_;

      return 1;
    }

    value = next_i32();
    return 1;
  }

  /// The string counterpart of next_same_i32.
  std::int32_t next_same_string(std::int32_t limit, std::string& value) {
    if (encoding_ == kEncodingDictRle || encoding_ == kEncodingDictFrontRle) {
      rows_remaining_--;
      if (run_remaining_ == 0) read_run();

      std::int32_t n = run_remaining_ < limit ? run_remaining_ : limit;
      run_remaining_ -= n;
      rows_remaining_ -= n - 1;
      value = dictionary_entry(run_value_);

      return n;
    }

    value = next_string();
    return 1;
  }

 private:
  /// Whether the block carried a dictionary of fixed-width entries.
  ///
  /// The width, not the byte count: a dictionary of no entries is still a dictionary,
  /// and a row asking one for a value has to be told its index is out of range rather
  /// than fall through to a raw read that would misinterpret the index stream.
  bool has_value_dictionary() const { return value_width_ != 0; }

  /// A sorted dictionary whose entries state only what they do not share with the
  /// entry before them.
  ///
  /// Decoded into whole strings here rather than kept folded, because a row wants a
  /// string and the folding was only ever about the bytes on disk. The scratch buffer
  /// grows to the longest entry and is reused, so what is allocated is the strings
  /// themselves - one per distinct value, which is the point.
  void read_front_coded_dictionary(TcbReader& reader, std::int32_t count,
                                   const char* field_name) {
    dictionary_.resize(static_cast<std::size_t>(count));

    std::vector<std::uint8_t> scratch(64);
    std::int32_t previous_length = 0;

    for (std::int32_t at = 0; at < count; ++at) {
      const std::int32_t shared = reader.read_counter32();
      const std::int32_t rest = reader.read_counter32();

      if (shared < 0 || rest < 0 || shared > previous_length) {
        throw TcbError(std::string(field_name) + ": dictionary entry " + std::to_string(at) +
                       " shares " + std::to_string(shared) + " bytes with an entry of " +
                       std::to_string(previous_length));
      }

      const std::int32_t length = shared + rest;

      // Signed, so a length that overflowed int32 does not become an enormous
      // capacity; the read below refuses it by running out of bytes instead.
      if (length > static_cast<std::int32_t>(scratch.size())) {
        std::size_t capacity = scratch.size();
        while (capacity < static_cast<std::size_t>(length)) capacity *= 2;

        // Resize keeps what is already there, which is the prefix this entry shares.
        scratch.resize(capacity);
      }

      if (rest > 0) {
        reader.read_bytes(scratch.data() + static_cast<std::size_t>(shared),
                          static_cast<std::size_t>(rest));
      }

      dictionary_[static_cast<std::size_t>(at)].assign(
          reinterpret_cast<const char*>(scratch.data()), static_cast<std::size_t>(length));

      previous_length = length;
    }
  }

  /// The lengths of an array column's rows, as their own encoded stream.
  ///
  /// A varint stream, so what may be chosen for it is what may be chosen for any varint
  /// column - each length as a counter32, or runs of them. Most columns have rows that are
  /// all the same length, which is one run.
  void read_lengths(TcbReader& reader, std::uint8_t encoding, std::int32_t row_count,
                    const char* field_name) {
    lengths_.resize(static_cast<std::size_t>(row_count));
    has_lengths_ = true;

    if (encoding == kEncodingRaw) {
      for (std::int32_t at = 0; at < row_count; ++at) {
        const std::int32_t length = reader.read_counter32();

        if (length < 0) {
          throw TcbError(std::string(field_name) + ": row " + std::to_string(at) + " declares " +
                         std::to_string(length) + " elements");
        }

        lengths_[static_cast<std::size_t>(at)] = length;
      }

      return;
    }

    if (encoding != kEncodingRle) {
      throw TcbError(std::string(field_name) + ": encoding " + std::to_string(encoding) +
                     " cannot carry an array column's row lengths");
    }

    std::int32_t filled = 0;

    while (filled < row_count) {
      const std::int32_t run = reader.read_counter32();
      const std::int32_t value = reader.read_counter32();

      if (run < 1 || run > row_count - filled) {
        throw TcbError(std::string(field_name) + ": a run of " + std::to_string(run) +
                       " lengths cannot cover the " + std::to_string(row_count - filled) +
                       " rows left in the column");
      }

      if (value < 0) {
        throw TcbError(std::string(field_name) + ": a row declares " + std::to_string(value) +
                       " elements");
      }

      for (std::int32_t at = 0; at < run; ++at)
        lengths_[static_cast<std::size_t>(filled++)] = value;
    }
  }

  /// A dictionary whose entries are lists of references into a table of the pieces they are
  /// built from.
  ///
  /// Two reads and a concatenation: the table, which is front coded because its own entries
  /// share their fronts, and then each value as the pieces it is made of. The result is the
  /// same vector of whole strings every other dictionary produces, so nothing downstream of
  /// here knows which kind it came from.
  void read_segment_dictionary(TcbReader& reader, const char* field_name) {
    const std::int32_t segment_count = reader.read_counter32();

    if (segment_count < 0) {
      throw TcbError(std::string(field_name) + ": the segment count is negative");
    }

    if (static_cast<std::size_t>(segment_count) > reader.remaining()) {
      throw TcbError(std::string(field_name) + ": a segment table of " +
                     std::to_string(segment_count) +
                     " entries is larger than the file can hold");
    }

    std::vector<std::string> segments(static_cast<std::size_t>(segment_count));
    std::int32_t previous_length = 0;

    for (std::int32_t at = 0; at < segment_count; ++at) {
      const std::int32_t shared = reader.read_counter32();
      const std::int32_t rest = reader.read_counter32();

      if (shared < 0 || rest < 0 || shared > previous_length) {
        throw TcbError(std::string(field_name) + ": segment " + std::to_string(at) + " shares " +
                       std::to_string(shared) + " bytes with an entry of " +
                       std::to_string(previous_length));
      }

      std::string& segment = segments[static_cast<std::size_t>(at)];

      // Anything shared is shared with the entry before, and a positive share is itself
      // what says there is one.
      if (shared > 0) {
        segment.assign(segments[static_cast<std::size_t>(at) - 1], 0,
                       static_cast<std::size_t>(shared));
      }

      if (rest > 0) {
        segment.resize(static_cast<std::size_t>(shared + rest));
        reader.read_bytes(reinterpret_cast<std::uint8_t*>(&segment[static_cast<std::size_t>(shared)]),
                          static_cast<std::size_t>(rest));
      }

      previous_length = shared + rest;
    }

    const std::int32_t count = reader.read_counter32();

    if (count < 0) {
      throw TcbError(std::string(field_name) + ": the dictionary entry count is negative");
    }

    if (static_cast<std::size_t>(count) > reader.remaining()) {
      throw TcbError(std::string(field_name) + ": a dictionary of " + std::to_string(count) +
                     " entries is larger than the file can hold");
    }

    dictionary_.resize(static_cast<std::size_t>(count));

    for (std::int32_t at = 0; at < count; ++at) {
      const std::int32_t pieces = reader.read_counter32();

      if (pieces < 0) {
        throw TcbError(std::string(field_name) + ": dictionary entry " + std::to_string(at) +
                       " declares " + std::to_string(pieces) + " pieces");
      }

      std::string& entry = dictionary_[static_cast<std::size_t>(at)];

      for (std::int32_t piece = 0; piece < pieces; ++piece) {
        const std::int32_t index = reader.read_counter32();

        if (index < 0 || index >= segment_count) {
          throw TcbError(std::string(field_name) + ": segment index " + std::to_string(index) +
                         " is out of range - the table holds " + std::to_string(segment_count) +
                         " entries");
        }

        entry += segments[static_cast<std::size_t>(index)];
      }
    }
  }

  /// The bytes of the next row's dictionary entry, for a fixed-width element.
  const std::uint8_t* next_value_entry() {
    --rows_remaining_;

    std::int32_t index = 0;

    if (encoding_ == kEncodingDict) {
      index = reader_.read_counter32();
    } else {  // kEncodingDictRle; encoding_supported refused the front-coded ones here.
      if (run_remaining_ == 0) read_run();

      --run_remaining_;
      index = run_value_;
    }

    const std::size_t count = value_dictionary_.size() / value_width_;

    if (index < 0 || static_cast<std::size_t>(index) >= count) {
      throw TcbError(std::string(field_name_) + ": dictionary index " + std::to_string(index) +
                     " is out of range - the dictionary holds " + std::to_string(count) +
                     " entries");
    }

    return value_dictionary_.data() + static_cast<std::size_t>(index) * value_width_;
  }

  /// int32 addition modulo 2^32, matching the writer's wrapping subtraction.
  static std::int32_t wrapping_add(std::int32_t a, std::int32_t b) {
    return static_cast<std::int32_t>(static_cast<std::uint32_t>(a) +
                                     static_cast<std::uint32_t>(b));
  }

  void read_run() {
    const std::int32_t length = reader_.read_counter32();

    // + 1 because the row this run was read for is already counted out of
    // rows_remaining_ by its next_* call.
    if (length < 1 || length > rows_remaining_ + 1) {
      throw TcbError(std::string(field_name_) + ": a run of " + std::to_string(length) +
                            " values cannot cover the " + std::to_string(rows_remaining_ + 1) +
                            " rows left in the column");
    }

    run_remaining_ = length;
    run_value_ = reader_.read_counter32();
  }

  const std::string& dictionary_entry(std::int32_t index) const {
    if (index < 0 || static_cast<std::size_t>(index) >= dictionary_.size()) {
      throw TcbError(std::string(field_name_) + ": dictionary index " + std::to_string(index) +
                            " is out of range - the dictionary holds " +
                            std::to_string(dictionary_.size()) + " entries");
    }

    return dictionary_[static_cast<std::size_t>(index)];
  }

  TcbReader& reader_;
  const char* field_name_;
  std::uint8_t element_;
  std::uint8_t encoding_;

  /// The block's dictionary, decoded once and handed out per row.
  ///
  /// One of the two is filled when the block has a dictionary at all, chosen by the
  /// element: strings are decoded to values that rows then copy, and a fixed-width
  /// element keeps its raw bytes so the value is reconstructed exactly as the raw
  /// layout would have read it.
  std::vector<std::string> dictionary_;

  std::vector<std::uint8_t> value_dictionary_;
  std::size_t value_width_ = 0;

  // A run-length family's current run: what remains of it, and its value - which is
  // a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
  std::int32_t run_remaining_ = 0;
  std::int32_t run_value_ = 0;

  // The delta family's accumulator, once started_.
  std::int32_t previous_ = 0;
  bool started_ = false;

  // Values not yet handed out. A run that claims more than this is corrupt, and catching it
  // here names the field instead of leaving it to the block-end check. For an array column
  // this counts elements, not rows.
  std::int32_t rows_remaining_;

  /// How many elements each row holds, decoded up front for an encoded array column.
  ///
  /// Up front because the element stream follows the length stream in the block, so every
  /// length has been read by the time the first element is. Left unfilled for a raw array,
  /// whose lengths are interleaved with its elements and read as they are reached - which
  /// is what the flag beside it says, since a column of no rows fills nothing either way.
  std::vector<std::int32_t> lengths_;
  bool has_lengths_ = false;
  std::size_t length_at_ = 0;

  /// Whether a float column's values are travelling as integers.
  bool whole_numbers_ = false;

  /// A bit-packed column's bytes, decoded up front, and where in them the next value is.
  ///
  /// Up front because the bytes are themselves under an encoding and a value can cross a
  /// byte boundary, so handing values out one at a time would mean carrying a decoder and
  /// a bit offset that disagree about where they are.
  std::vector<std::uint8_t> packed_;
  std::int32_t packed_width_ = 0;
  std::int64_t packed_base_ = 0;
  std::int64_t packed_bit_ = 0;
};

// That a block was consumed exactly: a mismatch is a format disagreement, and stopping
// here names the column instead of corrupting the next.
inline std::vector<std::uint8_t> read_presence(
    TcbReader& reader, const Column& column, std::size_t row_count) {
  if (!column.nullable) {
    return {};
  }

  // The bitmap is a bit-packed boolean column of width one, so it carries an encoding
  // byte and is laid out by the same choice a packed value block uses. Its width and base
  // are known in advance, which is why it does not carry them.
  const std::uint8_t encoding = reader.read_fixed8();

  return read_byte_stream(reader, encoding, (row_count + 7) / 8, "a presence bitmap");
}

// A column's element bitmap, or an empty vector for a column that has none.
//
// Behind the row bitmap and in front of the values. Its length is written ahead of it as a
// counter32, because a variable-length column's total is the sum of row lengths and those
// live inside the value block - a reader meeting the bitmap first would have nothing to
// size it by. One bit per element written, in the order the block wrote them.
inline std::vector<std::uint8_t> read_element_presence(
    TcbReader& reader, const Column& column) {
  if (!column.element_nullable) {
    return {};
  }

  const std::int32_t elements = reader.read_counter32();
  const std::uint8_t encoding = reader.read_fixed8();

  return read_byte_stream(reader, encoding, (static_cast<std::size_t>(elements) + 7) / 8,
                          "an element presence bitmap");
}

inline void check_block_end(const TcbReader& reader, const Column& column,
                            std::size_t expected_end) {
  if (reader.position() != expected_end) {
    throw TcbError("column tag " + std::to_string(column.tag) +
                          ": the block's declared length and the bytes consumed disagree");
  }
}
}  // namespace tabbit

/// Lets a uuid be the key of a generated table's lookup.
///
/// A table keyed by a `uuid` generates `std::unordered_map<tabbit::Uuid, ...>`, and that
/// needs a hash as well as the `operator==` the struct already had. Without this the table
/// declares a member the standard library cannot instantiate, so the failure is a page of
/// template errors in the consuming project rather than anything naming the column.
///
/// The bytes are folded rather than hashed a byte at a time: the two halves are already
/// well-distributed - a uuid is mostly random - so combining them the way `hash_combine` does
/// is enough, and it costs two loads instead of sixteen.
/// Written by reopening `namespace std` rather than as `template <> struct std::hash<...>`:
/// the qualified form is only valid from C++17, and reopening the namespace to specialize a
/// standard template for a user's own type is allowed in every standard.
namespace std {

template <>
struct hash<tabbit::Uuid> {
  std::size_t operator()(const tabbit::Uuid& value) const noexcept {
    std::uint64_t low = 0;
    std::uint64_t high = 0;

    std::memcpy(&low, value.bytes.data(), sizeof low);
    std::memcpy(&high, value.bytes.data() + sizeof low, sizeof high);

    std::size_t seed = static_cast<std::size_t>(low);
    seed ^= static_cast<std::size_t>(high) + 0x9e3779b97f4a7c15ULL + (seed << 6) + (seed >> 2);

    return seed;
  }
};

}  // namespace std

#endif  // TABBIT_TCB_READER_H
