using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tabbit.Lsp;

/// <summary>
/// What the editor has open, and what the rest of a schema's directory holds.
/// </summary>
/// <remarks>
/// **An open buffer wins over the file on disk.** The point of the server is to answer about
/// text that has not been saved yet, and a sibling declaration the author is halfway through
/// editing is still the declaration this file has to resolve against.
/// </remarks>
internal sealed class DocumentStore
{
    private readonly object _gate = new();

    /// <summary>Unsaved text, by normalized path.</summary>
    private readonly Dictionary<string, string> _open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The exact spelling of the URI the client used, by normalized path.
    /// </summary>
    /// <remarks>
    /// Kept rather than rebuilt because a URI that differs from the client's by one character
    /// is a URI the client does not recognise as the file it opened - and the drive letter's
    /// colon alone is spelled two ways in the wild. Rebuilt only for files the client has
    /// never named. spec/ops/lsp.md section 8.
    /// </remarks>
    private readonly Dictionary<string, string> _uris = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The file a URI names, in this server's one spelling of a path.
    /// </summary>
    /// <remarks>
    /// **Not <see cref="Uri.LocalPath"/>, which is wrong for the form editors actually send.**
    /// It turns `file:///C:/x` into `C:\x`, but leaves `file:///c%3A/x` as `/c:/x` - the
    /// escaped colon stops it recognising a drive letter, and the path then resolves against
    /// the current drive's root as `C:\c:\x`. So the path is unescaped here and the slash in
    /// front of a drive letter taken off.
    /// </remarks>
    public static string PathOf(string uri)
    {
        var parsed = new Uri(uri);
        string path = Uri.UnescapeDataString(parsed.AbsolutePath);

        if (path.Length > 2 && path[0] == '/' && path[2] == ':')
            path = path[1..];
        else if (parsed.Host.Length > 0)
            path = "//" + parsed.Host + path;   // a share, which has no drive letter to strip

        return Normalize(path);
    }

    /// <summary>One spelling of a path: absolute, with forward slashes.</summary>
    /// <remarks>
    /// Forward slashes because <see cref="Models.Location"/> stores them that way, and the
    /// path a report carries is what the diagnostics are grouped by.
    /// </remarks>
    public static string Normalize(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');

        // The drive letter gets one spelling as well. The same file arrives as `C:/x` from
        // one client and `c:/x` from another, and everything keyed by a path here should hold
        // one entry for it either way.
        if (full.Length > 1 && full[1] == ':')
            full = char.ToUpperInvariant(full[0]) + full[1..];

        return full;
    }

    /// <summary>The URI for a file, as an editor spells it.</summary>
    public string UriOf(string path)
    {
        lock (_gate)
        {
            if (_uris.TryGetValue(path, out string? known))
                return known;
        }

        return UriFor(path);
    }

    public void Open(string uri, string text)
    {
        string path = PathOf(uri);

        lock (_gate)
        {
            _open[path] = text;
            _uris[path] = uri;
        }
    }

    public void Close(string uri)
    {
        lock (_gate)
            _open.Remove(PathOf(uri));
    }

    /// <summary>The text of a file, from the open buffer or from disk.</summary>
    public string? TextOf(string path)
    {
        lock (_gate)
        {
            if (_open.TryGetValue(path, out string? held))
                return held;
        }

        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>One line of a file, without its ending.</summary>
    /// <remarks>
    /// What completion is worked out from. The whole text is split each time it is asked for -
    /// these are declaration files of a few hundred lines, and a cache of line offsets would
    /// be a second thing to keep in step with every keystroke.
    /// </remarks>
    public string LineOf(string path, int line)
    {
        string? text = TextOf(path);

        if (text is null || line < 0)
            return "";

        string[] lines = text.Replace("\r\n", "\n").Split('\n');

        return line < lines.Length ? lines[line] : "";
    }

    /// <summary>
    /// Every `.tbs` file of one directory, with its text.
    /// </summary>
    /// <remarks>
    /// A directory is the unit the declarations are checked as - section 4.2 of the spec - so
    /// this is what one round of checking reads. Sorted so that two runs over the same folder
    /// report in the same order.
    /// </remarks>
    public IReadOnlyList<(string Path, string Text)> FilesIn(string directory)
    {
        var found = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.tbs"))
            {
                string path = Normalize(file);
                string? text = TextOf(path);

                if (text is not null)
                    found[path] = text;
            }
        }
        catch (DirectoryNotFoundException)
        {
            // The folder was deleted while it was open. Whatever is still in a buffer below
            // is the whole of what is left.
        }

        // A buffer for a file that is not on disk yet - a new file the author has typed into
        // but not saved - belongs to the round as much as a saved one.
        lock (_gate)
        {
            foreach (var (path, text) in _open)
            {
                if (string.Equals(DirectoryOf(path), directory, StringComparison.OrdinalIgnoreCase))
                    found[path] = text;
            }
        }

        var files = new List<(string, string)>(found.Count);

        foreach (var (path, text) in found)
            files.Add((path, text));

        return files;
    }

    /// <summary>The directory a file sits in, in this server's one spelling.</summary>
    public static string DirectoryOf(string path)
        => Normalize(Path.GetDirectoryName(path) ?? path);

    /// <summary>
    /// A `file:` URI for a path the client has never named.
    /// </summary>
    /// <remarks>
    /// Built rather than taken from <see cref="Uri.AbsoluteUri"/> so that a drive letter comes
    /// out the way editors write it: lower case, with the colon escaped.
    /// </remarks>
    internal static string UriFor(string path)
    {
        string full = Normalize(path);
        var built = new StringBuilder("file://");

        if (full.Length > 1 && full[1] == ':')
        {
            built.Append('/').Append(char.ToLowerInvariant(full[0])).Append("%3A");
            full = full[2..];
        }

        foreach (string segment in full.Split('/'))
        {
            if (segment.Length > 0)
                built.Append('/').Append(Uri.EscapeDataString(segment));
        }

        return built.ToString();
    }
}
