using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Recipe;

namespace Tabbit.Cooking;

/// <summary>
/// Which of the model's names a spelling rule applies to.
/// </summary>
/// <remarks>
/// Tables, enums and constant sets are one kind rather than three: they name types in the
/// same generated namespace, so a rule that told them apart would be describing a
/// distinction the output does not have.
/// </remarks>
public enum NameKind
{
    Entity,
    Field,
    Label,
    Constant,
}

/// <summary>
/// The naming section of a recipe, read once and turned into the questions the check asks.
/// </summary>
/// <remarks>
/// Read before any name is judged, so a misspelled setting is reported on its own rather
/// than as a verdict about whichever name reached it first - the same reason the sheet
/// import settings are read per entry instead of per sheet.
///
/// spec/naming-conventions.md.
/// </remarks>
public sealed class NamingRules
{
    private readonly Dictionary<NameKind, NameCase> _declared;
    private readonly HashSet<string> _exempt;

    private NamingRules(
        Dictionary<NameKind, NameCase> declared,
        Severity onViolation,
        Severity? onSpellingConflict,
        Severity? onConsecutiveUnderscores,
        HashSet<string> exempt)
    {
        _declared = declared;
        _exempt = exempt;
        OnViolation = onViolation;
        OnSpellingConflict = onSpellingConflict;
        OnConsecutiveUnderscores = onConsecutiveUnderscores;
    }

    /// <summary>How much a name that breaks its kind's declared spelling weighs.</summary>
    public Severity OnViolation { get; }

    /// <summary>
    /// How much one name written several ways weighs, or null when the recipe switched the
    /// check off.
    /// </summary>
    public Severity? OnSpellingConflict { get; }

    /// <summary>
    /// How much an interior run of underscores weighs, or null when the recipe switched the
    /// check off.
    /// </summary>
    public Severity? OnConsecutiveUnderscores { get; }

    /// <summary>Whether anything here has a question to ask.</summary>
    public bool HasAnyCheck =>
        _declared.Count > 0 || OnSpellingConflict is not null || OnConsecutiveUnderscores is not null;

    /// <summary>The spelling this kind has to follow, or null when none was declared.</summary>
    public NameCase? DeclaredFor(NameKind kind)
        => _declared.TryGetValue(kind, out var value) ? value : null;

    /// <summary>Whether the recipe listed this spelling as one not to judge.</summary>
    /// <remarks>
    /// Matched against the name as the sheet spells it, and exactly. The list is how a
    /// model with a history says "these are the ones I already have", and a name that
    /// matched it loosely would take unwritten ones with it.
    /// </remarks>
    public bool IsExempt(string rawName) => _exempt.Contains(rawName);

    /// <summary>
    /// Whether a name is already spelled the way <paramref name="nameCase"/> spells it.
    /// </summary>
    /// <remarks>
    /// Asked as a round trip - spell it and see whether anything moved - rather than with a
    /// pattern of its own. A pattern would be a second opinion about where the words in a
    /// name are, and the two would disagree about acronyms the first time somebody wrote
    /// one: `HTTPServer` is two words to the case rules, and the rule that judges it has to
    /// agree with the rule that will rewrite it.
    /// </remarks>
    public static bool Follows(string name, NameCase nameCase)
        => string.Equals(name.ToCase(nameCase), name, StringComparison.Ordinal);

    /// <summary>
    /// What two spellings of one name have in common: the letters and digits, in order.
    /// </summary>
    /// <remarks>
    /// Case folded and the word separators dropped, so `maxHitPoints`, `MaxHitPoints` and
    /// `max_hit_points` all fold together. The nesting separator is kept, because
    /// `Slot.Id` and `SlotId` are a record member and a field rather than one name written
    /// twice.
    /// </remarks>
    public static string FoldKey(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);

        foreach (char symbol in name)
        {
            if (symbol is '_' or '-')
                continue;

            builder.Append(char.ToLowerInvariant(symbol));
        }

        return builder.ToString();
    }

    /// <summary>How a spelling is written in a report.</summary>
    public static string Spell(NameCase nameCase) => nameCase switch
    {
        NameCase.Camel => "camel",
        NameCase.Pascal => "pascal",
        NameCase.Snake => "snake",
        NameCase.UpperSnake => "upper-snake",
        _ => nameCase.ToString().ToLowerInvariant(),
    };

    /// <summary>How a kind is written in a report.</summary>
    public static string Describe(NameKind kind) => kind switch
    {
        NameKind.Entity => "table, enum and constant-set names",
        NameKind.Field => "field names",
        NameKind.Label => "enum labels",
        _ => "constant names",
    };

    /// <summary>
    /// Reads the section, rejecting values that are not spellings of anything.
    /// </summary>
    public static NamingRules From(NamingRecipe? recipe)
    {
        if (recipe is null)
            return new NamingRules([], Severity.Error, Severity.Warning, Severity.Warning, []);

        var declared = new Dictionary<NameKind, NameCase>();

        Declare(declared, NameKind.Entity, recipe.Entity, nameof(recipe.Entity));
        Declare(declared, NameKind.Field, recipe.Field, nameof(recipe.Field));
        Declare(declared, NameKind.Label, recipe.Label, nameof(recipe.Label));
        Declare(declared, NameKind.Constant, recipe.Constant, nameof(recipe.Constant));

        return new NamingRules(
            declared,
            ParseViolationSeverity(recipe.OnViolation),
            ParseReportSeverity(recipe.OnSpellingConflict, nameof(recipe.OnSpellingConflict)),
            ParseReportSeverity(recipe.OnConsecutiveUnderscores, nameof(recipe.OnConsecutiveUnderscores)),

            // Ordinal: these are spellings, and the whole subject here is that two
            // spellings differing only in case are two different things.
            new HashSet<string>(
                (recipe.Exempt ?? []).Select(name => (name ?? "").Trim()).Where(name => name.Length > 0),
                StringComparer.Ordinal));
    }

    private static void Declare(
        Dictionary<NameKind, NameCase> into, NameKind kind, string value, string key)
    {
        // Blank is "not checked" rather than an error, which is what a recipe written
        // before this section existed holds and what deleting the line leaves behind.
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return;

        // Hyphen and underscore taken as the same separator, as `OnDuplicateIndex` does:
        // `upper_snake` is nobody's mistake.
        switch (text.ToLowerInvariant().Replace("_", "-"))
        {
            case "pascal": into[kind] = NameCase.Pascal; return;
            case "camel": into[kind] = NameCase.Camel; return;
            case "snake": into[kind] = NameCase.Snake; return;
            case "upper-snake": into[kind] = NameCase.UpperSnake; return;
        }

        throw new TabbitException(
            $"Recipe `Naming` sets `{key}` to `{text}`. " +
            "It takes `pascal`, `camel`, `snake` or `upper-snake`.");
    }

    private static Severity ParseViolationSeverity(string value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return Severity.Error;

        switch (text.ToLowerInvariant())
        {
            case "error": return Severity.Error;
            case "warn": return Severity.Warning;
        }

        throw new TabbitException(
            $"Recipe `Naming` sets `OnViolation` to `{text}`. " +
            "It takes `error` or `warn`. To leave a kind of name unchecked, leave its " +
            "spelling blank instead.");
    }

    private static Severity? ParseReportSeverity(string value, string key)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return Severity.Warning;

        switch (text.ToLowerInvariant())
        {
            case "error": return Severity.Error;
            case "warn": return Severity.Warning;
            case "ignore": return null;
        }

        throw new TabbitException(
            $"Recipe `Naming` sets `{key}` to `{text}`. " +
            "It takes `error`, `warn` or `ignore`.");
    }
}
