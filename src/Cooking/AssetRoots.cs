using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tabbit.Recipe;
using Tabbit.Validation;

namespace Tabbit.Cooking;

/// <summary>
/// The folders an `asset` column's values are checked against, scanned once and kept.
/// </summary>
/// <remarks>
/// Once because the folder in question is usually a game's whole content tree and the
/// question is asked per cell - a real project asks it a few hundred thousand times.
///
/// The scanning itself is <see cref="FileMap"/>, which the validation rules already use for
/// this. What is here is only the part that maps a kind to the folders that hold it, which is
/// what the recipe adds on top.
/// </remarks>
public sealed class AssetRoots
{
    private readonly Dictionary<string, List<FileMap>> _byKind;

    private AssetRoots(Dictionary<string, List<FileMap>> byKind, string onMissing)
    {
        _byKind = byKind;
        OnMissing = onMissing;
    }

    /// <summary>What a value naming no file amounts to: `warn`, `error` or `ignore`.</summary>
    public Severity? OnMissingSeverity => OnMissing switch
    {
        "error" => Severity.Error,
        "warn" => Severity.Warning,
        _ => null,
    };

    /// <summary>The recipe's setting, kept for the messages.</summary>
    public string OnMissing { get; }

    /// <summary>The kinds a recipe configured, for a message about one it did not.</summary>
    public IEnumerable<string> Kinds => _byKind.Keys;

    /// <summary>
    /// Scans every folder the recipe named, or answers null when it named none.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty instance, because "no roots" is not "no assets found" - it
    /// is the check being switched off, and the two want different words at the call site.
    /// </remarks>
    public static AssetRoots? From(AssetsRecipe recipe)
    {
        if (recipe is null || recipe.Roots is null || recipe.Roots.Count == 0)
            return null;

        string onMissing = (recipe.OnMissing ?? "").Trim().ToLowerInvariant();

        if (onMissing.Length == 0)
            onMissing = "warn";

        if (onMissing is not ("warn" or "error" or "ignore"))
        {
            throw new TabbitException(
                $"Recipe setting `Assets.OnMissing` is `{recipe.OnMissing}`. It has to be "
                + $"`warn`, `error` or `ignore`.");
        }

        // Case-insensitive: a kind is a word somebody types into a sheet cell, and `Icon` and
        // `icon` are not two kinds.
        var byKind = new Dictionary<string, List<FileMap>>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in recipe.Roots)
        {
            // A root left in the recipe with a blank path is switched off, which is what a
            // blank path means at every other entry this tool reads. The skeleton a new
            // recipe starts from writes exactly this, and it has to run.
            if (string.IsNullOrWhiteSpace(root.Path))
                continue;

            string full = Path.GetFullPath(root.Path);

            if (!Directory.Exists(full))
            {
                throw new TabbitException(
                    $"`Assets.Roots` points at `{root.Path}`, and there is no folder at "
                    + $"`{full}`. A root that is not there is a recipe mistake rather than a "
                    + $"tree with nothing in it - every value checked against it would be "
                    + $"reported missing.");
            }

            string pattern = string.IsNullOrWhiteSpace(root.Pattern) ? "*" : root.Pattern;
            string kind = (root.Kind ?? "").Trim();

            if (!byKind.TryGetValue(kind, out var maps))
            {
                maps = new List<FileMap>();
                byKind.Add(kind, maps);
            }

            maps.Add(new FileMap(
                full, pattern, Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories)));
        }

        // Entries that were all switched off amount to no roots at all, and the caller's
        // "nothing is configured" path is the one that says so out loud.
        return byKind.Count == 0 ? null : new AssetRoots(byKind, onMissing);
    }

    /// <summary>Whether any root of this kind was configured.</summary>
    public bool Knows(string kind) => _byKind.ContainsKey(kind ?? "");

    /// <summary>Whether a file of this name is in any root of this kind.</summary>
    /// <remarks>
    /// Any, not all: a content tree that grew in two places is ordinary, and a value naming a
    /// file in either of them is a value that resolves.
    /// </remarks>
    public bool Has(string kind, string value)
    {
        if (!_byKind.TryGetValue(kind ?? "", out var maps))
            return false;

        foreach (var map in maps)
        {
            if (map.Has(value))
                return true;
        }

        return false;
    }

    /// <summary>How many files were found for a kind, for the run's own record.</summary>
    public int CountOf(string kind)
        => _byKind.TryGetValue(kind ?? "", out var maps) ? maps.Sum(map => map.Count) : 0;
}
