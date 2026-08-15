using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tabbit.History;

/// <summary>
/// A report as plain text, for a terminal.
///
/// The JSON is the document; this is a rendering of it and adds nothing. Anything a
/// reader needs that is not here is missing from the document rather than from the
/// formatting, which is the property that keeps the terminal, the page and the API
/// saying the same thing.
/// </summary>
internal static class HistoryText
{
    public static string Render(HistoryDocument document)
    {
        var text = new StringBuilder();

        var query = document.Query;

        text.AppendLine($"{query.Project} / {query.Branch}");
        text.AppendLine($"  {query.From ?? "(start)"} .. {query.To ?? "(head)"}"
                        + Filters(query));
        text.AppendLine();

        if (document.Snapshots.Count == 0)
        {
            text.AppendLine("No snapshots in this range.");
            return text.ToString();
        }

        foreach (var snapshot in document.Snapshots)
            RenderSnapshot(text, snapshot);

        var totals = document.Totals;

        text.AppendLine(
            $"{totals?.Snapshots} snapshot(s), {totals?.Schema} schema, " +
            $"{totals?.Rows} row and {totals?.Cells} cell change(s).");

        RenderDeployment(text, document.Deployment);

        if (totals?.Gaps > 0)
        {
            // Said plainly, because the alternative is a reader crediting one person
            // with several people's work.
            text.AppendLine(
                $"{totals.Gaps} of them cover more than their own commit: nothing converted the " +
                $"commits in between, so those changes are attributed to whoever's conversion " +
                $"finally recorded them.");
        }

        if (totals?.Pruned > 0)
        {
            text.AppendLine(
                $"{totals.Pruned} of them have had their change detail pruned, so what they " +
                $"changed is no longer recorded. Their statistics are.");
        }

        foreach (var note in query.Notes ?? System.Array.Empty<string>())
            text.AppendLine(note);

        if (query.Truncated)
        {
            text.AppendLine(
                $"{query.Omitted} further change(s) were left out by --limit {query.Limit}.");
        }

        return text.ToString();
    }

    private static string Filters(HistoryQueryInfo query)
    {
        var filters = new List<string>();

        if (query.Table is not null) filters.Add($"table {query.Table}");
        if (query.Field is not null) filters.Add($"field {query.Field}");
        if (query.Author is not null) filters.Add($"author {query.Author}");

        return filters.Count == 0 ? "" : "   [" + string.Join(", ", filters) + "]";
    }

    private static void RenderSnapshot(StringBuilder text, HistorySnapshotView snapshot)
    {
        text.Append($"{snapshot.ShortCommit}  {Day(snapshot.CommittedAt)}  ");
        text.AppendLine(Who(snapshot));

        if (!string.IsNullOrEmpty(snapshot.Subject))
            text.AppendLine($"    {snapshot.Subject}");

        if (!snapshot.Attributable)
        {
            text.AppendLine(snapshot.Dirty
                ? "    ! recorded from a working copy with uncommitted changes; not attributable"
                : "    ! not attributable");
        }

        if (snapshot.Pruned)
        {
            text.AppendLine(
                "    ! this snapshot's change detail was pruned; its statistics are still here");
        }

        if (!snapshot.FollowsParent && snapshot.PreviousCommit is not null)
        {
            text.AppendLine(
                $"    ! measured from {Short(snapshot.PreviousCommit)}, which is not this commit's " +
                $"parent - the commits in between were never converted");
        }

        if (snapshot.Deployment is not null)
        {
            text.AppendLine($"    => ship: {ShipPhrase(snapshot.Deployment)}");

            foreach (var warning in snapshot.Deployment.Warnings)
                text.AppendLine($"    ! {warning}");
        }

        // A renamed column moves every one of its cells, and none of that is an edit
        // anybody made. Counted on the rename's own line rather than listed.
        var renamed = new HashSet<(string?, string?)>();

        foreach (var change in snapshot.Schema.Where(c => c.RenamedFrom is not null))
        {
            renamed.Add((change.Entity, change.RenamedFrom));
            renamed.Add((change.Entity, change.Member));
        }

        foreach (var change in snapshot.Schema)
        {
            if (change.RenamedFrom is not null)
            {
                int carried = snapshot.Cells.Count(
                    c => c.Table == change.Entity && c.Field == change.Member);

                text.AppendLine(
                    $"    ~ field      {change.Entity}.{change.RenamedFrom} -> {change.Member}" +
                    $"  (renamed, {carried} row(s) carried over)");

                continue;
            }

            string what = change.Member is null
                ? $"{change.Entity}"
                : $"{change.Entity}.{change.Member}";

            text.AppendLine($"    {Mark(change.Kind)} {change.EntityKind,-10} {what}"
                            + Transition(change.Before, change.After));
        }

        foreach (var change in snapshot.Cells)
        {
            if (renamed.Contains((change.Table, change.Field)))
                continue;

            text.AppendLine(
                $"    {Mark(change.Kind)} {change.Table}[{change.RowKey}].{change.Field}"
                + Transition(change.Before, change.After)
                + At(change.Location));
        }

        // Rows whose cells are all elsewhere in the list would be repetition; only the
        // ones with no cell change of their own are worth a line.
        var cellRows = new HashSet<(string?, string?)>(
            snapshot.Cells.Select(c => ((string?)c.Table, (string?)c.RowKey)));

        foreach (var row in snapshot.Rows.Where(r => !cellRows.Contains(((string?)r.Table, (string?)r.RowKey))))
            text.AppendLine($"    {Mark(row.Kind)} {row.Table}[{row.RowKey}]  (row {row.Kind.ToLowerInvariant()})");

        text.AppendLine();
    }

