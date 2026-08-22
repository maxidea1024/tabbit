-- Tabbit's data updater.
--
-- Copied in beside the generated accessor so the emitted code needs nothing installed.
-- Edit it in the Tabbit repository.
--
-- Brings a local copy of the exported data up to date with a copy served over HTTP - a
-- CDN, a bucket, a patch server - so a running program can take new data without being
-- redeployed. Reads nothing but the manifest, so it knows nothing about the schema and
-- never has to change when one does.
--
-- Three properties, because a patcher that fails badly is worse than one that does not
-- exist: nothing is replaced until everything has arrived and been checked; every file
-- is checked against the hash the manifest gives for it; a transient failure is retried
-- with a doubling backoff and a permanent one is not.
--
-- Two things are not this file's:
--
--   HTTP. Lua's standard library has none, and the program embedding this - a game
--   client above all - has its own stack already; requiring a second one would be the
--   cost with no benefit. `options.fetch` is how the transfer comes in: a function
--   taking a URL and returning the body, or nil, a message, and whether the failure is
--   worth retrying. spec/lua-language-support.md.
--
--   The hash, the directories and the waiting, which come from tabbit.native - the MD5
--   is a byte loop Lua cannot afford, and mkdir and a sleep are simply not in os.

local _prefix = (...):match("^(.-)[^%.]*$")

local function native_module()
  local ok, native = pcall(require, _prefix .. "native")

  if ok then
    return native
  end

  error("tcb: the updater needs the tabbit.native module, and it is not loadable - " ..
    "compile tabbit/native/tabbit_native.c into your host or build it as a Lua module", 0)
end

local updater = {}

