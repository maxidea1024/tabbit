using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using System.Linq;

namespace Tabbit.Helpers;

/// <summary>
/// If we write the file right away, if an error occurs in the state that it is not finally completed, potential problems may occur,
/// It is used to move to the actual file only when it is finally completed.
/// </summary>
public static class StagingFiles
{
    static readonly List<(string, string)> _stagingFiles = [];

    /// <summary>
    /// One lock over everything this class remembers.
    /// </summary>
    /// <remarks>
    /// **Because the output entries are built at the same time.** Every list here is appended
    /// to from inside a target's own work - which staging files exist, which directories to
    /// sweep, which files to prune - and a List that two threads append to loses entries
    /// rather than failing.
    ///
    /// One lock rather than one per list: what these hold is small, the appends are short, and
    /// several locks over state this interrelated is how a deadlock gets written. Nothing is
    /// held across file I/O - see <see cref="Write"/>. spec/ops/conversion-time.md section 5.
    /// </remarks>
    static readonly object Gate = new object();

    /// <summary>
    /// Which recipe entry the work on this thread belongs to, or null outside one.
    /// </summary>
    /// <remarks>
    /// **How a file staged now is attributed to the entry that staged it.** The build cache
    /// records what each entry produced, and it used to work that out from the difference in
    /// the staging list across the entry's run - a slice of a shared list, which is an answer
    /// only while one entry runs at a time.
    ///
    /// The alternative that was rejected then is still rejected: asking every target to keep
    /// its own list would put the cache's bookkeeping into all of them. This puts it in
    /// neither place - the caller names the entry, and the ledger tags what arrives.
    ///
    /// `AsyncLocal` rather than `ThreadLocal` because a target may fan out internally - the
    /// binary exporter encodes a table's columns in parallel - and an async-local value flows
    /// into that work where a thread-local one would not.
    /// </remarks>
    static readonly System.Threading.AsyncLocal<string?> _attributedTo = new System.Threading.AsyncLocal<string?>();

    /// <summary>Files each entry staged, in the order it staged them.</summary>
    static readonly Dictionary<string, List<string>> _byEntry =
        new Dictionary<string, List<string>>(StringComparer.Ordinal);

    /// <summary>
    /// Names the entry that the staging done inside the returned scope belongs to.
    /// </summary>
    /// <remarks>
    /// Restores whatever was in force before, so a nested scope - a target that runs another
    /// one - leaves the outer attribution as it found it.
    /// </remarks>
    public static IDisposable Attributing(string section) => new Attribution(section);

    private sealed class Attribution : IDisposable
    {
        private readonly string? _previous;

        public Attribution(string section)
        {
            _previous = _attributedTo.Value;
            _attributedTo.Value = section;
        }

        public void Dispose() => _attributedTo.Value = _previous;
    }

    /// <summary>
    /// The destination paths one entry staged, in the order it staged them.
    /// </summary>
    /// <remarks>
    /// Read after the entries have run, so what comes back does not depend on the order they
    /// finished in - a target stages its own files in table order, and this keeps that order.
    /// </remarks>
    public static IReadOnlyList<string> StagedBy(string section)
    {
        lock (Gate)
        {
            return _byEntry.TryGetValue(section, out var staged)
                ? staged.ToList()
                : (IReadOnlyList<string>)Array.Empty<string>();
        }
    }

    /// <summary>Which staging paths are already in <see cref="_stagingFiles"/>.</summary>
    /// <remarks>
    /// The same membership the list carries, kept as a set so that asking is not a scan.
    /// Ordinal, because the path has already been folded to the case this filesystem
    /// compares in - see <see cref="RegisterStagingFile(string, out bool)"/>.
    /// </remarks>
    static readonly HashSet<string> _stagedPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// What each staged file holds, measured as it was written.
    /// </summary>
    /// <remarks>
    /// So that nothing has to read a file back to find out what is in it. The manifest
    /// records a size and an MD5 per file, and it was getting them by opening the file it
    /// had just been handed - which read every byte of the output a second time. On the
    /// sample project's full conversion that was 4.59 s for the `json` target and 2.37 s
    /// for `binary`, spent re-reading bytes that had been in hand a moment earlier.
    ///
    /// Keyed by the staging path, because that is what a target passes on. Cleared with the
    /// staging list itself: past a commit or a rollback these describe files that are no
    /// longer there.
    ///
    /// Concurrent, and not under <see cref="Gate"/>: this is written at the end of a file
    /// write, and holding a lock across the writing of a hundred-megabyte table would
    /// serialise exactly the work the entries are being run in parallel to spread.
    /// </remarks>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Md5, long Size)> _writtenContents =
        new System.Collections.Concurrent.ConcurrentDictionary<string, (string, long)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Directories a target asked to have its stale output removed from.
    /// </summary>
    static readonly List<string> _sweepRoots = [];

