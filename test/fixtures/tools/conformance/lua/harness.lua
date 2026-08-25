-- Conformance harness for the generated Lua reader.
--
-- Reads Vectors.tcb through the generated accessor and prints each row in the canonical
-- form described in ../README.md. No parsing here: the generated reader does that. Run
-- with the generated output directory as the working directory, so `require("tables")`
-- resolves through the default package.path.

local binary_dir = arg and arg[1]

if binary_dir == nil then
  io.stderr:write("usage: harness.lua <binary-directory>\n")
  os.exit(1)
end

local tables_module = require("tables")
local tcb = require("tabbit.tcb_reader")

-- The corpus is signed, so the key goes in before the first read - which is the whole
-- of what a consuming project does about the MAC. Without it the files would still
-- load, and nothing here would notice: the check is the reader's, and it needs the key
-- to run.
local mac_key_hex = os.getenv("TABBIT_TEST_TCB_MAC_KEY")

if mac_key_hex ~= nil and #mac_key_hex > 0 then
  tables_module.macKey =
    (mac_key_hex:gsub("%x%x", function(pair) return string.char(tonumber(pair, 16)) end))
end

local ok, err = pcall(function()
  local tables = tables_module.new()
  tables:readAll(binary_dir)

  -- JSON, written out by hand: neither supported runtime has a JSON library, and the
  -- canonical form is narrow enough that borrowing one would cost more than these
  -- twenty lines.
  local function escaped(s)
    s = s:gsub("\\", "\\\\"):gsub("\"", "\\\"")
    s = s:gsub("%c", function(c) return string.format("\\u%04x", string.byte(c)) end)
    return "\"" .. s .. "\""
  end

  -- An int64 - cdata under LuaJIT, an integer under 5.3+, a plain number where the file
  -- carried something narrower - as the decimal string the contract asks for.
  local function int64(value)
    if type(value) == "cdata" then
      return tcb.int64String(value)
    end

    return string.format("%d", value)
  end

  local function number(value)
    if value == math.floor(value) and value >= -2 ^ 53 and value <= 2 ^ 53 then
      return string.format("%d", value)
    end

    return string.format("%.17g", value)
  end

  local function array(values, render)
    local out = {}

    for i = 1, #values do
      out[i] = render(values[i])
    end

    return "[" .. table.concat(out, ",") .. "]"
  end

  local rows = {}

  for i = 1, #tables.vectors.records do
    local record = tables.vectors.records[i]

    rows[i] = "{"
      .. "\"index\":" .. number(record.index)
      .. ",\"intVal\":" .. number(record.intVal)

      -- A string, because JSON's single numeric type would round anything past 2^53 -
      -- which two of the corpus rows are.
      .. ",\"bigVal\":\"" .. int64(record.bigVal) .. "\""

      .. ",\"floatVal\":" .. number(record.floatVal)
      .. ",\"doubleVal\":" .. number(record.doubleVal)
      .. ",\"text\":" .. escaped(record.text)
      .. ",\"flag\":" .. tostring(record.flag)

      -- Ticks, which is what the generated fields hold.
      .. ",\"when\":\"" .. int64(record.when) .. "\""
      .. ",\"span\":\"" .. int64(record.span) .. "\""

      .. ",\"uid\":" .. escaped(record.uid)
      .. ",\"label\":" .. number(record.label)
      .. ",\"ints\":" .. array(record.ints, number)
      .. ",\"strs\":" .. array(record.strs, escaped)
      .. ",\"labels\":" .. array(record.labels, number)
      .. ",\"uids\":" .. array(record.uids, escaped)

      -- The reference indices, which is what the exporter writes for a foreign field.
      .. ",\"owner\":" .. number(record.owner)
      .. ",\"tier\":" .. number(record.tierIndex)

      -- And one reference per element, printed as the stored index each came in as.
      .. ",\"owners\":" .. array(record.owners, number)

      .. ",\"count\":" .. number(record.count)
      .. ",\"route\":" .. escaped(record.route)
      .. ",\"zone\":" .. escaped(record.zone)
      .. "}"
  end

  io.write("[" .. table.concat(rows, ",") .. "]")
end)

if not ok then
  -- The reason has to reach standard error: a reader that refuses for the right reason
  -- must be distinguishable from one that refuses for the wrong one.
  io.stderr:write(tostring(err) .. "\n")
  os.exit(1)
end
