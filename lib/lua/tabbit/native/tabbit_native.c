/* Tabbit's native module for Lua: the work a byte-at-a-time Lua loop cannot afford.
 *
 * Copied in beside the generated accessor so the emitted code needs nothing installed.
 * Edit it in the Tabbit repository.
 *
 * Three functions reach Lua:
 *
 *   open(bytes, key, macKey, verifyMac)  signature check, MAC verification, key check
 *                                        and ChaCha20 decryption in one call; returns
 *                                        the plaintext-shaped bytes
 *   md5hex(bytes)                        the updater's manifest hash
 *   mkdir(path)                          directory creation, which Lua's os library
 *                                        does not have; the updater needs it
 *
 * The cipher and hash code is the C runtime's (lib/c/tabbit), transcribed rather than
 * shared so that this file compiles alone: it includes lua.h and the C standard library
 * and nothing else. It builds against Lua 5.1 (LuaJIT) through 5.4.
 *
 * Two ways to load it:
 *
 *   - As a module: compile to a shared library named native.dll / native.so in a
 *     `tabbit` directory on package.cpath. require("tabbit.native") finds
 *     luaopen_tabbit_native there.
 *   - Statically, the way a game engine embeds Lua: compile this file into the host
 *     and register it before any table loads:
 *
 *       luaL_requiref(L, "tabbit.native", luaopen_tabbit_native, 0);
 *       lua_pop(L, 1);
 *
 *     (On Lua 5.1 / LuaJIT, which has no luaL_requiref, put the function into
 *     package.preload["tabbit.native"] instead.)
 */

/* Before any libc header, for nanosleep: glibc reads the macro in the first one a
 * translation unit pulls in. */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#define _POSIX_C_SOURCE 200809L
#endif

#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdio.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <direct.h>
#else
#include <sys/stat.h>
#include <sys/types.h>
#include <time.h>
#endif

#include "lua.h"
#include "lauxlib.h"

/* ------------------------------------------------------------- envelope layout */

#define TB_MAGIC 0x00424354u /* "TCB\0" as a little-endian fixed32 */
#define TB_MAGIC_OFFSET 0
#define TB_FLAGS_OFFSET 8
#define TB_CIPHER_OFFSET 9
#define TB_NONCE_OFFSET 10
#define TB_MAC_OFFSET 22
#define TB_KEY_CHECK_OFFSET 38
#define TB_HEADER_SIZE 42
#define TB_NONCE_SIZE 12
#define TB_MAC_SIZE 16
#define TB_FLAG_ENCRYPTED 0x01
#define TB_CIPHER_NONE 0
#define TB_CIPHER_CHACHA20 1

static uint32_t tbn_load_fixed32(const uint8_t* at) {
  return (uint32_t)at[0] | (uint32_t)at[1] << 8 | (uint32_t)at[2] << 16 | (uint32_t)at[3] << 24;
}

/* ------------------------------------------------------------------- ChaCha20 */

/* The ChaCha20 stream cipher of RFC 8439, as the file envelope uses it: a plain
 * keystream, applied in place, block counter starting at zero. */

static uint32_t tbn_rotl32(uint32_t value, int count) {
  return (value << count) | (value >> (32 - count));
}

static void tbn_chacha20_quarter(uint32_t* block, int a, int b, int c, int d) {
  block[a] += block[b]; block[d] = tbn_rotl32(block[d] ^ block[a], 16);
  block[c] += block[d]; block[b] = tbn_rotl32(block[b] ^ block[c], 12);
  block[a] += block[b]; block[d] = tbn_rotl32(block[d] ^ block[a], 8);
  block[c] += block[d]; block[b] = tbn_rotl32(block[b] ^ block[c], 7);
}

/* One 64-byte keystream block: twenty rounds over a copy of the state. */
static void tbn_chacha20_block(const uint32_t* state, uint8_t* keystream) {
  uint32_t working[16];
  int round;
  int at;

  memcpy(working, state, sizeof working);

  for (round = 0; round < 10; ++round) {
    tbn_chacha20_quarter(working, 0, 4, 8, 12);
    tbn_chacha20_quarter(working, 1, 5, 9, 13);
    tbn_chacha20_quarter(working, 2, 6, 10, 14);
    tbn_chacha20_quarter(working, 3, 7, 11, 15);

    tbn_chacha20_quarter(working, 0, 5, 10, 15);
    tbn_chacha20_quarter(working, 1, 6, 11, 12);
    tbn_chacha20_quarter(working, 2, 7, 8, 13);
    tbn_chacha20_quarter(working, 3, 4, 9, 14);
  }

  for (at = 0; at < 16; ++at) {
    uint32_t word = working[at] + state[at];

    keystream[at * 4] = (uint8_t)word;
    keystream[at * 4 + 1] = (uint8_t)(word >> 8);
    keystream[at * 4 + 2] = (uint8_t)(word >> 16);
    keystream[at * 4 + 3] = (uint8_t)(word >> 24);
  }
}

