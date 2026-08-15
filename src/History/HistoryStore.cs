using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using Serilog;

namespace Tabbit.History;

/// <summary>
/// The history, in MySQL.
///
/// One instance is one project on one branch, for the length of one conversion. It
/// reads what the branch currently holds, and writes a snapshot and the changes that
/// got there.
///
/// Two things shape almost every decision in here.
///
/// It is remote, so a round trip is the unit of cost. Reads are batched and descend
/// only where a hash says something moved; writes go out in multi-row statements.
///
/// And several build machines write to it at once. A snapshot is keyed on the commit it
/// describes, so the same commit converted twice is one row rather than two, and two CI
/// jobs racing on the same commit cannot produce a duplicate history.
/// </summary>
internal sealed class HistoryStore : IHistoryState, IDisposable
{
    /// <summary>Rows per multi-row INSERT. Beyond this the statement itself gets slow to parse.</summary>
    private const int WriteBatch = 500;

    /// <summary>Keys per IN list. A parameter list without a bound is a plan the server cannot reuse.</summary>
    private const int ReadBatch = 500;

    private readonly MySqlConnection _connection;
    private readonly int _projectId;
    private readonly string _branch;

    private HistoryStore(MySqlConnection connection, int projectId, string branch)
    {
        _connection = connection;
        _projectId = projectId;
        _branch = branch;
    }

    /// <summary>
    /// Connects, migrates if needed, and finds or creates the project.
    /// </summary>
    public static HistoryStore Open(string connectionString, string projectKey, string branch)
    {
        var connection = new MySqlConnection(connectionString);

        try
        {
            connection.Open();

            HistorySchema.Migrate(connection);

            return new HistoryStore(connection, ProjectId(connection, projectKey), branch ?? "");
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose() => _connection.Dispose();

    private static int ProjectId(MySqlConnection connection, string projectKey)
    {
        // Insert-then-select rather than select-then-insert: two machines recording a
        // new project at once both find nothing and both insert, and only the unique
        // key decides. Ignoring the duplicate and reading back is the version of that
        // race with no loser.
        using (var insert = new MySqlCommand(
            "INSERT IGNORE INTO project (project_key) VALUES (@key)", connection))
        {
            insert.Parameters.AddWithValue("@key", projectKey);
            insert.ExecuteNonQuery();
        }

        using var select = new MySqlCommand("SELECT id FROM project WHERE project_key = @key", connection);
        select.Parameters.AddWithValue("@key", projectKey);

        return Convert.ToInt32(select.ExecuteScalar());
    }

    // ------------------------------------------------------------- snapshots

    /// <summary>The snapshot this branch currently ends at, or null when it has none.</summary>
    public SnapshotRow? ReadHead()
    {
        using var command = new MySqlCommand(@"
            SELECT id, commit_hash, seq, model_hash, committed_at, dirty
            FROM snapshot
            WHERE project_id = @project AND branch = @branch
            ORDER BY seq DESC
            LIMIT 1", _connection);

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);

        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadSnapshotRow(reader) : null;
    }

    /// <summary>The snapshot already recorded for this commit, or null.</summary>
    public SnapshotRow? FindSnapshot(string commit)
    {
        using var command = new MySqlCommand(@"
            SELECT id, commit_hash, seq, model_hash, committed_at, dirty
            FROM snapshot
            WHERE project_id = @project AND branch = @branch AND commit_hash = @commit", _connection);

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);
        command.Parameters.AddWithValue("@commit", commit);

        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadSnapshotRow(reader) : null;
    }

    private static SnapshotRow ReadSnapshotRow(MySqlDataReader reader) => new SnapshotRow
    {
        Id = reader.GetInt64(0),
        CommitHash = reader.GetString(1),
        Seq = reader.GetInt64(2),
        ModelHash = reader.GetString(3),
        CommittedAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
        Dirty = reader.GetBoolean(5),
    };

    // ------------------------------------------------------------------ reads

    public IReadOnlyDictionary<string, StoredTable> ReadTables()
    {
        var tables = new Dictionary<string, StoredTable>(StringComparer.Ordinal);

        using var command = new MySqlCommand(@"
            SELECT table_name, table_hash, schema_hash
            FROM table_current
            WHERE project_id = @project AND branch = @branch", _connection);

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            tables[reader.GetString(0)] = new StoredTable
            {
                Name = reader.GetString(0),
                Hash = reader.GetString(1),
                SchemaHash = reader.GetString(2),
            };
        }

        return tables;
    }

    public IReadOnlyDictionary<string, StoredField> ReadFields(string table)
    {
        var fields = new Dictionary<string, StoredField>(StringComparer.Ordinal);

        using var command = new MySqlCommand(@"
            SELECT field_name, field_hash, descriptor
            FROM field_current
            WHERE project_id = @project AND branch = @branch AND table_name = @table", _connection);

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);
        command.Parameters.AddWithValue("@table", table);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            fields[reader.GetString(0)] = new StoredField
            {
                Name = reader.GetString(0),
                Hash = reader.GetString(1),
                Descriptor = reader.GetString(2),
            };
        }

        return fields;
    }

    public IReadOnlyDictionary<string, string> ReadRowHashes(string table)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        using var command = new MySqlCommand(@"
            SELECT row_key, row_hash
            FROM row_current
            WHERE project_id = @project AND branch = @branch AND table_name = @table", _connection);

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);
        command.Parameters.AddWithValue("@table", table);

        using var reader = command.ExecuteReader();

        while (reader.Read())
            rows[reader.GetString(0)] = reader.GetString(1);

        return rows;
    }