    /// <summary>
    /// The same directories, kept for the whole run rather than until the sweep.
    /// </summary>
    /// <remarks>
    /// The build cache records these, because a run that decides it has nothing to do never
    /// reaches a target and so is never told where the output lives - and it still has to
    /// sweep. Removing a generated file that is no longer produced is part of what a
    /// successful run leaves behind, and "unless it was quick" is not a condition anybody
    /// would want on that.
    /// </remarks>
    static readonly List<string> _declaredSweepRoots = [];

    /// <summary>Every directory this run was asked to sweep, whether or not it has yet.</summary>
    /// <remarks>
    /// Sorted, because these reach the build cache's seal - a file on disk - and the order
    /// they were declared in is now the order the output entries happened to finish in. What
    /// they are is a set of directories, so putting them in one order costs nothing and keeps
    /// two identical runs writing an identical seal. spec/ops/conversion-time.md section 5.
    /// </remarks>
    public static IReadOnlyList<string> DeclaredSweepRoots
    {
        get
        {
            lock (Gate)
            {
                var roots = new List<string>(_declaredSweepRoots);
                roots.Sort(StringComparer.Ordinal);
                return roots;
            }
        }
    }

    /// <summary>
    /// Files a target named individually as its own, to be removed if this run does not
    /// write them.
    /// </summary>
    static readonly List<string> _pruneCandidates = [];

    /// <summary>
    /// Files a previous run wrote that this run is keeping instead of writing again.
    /// </summary>
    /// <remarks>
    /// This exists because of what <see cref="Sweep"/> does. The sweep deletes generated
    /// files that this run did not write, which is right when a table was renamed and
    /// catastrophic when an output entry was skipped: its thousands of files are, by that
    /// definition, files this run did not write.
    ///
    /// So a skipped entry declares its previous output here, and the sweep counts it as
    /// written. The list comes from the build cache's record of what that entry produced
    /// last time, which is the only place that knows.
    /// </remarks>
    static readonly List<string> _keptFiles = [];


    /// <summary>
    /// Says that a file a previous run wrote is still this run's output, though this run
    /// did not write it.
    /// </summary>
    public static void Keep(string filename)
    {
        string full = Path.GetFullPath(filename);

        lock (Gate)
        {
            if (!_keptFiles.Contains(full, PathNames.Comparer))
                _keptFiles.Add(full);
        }
    }

    /// <summary>
    /// What every generated file says about itself, in its first few lines.
    ///
    /// Matched case-insensitively because the targets differ - C# shouts it inside an
    /// `auto-generated` block, the rest do not.
    /// </summary>
    /// <summary>How far into a file to look for it. Every target writes it as a header.</summary>
    const int MarkerWindow = GeneratedFileMarker.Window;

    /// <summary>
    /// Deletes the staging files when an error occurs.
    /// </summary>
    public static void Rollback()
    {
        // Delete all junky artifact files.
        try
        {
            foreach (var kv in _stagingFiles)
                File.Delete(kv.Item2);
        }
        catch
        {
            // Sink all exceptions. (don't worry)
        }

        _stagingFiles.Clear();
        _stagedPaths.Clear();
        _writtenContents.Clear();
        _byEntry.Clear();

        // Nothing is swept either: a run that failed has no business deciding which of
        // the previous run's files are stale.
        _sweepRoots.Clear();
        _declaredSweepRoots.Clear();
        _pruneCandidates.Clear();
        _keptFiles.Clear();
    }

