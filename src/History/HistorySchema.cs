using System;
using System.Collections.Generic;
using MySqlConnector;
using Serilog;

namespace Tabbit.History;

/// <summary>
/// The history's tables, and the migration that brings a database up to them.
///
/// Applied on connect rather than by a separate step. A build machine that has just
/// been pointed at a fresh database should record its first snapshot rather than fail
/// with an instruction to run something else, and the alternative - a migration tool
/// somebody has to remember - is how a schema and the code that reads it drift.
///
/// Migrations are additive and run once, guarded by the versions recorded in
/// `schema_version` rather than by each statement being safe to repeat. Two build
/// machines connecting at once is normal, so the whole thing runs inside a named lock:
/// without one, both would read the same version and both would run the same
/// statements, and one would fail on a race rather than on anything real.
///
/// An applied migration is never edited. It is tempting while a schema is still new -
/// nothing has shipped, so why not - but every database created during that development
/// is already at that version and will never see the change. The column below is
/// migration 2 rather than a line added to migration 1 for exactly that reason.
/// </summary>
internal static class HistorySchema
{
    /// <summary>
    /// What this build expects. A database at a higher version was written by a newer
    /// Tabbit and is left alone rather than downgraded.
    /// </summary>
    public const int Version = 5;

    private const string LockName = "tabbit_history_migrate";

    private const int LockTimeoutSeconds = 60;

    /// <summary>
    /// The lock a snapshot write and a prune both take, named per branch.
    ///
    /// Two conversions of one branch at once - two CI jobs, or a rerun overlapping a
    /// build - each read the branch's head and each write the next sequence number
    /// after it. They are different commits, so the unique key does not stop them, and
    /// the chain ends up with two snapshots claiming the same position: the order they
    /// are read back in is then whatever the storage engine feels like, and every diff
    /// past that point is measured from an arbitrary one of the two.
    ///
    /// This does not protect the value pool, and used to claim it did. The pool is shared
    /// across every project and branch while this lock is per branch, so a prune of one
    /// branch and a conversion of another hold different locks and the collector could
    /// delete a value between a conversion finding it and referencing it. Migration 5
    /// gives the pool foreign keys instead, so the database refuses the delete rather
    /// than a comment asking it not to.
    /// </summary>
    public static string WriteLockFor(int projectId, string branch)
        => $"tabbit_history_write:{projectId}:{branch}";

    /// <summary>
    /// Takes a named lock, or throws saying who is likely holding it.
    /// </summary>
    public static void Lock(MySqlConnection connection, string name, int seconds = LockTimeoutSeconds)
    {
        using var command = new MySqlCommand("SELECT GET_LOCK(@name, @timeout)", connection);

        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@timeout", seconds);

        if (Convert.ToInt32(command.ExecuteScalar() ?? 0) != 1)
        {
            throw new TabbitException(
                $"Another process has held `{name}` for more than {seconds} seconds. That is " +
                $"another conversion of the same branch, or a prune. Wait for it and try again.");
        }
    }

    /// <summary>Releases a named lock. Safe to call when it was never taken.</summary>
    public static void Unlock(MySqlConnection connection, string name)
    {
        using var command = new MySqlCommand("SELECT RELEASE_LOCK(@name)", connection);

        command.Parameters.AddWithValue("@name", name);
        command.ExecuteScalar();
    }

