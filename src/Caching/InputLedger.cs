using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tabbit.Helpers;

namespace Tabbit.Caching;

/// <summary>
/// What this run read, collected while it reads it.
/// </summary>
/// <remarks>
/// Recorded by whoever does the reading rather than worked out afterwards. A source knows
/// which files it opened and which directory it listed; reconstructing that from the outside
/// would mean a second implementation of every source's rules, and the two would disagree
/// exactly where it matters - about the file that was added.
///
/// The sizes, times and hashes are taken at the end rather than as each file is recorded.
/// A conversion can read one workbook twice, and hashing a gigabyte again for the second
/// mention would be paid on every full run for nothing.
/// </remarks>
public sealed class InputLedger
{
    private readonly List<string> _files = [];
    private readonly List<SealedListing> _listings = [];
    private readonly List<SealedRemote> _remotes = [];

    /// <summary>Notes a file this run read.</summary>
    public void Read(string path)
    {
        string full = Path.GetFullPath(path);

        if (!_files.Contains(full, PathNames.Comparer))
            _files.Add(full);
    }

    /// <summary>
    /// Notes which files were under a directory when this run looked.
    /// </summary>
    /// <param name="root">The directory that was searched.</param>
    /// <param name="extensions">Extensions it was searched with, as the recipe wrote them.</param>
    /// <param name="names">
    /// The names found, in the order the search produced them - which is fixed, because
    /// <see cref="SourceFiles"/> orders them.
    /// </param>
    public void Listed(string root, string extensions, IEnumerable<string> names)
    {
        string full = Path.GetFullPath(root);
        string hash = ContentHash.OfNames(names, out int count);

        var existing = _listings.Find(
            listing => PathNames.Comparer.Equals(listing.Root, full) && listing.Extensions == extensions);

        if (existing is not null)
        {
            existing.Hash = hash;
            existing.Count = count;
            return;
        }

        _listings.Add(new SealedListing
        {
            Root = full,
            Extensions = extensions,
            Hash = hash,
            Count = count,
        });
    }

    /// <summary>
    /// Notes the version of an input this tool cannot open - a hosted document.
    /// </summary>
    public void Remote(string source, string id, string version)
    {
        var existing = _remotes.Find(remote => remote.Source == source && remote.Id == id);

        if (existing is not null)
        {
            existing.Version = version;
            return;
        }

        _remotes.Add(new SealedRemote { Source = source, Id = id, Version = version });
    }

    /// <summary>Whether anything was recorded at all.</summary>
    public bool IsEmpty => _files.Count == 0 && _listings.Count == 0 && _remotes.Count == 0;

    /// <summary>
    /// Measures every recorded file, for the seal.
    /// </summary>
    /// <remarks>
    /// A file that has since disappeared is dropped rather than recorded as absent. It was
    /// read, so it existed; if it is gone by the time the run ends, the state being recorded
    /// is not the state the run read and the next run should decide for itself.
    /// </remarks>
    public List<SealedFile> Files()
    {
        var measured = new List<SealedFile>(_files.Count);

        foreach (var path in _files)
        {
            var info = new FileInfo(path);

            if (!info.Exists)
                continue;

            measured.Add(new SealedFile
            {
                Path = path,
                Size = info.Length,
                ModifiedTicks = info.LastWriteTimeUtc.Ticks,
                Hash = ContentHash.OfFile(path),
            });
        }

        return measured;
    }

    /// <summary>The listings, as recorded.</summary>
    public List<SealedListing> Listings() => [.. _listings];

    /// <summary>The remote versions, as recorded.</summary>
    public List<SealedRemote> Remotes() => [.. _remotes];
}
