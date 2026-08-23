using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// What the brackets on a declaration said - flags and key=value pairs.
/// </summary>
/// <remarks>
/// **The parser does not know what any of these keys mean, and does not check them.** That is
/// the policy `LayoutOptions` already runs on, and section 6.4 of the design adopts it whole:
/// the notation carries whatever was written, whatever reads a key reads it, and a separate
/// check reports the keys nobody read as the typos they almost always are.
///
/// The alternative - a fixed list here - would put every future constraint key in this file,
/// so that adding one meant editing the parser. It would also leave no room for a project's
/// own tag, which is what the `x.` prefix is for.
///
/// Order is kept. Nothing reads the entries in order today, but a report that lists them
/// should list them as they were written.
/// </remarks>
public sealed class SchemaMeta
{
    /// <summary>No brackets, or brackets with nothing in them.</summary>
    public static readonly SchemaMeta Empty = new SchemaMeta([]);

    private readonly List<SchemaMetaEntry> _entries;

    public SchemaMeta(List<SchemaMetaEntry> entries)
    {
        _entries = entries;
    }

    /// <summary>The entries, in the order they were written.</summary>
    public IReadOnlyList<SchemaMetaEntry> Entries => _entries;

    /// <summary>Whether a key was written at all, with or without a value.</summary>
    public bool Has(string key)
        => _entries.Any(entry => entry.Key == key);

    /// <summary>
    /// What a key was given, or null when the key was not written.
    /// </summary>
    /// <remarks>
    /// A flag - a key written with no `=` - answers with an empty string, which is what
    /// separates it from a key nobody wrote. <see cref="Has"/> is the question to ask about a
    /// flag; this one is for a key that carries something.
    /// </remarks>
    public string? Value(string key)
    {
        foreach (var entry in _entries)
        {
            if (entry.Key == key)
                return entry.Value ?? "";
        }

        return null;
    }

    /// <summary>Where a key was written, for a report about what it said.</summary>
    public Location? LocationOf(string key)
    {
        foreach (var entry in _entries)
        {
            if (entry.Key == key)
                return entry.Location;
        }

        return null;
    }

    /// <summary>
    /// The keys nothing has claimed, which is what a check for a misspelt one reads.
    /// </summary>
    /// <remarks>
    /// A key beginning `x.` is never here. That prefix is the way out of the check for a
    /// project that wants a tag of its own on a declaration - section 6.4 - and a tool that
    /// reported those would make the way out unusable.
    /// </remarks>
    public IEnumerable<SchemaMetaEntry> Beyond(params string[] known)
    {
        var claimed = known.ToHashSet(System.StringComparer.Ordinal);

        return _entries.Where(entry =>
            !claimed.Contains(entry.Key) && !entry.Key.StartsWith("x."));
    }
}

/// <summary>One metadata entry.</summary>
/// <param name="Key">The key as written.</param>
/// <param name="Value">
/// What followed the `=`, or null when the entry was a flag with no `=` at all.
/// </param>
/// <param name="Location">Where the key was written.</param>
public readonly record struct SchemaMetaEntry(string Key, string? Value, Location Location);