    /// <summary>
    /// Brings the database up to <see cref="Version"/>, or throws saying why it cannot.
    /// </summary>
    public static void Migrate(MySqlConnection connection)
    {
        if (!TryLock(connection))
        {
            throw new TabbitException(
                $"Another process has been migrating the history database for more than " +
                $"{LockTimeoutSeconds} seconds. If nothing else is running, the lock is stale " +
                $"and will clear when its connection closes.");
        }

        try
        {
            Execute(connection, @"
                CREATE TABLE IF NOT EXISTS schema_version (
                    version    INT          NOT NULL,
                    applied_at DATETIME(3)  NOT NULL,
                    PRIMARY KEY (version)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            int current = CurrentVersion(connection);

            if (current > Version)
            {
                throw new TabbitException(
                    $"The history database is at schema version {current}, and this build of " +
                    $"Tabbit understands version {Version}. Upgrade Tabbit rather than " +
                    $"letting an older one write to it.");
            }

            if (current == Version)
                return;

            Log.Information($"Migrating the history database from version {current} to {Version}.");

            for (int version = current + 1; version <= Version; version++)
            {
                var statements = Migrations[version];

                for (int step = 0; step < statements.Length; step++)
                {
                    try
                    {
                        Execute(connection, statements[step]);
                    }
                    catch (MySqlException ex)
                    {
                        // Which migration, which statement, and the server's own words.
                        // Without this the caller gets a bare MySQL error and no way to
                        // tell an already-applied schema from data the new shape refuses
                        // - and migration 5, which adds foreign keys, fails exactly when
                        // the data it is constraining is already wrong.
                        throw new TabbitException(
                            $"Migration {version} failed at statement {step + 1} of " +
                            $"{statements.Length}: {ex.Message}" +
                            Environment.NewLine + Environment.NewLine +
                            statements[step].Trim(), ex);
                    }
                }

                Execute(connection,
                    "INSERT INTO schema_version (version, applied_at) VALUES (@v, UTC_TIMESTAMP(3))",
                    ("@v", version));
            }
        }
        finally
        {
            Execute(connection, "SELECT RELEASE_LOCK(@name)", ("@name", LockName));
        }
    }

    private static bool TryLock(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT GET_LOCK(@name, @timeout)", connection);

        command.Parameters.AddWithValue("@name", LockName);
        command.Parameters.AddWithValue("@timeout", LockTimeoutSeconds);

        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }

    private static int CurrentVersion(MySqlConnection connection)
    {
        using var command = new MySqlCommand(
            "SELECT COALESCE(MAX(version), 0) FROM schema_version", connection);

        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void Execute(MySqlConnection connection, string sql, params (string Name, object Value)[] args)
    {
        using var command = new MySqlCommand(sql, connection);

        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Statements per version.
    ///
    /// Two decisions run through all of it.
    ///
    /// A row key is stored twice: as text for reading, and as a hash for indexing. A
    /// primary index can be a long string, and an index on a column long enough to hold
    /// one exceeds what InnoDB will key on - so keying on the hash removes the length
    /// limit rather than imposing one on the data.
    ///
    /// Values live in a pool addressed by their content. Planning data repeats itself
    /// enormously; storing each cell's text inline would store the string `0` some
    /// millions of times.
    /// </summary>
    private static readonly Dictionary<int, string[]> Migrations = new Dictionary<int, string[]>
    {
        [1] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS project (
                id          INT          NOT NULL AUTO_INCREMENT,
                project_key VARCHAR(128) NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY uq_project (project_key)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS value (
                id   BIGINT     NOT NULL AUTO_INCREMENT,
                hash BINARY(32) NOT NULL,
                text LONGTEXT   NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY uq_value (hash)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS snapshot (
                id            BIGINT       NOT NULL AUTO_INCREMENT,
                project_id    INT          NOT NULL,
                branch        VARCHAR(100) NOT NULL,
                commit_hash   VARCHAR(128) NOT NULL,
                seq           BIGINT       NOT NULL,
                parent_id     BIGINT       NULL,
                model_hash    CHAR(64)     NOT NULL,
                author_name   VARCHAR(190) NULL,
                author_email  VARCHAR(190) NULL,
                committed_at  DATETIME(3)  NULL,
                subject       TEXT         NULL,
                dirty         TINYINT(1)   NOT NULL,
                attributable  TINYINT(1)   NOT NULL,
                converted_at  DATETIME(3)  NOT NULL,
                converted_by  VARCHAR(190) NULL,
                tool_version  VARCHAR(64)  NULL,
                recipe        VARCHAR(255) NULL,
                summary       LONGBLOB     NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY uq_snapshot (project_id, branch, commit_hash),
                KEY ix_chain (project_id, branch, seq)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS snapshot_stat (
                snapshot_id      BIGINT NOT NULL,
                tables           INT    NOT NULL,
                rows_count       INT    NOT NULL,
                fields           INT    NOT NULL,
                cells            BIGINT NOT NULL,
                empty_cells      BIGINT NOT NULL,
                content_bytes    BIGINT NOT NULL,
                enums            INT    NOT NULL,
                enum_labels      INT    NOT NULL,
                constant_sets    INT    NOT NULL,
                constants        INT    NOT NULL,
                reference_fields INT    NOT NULL,
                array_fields     INT    NOT NULL,
                PRIMARY KEY (snapshot_id)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS table_stat (
                snapshot_id      BIGINT       NOT NULL,
                table_name       VARCHAR(128) NOT NULL,
                row_count        INT          NOT NULL,
                field_count      INT          NOT NULL,
                cell_count       BIGINT       NOT NULL,
                empty_cell_count BIGINT       NOT NULL,
                content_bytes    BIGINT       NOT NULL,
                table_hash       CHAR(64)     NOT NULL,
                schema_hash      CHAR(64)     NOT NULL,
                PRIMARY KEY (snapshot_id, table_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS table_current (
                project_id  INT          NOT NULL,
                branch      VARCHAR(100) NOT NULL,
                table_name  VARCHAR(128) NOT NULL,
                table_hash  CHAR(64)     NOT NULL,
                schema_hash CHAR(64)     NOT NULL,
                snapshot_id BIGINT       NOT NULL,
                PRIMARY KEY (project_id, branch, table_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS field_current (
                project_id  INT          NOT NULL,
                branch      VARCHAR(100) NOT NULL,
                table_name  VARCHAR(128) NOT NULL,
                field_name  VARCHAR(128) NOT NULL,
                field_hash  CHAR(64)     NOT NULL,
                descriptor  TEXT         NOT NULL,
                ordinal     INT          NOT NULL,
                snapshot_id BIGINT       NOT NULL,
                PRIMARY KEY (project_id, branch, table_name, field_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS row_current (
                project_id   INT          NOT NULL,
                branch       VARCHAR(100) NOT NULL,
                table_name   VARCHAR(128) NOT NULL,
                row_key_hash BINARY(32)   NOT NULL,
                row_key      TEXT         NOT NULL,
                row_hash     CHAR(64)     NOT NULL,
                snapshot_id  BIGINT       NOT NULL,
                PRIMARY KEY (project_id, branch, table_name, row_key_hash)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS cell_current (
                project_id   INT          NOT NULL,
                branch       VARCHAR(100) NOT NULL,
                table_name   VARCHAR(128) NOT NULL,
                row_key_hash BINARY(32)   NOT NULL,
                field_name   VARCHAR(128) NOT NULL,
                row_key      TEXT         NOT NULL,
                value_id     BIGINT       NULL,
                snapshot_id  BIGINT       NOT NULL,
                PRIMARY KEY (project_id, branch, table_name, row_key_hash, field_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS entity_current (
                project_id  INT          NOT NULL,
                branch      VARCHAR(100) NOT NULL,
                entity_kind VARCHAR(16)  NOT NULL,
                entity_name VARCHAR(128) NOT NULL,
                entity_hash CHAR(64)     NOT NULL,
                snapshot_id BIGINT       NOT NULL,
                PRIMARY KEY (project_id, branch, entity_kind, entity_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS member_current (
                project_id  INT          NOT NULL,
                branch      VARCHAR(100) NOT NULL,
                entity_kind VARCHAR(16)  NOT NULL,
                entity_name VARCHAR(128) NOT NULL,
                member_name VARCHAR(128) NOT NULL,
                member_value TEXT        NOT NULL,
                snapshot_id BIGINT       NOT NULL,
                PRIMARY KEY (project_id, branch, entity_kind, entity_name, member_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS schema_change (
                id           BIGINT       NOT NULL AUTO_INCREMENT,
                snapshot_id  BIGINT       NOT NULL,
                entity_kind  VARCHAR(16)  NOT NULL,
                entity_name  VARCHAR(128) NOT NULL,
                member_name  VARCHAR(128) NULL,
                change_kind  VARCHAR(16)  NOT NULL,
                before_value TEXT         NULL,
                after_value  TEXT         NULL,
                file         VARCHAR(255) NULL,
                sheet        VARCHAR(128) NULL,
                cell         VARCHAR(16)  NULL,
                url          TEXT         NULL,
                PRIMARY KEY (id),
                KEY ix_snapshot (snapshot_id)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS row_change (
                id           BIGINT       NOT NULL AUTO_INCREMENT,
                snapshot_id  BIGINT       NOT NULL,
                table_name   VARCHAR(128) NOT NULL,
                row_key_hash BINARY(32)   NOT NULL,
                row_key      TEXT         NOT NULL,
                change_kind  VARCHAR(16)  NOT NULL,
                PRIMARY KEY (id),
                KEY ix_snapshot (snapshot_id, table_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            @"CREATE TABLE IF NOT EXISTS cell_change (
                id           BIGINT       NOT NULL AUTO_INCREMENT,
                snapshot_id  BIGINT       NOT NULL,
                table_name   VARCHAR(128) NOT NULL,
                row_key_hash BINARY(32)   NOT NULL,
                row_key      TEXT         NOT NULL,
                field_name   VARCHAR(128) NOT NULL,
                change_kind  VARCHAR(16)  NOT NULL,
                old_value_id BIGINT       NULL,
                new_value_id BIGINT       NULL,
                file         VARCHAR(255) NULL,
                sheet        VARCHAR(128) NULL,
                cell         VARCHAR(16)  NULL,
                url          TEXT         NULL,
                PRIMARY KEY (id),
                KEY ix_snapshot (snapshot_id, table_name),
                KEY ix_cell (table_name, row_key_hash, field_name)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
        },

        [5] = new[]
        {
            // Foreign keys onto the value pool, so the database keeps the invariant that
            // a comment used to.
            //
            // The pool is shared by every project and branch; the write lock is per
            // branch. So a prune of `main` and a conversion of `dev` hold different
            // locks, and the collector - which deletes values nothing refers to - could
            // remove one between a conversion reading its id and writing the reference.
            // There were no constraints at all, so the result was a row pointing at a
            // value that no longer existed, and every query LEFT JOINs the pool: the
            // reference came back NULL, which this schema reads as "the cell was empty".
            //
            // RESTRICT rather than CASCADE or SET NULL. The collector's delete is the
            // thing that has to fail; the other two would carry out the corruption
            // tidily. It fails with a foreign key error, the collector skips that value,
            // and the next prune collects it once the reference really has gone.
            //
            // Added as three separate statements because a failure part-way through
            // should say which table it was on.
            @"ALTER TABLE cell_current
                ADD CONSTRAINT fk_cell_current_value
                FOREIGN KEY (value_id) REFERENCES value (id)
                ON DELETE RESTRICT ON UPDATE RESTRICT",

            @"ALTER TABLE cell_change
                ADD CONSTRAINT fk_cell_change_old_value
                FOREIGN KEY (old_value_id) REFERENCES value (id)
                ON DELETE RESTRICT ON UPDATE RESTRICT",

            @"ALTER TABLE cell_change
                ADD CONSTRAINT fk_cell_change_new_value
                FOREIGN KEY (new_value_id) REFERENCES value (id)
                ON DELETE RESTRICT ON UPDATE RESTRICT",
        },

        [4] = new[]
        {
            // Set when a snapshot's change detail has been removed to reclaim space.
            // The snapshot, its statistics and its stored summary stay - what goes is
            // the cell-by-cell log. A query over a range holding one of these says so
            // rather than reporting an empty changeset as "nothing changed".
            @"ALTER TABLE snapshot
                ADD COLUMN pruned TINYINT(1) NOT NULL DEFAULT 0 AFTER attributable",
        },

        [3] = new[]
        {
            // Set when a dropped column and an added one turn out to hold the same
            // values in the same rows - which is what a rename looks like from here.
            @"ALTER TABLE schema_change
                ADD COLUMN renamed_from VARCHAR(128) NULL AFTER member_name",
        },

        [2] = new[]
        {
            // Whether a snapshot's commit directly follows its parent snapshot's.
            // Recorded when the snapshot is written, because only a conversion has the
            // repository to ask - and false means the changes cover more than one
            // commit's work, which a report has to say rather than let a reader assume.
            //
            // Existing rows default to following: claiming a gap that cannot be checked
            // would put a warning on every snapshot recorded before this column existed.
            @"ALTER TABLE snapshot
                ADD COLUMN follows_parent TINYINT(1) NOT NULL DEFAULT 1 AFTER parent_id",
        },
    };
}
