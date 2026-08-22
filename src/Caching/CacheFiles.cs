using System.IO;
using Tabbit.Helpers;

namespace Tabbit.Caching;

/// <summary>
/// Where a run keeps the files it writes about itself rather than for a consumer.
/// </summary>
/// <remarks>
/// The build seal and the build report are both one-per-recipe, both belong beside each
/// other, and both have to be found again by a later run that was given nothing but the
/// same command line. That makes the naming a rule rather than two conventions, and a rule
/// with two copies is one that drifts: a run would seal under one name and report under
/// another the day somebody changed how the hash is taken.
///
/// The name is there so a person can tell which file is which; the hash is there because
/// two checkouts hold recipes of the same name and they describe different runs.
/// </remarks>
internal static class CacheFiles
{
    /// <summary>Directory these live in when the command line does not say.</summary>
    public const string DefaultDirectory = ".tabbit";

    /// <summary>Where this run's own files go.</summary>
    public static string Directory(Options options)
        => string.IsNullOrWhiteSpace(options.CacheDirectory)
            ? DefaultDirectory
            : options.CacheDirectory!;

    /// <summary>
    /// This recipe's file with the given suffix - `.seal.json`, `.report.html`.
    /// </summary>
    public static string PathFor(Options options, string suffix)
        => Path.Combine(Directory(options), NameFor(options) + suffix);

    /// <summary>The stem both files share: the recipe's name and where it sits.</summary>
    private static string NameFor(Options options)
    {
        string recipe = Path.GetFullPath(options.RecipeFilename!);

        string name = Path.GetFileNameWithoutExtension(recipe);
        string where = ContentHash.OfText(
            PathNames.Comparison == System.StringComparison.OrdinalIgnoreCase
                ? recipe.ToLowerInvariant()
                : recipe);

        return $"{name}-{where[..12]}";
    }
}
