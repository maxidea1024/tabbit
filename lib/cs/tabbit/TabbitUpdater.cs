// ---------------------------------------------------------------------------
// Tabbit's data updater.
//
// Brings a local copy of the exported data up to date with a copy served over HTTP - a
// CDN, a bucket, a patch server - so a build can take new data without shipping a new
// binary. Emitted beside the generated accessor and reads nothing but the manifest, so it
// knows nothing about the schema and never has to be regenerated when one changes.
//
// The manifest is what the exporter already writes next to the data: one entry per file
// with its size and MD5, plus a hash over the whole set. Comparing it with the local copy
// is the whole of the diff, so a run downloads the files that changed and nothing else.
//
// Three properties this holds to, because a patcher that fails badly is worse than one
// that does not exist:
//
//   Nothing is replaced until everything is downloaded and verified. Files land in a
//   staging directory first, and the local manifest is written last of all - so an update
//   killed halfway leaves the previous data readable and the next run picks up where it
//   left off.
//
//   Every downloaded file is checked against the hash the manifest gives for it. A
//   truncated transfer that a proxy reported as success does not reach the cache.
//
//   A transient failure is retried with a backoff, and a permanent one is not. A 404 is
//   the server telling you the file is not there; a 503 is it telling you to come back.
//
// Reading is somebody else's job. This produces a directory, and the generated accessor
// reads it: `await Tables.ReadAllAsync(result.LocalPath)`.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if !UNITY_5_3_OR_NEWER
using System.Net;
using System.Net.Http;
#endif

namespace Tabbit.Binary
{
    /// <summary>
    /// What an update is allowed to do. Every value has a working default.
    /// </summary>
    public sealed class TabbitUpdateOptions
    {
        /// <summary>
        /// The manifest's file name at the base URL.
        ///
        /// The binary exporter writes `manifest-binary.json`; the JSON exporter writes
        /// `manifest-json.json`. Point this at whichever set is being served.
        /// </summary>
        public string ManifestFileName = "manifest-binary.json";

        /// <summary>
        /// How many times to try a request that failed for a reason worth retrying.
        ///
        /// Three, because a patch download runs on a phone on a train. The first attempt is
        /// included in the count, so this is two retries.
        /// </summary>
        public int MaxAttempts = 3;

        /// <summary>
        /// How long to wait before the second attempt. Doubled for each attempt after it, so
        /// the defaults space three attempts over 1.5 seconds.
        /// </summary>
        public TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

        /// <summary>How long one request may take before it counts as failed.</summary>
        public TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether a downloaded file is checked against the manifest's hash.
        ///
        /// On by default and there is no good reason to turn it off: the hash is already in
        /// the manifest, and MD5 over a few hundred kilobytes costs nothing next to the
        /// transfer that just happened.
        /// </summary>
        public bool VerifyHash = true;

        /// <summary>Where progress and outcomes go. Null for no logging.</summary>
        public Action<string> Log = null;
    }

    /// <summary>
    /// What an update did.
    /// </summary>
    public sealed class TabbitUpdateResult
    {
        /// <summary>Whether the local copy is now current.</summary>
        public bool Succeeded;

        /// <summary>Why it is not, when it is not. Null on success.</summary>
        public string Error;

        /// <summary>True when the remote manifest matched what was already here.</summary>
        public bool UpToDate;

        public int DownloadedCount;
        public long DownloadedBytes;
        public int DeletedCount;

        /// <summary>
        /// The directory holding the data. Hand it to the generated accessor's `ReadAllAsync`.
        ///
        /// Set even on failure, because the previous data is still there and still readable -
        /// which is the point of failing the way this does.
        /// </summary>
        public string LocalPath;
    }

    /// <summary>
    /// Downloads whatever the served data has that the local copy does not.
    /// </summary>
    public static class TabbitUpdater
    {
        /// <summary>
        /// Where data is cached when the caller does not say.
        ///
        /// Inside Unity this is under `persistentDataPath`, which is the one location that is
        /// writable on every platform and survives an app update. Elsewhere it sits beside the
        /// executable.
        /// </summary>
        public static string DefaultCacheDirectory
        {
#if UNITY_5_3_OR_NEWER
            get { return Path.Combine(UnityEngine.Application.persistentDataPath, "tabbit-data"); }
#else
            get { return Path.Combine(AppContext.BaseDirectory, "tabbit-data"); }
#endif
        }

