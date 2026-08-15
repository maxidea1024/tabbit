using System.Linq;
using Tabbit.History;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// What changed between what the history holds and what a conversion produced.
///
/// Two properties, pulling against each other. It has to report every change, because
/// one it misses is an edit lost from the history with no symptom - the build succeeds
/// and the report simply does not mention it. And it has to read as little as it can,
/// because every level of the descent is a round trip to a remote database and a build
/// that changed nothing should cost one query.
/// </summary>
public class SnapshotDiffTests
{
    private static readonly (string, ValueType)[] Columns =
    {
        ("id", ValueType.Int32),
        ("name", ValueType.String),
        ("power", ValueType.Int32),
    };

    private static Model Items(params object[][] rows)
        => ModelFactory.Of(ModelFactory.Table("Item", Columns, rows));

    private static Model Default() => Items(
        new object[] { 1, "Sword", 10 },
        new object[] { 2, "Shield", 20 });

    private static SnapshotChanges Between(Model before, Model after)
        => SnapshotDiff.Compute(ModelFingerprint.Of(after), InMemoryHistoryState.Of(ModelFingerprint.Of(before)));

    // ------------------------------------------------------------- the cheap case

    [Fact]
    public void An_unchanged_model_reports_nothing()
    {
        Assert.True(Between(Default(), Default()).IsEmpty);
    }

    /// <summary>
    /// The reason the hashes are arranged in three levels.
    ///
    /// A build where nothing changed must compare one hash per table and stop. Reading
    /// rows or cells anyway would send the dataset across the network on every build to
    /// discover there was nothing to send.
    /// </summary>
    [Fact]
    public void An_unchanged_model_reads_no_rows_and_no_cells()
    {
        var state = InMemoryHistoryState.Of(ModelFingerprint.Of(Default()));

        SnapshotDiff.Compute(ModelFingerprint.Of(Default()), state);

        Assert.Empty(state.RowReads);
        Assert.Empty(state.CellReads);
        Assert.Empty(state.FieldReads);
    }

