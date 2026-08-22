using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MySqlConnector;
using Serilog;
using Tabbit.Messages;

namespace Tabbit.History;

/// <summary>What a prune removed.</summary>
public sealed class PruneResult
{
    public int Snapshots { get; set; }
    public long SchemaChanges { get; set; }
    public long RowChanges { get; set; }
    public long CellChanges { get; set; }
    public long Values { get; set; }

    public override string ToString()
        => $"{Snapshots} snapshot(s): {SchemaChanges} schema, {RowChanges} row and " +
           $"{CellChanges} cell change(s), and {Values} unreferenced value(s).";
}

/// <summary>
/// Reclaims space from a history that has been running for a long time.
///
/// What grows without bound is the change log: one row per edited cell per commit, for
/// ever. The value pool does not - it is addressed by content, so it is bounded by how
/// many *different* values a project has ever held, and that stops growing once the
/// vocabulary settles. Collecting it is worth doing only after a prune, which is when
/// values stop being referenced.
///
/// A pruned snapshot keeps its row, its statistics and its stored summary. Only the
/// cell-by-cell detail goes, and it is marked so a query over a range holding one says
/// the detail was removed rather than reporting no changes.
/// </summary>
internal static class HistoryMaintenance
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Recording;

    /// <summary>Values examined per statement, so one delete cannot lock the pool.</summary>
    private const int CollectBatch = 1000;

    /// <summary>
    /// Removes the change detail of snapshots older than a cutoff, then collects the
    /// values that nothing refers to any more.
    /// </summary>
    /// <param name="keep">
    /// How many of the branch's most recent snapshots to leave alone, whatever their
    /// age. A cutoff on its own can empty a branch that has been quiet.
    /// </param>
    public static PruneResult Prune(
        MySqlConnection connection, string project, string branch, DateTime? before, int keep)
    {
        int projectId = ProjectId(connection, project)
                ?? throw new TabbitException(null,
                    Message.Of(RecordMessages.ProjectUnknown, ("Project", project)));

        string lockName = HistorySchema.WriteLockFor(projectId, branch ?? "");

        HistorySchema.Lock(connection, lockName);

        try
        {
            var doomed = Doomed(connection, projectId, branch ?? "", before, keep);

            var result = new PruneResult { Snapshots = doomed.Count };

            if (doomed.Count == 0)
            {
                Log.Information("Nothing to prune.");
                return result;
            }

            using (var transaction = connection.BeginTransaction())
            {
                result.SchemaChanges = DeleteChanges(connection, transaction, "schema_change", doomed);
                result.RowChanges = DeleteChanges(connection, transaction, "row_change", doomed);
                result.CellChanges = DeleteChanges(connection, transaction, "cell_change", doomed);

                Mark(connection, transaction, doomed);

                transaction.Commit();
            }

            // After the detail is gone, and inside the same lock, so a conversion cannot
            // be part-way through referencing a value this is about to remove.
            result.Values = Collect(connection);

            Log.Information($"Pruned {result}");

            return result;
        }
        finally
        {
            HistorySchema.Unlock(connection, lockName);
        }
    }

    private static int? ProjectId(MySqlConnection connection, string project)
    {
        using var command = new MySqlCommand(
            "SELECT id FROM project WHERE project_key = @key", connection);

        command.Parameters.AddWithValue("@key", project);

        var id = command.ExecuteScalar();

        return id is null || id == DBNull.Value ? (int?)null : Convert.ToInt32(id);
    }

    /// <summary>
    /// The snapshots whose detail may go: old enough, and not among the most recent.
    /// </summary>
    private static List<long> Doomed(
        MySqlConnection connection, int projectId, string branch, DateTime? before, int keep)
    {
        var conditions = new List<string>
        {
            "project_id = @project", "branch = @branch", "pruned = 0",
        };

        if (before is not null)
            conditions.Add("COALESCE(committed_at, converted_at) < @before");

        // `keep` is a floor under the cutoff rather than an alternative to it: a branch
        // nobody has touched for a year would otherwise lose every snapshot's detail
        // and become a history with no history in it.
        string sql = $@"
            SELECT id FROM snapshot
            WHERE {string.Join(" AND ", conditions)}
              AND seq <= (
                  SELECT COALESCE(MAX(seq), 0) - @keep FROM snapshot
                  WHERE project_id = @project AND branch = @branch)
            ORDER BY seq";

        using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@project", projectId);
        command.Parameters.AddWithValue("@branch", branch);
        command.Parameters.AddWithValue("@keep", Math.Max(0, keep));

        if (before is not null)
            command.Parameters.AddWithValue("@before", before.Value);

        var ids = new List<long>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        return ids;
    }

    private static long DeleteChanges(
        MySqlConnection connection, MySqlTransaction transaction, string table, IReadOnlyList<long> snapshots)
    {
        long deleted = 0;

        foreach (var chunk in Chunk(snapshots, 200))
        {
            using var command = new MySqlCommand { Connection = connection, Transaction = transaction };

            var names = new List<string>(chunk.Count);

            for (int i = 0; i < chunk.Count; i++)
            {
                names.Add("@s" + i);
                command.Parameters.AddWithValue("@s" + i, chunk[i]);
            }

            command.CommandText =
                $"DELETE FROM {table} WHERE snapshot_id IN ({string.Join(", ", names)})";

            deleted += command.ExecuteNonQuery();
        }

        return deleted;
    }

    private static void Mark(
        MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<long> snapshots)
    {
        foreach (var chunk in Chunk(snapshots, 200))
        {
            using var command = new MySqlCommand { Connection = connection, Transaction = transaction };

            var names = new List<string>(chunk.Count);

            for (int i = 0; i < chunk.Count; i++)
            {
                names.Add("@s" + i);
                command.Parameters.AddWithValue("@s" + i, chunk[i]);
            }

            command.CommandText =
                $"UPDATE snapshot SET pruned = 1 WHERE id IN ({string.Join(", ", names)})";

            command.ExecuteNonQuery();
        }
    }

    /// <summary>MySQL's error for a delete a foreign key will not allow.</summary>
    private const int RowIsStillReferenced = 1451;

    /// <summary>
    /// Deletes values nothing refers to.
    ///
    /// Bounded by a watermark taken first, so a value a conversion inserts while this
    /// runs - which gets a higher id - is out of reach.
    ///
    /// That leaves the values already in the pool, and the write lock does not cover
    /// them: it is named per branch, while the pool is shared by every project and
    /// branch. So a conversion of another branch can be between reading an existing
    /// value's id and writing the reference, and the `NOT EXISTS` below - a consistent
    /// read - cannot see the reference it is about to hold.
    ///
    /// The foreign keys added in migration 5 are what actually settle it. The delete
    /// fails rather than succeeding into a dangling reference, and a failed batch is
    /// skipped: the value stays, and the next prune takes it once the reference has
    /// really gone. Nothing is lost by waiting.
    ///
    /// In batches, because the pool is the largest table here and one statement over
    /// all of it would hold locks for as long as it took - and because a batch is the
    /// unit that gets skipped when the constraint speaks up.
    /// </summary>
    private static long Collect(MySqlConnection connection)
    {
        long watermark;

        using (var high = new MySqlCommand("SELECT COALESCE(MAX(id), 0) FROM value", connection))
            watermark = Convert.ToInt64(high.ExecuteScalar());

        long deleted = 0;
        long from = 0;
        int contended = 0;

        while (from < watermark)
        {
            long to = Math.Min(from + CollectBatch, watermark);

            using var command = new MySqlCommand(@"
                DELETE v FROM value v
                WHERE v.id > @from AND v.id <= @to
                  AND NOT EXISTS (SELECT 1 FROM cell_current c WHERE c.value_id = v.id)
                  AND NOT EXISTS (SELECT 1 FROM cell_change c WHERE c.old_value_id = v.id)
                  AND NOT EXISTS (SELECT 1 FROM cell_change c WHERE c.new_value_id = v.id)",
                connection);

            command.Parameters.AddWithValue("@from", from);
            command.Parameters.AddWithValue("@to", to);

            try
            {
                deleted += command.ExecuteNonQuery();
            }
            catch (MySqlException ex) when (ex.Number == RowIsStillReferenced)
            {
                // Something started referring to one of these while the statement ran.
                // The constraint is doing exactly what it was added for; the batch is
                // left alone and the next prune will find it again.
                contended++;
            }

            from = to;
        }

        if (contended > 0)
        {
            Log.Information(
                $"{contended} batch(es) of values were left alone because something began " +
                $"referring to them while they were being collected. The next prune will " +
                $"take whatever is still unreferenced then.");
        }

        return deleted;
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (int start = 0; start < items.Count; start += size)
            yield return items.Skip(start).Take(size).ToList();
    }

    /// <summary>
    /// Reads `--before` as either a date or an age such as `90d`.
    ///
    /// An age is what a scheduled job wants: a date would have to be recomputed by
    /// whatever runs it, and one that is not is a job that prunes nothing after the
    /// first time it runs.
    /// </summary>
    public static DateTime? ParseCutoff(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int days))
        {
            return DateTime.UtcNow.AddDays(-days);
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.UtcDateTime;
        }

            throw new TabbitException(null,
                Message.Of(RecordMessages.BeforeNotADateOrAge, ("Text", text)));
    }
}