    /// <summary>
    /// Asks for stale generated files under <paramref name="directory"/> to be removed
    /// once this run commits.
    /// </summary>
    /// <remarks>
    /// Needed because the output is a file per table. Delete a table from the sheets and
    /// its file simply stays behind: it still names types nothing declares any more, so at
    /// best the output is untidy and at worst it does not compile. A single-file target
    /// never had the problem, which is why nothing noticed it in the one target that has
    /// always written per-table files.
    ///
    /// What is removed is not "everything not written". It is every file that says
    /// `Generated by Tabbit` in its own header and that this run did not write. So a
    /// target pointed at a directory holding somebody's own source cannot delete any of
    /// it - the marker is the permission, and only this tool writes it.
    /// </remarks>
    public static void SweepDirectory(string directory)
    {
        string full = Path.GetFullPath(directory);

        lock (Gate)
        {
            if (!_sweepRoots.Contains(full, PathNames.Comparer))
                _sweepRoots.Add(full);

            if (!_declaredSweepRoots.Contains(full, PathNames.Comparer))
                _declaredSweepRoots.Add(full);
        }
    }

    /// <summary>
    /// Names a file this tool wrote on a previous run, to be removed if this run does not
    /// write it again.
    /// </summary>
    /// <remarks>
    /// For the exporters, whose output is data rather than source and so carries no header
    /// to recognize it by. What stands in for the header is the manifest: it is this tool's
    /// own ledger of what it put in that directory, so a file named from it is one we wrote
    /// and a file absent from it is untouchable however it got there. That is a stronger
    /// guarantee than the marker gives the code generators - a marker travels with a file
    /// somebody copies, and a ledger entry does not.
    ///
    /// Which leaves one gap, worth naming: a file whose manifest entry is already gone -
    /// because a rename rewrote the ledger before anything pruned - is not in the ledger
    /// and so is never removed. Those are cleaned by hand once; every orphan made after
    /// this exists is caught by the run that orphans it.
    /// </remarks>
    public static void RegisterPruneCandidate(string filename)
    {
        string full = Path.GetFullPath(filename);

        lock (Gate)
        {
            if (!_pruneCandidates.Contains(full, PathNames.Comparer))
                _pruneCandidates.Add(full);
        }
    }

    /// <summary>
    /// The staging file one destination is written through.
    /// </summary>
    /// <remarks>
    /// A pure function of the path, so the two ways of claiming a file - one at a time and
    /// a set at once - cannot disagree about where it goes.
    ///
    /// Hashed the way this platform compares paths, so that one staging file stands for one
    /// real file. Hashing the spelling was right on Linux and wrong on Windows: two targets
    /// asking for `Item.cs` and `item.cs` got two staging files there, which is one file on
    /// NTFS - so the collision check never fired and whichever committed last was silently
    /// the only one that survived. That is the case the check exists for, and it was the case
    /// it could not see.
    /// </remarks>
    private static string StagingPathOf(string fullPath)
    {
        string md5 = Helper.CalculateMD5HashFromString(
            PathNames.Comparison == StringComparison.OrdinalIgnoreCase
                ? fullPath.ToLowerInvariant()
                : fullPath);

        return Path.Combine(Path.GetTempPath(), md5 + ".staging");
    }

    /// <summary>
    /// Claims a whole set of destinations at once, or claims none of them.
    /// </summary>
    /// <remarks>
    /// **For a target that plans its files and then writes them in parallel.** Two things
    /// have to be true before such a target can start, and neither can be checked one file
    /// at a time: that no two of its own files are the same file, and that nothing else has
    /// already claimed one of them. Both are answered here, under one lock, and either
    /// answer being no leaves the ledger exactly as it was.
    ///
    /// Null is not a failure. It means the caller has to take its sequential path, where the
    /// rule about a destination claimed twice lives - identical text is allowed through, and
    /// that comparison needs a file to compare against. spec/ops/conversion-time.md section 5.
    /// </remarks>
    /// <returns>
    /// The staging file for each destination, in the order given, or null when one of them
    /// cannot be claimed.
    /// </returns>
    public static IReadOnlyList<string>? ClaimAll(IReadOnlyList<string> destinations)
    {
        var full = new string[destinations.Count];
        var staged = new string[destinations.Count];

        for (int at = 0; at < destinations.Count; at++)
        {
            full[at] = Path.GetFullPath(destinations[at]);
            staged[at] = StagingPathOf(full[at]);
        }

        lock (Gate)
        {
            // Checked over the whole set first. A partial claim would leave the ledger
            // holding files the sequential path is about to claim again.
            var wanted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in staged)
            {
                if (!wanted.Add(path) || _stagedPaths.Contains(path))
                    return null;
            }

            string? entry = _attributedTo.Value;

            for (int at = 0; at < staged.Length; at++)
            {
                _stagedPaths.Add(staged[at]);
                _stagingFiles.Add((full[at], staged[at]));

                if (entry is not null)
                {
                    if (!_byEntry.TryGetValue(entry, out var claimed))
                    {
                        claimed = [];
                        _byEntry[entry] = claimed;
                    }

                    claimed.Add(full[at]);
                }
            }
        }