    /// <summary>
    /// An untouched table costs nothing even when its neighbour changed.
    /// </summary>
    [Fact]
    public void Only_the_table_that_changed_is_read()
    {
        var before = ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }),
            ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 }));

        var after = ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 12 }),
            ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 }));

        var state = InMemoryHistoryState.Of(ModelFingerprint.Of(before));

        SnapshotDiff.Compute(ModelFingerprint.Of(after), state);

        Assert.Equal(new[] { "Item" }, state.RowReads);
    }

    /// <summary>
    /// And within a changed table, only the row that moved.
    /// </summary>
    [Fact]
    public void Only_the_row_that_changed_has_its_cells_read()
    {
        var state = InMemoryHistoryState.Of(ModelFingerprint.Of(Default()));

        SnapshotDiff.Compute(ModelFingerprint.Of(Items(
            new object[] { 1, "Sword", 12 },
            new object[] { 2, "Shield", 20 })), state);

        Assert.Equal(new[] { "1" }, state.CellReads);
    }

    // ------------------------------------------------------------------ cells

    [Fact]
    public void An_edited_cell_is_reported_with_both_values()
    {
        var changes = Between(Default(), Items(
            new object[] { 1, "Sword", 12 },
            new object[] { 2, "Shield", 20 }));

        var cell = Assert.Single(changes.Cells);

        Assert.Equal("Item", cell.Table);
        Assert.Equal("1", cell.RowKey);
        Assert.Equal("power", cell.Field);
        Assert.Equal(ChangeKind.Modified, cell.Kind);
        Assert.Equal("10", cell.OldValue);
        Assert.Equal("12", cell.NewValue);

        var row = Assert.Single(changes.Rows);
        Assert.Equal(ChangeKind.Modified, row.Kind);
    }

    /// <summary>
    /// The change carries where the cell is, which is what makes a report worth opening:
    /// the answer to "who changed this" is next to a link to the cell itself.
    /// </summary>
    [Fact]
    public void An_edited_cell_says_where_it_is()
    {
        var changes = Between(Default(), Items(
            new object[] { 1, "Sword", 12 },
            new object[] { 2, "Shield", 20 }));

        var location = Assert.Single(changes.Cells).Location;

        Assert.Equal("Item", location.Sheet);
        Assert.Equal("memory.xlsx", location.File);
        // Third column, first data row - the factory puts the marker and the header
        // above it, as a sheet does.
        Assert.Equal("C3", location.Cell);
    }

    [Fact]
    public void Clearing_a_cell_is_a_change_to_nothing_rather_than_no_change()
    {
        var changes = Between(Default(), Items(
            new object[] { 1, null, 10 },
            new object[] { 2, "Shield", 20 }));

        var cell = Assert.Single(changes.Cells);

        Assert.Equal(ChangeKind.Modified, cell.Kind);
        Assert.Equal("Sword", cell.OldValue);
        Assert.Null(cell.NewValue);
    }

    [Fact]
    public void Emptying_a_cell_is_not_the_same_as_clearing_it()
    {
        var changes = Between(Default(), Items(
            new object[] { 1, "", 10 },
            new object[] { 2, "Shield", 20 }));

        Assert.Equal("", Assert.Single(changes.Cells).NewValue);
    }

    // ------------------------------------------------------------------- rows

    [Fact]
    public void A_new_row_is_reported_with_every_value_it_holds()
    {
        var changes = Between(Default(), Items(
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 },
            new object[] { 3, "Bow", 15 }));

        var row = Assert.Single(changes.Rows);
        Assert.Equal("3", row.RowKey);
        Assert.Equal(ChangeKind.Added, row.Kind);

        Assert.Equal(3, changes.Cells.Count);
        Assert.All(changes.Cells, c => Assert.Equal(ChangeKind.Added, c.Kind));
        Assert.Equal(new[] { "3", "Bow", "15" }, changes.Cells.Select(c => c.NewValue));
    }

    /// <summary>
    /// A blank cell in a new row is not a change - there was nothing and there is
    /// nothing. Recording it would fill the history with rows saying so.
    /// </summary>
    [Fact]
    public void A_blank_cell_in_a_new_row_is_not_reported()
    {
        var changes = Between(Default(), Items(
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 },
            new object[] { 3, null, 15 }));

        Assert.Equal(2, changes.Cells.Count);
        Assert.DoesNotContain(changes.Cells, c => c.Field == "name");
    }

    /// <summary>
    /// What a deleted row held is recorded on the way out. Without it the history can
    /// say a row went but not what was lost, which is the question actually asked.
    /// </summary>
    [Fact]
    public void A_deleted_row_is_reported_with_what_it_held()
    {
        var changes = Between(Default(), Items(new object[] { 1, "Sword", 10 }));

        var row = Assert.Single(changes.Rows);
        Assert.Equal("2", row.RowKey);
        Assert.Equal(ChangeKind.Removed, row.Kind);

        Assert.Equal(3, changes.Cells.Count);
        Assert.All(changes.Cells, c => Assert.Equal(ChangeKind.Removed, c.Kind));
        Assert.Equal(new[] { "2", "Shield", "20" }, changes.Cells.Select(c => c.OldValue));
        Assert.All(changes.Cells, c => Assert.Null(c.NewValue));
    }

    /// <summary>
    /// Rows are followed by their primary index, so moving one in the sheet is not an
    /// edit to it.
    /// </summary>
    [Fact]
    public void Reordering_rows_changes_nothing()
    {
        var changes = Between(Default(), Items(
            new object[] { 2, "Shield", 20 },
            new object[] { 1, "Sword", 10 }));

        Assert.True(changes.IsEmpty);
    }

    /// <summary>
    /// Changing a row's index is a different row, not an edited one - the key is what
    /// follows a row across snapshots, and there is nothing else to follow it by.
    /// </summary>
    [Fact]
    public void Changing_a_rows_index_reads_as_a_delete_and_an_add()
    {
        var changes = Between(Default(), Items(
            new object[] { 1, "Sword", 10 },
            new object[] { 9, "Shield", 20 }));

        Assert.Equal(2, changes.Rows.Count);
        Assert.Contains(changes.Rows, r => r.RowKey == "2" && r.Kind == ChangeKind.Removed);
        Assert.Contains(changes.Rows, r => r.RowKey == "9" && r.Kind == ChangeKind.Added);
    }

    // ----------------------------------------------------------------- tables

    [Fact]
    public void A_new_table_is_reported_whole()
    {
        var before = ModelFactory.Of(ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }));

        var after = ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }),
            ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 }));

        var changes = Between(before, after);

        Assert.Contains(changes.Schema,
            s => s.EntityKind == EntityKind.Table && s.EntityName == "Armour" && s.Kind == ChangeKind.Added);

        // Its columns too, so a later query can describe a table it never saw arrive
        // column by column.
        Assert.Equal(3, changes.Schema.Count(s => s.EntityKind == EntityKind.Field && s.EntityName == "Armour"));

        Assert.Single(changes.Rows);
        Assert.Equal(3, changes.Cells.Count);
    }

    [Fact]
    public void A_removed_table_takes_its_rows_with_it()
    {
        var before = ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }),
            ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 }));

        var after = ModelFactory.Of(ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }));

        var changes = Between(before, after);

        Assert.Contains(changes.Schema,
            s => s.EntityKind == EntityKind.Table && s.EntityName == "Armour" && s.Kind == ChangeKind.Removed);

        Assert.Single(changes.Rows, r => r.Table == "Armour" && r.Kind == ChangeKind.Removed);
    }

    // ---------------------------------------------------------------- columns

    [Fact]
    public void A_new_column_is_reported_with_its_type()
    {
        var before = ModelFactory.Of(ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }));

        var after = ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.Int32), ("name", ValueType.String), ("power", ValueType.Int32),
                    ("weight", ValueType.Float) },
            new object[] { 1, "Sword", 10, 2.5f }));

        var changes = Between(before, after);

        var added = Assert.Single(changes.Schema,
            s => s.EntityKind == EntityKind.Field && s.MemberName == "weight");

        Assert.Equal(ChangeKind.Added, added.Kind);
        Assert.Contains("\"type\":\"float\"", added.After);

        // And the values that appeared in it.
        Assert.Single(changes.Cells, c => c.Field == "weight" && c.Kind == ChangeKind.Added);
    }

    [Fact]
    public void A_removed_column_is_reported_with_the_values_it_took()
    {
        var before = ModelFactory.Of(ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }));

        var after = ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.Int32), ("name", ValueType.String) },
            new object[] { 1, "Sword" }));

        var changes = Between(before, after);

        var removed = Assert.Single(changes.Schema,
            s => s.EntityKind == EntityKind.Field && s.MemberName == "power");

        Assert.Equal(ChangeKind.Removed, removed.Kind);

        Assert.Single(changes.Cells, c => c.Field == "power" && c.Kind == ChangeKind.Removed && c.OldValue == "10");
    }

    [Fact]
    public void Retyping_a_column_is_reported_on_both_sides()
    {
        var before = ModelFactory.Of(ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }));

        var after = ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.Int32), ("name", ValueType.String), ("power", ValueType.Int64) },
            new object[] { 1, "Sword", 10L }));

        var changed = Assert.Single(Between(before, after).Schema,
            s => s.EntityKind == EntityKind.Field && s.MemberName == "power");

        Assert.Equal(ChangeKind.Modified, changed.Kind);
        Assert.Contains("\"type\":\"int32\"", changed.Before);
        Assert.Contains("\"type\":\"int64\"", changed.After);
    }

    /// <summary>
    /// A comment is emitted into every generated language as documentation, so changing
    /// one changes the output - and it moves no cell, so no row is reported.
    /// </summary>
    // ---------------------------------------------------------------- renames

    private static Model Renamed(string third, params object[][] rows)
        => ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.Int32), ("name", ValueType.String), (third, ValueType.Int32) },
            rows));

    /// <summary>
    /// To the data a rename is a drop and an add - every cell of the old column goes
    /// and every cell of the new one arrives. A five thousand row table would report
    /// ten thousand cell changes for an edit that changed no value, with the edits that
    /// did change something somewhere in the middle.
    /// </summary>
    [Fact]
    public void A_renamed_column_is_reported_as_a_rename()
    {
        var changes = Between(Default(), Renamed("attack",
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 }));

        var rename = Assert.Single(changes.Schema);

        Assert.Equal(EntityKind.Field, rename.EntityKind);
        Assert.Equal(ChangeKind.Modified, rename.Kind);
        Assert.Equal("power", rename.RenamedFrom);
        Assert.Equal("attack", rename.MemberName);

        // The drop is gone from the log: nothing was dropped.
        Assert.DoesNotContain(changes.Schema, s => s.Kind == ChangeKind.Removed);
    }

    /// <summary>
    /// The cell changes stay. They are what moves the stored state from the old column
    /// to the new one, and dropping them would leave the old name in the store for ever.
    /// The report folds them away; the data keeps them.
    /// </summary>
    [Fact]
    public void A_rename_still_carries_the_cells_that_move()
    {
        var changes = Between(Default(), Renamed("attack",
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 }));

        Assert.Equal(2, changes.Cells.Count(c => c.Field == "attack" && c.Kind == ChangeKind.Added));
        Assert.Equal(2, changes.Cells.Count(c => c.Field == "power" && c.Kind == ChangeKind.Removed));
    }

    /// <summary>
    /// A column renamed and edited at once is left as a drop and an add. Less tidy, and
    /// it cannot claim a value moved when it did not.
    /// </summary>
    [Fact]
    public void A_column_renamed_and_edited_at_once_is_not_called_a_rename()
    {
        var changes = Between(Default(), Renamed("attack",
            new object[] { 1, "Sword", 11 },
            new object[] { 2, "Shield", 20 }));

        Assert.DoesNotContain(changes.Schema, s => s.RenamedFrom != null);
        Assert.Contains(changes.Schema, s => s.MemberName == "power" && s.Kind == ChangeKind.Removed);
        Assert.Contains(changes.Schema, s => s.MemberName == "attack" && s.Kind == ChangeKind.Added);
    }

    /// <summary>
    /// Two columns dropped and two added, with values that do not line up, must not be
    /// paired off just because the counts match.
    /// </summary>
    [Fact]
    public void Columns_that_hold_different_values_are_not_paired()
    {
        var before = ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.Int32), ("a", ValueType.Int32), ("b", ValueType.Int32) },
            new object[] { 1, 10, 20 }));

        var after = ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.Int32), ("c", ValueType.Int32), ("d", ValueType.Int32) },
            new object[] { 1, 30, 40 }));

        Assert.DoesNotContain(Between(before, after).Schema, s => s.RenamedFrom != null);
    }

    /// <summary>
    /// An empty table renames nothing detectably, and pairing on no evidence would pair
    /// at random.
    /// </summary>
    [Fact]
    public void A_rename_in_an_empty_table_is_left_as_a_drop_and_an_add()
    {
        var changes = Between(Items(), Renamed("attack"));

        Assert.DoesNotContain(changes.Schema, s => s.RenamedFrom != null);
    }

    [Fact]
    public void Editing_a_columns_comment_is_a_schema_change_and_nothing_else()
    {
        var before = Default();

        var after = Default();
        after.Tables[0].Fields[2].Comment = "Base attack power.";

        var changes = Between(before, after);

        var changed = Assert.Single(changes.Schema);

        Assert.Equal(EntityKind.Field, changed.EntityKind);
        Assert.Equal("power", changed.MemberName);
        Assert.Contains("Base attack power.", changed.After);

        Assert.Empty(changes.Rows);
        Assert.Empty(changes.Cells);
    }
}
