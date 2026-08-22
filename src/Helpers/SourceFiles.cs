using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tabbit.Helpers;

/// <summary>
/// Which files under a directory are candidates to be read as workbooks.
/// </summary>
/// <remarks>
/// Here rather than inside the importer because two callers have to agree on the answer.
/// The importer reads these files; the build cache records which files were there and, on
/// the next run, asks the same question again to find out whether any were added or
/// removed. If the two rules drift, the cache is comparing a list of one thing against a
/// list of another - and the failure is a workbook that was added and not converted, with
/// nothing in the run saying so.
///
/// What this decides is only what is a candidate. Which candidates a recipe actually wants
/// is <see cref="Sources.SheetFilter"/>'s business, and it stays there: the cache has to
/// notice a workbook appearing whether or not the recipe currently excludes it, because
/// removing it from the exclude list is a change to the recipe and that is noticed
/// separately.
/// </remarks>
public static class SourceFiles
{
    /// <summary>
    /// The candidate files under <paramref name="root"/>, in a fixed order.
    /// </summary>
    /// <param name="root">Directory to search, including subdirectories.</param>
    /// <param name="extensions">
    /// Extensions to take, each with its leading dot and in lower case. Empty takes every
    /// file, which is what a scan of an asset tree wants.
    /// </param>
    /// <param name="onLockFile">
    /// Called for a workbook somebody has open in Excel. The importer says so, because a
    /// file it declined to read is worth a line; the cache does not, because there it is
    /// not an event.
    /// </param>
    /// <returns>Absolute path and the name relative to <paramref name="root"/>, ordered.</returns>
    public static IEnumerable<(string Path, string Name)> Candidates(
        string root,
        IReadOnlyCollection<string> extensions,
        Action<string>? onLockFile = null)
    {
        // Ordered, because this is the order the tables enter the model in and so the order
        // they leave in. The filesystem's own order is not the same on ext4 as on NTFS,
        // which made the same directory of workbooks produce different output on Linux than
        // on Windows - silently, since both are valid outputs of a run that read everything.
        var files = PathNames.InOrder(
            Directory.GetFiles(root, "*.*", SearchOption.AllDirectories));

        foreach (var filename in files)
        {
            // A directory or file whose name starts with `#` is switched off by whoever
            // named it that way.
            if (filename.Contains("/#") || filename.Contains("\\#"))
                continue;

            // Excel's lock file for a workbook somebody has open: `~$Book.xlsx`, same
            // extension and a few hundred bytes of nothing usable. Reading one throws, so
            // leaving a workbook open in Excel used to fail the whole run - and the message
            // named a file the author never created.
            if (System.IO.Path.GetFileName(filename).StartsWith("~$", StringComparison.Ordinal))
            {
                onLockFile?.Invoke(filename);
                continue;
            }

            if (extensions.Count > 0)
            {
                string extension = System.IO.Path.GetExtension(filename).ToLowerInvariant();

                if (!extensions.Contains(extension))
                    continue;
            }

            // Relative to the directory being searched, so a recipe names a workbook the way
            // somebody looking at that directory would - `backup/Items.xlsx` rather than
            // whatever absolute path this run happened to be given.
            yield return (filename, System.IO.Path.GetRelativePath(root, filename).Replace('\\', '/'));
        }
    }

    /// <summary>
    /// Reads an extension list as a recipe writes it: `.xlsx;.xlsm;.xlsb`.
    /// </summary>
    /// <remarks>
    /// Blank yields the one extension every spreadsheet source can read, which is what this
    /// did before the setting existed.
    /// </remarks>
    public static IReadOnlyCollection<string> Extensions(string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns))
            return [".xlsx"];

        var extensions = patterns
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pattern => pattern.Trim().ToLowerInvariant())
            .Where(pattern => pattern.Length > 0)
            .ToList();

        return extensions.Count > 0 ? extensions : [".xlsx"];
    }
}