static void tbn_chacha20_apply(const uint8_t* key, const uint8_t* nonce, uint8_t* data,
             int32_t length) {
  uint32_t state[16];
  uint8_t keystream[64];
  int32_t offset;
  int at;

  state[0] = 0x61707865u;
  state[1] = 0x3320646eu;
  state[2] = 0x79622d32u;
  state[3] = 0x6b206574u;

  for (at = 0; at < 8; ++at)
    state[4 + at] = tbn_load_fixed32(key + at * 4);

  state[12] = 0;

  for (at = 0; at < 3; ++at)
    state[13 + at] = tbn_load_fixed32(nonce + at * 4);

  for (offset = 0; offset < length; offset += 64) {
    int32_t count = length - offset < 64 ? length - offset : 64;
    int32_t i;

    tbn_chacha20_block(state, keystream);

    for (i = 0; i < count; ++i)
      data[offset + i] ^= keystream[i];

    state[12]++;
  }
}

/* ------------------------------------------------------------------- SHA-256 */

static uint32_t tbn_sha256_rotate_right(uint32_t value, int count) {
  return (value >> count) | (value << (32 - count));
}

static void tbn_sha256_block(uint32_t* state, const uint8_t* data) {
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

    const uint32_t s0 = tbn_sha256_rotate_right(before, 7)
       ^ tbn_sha256_rotate_right(before, 18) ^ (before >> 3);

    const uint32_t s1 = tbn_sha256_rotate_right(near_by, 17)
       ^ tbn_sha256_rotate_right(near_by, 19) ^ (near_by >> 10);

    schedule[at] = schedule[at - 16] + s0 + schedule[at - 7] + s1;
  }

  a = state[0]; b = state[1]; c = state[2]; d = state[3];
  e = state[4]; f = state[5]; g = state[6]; h = state[7];

  for (at = 0; at < 64; at++) {
    const uint32_t s1 = tbn_sha256_rotate_right(e, 6) ^ tbn_sha256_rotate_right(e, 11)
       ^ tbn_sha256_rotate_right(e, 25);

    const uint32_t choice = (e & f) ^ (~e & g);
    const uint32_t one = h + s1 + choice + k[at] + schedule[at];

    const uint32_t s0 = tbn_sha256_rotate_right(a, 2) ^ tbn_sha256_rotate_right(a, 13)
       ^ tbn_sha256_rotate_right(a, 22);

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
typedef struct tbn_hash_piece {
  const uint8_t* data;
  int32_t length;
} tbn_hash_piece;

/* SHA-256 of the pieces, hashed as though they were one message. */
static void tbn_sha256(const tbn_hash_piece* pieces, int count, uint8_t* digest) {
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

    while (taken < size) {
      int32_t taking;

      if (filled == 0 && size - taken >= 64) {
        tbn_sha256_block(state, data + taken);
        taken += 64;
        continue;
      }

      taking = 64 - filled < size - taken ? 64 - filled : size - taken;
      memcpy(partial + filled, data + taken, (size_t)taking);

      filled += taking;
      taken += taking;

      if (filled == 64) {
        tbn_sha256_block(state, partial);
        filled = 0;
      }
    }
  }

  tail_length = filled + 9 > 64 ? 128 : 64;
  memset(tail, 0, sizeof tail);
  memcpy(tail, partial, (size_t)filled);
  tail[filled] = 0x80;

  bits = length * 8;

  for (at = 0; at < 8; at++)
    tail[tail_length - 1 - at] = (uint8_t)(bits >> (at * 8));

  for (at = 0; at < tail_length; at += 64)
    tbn_sha256_block(state, tail + at);

  for (at = 0; at < 8; at++) {
    digest[at * 4] = (uint8_t)(state[at] >> 24);
    digest[at * 4 + 1] = (uint8_t)(state[at] >> 16);
    digest[at * 4 + 2] = (uint8_t)(state[at] >> 8);
    digest[at * 4 + 3] = (uint8_t)state[at];
  }
}