        /// <summary>
        /// Brings `cacheDirectory` up to date with the data served under `baseUrl`.
        /// </summary>
        /// <param name="baseUrl">
        /// The directory the manifest and the data files sit in, as a URL. Trailing slash
        /// optional.
        /// </param>
        /// <param name="cacheDirectory">Where to keep the data. Null for the default.</param>
        /// <remarks>
        /// Does not throw. Everything that can go wrong here - the network, the disk, a file
        /// that arrived corrupt - is a condition the caller has to handle rather than a defect,
        /// and a patcher that throws into a game loop is a patcher that gets wrapped in a
        /// try/catch that swallows the reason.
        /// </remarks>
        public static async Task<TabbitUpdateResult> UpdateAsync(
            string baseUrl,
            string cacheDirectory = null,
            TabbitUpdateOptions options = null,
            CancellationToken cancellationToken = default)
        {
            options = options ?? new TabbitUpdateOptions();

            string cache = string.IsNullOrEmpty(cacheDirectory) ? DefaultCacheDirectory : cacheDirectory;
            var result = new TabbitUpdateResult { LocalPath = cache };

            try
            {
                string remoteText = Encoding.UTF8.GetString(
                    await DownloadAsync(Combine(baseUrl, options.ManifestFileName), options, cancellationToken));

                var remote = TabbitManifest.Parse(remoteText);
                var local = TabbitManifest.Read(Path.Combine(cache, options.ManifestFileName));

                var wanted = new List<TabbitManifest.Entry>();
                long wantedBytes = 0;

                foreach (var entry in remote.Entries)
                {
                    if (IsCurrent(local, entry, cache))
                        continue;

                    wanted.Add(entry);
                    wantedBytes += entry.Size;
                }

                var gone = new List<string>();

                foreach (var entry in local.Entries)
                {
                    if (!remote.Contains(entry.Name))
                        gone.Add(entry.Name);
                }

                if (wanted.Count == 0 && gone.Count == 0)
                {
                    Log(options, "[TabbitUpdater] Already up to date.");

                    result.Succeeded = true;
                    result.UpToDate = true;
                    return result;
                }

                Log(options, $"[TabbitUpdater] {wanted.Count} file(s) to fetch ({wantedBytes} bytes), " +
                             $"{gone.Count} to remove.");

                // Everything lands here first. Nothing the caller can read is touched until
                // the last file has arrived and been checked.
                string staging = Path.Combine(cache, ".staging");

                Directory.CreateDirectory(cache);
                ResetDirectory(staging);

                foreach (var entry in wanted)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] bytes = await DownloadAsync(Combine(baseUrl, entry.Name), options, cancellationToken);

                    if (options.VerifyHash && !string.IsNullOrEmpty(entry.Hash))
                    {
                        string actual = Md5(bytes);

                        if (!string.Equals(actual, entry.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new TabbitUpdateException(
                                $"'{entry.Name}' arrived with hash {actual}, and the manifest says " +
                                $"{entry.Hash}. Nothing was replaced.");
                        }
                    }

                    string staged = Path.Combine(staging, entry.Name);

                    Directory.CreateDirectory(Path.GetDirectoryName(staged));
                    WriteAllBytes(staged, bytes);

                    result.DownloadedBytes += bytes.Length;
                }

                // From here on the update is applied. Nothing below reaches the network, so
                // what is left is a sequence of local moves and then the manifest.
                foreach (var name in gone)
                {
                    string target = Path.Combine(cache, name);

                    if (File.Exists(target))
                        File.Delete(target);

                    result.DeletedCount++;
                }

                foreach (var entry in wanted)
                {
                    string staged = Path.Combine(staging, entry.Name);
                    string target = Path.Combine(cache, entry.Name);

                    Directory.CreateDirectory(Path.GetDirectoryName(target));

                    if (File.Exists(target))
                        File.Delete(target);

                    File.Move(staged, target);
                    result.DownloadedCount++;
                }

                // Last, and that ordering is the recovery story: a run killed before this
                // point leaves a manifest describing the data that is still on disk, so the
                // next run downloads the same files again rather than believing it has them.
                WriteAllBytes(Path.Combine(cache, options.ManifestFileName), Encoding.UTF8.GetBytes(remoteText));

                ResetDirectory(staging, andRemove: true);

                Log(options, $"[TabbitUpdater] Updated. {result.DownloadedCount} fetched, " +
                             $"{result.DeletedCount} removed, master hash {remote.MasterHash}.");

                result.Succeeded = true;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Error = "The update was cancelled.";
                return result;
            }
            catch (Exception e)
            {
                // The previous data is untouched, so the caller can carry on with it.
                Log(options, $"[TabbitUpdater] Update failed: {e.Message}");

                result.Error = e.Message;
                return result;
            }
        }

        /// <summary>
        /// Whether the local copy already has this entry, and has the file to go with it.
        /// </summary>
        /// <remarks>
        /// The file's presence is checked as well as the manifest's word for it: a cache
        /// somebody has cleaned out by hand would otherwise never be refilled.
        /// </remarks>
        private static bool IsCurrent(TabbitManifest local, TabbitManifest.Entry entry, string cache)
        {
            var previous = local.Find(entry.Name);

            if (previous == null || previous.Hash != entry.Hash)
                return false;

            return File.Exists(Path.Combine(cache, entry.Name));
        }

