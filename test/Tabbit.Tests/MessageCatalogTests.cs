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
    /// The entries that quote braces come out with the braces they meant.
    /// </summary>
    /// <remarks>
    /// The `text` target's reports are the awkward case: they are about patterns written in
    /// braces, so they quote braces, and no test asserted on their wording before. Pinned
    /// here because nothing else would notice a doubling gained or lost - and one of these
    /// deliberately shows four braces, which reads like a mistake until you count them
    /// against the source it came from.
    ///
    /// A value that itself holds a brace is checked too. Values are inserted rather than
    /// re-scanned, so a pattern like `a{b` has to arrive intact.
    /// </remarks>
    [Fact]
    public void Brace_quoting_entries_render_the_braces_they_meant()
    {
        var english = MessageCatalog.English;

        Assert.Equal(
            "The `text` target's `Format` opens a `{` at position 3 and never closes it: "
            + "`a{b`. Write `{{` for a literal brace.",
            Message.Of(Tabbit.Exporters.ExportMessages.TextPatternUnclosedBrace,
                ("Setting", "Format"), ("At", 3), ("Pattern", "a{b")).In(english));

        string unknown = Message.Of(Tabbit.Exporters.ExportMessages.TextPatternUnknownName,
            ("Setting", "Format"), ("Name", "thing"), ("Pattern", "{thing}")).In(english);

        Assert.StartsWith(
            "The `text` target's `Format` uses `{thing}`, which is not a name this target "
            + "fills in: `{thing}`.",
            unknown);

        Assert.Contains("Per string: {text} {raw} {table} {field} {location} {index}", unknown);
        Assert.Contains("Per file:   {group} {namespace} {count}", unknown);
        Assert.EndsWith("`{{{{` writes a literal brace.", unknown);

        string needsFormat =
            Message.Of(Tabbit.Exporters.ExportMessages.TextNeedsFormat).In(english);

        Assert.Contains("\"Format\": \"NSLOCTEXT(\\\"{namespace}\\\", \\\"{group}\\\", "
                        + "\\\"{text}\\\")\"", needsFormat);

        Assert.Contains(
            "filled in per string: {text} {raw} {group} {namespace} {table} {field} "
            + "{location} {index}.",
            needsFormat);
    }

    /// <summary>
    /// A Korean particle follows the value it comes after.
    /// </summary>
    /// <remarks>
    /// The one place a value decides the grammar around it. The cases that matter are the ones
    /// nobody would guess: the values here are type names and column names, not Korean words,
    /// so the rule has to know that `int` closes and `float` does not, and that `2` is read
    /// 이 while `3` is read 삼.
    /// </remarks>
    [Theory]
    // Hangul, where the syllable itself carries the answer.
    [InlineData("값", "은", "값은")]
    [InlineData("자리", "은", "자리는")]
    [InlineData("컬럼", "이", "컬럼이")]
    [InlineData("이유", "이", "이유가")]
    // Latin words, which is what most values are. `int` is read 인트 and `float` 플로트, and
    // both end on a vowel - the shape of the spelling says nothing about it.
    [InlineData("int", "은", "int는")]
    [InlineData("float", "은", "float는")]
    [InlineData("bool", "이", "bool이")]
    [InlineData("uuid", "이", "uuid가")]
    // Digits, read aloud: 영 일 이 삼 사 오 육 칠 팔 구.
    [InlineData("0", "은", "0은")]
    [InlineData("2", "은", "2는")]
    [InlineData("3", "이", "3이")]
    [InlineData("9", "이", "9가")]
    // The copula is a pair too: 이어야/여야. It reads as a word rather than a particle,
    // which is why it was written by hand as 이어야 in an entry whose value was `bool` -
    // correct there, and wrong for every type that ends on a vowel.
    [InlineData("bool", "이어야", "bool이어야")]
    [InlineData("float", "이어야", "float여야")]
    // Trailing punctuation is not a sound, so the letter before it decides.
    [InlineData("`int`", "은", "`int`는")]
    [InlineData("`float`", "은", "`float`는")]
    public void A_Korean_particle_follows_the_value(string value, string pair, string expected)
    {
        Assert.Equal(
            expected,
            Message.Fill("{Value:" + pair + "}", new (string, object)[] { ("Value", value) }));
    }

    /// <summary>
    /// No Korean entry writes a particle after a placeholder without asking for the pair.
    /// </summary>
    /// <remarks>
    /// The one gate here that was written after the mistake rather than before it. Forty
    /// entries spelled `{Element}가` where they meant `{Element:가}` - a particle chosen once,
    /// by hand, for a value that is different on every run. They read correctly for whatever
    /// value happened to be in front of me while I wrote them, which is exactly why no other
    /// check saw it: the id is right, the placeholder is right, the text renders.
    ///
    /// Only Korean has this failure mode, and only Korean pays for the gate.
    /// </remarks>
    [Fact]
    public void A_Korean_particle_is_never_hardcoded_after_a_placeholder()
    {
        var pairs = new[]
        {
            // Longest first, so 으로 is not read as 로 with a stray 으 in front of it.
            "이어야", "여야", "으로", "이라", "이나",
            "이든", "이며", "은", "는", "이", "가", "을",
            "를", "와", "과", "로", "라", "나", "든", "며",
            "아", "야",
        };

        // A placeholder, not an escaped brace, followed straight by one of the pairs -
        // and then a word boundary, because a particle is only a particle at the end of
        // one. `{Count}가지` is "N kinds", where 가 opens a noun, and `{Type}이어야` is the
        // copula. Both read as a hardcoded particle to anything that matches on shape
        // alone, and both are correct as they stand.
        var hardcoded = new Regex(
            @"(?<!\{)\{[A-Za-z0-9_]+\}(" + string.Join("|", pairs)
            + @")(?![가-힣])");

        var written = MessageCatalog.ForLanguage("ko");

        var sites = MessageRegistry.Ids
            .Where(id => written.Has(id))
            .Where(id => hardcoded.IsMatch(written.TextOf(id)))
            .ToList();

        Assert.True(
            sites.Count == 0,
            "A Korean entry writes a particle straight after a placeholder, so it was "
            + "chosen once for a value that changes every run. Ask for the pair instead - "
            + "`{Name:은}` - and KoreanParticle picks: "
            + string.Join(", ", sites));
    }

    /// <summary>
    /// Anything after the colon that is not a particle is written as it stands.
    /// </summary>
    /// <remarks>
    /// A typo in a catalog entry should be visible rather than swallowed. Dropping it would
    /// leave a sentence that reads fine and is missing a word.
    /// </remarks>
    [Fact]
    public void A_suffix_that_is_not_a_particle_is_left_where_it_is()
    {
        Assert.Equal(
            "int:xyz",
            Message.Fill("{Value:xyz}", new (string, object)[] { ("Value", "int") }));
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
    /// Asking for a language really answers in it, and an id it has no text for falls back.
    /// </summary>
    /// <remarks>
    /// The end of the whole design, checked in one place: a catalog is chosen, a report comes
    /// out in that language, an untranslated one comes out in English, and the run knows how
    /// many of the second there were. Without the count, "we decided to say that in English"
    /// and "nobody has translated it" look the same.
    /// </remarks>
    [Fact]
    public void A_chosen_language_answers_in_it_and_falls_back_where_it_cannot()
    {
        var korean = MessageCatalog.ForLanguage("ko");

        Assert.Equal("ko", korean.Language);

        // Something Korean has.
        string translated = Message.Of(
            Tabbit.Importers.ImportMessages.WorkbookFormatUnsupported,
            ("Filename", "a.txt")).In(korean);

        Assert.Contains("워크북", translated);
        Assert.Contains("a.txt", translated);

        // And a language with no catalog at all, which is what a typo for a language code
        // produces. Every key falls back and every fallback is counted - the run works and
        // says how much of it came out in English.
        //
        // Asked of a language nobody translates rather than of an id nobody has translated:
        // the second kind of example stops being one the moment somebody translates it, and
        // this test should not be the reason a catalog entry cannot be filled in.
        var untranslated = MessageCatalog.ForLanguage("qq-Fake");

        string fallen = untranslated.TextOf(Tabbit.ReportMessages.ProblemsCounted);

        Assert.Equal(MessageCatalog.English.TextOf(Tabbit.ReportMessages.ProblemsCounted), fallen);
        Assert.True(untranslated.Untranslated > 0);
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
    ///
    /// The suffix after a colon is not part of the name. Without that, `{Element:가}` named
    /// nothing here, and this check - the one a person cannot do by eye - was blind to every
    /// placeholder that asked for a Korean particle. Which is every placeholder that most
    /// needed watching.
    /// </remarks>
    private static HashSet<string> Placeholders(string text)
        => Regex.Matches(text, "(?<!\\{)\\{([A-Za-z0-9_]+)(?::[^{}]+)?\\}(?!\\})")
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

    private static string Listed(IEnumerable<string> names)
    {
        var ordered = names.OrderBy(name => name, StringComparer.Ordinal).ToList();

        return ordered.Count == 0 ? "none" : string.Join(", ", ordered);
    }
}
