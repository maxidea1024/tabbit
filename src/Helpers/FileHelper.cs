using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO;

namespace Tabbit.Helpers;

/// <summary>
/// Direct file operations.
///
/// Note the difference from <see cref="StagingFiles"/>: these write immediately,
/// whereas the exporters and generators go through the staging area so a failed run
/// leaves the previous output intact. Anything producing a build artifact should use
/// StagingFiles; this is for the rest.
/// </summary>
public static class FileHelper
{
    #region File and Directory relatives

    //https://stackoverflow.com/questions/58744/copy-the-entire-contents-of-a-directory-in-c-sharp

    /// <summary>
    /// Copies a directory tree, overwriting files already at the destination.
    /// </summary>
    public static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);
        DirectoryInfo diTarget = new DirectoryInfo(targetDirectory);

        CopyDirectory(diSource, diTarget);
    }

    /// <summary>
    /// Copies a directory tree, overwriting files already at the destination.
    /// </summary>
    public static void CopyDirectory(DirectoryInfo source, DirectoryInfo target)
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

    /// <summary>
    /// Creates the directory a file is about to be written into.
    ///
    /// Takes the file name rather than the directory, since that is what every caller
    /// has in hand.
    /// </summary>
    /// <summary>
    /// Makes sure the directory a file is about to be written to exists.
    /// </summary>
    /// <remarks>
    /// **Remembers the directories it has already answered for.** This is called once per
    /// file, and a conversion commits thousands of them into a few dozen directories - so
    /// asking the filesystem every time was thousands of round trips to learn the same
    /// several dozen facts.
    ///
    /// The memory only ever holds directories this process has seen exist. A directory
    /// removed by something else mid-run would then not be recreated, and that is a trade
    /// worth naming: the alternative is a stat per file for the whole run, and a tool whose
    /// output directory is being deleted underneath it has a larger problem than this.
    /// spec/conversion-time.md section 5.
    /// </remarks>
    public static void EnsurePathExists(string filename)
    {
        var path = Path.GetDirectoryName(filename);

        // TryAdd answers "was this the first time" and adds, in one step - so only the first
        // caller for a directory reaches the filesystem, however many threads arrive at once.
        if (string.IsNullOrEmpty(path) || !_directoriesMade.TryAdd(path, true))
            return;

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    /// <summary>Directories this process has already made sure of.</summary>
    private static readonly ConcurrentDictionary<string, bool> _directoriesMade =
        new ConcurrentDictionary<string, bool>(PathNames.Comparer);

    /// <summary>
    /// Size of a file, or -1 when it cannot be read.
    ///
    /// Used while building the manifest, where a file that has gone missing should not
    /// abort the run.
    /// </summary>
    public static long GetFileSize(string filename)
    {
        try
        {
            var fi = new System.IO.FileInfo(filename);
            return fi.Length;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Writes text to a file, optionally creating the directory and a sidecar
    /// `.md5` holding the content's hash.
    /// </summary>
    public static void WriteAllTextToFile(string filename, string text, bool ensurePathExists = false, bool withMd5Hash = false)
    {
        if (ensurePathExists)
            EnsurePathExists(filename);

        File.WriteAllText(filename, text);

        if (withMd5Hash)
            File.WriteAllText(filename + ".md5", Helper.CalculateMD5HashFromString(text));
    }

    /// <summary>
    /// Writes bytes to a file, optionally creating the directory and a sidecar
    /// `.md5` holding the content's hash.
    /// </summary>
    public static void WriteAllBytesToFile(string filename, byte[] data, bool ensurePathExists = false, bool withMd5Hash = false)
    {
        if (ensurePathExists)
            EnsurePathExists(filename);

        File.WriteAllBytes(filename, data);

        if (withMd5Hash)
            File.WriteAllText(filename + ".md5", Helper.CalculateMD5HashFromBytes(data));
    }

    /// <summary>
    /// Serializes an object to a .json file.
    /// </summary>
    public static void WriteToJsonFile(string filename, object obj, bool ensurePathExists = false, bool indented = true, bool withMd5Hash = false)
    {
        if (ensurePathExists)
            EnsurePathExists(filename);

        string json = JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None);
        WriteAllTextToFile(filename, json, withMd5Hash);
    }

    /// <summary>
    /// Reads a .json file and deserializes it. Throws on a missing or malformed file, which
    /// Manifest.Load relies on to treat an absent or corrupt manifest as simply
    /// absent.
    /// </summary>
    public static T? ReadFromJsonFile <T>(string filename)
    {
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(filename));
    }

    // The recursive-delete family that used to live here is gone: nothing called it.
    // An unused recursive delete is a liability rather than an asset, and whoever
    // next needs one is better off writing exactly what they need.

    #endregion
}
