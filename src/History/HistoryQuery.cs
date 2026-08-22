using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MySqlConnector;
using Tabbit.Messages;

namespace Tabbit.History;

/// <summary>
/// Reads the history back.
///
/// Read-only, always. Nothing here writes, and the account it connects with need not be
/// able to: only a conversion adds to the history, and a query that could modify it is
/// a query that can corrupt it.
///
/// One class behind both the command line and the HTTP API. The two entry points differ
/// in how they are called and in nothing else, so that a number reported by one cannot
/// disagree with the same number reported by the other.
/// </summary>
public sealed class HistoryQuery : IDisposable
{
    /// <summary>
    /// The most changes any one answer will carry.
    ///
    /// A range over a busy month is hundreds of thousands of cells, which is neither
    /// readable nor sendable. What is cut is said out loud - see
    /// <see cref="HistoryQueryInfo.Truncated"/> - because a truncated answer that does
    /// not admit it reads as a complete one.
    /// </summary>
    public const int DefaultLimit = 5_000;

    /// <summary>The most any caller may ask for, however large a number they pass.</summary>
    public const int MaximumLimit = 50_000;

    private readonly MySqlConnection _connection;

    private readonly List<string> _notes = [];

    private HistoryQuery(MySqlConnection connection) => _connection = connection;

    /// <summary>
    /// A working copy to resolve revision names against, or null.
    ///
    /// A range asked for as a tag has to become a commit before the history can be
    /// asked about it, and only git knows what a tag points at. Without one, a name
    /// that is not already a stored commit cannot be resolved - which the error says
    /// plainly rather than reporting it as a missing snapshot.
    /// </summary>
    public string? RepositoryPath { get; set; }