/* The tag for a file: HMAC-SHA-256 over every byte but the sixteen the tag lives in. */
static void tbn_mac_tag(const uint8_t* key, int32_t key_length, const uint8_t* data,
        int32_t length, uint8_t* out) {
  uint8_t block_key[64];
  uint8_t inner[64];
  uint8_t outer[64];
  uint8_t inner_digest[32];
  uint8_t full[32];
  tbn_hash_piece message[3];
  tbn_hash_piece outer_message[2];
  int at;

  memset(block_key, 0, sizeof block_key);

  if (key_length > 64) {
    tbn_hash_piece whole[1];

    whole[0].data = key;
    whole[0].length = key_length;

    tbn_sha256(whole, 1, block_key);
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

  tbn_sha256(message, 3, inner_digest);

  outer_message[0].data = outer;
  outer_message[0].length = 64;
  outer_message[1].data = inner_digest;
  outer_message[1].length = 32;

  tbn_sha256(outer_message, 2, full);
  memcpy(out, full, TB_MAC_SIZE);
}

/* ----------------------------------------------------------------------- MD5 */

/* The updater's manifest hash. Transcribed from the C updater for the same reason the
 * cipher is transcribed from the C reader. */

static uint32_t tbn_md5_rotate(uint32_t value, uint32_t bits) {
  return (value << bits) | (value >> (32u - bits));
}

static void tbn_md5_hex(const uint8_t* data, size_t length, char out[33]) {
  static const uint32_t SHIFTS[64] = {
    7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
    5,  9, 14, 20, 5,  9, 14, 20, 5,  9, 14, 20, 5,  9, 14, 20,
    4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
    6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
  };

  static const uint32_t SINES[64] = {
    0xd76aa478u, 0xe8c7b756u, 0x242070dbu, 0xc1bdceeeu,
    0xf57c0fafu, 0x4787c62au, 0xa8304613u, 0xfd469501u,
    0x698098d8u, 0x8b44f7afu, 0xffff5bb1u, 0x895cd7beu,
    0x6b901122u, 0xfd987193u, 0xa679438eu, 0x49b40821u,
    0xf61e2562u, 0xc040b340u, 0x265e5a51u, 0xe9b6c7aau,
    0xd62f105du, 0x02441453u, 0xd8a1e681u, 0xe7d3fbc8u,
    0x21e1cde6u, 0xc33707d6u, 0xf4d50d87u, 0x455a14edu,
    0xa9e3e905u, 0xfcefa3f8u, 0x676f02d9u, 0x8d2a4c8au,
    0xfffa3942u, 0x8771f681u, 0x6d9d6122u, 0xfde5380cu,
    0xa4beea44u, 0x4bdecfa9u, 0xf6bb4b60u, 0xbebfbc70u,
    0x289b7ec6u, 0xeaa127fau, 0xd4ef3085u, 0x04881d05u,
    0xd9d4d039u, 0xe6db99e5u, 0x1fa27cf8u, 0xc4ac5665u,
    0xf4292244u, 0x432aff97u, 0xab9423a7u, 0xfc93a039u,
    0x655b59c3u, 0x8f0ccc92u, 0xffeff47du, 0x85845dd1u,
    0x6fa87e4fu, 0xfe2ce6e0u, 0xa3014314u, 0x4e0811a1u,
    0xf7537e82u, 0xbd3af235u, 0x2ad7d2bbu, 0xeb86d391u
  };

  uint32_t a0 = 0x67452301u, b0 = 0xefcdab89u, c0 = 0x98badcfeu, d0 = 0x10325476u;
  uint64_t bit_length = (uint64_t)length * 8u;
  size_t padded;
  size_t offset;
  uint8_t tail[128];
  size_t tail_length;
  size_t whole;
  int i;

  whole = length - (length % 64);

  padded = (length % 64) + 1;
  if (padded > 56) {
    tail_length = 128;
  } else {
    tail_length = 64;
  }

  memset(tail, 0, sizeof tail);
  memcpy(tail, data + whole, length % 64);
  tail[length % 64] = 0x80;

  for (i = 0; i < 8; ++i)
    tail[tail_length - 8 + i] = (uint8_t)((bit_length >> (8 * i)) & 0xFFu);

  for (offset = 0; offset < whole + tail_length; offset += 64) {
    const uint8_t* chunk = offset < whole ? data + offset : tail + (offset - whole);
    uint32_t words[16];
    uint32_t a = a0, b = b0, c = c0, d = d0;

    for (i = 0; i < 16; ++i) {
      words[i] = (uint32_t)chunk[i * 4]
               | ((uint32_t)chunk[i * 4 + 1] << 8)
               | ((uint32_t)chunk[i * 4 + 2] << 16)
               | ((uint32_t)chunk[i * 4 + 3] << 24);
    }

    for (i = 0; i < 64; ++i) {
      uint32_t f;
      int g;

      if (i < 16) {
        f = (b & c) | (~b & d);
        g = i;
      } else if (i < 32) {
        f = (d & b) | (~d & c);
        g = (5 * i + 1) % 16;
      } else if (i < 48) {
        f = b ^ c ^ d;
        g = (3 * i + 5) % 16;
      } else {
        f = c ^ (b | ~d);
        g = (7 * i) % 16;
      }

      f = f + a + SINES[i] + words[g];

      a = d;
      d = c;
      c = b;
      b = b + tbn_md5_rotate(f, SHIFTS[i]);
    }

    a0 += a;
    b0 += b;
    c0 += c;
    d0 += d;
  }

  {
    const uint32_t digest[4] = { a0, b0, c0, d0 };
    static const char HEX[] = "0123456789abcdef";
    int at = 0;
    int word;

    for (word = 0; word < 4; ++word) {
      int byte;

      for (byte = 0; byte < 4; ++byte) {
        uint8_t value = (uint8_t)((digest[word] >> (8 * byte)) & 0xFFu);

        out[at++] = HEX[(value >> 4) & 0xF];
        out[at++] = HEX[value & 0xF];
      }
    }

    out[32] = '\0';
  }
}

/* ------------------------------------------------------------- Lua bindings */

/* open(bytes, key, macKey, verifyMac) -> plaintext bytes, or raises.
 *
 * The order is verify, then decrypt: the tag covers the file as it is stored, so an
 * altered file is refused before the key is used on it. Lua strings are immutable, so
 * unlike the C runtime's tb_open this works on a copy and hands back a new string, with
 * the envelope fields returned to what a plain file holds - a second call over the
 * result passes it through instead of decrypting it again. One allocation per file, the
 * shape Java and Kotlin already have.
 *
 * The error messages are the shared ones every runtime uses, under the reader's "tcb: "
 * prefix so the Lua side's failures and this one's read the same. */
static int tbn_open(lua_State* L) {
  size_t data_length = 0;
  const char* data = luaL_checklstring(L, 1, &data_length);
  size_t key_length = 0;
  const char* key = lua_isnoneornil(L, 2) ? NULL : luaL_checklstring(L, 2, &key_length);
  size_t mac_key_length = 0;
  const char* mac_key =
    lua_isnoneornil(L, 3) ? NULL : luaL_checklstring(L, 3, &mac_key_length);
  int verify_mac = lua_isnoneornil(L, 4) ? 1 : lua_toboolean(L, 4);
  uint8_t* copy;
  int at;

  if (data_length < TB_HEADER_SIZE)
    return luaL_error(L, "tcb: the file is too short to be a table");

  if (data_length > 0x7FFFFFFF)
    return luaL_error(L, "tcb: the file is too large to be a table");

  if (tbn_load_fixed32((const uint8_t*)data + TB_MAGIC_OFFSET) != TB_MAGIC)
    return luaL_error(L, "tcb: the file does not begin with the table file signature");

  if (verify_mac && mac_key != NULL && mac_key_length > 0) {
    uint8_t expected[TB_MAC_SIZE];
    uint8_t difference = 0;
    int present = 0;

    if (mac_key_length != 32)
      return luaL_error(L, "tcb: the MAC key given is not 32 bytes");

    for (at = 0; at < TB_MAC_SIZE && !present; at++)
      present = data[TB_MAC_OFFSET + at] != 0;

    if (!present)
      return luaL_error(L,
        "tcb: the file carries no MAC and this build expects one - it was exported "
        "without a MAC key, or the field was cleared after it was written");

    tbn_mac_tag((const uint8_t*)mac_key, (int32_t)mac_key_length, (const uint8_t*)data,
      (int32_t)data_length, expected);

    /* Every byte, always: a comparison that returns early tells the caller how far
     * it got. */
    for (at = 0; at < TB_MAC_SIZE; at++)
      difference |= (uint8_t)(expected[at] ^ (uint8_t)data[TB_MAC_OFFSET + at]);

    if (difference != 0)
      return luaL_error(L,
        "tcb: the file does not match its MAC - it was altered after it was exported, "
        "or it was signed with a different key");
  }

  if (((uint8_t)data[TB_FLAGS_OFFSET] & TB_FLAG_ENCRYPTED) == 0) {
    lua_pushvalue(L, 1);
    return 1;
  }

  if ((uint8_t)data[TB_CIPHER_OFFSET] != TB_CIPHER_CHACHA20)
    return luaL_error(L, "tcb: the file uses cipher %d, which this reader does not know",
      (int)(uint8_t)data[TB_CIPHER_OFFSET]);

  if (key == NULL || key_length != 32)
    return luaL_error(L,
      "tcb: the file is encrypted and no key, or a key that is not 32 bytes, was given");

  /* A userdata rather than a luaL_Buffer, which differs between 5.1 and 5.3+; the
   * garbage collector frees it once the pushed string below is the only survivor. */
  copy = (uint8_t*)lua_newuserdata(L, data_length);
  memcpy(copy, data, data_length);

  tbn_chacha20_apply((const uint8_t*)key, copy + TB_NONCE_OFFSET,
    copy + TB_KEY_CHECK_OFFSET, (int32_t)(data_length - TB_KEY_CHECK_OFFSET));

  /* The key check separates "the key is wrong" from "the file is damaged". */
  if (tbn_load_fixed32(copy + TB_KEY_CHECK_OFFSET) != TB_MAGIC)
    return luaL_error(L,
      "tcb: the file did not decrypt to a table - the key is not the one it was "
      "written with");

  /* Back to what a plain file holds in these bytes, so that a second call over what
   * this returns passes it through instead of decrypting it again. */
  copy[TB_FLAGS_OFFSET] &= (uint8_t)(0xFFu ^ TB_FLAG_ENCRYPTED);
  copy[TB_CIPHER_OFFSET] = TB_CIPHER_NONE;

  for (at = 0; at < TB_NONCE_SIZE; at++)
    copy[TB_NONCE_OFFSET + at] = 0;

  lua_pushlstring(L, (const char*)copy, data_length);

  return 1;
}

/* md5hex(bytes) -> 32 lower-case hex characters. */
static int tbn_md5hex(lua_State* L) {
  size_t length = 0;
  const char* data = luaL_checklstring(L, 1, &length);
  char out[33];

  tbn_md5_hex((const uint8_t*)data, length, out);
  lua_pushlstring(L, out, 32);

  return 1;
}

/* mkdir(path) -> true, or nil and a message. One level; the caller walks the path. */
static int tbn_mkdir(lua_State* L) {
  const char* path = luaL_checkstring(L, 1);
  int result;

#ifdef _WIN32
  result = _mkdir(path);
#else
  result = mkdir(path, 0777);
#endif

  if (result == 0) {
    lua_pushboolean(L, 1);
    return 1;
  }

  lua_pushnil(L);
  lua_pushfstring(L, "cannot create directory %s", path);

  return 2;
}

/* Exported explicitly rather than through LUALIB_API, which marks the interpreter's
 * own exports and is an import declaration from a module's point of view. */
#if defined(_WIN32)
#define TBN_EXPORT __declspec(dllexport)
#else
#define TBN_EXPORT
#endif

/* sleepMs(milliseconds) - the updater's retry backoff. Not in Lua's os library, and a
 * busy loop would be the alternative. */
static int tbn_sleep_ms(lua_State* L) {
  lua_Integer milliseconds = luaL_checkinteger(L, 1);

  if (milliseconds < 0)
    milliseconds = 0;

#ifdef _WIN32
  Sleep((DWORD)milliseconds);
#else
  {
    struct timespec wait;

    wait.tv_sec = (time_t)(milliseconds / 1000);
    wait.tv_nsec = (long)(milliseconds % 1000) * 1000000L;

    nanosleep(&wait, NULL);
  }
#endif

  return 0;
}

/* Assembled by hand rather than with luaL_register / luaL_setfuncs, which differ
 * between 5.1 and 5.3+; pushing four functions is the part they agree on. */
TBN_EXPORT int luaopen_tabbit_native(lua_State* L) {
  lua_newtable(L);

  lua_pushcfunction(L, tbn_open);
  lua_setfield(L, -2, "open");

  lua_pushcfunction(L, tbn_md5hex);
  lua_setfield(L, -2, "md5hex");

  lua_pushcfunction(L, tbn_mkdir);
  lua_setfield(L, -2, "mkdir");

  lua_pushcfunction(L, tbn_sleep_ms);
  lua_setfield(L, -2, "sleepMs");

  return 1;
}
