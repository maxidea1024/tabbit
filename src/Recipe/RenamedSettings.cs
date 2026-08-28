using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tabbit.Messages;

namespace Tabbit.Recipe;

/// <summary>
/// Settings that have been renamed, and the recipes still holding the old name.
/// </summary>
/// <remarks>
/// Newtonsoft ignores a key nothing binds to, so a recipe left holding a renamed setting
/// would go on converting with the new setting's default. For `DefaultDelimiter` that means
/// a project whose sheets write `1|2|3` silently reading each cell as one value - a wrong
/// build that nothing in the run says a word about, which is the failure this tool exists
/// to prevent.
///
/// So the old name is read and reported rather than ignored. The rewrite happens on the
/// document before anything binds to it, which puts the new name in the build cache key as
/// well: a recipe renamed by hand afterwards keys the same and does not rebuild.
/// </remarks>
internal static class RenamedSettings
{
    private static Serilog.ILogger Log => LogCategory.Loading;

    /// <summary>Old name to new name. Both are matched at any depth of the document.</summary>
    /// <remarks>
    /// At any depth because a setting renamed on the recipe is usually renamed on the source
    /// entry with it - `DefaultDelimiter` is written in both places - and the two are the
    /// same rename to whoever has to do it.
    /// </remarks>
    private static readonly Dictionary<string, string> Renames = new Dictionary<string, string>
    {
        ["ArrayDelimiter"] = "DefaultDelimiter",
    };

    /// <summary>
    /// Rewrites every renamed setting the document holds, saying what it renamed.
    /// </summary>
    public static void Apply(JObject document, string filename)
    {
        foreach (var pair in Renames)
        {
            // Materialized before anything is touched: the properties are being removed
            // from the objects this walk is over.
            var holders = document.Descendants()
                .OfType<JObject>()
                .Concat(new[] { document })
                .Where(holder => holder.Property(pair.Key) is not null)
                .ToList();

            foreach (var holder in holders)
                Rename(holder, pair.Key, pair.Value, filename);
        }
    }

    private static void Rename(JObject holder, string from, string to, string filename)
    {
        var old = holder.Property(from)!;

        // Both names on one object. There is no reading of that which is not a guess about
        // which one the author meant to keep, and the two may hold different values.
        if (holder.Property(to) is not null)
        {
            throw new TabbitException(null,
                Message.Of(RecipeMessages.SettingNamedTwice,
                    ("Old", from), ("New", to), ("File", filename)));
        }

        old.Remove();
        holder.Add(to, old.Value);

        Log.Warning(
            Message.Of(RecipeMessages.LogSettingRenamed,
                ("Old", from), ("New", to), ("File", filename)).In(MessageCatalog.Current));
    }
}