    public static HistoryQuery Open(string connectionString)
    {
        var connection = new MySqlConnection(connectionString);

        try
        {
            connection.Open();
            return new HistoryQuery(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose() => _connection.Dispose();

    // ------------------------------------------------------------- listings

    /// <summary>Every project the database holds, in name order.</summary>
    public IReadOnlyList<string> Projects()
        => Read("SELECT project_key FROM project ORDER BY project_key", r => r.GetString(0));

    /// <summary>
    /// Every branch of a project, most recently written first.
    /// </summary>
    public IReadOnlyList<string> Branches(string project)
        => Read(@"
            SELECT s.branch
            FROM snapshot s JOIN project p ON p.id = s.project_id
            WHERE p.project_key = @project
            GROUP BY s.branch
            ORDER BY MAX(s.converted_at) DESC",
            r => r.GetString(0),
            ("@project", project));

    /// <summary>
    /// The branch a query means when it does not say: the one written to most recently.
    /// </summary>
    public string? DefaultBranch(string project) => Branches(project).FirstOrDefault();

    /// <summary>
    /// Snapshots of a branch, newest first.
    /// </summary>
    public IReadOnlyList<SnapshotListing> Snapshots(string project, string branch, int limit = 100)
    {
        return Read($@"
            SELECT s.id, s.seq, s.commit_hash, s.branch, s.author_name, s.author_email,
                   s.committed_at, s.subject, s.converted_at, s.dirty, s.attributable, s.pruned,
                   (SELECT COUNT(*) FROM schema_change c WHERE c.snapshot_id = s.id),
                   (SELECT COUNT(*) FROM row_change c WHERE c.snapshot_id = s.id),
                   (SELECT COUNT(*) FROM cell_change c WHERE c.snapshot_id = s.id)
            FROM snapshot s JOIN project p ON p.id = s.project_id
            WHERE p.project_key = @project AND s.branch = @branch
            ORDER BY s.seq DESC
            LIMIT {Bounded(limit, 1000)}",
            r => new SnapshotListing
            {
                Id = r.GetInt64(0),
                Seq = r.GetInt64(1),
                Commit = r.GetString(2),
                ShortCommit = Short(r.GetString(2)),
                Branch = r.GetString(3),
                AuthorName = Text(r, 4),
                AuthorEmail = Text(r, 5),
                CommittedAt = Time(r, 6),
                Subject = Text(r, 7),
                ConvertedAt = Time(r, 8),
                Dirty = r.GetBoolean(9),
                Attributable = r.GetBoolean(10),
                Pruned = r.GetBoolean(11),
                Counts = new HistoryChangeCounts
                {
                    Schema = r.GetInt32(12),
                    Rows = r.GetInt32(13),
                    Cells = r.GetInt32(14),
                },
            },
            ("@project", project), ("@branch", branch ?? ""));
    }

    /// <summary>Every table the branch's newest snapshot holds.</summary>
    public IReadOnlyList<string> Tables(string project, string branch)
        => Read(@"
            SELECT t.table_name
            FROM table_current t JOIN project p ON p.id = t.project_id
            WHERE p.project_key = @project AND t.branch = @branch
            ORDER BY t.table_name",
            r => r.GetString(0),
            ("@project", project), ("@branch", branch ?? ""));

    // ------------------------------------------------------------ statistics

    /// <summary>
    /// The whole summary as it was at a commit, or at the branch's head.
    ///
    /// Read back from what the conversion stored rather than recomputed. The workbook
    /// has moved on, so recomputing would describe today's sheets and file the answer
    /// under an old commit.
    /// </summary>
    public SummaryDocument? Stats(string project, string branch, string? commit = null)
    {
        long? id = ResolveSnapshot(project, branch, commit);

        if (id is null)
            return null;

        using var command = Command(
            "SELECT summary FROM snapshot WHERE id = @id", ("@id", id.Value));

        var blob = command.ExecuteScalar();

        if (blob is null || blob == DBNull.Value)
            return null;

        return Newtonsoft.Json.JsonConvert.DeserializeObject<SummaryDocument>(
            HistoryStore.Decompress((byte[])blob));
    }

    /// <summary>
    /// One number per snapshot, oldest first, for drawing a line.
    /// </summary>
    /// <param name="metric">
    /// `rows`, `cells`, `contentBytes`, `tables`, `fields`, or `changes` for how much
    /// each snapshot changed.
    /// </param>
    /// <param name="table">Narrows to one table. Only `rows`, `cells` and `contentBytes` support it.</param>
    public IReadOnlyList<TrendPoint> Trend(
        string project, string branch, string metric, string? table = null, int limit = 500)
    {
        string column = TrendColumn(metric, table is not null);

        string sql = table is null
            ? $@"SELECT s.commit_hash, s.committed_at, {column}
                 FROM snapshot s
                 JOIN project p ON p.id = s.project_id
                 JOIN snapshot_stat st ON st.snapshot_id = s.id
                 WHERE p.project_key = @project AND s.branch = @branch
                 ORDER BY s.seq DESC
                 LIMIT {Bounded(limit, 5000)}"
            : $@"SELECT s.commit_hash, s.committed_at, {column}
                 FROM snapshot s
                 JOIN project p ON p.id = s.project_id
                 JOIN table_stat st ON st.snapshot_id = s.id AND st.table_name = @table
                 WHERE p.project_key = @project AND s.branch = @branch
                 ORDER BY s.seq DESC
                 LIMIT {Bounded(limit, 5000)}";

        var points = Read(sql, r => new TrendPoint
        {
            Commit = r.GetString(0),
            ShortCommit = Short(r.GetString(0)),
            CommittedAt = Time(r, 1),
            Value = r.GetInt64(2),
        },
        table is null
            ? new[] { ("@project", (object)project), ("@branch", branch ?? "") }
            : new[] { ("@project", (object)project), ("@branch", branch ?? ""), ("@table", table) });

        // Newest first out of the database so the LIMIT keeps the recent end; oldest
        // first out of here, because that is the direction a line is read.
        return Enumerable.Reverse(points).ToList();
    }

    private static string TrendColumn(string? metric, bool perTable)
    {
        switch ((metric ?? "rows").ToLowerInvariant())
        {
            case "rows": return perTable ? "st.row_count" : "st.rows_count";
            case "cells": return perTable ? "st.cell_count" : "st.cells";
            case "contentbytes": return "st.content_bytes";
            case "tables" when !perTable: return "st.tables";
            case "fields": return perTable ? "st.field_count" : "st.fields";

            case "changes" when !perTable:
                return "(SELECT COUNT(*) FROM cell_change c WHERE c.snapshot_id = s.id)";

            default:
                    throw new TabbitException(null,
                        Message.Of(perTable
                            ? RecordMessages.MetricUnknownPerTable
                            : RecordMessages.MetricUnknown, ("Metric", metric)));
        }
    }

    /// <summary>
    /// Who changed how much, over a range. Busiest first.
    /// </summary>
    public IReadOnlyList<AuthorSummary> Authors(
        string project, string branch, string? from = null, string? to = null)
    {
        var (fromSeq, toSeq) = ResolveRange(project, branch, from, to);

        return Read(@"
            SELECT COALESCE(s.author_name, '(unknown)'), COALESCE(s.author_email, ''),
                   COUNT(*),
                   COALESCE(SUM((SELECT COUNT(*) FROM cell_change c WHERE c.snapshot_id = s.id)), 0),
                   COALESCE(SUM((SELECT COUNT(*) FROM row_change c WHERE c.snapshot_id = s.id)), 0),
                   COALESCE(SUM((SELECT COUNT(*) FROM schema_change c WHERE c.snapshot_id = s.id)), 0),
                   MIN(s.committed_at), MAX(s.committed_at)
            FROM snapshot s JOIN project p ON p.id = s.project_id
            WHERE p.project_key = @project AND s.branch = @branch
              AND s.seq > @from AND s.seq <= @to
            GROUP BY s.author_name, s.author_email
            ORDER BY 4 DESC, 1",
            r => new AuthorSummary
            {
                Name = r.GetString(0),
                Email = Text(r, 1),
                Snapshots = r.GetInt32(2),
                Cells = r.GetInt64(3),
                Rows = r.GetInt64(4),
                Schema = r.GetInt64(5),
                FirstAt = Time(r, 6),
                LastAt = Time(r, 7),
            },
            ("@project", project), ("@branch", branch ?? ""), ("@from", fromSeq), ("@to", toSeq));
    }

    /// <summary>
    /// Every value one cell has held, newest first.
    ///
    /// The question a designer actually asks: this number is wrong, when did it become
    /// this, and who made it so.
    /// </summary>
    public IReadOnlyList<CellHistoryEntry> CellHistory(
        string project, string branch, string? table, string? rowKey = null, string? field = null, int limit = 200)
    {
        var conditions = new List<string> { "p.project_key = @project", "s.branch = @branch",
                                            "c.table_name = @table" };

        var args = new List<(string, object)>
        {
            ("@project", project), ("@branch", branch ?? ""), ("@table", (object)(table ?? "")),
        };

        if (rowKey is not null)
        {
            conditions.Add("c.row_key_hash = @rowKey");
            args.Add(("@rowKey", HistoryStore.KeyHash(rowKey)));
        }

        if (field is not null)
        {
            conditions.Add("c.field_name = @field");
            args.Add(("@field", field));
        }

        return Read($@"
            SELECT s.commit_hash, s.author_name, s.committed_at,
                   c.table_name, c.row_key, c.field_name, c.change_kind, o.text, n.text
            FROM cell_change c
            JOIN snapshot s ON s.id = c.snapshot_id
            JOIN project p ON p.id = s.project_id
            LEFT JOIN value o ON o.id = c.old_value_id
            LEFT JOIN value n ON n.id = c.new_value_id
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY s.seq DESC, c.id DESC
            LIMIT {Bounded(limit, 2000)}",
            r => new CellHistoryEntry
            {
                Commit = r.GetString(0),
                ShortCommit = Short(r.GetString(0)),
                AuthorName = Text(r, 1),
                CommittedAt = Time(r, 2),
                Table = r.GetString(3),
                RowKey = r.GetString(4),
                Field = r.GetString(5),
                Kind = r.GetString(6),
                Before = Text(r, 7),
                After = Text(r, 8),
            },
            args.ToArray());
    }

    // ----------------------------------------------------------------- range

    /// <summary>
    /// What changed between two commits.
    ///
    /// <paramref name="from"/> is exclusive and <paramref name="to"/> inclusive, which
    /// is what "between A and B" means for a difference: A is the state compared from,
    /// so A's own changes belong to the range before this one.
    /// </summary>
    public HistoryDocument Diff(
        string project,
        string? branch,
        string? from = null,
        string? to = null,
        string? table = null,
        string? field = null,
        string? author = null,
        int limit = DefaultLimit)
    {
        branch ??= DefaultBranch(project) ?? "";
        limit = Bounded(limit, MaximumLimit);

        _notes.Clear();

        var (fromSeq, toSeq) = ResolveRange(project, branch, from, to);

        var snapshots = ReadSnapshotsInRange(project, branch, fromSeq, toSeq, author);

        var document = new HistoryDocument
        {
            Query = new HistoryQueryInfo
            {
                Project = project,
                Branch = branch,
                From = from,
                To = to,
                Table = table,
                Field = field,
                Author = author,
                Limit = limit,
                GeneratedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                Notes = _notes.ToList(),
            },
            Snapshots = snapshots,
        };

        long budget = limit;
        long omitted = 0;

        foreach (var snapshot in snapshots)
        {
            snapshot.Schema = ReadSchemaChanges(snapshot.Id, table, ref budget, ref omitted);
            snapshot.Rows = ReadRowChanges(snapshot.Id, table, ref budget, ref omitted);
            snapshot.Cells = ReadCellChanges(snapshot.Id, table, field, ref budget, ref omitted);

            snapshot.Counts = new HistoryChangeCounts
            {
                Schema = snapshot.Schema.Count,
                Rows = snapshot.Rows.Count,
                Cells = snapshot.Cells.Count,
            };
        }

        AttachDeploymentAdvice(project, branch, snapshots);

        document.Deployment = DeploymentAdvice.Merge(snapshots.Select(s => s.Deployment));

        document.Query.Truncated = omitted > 0;
        document.Query.Omitted = omitted;

        document.Totals = new HistoryTotals
        {
            Snapshots = snapshots.Count,
            Schema = snapshots.Sum(s => (long)s.Schema.Count),
            Rows = snapshots.Sum(s => (long)s.Rows.Count),
            Cells = snapshots.Sum(s => (long)s.Cells.Count),
            Gaps = snapshots.Count(s => !s.FollowsParent),
            Pruned = snapshots.Count(s => s.Pruned),
        };

        return document;
    }

    private List<HistorySnapshotView> ReadSnapshotsInRange(
        string project, string branch, long fromSeq, long toSeq, string? author)
    {
        var args = new List<(string, object)>
        {
            ("@project", project), ("@branch", branch), ("@from", fromSeq), ("@to", toSeq),
        };

        string filter = "";

        if (!string.IsNullOrEmpty(author))
        {
            // Matched on either name or address, because a person is asked about by
            // whichever of the two the asker happens to know.
            filter = " AND (s.author_name LIKE @author OR s.author_email LIKE @author)";
            args.Add(("@author", "%" + author + "%"));
        }

        var snapshots = Read($@"
            SELECT s.id, s.seq, s.commit_hash, s.branch, s.author_name, s.author_email,
                   s.committed_at, s.subject, s.converted_at, s.converted_by, s.dirty, s.attributable,
                   s.follows_parent, s.pruned,
                   (SELECT x.commit_hash FROM snapshot x WHERE x.id = s.parent_id)
            FROM snapshot s JOIN project p ON p.id = s.project_id
            WHERE p.project_key = @project AND s.branch = @branch
              AND s.seq > @from AND s.seq <= @to{filter}
            ORDER BY s.seq",
            r => new HistorySnapshotView
            {
                Id = r.GetInt64(0),
                Seq = r.GetInt64(1),
                Commit = r.GetString(2),
                ShortCommit = Short(r.GetString(2)),
                Branch = r.GetString(3),
                AuthorName = Text(r, 4),
                AuthorEmail = Text(r, 5),
                CommittedAt = Time(r, 6),
                Subject = Text(r, 7),
                ConvertedAt = Time(r, 8),
                ConvertedBy = Text(r, 9),
                Dirty = r.GetBoolean(10),
                Attributable = r.GetBoolean(11),
                FollowsParent = r.GetBoolean(12),
                Pruned = r.GetBoolean(13),
                PreviousCommit = Text(r, 14),
            },
            args.ToArray());

        return snapshots.ToList();
    }

    private IReadOnlyList<SchemaChangeView> ReadSchemaChanges(
        long snapshotId, string? table, ref long budget, ref long omitted)
    {
        string filter = table is null ? "" : " AND entity_name = @table";

        long total = Count("schema_change", snapshotId, filter, table);
        int take = Take(total, ref budget, ref omitted);

        if (take == 0)
            return Array.Empty<SchemaChangeView>();

        var args = table is null
            ? new[] { ("@id", (object)snapshotId) }
            : new[] { ("@id", (object)snapshotId), ("@table", table) };

        return Read($@"
            SELECT entity_kind, entity_name, member_name, change_kind, before_value, after_value,
                   file, sheet, cell, url, renamed_from
            FROM schema_change
            WHERE snapshot_id = @id{filter}
            ORDER BY id
            LIMIT {take}",
            r => new SchemaChangeView
            {
                EntityKind = r.GetString(0),
                Entity = r.GetString(1),
                Member = Text(r, 2),
                Kind = r.GetString(3),
                Before = Text(r, 4),
                After = Text(r, 5),
                Location = LocationOf(r, 6),
                RenamedFrom = Text(r, 10),
            },
            args);
    }

    private IReadOnlyList<RowChangeView> ReadRowChanges(
        long snapshotId, string? table, ref long budget, ref long omitted)
    {
        string filter = table is null ? "" : " AND table_name = @table";

        long total = Count("row_change", snapshotId, filter, table);
        int take = Take(total, ref budget, ref omitted);

        if (take == 0)
            return Array.Empty<RowChangeView>();

        var args = table is null
            ? new[] { ("@id", (object)snapshotId) }
            : new[] { ("@id", (object)snapshotId), ("@table", table) };

        return Read($@"
            SELECT table_name, row_key, change_kind
            FROM row_change
            WHERE snapshot_id = @id{filter}
            ORDER BY id
            LIMIT {take}",
            r => new RowChangeView
            {
                Table = r.GetString(0),
                RowKey = r.GetString(1),
                Kind = r.GetString(2),
            },
            args);
    }

    private IReadOnlyList<CellChangeView> ReadCellChanges(
        long snapshotId, string? table, string? field, ref long budget, ref long omitted)
    {
        var conditions = new List<string>();
        var args = new List<(string, object)> { ("@id", snapshotId) };

        if (table is not null)
        {
            conditions.Add(" AND c.table_name = @table");
            args.Add(("@table", table));
        }

        if (field is not null)
        {
            conditions.Add(" AND c.field_name = @field");
            args.Add(("@field", field));
        }

        string filter = string.Concat(conditions);

        long total = CountCells(snapshotId, filter, table, field);
        int take = Take(total, ref budget, ref omitted);

        if (take == 0)
            return Array.Empty<CellChangeView>();

        return Read($@"
            SELECT c.table_name, c.row_key, c.field_name, c.change_kind, o.text, n.text,
                   c.file, c.sheet, c.cell, c.url
            FROM cell_change c
            LEFT JOIN value o ON o.id = c.old_value_id
            LEFT JOIN value n ON n.id = c.new_value_id
            WHERE c.snapshot_id = @id{filter}
            ORDER BY c.id
            LIMIT {take}",
            r => new CellChangeView
            {
                Table = r.GetString(0),
                RowKey = r.GetString(1),
                Field = r.GetString(2),
                Kind = r.GetString(3),
                Before = Text(r, 4),
                After = Text(r, 5),
                Location = LocationOf(r, 6),
            },
            args.ToArray());
    }

    /// <summary>
    /// Works out what each snapshot needs shipped, and pins it to the snapshot.
    ///
    /// Reads the schema changes again, in full and unfiltered, rather than reusing what
    /// the budgeted reads returned. The budget exists to keep an answer sendable and
    /// cuts whatever falls past the limit - and a verdict computed from a cut list
    /// would report "data only" for the snapshot whose enum change fell off the end.
    /// Schema changes are bounded by the model's shape, not its rows, so the second
    /// read is small. The table filter is ignored for the same reason: the verdict is
    /// about shipping the snapshot, which no filter makes smaller.
    /// </summary>
    private void AttachDeploymentAdvice(
        string project, string branch, IReadOnlyList<HistorySnapshotView> snapshots)
    {
        // Pruned snapshots are skipped, not judged: their change detail is deleted, and
        // the honest verdict on evidence that was thrown away is no verdict.
        var ids = snapshots.Where(s => !s.Pruned).Select(s => s.Id).ToList();

        if (ids.Count == 0)
            return;

        var schema = ReadSchemaForAdvice(ids);
        var moved = SnapshotsWhereDataMoved(ids);
        var enumsInUse = ReadEnumsInUse(project, branch);

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Pruned)
                continue;

            schema.TryGetValue(snapshot.Id, out var changes);

            snapshot.Deployment = DeploymentAdvice.Compute(
                changes ?? [], moved.Contains(snapshot.Id), enumsInUse);
        }
    }

    /// <summary>
    /// The type names the branch's current columns are declared with.
    ///
    /// An enum in this set has its values written into exported rows; one outside it
    /// exists only as a declaration, and changing it - renumbering included - touches
    /// no data. Read from the head state rather than reconstructed per snapshot,
    /// because the verdict is about shipping into the present: what matters is whether
    /// the values are out there now.
    ///
    /// The set holds every column type name, not only the enums. Membership is only
    /// ever tested with an enum's name, and a scalar type name cannot collide with one
    /// - the conversion would have refused an enum named `int` long before it got here.
    /// </summary>
    private HashSet<string> ReadEnumsInUse(string project, string branch)
    {
        var descriptors = Read(@"
            SELECT f.descriptor
            FROM field_current f JOIN project p ON p.id = f.project_id
            WHERE p.project_key = @project AND f.branch = @branch",
            r => r.GetString(0),
            ("@project", project), ("@branch", branch ?? ""));

        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            try
            {
                string? type = (string?)Newtonsoft.Json.Linq.JObject.Parse(descriptor)["type"];

                if (!string.IsNullOrEmpty(type))
                    used.Add(type);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // A descriptor this build cannot parse proves nothing about usage,
                // and skipping it errs toward fewer warnings, not wrong ones.
            }
        }

        return used;
    }

