using System;
using System.Collections.Generic;
using System.Linq;
using MySqlConnector;
using Tabbit.History;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The history, against a real MySQL.
///
/// A real engine rather than a fake, for the same reason the database exporters are
/// tested against real engines: what is being checked is engine behaviour. Whether
/// `INSERT IGNORE` and `ON DUPLICATE KEY UPDATE` make a second recording of the same
/// commit a no-op rather than a duplicate, whether a migration run twice is harmless,
/// whether the transaction leaves nothing behind when a write fails. A fake would
/// confirm the code makes the calls it makes, which is not the question.
///
/// The models are built in memory rather than converted from workbooks. Every test here
/// is about a specific edit - one cell, one column, one deleted row - and expressing
/// each of those as a second .xlsx would make reviewing the suite a matter of opening
/// Excel.
/// </summary>
[Collection("databases")]
public class HistoryStoreTests : IDisposable
{
    private const string Database = "tabbit_history_test";

    private readonly string _projectKey = "p" + Guid.NewGuid().ToString("N").Substring(0, 12);

    private static readonly (string, ValueType)[] Columns =
    {
        ("id", ValueType.Int32),
        ("name", ValueType.String),
        ("power", ValueType.Int32),
    };

    public HistoryStoreTests()
    {
        DatabaseFixture.EnsureRunning();

        using var connection = new MySqlConnection(ServerConnectionString);
        connection.Open();

        using var command = new MySqlCommand(
            $"CREATE DATABASE IF NOT EXISTS `{Database}` DEFAULT CHARACTER SET utf8mb4", connection);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Each test gets its own project key, so the shared database does not have to be
    /// dropped between them - and so they can run in any order.
    /// </summary>
    public void Dispose() { }

    private static string ServerConnectionString => DatabaseFixture.MySqlConnectionString
        .Replace("Database=tabbit_test", "Database=mysql");

    private static string ConnectionString => DatabaseFixture.MySqlConnectionString
        .Replace("Database=tabbit_test", "Database=" + Database);

    private HistoryStore Open(string branch = "main")
        => HistoryStore.Open(ConnectionString, _projectKey, branch);

    // ------------------------------------------------------------- fixtures

    private static Model Items(params object[][] rows)
        => ModelFactory.Of(ModelFactory.Table("Item", Columns, rows));

    private static Model Default() => Items(
        new object[] { 1, "Sword", 10 },
        new object[] { 2, "Shield", 20 });

    private static CommitInfo Commit(string hash, string author = "Kim", int minute = 0)
        => CommitInfo.Resolve(
            new Options
            {
                Repository = System.IO.Path.GetTempPath(),
                Commit = hash,
                Branch = "main",
                CommitAuthor = $"{author} <{author.ToLowerInvariant()}@example.com>",
                CommitDate = $"2026-08-03T10:{minute:00}:00+09:00",
            },
            new Tabbit.Recipe.RecipeModel());

    private RecordOutcome Record(HistoryStore store, Model model, CommitInfo commit,
                                 HistoryRecipe recipe = null)
    {
        var summary = SummaryBuilder.Build(model, commit, null);
        var fingerprint = ModelFingerprint.Of(model);

        return HistoryRecorder.Record(
            store, summary, fingerprint, commit, recipe ?? new HistoryRecipe(), out _);
    }

    // ---------------------------------------------------------------- tests

    /// <summary>
    /// A value something still refers to cannot be deleted.
    ///
    /// This is the whole of what migration 5 bought. The value pool is shared by every
    /// project and branch, and the write lock that a conversion and a prune both take is
    /// named per branch - so a prune of `main` and a conversion of `dev` hold different
    /// locks, and the collector could delete a value between another conversion reading
    /// its id and writing the reference. There were no constraints, so the result was a
    /// row pointing at nothing; and every query LEFT JOINs the pool, so the reference
    /// came back NULL, which this schema reads as "the cell was empty".
    ///
    /// The delete is attempted directly here rather than through the collector, because
    /// the collector only deletes what nothing refers to - the point is what the database
    /// does when something does.
    /// </summary>
    [Fact]
    public void A_value_something_refers_to_cannot_be_deleted()
    {
        using (var store = Open())
            Record(store, Default(), Commit("aaaa1111"));

        using var connection = new MySqlConnection(ConnectionString);
        connection.Open();

        long referenced;

        using (var pick = new MySqlCommand(
                   "SELECT new_value_id FROM cell_change WHERE new_value_id IS NOT NULL LIMIT 1",
                   connection))
        {
            var id = pick.ExecuteScalar();

            Assert.NotNull(id);
            Assert.NotEqual(DBNull.Value, id);

            referenced = Convert.ToInt64(id);
        }

        using var delete = new MySqlCommand("DELETE FROM value WHERE id = @id", connection);
        delete.Parameters.AddWithValue("@id", referenced);

        var ex = Assert.Throws<MySqlException>(() => delete.ExecuteNonQuery());

        // 1451: a row cannot be deleted while a foreign key still points at it.
        Assert.Equal(1451, ex.Number);
    }

    /// <summary>
    /// A fresh database has to be usable without anyone running anything first, and a
    /// second machine connecting must not trip over the first one's tables.
    /// </summary>
    [Fact]
    public void A_fresh_database_migrates_itself_and_migrating_again_does_nothing()
    {
        using (var first = Open())
            Assert.Empty(first.ReadTables());

        using var second = Open();
        Assert.Empty(second.ReadTables());
    }

    [Fact]
    public void The_first_snapshot_records_every_row()
    {
        using var store = Open();

        Assert.Equal(RecordOutcome.Recorded, Record(store, Default(), Commit("aaa")));

        var tables = store.ReadTables();
        Assert.Single(tables);
        Assert.Equal(new[] { "1", "2" }, store.ReadRowHashes("Item").Keys.OrderBy(k => k));

        Assert.Equal(6, CountRows("cell_change"));
        Assert.Equal(2, CountRows("row_change"));
    }

    /// <summary>
    /// The point of the whole thing: an edit, attributed.
    /// </summary>
    [Fact]
    public void An_edited_cell_is_recorded_with_both_values_and_its_author()
    {
        using var store = Open();

        Record(store, Default(), Commit("aaa", "Kim", 0));

        Record(store, Items(
            new object[] { 1, "Sword", 12 },
            new object[] { 2, "Shield", 20 }), Commit("bbb", "Park", 5));

        var changes = ReadCellChanges("bbb");

        var change = Assert.Single(changes);

        Assert.Equal("Item", change.Table);
        Assert.Equal("1", change.RowKey);
        Assert.Equal("power", change.Field);
        Assert.Equal("Modified", change.Kind);
        Assert.Equal("10", change.OldValue);
        Assert.Equal("12", change.NewValue);
        Assert.Equal("Park", change.Author);

        // And where it is, which is what makes the report worth opening.
        Assert.Equal("Item", change.Sheet);
        Assert.Equal("C3", change.Cell);
    }

    /// <summary>
    /// Two CI jobs on the same commit, or a rerun. One snapshot, not two.
    /// </summary>
    [Fact]
    public void Recording_the_same_commit_twice_changes_nothing()
    {
        using var store = Open();

        Record(store, Default(), Commit("aaa"));

        int cellsAfterFirst = CountRows("cell_change");

        Assert.Equal(RecordOutcome.AlreadyPresent, Record(store, Default(), Commit("aaa")));

        Assert.Equal(1, CountRows("snapshot"));
        Assert.Equal(cellsAfterFirst, CountRows("cell_change"));
    }

    /// <summary>
    /// A new commit whose data is identical still gets a snapshot, so the next real
    /// change is measured from here rather than from further back.
    /// </summary>
    [Fact]
    public void A_commit_that_changed_no_data_still_gets_a_snapshot()
    {
        using var store = Open();

        Record(store, Default(), Commit("aaa", minute: 0));

        int cells = CountRows("cell_change");

        Assert.Equal(RecordOutcome.Recorded, Record(store, Default(), Commit("bbb", minute: 5)));

        Assert.Equal(2, CountRows("snapshot"));
        Assert.Equal(cells, CountRows("cell_change"));
    }

    /// <summary>
    /// The same commit describing different data is a question with no safe answer, so
    /// it is refused rather than resolved.
    /// </summary>
    [Fact]
    public void The_same_commit_with_different_data_is_refused_rather_than_overwritten()
    {
        using var store = Open();

        Record(store, Default(), Commit("aaa"));

        var ex = Assert.Throws<TabbitException>(
            () => Record(store, Items(new object[] { 1, "Sword", 99 }), Commit("aaa")));

        Assert.Equal(Tabbit.History.RecordMessages.ModelDiffersForCommit, ex.MessageId);
        Assert.Equal(1, CountRows("snapshot"));
    }

    /// <summary>
    /// Recording an older commit after a newer one would report the newer one's work as
    /// undone, by the older one's author.
    /// </summary>
    [Fact]
    public void A_commit_behind_the_head_is_refused()
    {
        using var store = Open();

        Record(store, Default(), Commit("bbb", minute: 10));

        var outcome = Record(store,
            Items(new object[] { 1, "Sword", 99 }),
            Commit("aaa", minute: 5));

        Assert.Equal(RecordOutcome.Refused, outcome);
        Assert.Equal(1, CountRows("snapshot"));
    }

    [Fact]
    public void An_out_of_order_commit_is_recorded_when_the_recipe_asks_for_it()
    {
        using var store = Open();

        Record(store, Default(), Commit("bbb", minute: 10));

        var outcome = Record(store,
            Items(new object[] { 1, "Sword", 99 }),
            Commit("aaa", minute: 5),
            new HistoryRecipe { AllowOutOfOrder = true });

        Assert.Equal(RecordOutcome.Recorded, outcome);
        Assert.Equal(2, CountRows("snapshot"));
    }

    /// <summary>
    /// A deleted row's contents are recorded on the way out, and the row leaves the
    /// current state so the next comparison does not keep finding it.
    /// </summary>
    [Fact]
    public void A_deleted_row_is_recorded_and_leaves_the_current_state()
    {
        using var store = Open();

        Record(store, Default(), Commit("aaa", minute: 0));
        Record(store, Items(new object[] { 1, "Sword", 10 }), Commit("bbb", minute: 5));

        Assert.Equal(new[] { "1" }, store.ReadRowHashes("Item").Keys);

        var removed = ReadCellChanges("bbb");

        Assert.Equal(3, removed.Count);
        Assert.All(removed, c => Assert.Equal("Removed", c.Kind));
        Assert.Contains(removed, c => c.OldValue == "Shield");
        Assert.All(removed, c => Assert.Null(c.NewValue));
    }

    /// <summary>
    /// A dropped table takes its rows and cells out of the current state, or the next
    /// comparison reads a table that no longer exists.
    /// </summary>
    [Fact]
    public void A_dropped_table_leaves_nothing_behind_in_the_current_state()
    {
        using var store = Open();

        Record(store, ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }),
            ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 })),
            Commit("aaa", minute: 0));

        Record(store, Items(new object[] { 1, "Sword", 10 }), Commit("bbb", minute: 5));

        Assert.Equal(new[] { "Item" }, store.ReadTables().Keys);
        Assert.Empty(store.ReadRowHashes("Armour"));
        Assert.Empty(store.ReadCells("Armour", new[] { "1" }));
    }

    /// <summary>
    /// The value pool is addressed by content, so a value that repeats is stored once -
    /// which is the difference between a change log that fits and one that does not.
    /// </summary>
    [Fact]
    public void A_value_that_repeats_is_stored_once()
    {
        using var store = Open();

        Record(store, Items(
            new object[] { 1, "same", 7 },
            new object[] { 2, "same", 7 },
            new object[] { 3, "same", 7 }), Commit("aaa"));

        Assert.Equal(1, CountRows("value", "text = 'same'"));
        Assert.Equal(1, CountRows("value", "text = '7'"));
    }

    /// <summary>
    /// Two branches are two histories. A snapshot on one must not be measured against
    /// the other's state, or every branch would read as having rewritten the other.
    /// </summary>
    [Fact]
    public void Branches_hold_separate_histories()
    {
        using (var main = Open("main"))
            Record(main, Default(), Commit("aaa"));

        using var feature = Open("feature");

        // Nothing carried over, so the branch's first snapshot records the model whole.
        Assert.Empty(feature.ReadTables());

        Record(feature, Default(), Commit("ccc"));

        Assert.Equal(2, CountRows("snapshot"));
        Assert.Equal(2, CountRows("table_current"));
    }

    /// <summary>
    /// The whole document is kept per snapshot, so statistics for an old commit are one
    /// row rather than a re-conversion of a workbook that has since changed.
    /// </summary>
    [Fact]
    public void The_snapshot_keeps_the_whole_summary()
    {
        using var store = Open();

        Record(store, Default(), Commit("aaa"));

        using var connection = Connect();
        using var command = new MySqlCommand(
            "SELECT s.summary FROM snapshot s JOIN project p ON p.id = s.project_id " +
            "WHERE p.project_key = @key", connection);

        command.Parameters.AddWithValue("@key", _projectKey);

        string json = HistoryStore.Decompress((byte[])command.ExecuteScalar());

        Assert.Contains("\"tables\": 1", json);
        Assert.Contains("\"rows\": 2", json);
    }

    /// <summary>
    /// A non-ASCII value has to survive the round trip: the connection, the column
    /// collation and the compression of the stored document.
    /// </summary>
    [Fact]
    public void Non_ascii_values_survive_the_round_trip()
    {
        using var store = Open();

        Record(store, Items(new object[] { 1, "한글 이름", 10 }), Commit("aaa"));

        var stored = store.ReadCells("Item", new[] { "1" });

        Assert.Equal("한글 이름", stored[new CellAddress("1", "name")]);
    }

    /// <summary>
    /// A primary index longer than any sane index column, which is why the state tables
    /// are keyed on a hash of the key rather than on the key.
    /// </summary>
    [Fact]
    public void A_very_long_row_key_is_stored_and_found_again()
    {
        string key = new string('k', 2000);

        using var store = Open();

        Record(store, ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.String), ("name", ValueType.String) },
            new object[] { key, "Sword" })), Commit("aaa"));

        Assert.Equal(new[] { key }, store.ReadRowHashes("Item").Keys);
        Assert.Equal("Sword", store.ReadCells("Item", new[] { key })[new CellAddress(key, "name")]);
    }

    // ------------------------------------------------------------- plumbing

    private static MySqlConnection Connect()
    {
        var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private int CountRows(string table, string where = null)
    {
        using var connection = Connect();

        // Scoped to this test's project, since the database is shared across the class.
        string scope = table == "value"
            ? ""
            : table == "snapshot"
                ? " JOIN project p ON p.id = t.project_id AND p.project_key = @key"
                : " JOIN snapshot s ON s.id = t.snapshot_id" +
                  " JOIN project p ON p.id = s.project_id AND p.project_key = @key";

        if (table == "table_current")
            scope = " JOIN project p ON p.id = t.project_id AND p.project_key = @key";

        using var command = new MySqlCommand(
            $"SELECT COUNT(*) FROM {table} t{scope}" +
            (where == null ? "" : (scope == "" ? " WHERE " : " AND ") + where.Replace("text", "t.text")),
            connection);

        if (scope != "")
            command.Parameters.AddWithValue("@key", _projectKey);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private sealed class RecordedCell
    {
        public string Table, RowKey, Field, Kind, OldValue, NewValue, Author, Sheet, Cell;
    }

    private List<RecordedCell> ReadCellChanges(string commit)
    {
        using var connection = Connect();

        using var command = new MySqlCommand(@"
            SELECT c.table_name, c.row_key, c.field_name, c.change_kind,
                   o.text, n.text, s.author_name, c.sheet, c.cell
            FROM cell_change c
            JOIN snapshot s ON s.id = c.snapshot_id
            JOIN project p ON p.id = s.project_id
            LEFT JOIN value o ON o.id = c.old_value_id
            LEFT JOIN value n ON n.id = c.new_value_id
            WHERE p.project_key = @key AND s.commit_hash = @commit
            ORDER BY c.id", connection);

        command.Parameters.AddWithValue("@key", _projectKey);
        command.Parameters.AddWithValue("@commit", commit);

        var changes = new List<RecordedCell>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            changes.Add(new RecordedCell
            {
                Table = reader.GetString(0),
                RowKey = reader.GetString(1),
                Field = reader.GetString(2),
                Kind = reader.GetString(3),
                OldValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                NewValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                Author = reader.IsDBNull(6) ? null : reader.GetString(6),
                Sheet = reader.IsDBNull(7) ? null : reader.GetString(7),
                Cell = reader.IsDBNull(8) ? null : reader.GetString(8),
            });
        }

        return changes;
    }
}
