-- Drives the Lua updater for the test suite: one update, and the result as JSON on
-- standard output.
--
-- The injected fetch runs curl to a temporary file and reads it back - binary safe,
-- and on the PATH of every machine this suite supports. It is an example of the
-- contract, not part of the updater: a consuming program brings its own HTTP.
--
-- Usage: lua main.lua <baseUrl> <cacheDirectory>

local updater = require("tabbit.updater")

local base_url = assert(arg[1], "usage: main.lua <baseUrl> <cacheDirectory>")
local cache = assert(arg[2], "usage: main.lua <baseUrl> <cacheDirectory>")

local fetches = 0

local function fetch(url)
  fetches = fetches + 1

  -- In the working directory rather than the cache, which does not exist before the
  -- first manifest arrives.
  local temporary = ".fetch-" .. fetches .. ".tmp"

  local command = string.format(
    'curl -s -o "%s" -w "%%{http_code}" --max-time 30 "%s"', temporary, url)

  local pipe = io.popen(command, "r")
  local answer = pipe and pipe:read("*a") or ""

  if pipe then
    pipe:close()
  end

  local status = tonumber(answer:match("%d+") or "0") or 0

  if status == 200 then
    local handle = io.open(temporary, "rb")
    local body = handle and handle:read("*a") or nil

    if handle then
      handle:close()
    end

    os.remove(temporary)

    if body ~= nil then
      return body
    end

    return nil, url .. " downloaded nothing.", true
  end

  os.remove(temporary)

  -- 408 and 429 are the server asking for another attempt, and 5xx is it failing on
  -- its own account; 0 is curl never reaching it. A 404 is an answer.
  local transient = status == 0 or status == 408 or status == 429
    or (status >= 500 and status <= 599)

  return nil, url .. " answered " .. status .. ".", transient
end

local result = updater.update(base_url, cache, {
  fetch = fetch,

  -- Fast retries: what the gate checks is the count, not the pacing.
  retryDelay = 0.05,
})

local function json_string(s)
  return "\"" .. s:gsub("\\", "\\\\"):gsub("\"", "\\\""):gsub("%c", " ") .. "\""
end

io.write(string.format(
  '{"succeeded":%s,"upToDate":%s,"downloadedCount":%d,"deletedCount":%d,"error":%s}\n',
  tostring(result.succeeded),
  tostring(result.upToDate),
  result.downloadedCount,
  result.deletedCount,
  result.error == nil and "null" or json_string(result.error)))