    /// <summary>Every schema change of every listed snapshot, grouped by snapshot.</summary>
    private Dictionary<long, List<SchemaChangeView>> ReadSchemaForAdvice(IReadOnlyList<long> ids)
    {
        var result = new Dictionary<long, List<SchemaChangeView>>();

        var (placeholders, args) = InList(ids);

        using var command = Command($@"
            SELECT snapshot_id, entity_kind, entity_name, member_name, change_kind,
                   before_value, after_value, renamed_from
            FROM schema_change
            WHERE snapshot_id IN ({placeholders})
            ORDER BY id", args);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            long id = reader.GetInt64(0);

            if (!result.TryGetValue(id, out var list))
                result[id] = list = new List<SchemaChangeView>();

            list.Add(new SchemaChangeView
            {
                EntityKind = reader.GetString(1),
                Entity = reader.GetString(2),
                Member = Text(reader, 3),
                Kind = reader.GetString(4),
                Before = Text(reader, 5),
                After = Text(reader, 6),
                RenamedFrom = Text(reader, 7),
            });
        }

        return result;
    }

    /// <summary>
    /// Which of the listed snapshots changed any row or cell.
    ///
    /// Existence is the whole question, so this reads distinct ids off the snapshot
    /// index instead of counting rows a big conversion has hundreds of thousands of.
    /// </summary>
    private HashSet<long> SnapshotsWhereDataMoved(IReadOnlyList<long> ids)
    {
        var (placeholders, args) = InList(ids);

        var moved = Read($@"
            SELECT DISTINCT snapshot_id FROM row_change WHERE snapshot_id IN ({placeholders})
            UNION
            SELECT DISTINCT snapshot_id FROM cell_change WHERE snapshot_id IN ({placeholders})",
            r => r.GetInt64(0),
            args);

        return new HashSet<long>(moved);
    }

