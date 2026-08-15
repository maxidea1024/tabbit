using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Tabbit.History;

/// <summary>
/// What has to ship to make a snapshot's changes reach a running game: the data files,
/// the generated code, or both.
///
/// The question every entry in the history quietly raises is "so what do I deploy?",
/// and the answer is not guessable from the change list unless the reader already knows
/// which entities live in the data and which live in the code. A cell edit rides on a
/// data patch. A constant does not ride on anything but a build - it exists only as a
/// declaration in generated source, and exporting data carries none of it. An enum sits
/// across the line: its labels' names are compiled into every build, but the numbers
/// behind them are already written into every exported row.
///
/// Computed when the history is read rather than stored when it is written, so a
/// snapshot recorded before this existed gets a verdict too, and a rule learned later
/// corrects the past instead of freezing the mistake into it.
/// </summary>
public sealed class DeploymentAdvice
{
    /// <summary>Whether the exported data files changed and have to go out.</summary>
    public bool Data { get; set; }

    /// <summary>Whether generated code changed in a way a data patch cannot carry.</summary>
    public bool Code { get; set; }

    /// <summary>Why, one short phrase per cause. What the flags alone do not say.</summary>
    public IReadOnlyList<string> Reasons { get; set; } = [];

    /// <summary>
    /// The changes that go wrong quietly rather than loudly.
    ///
    /// Everything here shares one property: nothing fails. The conversion succeeds, the
    /// files load, and the mistake surfaces as a player report. That is exactly the
    /// kind of thing a report has to say out loud, because no other tool in the
    /// pipeline is in a position to.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];

    /// <summary>
    /// Reasons are capped here and the rest folded into "+N more" - a first snapshot
    /// declares every entity at once, and a verdict is not a change list.
    /// </summary>
    private const int ReasonLimit = 12;

    /// <summary>
    /// Judges one snapshot's schema changes.
    /// </summary>
    /// <param name="schema">
    /// Every schema change of the snapshot - not a page of them. A verdict computed
    /// from a truncated list would report "data only" for a commit whose enum change
    /// fell off the end.
    /// </param>
    /// <param name="dataMoved">Whether any row or cell changed.</param>
    /// <param name="enumsInUse">
    /// The enums some column is actually typed with. An enum no column uses has no
    /// values in any exported row, so nothing about changing it - renumbering
    /// included - touches data, and warning about re-exports would be crying wolf.
    /// Its declaration is still generated code, so the code flag stands either way.
    /// </param>
    /// <returns>Null when nothing changed at all - there is nothing to advise on.</returns>
    public static DeploymentAdvice? Compute(
        IReadOnlyList<SchemaChangeView> schema, bool dataMoved, ISet<string> enumsInUse)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(enumsInUse);

        if (schema.Count == 0 && !dataMoved)
            return null;

        var advice = new DeploymentAdvice();
        var reasons = new List<string>();
        var warnings = new List<string>();

        JudgeTables(schema, advice, reasons, warnings);
        JudgeColumns(schema, advice, reasons);
        JudgeEnums(schema, advice, reasons, warnings, dataMoved, enumsInUse);
        JudgeConstants(schema, advice, reasons);

        if (dataMoved)
        {
            advice.Data = true;

            // Only worth a line when it is not already obvious from the schema ones.
            if (reasons.Count == 0)
                reasons.Add("row or cell values changed");
        }

        advice.Reasons = Cap(reasons);
        advice.Warnings = warnings;

