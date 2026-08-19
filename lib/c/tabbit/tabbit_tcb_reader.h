/* ---------------------------------------------------------------------------
 * Tabbit Tcb reader for C99.
 *
 * Reads the .tcb files produced by Tabbit's binary exporter. The format is
 * defined by the C# writer in src/Exporters/TcbWriter.cs, and this is a
 * deliberate re-implementation of the reading half of it:
 *
 *   fixed8      one byte
 *   fixed32     four bytes, little endian
 *   fixed64     eight bytes, little endian
 *   varint32    seven bits per byte, high bit set while more bytes follow,
 *               at most five bytes
 *   counter32   zig-zag encoded int32 written as a varint32, so small values
 *               of either sign cost one byte
 *   string      counter32 byte length, then that many UTF-8 bytes
 *   int32/uint32   fixed32
 *   int64          fixed64
 *   bool           fixed8, zero meaning false
 *   float/double   fixed32 / fixed64 holding the IEEE-754 bit pattern
 *   datetime       fixed64 of .NET ticks: 100 ns units since 0001-01-01
 *   timespan       fixed64 of .NET ticks
 *   uuid           sixteen bytes in .NET Guid layout
 *
 * Two things this has to answer that the other readers do not.
 *
 * Who owns the strings. Every table owns one arena; the records point into it
 * and a table is freed in one call. The alternative - a malloc per string and
 * a matching free somewhere - is how a generated API becomes a leak nobody can
 * find. The arena is a chain of blocks that are never reallocated, so a pointer
 * handed out stays valid until the whole table goes.
 *
 * How failure is reported. C has nothing to throw, so a read returns false and
 * the reader remembers why. Failure is sticky: the first read that runs out of
 * data records the reason and every read after it does nothing, which is what
 * lets generated code read a record's twenty fields in a row and ask once.
 *
 * Header only. Define TABBIT_TCB_IMPLEMENTATION in exactly one
 * translation unit before including it to get the function bodies; the
 * generated .c file does that for you.
 * ---------------------------------------------------------------------------
 */

#ifndef TABBIT_TCB_READER_H
#define TABBIT_TCB_READER_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Version stamped at the head of every table file by the exporter.
 *
 * The format is column-oriented and self-describing: the header names every column
 * and how long its block is, and a reader that meets a version it does not know
 * stops rather than guessing. 102 replaced 101 outright - a descriptor gained its
 * encoding byte - before any 101 file had shipped. 104 is the current one: four
 * encodings joined the nine, and the flags byte gained a meaning. */
#define TABBIT_BINARY_FILE_FORMAT_VERSION 106u

/* The wire element types and kinds, as a column descriptor spells them. */
#define TB_ELEMENT_VARINT 0
#define TB_ELEMENT_BOOL 1
#define TB_ELEMENT_I32 2
#define TB_ELEMENT_I64 3
#define TB_ELEMENT_F32 4
#define TB_ELEMENT_F64 5
#define TB_ELEMENT_STRING 6
#define TB_ELEMENT_UUID 7

#define TB_KIND_SCALAR 0
#define TB_KIND_FIXED_ARRAY 1
#define TB_KIND_VAR_ARRAY 2

/* How a block's values are laid out. Raw is the layout 101 had; the others compress
 * a column that repeats itself. spec/tcb-v102-column-encoding.md is the contract. */
#define TB_ENCODING_RAW 0
#define TB_ENCODING_VARINT 1
#define TB_ENCODING_DELTA 2
#define TB_ENCODING_RLE 3
#define TB_ENCODING_DELTA_RLE 4
#define TB_ENCODING_DICT 5
#define TB_ENCODING_DICT_RLE 6
#define TB_ENCODING_DICT_FRONT 7
#define TB_ENCODING_DICT_FRONT_RLE 8

/* Composition rather than layout. An array block names an encoding for its elements and
 * one for its rows' lengths, and a whole-number float block names the integer encoding
 * its values travel under - so both are decoded by the cursors that already exist, one
 * level down, and neither adds a decode step anywhere. */
#define TB_ENCODING_ARRAY 9
#define TB_ENCODING_WHOLE 10

/* A dictionary whose entries are built from a shared table of the pieces they are made
 * of, which reaches what two values share in the middle and at the end where front
 * coding can only reach what they share at the front. */
#define TB_ENCODING_DICT_SEGMENT 11
#define TB_ENCODING_DICT_SEGMENT_RLE 12
/* An integer stream at the width its own range needs, over a base. */
#define TB_ENCODING_BITPACK 13

/* The file header, at fixed offsets whether or not the file is encrypted and whether or not
 * it carries a MAC. spec/tcb-mac-and-signature.md. */
#define TB_MAGIC_OFFSET 0
#define TB_VERSION_OFFSET 4
#define TB_FLAGS_OFFSET 8
#define TB_CIPHER_OFFSET 9
#define TB_NONCE_OFFSET 10
#define TB_MAC_OFFSET 22
#define TB_KEY_CHECK_OFFSET 38

/* Where the body begins. The header before it is always this long. */
#define TB_HEADER_SIZE 42

#define TB_NONCE_SIZE 12
#define TB_MAC_SIZE 16

/* The signature, as the fixed32 it is on disk: 'S' 'C' 'B' 0, little endian.
 *
 * The same four bytes serve twice. At offset zero they are the file format signature, in the
 * clear whether or not the file is encrypted. At the key check they are under the key, so a
 * file that decrypts to something else was written with a different key - which is the one
 * thing no structural check can tell from damage. */
#define TB_MAGIC 0x00424354u

/* Bit 0 of the flags byte: from the key check on, the file is ciphertext. */
#define TB_FLAG_ENCRYPTED 0x01u

/* The cipher byte of a file that is not encrypted. */
#define TB_CIPHER_NONE 0

/* The only cipher the format defines. */
#define TB_CIPHER_CHACHA20 1

/* One element type as a bit, so the set a member accepts is one integer argument.
 * A set rather than an array because the generated code has to spell it inline, and
 * C89 has no array literal to spell it with. */
#define TB_ELEMENT_MASK(element) (1u << (element))

/* One column as the file describes it. */
typedef struct tb_column {
  /* What identifies the column, instead of its position. */
  int32_t tag;
  uint8_t element;
  uint8_t kind;
  /* Whether the block begins with one presence bit per row, low bit first.
   *
   * Set only where the sheet marked the column optional. The values are still written for
   * every row - a row without one carries the type's empty value - so the bitmap says which
   * of those to believe and nothing about the layout after it. */
  bool nullable;
  /* Whether the block states, per element, which of an array's places hold a value.
   *
   * Independent of `nullable`: a column may say either, or both.
   * spec/nullable-array-elements.md. */
  bool element_nullable;
  /* How the block's values are laid out: one of the TB_ENCODING_* constants. */
  uint8_t encoding;
  /* Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one. */
  int32_t count;
  /* Total bytes of the column block - what a skip advances by. */
  int32_t byte_length;
} tb_column;

/* Longest message the reader will keep. Truncated rather than allocated: a
 * reader that has just run out of memory is a poor time to ask for more. */
#define TABBIT_ERROR_MAX 256

/* A 128 bit identifier, in .NET Guid byte order.
 *
 * That order is not plain big-endian: the first three components are little
 * endian and the trailing eight bytes are not, which is what tb_uuid_to_string
 * has to account for. */
typedef struct tb_uuid {
  uint8_t bytes[16];
} tb_uuid;

/* Renders a uuid in the 8-4-4-4-12 form, matching .NET's Guid.ToString("D").
 * `out` must have room for 37 characters, the terminator included. */
void tb_uuid_to_string(const tb_uuid* value, char out[37]);

/* A block of memory a table owns.
 *
 * Blocks are never reallocated, so every pointer handed out of an arena stays
 * valid until tb_arena_free. That is the property the generated records depend
 * on: they hold interior pointers and nothing fixes them up. */
typedef struct tb_block {
  struct tb_block* next;
  size_t used;
  size_t capacity;
  unsigned char* bytes;
} tb_block;

typedef struct tb_arena {
  tb_block* head;
} tb_arena;

/* Hands back zeroed, aligned storage, or NULL when the allocator refuses. */
void* tb_arena_alloc(tb_arena* arena, size_t size);

/* Releases every block. Safe on a zeroed arena, and safe to call twice. */
void tb_arena_free(tb_arena* arena);

/* Sequential reader over a table file's bytes.
 *
 * Non-owning: the buffer has to outlive the reader. Strings are copied into the
 * arena, so the buffer may be released once a table is loaded. */
typedef struct tb_reader {
  const uint8_t* data;
  int32_t length;
  int32_t position;
  bool failed;
  char error[TABBIT_ERROR_MAX];

  /* Where strings and arrays are copied to. May be NULL for a reader that
   * only reads scalars. */
  tb_arena* arena;
} tb_reader;

void tb_reader_init(tb_reader* reader, const uint8_t* data, int32_t length, tb_arena* arena);

/* True once any read has run out of data or found the file malformed. */
bool tb_failed(const tb_reader* reader);

/* Why the first failure happened, or "" while nothing has gone wrong. */
const char* tb_error(const tb_reader* reader);

bool tb_read_bool(tb_reader* reader, bool* out);
bool tb_read_int32(tb_reader* reader, int32_t* out);
bool tb_read_uint32(tb_reader* reader, uint32_t* out);
bool tb_read_int64(tb_reader* reader, int64_t* out);
bool tb_read_float(tb_reader* reader, float* out);
bool tb_read_double(tb_reader* reader, double* out);
bool tb_read_uuid(tb_reader* reader, tb_uuid* out);

/* Ticks, for both. Not a time type: a sheet reaches 0001-01-01 and TimeSpan's
 * full range, and C has nothing that holds either without loss. */
bool tb_read_datetime(tb_reader* reader, int64_t* out_ticks);
bool tb_read_timespan(tb_reader* reader, int64_t* out_ticks);

/* Copies UTF-8 bytes into the arena and hands back a terminated pointer.
 *
 * A value holding an embedded NUL is refused rather than truncated. C has no
 * way to carry one in a `const char*`, and half a string returned as the whole
 * of it is the kind of failure this format's readers exist to avoid. */
bool tb_read_string(tb_reader* reader, const char** out);

/* The zig-zag encoded count in front of a variable length array. */
bool tb_read_counter32(tb_reader* reader, int32_t* out);

/* An int64 written in as few bytes as its magnitude needed, either sign.
 *
 * The base of a bit-packed block, which is a value of the column's own element type -
 * an i64 column's base does not fit in thirty-two bits. One byte when it is zero. */
bool tb_read_counter64(tb_reader* reader, int64_t* out);

/* A stream of bytes under one of the integer encodings, which is what a packed block
 * and a presence bitmap both end in.
 *
 * One reader for both, so a bitmap and a packed value block cannot disagree about the
 * same bits. The count is known before the call in both cases, so nothing here reads a
 * length. The bytes are allocated in the reader's arena. */
bool tb_read_byte_stream(tb_reader* reader, uint8_t encoding, int32_t count,
       const char* field_name, const uint8_t** out_bytes);

/* An enum, which travels as its underlying zig-zag encoded int32. */
bool tb_read_enum(tb_reader* reader, int32_t* out);

/* Reads and checks the file header, handing back the row count that follows.
 *
 * The flags byte says what the body needed before a reader could reach it. Encryption is
 * bit 0 and tb_open has dealt with it by this point; any other bit means the file needs
 * handling this build does not have. */
/* Reads and checks a table file's header. The descriptors are allocated from the
 * reader's arena; *out_columns is left NULL when the table has none. */
bool tb_read_table_header(tb_reader* reader, int32_t* out_row_count,
             tb_column** out_columns, int32_t* out_column_count);

/* Advances past bytes without interpreting them: an unknown column's whole block.
 * The column-oriented layout is what makes this one call the entirety of skipping. */
bool tb_skip(tb_reader* reader, int32_t byte_count);

/* Promotions: a member reading a file element narrower than itself. Only the
 * mathematically lossless directions exist; tb_check_column already refused the rest. */
bool tb_read_i32_as(tb_reader* reader, uint8_t element, int32_t* out);
bool tb_read_i64_as(tb_reader* reader, uint8_t element, int64_t* out);
bool tb_read_f64_as(tb_reader* reader, uint8_t element, double* out);

/* That a column is what the generated member expects, or a lossless promotion of it.
 * Refusal is by name and both types, never by reading anyway. `accepted` is the set the
 * member can read, built out of TB_ELEMENT_MASK. */
bool tb_check_column(tb_reader* reader, const tb_column* column, const char* field_name,
          uint8_t kind, int32_t count, bool nullable, unsigned accepted);