        return staged;
    }

    /// <summary>
    /// Add one staged file and return the staged file name.
    /// </summary>
    public static string RegisterStagingFile(string filename)
        => RegisterStagingFile(filename, out _);

    /// <param name="alreadyStaged">
    /// True when this run has staged this path before, so the caller is about to overwrite
    /// something it wrote itself.
    /// </param>
    public static string RegisterStagingFile(string filename, out bool alreadyStaged)
    {
        string fullPath = Path.GetFullPath(filename);
        string stagingFilename = StagingPathOf(fullPath);

        string? entry = _attributedTo.Value;

        lock (Gate)
        {
            // Asked of a set rather than by scanning the list. Every file a target writes
            // comes through here, so a scan makes staging quadratic in the number of files -
            // and a conversion of five hundred tables into seven targets stages thousands.
            alreadyStaged = !_stagedPaths.Add(stagingFilename);

            if (alreadyStaged)
                return stagingFilename;

            _stagingFiles.Add((fullPath, stagingFilename));

            // Tagged with whichever entry is being built, so the cache can be told what each
            // one produced without any target having to keep a list.
            if (entry is not null)
            {
                if (!_byEntry.TryGetValue(entry, out var staged))
                {
                    staged = [];
                    _byEntry[entry] = staged;
                }

                staged.Add(fullPath);
            }
        }

        return stagingFilename;
    }

    /// <summary>
    /// Commits all staging files to the original files.
    /// </summary>
    public static void CommitFiles(Action<string, string>? progressCallback = null)
    {
        // Taken before the loop below drains the list, because the sweep needs to know
        // everything this run wrote and the loop forgets each entry as it goes.
        //
        // Compared the way this platform's filesystem compares paths. Case-insensitively
        // everywhere - which is what this said - makes a Linux sweep skip a stale `item.cs`
        // because the run wrote `Item.cs`, and those are two files there.
        var written = new HashSet<string>(
            _stagingFiles.Select(kv => kv.Item1), PathNames.Comparer);

        // The output of an entry this run skipped counts as written. Without this the
        // sweep, whose whole rule is "delete what this run did not write", deletes exactly
        // the files the cache decided were already correct.
        written.UnionWith(_keptFiles);

        // Walked forwards, one at a time, and the committed ones dropped in one go at the end.
        //
        // **One at a time is measured rather than assumed.** A commit is thousands of renames
        // within one volume, so it looks like latency against the filesystem - the shape that
        // parallelises. It is not: NTFS serialises on the directory metadata these all share,
        // and moving them across every core took this run's commit from 6.2 s to 9.5 s. The
        // sequential loop is the fast one. spec/ops/conversion-time.md section 7.
        //
        // The list used to be drained with RemoveAt(0) per file, which moves every remaining
        // entry down one - quadratic in the number of files, and this run commits thousands.
        // What that per-file removal bought is the invariant the finally block below keeps:
        // a commit that fails part-way leaves in the list exactly the files that have not
        // moved yet, so a rollback deletes their staging copies and nothing that is already
        // at its destination.
        int committed = 0;

        try
        {
            foreach (var kv in _stagingFiles)
            {
                // Progress
                progressCallback?.Invoke(kv.Item1, kv.Item2);

                FileHelper.EnsurePathExists(kv.Item1);

                // One call rather than a delete and a move. The overload replaces the
                // destination itself, which is the same outcome with one fewer round trip to
                // the filesystem per file - and the delete that used to follow the move was
                // removing a file the move had already taken away.
                File.Move(kv.Item2, kv.Item1, overwrite: true);

                committed++;
            }
        }
        finally
        {
            _stagingFiles.RemoveRange(0, committed);
        }

        // These describe files that are no longer in staging.
        _stagedPaths.Clear();
        _writtenContents.Clear();

        Sweep(written);
    }

    /// <summary>
    /// Removes the generated files this run did not write.
    ///
    /// After the commit rather than before it, so a run that fails part-way through leaves
    /// the previous output alone - and so a file that is about to be rewritten is never
    /// briefly absent.
    /// </summary>
    /// <returns>The files removed, for a caller that wants to report them.</returns>
    public static IReadOnlyList<string> Sweep(ISet<string> written)
    {
        var removed = new List<string>();

        // The files a target named from its own ledger. No marker is consulted: being in
        // the ledger is what says the file is ours, and nothing else in the directory can
        // be reached from here at all.
        foreach (var path in _pruneCandidates)
        {
            if (written.Contains(path) || !File.Exists(path))
                continue;

            try
            {
                File.Delete(path);
                removed.Add(path);
            }
            catch (IOException)
            {
                // Something has it open. Leaving a stale file behind is untidy; failing
                // a conversion that has already written everything correctly is worse.
            }
        }

        _pruneCandidates.Clear();

        foreach (var root in _sweepRoots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (written.Contains(Path.GetFullPath(path)))
                    continue;

                if (!IsGenerated(path))
                    continue;

                try
                {
                    File.Delete(path);
                    removed.Add(path);
                }
                catch (IOException)
                {
                    // Something has it open. Leaving a stale file behind is untidy; failing
                    // a conversion that has already written everything correctly is worse.
                }
            }
        }

        _sweepRoots.Clear();
        _keptFiles.Clear();

        return removed;
    }

    /// <summary>
    /// Whether a file says, in its own header, that this tool wrote it.
    ///
    /// Read as bytes and matched as ASCII, so an unreadable or binary file simply does not
    /// match rather than throwing. Only the head is examined: every target writes the
    /// marker as a header, and a file that merely mentions the phrase somewhere in the
    /// middle is somebody's own source.
    /// </summary>
    static bool IsGenerated(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            var head = new byte[MarkerWindow];
            int read = stream.Read(head, 0, head.Length);

            if (read <= 0)
                return false;

            return GeneratedFileMarker.IsMarked(System.Text.Encoding.UTF8.GetString(head, 0, read));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a new file, write the contents to the file, and then closes the file.
    /// If the target file already exists, it is overwritten.
    /// </summary>
    /// <remarks>
    /// Writing two different texts to one path is an error rather than the second winning.
    ///
    /// It became possible when the targets started writing a file per table: a file name now
    /// comes from a table, an enum or a constant set name, and two of those can land on the
    /// same one. `Item` the table and `Item` the enum both want item.rs; so do `ItemType` and
    /// `Item_Type` in any target that snake-cases. The old behaviour was that whichever ran
    /// last was the file, and the other type simply was not in the output - which shows up as
    /// a compile error in the consumer's project naming a type this tool said it generated,
    /// with nothing anywhere saying why.
    ///
    /// Identical text is allowed through, because a target legitimately re-writes a file it
    /// has already written - the reader runtime when two targets share an output directory,
    /// for one.
    /// </remarks>
    public static string WriteAllTextToFile(string filename, string text)
        => WriteAllTextToFile(filename, text, withByteOrderMark: false);

    /// <summary>
    /// The same, choosing whether the UTF-8 text is written with a byte order mark.
    /// </summary>
    /// <remarks>
    /// A BOM is the wrong default - most toolchains read UTF-8 without being told, and a
    /// mark at the head of the file is one more thing for a diff to show. MSVC is the
    /// exception that makes it necessary for C, C++ and Unreal: with no BOM it reads a
    /// source file in the system codepage, and on a Korean or Japanese Windows a comment
    /// carried over from the sheet then ends in a byte that is a backslash in that
    /// codepage - which continues the comment onto the next line and swallows the
    /// declaration under it. The compiler reports a syntax error on a line that is
    /// correct. `/utf-8` also fixes it, but generated code should compile without the
    /// consumer having to find that out.
    /// </remarks>
    public static string WriteAllTextToFile(string filename, string text, bool withByteOrderMark)
        => WriteText(filename, text, withByteOrderMark, trailingNewline: false);

    /// <summary>
    /// The same, optionally ending the file with exactly one newline.
    /// </summary>
    /// <remarks>
    /// The newline is appended as a byte after the text rather than by building a second
    /// copy of the string to carry it. That is not a micro-optimization at this size: a
    /// table's JSON reaches hundreds of megabytes, and `text + "\n"` copies all of it.
    /// spec/ops/conversion-time.md section 4.
    /// </remarks>
    private static string WriteText(
        string filename, string text, bool withByteOrderMark, bool trailingNewline)
    {
        string stagingFilename = RegisterStagingFile(filename, out bool alreadyStaged);

        // ReadAllText strips a BOM, so this compares the text either way. The newline is put
        // back for the comparison, because what is on disk is what this wrote.
        if (alreadyStaged
            && File.ReadAllText(stagingFilename) != (trailingNewline ? text + "\n" : text))
        {
                throw new TabbitException(null,
                    Messages.Message.Of(Exporters.ExportMessages.GeneratedFileNameClash,
                        ("Path", Path.GetFullPath(filename))));
        }

        // Encoded here rather than left to File.WriteAllText, so the bytes that go to disk
        // are the bytes that get measured. A StreamWriter emits the encoding's preamble at
        // position zero, so preamble-then-body is exactly what the file holds either way.
        var encoding = withByteOrderMark ? Utf8WithBom : Utf8WithoutBom;

        Write(
            stagingFilename, encoding.GetPreamble(), encoding.GetBytes(text),
            trailingNewline ? Newline : ReadOnlySpan<byte>.Empty);

        return stagingFilename;
    }

    /// <summary>One LF, for the files that end with exactly one.</summary>
    private static readonly byte[] Newline = [(byte)'\n'];

    private static readonly System.Text.UTF8Encoding Utf8WithBom = new System.Text.UTF8Encoding(true);
    private static readonly System.Text.UTF8Encoding Utf8WithoutBom = new System.Text.UTF8Encoding(false);

    /// <summary>
    /// Creates a new file, writes the specified bytes to the file, and then closes the
    /// file. If the target file already exists, it is overwritten.
    /// </summary>
    /// <remarks>
    /// Takes a span rather than an array so a caller can hand over a view of a buffer
    /// it already has. A table's bytes are the largest allocation the export makes,
    /// and copying them to pass them here would double it.
    /// </remarks>
    public static string WriteAllBytesToFile(string filename, ReadOnlySpan<byte> data)
    {
        string stagingFilename = RegisterStagingFile(filename);

        Write(stagingFilename, ReadOnlySpan<byte>.Empty, data);

        return stagingFilename;
    }

    /// <summary>
    /// Writes a staged file and records its size and MD5 as it goes.
    /// </summary>
    /// <remarks>
    /// The one place a staging file's bytes are written, so that the measurement cannot
    /// drift from the content. The hash is taken over the same spans that reach the stream,
    /// which is what makes it the file's hash rather than an approximation of it - and it
    /// costs one pass over bytes already in memory instead of a second read from disk.
    ///
    /// The digest is MD5 because that is what <see cref="Manifest"/> records and what the
    /// committed manifests hold; the reasoning for that choice is written where the
    /// manifest states it. Nothing here is a defence against a chosen-prefix attack.
    /// </remarks>
    private static void Write(
        string stagingFilename, ReadOnlySpan<byte> preamble, ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> tail = default)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        using (var stream = new FileStream(
            stagingFilename, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024))
        {
            // Written out one part at a time rather than joined first. Joining would copy the
            // body, which is the largest thing this class handles.
            if (!preamble.IsEmpty)
            {
                stream.Write(preamble);
                digest.AppendData(preamble);
            }

            stream.Write(body);
            digest.AppendData(body);

            if (!tail.IsEmpty)
            {
                stream.Write(tail);
                digest.AppendData(tail);
            }
        }

        _writtenContents[stagingFilename] = (
            Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
            preamble.Length + (long)body.Length + tail.Length);
    }

    /// <summary>
    /// What a staged file holds, when this run is the one that wrote it.
    /// </summary>
    /// <remarks>
    /// False for a file staged some other way - a copy, or a target reaching past this
    /// class - and the caller then measures it itself. Answering "I do not know" rather
    /// than guessing is the whole point: a wrong hash in the manifest is a file that never
    /// gets copied again.
    /// </remarks>
    public static bool TryWrittenContents(string stagingFilename, out string md5, out long size)
    {
        if (_writtenContents.TryGetValue(stagingFilename, out var written))
        {
            md5 = written.Md5;
            size = written.Size;
            return true;
        }

        md5 = "";
        size = 0;
        return false;
    }

    /// <summary>
    /// Creates a new file, writes the specified object to the .json file, and then closes the file.
    /// If the target file already exists, it is overwritten.
    /// </summary>
    /// <remarks>
    /// Line endings are LF and the file ends with exactly one newline, which is what the
    /// generated source files do and what makes a file's last line a line.
    ///
    /// Newtonsoft writes the platform's line ending when indenting, so a JSON export from
    /// Windows differed from the same export on Linux - the golden trees are recorded on
    /// one of those and compared on the other in CI. And it wrote no trailing newline at
    /// all, so a .json export was the one kind of file this tool produced that git, a
    /// diff and every editor would complain about.
    /// </remarks>
    public static string WriteToJsonFile(string filename, object obj, bool indented = true)
        => WriteText(filename, Rendered(obj, indented), withByteOrderMark: false, trailingNewline: true);

    /// <summary>
    /// Writes an object as JSON into a staging file that <see cref="RegisterStagingFile(string)"/>
    /// has already handed out.
    /// </summary>
    /// <remarks>
    /// **For a target that claims its files in order and then writes them at once.** The
    /// order the staging list is in reaches the build cache's seal, and the order a target's
    /// manifest entries are in is the manifest file itself - so a target that writes its
    /// tables in parallel claims them sequentially first and fills the manifest in
    /// afterwards. What is left to do in parallel is this.
    ///
    /// The one thing this does not do is the check <see cref="WriteText"/> makes when a
    /// destination has been claimed twice. That rule - identical text is allowed through -
    /// belongs to the sequential path, and a target whose plan finds two of its tables
    /// wanting one file takes that path instead. spec/ops/conversion-time.md section 5.
    /// </remarks>
    public static void WriteJsonInto(string stagingFilename, object obj, bool indented)
    {
        var body = Utf8WithoutBom.GetBytes(Rendered(obj, indented));

        Write(stagingFilename, ReadOnlySpan<byte>.Empty, body, Newline);
    }

    /// <summary>The same, for a target whose output is bytes rather than text.</summary>
    public static void WriteBytesInto(string stagingFilename, ReadOnlySpan<byte> data)
        => Write(stagingFilename, ReadOnlySpan<byte>.Empty, data);

    /// <summary>
    /// An object as the JSON that goes to disk: LF line endings, and no trailing newline -
    /// the writer adds the one the file ends with.
    /// </summary>
    private static string Rendered(object obj, bool indented)
    {
        string json = JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None);

        // Asked before it is done. Replace copies the whole string, and the unindented form -
        // which is what a data export uses - holds no line ending at all, so the copy was of
        // hundreds of megabytes in order to change nothing. spec/ops/conversion-time.md section 4.
        if (json.Contains('\r'))
            json = json.Replace("\r\n", "\n");

        // TrimEnd returns the same string when there is nothing to trim, which is the usual
        // case.
        return json.TrimEnd('\n');
    }



    //TODO

    /*

    /// <summary>Copy directory recursively</summary>
    public static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(target.FullName);

        // Copy each file into the new directory.
        foreach (FileInfo fi in source.GetFiles())
        {
            //Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
            fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
        }

        // Copy each subdirectory using recursion.
        foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
        {
            DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
            CopyDirectory(diSourceSubDir, nextTargetSubDir);
        }
    }
    */
}