        return advice;
    }

    /// <summary>
    /// One verdict for a whole range: the union of its snapshots'.
    ///
    /// Shipping a range means shipping everything any snapshot in it needs, which is
    /// why this is a union and not a summary of the last one.
    /// </summary>
    public static DeploymentAdvice? Merge(IEnumerable<DeploymentAdvice?> advices)
    {
        ArgumentNullException.ThrowIfNull(advices);

        var parts = advices.Where(a => a is not null).Select(a => a!).ToList();

        if (parts.Count == 0)
            return null;

        return new DeploymentAdvice
        {
            Data = parts.Any(p => p.Data),
            Code = parts.Any(p => p.Code),
            Reasons = Cap(Distinct(parts.SelectMany(p => p.Reasons))),
            Warnings = Distinct(parts.SelectMany(p => p.Warnings)),
        };
    }

    // ---------------------------------------------------------------- judging

    private static void JudgeTables(
        IReadOnlyList<SchemaChangeView> schema,
        DeploymentAdvice advice,
        List<string> reasons,
        List<string> warnings)
    {
        foreach (var change in OfKind(schema, "Table"))
        {
            // A table is a data file and a reader class, so either way both move.
            advice.Data = true;
            advice.Code = true;

            if (change.Kind == "Added")
            {
                reasons.Add($"table {change.Entity} added - its reader class is new");
                continue;
            }

            reasons.Add($"table {change.Entity} removed");

            // The old build's load reads a fixed list of files, and this one is on it.
            warnings.Add(
                $"table {change.Entity} removed: builds generated before this still ask " +
                $"for its file when they load.");
        }
    }

    private static void JudgeColumns(
        IReadOnlyList<SchemaChangeView> schema, DeploymentAdvice advice, List<string> reasons)
    {
        foreach (var change in OfKind(schema, "Field"))
        {
            if (change.Kind == "Added")
            {
                advice.Data = true;
                reasons.Add($"column {change.Entity}.{change.Member} added");
                continue;
            }

            if (change.Kind == "Removed")
            {
                advice.Data = true;
                reasons.Add($"column {change.Entity}.{change.Member} removed");
                continue;
            }

            if (change.RenamedFrom is not null)
            {
                // A rename is the tag system's showcase: the file identifies columns by
                // number, so deployed readers never notice.
                advice.Data = true;

                reasons.Add(
                    $"column {change.Entity}.{change.RenamedFrom} renamed to {change.Member}");

                continue;
            }

            // Modified: the descriptor says in what way, and the way decides everything.
            // A type or side change reshapes both the file and the generated member;
            // a comment is neither, and forces nothing out the door.
            var before = Descriptor(change.Before);
            var after = Descriptor(change.After);

            string? was = (string?)before?["type"];
            string? now = (string?)after?["type"];

            if (!string.Equals(was, now, StringComparison.Ordinal))
            {
                advice.Data = true;
                advice.Code = true;

                reasons.Add(
                    $"column {change.Entity}.{change.Member}: type {was ?? "?"} -> {now ?? "?"} " +
                    $"- readers built before this refuse the new data");

                continue;
            }

            if (!string.Equals((string?)before?["side"], (string?)after?["side"], StringComparison.Ordinal))
            {
                advice.Data = true;
                advice.Code = true;

                reasons.Add(
                    $"column {change.Entity}.{change.Member}: side changed - it enters or " +
                    $"leaves one side's data and code");

                continue;
            }

            if (!string.Equals(Reference(before), Reference(after), StringComparison.Ordinal))
            {
                advice.Data = true;
                advice.Code = true;
                reasons.Add($"column {change.Entity}.{change.Member}: reference changed");
            }
        }
    }

    private static void JudgeEnums(
        IReadOnlyList<SchemaChangeView> schema,
        DeploymentAdvice advice,
        List<string> reasons,
        List<string> warnings,
        bool dataMoved,
        ISet<string> enumsInUse)
    {
        // Judged per enum rather than per label, because the verdict reads per enum:
        // "Grade changed" is what somebody ships against, not its fourth label.
        var byEnum = schema
            .Where(c => c.EntityKind is "Enum" or "EnumLabel")
            .GroupBy(c => c.Entity, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in byEnum)
        {
            // Whatever happened to it, its declaration is in every generated language.
            advice.Code = true;

            int added = group.Count(c => c.EntityKind == "EnumLabel" && c.Kind == "Added");
            int removed = group.Count(c => c.EntityKind == "EnumLabel" && c.Kind == "Removed");
            int renumbered = group.Count(c => c.EntityKind == "EnumLabel" && c.Kind == "Modified");

            if (group.Any(c => c.EntityKind == "Enum" && c.Kind == "Removed"))
            {
                reasons.Add($"enum {group.Key} removed");
                continue;
            }

            var moves = new List<string>();

            if (added > 0) moves.Add($"{added} label(s) added");
            if (removed > 0) moves.Add($"{removed} removed");
            if (renumbered > 0) moves.Add($"{renumbered} renumbered");

            string what = moves.Count > 0 ? string.Join(", ", moves) : "added";

            // No column is typed with it, so no exported row holds its values. Every
            // hazard below is about numbers already out in the data; with none out
            // there, even renumbering is just a code edit.
            if (!enumsInUse.Contains(group.Key))
            {
                reasons.Add($"enum {group.Key}: {what} (no column uses it)");
                continue;
            }

            reasons.Add($"enum {group.Key}: {what}");

            if (renumbered > 0)
            {
                // The one change nothing rejects. Every shifted number is still a
                // declared value, so old data reads cleanly into the wrong labels.
                advice.Data = true;

                warnings.Add(
                    $"enum {group.Key}: existing label values changed. Rows exported before " +
                    $"this carry numbers that now name a different label - re-export all " +
                    $"data and ship it together with the code.");
            }

            if (removed > 0)
            {
                warnings.Add(
                    $"enum {group.Key}: label(s) removed. Rolling data back past this point " +
                    $"revives values this build no longer names.");
            }

            if (added > 0 && dataMoved)
            {
                warnings.Add(
                    $"enum {group.Key}: labels added while data changed in the same " +
                    $"conversion. If rows already use the new values, deploy this code " +
                    $"before that data reaches builds that lack the labels.");
            }
        }
    }

    private static void JudgeConstants(
        IReadOnlyList<SchemaChangeView> schema, DeploymentAdvice advice, List<string> reasons)
    {
        var names = schema
            .Where(c => c.EntityKind is "Constants" or "Constant")
            .Select(c => c.Entity)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in names)
        {
            advice.Code = true;

            // Said in full every time, because this is the change most often believed
            // shipped when it was not: nothing about a constant reaches a data file,
            // so exporting after the edit changes nothing anywhere.
            reasons.Add(
                $"constant set {name} changed - constants exist only in generated code, " +
                $"a data patch carries none of this");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<SchemaChangeView> OfKind(
        IReadOnlyList<SchemaChangeView> schema, string kind)
        => schema.Where(c => string.Equals(c.EntityKind, kind, StringComparison.Ordinal));

    private static JObject? Descriptor(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            return JObject.Parse(text);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            // Not this schema's JSON - an older record, perhaps. Unreadable attributes
            // cannot prove a type change, and inventing one would flag code deploys at
            // random.
            return null;
        }
    }

    private static string? Reference(JObject? descriptor)
    {
        if (descriptor is null)
            return null;

        string? table = (string?)descriptor["refTable"];

        return table is null ? null : table + "." + (string?)descriptor["refField"];
    }

    private static List<string> Distinct(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var item in items)
        {
            if (seen.Add(item))
                result.Add(item);
        }

        return result;
    }

    private static IReadOnlyList<string> Cap(List<string> reasons)
    {
        if (reasons.Count <= ReasonLimit)
            return reasons;

        int hidden = reasons.Count - ReasonLimit;

        return reasons.Take(ReasonLimit).Append($"+{hidden} more").ToList();
    }
}