/* The same, for a member whose array elements may be absent. */
bool tb_check_column_elements(tb_reader* reader, const tb_column* column,
          const char* field_name, uint8_t kind, int32_t count, bool nullable,
          unsigned accepted, bool element_nullable);

/* A nullable column's presence bitmap, read into the caller's buffer.
 *
 * Called by the generated code before the row loop: the bitmap sits at the front of the
 * block and the values follow it. One bit per row, low bit first, padded to a byte. The
 * buffer comes from the table's arena, so it dies with the load. */
bool tb_read_presence(tb_reader* reader, const tb_column* column, int32_t row_count,
          const uint8_t** out_presence);

/* A column's element bitmap, behind the row bitmap and in front of the values.
 *
 * NULL for a column that does not carry one. Its length is written ahead of it as a
 * counter32, because a variable-length column's total is the sum of its row lengths and
 * those live inside the value block - a reader meeting the bitmap first would have nothing
 * to size it by. spec/nullable-array-elements.md. */
bool tb_read_element_presence(tb_reader* reader, const tb_column* column,
          const uint8_t** out_presence);

/* Whether a row has a value, for a column that says which do.
 *
 * A NULL bitmap means the column is not optional and every row has one, so the generated
 * code can call this unconditionally. */
bool tb_is_present(const uint8_t* presence, int32_t row);

/* That a block was consumed exactly: a mismatch is a format disagreement, and stopping
 * here names the column instead of corrupting the next. */
bool tb_check_block_end(tb_reader* reader, const tb_column* column, int32_t expected_end);

/* Reads one scalar column's values in row order, whatever the block's encoding.
 *
 * The generated row loop stays a row loop; this is the one place that knows how a
 * delta accumulates, how long a run has left, or that a dictionary index is a
 * reference into strings decoded once. That last one matters beyond file size: a
 * hundred-thousand-row column with three distinct strings copies three strings into
 * the arena, not a hundred thousand.
 *
 * tb_check_column has already refused any (element, encoding) pair the spec does not
 * define, so the functions here do not re-litigate that. Sticky like every read: once
 * the reader has failed, every next does nothing and returns false. */
typedef struct tb_cursor {
  tb_reader* reader;
  const char* field_name;
  uint8_t element;
  uint8_t encoding;

  /* The block's dictionary, decoded into the arena once and handed out per row.
   *
   * Which of the two the block filled is decided by its element: a string is
   * decoded to one copy in the arena that every row holding it points at - and a
   * front coded dictionary is decoded here too, because the folding was only ever
   * about the bytes on disk. */
  const char** dictionary;
  int32_t dictionary_count;

  /* A fixed-width element keeps its entries as the raw bytes they were written
   * as, and a row turns one into a value only when it asks for it - so the value
   * is reconstructed exactly as the raw layout would have read it.
   *
   * `value_width` is non-zero for exactly the blocks that have one, which is what
   * the next functions test: a dictionary of no entries is still a dictionary. */
  const uint8_t* value_dictionary;
  int32_t value_width;
  int32_t value_count;

  /* A run-length family's current run: what remains of it, and its value - which
   * is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE. */
  int32_t run_remaining;
  int32_t run_value;

  /* The delta family's accumulator, once started. */
  int32_t previous;
  bool started;

  /* Rows not yet handed out. A run that claims more than this is corrupt, and
   * catching it here names the field instead of leaving it to the block-end check.
   * For an array column this counts elements, not rows. */
  int32_t rows_remaining;

  /* How many elements each row holds, decoded up front for an encoded array column.
   *
   * Up front because the element stream follows the length stream in the block, so every
   * length has been read by the time the first element is. NULL for a raw array, whose
   * lengths are interleaved with its elements and read as they are reached. */
  const int32_t* lengths;
  int32_t length_count;
  int32_t length_at;

  /* Whether a float column's values are travelling as integers. */
  bool whole_numbers;

  /* A bit-packed column's bytes, decoded up front, and where in them the next value is.
   *
   * Up front because the bytes are themselves under an encoding and a value can cross a
   * byte boundary, so handing values out one at a time would mean carrying a decoder and
   * a bit offset that disagree about where they are. */
  const uint8_t* packed;
  int32_t packed_width;
  int64_t packed_base;
  int64_t packed_bit;
} tb_cursor;

/* Opens a cursor over one column's block, right after tb_check_column. A DICT family
 * block decodes its dictionary here, once. `field_name` is kept for error messages
 * and has to outlive the cursor - generated code passes a literal. */
bool tb_cursor_init(tb_cursor* cursor, tb_reader* reader, const tb_column* column,
          int32_t row_count, const char* field_name);

/* How many elements the next row of an array column holds.
 *
 * One call whichever way the block is laid out. An encoded array decoded every length
 * before the first element was read, so this hands out what it already has; a raw one
 * states each row's length in front of that row's elements, so this reads it where it
 * stands. */
bool tb_cursor_next_length(tb_cursor* cursor, int32_t* out);

/* The next int32 - which also serves enums, and reference indexes. */
bool tb_cursor_next_i32(tb_cursor* cursor, int32_t* out);

/* An int64 member: an i64 column raw or through its dictionary, and anything
 * narrower by decoding an int32 and widening it. Ticks read through this one, so a
 * datetime or a timespan column meets the i64 dictionary like any other. */
bool tb_cursor_next_i64(tb_cursor* cursor, int64_t* out);

/* A float member: raw, or the dictionary entry's exact bit pattern. */
bool tb_cursor_next_f32(tb_cursor* cursor, float* out);

/* A double member: from f64 or f32 - either of them raw or dictionary-encoded -
 * and from an i32 column by decoding and widening. */
bool tb_cursor_next_f64(tb_cursor* cursor, double* out);

/* A bool member: one byte raw, or a run of them. */
bool tb_cursor_next_bool(tb_cursor* cursor, bool* out);

/* The next string - the dictionary's copy where the block has one, so rows that
 * repeat a value share one pointer into the arena. */
bool tb_cursor_next_string(tb_cursor* cursor, const char** out);

/* Up to `limit` rows that all hold the next value. `*out_count` is how many, always
 * at least 1, and `*out` is the value.
 *
 * This is what makes a run cost one call instead of one per row: the generated loop
 * asks once, then assigns the value that many times. An encoding that cannot promise
 * sameness cheaply answers 1, so the caller's loop is correct over every encoding and
 * only faster over runs. */
bool tb_cursor_next_same_i32(tb_cursor* cursor, int32_t limit, int32_t* out_count,
           int32_t* out);

/* The string counterpart of tb_cursor_next_same_i32. */
bool tb_cursor_next_same_string(tb_cursor* cursor, int32_t limit, int32_t* out_count,
              const char** out);

/* Reads a whole file. The caller frees the buffer with tb_free_bytes. */
bool tb_read_all_bytes(const char* filename, uint8_t** out_data, int32_t* out_length);

void tb_free_bytes(uint8_t* data);

/* A file's plaintext bytes, checked against its MAC on the way.
 *
 * Call this on the bytes before handing them to a reader. A file that is neither encrypted
 * nor authenticated comes back untouched, so the call belongs in the load path whether or
 * not the project uses either.
 *
 * The order is verify, then decrypt. The tag covers the file as it is stored, so an altered
 * file is refused before the key is used on it, and the header - the flags, the cipher byte,
 * the nonce - is covered along with the body.
 *
 * Decryption happens in place, and what comes back is a window onto the same buffer rather
 * than a copy of it. The fields it consumes are returned to what a plain file has in them,
 * so calling it twice on the same buffer is the same as calling it once. `data` still owns
 * the memory, so it is what tb_free_bytes is given.
 *
 * `key` is 32 bytes, and may be NULL for a project that writes no encrypted files. Why it
 * fails goes into `error` the way a load's does, so a caller that does not want the detail
 * passes NULL.
 *
 * `mac_key` is 32 bytes, and NULL for a project that does not sign its files. A reader that
 * has one refuses a file that carries no MAC: the field being zero is how a file says it is
 * unauthenticated, so accepting that from a project that signs its files would put the check
 * sixteen zero bytes away from being removed.
 *
 * `verify_mac` false skips the check. For tools and for measuring load time - and no weaker
 * than it looks, because anyone who can flip this flag in a shipped binary can read the key
 * out of the same binary.
 *
 * What the two layers are and are not for: both keys ship inside the client that reads the
 * file. Encryption stops a data file being read in an editor; the MAC stops an edited one
 * loading. Neither stops anyone who can take the keys out of the client, and no format
 * does. */
bool tb_open(uint8_t* data, int32_t length, const uint8_t* key, int32_t key_length,
       const uint8_t* mac_key, int32_t mac_key_length, bool verify_mac,
       const uint8_t** out_data, int32_t* out_length, char* error, size_t error_size);

/* How much to reserve up front for a count that came off the wire.
 *
 * A corrupt count of two billion would otherwise be an immediate allocation of
 * that many elements, which fails long before the reader notices the file is
 * short. */
int32_t tb_reserve_bound(int32_t count);

/* Whether a row count can possibly be honest.
 *
 * `min_row_bytes` is what one row costs at its very smallest, which the
 * generator knows because every field encodes to at least one byte. A count
 * larger than the bytes left could not have been written by the exporter, and
 * believing it means allocating for rows that are not there.
 *
 * Checked before the allocation rather than discovered during it. */
bool tb_row_count_is_plausible(const tb_reader* reader, int32_t row_count, int32_t min_row_bytes);

/* One row's key, and where the row is.
 *
 * C has no map, so a table keeps these sorted and looks a key up by bisection.
 * A linear scan would be simpler and would turn every lookup into a walk of the
 * whole table.
 *
 * Four families rather than one, because a table indexes whatever column the
 * sheet marked and those columns are not all int32_t. Which one a generated
 * table uses is decided at generation time, from the field's own type:
 *
 *   tb_index_entry         int, enum, bool
 *   tb_index64_entry       bigint, datetime, timespan
 *   tb_string_index_entry  string
 *   tb_uuid_index_entry    uuid
 *
 * They are four copies of the same twenty lines. A macro would fold them into
 * one, at the price of a generated table declaring its index through an
 * expansion nobody can step into - and these are the four the format can
 * produce, not an open set. */
typedef struct tb_index_entry {
  int32_t key;
  int32_t position;
} tb_index_entry;