        // ------------------------------------------------------------------ transfer

        /// <summary>
        /// Fetches one URL, retrying what is worth retrying.
        /// </summary>
        private static async Task<byte[]> DownloadAsync(
            string url, TabbitUpdateOptions options, CancellationToken cancellationToken)
        {
            TimeSpan delay = options.RetryDelay;
            int attempts = options.MaxAttempts < 1 ? 1 : options.MaxAttempts;

            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await FetchAsync(url, options, cancellationToken);
                }
                catch (TabbitUpdateException e) when (e.IsTransient && attempt < attempts)
                {
                    Log(options, $"[TabbitUpdater] {e.Message} Retrying in {delay.TotalMilliseconds:F0}ms " +
                                 $"({attempt} of {attempts}).");

                    await Task.Delay(delay, cancellationToken);

                    // Doubling rather than a fixed wait: a server that is refusing because it
                    // is overloaded is not helped by every client coming back at the same
                    // interval.
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                }
            }
        }

#if UNITY_5_3_OR_NEWER
        /// <summary>
        /// One request, through UnityWebRequest.
        /// </summary>
        /// <remarks>
        /// Yielding rather than blocking. WebGL has one thread, so waiting on a handle would
        /// stop the frame the request needs in order to finish and nothing would ever
        /// complete; elsewhere it keeps the main thread moving.
        /// </remarks>
        private static async Task<byte[]> FetchAsync(
            string url, TabbitUpdateOptions options, CancellationToken cancellationToken)
        {
            using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                request.timeout = (int)options.RequestTimeout.TotalSeconds;

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    await Task.Yield();
                }

                bool failed = request.result != UnityEngine.Networking.UnityWebRequest.Result.Success;
                bool connection = request.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError;

                if (failed)
                {
                    // A connection that never arrived is worth another try; a status code is
                    // the server's answer, and only some of those mean "later".
                    bool transient = connection || IsTransientStatus((int)request.responseCode);

                    throw new TabbitUpdateException(
                        $"'{url}' could not be fetched: {request.error}.", transient);
                }

                return request.downloadHandler.data;
            }
        }
#else
        /// <summary>
        /// One HttpClient, shared. A client per request exhausts the socket pool under any
        /// real load, and this one is configured by the first caller's options.
        /// </summary>
        private static readonly HttpClient Http = new HttpClient();

        private static async Task<byte[]> FetchAsync(
            string url, TabbitUpdateOptions options, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(options.RequestTimeout);

                try
                {
                    using (var response = await Http.GetAsync(url, timeout.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new TabbitUpdateException(
                                $"'{url}' answered {(int)response.StatusCode} {response.ReasonPhrase}.",
                                IsTransientStatus((int)response.StatusCode));
                        }

                        return await response.Content.ReadAsByteArrayAsync();
                    }
                }
                catch (HttpRequestException e)
                {
                    // The request never got an answer - DNS, a refused connection, a dropped
                    // link. Worth another try.
                    throw new TabbitUpdateException($"'{url}' could not be reached: {e.Message}.", true);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TabbitUpdateException(
                        $"'{url}' did not answer within {options.RequestTimeout.TotalSeconds:F0}s.", true);
                }
            }
        }