    public IReadOnlyDictionary<CellAddress, string?> ReadCells(
        string table, IReadOnlyCollection<string> rowKeys)
    {
        var cells = new Dictionary<CellAddress, string?>();

        foreach (var chunk in Chunk(rowKeys.ToList(), ReadBatch))
        {
            var parameters = new List<string>(chunk.Count);

            using var command = new MySqlCommand { Connection = _connection };

            for (int i = 0; i < chunk.Count; i++)
            {
                parameters.Add("@k" + i);
                command.Parameters.AddWithValue("@k" + i, KeyHash(chunk[i]));
            }

            command.CommandText = $@"
                SELECT c.row_key, c.field_name, v.text
                FROM cell_current c
                LEFT JOIN value v ON v.id = c.value_id
                WHERE c.project_id = @project AND c.branch = @branch AND c.table_name = @table
                  AND c.row_key_hash IN ({string.Join(", ", parameters)})";

            command.Parameters.AddWithValue("@project", _projectId);
            command.Parameters.AddWithValue("@branch", _branch);
            command.Parameters.AddWithValue("@table", table);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                cells[new CellAddress(reader.GetString(0), reader.GetString(1))] =
                    reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        return cells;
    }

    public IReadOnlyDictionary<EntityAddress, StoredEntity> ReadEntities()
    {
        var entities = new Dictionary<EntityAddress, StoredEntity>();

        using var command = new MySqlCommand(@"
            SELECT entity_kind, entity_name, entity_hash
            FROM entity_current
            WHERE project_id = @project AND branch = @branch", _connection);

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var kind = (EntityKind)Enum.Parse(typeof(EntityKind), reader.GetString(0), ignoreCase: true);
            var address = new EntityAddress(kind, reader.GetString(1));

            entities[address] = new StoredEntity
            {
                Kind = kind,
                Name = reader.GetString(1),
                Hash = reader.GetString(2),
            };
        }

        return entities;
    }

    public IReadOnlyDictionary<string, string> ReadMembers(EntityAddress entity)
    {
        var members = new Dictionary<string, string>(StringComparer.Ordinal);

        using var command = new MySqlCommand(@"
            SELECT member_name, member_value
            FROM member_current
            WHERE project_id = @project AND branch = @branch
              AND entity_kind = @kind AND entity_name = @name", _connection);

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);
        command.Parameters.AddWithValue("@kind", entity.Kind.ToString());
        command.Parameters.AddWithValue("@name", entity.Name);

        using var reader = command.ExecuteReader();

        while (reader.Read())
            members[reader.GetString(0)] = reader.GetString(1);

        return members;
    }

    // ------------------------------------------------------------------ write

    /// <summary>
    /// Records a snapshot and everything that changed to reach it.
    ///
    /// One transaction. A half-written snapshot would leave the current state describing
    /// a model no snapshot recorded, and every comparison after it would be against
    /// something that never existed.
    /// </summary>
    public long Write(SnapshotWrite write)
    {
        // Around the whole write, including the head this snapshot's sequence number
        // was worked out from. Two conversions of one branch at once would otherwise
        // both read the same head and both claim the position after it.
        string lockName = HistorySchema.WriteLockFor(_projectId, _branch);

        HistorySchema.Lock(_connection, lockName);

        try
        {
            return Locked(write);
        }
        finally
        {
            HistorySchema.Unlock(_connection, lockName);
        }
    }

    private long Locked(SnapshotWrite write)
    {
        // The head is read again under the lock. What was read before it is what
        // decided the sequence number, and between then and now another conversion may
        // have moved the branch on.
        var head = ReadHead();

        if (head is not null && write.Seq <= head.Seq)
        {
            write.Seq = head.Seq + 1;
            write.ParentId = head.Id;
        }

        using var transaction = _connection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            long snapshotId = InsertSnapshot(transaction, write);

            var values = ResolveValues(transaction, write.Changes);

            InsertSchemaChanges(transaction, snapshotId, write.Changes.Schema);
            InsertRowChanges(transaction, snapshotId, write.Changes.Rows);
            InsertCellChanges(transaction, snapshotId, write.Changes.Cells, values);

            ApplyState(transaction, snapshotId, write, values);
            InsertStats(transaction, snapshotId, write.Summary);

            transaction.Commit();

            return snapshotId;
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* the connection is already broken */ }
            throw;
        }
    }

    private long InsertSnapshot(MySqlTransaction transaction, SnapshotWrite write)
    {
        using var command = Command(transaction, @"
            INSERT INTO snapshot
                (project_id, branch, commit_hash, seq, parent_id, follows_parent, model_hash,
                 author_name, author_email, committed_at, subject, dirty, attributable,
                 converted_at, converted_by, tool_version, recipe, summary)
            VALUES
                (@project, @branch, @commit, @seq, @parent, @followsParent, @modelHash,
                 @authorName, @authorEmail, @committedAt, @subject, @dirty, @attributable,
                 UTC_TIMESTAMP(3), @convertedBy, @toolVersion, @recipe, @summary)");

        var commit = write.Summary.Run.Commit;

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);
        command.Parameters.AddWithValue("@commit", commit.Hash);
        command.Parameters.AddWithValue("@seq", write.Seq);
        command.Parameters.AddWithValue("@parent", (object?)write.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@followsParent", write.FollowsParent);
        command.Parameters.AddWithValue("@modelHash", write.Summary.Data.Hash);
        command.Parameters.AddWithValue("@authorName", (object?)commit.AuthorName ?? DBNull.Value);
        command.Parameters.AddWithValue("@authorEmail", (object?)commit.AuthorEmail ?? DBNull.Value);
        command.Parameters.AddWithValue("@committedAt", (object?)ParseUtc(commit.CommittedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("@subject", (object?)commit.Subject ?? DBNull.Value);
        command.Parameters.AddWithValue("@dirty", commit.Dirty);
        command.Parameters.AddWithValue("@attributable", commit.Attributable);
        command.Parameters.AddWithValue("@convertedBy", Environment.UserName);
        command.Parameters.AddWithValue("@toolVersion", (object?)write.Summary.Run.ToolVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("@recipe", (object?)write.Summary.Run.Recipe ?? DBNull.Value);

        // Compressed, because the document repeats every column name for every table
        // and a project accumulates one of these per commit for ever.
        command.Parameters.AddWithValue("@summary", Compress(SummaryTarget.Render(write.Summary)));

        command.ExecuteNonQuery();

        return command.LastInsertedId;
    }

    /// <summary>
    /// Puts every value the changes mention into the pool, and hands back their ids.
    ///
    /// Content-addressed, so a value already stored costs nothing. Planning data repeats
    /// itself enormously - the string `0` appears in a large project some millions of
    /// times - and storing each occurrence inline would make the change log larger than
    /// the data it describes.
    /// </summary>
    private Dictionary<string, long> ResolveValues(MySqlTransaction transaction, SnapshotChanges changes)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cell in changes.Cells)
        {
            if (cell.OldValue is not null) texts.Add(cell.OldValue);
            if (cell.NewValue is not null) texts.Add(cell.NewValue);
        }

        var ids = new Dictionary<string, long>(StringComparer.Ordinal);

        if (texts.Count == 0)
            return ids;

        var pending = texts.ToList();

        foreach (var chunk in Chunk(pending, WriteBatch))
        {
            // INSERT IGNORE first, then read every id back. The alternative - read,
            // then insert what was missing - loses a race with another build machine
            // inserting the same value between the two statements.
            using (var insert = Command(transaction, ""))
            {
                var rows = new List<string>(chunk.Count);

                for (int i = 0; i < chunk.Count; i++)
                {
                    rows.Add($"(@h{i}, @t{i})");
                    insert.Parameters.AddWithValue("@h" + i, ValueHash(chunk[i]));
                    insert.Parameters.AddWithValue("@t" + i, chunk[i]);
                }

                insert.CommandText = "INSERT IGNORE INTO value (hash, text) VALUES " + string.Join(", ", rows);
                insert.ExecuteNonQuery();
            }

            using var select = Command(transaction, "");
            var keys = new List<string>(chunk.Count);

            for (int i = 0; i < chunk.Count; i++)
            {
                keys.Add("@h" + i);
                select.Parameters.AddWithValue("@h" + i, ValueHash(chunk[i]));
            }

            select.CommandText = $"SELECT text, id FROM value WHERE hash IN ({string.Join(", ", keys)})";

            using var reader = select.ExecuteReader();

            while (reader.Read())
                ids[reader.GetString(0)] = reader.GetInt64(1);
        }

        return ids;
    }

    private void InsertSchemaChanges(
        MySqlTransaction transaction, long snapshotId, IReadOnlyList<SchemaChange> changes)
    {
        Batched(transaction, changes, WriteBatch,
            @"INSERT INTO schema_change
                 (snapshot_id, entity_kind, entity_name, member_name, renamed_from,
                  change_kind, before_value, after_value, file, sheet, cell, url) VALUES ",
            "(@s{0}, @k{0}, @e{0}, @m{0}, @rf{0}, @c{0}, @b{0}, @a{0}, @f{0}, @sh{0}, @cl{0}, @u{0})",
            (command, change, i) =>
            {
                command.Parameters.AddWithValue("@s" + i, snapshotId);
                command.Parameters.AddWithValue("@k" + i, change.EntityKind.ToString());
                command.Parameters.AddWithValue("@e" + i, change.EntityName);
                command.Parameters.AddWithValue("@m" + i, (object?)change.MemberName ?? DBNull.Value);
                command.Parameters.AddWithValue("@rf" + i, (object?)change.RenamedFrom ?? DBNull.Value);
                command.Parameters.AddWithValue("@c" + i, change.Kind.ToString());
                command.Parameters.AddWithValue("@b" + i, (object?)change.Before ?? DBNull.Value);
                command.Parameters.AddWithValue("@a" + i, (object?)change.After ?? DBNull.Value);

                AddLocation(command, i, change.Location);
            });
    }

    private void InsertRowChanges(
        MySqlTransaction transaction, long snapshotId, IReadOnlyList<RowChange> changes)
    {
        Batched(transaction, changes, WriteBatch,
            "INSERT INTO row_change (snapshot_id, table_name, row_key_hash, row_key, change_kind) VALUES ",
            "(@s{0}, @t{0}, @h{0}, @r{0}, @c{0})",
            (command, change, i) =>
            {
                command.Parameters.AddWithValue("@s" + i, snapshotId);
                command.Parameters.AddWithValue("@t" + i, change.Table);
                command.Parameters.AddWithValue("@h" + i, KeyHash(change.RowKey));
                command.Parameters.AddWithValue("@r" + i, change.RowKey ?? "");
                command.Parameters.AddWithValue("@c" + i, change.Kind.ToString());
            });
    }

    private void InsertCellChanges(
        MySqlTransaction transaction,
        long snapshotId,
        IReadOnlyList<CellChange> changes,
        IReadOnlyDictionary<string, long> values)
    {
        Batched(transaction, changes, WriteBatch,
            @"INSERT INTO cell_change
                 (snapshot_id, table_name, row_key_hash, row_key, field_name, change_kind,
                  old_value_id, new_value_id, file, sheet, cell, url) VALUES ",
            "(@s{0}, @t{0}, @h{0}, @r{0}, @n{0}, @c{0}, @o{0}, @w{0}, @f{0}, @sh{0}, @cl{0}, @u{0})",
            (command, change, i) =>
            {
                command.Parameters.AddWithValue("@s" + i, snapshotId);
                command.Parameters.AddWithValue("@t" + i, change.Table);
                command.Parameters.AddWithValue("@h" + i, KeyHash(change.RowKey));
                command.Parameters.AddWithValue("@r" + i, change.RowKey ?? "");
                command.Parameters.AddWithValue("@n" + i, change.Field);
                command.Parameters.AddWithValue("@c" + i, change.Kind.ToString());
                command.Parameters.AddWithValue("@o" + i, ValueId(values, change.OldValue));
                command.Parameters.AddWithValue("@w" + i, ValueId(values, change.NewValue));

                AddLocation(command, i, change.Location);
            });
    }

    /// <summary>
    /// The pool id for a cell's text, or NULL when the cell held nothing.
    ///
    /// NULL means "the cell was empty" in this schema - it is not a stand-in for "no
    /// id was found". So a text that has one and a text that does not cannot both come
    /// out as NULL, which is what this used to do: it was a single expression whose
    /// fallback covered both cases, and a lookup that missed turned "this cell changed
    /// to X" into "this cell was emptied" in every report that read it afterwards.
    ///
    /// A miss is not something to render. <see cref="ResolveValues"/> inserts every text
    /// before reading the ids back, so one can only go missing if something removed it in
    /// between - the value pool collector, if it ever ran outside the lock that is
    /// supposed to keep it away. Failing here is how that would be found, rather than
    /// being read months later as a blanked cell.
    ///
    /// Internal so it can be tested directly. Reaching it through a conversion means
    /// arranging for a value to vanish mid-transaction, which is not something a test
    /// can ask for.
    /// </summary>
    internal static object ValueId(IReadOnlyDictionary<string, long> values, string? text)
    {
        if (text is null)
            return DBNull.Value;

        if (values.TryGetValue(text, out long id))
            return id;

        throw new TabbitException(
            $"The value pool has no id for a cell holding `{Ellipsis(text)}`, which was " +
            $"put there moments ago. Something removed it between the insert and the read " +
            $"- a prune of another branch is the only thing that does. The snapshot has " +
            $"not been written.");
    }

    /// <summary>Enough of a value to recognise it, without putting a cell of prose in a log.</summary>
    private static string Ellipsis(string text)
        => text.Length <= 60 ? text : text.Substring(0, 60) + "...";

    private static void AddLocation(MySqlCommand command, int i, SummaryLocation? location)
    {
        command.Parameters.AddWithValue("@f" + i, (object?)Truncate(location?.File, 255) ?? DBNull.Value);
        command.Parameters.AddWithValue("@sh" + i, (object?)Truncate(location?.Sheet, 128) ?? DBNull.Value);
        command.Parameters.AddWithValue("@cl" + i, (object?)Truncate(location?.Cell, 16) ?? DBNull.Value);
        command.Parameters.AddWithValue("@u" + i, (object?)location?.Url ?? DBNull.Value);
    }

    // ------------------------------------------------------------ state after

    /// <summary>
    /// Moves the branch's current state to what this snapshot describes.
    ///
    /// Incrementally, from the change list rather than by rewriting each changed table.
    /// A table of a million rows where one row moved must cost one row of writing, or
    /// the descent that found it cheaply is wasted at the last step.
    /// </summary>
    private void ApplyState(
        MySqlTransaction transaction,
        long snapshotId,
        SnapshotWrite write,
        IReadOnlyDictionary<string, long> values)
    {
        var fingerprint = write.Fingerprint;

        // Tables and their columns. Small enough - a project has hundreds, not millions
        // - that replacing the changed ones wholesale is simpler and costs nothing.
        var live = new HashSet<string>(fingerprint.Tables.Select(t => t.Name), StringComparer.Ordinal);

        DeleteMissing(transaction, "table_current", "table_name", live);
        DeleteMissing(transaction, "field_current", "table_name", live);
        DeleteMissing(transaction, "row_current", "table_name", live);
        DeleteMissing(transaction, "cell_current", "table_name", live);

        foreach (var table in fingerprint.Tables)
        {
            UpsertTable(transaction, snapshotId, table);

            if (write.ChangedTables.Contains(table.Name))
                ReplaceFields(transaction, snapshotId, table);
        }

        ApplyRows(transaction, snapshotId, write);
        ApplyCells(transaction, snapshotId, write, values);
        ApplyEntities(transaction, snapshotId, fingerprint);
    }

    private void UpsertTable(MySqlTransaction transaction, long snapshotId, TableFingerprint table)
    {
        using var command = Command(transaction, @"
            INSERT INTO table_current
                (project_id, branch, table_name, table_hash, schema_hash, snapshot_id)
            VALUES (@project, @branch, @table, @hash, @schema, @snapshot)
            ON DUPLICATE KEY UPDATE
                table_hash = VALUES(table_hash),
                schema_hash = VALUES(schema_hash),
                snapshot_id = VALUES(snapshot_id)");

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);
        command.Parameters.AddWithValue("@table", table.Name);
        command.Parameters.AddWithValue("@hash", table.Hash);
        command.Parameters.AddWithValue("@schema", table.SchemaHash);
        command.Parameters.AddWithValue("@snapshot", snapshotId);

        command.ExecuteNonQuery();
    }

    private void ReplaceFields(MySqlTransaction transaction, long snapshotId, TableFingerprint table)
    {
        using (var delete = Command(transaction, @"
            DELETE FROM field_current
            WHERE project_id = @project AND branch = @branch AND table_name = @table"))
        {
            delete.Parameters.AddWithValue("@project", _projectId);
            delete.Parameters.AddWithValue("@branch", _branch);
            delete.Parameters.AddWithValue("@table", table.Name);
            delete.ExecuteNonQuery();
        }

        var ordered = table.Fields.Select((field, ordinal) => (field, ordinal)).ToList();

        Batched(transaction, ordered, WriteBatch,
            @"INSERT INTO field_current
                 (project_id, branch, table_name, field_name, field_hash, descriptor, ordinal, snapshot_id)
              VALUES ",
            "(@p{0}, @b{0}, @t{0}, @n{0}, @h{0}, @d{0}, @o{0}, @s{0})",
            (command, item, i) =>
            {
                command.Parameters.AddWithValue("@p" + i, _projectId);
                command.Parameters.AddWithValue("@b" + i, _branch);
                command.Parameters.AddWithValue("@t" + i, table.Name);
                command.Parameters.AddWithValue("@n" + i, item.field.Name);
                command.Parameters.AddWithValue("@h" + i, item.field.Hash);
                command.Parameters.AddWithValue("@d" + i, SnapshotDiff.DescriptorOf(item.field));
                command.Parameters.AddWithValue("@o" + i, item.ordinal);
                command.Parameters.AddWithValue("@s" + i, snapshotId);
            });
    }

    private void ApplyRows(MySqlTransaction transaction, long snapshotId, SnapshotWrite write)
    {
        var hashes = write.RowHashes;

        var upserts = write.Changes.Rows
            .Where(r => r.Kind != ChangeKind.Removed)
            .Select(r => (r.Table, r.RowKey, Hash: hashes[(r.Table, r.RowKey)]))
            .ToList();

        Batched(transaction, upserts, WriteBatch,
            @"INSERT INTO row_current
                 (project_id, branch, table_name, row_key_hash, row_key, row_hash, snapshot_id)
              VALUES ",
            "(@p{0}, @b{0}, @t{0}, @k{0}, @r{0}, @h{0}, @s{0})",
            (command, row, i) =>
            {
                command.Parameters.AddWithValue("@p" + i, _projectId);
                command.Parameters.AddWithValue("@b" + i, _branch);
                command.Parameters.AddWithValue("@t" + i, row.Table);
                command.Parameters.AddWithValue("@k" + i, KeyHash(row.RowKey));
                command.Parameters.AddWithValue("@r" + i, row.RowKey ?? "");
                command.Parameters.AddWithValue("@h" + i, row.Hash);
                command.Parameters.AddWithValue("@s" + i, snapshotId);
            },
            @" ON DUPLICATE KEY UPDATE
                 row_hash = VALUES(row_hash), snapshot_id = VALUES(snapshot_id)");

        foreach (var removed in write.Changes.Rows.Where(r => r.Kind == ChangeKind.Removed))
        {
            using var command = Command(transaction, @"
                DELETE FROM row_current
                WHERE project_id = @project AND branch = @branch
                  AND table_name = @table AND row_key_hash = @key");

            command.Parameters.AddWithValue("@project", _projectId);
            command.Parameters.AddWithValue("@branch", _branch);
            command.Parameters.AddWithValue("@table", removed.Table);
            command.Parameters.AddWithValue("@key", KeyHash(removed.RowKey));

            command.ExecuteNonQuery();
        }
    }

    private void ApplyCells(
        MySqlTransaction transaction,
        long snapshotId,
        SnapshotWrite write,
        IReadOnlyDictionary<string, long> values)
    {
        var upserts = write.Changes.Cells.Where(c => c.Kind != ChangeKind.Removed).ToList();

        Batched(transaction, upserts, WriteBatch,
            @"INSERT INTO cell_current
                 (project_id, branch, table_name, row_key_hash, field_name, row_key, value_id, snapshot_id)
              VALUES ",
            "(@p{0}, @b{0}, @t{0}, @k{0}, @n{0}, @r{0}, @v{0}, @s{0})",
            (command, cell, i) =>
            {
                command.Parameters.AddWithValue("@p" + i, _projectId);
                command.Parameters.AddWithValue("@b" + i, _branch);
                command.Parameters.AddWithValue("@t" + i, cell.Table);
                command.Parameters.AddWithValue("@k" + i, KeyHash(cell.RowKey));
                command.Parameters.AddWithValue("@n" + i, cell.Field);
                command.Parameters.AddWithValue("@r" + i, cell.RowKey ?? "");
                command.Parameters.AddWithValue("@v" + i, ValueId(values, cell.NewValue));
                command.Parameters.AddWithValue("@s" + i, snapshotId);
            },
            @" ON DUPLICATE KEY UPDATE
                 value_id = VALUES(value_id), snapshot_id = VALUES(snapshot_id)");

        foreach (var removed in write.Changes.Cells.Where(c => c.Kind == ChangeKind.Removed))
        {
            using var command = Command(transaction, @"
                DELETE FROM cell_current
                WHERE project_id = @project AND branch = @branch AND table_name = @table
                  AND row_key_hash = @key AND field_name = @field");

            command.Parameters.AddWithValue("@project", _projectId);
            command.Parameters.AddWithValue("@branch", _branch);
            command.Parameters.AddWithValue("@table", removed.Table);
            command.Parameters.AddWithValue("@key", KeyHash(removed.RowKey));
            command.Parameters.AddWithValue("@field", removed.Field);

            command.ExecuteNonQuery();
        }
    }

    private void ApplyEntities(MySqlTransaction transaction, long snapshotId, ModelFingerprint fingerprint)
    {
        var live = new List<(EntityKind Kind, EntityFingerprint Entity)>();

        foreach (var entity in fingerprint.Enums)
            live.Add((EntityKind.Enum, entity));

        foreach (var entity in fingerprint.ConstantSets)
            live.Add((EntityKind.Constants, entity));

        // A handful of entities with a handful of members each. Replacing them all is
        // simpler than tracking which moved and costs nothing at this size.
        foreach (var name in new[] { "entity_current", "member_current" })
        {
            using var delete = Command(transaction,
                $"DELETE FROM {name} WHERE project_id = @project AND branch = @branch");

            delete.Parameters.AddWithValue("@project", _projectId);
            delete.Parameters.AddWithValue("@branch", _branch);
            delete.ExecuteNonQuery();
        }

        Batched(transaction, live, WriteBatch,
            @"INSERT INTO entity_current
                 (project_id, branch, entity_kind, entity_name, entity_hash, snapshot_id) VALUES ",
            "(@p{0}, @b{0}, @k{0}, @n{0}, @h{0}, @s{0})",
            (command, item, i) =>
            {
                command.Parameters.AddWithValue("@p" + i, _projectId);
                command.Parameters.AddWithValue("@b" + i, _branch);
                command.Parameters.AddWithValue("@k" + i, item.Kind.ToString());
                command.Parameters.AddWithValue("@n" + i, item.Entity.Name);
                command.Parameters.AddWithValue("@h" + i, item.Entity.Hash);
                command.Parameters.AddWithValue("@s" + i, snapshotId);
            });

        var members = live
            .SelectMany(item => item.Entity.Members.Select(m => (item.Kind, item.Entity.Name, Member: m)))
            .ToList();

        Batched(transaction, members, WriteBatch,
            @"INSERT INTO member_current
                 (project_id, branch, entity_kind, entity_name, member_name, member_value, snapshot_id)
              VALUES ",
            "(@p{0}, @b{0}, @k{0}, @e{0}, @m{0}, @v{0}, @s{0})",
            (command, item, i) =>
            {
                command.Parameters.AddWithValue("@p" + i, _projectId);
                command.Parameters.AddWithValue("@b" + i, _branch);
                command.Parameters.AddWithValue("@k" + i, item.Kind.ToString());
                command.Parameters.AddWithValue("@e" + i, item.Name);
                command.Parameters.AddWithValue("@m" + i, item.Member.Name);
                command.Parameters.AddWithValue("@v" + i, item.Member.Value ?? "");
                command.Parameters.AddWithValue("@s" + i, snapshotId);
            });
    }

    private void DeleteMissing(
        MySqlTransaction transaction, string table, string column, IReadOnlyCollection<string> keep)
    {
        using var command = Command(transaction, "");

        var parameters = new List<string>(keep.Count);
        int i = 0;

        foreach (var name in keep)
        {
            parameters.Add("@n" + i);
            command.Parameters.AddWithValue("@n" + i, name);
            i++;
        }

        command.CommandText =
            $"DELETE FROM {table} WHERE project_id = @project AND branch = @branch" +
            (parameters.Count == 0 ? "" : $" AND {column} NOT IN ({string.Join(", ", parameters)})");

        command.Parameters.AddWithValue("@project", _projectId);
        command.Parameters.AddWithValue("@branch", _branch);

        command.ExecuteNonQuery();
    }

    private void InsertStats(MySqlTransaction transaction, long snapshotId, SummaryDocument summary)
    {
        var totals = summary.Data.Totals;

        using (var command = Command(transaction, @"
            INSERT INTO snapshot_stat
                (snapshot_id, tables, rows_count, fields, cells, empty_cells, content_bytes,
                 enums, enum_labels, constant_sets, constants, reference_fields, array_fields)
            VALUES (@s, @t, @r, @f, @c, @e, @cb, @en, @el, @cs, @co, @rf, @af)"))
        {
            command.Parameters.AddWithValue("@s", snapshotId);
            command.Parameters.AddWithValue("@t", totals.Tables);
            command.Parameters.AddWithValue("@r", totals.Rows);
            command.Parameters.AddWithValue("@f", totals.Fields);
            command.Parameters.AddWithValue("@c", totals.Cells);
            command.Parameters.AddWithValue("@e", totals.EmptyCells);
            command.Parameters.AddWithValue("@cb", totals.ContentBytes);
            command.Parameters.AddWithValue("@en", totals.Enums);
            command.Parameters.AddWithValue("@el", totals.EnumLabels);
            command.Parameters.AddWithValue("@cs", totals.ConstantSets);
            command.Parameters.AddWithValue("@co", totals.Constants);
            command.Parameters.AddWithValue("@rf", totals.ReferenceFields);
            command.Parameters.AddWithValue("@af", totals.ArrayFields);

            command.ExecuteNonQuery();
        }

        Batched(transaction, summary.Data.Tables, WriteBatch,
            @"INSERT INTO table_stat
                 (snapshot_id, table_name, row_count, field_count, cell_count,
                  empty_cell_count, content_bytes, table_hash, schema_hash) VALUES ",
            "(@s{0}, @t{0}, @r{0}, @f{0}, @c{0}, @e{0}, @b{0}, @h{0}, @x{0})",
            (command, table, i) =>
            {
                command.Parameters.AddWithValue("@s" + i, snapshotId);
                command.Parameters.AddWithValue("@t" + i, table.Name);
                command.Parameters.AddWithValue("@r" + i, table.RowCount);
                command.Parameters.AddWithValue("@f" + i, table.FieldCount);
                command.Parameters.AddWithValue("@c" + i, table.CellCount);
                command.Parameters.AddWithValue("@e" + i, table.EmptyCellCount);
                command.Parameters.AddWithValue("@b" + i, table.ContentBytes);
                command.Parameters.AddWithValue("@h" + i, table.Hash);
                command.Parameters.AddWithValue("@x" + i, table.SchemaHash);
            });
    }

    // -------------------------------------------------------------- plumbing

    private MySqlCommand Command(MySqlTransaction transaction, string sql)
        => new MySqlCommand(sql, _connection, transaction);

    /// <summary>
    /// Writes a list as multi-row INSERTs.
    ///
    /// One statement per row would make a snapshot of a hundred thousand changes a
    /// hundred thousand round trips, which on a remote database is the difference
    /// between a build that records history and one that does not.
    /// </summary>
    private void Batched<T>(
        MySqlTransaction transaction,
        IReadOnlyList<T> items,
        int batch,
        string prefix,
        string rowTemplate,
        Action<MySqlCommand, T, int> bind,
        string suffix = "")
    {
        foreach (var chunk in Chunk(items, batch))
        {
            using var command = Command(transaction, "");

            var rows = new List<string>(chunk.Count);

            for (int i = 0; i < chunk.Count; i++)
            {
                rows.Add(string.Format(rowTemplate, i));
                bind(command, chunk[i], i);
            }

            command.CommandText = prefix + string.Join(", ", rows) + suffix;
            command.ExecuteNonQuery();
        }
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (int start = 0; start < items.Count; start += size)
            yield return items.Skip(start).Take(size).ToList();
    }

    /// <summary>SHA-256 of a row key, which is what the state tables are indexed on.</summary>
    public static byte[] KeyHash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key ?? ""));

    /// <summary>SHA-256 of a value, which is its address in the pool.</summary>
    public static byte[] ValueHash(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));

    private static byte[] Compress(string text)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>Reads back what <see cref="Compress"/> wrote.</summary>
    public static string Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private static DateTime? ParseUtc(string? iso)
        => string.IsNullOrEmpty(iso)
            ? (DateTime?)null
            : DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.RoundtripKind).UtcDateTime;

    private static string? Truncate(string? value, int length)
        => value is null || value.Length <= length ? value : value.Substring(0, length);
}

/// <summary>One snapshot, as the store reads it back.</summary>
internal sealed class SnapshotRow
{
    public long Id { get; set; }
    public required string CommitHash { get; set; }
    public long Seq { get; set; }
    public required string ModelHash { get; set; }
    public DateTime? CommittedAt { get; set; }
    public bool Dirty { get; set; }
}

/// <summary>Everything one snapshot needs written.</summary>
internal sealed class SnapshotWrite
{
    public required SummaryDocument Summary { get; set; }

    public required ModelFingerprint Fingerprint { get; set; }

    public required SnapshotChanges Changes { get; set; }

    /// <summary>Position in the branch's chain. Re-checked under the write lock.</summary>
    public long Seq { get; set; }

    public long? ParentId { get; set; }

    /// <summary>
    /// Whether the parent snapshot's commit is this commit's parent in the repository.
    ///
    /// Recorded here rather than worked out when the history is read, because only a
    /// conversion has the repository to ask. False means nothing converted the commits
    /// in between, so these changes cover more than this commit made - which a report
    /// has to say, or it credits one person with several people's work.
    /// </summary>
    public bool FollowsParent { get; set; } = true;

    /// <summary>Tables whose columns need rewriting, which is those whose schema moved.</summary>
    public required HashSet<string> ChangedTables { get; set; }

    /// <summary>Row hash by table and key, for the rows being written.</summary>
    public required Dictionary<(string Table, string RowKey), string> RowHashes { get; set; }
}
