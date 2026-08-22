using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Tabbit.Messages;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The gate around this tool's own reports.
///
/// Golden does not cover any of this - no golden fixture holds a diagnostic - so nothing
/// else notices when a report's id and its text stop agreeing. These checks are what stands
/// in for it, and all of them are static: no conversion has to run for a missing entry or a
/// dropped placeholder to be found. spec/message-ids.md §8.
/// </summary>
public class MessageCatalogTests
{
    /// <summary>
    /// Every id the code declares has English text.
    /// </summary>
    /// <remarks>
    /// Without this the first sign of a missing entry is the id itself appearing in a report,
    /// on the run that hit that path - which for an error path can be months later.
    /// </remarks>
    [Fact]
    public void Every_declared_id_has_English_text()
    {
        var english = MessageCatalog.English;

        var missing = MessageRegistry.All
            .Where(declared => !english.Has(declared.Id))
            .Select(declared => $"{declared.DeclaringType}: {declared.Id}")
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// No catalog entry that nothing declares.
    /// </summary>
    /// <remarks>
    /// The same reason the known-problem list refuses an entry that matches nothing: a
    /// catalog nobody prunes becomes a place where a translator's work goes to be paid for
    /// and never read. A renamed id leaves its old text behind, and this is what says so.
    /// </remarks>
    [Fact]
    public void No_English_entry_is_undeclared()
    {
        var declared = MessageRegistry.Ids.ToHashSet(StringComparer.Ordinal);

        var orphaned = MessageCatalog.IdsInFiles(MessageCatalog.FallbackLanguage)
            .Where(id => !declared.Contains(id))
            .ToList();

        Assert.Empty(orphaned);
    }

    /// <summary>
    /// Every language names the same placeholders as English, for each id it translates.
    /// </summary>
    /// <remarks>
    /// The check that a person cannot do by eye. A translation may reorder a sentence freely
    /// and that is the whole reason the placeholders are named - but one that drops `{Type}`
    /// reads perfectly and names nothing, and one that invents `{Kind}` prints the word
    /// `{Kind}` to whoever is trying to fix a sheet.
    ///
    /// Only the ids a language actually has are compared. A key it has not translated yet
    /// falls back to English, which by definition matches.
    /// </remarks>
    [Fact]
    public void Every_translation_names_the_same_placeholders_as_English()
    {
        var english = MessageCatalog.English;
        var wrong = new List<string>();

        foreach (string language in TranslatedLanguages())
        {
            var catalog = MessageCatalog.ForLanguage(language);

            foreach (string id in MessageCatalog.IdsInFiles(language))
            {
                var expected = Placeholders(english.TextOf(id));
                var actual = Placeholders(catalog.TextOf(id));

                if (!expected.SetEquals(actual))
                {
                    wrong.Add($"{language} `{id}`: English names {Listed(expected)}, "
                              + $"this names {Listed(actual)}");
                }
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// The ids read as ids: lower case, dashes between words, the owner's prefix in front.
    /// </summary>
    /// <remarks>
    /// Stated so that a catalog stays greppable and sorts into its owners. The registry
    /// already refuses a constant whose prefix does not match the class it sits in; this is
    /// about the rest of the name.
    /// </remarks>
    [Fact]
    public void Ids_are_lower_case_and_dashed()
    {
        var wrong = MessageRegistry.All
            .Where(declared => !Regex.IsMatch(declared.Id, "^[a-z0-9]+(\\.[a-z0-9]+(-[a-z0-9]+)*)+$"))
            .Select(declared => declared.Id)
            .ToList();

        Assert.Empty(wrong);
    }

    /// <summary>
    /// Values go in by name, and a name nobody supplied is left where it stands.
    /// </summary>
    [Theory]
    [InlineData("`{Written}` is wrong.", "`text()` is wrong.")]
    [InlineData("{What} and {What} again.", "group and group again.")]
    [InlineData("Nothing here.", "Nothing here.")]
    // Reordered, which is the reason the placeholders are named rather than numbered.
    [InlineData("{What}: `{Written}`", "group: `text()`")]
    // A name the call site did not supply stays as it is, so the report still arrives.
    [InlineData("`{Written}` wants {Missing}.", "`text()` wants {Missing}.")]
    // Doubled braces write one brace and are not looked up. Several messages quote the `text`
    // target's own patterns, which are full of names in braces.
    [InlineData("Write `{{{{` for a literal brace.", "Write `{{` for a literal brace.")]
    [InlineData("uses `{{group}}`, not `{Written}`", "uses `{group}`, not `text()`")]
    [InlineData("{{text}} {{raw}} {{group}}", "{text} {raw} {group}")]
    // A brace with no closing one is left alone rather than swallowing the rest.
    [InlineData("opens a `{{` at {What} and never closes", "opens a `{` at group and never closes")]
    public void Fill_puts_named_values_in(string text, string expected)
    {
        var values = new (string Name, object Value)[] { ("Written", "text()"), ("What", "group") };

        Assert.Equal(expected, Message.Fill(text, values));
    }

    /// <summary>
    /// Numbers are written invariantly, whatever the machine's locale.
    /// </summary>
    /// <remarks>
    /// The language of a sentence and the notation of a number are separate questions. A run
    /// whose numbers follow the machine would put `1,5` in one CI log and `1.5` in another.
    /// </remarks>
    [Fact]
    public void Values_are_written_invariantly()
    {
        var was = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            Assert.Equal("1.5 and 2000",
                Message.Fill("{Ratio} and {Count}",
                    new (string, object)[] { ("Ratio", 1.5), ("Count", 2000) }));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = was;
        }
    }

    /// <summary>
    /// Every language file is one flat object of strings, and no id has text in two files.
    /// </summary>
    /// <remarks>
    /// Loading is what enforces both, so this is here to make the failure a test rather than
    /// a run that fell over on somebody's machine.
    /// </remarks>
    [Fact]
    public void Every_catalog_file_loads()
    {
        foreach (string language in AllLanguages())
            Assert.NotEmpty(MessageCatalog.IdsInFiles(language));
    }

    private static IEnumerable<string> AllLanguages()
        => typeof(MessageCatalog).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Tabbit.Messages.", StringComparison.Ordinal)
                        && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(LanguageOf)
            .Where(language => language.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(language => language, StringComparer.Ordinal);

    private static IEnumerable<string> TranslatedLanguages()
        => AllLanguages().Where(language =>
            !string.Equals(language, MessageCatalog.FallbackLanguage, StringComparison.Ordinal));

    /// <summary>`Tabbit.Messages.core.en.json` is the `en` of `core`.</summary>
    private static string LanguageOf(string resource)
    {
        var parts = resource.Split('.');

        // Tabbit . Messages . <owner> . <language> . json
        return parts.Length >= 5 ? parts[^2] : "";
    }

    /// <remarks>
    /// A doubled brace is a literal one, so `{{group}}` names nothing - the lookarounds are
    /// what keep this from reporting the `text` target's quoted patterns as placeholders that
    /// every translation has to carry.
    /// </remarks>
    private static HashSet<string> Placeholders(string text)
        => Regex.Matches(text, "(?<!\\{)\\{([A-Za-z0-9_]+)\\}(?!\\})")
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

    private static string Listed(IEnumerable<string> names)
    {
        var ordered = names.OrderBy(name => name, StringComparer.Ordinal).ToList();

        return ordered.Count == 0 ? "none" : string.Join(", ", ordered);
    }
}