    /// <summary>
    /// The range's verdict, at the end where a reader looks for the conclusion.
    ///
    /// The per-snapshot lines say what each one needs; this says what the range needs,
    /// which is the question "to go from A to B, what do I deploy?" asked directly.
    /// </summary>
    private static void RenderDeployment(StringBuilder text, DeploymentAdvice? advice)
    {
        if (advice is null)
            return;

        text.AppendLine();
        text.AppendLine($"To ship this range: {ShipPhrase(advice)}");

        foreach (var reason in advice.Reasons)
            text.AppendLine($"  - {reason}");

        foreach (var warning in advice.Warnings)
            text.AppendLine($"  ! {warning}");
    }

    private static string ShipPhrase(DeploymentAdvice advice)
    {
        if (advice.Data && advice.Code) return "data + code";
        if (advice.Data) return "data only";
        if (advice.Code) return "code only - a data patch carries none of this";

        return "nothing - cosmetic change only";
    }

    private static string Who(HistorySnapshotView snapshot)
    {
        if (snapshot.AuthorName is null)
            return "(unknown author)";

        return snapshot.AuthorEmail is null
            ? snapshot.AuthorName
            : $"{snapshot.AuthorName} <{snapshot.AuthorEmail}>";
    }

    private static string Mark(string? kind)
    {
        return kind switch
        {
            "Added" => "+",
            "Removed" => "-",
            _ => "~",
        };
    }

    private static string Transition(string? before, string? after)
    {
        if (before is null && after is null)
            return "";

        if (before is null)
            return $"  {Value(after)}";

        if (after is null)
            return $"  {Value(before)} -> (blank)";

        return $"  {Value(before)} -> {Value(after)}";
    }

    /// <summary>
    /// A value, shortened when it is long enough to swamp the line.
    ///
    /// The cut is marked, so nobody reads a truncated value as the value.
    /// </summary>
    private static string Value(string? text)
    {
        const int Limit = 60;

        if (text is null)
            return "(blank)";

        string single = text.Replace("\r", "").Replace("\n", "\\n");

        return single.Length <= Limit ? single : single.Substring(0, Limit) + "…";
    }

    private static string At(SummaryLocation? location)
        => location?.Sheet is null ? "" : $"    {location.File} : {location.Sheet} : {location.Cell}";

    // -------------------------------------------------------------- statistics

    public static string Render(SummaryDocument summary, string branch)
    {
        var text = new StringBuilder();
        var commit = summary.Run.Commit;
        var totals = summary.Data.Totals;

        text.AppendLine($"{commit.ShortHash ?? "(unidentified)"} on {branch}  {Day(commit.CommittedAt)}");

        if (!string.IsNullOrEmpty(commit.Subject))
            text.AppendLine($"  {commit.Subject}");

        if (commit.AuthorName is not null)
            text.AppendLine($"  by {commit.AuthorName}");

        text.AppendLine();

        text.AppendLine($"  {totals.Tables,8:N0} tables");
        text.AppendLine($"  {totals.Rows,8:N0} rows");
        text.AppendLine($"  {totals.Fields,8:N0} columns");
        text.AppendLine($"  {totals.Cells,8:N0} cells ({totals.EmptyCells:N0} blank)");
        text.AppendLine($"  {totals.ContentBytes,8:N0} bytes of values");
        text.AppendLine($"  {totals.Enums,8:N0} enums ({totals.EnumLabels:N0} labels)");
        text.AppendLine($"  {totals.ConstantSets,8:N0} constant sets ({totals.Constants:N0} constants)");
        text.AppendLine($"  {totals.ReferenceFields,8:N0} reference columns, {totals.ArrayFields:N0} array columns");
        text.AppendLine();

        int width = Math.Max(5, summary.Data.Tables.Max(t => t.Name.Length));

        text.AppendLine($"  {"table".PadRight(width)}  {"rows",8}  {"cols",5}  {"blank",8}  {"bytes",10}");

        foreach (var table in summary.Data.Tables.OrderByDescending(t => t.RowCount))
        {
            text.AppendLine(
                $"  {table.Name.PadRight(width)}  {table.RowCount,8:N0}  {table.FieldCount,5:N0}  " +
                $"{table.EmptyCellCount,8:N0}  {table.ContentBytes,10:N0}");
        }

        return text.ToString();
    }

    private static string Day(string? iso)
    {
        if (string.IsNullOrEmpty(iso))
            return "(no date)";

        return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                                       DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : iso;
    }

    private static string? Short(string? hash)
        => hash is null ? null : hash.Substring(0, Math.Min(12, hash.Length));
}
