using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tabbit.Messages;

namespace Tabbit.Validation;

/// <summary>
/// The files under one root, by name.
/// </summary>
/// <remarks>
/// For the rules that check a value names something that exists - a texture, a map, a sound.
/// Whether an asset is there is not a question this tool can answer for itself: it does not know
/// what an asset is, which is why spec/layout/column-constraints.md leaves the sheets' `:asset` row
/// alone. What it can do is hand over a scanned folder and let the project's own rule decide -
/// the core never learns which extension matters.
///
/// Scanned once per root and kept, because the folder in question is usually a game's whole
/// content tree and a rule asks about it per row.
/// </remarks>
public sealed class FileMap : IFileMap
{
    private readonly Dictionary<string, string> _byName;

    internal FileMap(string root, string pattern, IEnumerable<string> paths)
    {
        Root = root;
        Pattern = pattern;

        // Keyed without the extension and case-insensitively, because that is how a sheet names
        // an asset: `Ship_Galleon`, not `Ship_Galleon.uasset` in whatever case the artist saved.
        _byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Ordered here rather than by the callers, so "first" below means the same thing on
        // every platform. The scan hands these over in filesystem order, which ext4 and NTFS
        // do not agree on - so which of two files of one name a rule saw depended on where
        // the conversion ran, and so did the order `Names` reports.
        foreach (string path in Helpers.PathNames.InOrder(paths))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);

            // First wins. Two files of one name in different folders is the project's business,
            // and a rule that cares can walk `Names` and ask for each path.
            if (!_byName.ContainsKey(name))
                _byName.Add(name, path);
        }
    }

    /// <summary>Folder that was scanned.</summary>
    public string Root { get; }

    /// <summary>Pattern the scan used.</summary>
    public string Pattern { get; }

    /// <summary>How many files were found.</summary>
    public int Count => _byName.Count;

    /// <summary>Whether a file of this name exists, extension and case aside.</summary>
    public bool Has(string name) => name is not null && _byName.ContainsKey(name);

    /// <summary>The path of a file of this name, or null.</summary>
    public string? PathOf(string name)
        => name is not null && _byName.TryGetValue(name, out string? found) ? found : null;

    /// <summary>Every name found, for a rule that wants to walk them.</summary>
    public IEnumerable<string> Names => _byName.Keys;
}

/// <summary>
/// The files and JSON documents a rule reads from outside the sheets.
/// </summary>
/// <remarks>
/// Both cached per run and shared by every rule, since the cost is in the scanning and the
/// parsing rather than in the asking. Held here rather than in the context so the cache is one
/// per run rather than one per rule file.
/// </remarks>
internal sealed class ExternalFiles
{
    private readonly Dictionary<string, FileMap> _maps = new Dictionary<string, FileMap>();
    private readonly Dictionary<string, JToken> _json = new Dictionary<string, JToken>();

    /// <summary>Scans a folder, or answers the scan already made of it.</summary>
    internal FileMap Map(string root, string pattern)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new TabbitException(null, Message.Of(ValidationMessages.FilesNeedsFolder));

        string full = System.IO.Path.GetFullPath(root);
        string key = $"{full}|{pattern}";

        lock (_maps)
        {
            if (_maps.TryGetValue(key, out var found))
                return found;

            if (!Directory.Exists(full))
            {
                throw new TabbitException(null,
                    Message.Of(ValidationMessages.FilesFolderMissing,
                        ("Root", root), ("Pattern", pattern), ("Full", full)));
            }

            var map = new FileMap(full, pattern,
                Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories));

            _maps.Add(key, map);

            return map;
        }
    }

    /// <summary>Reads a JSON document, or answers the one already read.</summary>
    /// <remarks>
    /// For the data a project keeps outside its sheets - the case the Lua validators met most
    /// often after the tables themselves. A table is not this: `Tables` holds those, typed.
    /// </remarks>
    internal JToken Json(string path)
    {
        string full = System.IO.Path.GetFullPath(path);

        lock (_json)
        {
            if (_json.TryGetValue(full, out var found))
                return found;

            if (!File.Exists(full))
                throw new TabbitException(null,
                    Message.Of(ValidationMessages.JsonFileMissing, ("Path", path), ("Full", full)));

            JToken parsed;

            try
            {
                parsed = JToken.Parse(File.ReadAllText(full));
            }
            catch (Exception failure) when (failure is not TabbitDefectException)
            {
                throw new TabbitException(null,
                    Message.Of(ValidationMessages.JsonUnreadable,
                        ("Full", full), ("Detail", failure.Message)));
            }

            _json.Add(full, parsed);

            return parsed;
        }
    }
}
