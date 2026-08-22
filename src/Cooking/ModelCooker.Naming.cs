using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Helpers;
using Tabbit.Models;
using Tabbit.Messages;

namespace Tabbit.Cooking;

/// <summary>
/// Whether the names in the sheets are spelled the way the recipe says, and whether any of
/// them is spelled more than one way.
/// </summary>
/// <remarks>
/// Here rather than in the layout parsers, and once rather than per sheet, because the
/// subject is the model: the case worth reporting is one name written one way in one
/// workbook and another way in the next, which no parser can see from inside a single
/// sheet. Everything needed is already on the model - each name kept both as the sheet
/// spells it and as the generated code spells it, alongside the cell it came from.
///
/// Nothing here changes the model. A spelling the tool disagrees with is reported and
/// carried through as written, because the sheet is the original and a name whose word
/// boundaries have been lost cannot be recovered, only guessed at.
///
/// spec/naming-conventions.md.
/// </remarks>
public partial class ModelCooker
{
    /// <summary>One name as some sheet wrote it, and everything a check asks about it.</summary>
    private sealed class NameSite
    {
        public required NameKind Kind { get; init; }

        /// <summary>The name as written, with the secondary-index marker taken off.</summary>
        public required string Raw { get; init; }

        /// <summary>
        /// The parts judged separately. One element for every name but a nested field's,
        /// where each level is a name of its own and is spelled on its own.
        /// </summary>
        public required string[] Levels { get; init; }

        /// <summary>What the generated code calls it.</summary>
        public required string Normalized { get; init; }

        /// <summary>Cell it was written in.</summary>
        public required Location? Location { get; init; }

        /// <summary>What kind of thing this names, as a report opens with it.</summary>
        public required string KindWord { get; init; }

        /// <summary>
        /// What it belongs to, worded as a trailing clause - "table Item". Blank for an
        /// entity, which belongs to nothing.
        /// </summary>
        public required string Owner { get; init; }

        /// <summary>
        /// How a report names this, with <paramref name="subject"/> standing where the name
        /// goes.
        /// </summary>
        /// <remarks>
        /// Composed rather than concatenated so the name lands next to the word for what it
        /// is. Putting the owner first gave "Field of table Item MaxHitPoints", where two
        /// quoted names sit against each other and neither reads as the subject.
        /// </remarks>
        public Message Say(string subject)
            => Owner.Length == 0
                ? Message.Of(NamingMessages.SaidAlone, ("Kind", KindWord), ("Subject", subject))
                : Message.Of(NamingMessages.SaidOfOwner,
                    ("Kind", KindWord), ("Subject", subject), ("Owner", Owner));
    }

    internal static void ValidateNaming(Model model, NamingRules rules, Diagnostics diagnostics)
    {
        if (!rules.HasAnyCheck)
            return;

        var sites = CollectNameSites(model, rules);

        CheckDeclaredSpelling(sites, rules, diagnostics);
        CheckSpellingConflicts(sites, rules, diagnostics);
        CheckConsecutiveUnderscores(sites, rules, diagnostics);
    }

    /// <summary>
    /// Every name in the model that a sheet wrote, in declaration order.
    /// </summary>
    /// <remarks>
    /// Order matters to the reports: the checks below group and count, and a group's
    /// diagnostic names one spelling as the one to keep. Walking the model in its own order
    /// makes that answer the same on every run, which is what keeps a CI log from showing a
    /// change when nothing changed.
    /// </remarks>
    private static List<NameSite> CollectNameSites(Model model, NamingRules rules)
    {
        var sites = new List<NameSite>();

        void AddSimple(
            NameKind kind, string raw, string normalized, Location? location,
            string kindWord, string owner = "")
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0 || rules.IsExempt(text))
                return;

