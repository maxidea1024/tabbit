using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Tabbit.History;
using Tabbit.Models;

namespace Tabbit.Caching;

/// <summary>
/// The keys the cache compares, taken from the recipe and the command line.
/// </summary>
/// <remarks>
/// Three keys rather than one, because the answers they lead to differ. An output entry's
/// setting changing is no reason to read thirty-six workbooks again; a recipe-wide setting
/// changing is every reason. Folding them together would make the smaller change cost what
/// the larger one costs - spec/build-cache.md §2.
///
/// What goes into the recipe key is defined by subtraction: the whole document, less the
/// parts that have keys of their own. So a setting added to the recipe later is in the key
/// the day it is added, and nobody has to remember to put it there.
/// </remarks>
internal static class RecipeKeys
{
    /// <summary>Sections of the recipe that have a key of their own.</summary>
    private static readonly string[] OwnKeyed = ["Validation", "Targets"];

    /// <summary>
    /// The recipe's settings other than validation and the output entries.
    /// </summary>
    public static string Recipe(JObject document)
    {
        var rest = (JObject)document.DeepClone();

        foreach (var name in OwnKeyed)
        {
            foreach (var property in rest.Properties()
                                         .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                                         .ToList())
            {
                property.Remove();
            }
        }

        return ContentHash.OfText(Canonical(rest));
    }

    /// <summary>The validation section, and the switches that narrow what it runs.</summary>
    public static string Validation(JObject document, Options options)
    {
        var section = Section(document, "Validation");

        return ContentHash.OfParts(
            section is null ? null : Canonical(section),
            options.SkipRuntimeValidation ? "skip-runtime" : "all-rules");
    }

    /// <summary>
    /// One output entry's own settings, and the side it will be built for.
    /// </summary>
    /// <remarks>
    /// Taken from the entry as the registry materialised it rather than from the recipe's
    /// text. Two spellings of one entry then produce one key - a setting written out at its
    /// default, and the same setting left out - which is what a person editing a recipe
    /// would expect, and it avoids having to find the entry's own index in the document.
    /// </remarks>
    public static string Target(object entry, TargetSide side)
        => ContentHash.OfParts(
            Canonical(JObject.FromObject(entry, EntryReader)),
            side.ToString());

    /// <summary>
    /// Writes an output entry out for hashing.
    /// </summary>
    /// <remarks>
    /// Defaults included, so a setting whose default changes between builds changes the key.
    /// It has to: the previous build produced different output from the same recipe, and the
    /// tool version alone would not say which entries were affected.
    /// </remarks>
    private static readonly Newtonsoft.Json.JsonSerializer EntryReader =
        Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        {
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Include,
            DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include,
        });

    /// <summary>
    /// The command line, as far as it changes what the conversion produces.
    /// </summary>
    /// <remarks>
    /// Built from the classification on the options themselves, so an option added without
    /// a decision about caching cannot quietly land in the "does not matter" half. What
    /// carries the decision is <see cref="CacheAttribute"/>, and
    /// <c>OptionCacheClassificationTests</c> is what makes it mandatory.
    /// </remarks>
    /// <param name="withCommit">
    /// Whether the commit options belong in the key. True when the recipe has a target that
    /// files a snapshot under a commit, because then converting the same data at a new
    /// commit still has something to record.
    /// </param>
    public static string Options(Options options, bool withCommit)
    {
        using var fingerprint = new Fingerprint();

        // Sorted by name so the key does not depend on the order reflection happens to
        // hand the properties over, which is not contractual.
        var properties = typeof(Options).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .OrderBy(property => property.Name, StringComparer.Ordinal);

        foreach (var property in properties)
        {
            var relevance = property.GetCustomAttribute<CacheAttribute>()?.Relevance;

            bool counts = relevance == CacheRelevance.Output
                       || (relevance == CacheRelevance.Commit && withCommit);

            if (!counts)
                continue;

            fingerprint.Add(property.Name);
            fingerprint.Add(Rendered(property.GetValue(options)));
        }

        return fingerprint.Complete();
    }

    /// <summary>
    /// One recipe section, or null when the document has none.
    /// </summary>
    public static JToken? Section(JObject document, string name)
        => document.Properties()
                   .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                   ?.Value;

    /// <summary>
    /// A document as text, with every object's properties in a fixed order.
    /// </summary>
    /// <remarks>
    /// Sorted so that moving a setting to another line of the recipe is not a change. It
    /// is not one: the recipe is read into an object model where the order of properties
    /// means nothing, and a person who tidies a file should not pay for a full conversion.
    ///
    /// Arrays keep their order, because in a recipe an array's order does mean something -
    /// the output entries run in it, and the sources are read in it.
    /// </remarks>
    private static string Canonical(JToken token)
    {
        var canonical = Sorted(token);

        return canonical.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JToken Sorted(JToken token)
    {
        switch (token)
        {
            case JObject o:
            {
                var sorted = new JObject();

                foreach (var property in o.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted.Add(property.Name, Sorted(property.Value));

                return sorted;
            }

            case JArray a:
            {
                var sorted = new JArray();

                foreach (var item in a)
                    sorted.Add(Sorted(item));

                return sorted;
            }

            default:
                return token.DeepClone();
        }
    }

    /// <summary>
    /// One option's value as text, the same way on every machine.
    /// </summary>
    /// <remarks>
    /// A null and an empty string are one thing here - both are the option not being given -
    /// while <see cref="Fingerprint"/> keeps them apart in general. Collapsing them means
    /// `--time-zone ""` and no `--time-zone` produce one key, which they should: the run
    /// does the same thing either way.
    /// </remarks>
    private static string? Rendered(object? value)
        => value switch
        {
            null => null,
            string text => string.IsNullOrEmpty(text) ? null : text,
            bool flag => flag ? "1" : "0",
            IEnumerable<string> list => string.Join('', list),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
}
