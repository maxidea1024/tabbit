-- Round-trip check for the generated Lua reader of a `set` and a `map`.
--
-- Reads the binary the exporter wrote and prints, for one row, both layers of the container
-- surface: the arrays in the file's order, and what the lookups beside them answer.
--
-- **The lookups are the part nothing else can see.** The exported JSON says what the arrays
-- hold, so a reader that filled them wrongly is already caught; a lookup built from the wrong
-- column, or not built at all, produces exactly the same JSON.
--
-- Run with the generated output directory as the working directory, so `require("tables")`
-- resolves through the default package.path. spec/types/set-and-map.md section 7.

local binary_dir = arg and arg[1]

if binary_dir == nil then
  io.stderr:write("usage: harness.lua <binary-directory>\n")
  os.exit(2)
end

local tables_module = require("tables")

local tables = tables_module.new()
tables:readAll(binary_dir)

local first = tables.shop.records[1].bag
local empty = tables.shop.records[3].bag

local at = first.drops.indexByKey[2]

local function quoted(values)
  local parts = {}
  for i = 1, #values do
    parts[i] = '"' .. values[i] .. '"'
  end
  return "[" .. table.concat(parts, ",") .. "]"
end

local function numbers(values)
  local parts = {}
  for i = 1, #values do
    parts[i] = tostring(values[i])
  end
  return "[" .. table.concat(parts, ",") .. "]"
end

local function count(t)
  local n = 0
  for _ in pairs(t) do n = n + 1 end
  return n
end

local parts = {
  '"tags":' .. quoted(first.tags),
  '"hasSale":' .. tostring(first.tagsSet["sale"] == true),
  '"hasGone":' .. tostring(first.tagsSet["gone"] == true),

  -- A map of scalars answers with the value.
  '"priceOf11":' .. tostring(first.prices.byKey[11]),

  -- A map of records answers with the entry's position, and the fields are read at it.
  -- One-based here, like every other index in this language - and reported zero-based so
  -- the harness can ask every language the same question.
  '"dropIndexOf2":' .. tostring(at - 1),
  '"dropItemAt2":' .. tostring(first.drops.value.itemId[at]),
  '"dropCountAt2":' .. tostring(first.drops.value.count[at]),

  -- The array keeps the file's order, which a table does not.
  '"priceKeys":' .. numbers(first.prices.key),

  -- And a row that wrote nothing has containers of no entries rather than none.
  '"emptyTagCount":' .. tostring(count(empty.tagsSet)),
  '"emptyPriceCount":' .. tostring(count(empty.prices.byKey)),
}

print("{" .. table.concat(parts, ",") .. "}")