            sites.Add(new NameSite
            {
                Kind = kind,
                Raw = text,
                Levels = [text],
                Normalized = normalized,
                Location = location,
                KindWord = kindWord,
                Owner = owner,
            });
        }

        foreach (var table in model.Tables)
        {
            AddSimple(NameKind.Entity, table.RawName, table.Name, table.Location, "Table");

            foreach (var field in table.Fields)
            {
                // The marker comes off before anything is judged. `*` says the column is a
                // secondary index; it is not part of the name, and the generated member does
                // not carry it.
                string written = (field.RawName ?? "").Trim();
                string raw = written.StartsWith('*') ? written[1..].Trim() : written;

                if (raw.Length == 0)
                    continue;

                // Checked against both spellings so that listing `ItemId` covers the column
                // written `*ItemId`. The marker is not part of the name, so a recipe naming
                // one and not the other is not making a distinction.
                if (rules.IsExempt(raw) || rules.IsExempt(written))
                    continue;

                // `Slot1.Id` is two names, and each is spelled on its own - which is also
                // how the parser read it. Judging the whole string instead would call the
                // separator a word boundary and report a level that is spelled correctly.
                string[] levels = NestedName.LooksNested(raw)
                    ? raw.Split(NestedName.MemberSeparator).Select(part => part.Trim()).ToArray()
                    : [raw];

                sites.Add(new NameSite
                {
                    Kind = NameKind.Field,
                    Raw = raw,
                    Levels = levels,
                    Normalized = field.Name,
                    Location = field.NameLocation,
                    KindWord = "Field",
                    Owner = $"table `{table.Name}`",
                });
            }
        }

        foreach (var enumm in model.Enums)
        {
            // Declared by the cooker rather than by a sheet - the target list of a column
            // reaching several tables. Same reason the zero label below is skipped: there is
            // no cell holding this name.
            if (enumm.Synthesized)
                continue;

            AddSimple(NameKind.Entity, enumm.RawName, enumm.Name, enumm.Location, "Enum");

            foreach (var label in enumm.Labels)
            {
                // The zero label the tool inserts is not a spelling anybody chose, and it
                // carries the enum's own location, so a report about it would point at a
                // cell that holds no name.
                if (label.Synthesized)
                    continue;

                AddSimple(
                    NameKind.Label, label.RawName, label.Name, label.Location,
                    "Label", $"enum `{enumm.Name}`");
            }
        }

        foreach (var constantSet in model.ConstantSets)
        {
            AddSimple(
                NameKind.Entity, constantSet.RawName, constantSet.Name, constantSet.Location,
                "Constant set");

            foreach (var constant in constantSet.Constants)
            {
                AddSimple(
                    NameKind.Constant, constant.RawName, constant.Name, constant.Location,
                    "Constant", $"constant set `{constantSet.Name}`");
            }
        }

        return sites;
    }

    /// <summary>
    /// Reports a name that is not spelled the way its kind's declared spelling spells it.
    /// </summary>
    private static void CheckDeclaredSpelling(
        List<NameSite> sites, NamingRules rules, Diagnostics diagnostics)
    {
        foreach (var site in sites)
        {
            var declared = rules.DeclaredFor(site.Kind);
            if (declared is null)
                continue;

            foreach (string level in site.Levels)
            {
                if (NamingRules.Follows(level, declared.Value))
                    continue;

                string spelling = NamingRules.Spell(declared.Value);
                string wanted = level.ToCase(declared.Value);

                // A nested name says which level is at fault, because the others may be
                // spelled correctly and the author has to know which part of the cell to edit.
                // The two shapes are two ids rather than a clause spliced in: which one is
                // written depends on whether the name has levels, and a translator handed the
                // fragment could not know.
                bool inLevel = site.Levels.Length > 1;

                // Ends with the spelling to type, not with the verdict. A report that only
                // says a name is wrong leaves the reader to work out the casing rule and
                // apply it by hand, which is the work the tool has already done to decide
                // there was something to report.
                diagnostics.Add(rules.OnViolation, site.Location, inLevel
                    ? Message.Of(NamingMessages.SpellingViolationInLevel,
                        ("Said", site.Say($"`{site.Raw}`")), ("Level", level),
                        ("Spelling", spelling), ("Kinds", NamingRules.Describe(site.Kind)),
                        ("Wanted", wanted))
                    : Message.Of(NamingMessages.SpellingViolation,
                        ("Said", site.Say($"`{site.Raw}`")),
                        ("Spelling", spelling), ("Kinds", NamingRules.Describe(site.Kind)),
                        ("Wanted", wanted)));
            }
        }
    }

    /// <summary>
    /// Reports one name that the sheets write more than one way.
    /// </summary>
    /// <remarks>
    /// Runs whether or not a convention is declared, because it needs none: whichever
    /// spelling is right, writing a name two ways is a mistake. What it groups on is the
    /// letters and digits, so `maxHitPoints` and `max_hit_points` are recognized as the
    /// same name, and it reports a group only when the sheets disagree about how to spell
    /// it.
    ///
    /// Fields are grouped across tables rather than within one. Two tables spelling the
    /// same column differently is the case that costs a consumer the most - every place
    /// that reads it has to know which table spells it which way - and it is invisible to
    /// any check that looks at one table at a time.
    ///
    /// One report per group rather than per site: three spellings of a name are one thing
    /// to fix, and three separate reports read as three problems.
    /// </remarks>
    private static void CheckSpellingConflicts(
        List<NameSite> sites, NamingRules rules, Diagnostics diagnostics)
    {
        if (rules.OnSpellingConflict is null)
            return;

        var groups = new Dictionary<(NameKind, string), List<NameSite>>();
        var order = new List<(NameKind, string)>();

        foreach (var site in sites)
        {
            var key = (site.Kind, NamingRules.FoldKey(site.Raw));

            if (!groups.TryGetValue(key, out var members))
            {
                members = [];
                groups[key] = members;
                order.Add(key);
            }

            members.Add(site);
        }

        foreach (var key in order)
        {
            var members = groups[key];

            // Distinct spellings, each keeping the order it was first seen in and the site
            // that saw it, so the report can point at a cell for every one of them.
            var spellings = new List<(string Text, int Count, NameSite First)>();

            foreach (var member in members)
            {
                int at = spellings.FindIndex(s => string.Equals(s.Text, member.Raw, StringComparison.Ordinal));

                if (at < 0)
                    spellings.Add((member.Raw, 1, member));
                else
                    spellings[at] = (spellings[at].Text, spellings[at].Count + 1, spellings[at].First);
            }

            if (spellings.Count < 2)
                continue;

            string recommended = Recommend(spellings, rules, key.Item1);

            // Whether the generated code is split as well. Both are worth reporting and they
            // are not the same finding: one is a sheet that disagrees with itself, the other
            // is a name that has become two members every consumer has to know about.
            bool splitsOutput = members
                .Select(member => member.Normalized)
                .Distinct(StringComparer.Ordinal)
                .Count() > 1;

            string written = string.Join(", ", spellings.Select(
                s => $"`{s.Text}` ({Occurrences(s.Count)}, first at {s.First.Location})"));

            var consequence = Message.Of(splitsOutput
                ? NamingMessages.ConsequenceSplits
                : NamingMessages.ConsequenceSame);

            // A conflict the generated code carries weighs more than one it does not. Both
            // are reported - the sheets disagree either way, and the next spelling of the
            // name may be the one that splits it - but only the first is worth stopping a
            // build over, and `Info` is the level that says so: it is the one a
            // `TreatWarningsAsErrors` build cannot promote.
            var weight = splitsOutput
                ? rules.OnSpellingConflict.Value
                : Soften(rules.OnSpellingConflict.Value);

            // Pointed at a cell somebody has to edit, not at the one being recommended.
            // The message lists every spelling with its own location, so what this picks is
            // where an editor jumps to first - and jumping to the correct cell reads as the
            // report accusing it.
            var blame = spellings.FirstOrDefault(
                s => !string.Equals(s.Text, recommended, StringComparison.Ordinal),
                spellings[0]);

            // Says how much work the fix is, because that is the question the reader asks
            // next and the report is holding the answer: the other spellings are counted
            // above, so the number of cells to edit is already known.
            int toChange = spellings
                .Where(s => !string.Equals(s.Text, recommended, StringComparison.Ordinal))
                .Sum(s => s.Count);

            diagnostics.Add(weight, blame.First.Location,
                Message.Of(NamingMessages.SpellingConflict,
                    ("Subject", Subject(key.Item1)), ("Count", spellings.Count),
                    ("Spellings", written), ("Consequence", consequence),
                    ("Recommended", recommended), ("Places", Occurrences(toChange))));
        }
    }

    /// <summary>
    /// Which of a group's spellings the report asks for.
    /// </summary>
    /// <remarks>
    /// The declared convention wins over the count when there is one, because the majority
    /// is not evidence: a family of sheets copied from one another spreads a spelling as
    /// readily as it spreads a correct one, and a recipe that declares a convention has
    /// already answered the question. Without a convention there is nothing to go on but
    /// the count, and ties go to the spelling seen first so that the answer does not move
    /// between runs.
    /// </remarks>
    private static string Recommend(
        List<(string Text, int Count, NameSite First)> spellings, NamingRules rules, NameKind kind)
    {
        var declared = rules.DeclaredFor(kind);

        if (declared is not null)
        {
            var following = spellings
                .Where(s => s.Text.Split(NestedName.MemberSeparator)
                             .All(level => NamingRules.Follows(level.Trim(), declared.Value)))
                .ToList();

            if (following.Count > 0)
                return following.OrderByDescending(s => s.Count).First().Text;
        }

        return spellings.OrderByDescending(s => s.Count).First().Text;
    }

    /// <summary>
    /// Reports a name holding two or more underscores in a row.
    /// </summary>
    /// <remarks>
    /// The case rules read an interior underscore as a word boundary and keep no count, so
    /// `a_b`, `a__b` and `a___b` all arrive as one name. Nothing downstream can show the
    /// difference, which leaves two possibilities and no third: a typo, or an intention
    /// that was never delivered.
    ///
    /// It is also the one thing a declared convention cannot catch. `snake` is judged by
    /// spelling the name and comparing, and spelling `a__b` in snake case gives `a__b`
    /// back - interior underscores are preserved, so the round trip holds.
    ///
    /// Leading and trailing runs are left alone. Those survive into the generated code, so
    /// `_name` and `__name` are two names rather than two spellings of one, and a project
    /// using a leading underscore to mean something is not making a mistake.
    /// </remarks>
    private static void CheckConsecutiveUnderscores(
        List<NameSite> sites, NamingRules rules, Diagnostics diagnostics)
    {
        if (rules.OnConsecutiveUnderscores is null)
            return;

        foreach (var site in sites)
        {
            foreach (string level in site.Levels)
            {
                string interior = level.Trim('_');
                if (!interior.Contains("__", StringComparison.Ordinal))
                    continue;

                bool inLevel = site.Levels.Length > 1;

                string collapsed = CollapseUnderscoreRuns(site.Raw);

                diagnostics.Add(rules.OnConsecutiveUnderscores.Value, site.Location, inLevel
                    ? Message.Of(NamingMessages.ConsecutiveUnderscoresInLevel,
                        ("Said", site.Say($"`{site.Raw}`")), ("Level", level),
                        ("Collapsed", collapsed), ("Normalized", site.Normalized))
                    : Message.Of(NamingMessages.ConsecutiveUnderscores,
                        ("Said", site.Say($"`{site.Raw}`")),
                        ("Collapsed", collapsed), ("Normalized", site.Normalized)));
            }
        }
    }

    /// <summary>
    /// The same name with every interior run of underscores reduced to one, which is what
    /// the sheet would have to say for the name it produces to be the name it looks like.
    /// </summary>
    /// <remarks>
    /// Leading and trailing runs are left as written. Those reach the generated code, so
    /// collapsing them in a suggestion would be proposing a different name rather than the
    /// same name spelled unambiguously.
    /// </remarks>
    private static string CollapseUnderscoreRuns(string name)
    {
        int head = 0;
        while (head < name.Length && name[head] == '_')
            head++;

        int tail = name.Length;
        while (tail > head && name[tail - 1] == '_')
            tail--;

        var builder = new System.Text.StringBuilder(name.Length);
        builder.Append(name, 0, head);

        bool afterUnderscore = false;

        for (int at = head; at < tail; at++)
        {
            if (name[at] == '_')
            {
                if (afterUnderscore)
                    continue;

                afterUnderscore = true;
            }
            else
            {
                afterUnderscore = false;
            }

            builder.Append(name[at]);
        }

        builder.Append(name, tail, name.Length - tail);

        return builder.ToString();
    }

    /// <summary>
    /// One level down from what the recipe asked for, for the conflict whose spellings all
    /// reach the generated code as one name.
    /// </summary>
    /// <remarks>
    /// Graded rather than given a setting of its own, so the recipe keeps one dial and the
    /// rule reads in a sentence: a project raising the setting to `error` gets an error on
    /// the conflicts that split its output and a warning on the ones that do not.
    /// </remarks>
    private static Severity Soften(Severity severity)
        => severity == Severity.Error ? Severity.Warning : Severity.Info;

    /// <remarks>
    /// Four phrases rather than four whole sentences. What differs between them is one noun,
    /// and writing the sentence out four times would mean four places to keep in step every
    /// time it is reworded.
    /// </remarks>
    private static Message Subject(NameKind kind) => Message.Of(kind switch
    {
        NameKind.Entity => NamingMessages.SubjectEntity,
        NameKind.Field => NamingMessages.SubjectField,
        NameKind.Label => NamingMessages.SubjectLabel,
        _ => NamingMessages.SubjectConstant,
    });

    /// <remarks>
    /// The singular is the one place English needs a form the other four catalogs do not.
    /// Two ids keep that out of the sentence it sits in.
    /// </remarks>
    private static Message Occurrences(int count)
        => count == 1
            ? Message.Of(NamingMessages.PlacesOne)
            : Message.Of(NamingMessages.PlacesMany, ("Count", count));
}