-- The entries of a manifest's JSON: name, size, hash per file. A narrow parser rather
-- than a JSON library - the manifest is the exporter's, its shape does not vary, and
-- neither supported runtime ships a parser to borrow.
local function parse_manifest(text)
  local at = 1

  local function skip_space()
    at = string.find(text, "[^ \t\r\n]", at) or #text + 1
  end

  local function fail_at(what)
    error(("the manifest is not readable: %s at byte %d"):format(what, at), 0)
  end

  local parse_value

  local function parse_string()
    if text:sub(at, at) ~= "\"" then
      fail_at("expected a string")
    end

    local out = {}
    at = at + 1

    while true do
      local c = text:sub(at, at)

      if c == "" then
        fail_at("unterminated string")
      elseif c == "\"" then
        at = at + 1
        return table.concat(out)
      elseif c == "\\" then
        local escape = text:sub(at + 1, at + 1)
        local plain = { ["\""] = "\"", ["\\"] = "\\", ["/"] = "/", b = "\b",
                        f = "\f", n = "\n", r = "\r", t = "\t" }

        if plain[escape] ~= nil then
          out[#out + 1] = plain[escape]
          at = at + 2
        elseif escape == "u" then
          -- The manifest's names are file names; a code point above 7F is carried
          -- through as the replacement the writer used, which for this file is none.
          local hex = text:sub(at + 2, at + 5)
          local code = tonumber(hex, 16) or fail_at("a broken \\u escape")

          if code < 0x80 then
            out[#out + 1] = string.char(code)
          else
            out[#out + 1] = "?"
          end

          at = at + 6
        else
          fail_at("an unknown escape")
        end
      else
        out[#out + 1] = c
        at = at + 1
      end
    end
  end

  local function parse_number()
    local from = at
    at = string.find(text, "[^-+0-9eE.]", at) or #text + 1
    return tonumber(text:sub(from, at - 1)) or fail_at("a broken number")
  end

  parse_value = function()
    skip_space()
    local c = text:sub(at, at)

    if c == "\"" then
      return parse_string()
    elseif c == "{" then
      local out = {}
      at = at + 1
      skip_space()

      if text:sub(at, at) == "}" then
        at = at + 1
        return out
      end

      while true do
        skip_space()
        local key = parse_string()
        skip_space()

        if text:sub(at, at) ~= ":" then
          fail_at("expected a colon")
        end

        at = at + 1
        out[key] = parse_value()
        skip_space()

        local nextc = text:sub(at, at)
        at = at + 1

        if nextc == "}" then
          return out
        elseif nextc ~= "," then
          fail_at("expected , or }")
        end
      end
    elseif c == "[" then
      local out = {}
      at = at + 1
      skip_space()

      if text:sub(at, at) == "]" then
        at = at + 1
        return out
      end

      while true do
        out[#out + 1] = parse_value()
        skip_space()

        local nextc = text:sub(at, at)
        at = at + 1

        if nextc == "]" then
          return out
        elseif nextc ~= "," then
          fail_at("expected , or ]")
        end
      end
    elseif text:sub(at, at + 3) == "true" then
      at = at + 4
      return true
    elseif text:sub(at, at + 4) == "false" then
      at = at + 5
      return false
    elseif text:sub(at, at + 3) == "null" then
      at = at + 4
      return nil
    else
      return parse_number()
    end
  end

  local document = parse_value()
  local items = type(document) == "table" and document.Items or nil

  if type(items) ~= "table" then
    error("the manifest has no Items array", 0)
  end

  local entries = {}

  for i = 1, #items do
    local item = items[i]

    if type(item) == "table" and item.Name ~= nil and item.Name ~= "" then
      entries[#entries + 1] = {
        name = item.Name,
        size = item.Size or 0,
        hash = item.Hash or "",
      }
    end
  end

  return entries
end

updater.parseManifest = parse_manifest

-- Joins a base URL and a file name. Not a path join, which on Windows would produce a
-- backslash and a URL no server will answer.
local function join_url(base_url, name)
  return (base_url:gsub("/+$", "")) .. "/" .. (name:gsub("\\", "/"))
end

local function join_path(directory, name)
  return directory .. "/" .. (name:gsub("\\", "/"))
end

-- Creates a path's directories, one level at a time - native.mkdir is one mkdir(2), and
-- a level that already exists answers an error this ignores.
local function make_directories(native, path)
  local built = nil

  for piece in path:gmatch("[^/\\]+") do
    built = built and (built .. "/" .. piece) or piece
    native.mkdir(built)
  end
end

local function read_file(path)
  local handle = io.open(path, "rb")

  if handle == nil then
    return nil
  end

  local data = handle:read("*a")
  handle:close()

  return data
end

local function write_file(path, data)
  local handle, err = io.open(path, "wb")

  if handle == nil then
    error(("cannot write %s: %s"):format(path, err or "unknown error"), 0)
  end

  handle:write(data)
  handle:close()
end

-- The cached manifest. A missing or unreadable one is an empty manifest, which makes
-- the next update fetch everything - the safe direction to be wrong in.
local function read_local_manifest(path)
  local text = read_file(path)

  if text == nil then
    return {}
  end

  local ok, entries = pcall(parse_manifest, text)

  return ok and entries or {}
end

-- Fetches one URL through options.fetch, retrying what the fetch says is worth
-- retrying, with a doubling backoff.
local function download(native, url, options, log)
  local delay = options.retryDelay or 0.5
  local attempts = math.max(1, options.maxAttempts or 3)

  for attempt = 1, attempts do
    local body, message, transient = options.fetch(url)

    if body ~= nil then
      return body
    end

    if not transient or attempt >= attempts then
      error(message or (url .. " could not be fetched"), 0)
    end

    log(("tabbit: %s Retrying in %.1fs (%d of %d)."):format(
      message or "fetch failed.", delay, attempt, attempts))

    native.sleepMs(math.floor(delay * 1000))

    -- Doubling rather than a fixed wait: a server refusing because it is overloaded is
    -- not helped by every client coming back at the same interval.
    delay = delay * 2
  end
end

-- Brings `cacheDirectory` up to date with the data served under `baseUrl`.
--
-- Does not raise. Everything that can go wrong here - the network, the disk, a file
-- that arrived corrupt - is a condition the caller has to handle rather than a defect.
-- The result says what happened; `localPath` is set even on failure, because the
-- previous data is still there and still readable, which is the point of failing the
-- way this does.
--
-- `options.fetch` is required - see the note at the top of this file. Everything else
-- has a working default: manifestFileName, maxAttempts, retryDelay (seconds),
-- verifyHash, log.
function updater.update(base_url, cache_directory, options)
  options = options or {}

  local result = {
    succeeded = false,
    error = nil,
    upToDate = false,
    downloadedCount = 0,
    downloadedBytes = 0,
    deletedCount = 0,
    localPath = cache_directory,
  }

  local function log(message)
    if options.log ~= nil then
      options.log(message)
    end
  end

  local ok, failure = pcall(function()
    if type(options.fetch) ~= "function" then
      error("options.fetch is required: a function taking a URL and returning the " ..
        "body, or nil, a message, and whether the failure is transient", 0)
    end

    local native = native_module()
    local manifest_name = options.manifestFileName or "manifest-binary.json"
    local verify = options.verifyHash ~= false

    local manifest_text = download(native, join_url(base_url, manifest_name), options, log)
    local remote = parse_manifest(manifest_text)
    local local_entries = read_local_manifest(join_path(cache_directory, manifest_name))

    local by_name = {}

    for i = 1, #local_entries do
      by_name[local_entries[i].name] = local_entries[i]
    end

    local wanted = {}

    for i = 1, #remote do
      local entry = remote[i]
      local previous = by_name[entry.name]

      -- The file's presence is checked as well as the manifest's word for it: a cache
      -- somebody cleaned out by hand would otherwise never be refilled.
      local current = previous ~= nil
        and previous.hash == entry.hash
        and read_file(join_path(cache_directory, entry.name)) ~= nil

      if not current then
        wanted[#wanted + 1] = entry
      end
    end

    local served = {}

    for i = 1, #remote do
      served[remote[i].name] = true
    end

    local gone = {}

    for i = 1, #local_entries do
      if not served[local_entries[i].name] then
        gone[#gone + 1] = local_entries[i].name
      end
    end

    if #wanted == 0 and #gone == 0 then
      log("tabbit: already up to date.")

      result.succeeded = true
      result.upToDate = true
      return
    end

    log(("tabbit: %d file(s) to fetch, %d to remove."):format(#wanted, #gone))

    -- Everything lands here first. Nothing the caller can read is touched until the
    -- last file has arrived and been checked. Stale files a killed run left behind are
    -- harmless: only this run's names are moved out, and a name this run downloads is
    -- written over.
    local staging = join_path(cache_directory, ".staging")

    make_directories(native, cache_directory)
    make_directories(native, staging)

    for i = 1, #wanted do
      local entry = wanted[i]
      local data = download(native, join_url(base_url, entry.name), options, log)

      if verify and entry.hash ~= "" then
        local actual = native.md5hex(data)

        if actual:lower() ~= entry.hash:lower() then
          error(("%s arrived with hash %s, and the manifest says %s. Nothing was " ..
            "replaced."):format(entry.name, actual, entry.hash), 0)
        end
      end

      local staged = join_path(staging, entry.name)

      make_directories(native, staged:match("^(.*)[/\\][^/\\]+$") or "")
      write_file(staged, data)

      result.downloadedBytes = result.downloadedBytes + #data
    end

    -- From here on the update is applied. Nothing below reaches the network.
    for i = 1, #gone do
      os.remove(join_path(cache_directory, gone[i]))
      result.deletedCount = result.deletedCount + 1
    end

    for i = 1, #wanted do
      local entry = wanted[i]
      local target = join_path(cache_directory, entry.name)

      make_directories(native, target:match("^(.*)[/\\][^/\\]+$") or "")

      -- Removed first: os.rename does not replace an existing file on Windows.
      os.remove(target)
      local moved, err = os.rename(join_path(staging, entry.name), target)

      if not moved then
        error(("cannot move %s into place: %s"):format(entry.name, err or "unknown"), 0)
      end

      result.downloadedCount = result.downloadedCount + 1
    end

    -- Last, and that ordering is the recovery story: a run killed before this point
    -- leaves a manifest describing the data that is still on disk, so the next run
    -- fetches the same files again rather than believing it has them.
    write_file(join_path(cache_directory, manifest_name), manifest_text)

    -- os.remove takes an empty directory where the platform's remove() does; where it
    -- does not, an empty .staging stays behind and costs nothing.
    os.remove(staging)

    log(("tabbit: updated. %d fetched, %d removed."):format(
      result.downloadedCount, result.deletedCount))

    result.succeeded = true
  end)

  if not ok then
    -- The previous data is untouched, so the caller can carry on with it.
    result.error = tostring(failure)
    result.succeeded = false

    log("tabbit: update failed: " .. result.error)
  end

  return result
end

return updater