void tb_index_sort(tb_index_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t tb_index_find(const tb_index_entry* entries, int32_t count, int32_t key);

typedef struct tb_index64_entry {
  int64_t key;
  int32_t position;
} tb_index64_entry;

void tb_index64_sort(tb_index64_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t tb_index64_find(const tb_index64_entry* entries, int32_t count, int64_t key);

/* The key points into the table's arena, which owns it for as long as the table
 * does - so the entry borrows rather than copies. */
typedef struct tb_string_index_entry {
  const char* key;
  int32_t position;
} tb_string_index_entry;

void tb_string_index_sort(tb_string_index_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t tb_string_index_find(const tb_string_index_entry* entries, int32_t count,
                             const char* key);

typedef struct tb_uuid_index_entry {
  tb_uuid key;
  int32_t position;
} tb_uuid_index_entry;

void tb_uuid_index_sort(tb_uuid_index_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t tb_uuid_index_find(const tb_uuid_index_entry* entries, int32_t count,
                           tb_uuid key);

/* Marks the reader failed with a message of the caller's own.
 *
 * For the generated code, which allocates and so can fail where the reader by
 * itself cannot. Always returns false, so it reads as `return tb_fail_with(...)`.
 * Sticky like every other failure: an earlier reason is kept. */
bool tb_fail_with(tb_reader* reader, const char* message);

/* Writes "context: message" into a caller's buffer, truncating rather than
 * allocating. Does nothing when there is no buffer, so a caller that does not
 * want the detail passes NULL. */
void tb_copy_error(char* error, size_t error_size, const char* context, const char* message);

#ifdef __cplusplus
}
#endif

/* ------------------------------------------------------------ implementation */

#ifdef TABBIT_TCB_IMPLEMENTATION

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef __cplusplus
extern "C" {
#endif

#define TABBIT_ARENA_MIN_BLOCK 4096

void tb_uuid_to_string(const tb_uuid* value, char out[37]) {
  static const char hex[] = "0123456789abcdef";

  /* Component order matching .NET's Guid.ToString("D"): the first three
   * groups are little endian, the last eight bytes are in order. */
  static const int order[16] = { 3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15 };

  int at = 0;
  int i;

  for (i = 0; i < 16; ++i) {
    uint8_t b;

    if (i == 4 || i == 6 || i == 8 || i == 10)
      out[at++] = '-';

    b = value->bytes[order[i]];
    out[at++] = hex[b >> 4];
    out[at++] = hex[b & 0x0F];
  }

  out[at] = '\0';
}

void* tb_arena_alloc(tb_arena* arena, size_t size) {
  tb_block* block;
  size_t capacity;
  void* result;

  /* Everything the generated code stores is at most eight bytes wide, so
   * rounding to eight satisfies all of it without needing max_align_t. */
  size = (size + 7u) & ~(size_t)7u;

  if (size == 0)
    size = 8;

  block = arena->head;

  if (block == NULL || block->capacity - block->used < size) {
    capacity = size > TABBIT_ARENA_MIN_BLOCK ? size : TABBIT_ARENA_MIN_BLOCK;

    block = (tb_block*)malloc(sizeof(tb_block));
    if (block == NULL)
      return NULL;

    block->bytes = (unsigned char*)calloc(1, capacity);
    if (block->bytes == NULL) {
      free(block);
      return NULL;
    }

    block->next = arena->head;
    block->used = 0;
    block->capacity = capacity;

    arena->head = block;
  }

  result = block->bytes + block->used;
  block->used += size;

  return result;
}

void tb_arena_free(tb_arena* arena) {
  tb_block* block = arena->head;

  while (block != NULL) {
    tb_block* next = block->next;

    free(block->bytes);
    free(block);

    block = next;
  }

  arena->head = NULL;
}

void tb_reader_init(tb_reader* reader, const uint8_t* data, int32_t length, tb_arena* arena) {
  reader->data = data;
  reader->length = length;
  reader->position = 0;
  reader->failed = false;
  reader->error[0] = '\0';
  reader->arena = arena;
}

bool tb_failed(const tb_reader* reader) { return reader->failed; }

const char* tb_error(const tb_reader* reader) { return reader->error; }

/* The first failure is the informative one; everything after it is a
 * consequence of reading past the end. */
static bool tb_fail(tb_reader* reader, const char* format, ...) {
  if (!reader->failed) {
    va_list args;

    reader->failed = true;

    va_start(args, format);
    vsnprintf(reader->error, sizeof reader->error, format, args);
    va_end(args);
  }

  return false;
}

static bool tb_require(tb_reader* reader, int32_t count) {
  if (reader->failed)
    return false;

  if (count < 0 || reader->length - reader->position < count) {
    return tb_fail(reader,
           "table data ended after %d of %d bytes while %d more were expected",
           reader->position, reader->length, count);
  }

  return true;
}

static bool tb_read_fixed8(tb_reader* reader, uint8_t* out) {
  if (!tb_require(reader, 1))
    return false;

  *out = reader->data[reader->position++];
  return true;
}

/* The two fixed widths, assembled from bytes rather than copied over the host's
 * own layout: the file is little endian wherever it is read. Split from the reads
 * below because a dictionary entry is those same bytes sitting in the arena. */
static uint32_t tb_load_fixed32(const uint8_t* at) {
  return (uint32_t)at[0]
    | (uint32_t)at[1] << 8
    | (uint32_t)at[2] << 16
    | (uint32_t)at[3] << 24;
}

static uint64_t tb_load_fixed64(const uint8_t* at) {
  uint64_t value = 0;
  int i;

  for (i = 0; i < 8; ++i)
    value |= (uint64_t)at[i] << (8 * i);

  return value;
}

static bool tb_read_fixed32(tb_reader* reader, uint32_t* out) {
  if (!tb_require(reader, 4))
    return false;

  *out = tb_load_fixed32(reader->data + reader->position);

  reader->position += 4;
  return true;
}

static bool tb_read_fixed64(tb_reader* reader, uint64_t* out) {
  if (!tb_require(reader, 8))
    return false;

  *out = tb_load_fixed64(reader->data + reader->position);

  reader->position += 8;
  return true;
}

static bool tb_read_varint32(tb_reader* reader, uint32_t* out) {
  uint32_t value = 0;
  int shift;

  for (shift = 0; shift < 35; shift += 7) {
    uint8_t byte;

    if (!tb_read_fixed8(reader, &byte))
      return false;

    value |= (uint32_t)(byte & 0x7F) << shift;

    if ((byte & 0x80) == 0) {
      *out = value;
      return true;
    }
  }

  return tb_fail(reader, "varint32 is longer than five bytes");
}

bool tb_read_counter64(tb_reader* reader, int64_t* out) {
  uint64_t encoded = 0;
  int shift = 0;

  *out = 0;

  for (;;) {
    uint8_t piece = 0;

    if (!tb_read_fixed8(reader, &piece))
      return false;

    encoded |= (uint64_t)(piece & 0x7Fu) << shift;

    if ((piece & 0x80u) == 0)
      break;

    shift += 7;

    if (shift > 63)
      return tb_fail(reader, "a 64-bit variable length integer runs past ten bytes");
  }

  *out = (int64_t)(encoded >> 1) ^ -(int64_t)(encoded & 1u);
  return true;
}

static bool tb_as_byte(tb_reader* reader, int32_t value, const char* field_name,
           int32_t* out) {
  if (value < 0 || value > 255)
    return tb_fail(reader, "%s: %d is not a byte", field_name, (int)value);

  *out = value;
  return true;
}

bool tb_read_byte_stream(tb_reader* reader, uint8_t encoding, int32_t count,
       const char* field_name, const uint8_t** out_bytes) {
  uint8_t* out;
  int32_t filled = 0;
  int32_t previous = 0;
  bool walking;

  *out_bytes = NULL;

  if (tb_failed(reader))
    return false;

  out = (uint8_t*)tb_arena_alloc(reader->arena, (size_t)(count > 0 ? count : 1));

  if (out == NULL)
    return tb_fail(reader, "out of memory reading a packed byte stream");

  if (encoding == TB_ENCODING_RAW) {
    for (filled = 0; filled < count; ++filled) {
      if (!tb_read_fixed8(reader, &out[filled]))
        return false;
    }

    *out_bytes = out;
    return true;
  }

  if (encoding > TB_ENCODING_DELTA_RLE) {
    return tb_fail(reader, "%s: encoding %d cannot carry a packed byte stream",
           field_name, (int)encoding);
  }

  walking = encoding == TB_ENCODING_DELTA || encoding == TB_ENCODING_DELTA_RLE;

  /* The first value of a delta stream is written outright; the rest are steps from it.
   * A run in a delta stream repeats the step, not the value, so it walks. */
  if (count > 0 && walking) {
    int32_t first = 0;

    if (!tb_read_counter32(reader, &first))
      return false;

    if (!tb_as_byte(reader, first, field_name, &previous))
      return false;

    out[filled++] = (uint8_t)previous;
  }

  while (filled < count) {
    int32_t run = 1;
    int32_t step = 0;
    int32_t value = 0;
    int32_t at;

    if (encoding == TB_ENCODING_VARINT) {
      int32_t read = 0;

      if (!tb_read_counter32(reader, &read)
          || !tb_as_byte(reader, read, field_name, &value)) {
        return false;
      }
    } else if (encoding == TB_ENCODING_DELTA) {
      if (!tb_read_counter32(reader, &step))
        return false;
    } else if (encoding == TB_ENCODING_RLE) {
      int32_t read = 0;

      if (!tb_read_counter32(reader, &run)
          || !tb_read_counter32(reader, &read)
          || !tb_as_byte(reader, read, field_name, &value)) {
        return false;
      }
    } else { /* TB_ENCODING_DELTA_RLE */
      if (!tb_read_counter32(reader, &run) || !tb_read_counter32(reader, &step))
        return false;
    }

    if (run < 1 || run > count - filled) {
      return tb_fail(reader, "%s: a run of %d cannot cover the %d bytes left",
             field_name, (int)run, (int)(count - filled));
    }

    for (at = 0; at < run; ++at) {
      if (walking) {
        int32_t stepped = (int32_t)((uint32_t)previous + (uint32_t)step);

        if (!tb_as_byte(reader, stepped, field_name, &previous))
          return false;

        out[filled++] = (uint8_t)previous;
      } else {
        out[filled++] = (uint8_t)value;
      }
    }
  }

  *out_bytes = out;
  return true;
}

bool tb_read_counter32(tb_reader* reader, int32_t* out) {
  uint32_t encoded;

  if (!tb_read_varint32(reader, &encoded))
    return false;

  /* Zig-zag: the sign lives in the low bit, so small negatives cost as little
   * as small positives. The cast through uint32_t keeps the negation defined. */
  *out = (int32_t)(encoded >> 1) ^ -(int32_t)(encoded & 1u);
  return true;
}

bool tb_read_enum(tb_reader* reader, int32_t* out) { return tb_read_counter32(reader, out); }

bool tb_read_bool(tb_reader* reader, bool* out) {
  uint8_t byte;

  if (!tb_read_fixed8(reader, &byte))
    return false;

  *out = byte != 0;
  return true;
}

bool tb_read_int32(tb_reader* reader, int32_t* out) {
  uint32_t bits;

  if (!tb_read_fixed32(reader, &bits))
    return false;

  *out = (int32_t)bits;
  return true;
}

bool tb_read_uint32(tb_reader* reader, uint32_t* out) { return tb_read_fixed32(reader, out); }

bool tb_read_int64(tb_reader* reader, int64_t* out) {
  uint64_t bits;

  if (!tb_read_fixed64(reader, &bits))
    return false;

  *out = (int64_t)bits;
  return true;
}

bool tb_read_float(tb_reader* reader, float* out) {
  uint32_t bits;

  if (!tb_read_fixed32(reader, &bits))
    return false;

  memcpy(out, &bits, sizeof *out);
  return true;
}

bool tb_read_double(tb_reader* reader, double* out) {
  uint64_t bits;

  if (!tb_read_fixed64(reader, &bits))
    return false;

  memcpy(out, &bits, sizeof *out);
  return true;
}

bool tb_read_datetime(tb_reader* reader, int64_t* out_ticks) {
  return tb_read_int64(reader, out_ticks);
}

bool tb_read_timespan(tb_reader* reader, int64_t* out_ticks) {
  return tb_read_int64(reader, out_ticks);
}

bool tb_read_uuid(tb_reader* reader, tb_uuid* out) {
  if (!tb_require(reader, 16))
    return false;

  memcpy(out->bytes, reader->data + reader->position, 16);
  reader->position += 16;
  return true;
}

bool tb_read_string(tb_reader* reader, const char** out) {
  int32_t length;
  char* copy;

  if (!tb_read_counter32(reader, &length))
    return false;

  if (length < 0)
    return tb_fail(reader, "string length %d is negative", length);

  if (!tb_require(reader, length))
    return false;

  if (memchr(reader->data + reader->position, 0, (size_t)length) != NULL) {
    return tb_fail(reader,
           "a string holds a NUL byte, which cannot be carried in a C string");
  }

  if (reader->arena == NULL)
    return tb_fail(reader, "a string was read through a reader with no arena");

  copy = (char*)tb_arena_alloc(reader->arena, (size_t)length + 1);
  if (copy == NULL)
    return tb_fail(reader, "out of memory reading a string of %d bytes", length);

  memcpy(copy, reader->data + reader->position, (size_t)length);
  copy[length] = '\0';

  reader->position += length;
  *out = copy;
  return true;
}

bool tb_read_table_header(tb_reader* reader, int32_t* out_row_count,
             tb_column** out_columns, int32_t* out_column_count) {
  uint32_t signature;
  uint32_t version;
  uint8_t flags;
  int32_t column_count;
  int32_t at;
  tb_column* columns;

  *out_row_count = 0;
  *out_columns = NULL;
  *out_column_count = 0;

  /* Checked again here rather than only in tb_open, because a reader can be handed bytes
   * that never went through it. */
  if (!tb_read_fixed32(reader, &signature))
    return false;

  if (signature != TB_MAGIC)
    return tb_fail(reader, "the file does not begin with the table file signature");

  if (!tb_read_fixed32(reader, &version))
    return false;

  if (version != TABBIT_BINARY_FILE_FORMAT_VERSION) {
    return tb_fail(reader, "table format version %u is not supported (expected %u)",
           (unsigned)version, (unsigned)TABBIT_BINARY_FILE_FORMAT_VERSION);
  }

  if (!tb_read_fixed8(reader, &flags))
    return false;

  /* Bit 0 too, not only the bits above it. tb_open clears it on a file it has decrypted,
   * so meeting it set here means the bytes reached the reader without the key ever being
   * applied - and naming that beats letting the block lengths make what they can of
   * ciphertext. */
  if ((flags & (unsigned)TB_FLAG_ENCRYPTED) != 0) {
    return tb_fail(reader,
           "the table is encrypted and was not decrypted - pass the key through tb_open first");
  }

  if (flags != 0)
    return tb_fail(reader, "table declares unsupported features");

  /* The cipher byte, the nonce, the MAC and the key check. tb_open has dealt with all four
   * by now; what is left is to be standing at the body. */
  if (!tb_skip(reader, TB_HEADER_SIZE - TB_CIPHER_OFFSET))
    return false;

  if (!tb_read_counter32(reader, out_row_count))
    return false;

  if (*out_row_count < 0) {
    int32_t bad = *out_row_count;

    *out_row_count = 0;
    return tb_fail(reader, "table row count %d is negative", bad);
  }

  if (!tb_read_counter32(reader, &column_count))
    return false;

  if (column_count < 0)
    return tb_fail(reader, "table column count %d is negative", column_count);

  if (column_count == 0)
    return true;

  columns = (tb_column*)tb_arena_alloc(reader->arena, (size_t)column_count * sizeof *columns);
  if (columns == NULL)
    return tb_fail(reader, "out of memory allocating the column descriptors");

  for (at = 0; at < column_count; ++at) {
    uint8_t wire = 0;
    uint8_t encoding = 0;
    uint32_t byte_length = 0;

    (void)tb_read_counter32(reader, &columns[at].tag);
    (void)tb_read_fixed8(reader, &wire);
    (void)tb_read_fixed8(reader, &encoding);
    (void)tb_read_counter32(reader, &columns[at].count);
    (void)tb_read_fixed32(reader, &byte_length);

    columns[at].element = (uint8_t)(wire & 0x0f);
    columns[at].kind = (uint8_t)((wire >> 4) & 0x03);
    columns[at].nullable = (wire & 0x40) != 0;
    columns[at].element_nullable = (wire & 0x80) != 0;
    columns[at].encoding = encoding;
    columns[at].byte_length = (int32_t)byte_length;
  }

  if (tb_failed(reader))
    return false;

  /* What the descriptors themselves say about the file, checked before the generated
   * code allocates for the row count. The blocks are all that follows the header, so
   * their declared lengths have to add up to the bytes left. A raw block also costs
   * at least one byte per row - a varint's shortest form, an empty string's length
   * prefix, a variable array's counter - so a larger row count is one the exporter
   * could not have written. An encoded block has no such floor; its decode checks
   * run sums and dictionary bounds instead. */
  {
    int32_t remaining = reader->length - reader->position;
    int32_t declared = 0;

    for (at = 0; at < column_count; ++at) {
      if (columns[at].byte_length < 0 || columns[at].byte_length > remaining - declared) {
        return tb_fail(reader,
               "column tag %d declares %d bytes, which the file cannot hold",
               columns[at].tag, columns[at].byte_length);
      }

      declared += columns[at].byte_length;

      if (columns[at].encoding == TB_ENCODING_RAW
          && *out_row_count > columns[at].byte_length) {
        int32_t bad = *out_row_count;

        *out_row_count = 0;
        return tb_fail(reader,
               "the row count %d is larger than column tag %d can hold in "
               "its %d bytes", bad, columns[at].tag, columns[at].byte_length);
      }
    }

    if (declared != remaining) {
      return tb_fail(reader,
             "the columns declare %d bytes but %d follow the header",
             declared, remaining);
    }
  }

  *out_columns = columns;
  *out_column_count = column_count;

  return true;
}

bool tb_skip(tb_reader* reader, int32_t byte_count) {
  if (tb_failed(reader))
    return false;

  if (byte_count < 0 || byte_count > reader->length - reader->position)
    return tb_fail(reader, "cannot skip %d bytes with %d remaining",
           byte_count, reader->length - reader->position);

  reader->position += byte_count;

  return true;
}

bool tb_read_i32_as(tb_reader* reader, uint8_t element, int32_t* out) {
  if (element == TB_ELEMENT_I32)
    return tb_read_int32(reader, out);

  return tb_read_counter32(reader, out);
}

bool tb_read_i64_as(tb_reader* reader, uint8_t element, int64_t* out) {
  if (element == TB_ELEMENT_I64)
    return tb_read_int64(reader, out);

  {
    int32_t narrower = 0;
    bool ok = (element == TB_ELEMENT_I32)
      ? tb_read_int32(reader, &narrower)
      : tb_read_counter32(reader, &narrower);

    *out = narrower;
    return ok;
  }
}

bool tb_read_f64_as(tb_reader* reader, uint8_t element, double* out) {
  if (element == TB_ELEMENT_F64)
    return tb_read_double(reader, out);

  if (element == TB_ELEMENT_F32) {
    float single = 0.0f;
    bool ok = tb_read_float(reader, &single);

    *out = single;
    return ok;
  }

  {
    int32_t integer = 0;
    bool ok = tb_read_int32(reader, &integer);

    *out = integer;
    return ok;
  }
}

/* The element codes in a mask, as "2, 0", for a message that has to say what the
 * member would have taken. */
static void tb_describe_elements(unsigned accepted, char* out, size_t out_size) {
  size_t at = 0;
  int element;

  if (out_size == 0)
    return;

  for (element = 0; element < 16; ++element) {
    if ((accepted & TB_ELEMENT_MASK(element)) == 0)
      continue;

    if (at > 0 && at + 2 < out_size) {
      out[at++] = ',';
      out[at++] = ' ';
    }

    if (at + 1 < out_size)
      out[at++] = (char)('0' + element);
  }

  out[at] = '\0';
}

/* The (element, encoding) pairs the spec defines. Integers take the integer encodings,
 * strings the dictionary ones, and an array takes the composition that applies all of
 * those to its elements. */
static bool tb_encoding_supported(const tb_column* column) {
  if (column->encoding == TB_ENCODING_RAW)
    return true;

  /* An array's block says what its elements use, and the element encoding is checked as
   * it is read rather than here - the descriptor carries only the outer one, so this is
   * as far as the descriptor can be checked. */
  if (column->kind != TB_KIND_SCALAR)
    return column->encoding == TB_ENCODING_ARRAY;

  switch (column->element) {
  case TB_ELEMENT_BOOL:
  case TB_ELEMENT_VARINT:
    return column->encoding == TB_ENCODING_RLE
      || column->encoding == TB_ENCODING_BITPACK;

  case TB_ELEMENT_I32:
    return (column->encoding >= TB_ENCODING_VARINT
        && column->encoding <= TB_ENCODING_DELTA_RLE)
      || column->encoding == TB_ENCODING_BITPACK;

  /* The dictionary is parameterized by element, so these three reach it with
   * entries that are simply their own raw bytes. */
  case TB_ELEMENT_I64:
    return column->encoding == TB_ENCODING_DICT
      || column->encoding == TB_ENCODING_DICT_RLE
      || column->encoding == TB_ENCODING_BITPACK;

  /* A float column additionally reaches the integer encodings, through the block that
   * says its values are whole numbers. */
  case TB_ELEMENT_F32:
  case TB_ELEMENT_F64:
    return column->encoding == TB_ENCODING_DICT
      || column->encoding == TB_ENCODING_DICT_RLE
      || column->encoding == TB_ENCODING_WHOLE;

  /* And a string dictionary can be front coded or built from segments, both of which
   * are meaningless for a fixed-width element and refused for one. */
  case TB_ELEMENT_STRING:
    return (column->encoding >= TB_ENCODING_DICT
        && column->encoding <= TB_ENCODING_DICT_FRONT_RLE)
      || column->encoding == TB_ENCODING_DICT_SEGMENT
      || column->encoding == TB_ENCODING_DICT_SEGMENT_RLE;

  default:
    return false;
  }
}

bool tb_read_presence(tb_reader* reader, const tb_column* column, int32_t row_count,
          const uint8_t** out_presence) {
  int32_t bytes;
  uint8_t* bits = NULL;
  int32_t at = 0;
  uint8_t encoding = 0;

  *out_presence = NULL;

  if (!column->nullable || tb_failed(reader))
    return !tb_failed(reader);

  /* The bitmap is a bit-packed boolean column of width one, so it carries an encoding
   * byte and is laid out by the same choice a packed value block uses. Its width and base
   * are known in advance, which is why it does not carry them. */
  bytes = (row_count + 7) / 8;
  (void)at;
  (void)bits;

  if (!tb_read_fixed8(reader, &encoding))
    return false;

  return tb_read_byte_stream(reader, encoding, bytes, "a presence bitmap", out_presence);
}

bool tb_read_element_presence(tb_reader* reader, const tb_column* column,
          const uint8_t** out_presence) {
  int32_t elements = 0;
  uint8_t encoding = 0;

  *out_presence = NULL;

  if (!column->element_nullable || tb_failed(reader))
    return !tb_failed(reader);

  if (!tb_read_counter32(reader, &elements))
    return false;

  if (!tb_read_fixed8(reader, &encoding))
    return false;

  return tb_read_byte_stream(reader, encoding, (elements + 7) / 8,
            "an element presence bitmap", out_presence);
}

bool tb_is_present(const uint8_t* presence, int32_t row) {
  return presence == NULL || (presence[row >> 3] & (1u << (row & 7))) != 0;
}

bool tb_check_column(tb_reader* reader, const tb_column* column, const char* field_name,
          uint8_t kind, int32_t count, bool nullable, unsigned accepted) {
  return tb_check_column_elements(reader, column, field_name, kind, count, nullable, accepted,
            false);
}

bool tb_check_column_elements(tb_reader* reader, const tb_column* column,
          const char* field_name, uint8_t kind, int32_t count, bool nullable,
          unsigned accepted, bool element_nullable) {
  char elements[48];

  if (tb_failed(reader))
    return false;

  /* The same statement about the other bitmap: code not expecting one would read it as
   * values. spec/nullable-array-elements.md. */
  if (column->element_nullable != element_nullable) {
    return tb_fail(reader,
        "%s: the file and the generated member disagree about whether this column's elements"
        " are optional; the schema changed, regenerate the code or rebuild the data",
        field_name);
  }

  /* Nullability is part of the shape: a file that says optional puts a presence bitmap at
   * the front of the block, and code not expecting one would read the bitmap as values. */
  if (column->nullable != nullable) {
    return tb_fail(reader,
        "%s: the file and the generated member disagree about whether this column is optional"
        "; the schema changed, regenerate the code or rebuild the data",
        field_name);
  }

  if (column->kind != kind || (kind != TB_KIND_VAR_ARRAY && column->count != count)) {
    return tb_fail(reader,
           "%s: the file column (kind %d, count %d) does not match the generated "
           "member (kind %d, count %d). The schema changed shape; regenerate the "
           "code or rebuild the data.",
           field_name, (int)column->kind, column->count, (int)kind, count);
  }

  /* An encoding this build cannot decode - or one the spec does not define for this
   * element - is refused by name, exactly like an element it cannot read. An unknown
   * column's encoding never gets here - a skip is a skip whatever the block's
   * layout. */
  if (!tb_encoding_supported(column)) {
    return tb_fail(reader,
           "%s: the file's column uses encoding %d, which this reader cannot decode "
           "for its element type. Regenerate the code or rebuild the data.",
           field_name, (int)column->encoding);
  }

  if ((accepted & TB_ELEMENT_MASK(column->element)) != 0)
    return true;

  tb_describe_elements(accepted, elements, sizeof elements);

  return tb_fail(reader,
         "%s: the file carries element type %d, which this member cannot read "
         "(accepts %s). The column changed type incompatibly; regenerate the code "
         "or rebuild the data.",
         field_name, (int)column->element, elements);
}

bool tb_check_block_end(tb_reader* reader, const tb_column* column, int32_t expected_end) {
  if (tb_failed(reader))
    return false;

  if (reader->position != expected_end) {
    return tb_fail(reader,
           "column tag %d: its block declared %d bytes but the read ended %d "
           "bytes short of its boundary",
           column->tag, column->byte_length, expected_end - reader->position);
  }

  return true;
}

/* The array of pointers a string dictionary hands out of, allocated once. */
static bool tb_cursor_alloc_dictionary(tb_cursor* cursor, int32_t count) {
  tb_reader* reader = cursor->reader;

  if (reader->arena == NULL)
    return tb_fail(reader, "a string was read through a reader with no arena");

  cursor->dictionary = (const char**)tb_arena_alloc(
    reader->arena, (size_t)count * sizeof *cursor->dictionary);

  if (cursor->dictionary == NULL)
    return tb_fail(reader, "%s: out of memory allocating the dictionary", cursor->field_name);

  return true;
}

/* A plain string dictionary: each entry is the value in its raw form, a length
 * and then its bytes. */
static bool tb_cursor_read_string_dictionary(tb_cursor* cursor, int32_t count) {
  int32_t at;

  if (!tb_cursor_alloc_dictionary(cursor, count))
    return false;

  for (at = 0; at < count; ++at) {
    if (!tb_read_string(cursor->reader, &cursor->dictionary[at]))
      return false;
  }

  cursor->dictionary_count = count;
  return true;
}

/* A sorted dictionary whose entries state only what they do not share with the
 * entry before them.
 *
 * Decoded into whole strings here rather than kept folded, because a row wants a
 * string and the folding was only ever about the bytes on disk. Each entry is built
 * straight into the arena out of the one before it - which is already sitting there,
 * terminated - so there is no scratch buffer to grow and free. */
static bool tb_cursor_read_front_dictionary(tb_cursor* cursor, int32_t count) {
  tb_reader* reader = cursor->reader;
  int32_t previous_length = 0;
  int32_t at;

  if (!tb_cursor_alloc_dictionary(cursor, count))
    return false;

  for (at = 0; at < count; ++at) {
    int32_t shared = 0;
    int32_t rest = 0;
    int32_t length;
    char* entry;

    if (!tb_read_counter32(reader, &shared) || !tb_read_counter32(reader, &rest))
      return false;

    if (shared < 0 || rest < 0 || shared > previous_length) {
      return tb_fail(reader,
             "%s: dictionary entry %d shares %d bytes with an entry of %d",
             cursor->field_name, at, shared, previous_length);
    }

    /* Before the addition, not only for the copy: it bounds `rest` by the bytes
     * left, and an entry is never longer than the dictionary's own bytes plus
     * what is left of the file - so the sum cannot leave int32_t. */
    if (!tb_require(reader, rest))
      return false;

    if (memchr(reader->data + reader->position, 0, (size_t)rest) != NULL) {
      return tb_fail(reader,
             "a string holds a NUL byte, which cannot be carried in a C string");
    }

    length = shared + rest;

    entry = (char*)tb_arena_alloc(reader->arena, (size_t)length + 1);
    if (entry == NULL) {
      return tb_fail(reader, "%s: out of memory decoding a dictionary entry of %d bytes",
             cursor->field_name, length);
    }

    /* The shared bytes come from the entry before it, which is why a `shared`
     * larger than that entry is refused rather than clamped. */
    if (shared > 0)
      memcpy(entry, cursor->dictionary[at - 1], (size_t)shared);

    if (rest > 0)
      memcpy(entry + shared, reader->data + reader->position, (size_t)rest);

    entry[length] = '\0';
    reader->position += rest;

    cursor->dictionary[at] = entry;
    previous_length = length;
  }

  cursor->dictionary_count = count;
  return true;
}

/* A fixed-width element: the entries are the value's own bytes, so they are kept as
 * bytes and turned into values only when a row asks for one. */
static bool tb_cursor_read_value_dictionary(tb_cursor* cursor, int32_t count) {
  tb_reader* reader = cursor->reader;
  int32_t width = cursor->element == TB_ELEMENT_F32 ? 4 : 8;
  int32_t bytes = 0;
  uint8_t* copy;

  cursor->value_width = width;
  cursor->value_count = count;

  if (count == 0)
    return true;

  /* The division rather than a multiplication, which would overflow for exactly
   * the corrupt count this is here to catch. */
  if (count > (reader->length - reader->position) / width) {
    return tb_fail(reader,
           "%s: a dictionary of %d entries is larger than the file can hold",
           cursor->field_name, count);
  }

  bytes = count * width;

  if (reader->arena == NULL)
    return tb_fail(reader, "a dictionary was read through a reader with no arena");

  copy = (uint8_t*)tb_arena_alloc(reader->arena, (size_t)bytes);
  if (copy == NULL)
    return tb_fail(reader, "%s: out of memory allocating the dictionary", cursor->field_name);

  memcpy(copy, reader->data + reader->position, (size_t)bytes);
  reader->position += bytes;

  cursor->value_dictionary = copy;
  return true;
}

/* The lengths of an array column's rows, as their own encoded stream.
 *
 * A varint stream, so what may be chosen for it is what may be chosen for any varint
 * column - each length as a counter32, or runs of them. Most columns have rows that are
 * all the same length, which is one run. */
static bool tb_cursor_read_lengths(tb_cursor* cursor, uint8_t encoding, int32_t row_count) {
  tb_reader* reader = cursor->reader;
  int32_t* lengths;
  int32_t at;

  if (encoding != TB_ENCODING_RAW && encoding != TB_ENCODING_RLE) {
    return tb_fail(reader, "%s: encoding %d cannot carry an array column's row lengths",
           cursor->field_name, (int)encoding);
  }

  if (reader->arena == NULL)
    return tb_fail(reader, "an array column was read through a reader with no arena");

  lengths = (int32_t*)tb_arena_alloc(reader->arena, (size_t)row_count * sizeof *lengths);
  if (lengths == NULL)
    return tb_fail(reader, "%s: out of memory decoding the row lengths", cursor->field_name);

  cursor->lengths = lengths;
  cursor->length_count = row_count;

  if (encoding == TB_ENCODING_RAW) {
    for (at = 0; at < row_count; ++at) {
      if (!tb_read_counter32(reader, &lengths[at]))
        return false;

      if (lengths[at] < 0) {
        return tb_fail(reader, "%s: row %d declares %d elements",
               cursor->field_name, at, lengths[at]);
      }
    }

    return true;
  }

  {
    int32_t filled = 0;

    while (filled < row_count) {
      int32_t run = 0;
      int32_t value = 0;

      if (!tb_read_counter32(reader, &run) || !tb_read_counter32(reader, &value))
        return false;

      if (run < 1 || run > row_count - filled) {
        return tb_fail(reader,
               "%s: a run of %d lengths cannot cover the %d rows left in the column",
               cursor->field_name, run, row_count - filled);
      }

      if (value < 0)
        return tb_fail(reader, "%s: a row declares %d elements", cursor->field_name, value);

      for (at = 0; at < run; ++at)
        lengths[filled++] = value;
    }
  }

  return true;
}

/* One piece of the table a segment dictionary assembles its entries out of. */
typedef struct tb_segment {
  const uint8_t* bytes;
  int32_t length;
} tb_segment;

/* A dictionary whose entries are lists of references into a table of the pieces they are
 * built from.
 *
 * Two reads and a concatenation: the table, which is front coded because its own entries
 * share their fronts, and then each value as the pieces it is made of. The result is the
 * same array of terminated strings every other dictionary produces, so nothing
 * downstream of here knows which kind it came from.
 *
 * The pieces go into the arena beside the entries. They are of no use once the entries
 * are built, but they are smaller than the entries they built and this is the allocation
 * this file has - a scratch buffer would have to be released on each of the failure paths
 * below, which is the shape of leak the arena exists to rule out. */
static bool tb_cursor_read_segment_dictionary(tb_cursor* cursor) {
  tb_reader* reader = cursor->reader;
  tb_segment* segments;
  int32_t segment_count = 0;
  int32_t previous_length = 0;
  int32_t count = 0;
  int32_t at;

  if (!tb_read_counter32(reader, &segment_count))
    return false;

  if (segment_count < 0)
    return tb_fail(reader, "%s: the segment count is negative", cursor->field_name);

  /* Every segment costs at least its two counters on the wire, so a count the bytes left
   * cannot cover is one the exporter could not have written. Checked here because the
   * allocation comes before the reads that would catch it. */
  if (segment_count > reader->length - reader->position) {
    return tb_fail(reader,
           "%s: a segment table of %d entries is larger than the file can hold",
           cursor->field_name, segment_count);
  }

  if (reader->arena == NULL)
    return tb_fail(reader, "a dictionary was read through a reader with no arena");

  segments = (tb_segment*)tb_arena_alloc(
    reader->arena, (size_t)segment_count * sizeof *segments);

  if (segments == NULL)
    return tb_fail(reader, "%s: out of memory allocating the segment table", cursor->field_name);

  for (at = 0; at < segment_count; ++at) {
    int32_t shared = 0;
    int32_t rest = 0;
    uint8_t* bytes;

    if (!tb_read_counter32(reader, &shared) || !tb_read_counter32(reader, &rest))
      return false;

    if (shared < 0 || rest < 0 || shared > previous_length) {
      return tb_fail(reader, "%s: segment %d shares %d bytes with an entry of %d",
             cursor->field_name, at, shared, previous_length);
    }

    /* Before the addition, not only for the copy: it bounds `rest` by the bytes left, so
     * the sum cannot leave int32_t. */
    if (!tb_require(reader, rest))
      return false;

    /* The entries become C strings, so a NUL in any piece they are assembled from is
     * refused here rather than cutting a value short later. */
    if (memchr(reader->data + reader->position, 0, (size_t)rest) != NULL) {
      return tb_fail(reader,
             "a string holds a NUL byte, which cannot be carried in a C string");
    }

    bytes = (uint8_t*)tb_arena_alloc(reader->arena, (size_t)(shared + rest));
    if (bytes == NULL) {
      return tb_fail(reader, "%s: out of memory decoding a segment of %d bytes",
             cursor->field_name, shared + rest);
    }

    /* The shared bytes come from the segment before it, which is why a `shared` larger
     * than that segment is refused rather than clamped. */
    if (shared > 0)
      memcpy(bytes, segments[at - 1].bytes, (size_t)shared);

    if (rest > 0)
      memcpy(bytes + shared, reader->data + reader->position, (size_t)rest);

    reader->position += rest;

    segments[at].bytes = bytes;
    segments[at].length = shared + rest;
    previous_length = segments[at].length;
  }

  if (!tb_read_counter32(reader, &count))
    return false;

  if (count < 0)
    return tb_fail(reader, "%s: the dictionary entry count is negative", cursor->field_name);

  if (count > reader->length - reader->position) {
    return tb_fail(reader, "%s: a dictionary of %d entries is larger than the file can hold",
           cursor->field_name, count);
  }

  if (!tb_cursor_alloc_dictionary(cursor, count))
    return false;

  for (at = 0; at < count; ++at) {
    int32_t pieces = 0;
    int32_t list;
    int64_t length = 0;
    int32_t written = 0;
    int32_t piece;
    char* entry;

    if (!tb_read_counter32(reader, &pieces))
      return false;

    if (pieces < 0) {
      return tb_fail(reader, "%s: dictionary entry %d declares %d pieces",
             cursor->field_name, at, pieces);
    }

    /* The index list is walked twice off the same bytes. How long the entry is, is what
     * its pieces add up to, and that is known only once the list has been read - so the
     * alternative is a scratch buffer that grows to the longest entry and that every
     * failure path here would have to release. */
    list = reader->position;

    for (piece = 0; piece < pieces; ++piece) {
      int32_t index = 0;

      if (!tb_read_counter32(reader, &index))
        return false;

      if (index < 0 || index >= segment_count) {
        return tb_fail(reader,
               "%s: segment index %d is out of range - the table holds %d entries",
               cursor->field_name, index, segment_count);
      }

      length += segments[index].length;
    }

    if (length > INT32_MAX) {
      return tb_fail(reader, "%s: dictionary entry %d is longer than can be held",
             cursor->field_name, at);
    }

    entry = (char*)tb_arena_alloc(reader->arena, (size_t)length + 1);
    if (entry == NULL) {
      return tb_fail(reader, "%s: out of memory decoding a dictionary entry of %d bytes",
             cursor->field_name, (int)length);
    }

    reader->position = list;

    for (piece = 0; piece < pieces; ++piece) {
      int32_t index = 0;

      /* Neither read can fail and no index can be out of range: the pass above read the
       * same bytes and refused both. */
      (void)tb_read_counter32(reader, &index);

      memcpy(entry + written, segments[index].bytes, (size_t)segments[index].length);
      written += segments[index].length;
    }

    entry[written] = '\0';
    cursor->dictionary[at] = entry;
  }

  cursor->dictionary_count = count;
  return true;
}

bool tb_cursor_init(tb_cursor* cursor, tb_reader* reader, const tb_column* column,
          int32_t row_count, const char* field_name) {
  bool plain_dictionary;
  bool front_dictionary;

  cursor->reader = reader;
  cursor->field_name = field_name;
  cursor->element = column->element;
  cursor->encoding = column->encoding;
  cursor->dictionary = NULL;
  cursor->dictionary_count = 0;
  cursor->value_dictionary = NULL;
  cursor->value_width = 0;
  cursor->value_count = 0;
  cursor->run_remaining = 0;
  cursor->run_value = 0;
  cursor->previous = 0;
  cursor->started = false;
  cursor->rows_remaining = row_count;
  cursor->lengths = NULL;
  cursor->length_count = 0;
  cursor->length_at = 0;
  cursor->whole_numbers = false;
  cursor->packed = NULL;
  cursor->packed_width = 0;
  cursor->packed_base = 0;
  cursor->packed_bit = 0;

  if (tb_failed(reader))
    return false;

  /* An array column's block names an encoding for its elements and, where its rows differ
   * in length, one for the lengths. Both are encodings that already exist, so all this
   * does is read them and then go on being the element stream's cursor. */
  if (cursor->encoding == TB_ENCODING_ARRAY) {
    uint8_t element_encoding = 0;
    int64_t elements;

    if (!tb_read_fixed8(reader, &element_encoding))
      return false;

    cursor->encoding = element_encoding;

    if (column->kind == TB_KIND_VAR_ARRAY) {
      uint8_t length_encoding = 0;
      int32_t at;

      if (!tb_read_fixed8(reader, &length_encoding))
        return false;

      /* Every length first, which is also what puts the reader at the element stream:
       * the two are laid out in that order and the lengths are what say how long the
       * one after them is. */
      if (!tb_cursor_read_lengths(cursor, length_encoding, row_count))
        return false;

      elements = 0;

      for (at = 0; at < row_count; ++at)
        elements += cursor->lengths[at];
    } else {
      elements = (int64_t)row_count * column->count;
    }

    if (elements > INT32_MAX) {
      return tb_fail(reader, "%s: the column declares more elements than can be held",
             field_name);
    }

    cursor->rows_remaining = (int32_t)elements;
  }

  /* A bit-packed column states the width its range needs, the base subtracted from
   * every value, and which encoding carries the packed bytes. Decoded here so that
   * handing values out is a shift and an add. */
  if (cursor->encoding == TB_ENCODING_BITPACK) {
    uint8_t width = 0;
    uint8_t inner = 0;
    int64_t base = 0;
    int64_t bits;

    if (!tb_read_fixed8(reader, &width) || !tb_read_counter64(reader, &base)
        || !tb_read_fixed8(reader, &inner)) {
      return false;
    }

    if (width < 1 || width > 64) {
      return tb_fail(reader, "%s: a bit width of %d is not between 1 and 64",
             field_name, (int)width);
    }

    cursor->packed_width = (int32_t)width;
    cursor->packed_base = base;
    cursor->packed_bit = 0;

    bits = (int64_t)cursor->rows_remaining * (int64_t)width;

    return tb_read_byte_stream(reader, inner, (int32_t)((bits + 7) / 8), field_name,
             &cursor->packed);
  }

  /* A float column whose values are all whole numbers carries them as integers and says
   * which integer encoding they travel under. From here down it is that encoding's
   * cursor, and only the handing out converts back. */
  if (cursor->encoding == TB_ENCODING_WHOLE) {
    uint8_t inner = 0;

    if (!tb_read_fixed8(reader, &inner))
      return false;

    if (inner < TB_ENCODING_VARINT || inner > TB_ENCODING_DELTA_RLE) {
      return tb_fail(reader, "%s: encoding %d cannot carry a whole-number column's values",
             field_name, (int)inner);
    }

    cursor->encoding = inner;
    cursor->whole_numbers = true;
  }

  /* A segment dictionary is built once, here, and from then on the block is a dictionary
   * with an index stream like any other - so the row-by-row paths below need to know
   * nothing about it. */
  if (cursor->encoding == TB_ENCODING_DICT_SEGMENT
      || cursor->encoding == TB_ENCODING_DICT_SEGMENT_RLE) {
    bool runs = cursor->encoding == TB_ENCODING_DICT_SEGMENT_RLE;

    if (!tb_cursor_read_segment_dictionary(cursor))
      return false;

    cursor->encoding = runs ? TB_ENCODING_DICT_RLE : TB_ENCODING_DICT;
    return true;
  }

  plain_dictionary = cursor->encoding == TB_ENCODING_DICT
    || cursor->encoding == TB_ENCODING_DICT_RLE;

  front_dictionary = cursor->encoding == TB_ENCODING_DICT_FRONT
    || cursor->encoding == TB_ENCODING_DICT_FRONT_RLE;

  if (!plain_dictionary && !front_dictionary)
    return true;

  {
    int32_t count = 0;

    if (!tb_read_counter32(reader, &count))
      return false;

    if (count < 0)
      return tb_fail(reader, "%s: the dictionary entry count is negative", field_name);

    /* Every entry costs at least one byte on the wire - a string's length prefix,
     * a front coded entry's two counters, a fixed-width value's own bytes - so a
     * count the bytes left cannot cover is one the exporter could not have
     * written. Checked here because the allocation comes before the reads that
     * would catch it. */
    if (count > reader->length - reader->position) {
      return tb_fail(reader,
             "%s: a dictionary of %d entries is larger than the file can hold",
             field_name, count);
    }

    if (front_dictionary)
      return count == 0 ? true : tb_cursor_read_front_dictionary(cursor, count);

    /* A fixed-width element's dictionary is bytes, and it stays a dictionary at
     * no entries at all - which is why the width, not the pointer, is what says
     * the block has one. */
    if (cursor->element != TB_ELEMENT_STRING)
      return tb_cursor_read_value_dictionary(cursor, count);

    return count == 0 ? true : tb_cursor_read_string_dictionary(cursor, count);
  }
}

/* The next run of a run-length family: its length, checked against the rows the
 * column has left, then its value. */
static bool tb_cursor_read_run(tb_cursor* cursor) {
  int32_t length = 0;

  if (!tb_read_counter32(cursor->reader, &length))
    return false;

  /* + 1 because the row this run was read for is already counted out of
   * rows_remaining by its next call. */
  if (length < 1 || length > cursor->rows_remaining + 1) {
    return tb_fail(cursor->reader,
           "%s: a run of %d values cannot cover the %d rows left in the column",
           cursor->field_name, length, cursor->rows_remaining + 1);
  }

  cursor->run_remaining = length;

  return tb_read_counter32(cursor->reader, &cursor->run_value);
}

static bool tb_cursor_dictionary_entry(const tb_cursor* cursor, int32_t index,
             const char** out) {
  if (index < 0 || index >= cursor->dictionary_count) {
    return tb_fail(cursor->reader,
           "%s: dictionary index %d is out of range - the dictionary holds %d entries",
           cursor->field_name, index, cursor->dictionary_count);
  }

  *out = cursor->dictionary[index];
  return true;
}

/* The bytes of the next row's dictionary entry, for a fixed-width element.
 *
 * The one place a value-dictionary row is counted out, so every member reading
 * through it - i64, f32, f64 - decrements exactly once whichever way it came. */
static bool tb_cursor_next_value_entry(tb_cursor* cursor, const uint8_t** out) {
  int32_t index = 0;

  cursor->rows_remaining--;

  if (cursor->encoding == TB_ENCODING_DICT) {
    if (!tb_read_counter32(cursor->reader, &index))
      return false;
  } else {
    if (cursor->run_remaining == 0 && !tb_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    index = cursor->run_value;
  }

  if (index < 0 || index >= cursor->value_count) {
    return tb_fail(cursor->reader,
           "%s: dictionary index %d is out of range - the dictionary holds %d entries",
           cursor->field_name, index, cursor->value_count);
  }

  *out = cursor->value_dictionary + (size_t)index * (size_t)cursor->value_width;
  return true;
}

bool tb_cursor_next_length(tb_cursor* cursor, int32_t* out) {
  tb_reader* reader = cursor->reader;
  int32_t length = 0;

  if (tb_failed(reader))
    return false;

  if (cursor->lengths != NULL) {
    if (cursor->length_at >= cursor->length_count)
      return tb_fail(reader, "%s: the column has no more rows to read", cursor->field_name);

    *out = cursor->lengths[cursor->length_at++];
    return true;
  }

  if (!tb_read_counter32(reader, &length))
    return false;

  if (length < 0)
    return tb_fail(reader, "%s: a row declares %d elements", cursor->field_name, length);

  *out = length;
  return true;
}

/* The next value of a bit-packed stream: the packed bits, over the block's base.
 *
 * A value may cross a byte boundary, so this walks bits rather than bytes. The addition
 * wraps, mirroring the writer's wrapping subtraction. */
static int64_t tb_cursor_next_packed(tb_cursor* cursor) {
  uint64_t slot = 0;
  int32_t at;

  for (at = 0; at < cursor->packed_width; ++at, ++cursor->packed_bit) {
    int64_t bit = cursor->packed_bit;

    if ((cursor->packed[bit >> 3] >> (bit & 7)) & 1)
      slot |= (uint64_t)1 << at;
  }

  return (int64_t)((uint64_t)cursor->packed_base + slot);
}

bool tb_cursor_next_i32(tb_cursor* cursor, int32_t* out) {
  tb_reader* reader = cursor->reader;

  if (tb_failed(reader))
    return false;

  cursor->rows_remaining--;

  if (cursor->encoding == TB_ENCODING_BITPACK) {
    *out = (int32_t)tb_cursor_next_packed(cursor);
    return true;
  }

  switch (cursor->encoding) {
  case TB_ENCODING_RAW:
    if (cursor->element == TB_ELEMENT_I32)
      return tb_read_int32(reader, out);

    return tb_read_counter32(reader, out);

  case TB_ENCODING_VARINT:
    return tb_read_counter32(reader, out);

  case TB_ENCODING_DELTA: {
    int32_t value = 0;

    if (!tb_read_counter32(reader, &value))
      return false;

    /* The addition wraps on purpose, mirroring the writer's wrapping subtraction;
     * together they are exact for every int32 pair. On uint32_t, because signed
     * overflow is undefined in C. */
    if (cursor->started) {
      cursor->previous = (int32_t)((uint32_t)cursor->previous + (uint32_t)value);
    } else {
      cursor->previous = value;
      cursor->started = true;
    }

    *out = cursor->previous;
    return true;
  }

  case TB_ENCODING_RLE:
    if (cursor->run_remaining == 0 && !tb_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    *out = cursor->run_value;
    return true;

  default: /* TB_ENCODING_DELTA_RLE; tb_check_column refused everything else. */
    if (!cursor->started) {
      if (!tb_read_counter32(reader, &cursor->previous))
        return false;

      cursor->started = true;
      *out = cursor->previous;
      return true;
    }

    if (cursor->run_remaining == 0 && !tb_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    cursor->previous = (int32_t)((uint32_t)cursor->previous + (uint32_t)cursor->run_value);
    *out = cursor->previous;
    return true;
  }
}

bool tb_cursor_next_i64(tb_cursor* cursor, int64_t* out) {
  if (cursor->element != TB_ELEMENT_I64) {
    int32_t narrower = 0;
    bool ok = tb_cursor_next_i32(cursor, &narrower);

    *out = narrower;
    return ok;
  }

  if (tb_failed(cursor->reader))
    return false;

  if (cursor->encoding == TB_ENCODING_BITPACK) {
    cursor->rows_remaining--;
    *out = tb_cursor_next_packed(cursor);
    return true;
  }

  if (cursor->value_width != 0) {
    const uint8_t* entry = NULL;

    if (!tb_cursor_next_value_entry(cursor, &entry))
      return false;

    *out = (int64_t)tb_load_fixed64(entry);
    return true;
  }

  cursor->rows_remaining--;

  return tb_read_int64(cursor->reader, out);
}

bool tb_cursor_next_f32(tb_cursor* cursor, float* out) {
  /* The block said its values are whole numbers and carried them as integers, and the
   * writer only said so because the conversion back lands on the same bytes the raw
   * layout would have written. */
  if (cursor->whole_numbers) {
    int32_t integer = 0;
    bool ok = tb_cursor_next_i32(cursor, &integer);

    *out = (float)integer;
    return ok;
  }

  if (tb_failed(cursor->reader))
    return false;

  if (cursor->value_width != 0) {
    const uint8_t* entry = NULL;
    uint32_t bits;

    if (!tb_cursor_next_value_entry(cursor, &entry))
      return false;

    /* Through memcpy, as the raw read is: the entry's bytes are the value's bit
     * pattern, and reading them as a float any other way is a type C does not
     * let one object have. */
    bits = tb_load_fixed32(entry);
    memcpy(out, &bits, sizeof *out);
    return true;
  }

  cursor->rows_remaining--;

  return tb_read_float(cursor->reader, out);
}

bool tb_cursor_next_f64(tb_cursor* cursor, double* out) {
  if (cursor->whole_numbers) {
    int32_t integer = 0;
    bool ok = tb_cursor_next_i32(cursor, &integer);

    *out = integer;
    return ok;
  }

  if (cursor->element == TB_ELEMENT_F32) {
    float single = 0.0f;
    bool ok = tb_cursor_next_f32(cursor, &single);

    *out = single;
    return ok;
  }

  if (cursor->element != TB_ELEMENT_F64) {
    int32_t integer = 0;
    bool ok = tb_cursor_next_i32(cursor, &integer);

    *out = integer;
    return ok;
  }

  if (tb_failed(cursor->reader))
    return false;

  if (cursor->value_width != 0) {
    const uint8_t* entry = NULL;
    uint64_t bits;

    if (!tb_cursor_next_value_entry(cursor, &entry))
      return false;

    bits = tb_load_fixed64(entry);
    memcpy(out, &bits, sizeof *out);
    return true;
  }

  cursor->rows_remaining--;

  return tb_read_double(cursor->reader, out);
}

bool tb_cursor_next_bool(tb_cursor* cursor, bool* out) {
  if (cursor->encoding == TB_ENCODING_RLE
      || cursor->encoding == TB_ENCODING_BITPACK) {
    int32_t value = 0;
    bool ok = tb_cursor_next_i32(cursor, &value);

    *out = value != 0;
    return ok;
  }

  if (tb_failed(cursor->reader))
    return false;

  cursor->rows_remaining--;

  return tb_read_bool(cursor->reader, out);
}

bool tb_cursor_next_string(tb_cursor* cursor, const char** out) {
  tb_reader* reader = cursor->reader;

  if (tb_failed(reader))
    return false;

  cursor->rows_remaining--;

  switch (cursor->encoding) {
  case TB_ENCODING_RAW:
    return tb_read_string(reader, out);

  /* A front coded dictionary was decoded to whole strings at construction, so from
   * here it is the same dictionary as any other. */
  case TB_ENCODING_DICT:
  case TB_ENCODING_DICT_FRONT: {
    int32_t index = 0;

    if (!tb_read_counter32(reader, &index))
      return false;

    return tb_cursor_dictionary_entry(cursor, index, out);
  }

  default: /* TB_ENCODING_DICT_RLE and TB_ENCODING_DICT_FRONT_RLE */
    if (cursor->run_remaining == 0 && !tb_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    return tb_cursor_dictionary_entry(cursor, cursor->run_value, out);
  }
}

bool tb_cursor_next_same_i32(tb_cursor* cursor, int32_t limit, int32_t* out_count,
           int32_t* out) {
  int32_t n;

  *out_count = 1;

  if (tb_failed(cursor->reader))
    return false;

  if (cursor->encoding == TB_ENCODING_RLE) {
    cursor->rows_remaining--;

    if (cursor->run_remaining == 0 && !tb_cursor_read_run(cursor))
      return false;

    n = cursor->run_remaining < limit ? cursor->run_remaining : limit;
    cursor->run_remaining -= n;
    cursor->rows_remaining -= n - 1;

    *out_count = n;
    *out = cursor->run_value;

    return true;
  }

  if (cursor->encoding == TB_ENCODING_DELTA_RLE && cursor->started) {
    cursor->rows_remaining--;

    if (cursor->run_remaining == 0 && !tb_cursor_read_run(cursor))
      return false;

    if (cursor->run_value == 0) {
      /* A zero-delta run is a run of one value. */
      n = cursor->run_remaining < limit ? cursor->run_remaining : limit;
      cursor->run_remaining -= n;
      cursor->rows_remaining -= n - 1;

      *out_count = n;
      *out = cursor->previous;

      return true;
    }

    cursor->run_remaining--;
    cursor->previous = (int32_t)((uint32_t)cursor->previous + (uint32_t)cursor->run_value);
    *out = cursor->previous;

    return true;
  }

  return tb_cursor_next_i32(cursor, out);
}

bool tb_cursor_next_same_string(tb_cursor* cursor, int32_t limit, int32_t* out_count,
              const char** out) {
  int32_t n;

  *out_count = 1;

  if (tb_failed(cursor->reader))
    return false;

  if (cursor->encoding == TB_ENCODING_DICT_RLE
      || cursor->encoding == TB_ENCODING_DICT_FRONT_RLE) {
    cursor->rows_remaining--;

    if (cursor->run_remaining == 0 && !tb_cursor_read_run(cursor))
      return false;

    n = cursor->run_remaining < limit ? cursor->run_remaining : limit;
    cursor->run_remaining -= n;
    cursor->rows_remaining -= n - 1;

    *out_count = n;

    return tb_cursor_dictionary_entry(cursor, cursor->run_value, out);
  }

  return tb_cursor_next_string(cursor, out);
}

/* MSVC deprecates fopen and a project built with warnings as errors will not
 * take it. Branching here rather than defining _CRT_SECURE_NO_WARNINGS, which
 * a header has no business turning off for whoever includes it. */
static FILE* tb_fopen_read(const char* filename) {
#if defined(_MSC_VER)
  FILE* file = NULL;

  if (fopen_s(&file, filename, "rb") != 0)
    return NULL;

  return file;
#else
  return fopen(filename, "rb");
#endif
}

bool tb_read_all_bytes(const char* filename, uint8_t** out_data, int32_t* out_length) {
  FILE* file;
  long size;
  uint8_t* buffer;

  *out_data = NULL;
  *out_length = 0;

  file = tb_fopen_read(filename);
  if (file == NULL)
    return false;

  if (fseek(file, 0, SEEK_END) != 0) {
    fclose(file);
    return false;
  }

  size = ftell(file);

  if (size < 0 || size > 0x7FFFFFFF || fseek(file, 0, SEEK_SET) != 0) {
    fclose(file);
    return false;
  }

  /* One byte over, so a zero-length file still gets a non-NULL pointer and
   * the caller's "did the allocation work" check means what it says. */
  buffer = (uint8_t*)malloc((size_t)size + 1);
  if (buffer == NULL) {
    fclose(file);
    return false;
  }

  if (size > 0 && fread(buffer, 1, (size_t)size, file) != (size_t)size) {
    free(buffer);
    fclose(file);
    return false;
  }

  fclose(file);

  *out_data = buffer;
  *out_length = (int32_t)size;
  return true;
}

void tb_free_bytes(uint8_t* data) { free(data); }

/* The ChaCha20 stream cipher of RFC 8439, as the file envelope uses it.
 *
 * Here rather than from a library because what a library offers is an authenticated
 * construction, which changes the length. This format wants a plain keystream: applying
 * it leaves every byte count as it was, so the structural checks - the block lengths that
 * must sum exactly - hold over the ciphertext unchanged. Under two hundred lines with no
 * dependency, which is what lets the same cipher exist in every runtime that has to read
 * one of these files. */
static uint32_t tb_rotl32(uint32_t value, int count) {
  return (value << count) | (value >> (32 - count));
}

static void tb_chacha20_quarter(uint32_t* block, int a, int b, int c, int d) {
  block[a] += block[b]; block[d] = tb_rotl32(block[d] ^ block[a], 16);
  block[c] += block[d]; block[b] = tb_rotl32(block[b] ^ block[c], 12);
  block[a] += block[b]; block[d] = tb_rotl32(block[d] ^ block[a], 8);
  block[c] += block[d]; block[b] = tb_rotl32(block[b] ^ block[c], 7);
}

/* One 64-byte keystream block: twenty rounds over a copy of the state. */
static void tb_chacha20_block(const uint32_t* state, uint8_t* keystream) {
  uint32_t working[16];
  int round;
  int at;

  memcpy(working, state, sizeof working);

  /* Ten double rounds. Each is four column quarter-rounds and four diagonal ones, which
   * between them let every word reach every other. */
  for (round = 0; round < 10; ++round) {
    tb_chacha20_quarter(working, 0, 4, 8, 12);
    tb_chacha20_quarter(working, 1, 5, 9, 13);
    tb_chacha20_quarter(working, 2, 6, 10, 14);
    tb_chacha20_quarter(working, 3, 7, 11, 15);

    tb_chacha20_quarter(working, 0, 5, 10, 15);
    tb_chacha20_quarter(working, 1, 6, 11, 12);
    tb_chacha20_quarter(working, 2, 7, 8, 13);
    tb_chacha20_quarter(working, 3, 4, 9, 14);
  }

  /* Added back to the state it started from, which is what stops the rounds being
   * reversible and so the keystream being recoverable. */
  for (at = 0; at < 16; ++at) {
    uint32_t word = working[at] + state[at];

    keystream[at * 4] = (uint8_t)word;
    keystream[at * 4 + 1] = (uint8_t)(word >> 8);
    keystream[at * 4 + 2] = (uint8_t)(word >> 16);
    keystream[at * 4 + 3] = (uint8_t)(word >> 24);
  }
}

/* Exclusive-ors the keystream over `data`, in place.
 *
 * One routine for both directions, which is what a stream cipher is: the keystream
 * depends only on the key, the nonce and the position, so applying it twice returns what
 * went in. The block counter starts at zero. */
static void tb_chacha20_apply(const uint8_t* key, const uint8_t* nonce, uint8_t* data,
            int32_t length) {
  uint32_t state[16];
  uint8_t keystream[64];
  int32_t offset;
  int at;

  /* "expand 32-byte k", as four little-endian words. */
  state[0] = 0x61707865u;
  state[1] = 0x3320646eu;
  state[2] = 0x79622d32u;
  state[3] = 0x6b206574u;

  for (at = 0; at < 8; ++at)
    state[4 + at] = tb_load_fixed32(key + at * 4);

  state[12] = 0;

  for (at = 0; at < 3; ++at)
    state[13 + at] = tb_load_fixed32(nonce + at * 4);

  for (offset = 0; offset < length; offset += 64) {
    int32_t count = length - offset < 64 ? length - offset : 64;
    int32_t i;

    tb_chacha20_block(state, keystream);

    for (i = 0; i < count; ++i)
      data[offset + i] ^= keystream[i];

    state[12]++;
  }
}

/* HMAC-SHA-256 over the file, truncated to the sixteen bytes the header keeps for it.
 *
 * Written out here for the same reason the cipher is: C has no standard library to take it
 * from, and this runtime is a single header with no dependencies.
 *
 * What the tag catches is what the structural checks cannot. A block length that does not
 * add up is a malformed file and the reader says so; four other bytes in an f32 column is a
 * well-formed file holding a different number, and no check over a file's shape can tell
 * that from data that was always there. */

static uint32_t tb_sha256_rotate_right(uint32_t value, int count) {
  return (value >> count) | (value << (32 - count));
}

/* One 64-byte block of the compression function. */
static void tb_sha256_block(uint32_t* state, const uint8_t* data) {
  /* The fractional parts of the cube roots of the first 64 primes. */
  static const uint32_t k[64] = {
    0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u,
    0x923f82a4u, 0xab1c5ed5u, 0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u,
    0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u, 0xe49b69c1u, 0xefbe4786u,
    0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
    0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u,
    0x06ca6351u, 0x14292967u, 0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u,
    0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u, 0xa2bfe8a1u, 0xa81a664bu,
    0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
    0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au,
    0x5b9cca4fu, 0x682e6ff3u, 0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u,
    0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
  };

  uint32_t schedule[64];
  uint32_t a, b, c, d, e, f, g, h;
  int at;

  for (at = 0; at < 16; at++) {
    schedule[at] = (uint32_t)data[at * 4] << 24
       | (uint32_t)data[at * 4 + 1] << 16
       | (uint32_t)data[at * 4 + 2] << 8
       | (uint32_t)data[at * 4 + 3];
  }

  for (at = 16; at < 64; at++) {
    const uint32_t before = schedule[at - 15];
    const uint32_t near_by = schedule[at - 2];

    const uint32_t s0 = tb_sha256_rotate_right(before, 7)
       ^ tb_sha256_rotate_right(before, 18) ^ (before >> 3);

    const uint32_t s1 = tb_sha256_rotate_right(near_by, 17)
       ^ tb_sha256_rotate_right(near_by, 19) ^ (near_by >> 10);

    schedule[at] = schedule[at - 16] + s0 + schedule[at - 7] + s1;
  }

  a = state[0]; b = state[1]; c = state[2]; d = state[3];
  e = state[4]; f = state[5]; g = state[6]; h = state[7];

  for (at = 0; at < 64; at++) {
    const uint32_t s1 = tb_sha256_rotate_right(e, 6) ^ tb_sha256_rotate_right(e, 11)
       ^ tb_sha256_rotate_right(e, 25);

    const uint32_t choice = (e & f) ^ (~e & g);
    const uint32_t one = h + s1 + choice + k[at] + schedule[at];

    const uint32_t s0 = tb_sha256_rotate_right(a, 2) ^ tb_sha256_rotate_right(a, 13)
       ^ tb_sha256_rotate_right(a, 22);

    const uint32_t majority = (a & b) ^ (a & c) ^ (b & c);
    const uint32_t two = s0 + majority;

    h = g; g = f; f = e;
    e = d + one;
    d = c; c = b; b = a;
    a = one + two;
  }

  state[0] += a; state[1] += b; state[2] += c; state[3] += d;
  state[4] += e; state[5] += f; state[6] += g; state[7] += h;
}

/* One piece of a message: hashing takes several, and joining them would copy the file. */
typedef struct tb_hash_piece {
  const uint8_t* data;
  int32_t length;
} tb_hash_piece;

/* SHA-256 of the pieces, hashed as though they were one message. */
static void tb_sha256(const tb_hash_piece* pieces, int count, uint8_t* digest) {
  uint32_t state[8];
  uint8_t partial[64];
  uint8_t tail[128];
  int32_t filled = 0;
  uint64_t length = 0;
  uint64_t bits;
  int32_t tail_length;
  int piece;
  int at;

  state[0] = 0x6a09e667u; state[1] = 0xbb67ae85u;
  state[2] = 0x3c6ef372u; state[3] = 0xa54ff53au;
  state[4] = 0x510e527fu; state[5] = 0x9b05688cu;
  state[6] = 0x1f83d9abu; state[7] = 0x5be0cd19u;

  for (piece = 0; piece < count; piece++) {
    const uint8_t* data = pieces[piece].data;
    const int32_t size = pieces[piece].length;
    int32_t taken = 0;

    length += (uint64_t)size;

    /* The partial block first, then whole blocks straight out of the piece: the copy into
     * `partial` is only for the bytes that straddle a boundary. */
    while (taken < size) {
      int32_t taking;

      if (filled == 0 && size - taken >= 64) {
        tb_sha256_block(state, data + taken);
        taken += 64;
        continue;
      }

      taking = 64 - filled < size - taken ? 64 - filled : size - taken;
      memcpy(partial + filled, data + taken, (size_t)taking);

      filled += taking;
      taken += taking;

      if (filled == 64) {
        tb_sha256_block(state, partial);
        filled = 0;
      }
    }
  }

  /* The padding: a set bit, zeros, and the message length in bits as a 64-bit big-endian
   * number. Two blocks when the length does not fit in the one that is open. */
  tail_length = filled + 9 > 64 ? 128 : 64;
  memset(tail, 0, sizeof tail);
  memcpy(tail, partial, (size_t)filled);
  tail[filled] = 0x80;

  bits = length * 8;

  for (at = 0; at < 8; at++)
    tail[tail_length - 1 - at] = (uint8_t)(bits >> (at * 8));

  for (at = 0; at < tail_length; at += 64)
    tb_sha256_block(state, tail + at);

  for (at = 0; at < 8; at++) {
    digest[at * 4] = (uint8_t)(state[at] >> 24);
    digest[at * 4 + 1] = (uint8_t)(state[at] >> 16);
    digest[at * 4 + 2] = (uint8_t)(state[at] >> 8);
    digest[at * 4 + 3] = (uint8_t)state[at];
  }
}

/* The tag for a file: HMAC-SHA-256 over every byte but the sixteen the tag lives in.
 *
 * Skipping them is the same as zeroing them and cheaper by a copy of the file. */
static void tb_mac_tag(const uint8_t* key, int32_t key_length, const uint8_t* data,
       int32_t length, uint8_t* out) {
  uint8_t block_key[64];
  uint8_t inner[64];
  uint8_t outer[64];
  uint8_t inner_digest[32];
  uint8_t full[32];
  tb_hash_piece message[3];
  tb_hash_piece outer_message[2];
  int at;

  memset(block_key, 0, sizeof block_key);

  /* A key longer than the block is hashed first; ours is thirty-two bytes, but the rule is
   * part of HMAC and leaving it out would make this agree with nothing. */
  if (key_length > 64) {
    tb_hash_piece whole[1];

    whole[0].data = key;
    whole[0].length = key_length;

    tb_sha256(whole, 1, block_key);
  } else {
    memcpy(block_key, key, (size_t)key_length);
  }

  for (at = 0; at < 64; at++) {
    inner[at] = (uint8_t)(block_key[at] ^ 0x36);
    outer[at] = (uint8_t)(block_key[at] ^ 0x5c);
  }

  message[0].data = inner;
  message[0].length = 64;
  message[1].data = data;
  message[1].length = TB_MAC_OFFSET;
  message[2].data = data + TB_KEY_CHECK_OFFSET;
  message[2].length = length - TB_KEY_CHECK_OFFSET;

  tb_sha256(message, 3, inner_digest);

  outer_message[0].data = outer;
  outer_message[0].length = 64;
  outer_message[1].data = inner_digest;
  outer_message[1].length = 32;

  tb_sha256(outer_message, 2, full);
  memcpy(out, full, TB_MAC_SIZE);
}

/* Four bytes as the fixed32 the signature and the key check are compared as. */
static uint32_t tb_read_magic(const uint8_t* at) {
  return (uint32_t)at[0] | (uint32_t)at[1] << 8 | (uint32_t)at[2] << 16 | (uint32_t)at[3] << 24;
}

bool tb_open(uint8_t* data, int32_t length, const uint8_t* key, int32_t key_length,
       const uint8_t* mac_key, int32_t mac_key_length, bool verify_mac,
       const uint8_t** out_data, int32_t* out_length, char* error, size_t error_size) {
  int32_t at;

  *out_data = NULL;
  *out_length = 0;

  if (data == NULL || length < TB_HEADER_SIZE) {
    tb_copy_error(error, error_size, "", "the file is too short to be a table");
    return false;
  }

  if (tb_read_magic(data + TB_MAGIC_OFFSET) != TB_MAGIC) {
    tb_copy_error(error, error_size, "",
      "the file does not begin with the table file signature");
    return false;
  }

  /* Nothing to check with when no key was given, and a file that carries a tag is read
   * anyway rather than refused: a client built before the project turned MACs on is one this
   * format has promised can still read what it is sent. */
  if (verify_mac && mac_key != NULL && mac_key_length > 0) {
    uint8_t expected[TB_MAC_SIZE];
    uint8_t difference = 0;
    bool present = false;

    if (mac_key_length != 32) {
      tb_copy_error(error, error_size, "", "the MAC key given is not 32 bytes");
      return false;
    }

    for (at = 0; at < TB_MAC_SIZE && !present; at++)
      present = data[TB_MAC_OFFSET + at] != 0;

    if (!present) {
      tb_copy_error(error, error_size, "",
        "the file carries no MAC and this build expects one - it was exported without a "
        "MAC key, or the field was cleared after it was written");
      return false;
    }

    tb_mac_tag(mac_key, mac_key_length, data, length, expected);

    /* Every byte, always: a comparison that returns early tells the caller how far it got. */
    for (at = 0; at < TB_MAC_SIZE; at++)
      difference |= (uint8_t)(expected[at] ^ data[TB_MAC_OFFSET + at]);

    if (difference != 0) {
      tb_copy_error(error, error_size, "",
        "the file does not match its MAC - it was altered after it was exported, or it "
        "was signed with a different key");
      return false;
    }
  }

  if ((data[TB_FLAGS_OFFSET] & TB_FLAG_ENCRYPTED) == 0) {
    *out_data = data;
    *out_length = length;
    return true;
  }

  if (data[TB_CIPHER_OFFSET] != TB_CIPHER_CHACHA20) {
    char message[96];

    snprintf(message, sizeof message,
       "the file uses cipher %d, which this reader does not know",
       (int)data[TB_CIPHER_OFFSET]);

    tb_copy_error(error, error_size, "", message);
    return false;
  }

  if (key == NULL || key_length != 32) {
    tb_copy_error(error, error_size, "",
      "the file is encrypted and no key, or a key that is not 32 bytes, was given");
    return false;
  }

  tb_chacha20_apply(key, data + TB_NONCE_OFFSET, data + TB_KEY_CHECK_OFFSET,
       length - TB_KEY_CHECK_OFFSET);

  /* The key check separates "the key is wrong" from "the file is damaged". A MAC that
   * verifies does not answer this one - the two keys are different keys. */
  if (tb_read_magic(data + TB_KEY_CHECK_OFFSET) != TB_MAGIC) {
    tb_copy_error(error, error_size, "",
      "the file did not decrypt to a table - the key is not the one it was written with");
    return false;
  }

  /* Back to what a plain file holds in these bytes, so that a second call over the same
   * buffer passes it through instead of decrypting it again. */
  /* The complement written as an exclusive-or against 0xFF rather than as ~: the flag is an
   * unsigned int, so ~ of it is a 32-bit value that does not fit the byte it is assigned to,
   * and a compiler with warnings as errors is right to say so. */
  data[TB_FLAGS_OFFSET] &= (uint8_t)(0xFFu ^ TB_FLAG_ENCRYPTED);
  data[TB_CIPHER_OFFSET] = TB_CIPHER_NONE;

  for (at = 0; at < TB_NONCE_SIZE; at++)
    data[TB_NONCE_OFFSET + at] = 0;

  *out_data = data;
  *out_length = length;

  return true;
}

int32_t tb_reserve_bound(int32_t count) {
  const int32_t max_up_front = 65536;

  if (count < 0)
    return 0;

  return count < max_up_front ? count : max_up_front;
}

bool tb_row_count_is_plausible(const tb_reader* reader, int32_t row_count, int32_t min_row_bytes) {
  int32_t remaining;

  if (row_count < 0)
    return false;

  if (min_row_bytes <= 0)
    return true;

  remaining = reader->length - reader->position;

  return row_count <= remaining / min_row_bytes;
}

bool tb_fail_with(tb_reader* reader, const char* message) {
  return tb_fail(reader, "%s", message);
}

void tb_copy_error(char* error, size_t error_size, const char* context, const char* message) {
  if (error == NULL || error_size == 0)
    return;

  if (message == NULL || message[0] == '\0')
    snprintf(error, error_size, "%s", context != NULL ? context : "");
  else if (context == NULL || context[0] == '\0')
    snprintf(error, error_size, "%s", message);
  else
    snprintf(error, error_size, "%s: %s", context, message);
}

static int tb_index_compare(const void* left, const void* right) {
  const int32_t a = ((const tb_index_entry*)left)->key;
  const int32_t b = ((const tb_index_entry*)right)->key;

  /* Not a - b: that overflows for keys at opposite ends of the range, and the
   * result feeds straight into qsort's ordering. */
  return a < b ? -1 : (a > b ? 1 : 0);
}

void tb_index_sort(tb_index_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, tb_index_compare);
}

int32_t tb_index_find(const tb_index_entry* entries, int32_t count, int32_t key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL)
    return -1;

  while (low <= high) {
    /* low + (high - low) / 2 rather than (low + high) / 2, which overflows
     * once a table passes about a billion rows. */
    int32_t middle = low + (high - low) / 2;
    int32_t at = entries[middle].key;

    if (at == key)
      return entries[middle].position;

    if (at < key)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

static int tb_index64_compare(const void* left, const void* right) {
  const int64_t a = ((const tb_index64_entry*)left)->key;
  const int64_t b = ((const tb_index64_entry*)right)->key;

  return a < b ? -1 : (a > b ? 1 : 0);
}

void tb_index64_sort(tb_index64_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, tb_index64_compare);
}

int32_t tb_index64_find(const tb_index64_entry* entries, int32_t count, int64_t key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL)
    return -1;

  while (low <= high) {
    int32_t middle = low + (high - low) / 2;
    int64_t at = entries[middle].key;

    if (at == key)
      return entries[middle].position;

    if (at < key)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

static int tb_string_index_compare(const void* left, const void* right) {
  const char* a = ((const tb_string_index_entry*)left)->key;
  const char* b = ((const tb_string_index_entry*)right)->key;

  /* Never NULL: a string member that the file carried no column for is set to
   * the empty literal before the read, which is what keeps this from being the
   * one comparison that has to check. */
  return strcmp(a, b);
}

void tb_string_index_sort(tb_string_index_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, tb_string_index_compare);
}

int32_t tb_string_index_find(const tb_string_index_entry* entries, int32_t count,
                             const char* key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL || key == NULL)
    return -1;

  while (low <= high) {
    int32_t middle = low + (high - low) / 2;
    int at = strcmp(entries[middle].key, key);

    if (at == 0)
      return entries[middle].position;

    if (at < 0)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

static int tb_uuid_index_compare(const void* left, const void* right) {
  const tb_uuid* a = &((const tb_uuid_index_entry*)left)->key;
  const tb_uuid* b = &((const tb_uuid_index_entry*)right)->key;

  /* Byte order, not .NET's display order. Nothing here shows the key to anyone;
   * it only has to be a total order, and the same one on every platform. */
  return memcmp(a->bytes, b->bytes, sizeof a->bytes);
}

void tb_uuid_index_sort(tb_uuid_index_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, tb_uuid_index_compare);
}

int32_t tb_uuid_index_find(const tb_uuid_index_entry* entries, int32_t count,
                           tb_uuid key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL)
    return -1;

  while (low <= high) {
    int32_t middle = low + (high - low) / 2;
    int at = memcmp(entries[middle].key.bytes, key.bytes, sizeof key.bytes);

    if (at == 0)
      return entries[middle].position;

    if (at < 0)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

#ifdef __cplusplus
}
#endif

#endif /* TABBIT_TCB_IMPLEMENTATION */

#endif /* TABBIT_TCB_READER_H */
