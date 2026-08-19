-- Tabbit's numeric backend for Lua 5.3 and later.
--
-- The twin of tcb_ops_jit.lua - see the note there. This file uses 5.3 operator syntax
-- (integer division, bitwise operators, string.unpack), which is why it is a separate
-- file: LuaJIT's parser rejects the syntax, so the reader loads one backend or the
-- other and never both.
--
-- 64-bit values are native integers here. Integer arithmetic wraps in 5.3, which is
-- exactly what the delta and bit-pack decodes are defined over.

local unpack = string.unpack

local ops = {}

ops.name = "lua53"

-- A signed 32-bit integer, little endian, at 0-based offset `at`.
function ops.i32(s, at)
  return (unpack("<i4", s, at + 1))
end

function ops.u32(s, at)
  return (unpack("<I4", s, at + 1))
end

function ops.i64(s, at)
  return (unpack("<i8", s, at + 1))
end

function ops.f32(s, at)
  return (unpack("<f", s, at + 1))
end

function ops.f64(s, at)
  return (unpack("<d", s, at + 1))
end

-- The zig-zag fold of a 64-bit varint, from its 7-bit pieces in stream order.
function ops.dezig64(pieces)
  local encoded = 0

  for i = #pieces, 1, -1 do
    encoded = (encoded << 7) | pieces[i]
  end

  return (encoded >> 1) ~ -(encoded & 1)
end

-- A wrapping int32 sum, mirroring the writer's wrapping subtraction.
function ops.add32(a, b)
  local v = (a + b) & 0xFFFFFFFF

  if v >= 0x80000000 then
    v = v - 0x100000000
  end

  return v
end

-- A fresh bit-pack accumulator, and setting one bit of it.
ops.slot0 = 0

function ops.setbit(slot, at)
  return slot | (1 << at)
end

-- Whether bit `i` of a byte value is set.
function ops.bittest(byte, i)
  return (byte >> i) & 1 == 1
end

-- base + slot over 64 bits, wrapping - which native integer addition already does.
function ops.addbase64(base, slot)
  return base + slot
end

-- The low 32 bits of a 64-bit value, sign extended.
function ops.to32(v)
  local x = v & 0xFFFFFFFF

  if x >= 0x80000000 then
    x = x - 0x100000000
  end

  return x
end

-- A number as the float subtype: what a whole-number float column's values become.
function ops.tofloat(n)
  return n + 0.0
end

-- An int64 as decimal digits.
function ops.i64string(v)
  return string.format("%d", v)
end

return ops