#endif

        /// <summary>
        /// Whether a status code means "try again" rather than "no".
        /// </summary>
        /// <remarks>
        /// 408 and 429 are the server asking for another attempt, and 5xx is it failing on its
        /// own account. A 404 is not in here on purpose: during a deploy it can be transient,
        /// but retrying it costs a client three round trips to be told the same thing, and a
        /// manifest naming a file that is not there is a mistake to see rather than to wait
        /// out.
        /// </remarks>
        private static bool IsTransientStatus(int status)
            => status == 408 || status == 429 || (status >= 500 && status <= 599);

        // -------------------------------------------------------------------- local

        private static void WriteAllBytes(string filename, byte[] bytes)
            => File.WriteAllBytes(filename, bytes);

        /// <summary>Empties a directory, and creates it when it is not there at all.</summary>
        private static void ResetDirectory(string directory, bool andRemove = false)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            if (!andRemove)
                Directory.CreateDirectory(directory);
        }

        private static string Md5(byte[] bytes)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(bytes);
                var text = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                    text.Append(b.ToString("x2", CultureInfo.InvariantCulture));

                return text.ToString();
            }
        }

        /// <summary>
        /// Joins a base URL and a file name. Not Path.Combine, which on Windows produces a
        /// backslash and a URL that no server will answer.
        /// </summary>
        private static string Combine(string baseUrl, string name)
        {
            string trimmed = (baseUrl ?? string.Empty).TrimEnd('/');

            return trimmed + "/" + name.Replace("\\", "/");
        }

        private static void Log(TabbitUpdateOptions options, string message)
        {
            if (options.Log != null)
                options.Log(message);
        }
    }

    /// <summary>
    /// A failure during an update, and whether trying again might work.
    /// </summary>
    public sealed class TabbitUpdateException : Exception
    {
        public TabbitUpdateException(string message, bool isTransient = false)
            : base(message)
        {
            IsTransient = isTransient;
        }

        /// <summary>Whether the same request might succeed a moment later.</summary>
        public bool IsTransient { get; }
    }

    /// <summary>
    /// The manifest the exporter writes: one entry per file, with the hash to check it by.
    /// </summary>
    /// <remarks>
    /// Parsed by hand rather than with a JSON library, because the generated code is meant to
    /// compile with nothing installed and Unity ships no JSON reader that handles this shape.
    /// The manifest is written by this same project and its shape is fixed, so what is needed
    /// is a reader for that shape rather than a parser for the format.
    /// </remarks>
    public sealed class TabbitManifest
    {
        public sealed class Entry
        {
            public string Name;
            public long Size;
            public string Hash;
        }

        public string MasterHash = string.Empty;
        public readonly List<Entry> Entries = new List<Entry>();

        public Entry Find(string name)
        {
            foreach (var entry in Entries)
            {
                if (entry.Name == name)
                    return entry;
            }

            return null;
        }

        public bool Contains(string name) => Find(name) != null;

        /// <summary>
        /// Reads the cached manifest. A missing or unreadable one is an empty manifest, which
        /// makes the next update fetch everything - the safe direction to be wrong in.
        /// </summary>
        public static TabbitManifest Read(string filename)
        {
            try
            {
                return File.Exists(filename)
                    ? Parse(File.ReadAllText(filename))
                    : new TabbitManifest();
            }
            catch (Exception)
            {
                return new TabbitManifest();
            }
        }

        public static TabbitManifest Parse(string json)
        {
            var manifest = new TabbitManifest();

            if (string.IsNullOrEmpty(json))
                return manifest;

            manifest.MasterHash = ReadStringField(json, "MasterHash", 0) ?? string.Empty;

            int items = json.IndexOf("\"Items\"", StringComparison.Ordinal);

            if (items < 0)
                return manifest;

            // Every object after "Items" is one file. The manifest has no other array and no
            // nested objects, so counting braces from here is enough to walk them.
            int at = items;

            while (true)
            {
                int open = json.IndexOf('{', at);

                if (open < 0)
                    break;

                int close = json.IndexOf('}', open);

                if (close < 0)
                    break;

                string body = json.Substring(open, close - open + 1);
                string name = ReadStringField(body, "Name", 0);

                if (!string.IsNullOrEmpty(name))
                {
                    manifest.Entries.Add(new Entry
                    {
                        Name = name,
                        Hash = ReadStringField(body, "Hash", 0) ?? string.Empty,
                        Size = ReadLongField(body, "Size"),
                    });
                }

                at = close + 1;
            }

            return manifest;
        }

        /// <summary>The value of `"field": "..."`, or null when the field is not there.</summary>
        private static string ReadStringField(string json, string field, int from)
        {
            int at = ValueStart(json, field, from);

            if (at < 0 || at >= json.Length || json[at] != '"')
                return null;

            var value = new StringBuilder();

            for (int i = at + 1; i < json.Length; i++)
            {
                char c = json[i];

                if (c == '\\' && i + 1 < json.Length)
                {
                    char escaped = json[++i];

                    switch (escaped)
                    {
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'u':
                            if (i + 4 < json.Length)
                            {
                                value.Append((char)Convert.ToInt32(json.Substring(i + 1, 4), 16));
                                i += 4;
                            }
                            break;
                        default: value.Append(escaped); break;
                    }

                    continue;
                }

                if (c == '"')
                    return value.ToString();

                value.Append(c);
            }

            return null;
        }

        /// <summary>The value of `"field": 123`, or zero when it is not there.</summary>
        private static long ReadLongField(string json, string field)
        {
            int at = ValueStart(json, field, 0);

            if (at < 0)
                return 0;

            int end = at;

            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;

            long.TryParse(json.Substring(at, end - at), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long value);

            return value;
        }

        /// <summary>Where a field's value begins, past the name, the colon and any spaces.</summary>
        private static int ValueStart(string json, string field, int from)
        {
            int key = json.IndexOf("\"" + field + "\"", from, StringComparison.Ordinal);

            if (key < 0)
                return -1;

            int colon = json.IndexOf(':', key);

            if (colon < 0)
                return -1;

            int at = colon + 1;

            while (at < json.Length && char.IsWhiteSpace(json[at]))
                at++;

            return at;
        }
    }
}
