using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Tabbit.Messages;

/// <summary>
/// The text of every message in one language.
/// </summary>
/// <remarks>
/// Loaded from embedded resources named `Tabbit.Messages.&lt;owner&gt;.&lt;language&gt;.json`, and
/// merged. The split into files is who owns which messages - the core has one, a layout has
/// its own - so that deleting a layout deletes its reports with it. Nothing registers a file:
/// the resource names are the list.
///
/// Not `.resx`. Satellite assemblies put the language axis in the build rather than in a file
/// somebody can open, which does not fit a catalog a layout is supposed to be able to bring
/// with it. And this repository already reads JSON everywhere.
///
/// English is always loaded, and always as the fallback under whatever language was asked
/// for, because a run should not go silent on a key nobody has translated yet.
/// spec/message-ids.md §5.
/// </remarks>
public sealed class MessageCatalog
{
    /// <summary>The language every catalog falls back to, key by key.</summary>
    public const string FallbackLanguage = "en";

    private const string ResourcePrefix = "Tabbit.Messages.";

    private static readonly ConcurrentDictionary<string, MessageCatalog> Loaded = new();

    private readonly IReadOnlyDictionary<string, string> _text;
    private readonly IReadOnlyDictionary<string, string> _fallback;
    private int _untranslated;
    private int _unknown;

    private MessageCatalog(
        string language,
        IReadOnlyDictionary<string, string> text,
        IReadOnlyDictionary<string, string> fallback)
    {
        Language = language;
        _text = text;
        _fallback = fallback;
    }

    /// <summary>Which language this holds.</summary>
    public string Language { get; }

    /// <summary>English, which every id has to be in.</summary>
    public static MessageCatalog English => ForLanguage(FallbackLanguage);

    /// <summary>
    /// The catalog a report is written in when nobody says otherwise.
    /// </summary>
    /// <remarks>
    /// A property rather than the language being passed down to every place that writes a
    /// report: which language a person reads is settled once, at startup, and threading it
    /// through the cooking and validation code would put an argument on hundreds of calls to
    /// answer a question none of them are about.
    ///
    /// English until something sets it. Not the machine's UI culture: two runners on the
    /// same recipe would then produce logs that differ, and a diff between them would show
    /// a change on every run. spec/message-ids.md §5.
    /// </remarks>
    public static MessageCatalog Current { get; set; } = English;

    /// <summary>
    /// How many ids were asked for that this language has no entry for and English answered
    /// instead.
    /// </summary>
    /// <remarks>
    /// Counted so a run can say so once at the end. A translation that is simply absent and
    /// one that was decided against look the same on screen otherwise, and the first is worth
    /// knowing about.
    /// </remarks>
    public int Untranslated => _untranslated;

    /// <summary>
    /// How many ids were asked for that no catalog has at all - which the gate makes
    /// impossible, so a run seeing one has found a defect.
    /// </summary>
    public int Unknown => _unknown;

    /// <summary>The catalog for a language, loaded once per process.</summary>
    public static MessageCatalog ForLanguage(string language)
        => Loaded.GetOrAdd(language, Load);

    /// <summary>Every id any catalog file in this build holds, for the gate.</summary>
    public static IReadOnlyList<string> IdsInFiles(string language)
        => Read(language).Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// This language's text for an id, English if it has none, and the id itself if nothing
    /// does.
    /// </summary>
    public string TextOf(string id)
    {
        if (_text.TryGetValue(id, out string? text))
            return text;

        if (_fallback.TryGetValue(id, out string? english))
        {
            System.Threading.Interlocked.Increment(ref _untranslated);
            return english;
        }

        // The id rather than an empty string or a throw. Throwing while reporting a problem
        // would replace the problem, and an empty message says nothing at all - whereas the
        // id names what happened and can be looked up.
        System.Threading.Interlocked.Increment(ref _unknown);
        return id;
    }

    /// <summary>Whether this language has its own entry for an id.</summary>
    public bool Has(string id) => _text.ContainsKey(id);

    private static MessageCatalog Load(string language)
    {
        var text = Read(language);

        var fallback = string.Equals(language, FallbackLanguage, StringComparison.OrdinalIgnoreCase)
            ? text
            : Read(FallbackLanguage);

        return new MessageCatalog(language, text, fallback);
    }

    /// <summary>
    /// Merges every catalog file for one language.
    /// </summary>
    /// <remarks>
    /// An id in two files is refused rather than one of them winning: which file a report's
    /// text comes from would then depend on the order the resources happen to be listed in.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> Read(string language)
    {
        var assembly = typeof(MessageCatalog).Assembly;
        string suffix = "." + language + ".json";

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var cameFrom = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (id, text) in Entries(assembly, resource))
            {
                if (cameFrom.TryGetValue(id, out string? earlier))
                {
                    throw new TabbitDefectException(
                        $"The id `{id}` has text in both `{earlier}` and `{resource}`.");
                }

                merged[id] = text;
                cameFrom[id] = resource;
            }
        }

        return merged;
    }

    private static IEnumerable<(string Id, string Text)> Entries(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new TabbitDefectException($"Embedded resource `{resource}` is missing from the build.");

        using var reader = new StreamReader(stream);

        var parsed = JObject.Parse(reader.ReadToEnd());

        foreach (var property in parsed.Properties())
        {
            string? text = property.Value.Type == JTokenType.String
                ? (string?)property.Value
                : null;

            if (text is null)
            {
                throw new TabbitDefectException(
                    $"`{resource}` gives `{property.Name}` something other than a string.");
            }

            yield return (property.Name, text);
        }
    }
}