    private static (string Placeholders, (string, object)[] Args) InList(IReadOnlyList<long> ids)
    {
        var placeholders = string.Join(",", ids.Select((_, i) => "@i" + i));
        var args = ids.Select((id, i) => ("@i" + i, (object)id)).ToArray();

        return (placeholders, args);
    }

    /// <summary>
    /// How many of a total the budget allows, adding the rest to what was left out.
    /// </summary>
    private static int Take(long total, ref long budget, ref long omitted)
    {
        long take = Math.Min(total, Math.Max(budget, 0));

        budget -= take;
        omitted += total - take;

        return (int)take;
    }

    private long Count(string table, long snapshotId, string filter, string? tableName)
    {
        var args = tableName is null
            ? new[] { ("@id", (object)snapshotId) }
            : new[] { ("@id", (object)snapshotId), ("@table", tableName) };

        using var command = Command(
            $"SELECT COUNT(*) FROM {table} WHERE snapshot_id = @id{filter}", args);

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private long CountCells(long snapshotId, string filter, string? table, string? field)
    {
        var args = new List<(string, object)> { ("@id", snapshotId) };

        if (table is not null) args.Add(("@table", table));
        if (field is not null) args.Add(("@field", field));

        using var command = Command(
            $"SELECT COUNT(*) FROM cell_change c WHERE c.snapshot_id = @id{filter}", args.ToArray());

        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>
    /// Everything one view of the history needs, in one round of queries.
    ///
    /// Assembled here rather than by whoever is drawing the page, so the file written
    /// by `--history --format html` and the object the server sends are the same shape
    /// and are filled in the same way.
    /// </summary>
    public DashboardDocument Dashboard(
        string project,
        string? branch = null,
        string? from = null,
        string? to = null,
        string? table = null,
        string? field = null,
        string? author = null,
        int limit = DefaultLimit)
    {
        branch ??= DefaultBranch(project) ?? "";

        return new DashboardDocument
        {
            Project = project,
            Branch = branch,
            Branches = Branches(project),
            Stats = Stats(project, branch, to),
            History = Diff(project, branch, from, to, table, field, author, limit),
            Snapshots = Snapshots(project, branch),
            Rows = Trend(project, branch, "rows", table),
            Churn = Trend(project, branch, "changes"),
            Authors = Authors(project, branch, from, to),
        };
    }

    // ------------------------------------------------------------ resolution

    /// <summary>
    /// Turns a commit - or a prefix of one - into the snapshot that holds it.
    /// </summary>
    /// <summary>
    /// Finds the snapshot a name refers to.
    ///
    /// A stored commit hash, or a prefix of one, is matched directly. Anything else is
    /// put to git - a tag, a branch, HEAD~3 - and the commit it names is looked up
    /// instead. Release tags are the reason: nobody remembers the hash a version was
    /// cut at, and the tag is the name the question gets asked in.
    ///
    /// A tag usually points at a commit no conversion ever ran on, because bumping a
    /// version touches no sheets. The last snapshot at or before it is used instead,
    /// and the substitution is recorded in the answer's notes. Erroring would send
    /// somebody hunting for a hash by hand; substituting quietly would answer a
    /// different question from the one asked.
    /// </summary>
    public long? ResolveSnapshot(string project, string branch, string? commit)
    {
        if (string.IsNullOrEmpty(commit))
        {
            using var head = Command(@"
                SELECT s.id FROM snapshot s JOIN project p ON p.id = s.project_id
                WHERE p.project_key = @project AND s.branch = @branch
                ORDER BY s.seq DESC LIMIT 1",
                ("@project", project), ("@branch", branch ?? ""));

            var id = head.ExecuteScalar();
            return id is null || id == DBNull.Value ? (long?)null : Convert.ToInt64(id);
        }

        var matches = Read(@"
            SELECT s.id, s.commit_hash
            FROM snapshot s JOIN project p ON p.id = s.project_id
            WHERE p.project_key = @project AND s.branch = @branch
              AND (s.commit_hash = @commit OR s.commit_hash LIKE CONCAT(@commit, '%'))
            LIMIT 5",
            r => (Id: r.GetInt64(0), Hash: r.GetString(1)),
            ("@project", project), ("@branch", branch ?? ""), ("@commit", commit));

        if (matches.Count == 0)
            return ResolveThroughGit(project, branch, commit);

        // An exact match wins over a prefix, so a short identifier that happens to
        // prefix a longer one still resolves to itself.
        var exact = matches.FirstOrDefault(m => string.Equals(m.Hash, commit, StringComparison.Ordinal));
        if (exact.Id != 0)
            return exact.Id;

        if (matches.Count > 1)
        {
                throw new TabbitException(null,
                    Message.Of(RecordMessages.CommitAmbiguous,
                        ("Commit", commit), ("Count", matches.Count), ("Branch", branch),
                        ("Matches", string.Join(", ", matches.Select(m => Short(m.Hash))))));
        }

        return matches[0].Id;
    }

    /// <summary>
    /// Asks git what the name means, then looks that commit up.
    /// </summary>
    private long? ResolveThroughGit(string project, string? branch, string? name)
    {
        if (RepositoryPath is null
            || !GitProbe.TryResolveCommit(RepositoryPath, name, out string? hash))
        {
            return null;
        }

        var direct = Read(@"
            SELECT s.id FROM snapshot s JOIN project p ON p.id = s.project_id
            WHERE p.project_key = @project AND s.branch = @branch AND s.commit_hash = @commit",
            r => r.GetInt64(0),
            ("@project", project), ("@branch", branch ?? ""), ("@commit", hash));

        if (direct.Count > 0)
        {
            Note($"`{name}` is {Short(hash)}.");
            return direct[0];
        }

        // The commit the name points at was never converted, which is the ordinary case
        // for a release tag. The nearest snapshot behind it answers what was meant.
        if (!GitProbe.TryCommittedAt(RepositoryPath, hash, out var at))
            return null;

        var nearest = Read(@"
            SELECT s.id, s.commit_hash
            FROM snapshot s JOIN project p ON p.id = s.project_id
            WHERE p.project_key = @project AND s.branch = @branch
              AND s.committed_at IS NOT NULL AND s.committed_at <= @at
            ORDER BY s.committed_at DESC, s.seq DESC
            LIMIT 1",
            r => (Id: r.GetInt64(0), Hash: r.GetString(1)),
            ("@project", project), ("@branch", branch ?? ""), ("@at", at.UtcDateTime));

        if (nearest.Count == 0)
        {
            Note($"`{name}` is {Short(hash)}, which is older than every snapshot on " +
                 $"`{branch}` - there is nothing behind it to stand in for it.");

            return null;
        }

        Note($"`{name}` is {Short(hash)}, which no conversion ever ran on. Using " +
             $"{Short(nearest[0].Hash)}, the last snapshot before it.");

        return nearest[0].Id;
    }

    /// <summary>
    /// Something the answer did that was not asked for, and that changes what it means.
    /// </summary>
    private void Note(string note)
    {
        if (!_notes.Contains(note))
            _notes.Add(note);
    }

    /// <summary>
    /// The sequence numbers a range covers: after <paramref name="from"/>, up to and
    /// including <paramref name="to"/>.
    /// </summary>
    private (long From, long To) ResolveRange(string project, string branch, string? from, string? to)
    {
        long fromSeq = 0;
        long toSeq = long.MaxValue;

        if (!string.IsNullOrEmpty(from))
        {
            long? id = ResolveSnapshot(project, branch, from)
                       ?? throw NotFound(from, project, branch);

            fromSeq = SeqOf(id.Value);
        }

        if (!string.IsNullOrEmpty(to))
        {
            long? id = ResolveSnapshot(project, branch, to)
                       ?? throw NotFound(to, project, branch);

            toSeq = SeqOf(id.Value);
        }

        if (fromSeq > toSeq)
        {
                throw new TabbitException(null,
                    Message.Of(RecordMessages.RangeReversed,
                        ("From", from), ("To", to), ("Branch", branch)));
        }

        return (fromSeq, toSeq);
    }

    private static TabbitException NotFound(string commit, string project, string branch)
        => new TabbitException(null,
            Message.Of(RecordMessages.SnapshotNotFound,
                ("Commit", commit), ("Branch", branch), ("Project", project)));

    private long SeqOf(long snapshotId)
    {
        using var command = Command("SELECT seq FROM snapshot WHERE id = @id", ("@id", snapshotId));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    // -------------------------------------------------------------- plumbing

    private MySqlCommand Command(string sql, params (string Name, object Value)[] args)
    {
        var command = new MySqlCommand(sql, _connection);

        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value);

        return command;
    }

    private List<T> Read<T>(string sql, Func<MySqlDataReader, T> map, params (string Name, object Value)[] args)
    {
        using var command = Command(sql, args);
        using var reader = command.ExecuteReader();

        var results = new List<T>();

        while (reader.Read())
            results.Add(map(reader));

        return results;
    }

    private static string? Text(MySqlDataReader reader, int column)
        => reader.IsDBNull(column) ? null : reader.GetString(column);

    private static string? Time(MySqlDataReader reader, int column)
        => reader.IsDBNull(column)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc)
                      .ToString("o", CultureInfo.InvariantCulture);

    private static SummaryLocation? LocationOf(MySqlDataReader reader, int first)
    {
        if (reader.IsDBNull(first) && reader.IsDBNull(first + 1))
            return null;

        return new SummaryLocation
        {
            File = Text(reader, first),
            Sheet = Text(reader, first + 1),
            Cell = Text(reader, first + 2),
            Url = Text(reader, first + 3),
        };
    }

    private static string? Short(string? hash)
        => hash is null ? null : hash.Substring(0, Math.Min(12, hash.Length));

    private static int Bounded(int requested, int maximum)
        => requested <= 0 ? maximum : Math.Min(requested, maximum);
}
