using System.Linq;
using Tabbit.History;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The three-level hash the history compares snapshots with.
///
/// Two properties have to hold together, and they pull in opposite directions. It must
/// notice every change - a hash that misses one loses an edit from the history with no
/// symptom at all. And it must notice nothing else, because every table whose hash
/// moves costs a round trip to a remote database on every build.
/// </summary>
public class ModelFingerprintTests
{
    private static readonly (string, ValueType)[] Columns =
    {
        ("id", ValueType.Int32),
        ("name", ValueType.String),
        ("power", ValueType.Int32),
    };

    private static Model Sample(params object[][] rows)
        => ModelFactory.Of(ModelFactory.Table("Item", Columns, rows));

    private static Model Default() => Sample(
        new object[] { 1, "Sword", 10 },
        new object[] { 2, "Shield", 20 });

    [Fact]
    public void The_same_model_hashes_the_same()
    {
        Assert.Equal(ModelFingerprint.Of(Default()).Hash, ModelFingerprint.Of(Default()).Hash);
    }

    /// <summary>
    /// One cell edited moves that row, its table and the model - and nothing else.
    ///
    /// The second half is what makes the store affordable: an untouched row must not
    /// cost a comparison, and an untouched table must not cost a query.
    /// </summary>
    [Fact]
    public void One_edited_cell_moves_exactly_one_row()
    {
        var before = ModelFingerprint.Of(Default());

        var after = ModelFingerprint.Of(Sample(
            new object[] { 1, "Sword", 12 },
            new object[] { 2, "Shield", 20 }));

        Assert.NotEqual(before.Hash, after.Hash);
        Assert.NotEqual(before.Tables[0].Hash, after.Tables[0].Hash);

        // The schema did not move, so a schema diff has nothing to report.
        Assert.Equal(before.Tables[0].SchemaHash, after.Tables[0].SchemaHash);

        Assert.NotEqual(before.Tables[0].Rows[0].Hash, after.Tables[0].Rows[0].Hash);
        Assert.Equal(before.Tables[0].Rows[1].Hash, after.Tables[0].Rows[1].Hash);
    }

    /// <summary>
    /// A blank cell and a cell holding an empty string are different rows.
    /// </summary>
    [Fact]
    public void Clearing_a_cell_is_not_the_same_as_emptying_it()
    {
        var blank = ModelFingerprint.Of(Sample(new object[] { 1, null, 10 }));
        var empty = ModelFingerprint.Of(Sample(new object[] { 1, "", 10 }));

        Assert.NotEqual(blank.Tables[0].Rows[0].Hash, empty.Tables[0].Rows[0].Hash);
    }

    /// <summary>
    /// Rows are followed by their primary index, so reordering them is not an edit to
    /// any of them. The table hash still moves - the comparison descends once and finds
    /// nothing, which is cheaper than sorting millions of hashes on every build.
    /// </summary>
    [Fact]
    public void Reordering_rows_leaves_every_row_hash_alone()
    {
        var before = ModelFingerprint.Of(Default());

        var after = ModelFingerprint.Of(Sample(
            new object[] { 2, "Shield", 20 },
            new object[] { 1, "Sword", 10 }));

        Assert.NotEqual(before.Tables[0].Hash, after.Tables[0].Hash);

        Assert.Equal(
            before.Tables[0].Rows.Select(r => r.Hash).OrderBy(h => h),
            after.Tables[0].Rows.Select(r => r.Hash).OrderBy(h => h));
    }

    /// <summary>
    /// A renamed column rewrites every row, because every cell in it is now a cell of a
    /// column that did not exist before. Hashing values without their column name would
    /// hide the rename here and leave the store holding the old name for ever.
    /// </summary>
    [Fact]
    public void Renaming_a_column_moves_the_schema_and_every_row()
    {
        var before = ModelFingerprint.Of(Default());

        var renamed = ModelFactory.Of(ModelFactory.Table("Item",
            new[] { ("id", ValueType.Int32), ("title", ValueType.String), ("power", ValueType.Int32) },
            new object[] { 1, "Sword", 10 },
            new object[] { 2, "Shield", 20 }));

        var after = ModelFingerprint.Of(renamed);

        Assert.NotEqual(before.Tables[0].SchemaHash, after.Tables[0].SchemaHash);
        Assert.NotEqual(before.Tables[0].Rows[0].Hash, after.Tables[0].Rows[0].Hash);
        Assert.NotEqual(before.Tables[0].Rows[1].Hash, after.Tables[0].Rows[1].Hash);
    }

    /// <summary>
    /// A column comment is emitted into every generated language as documentation, so
    /// rewriting one does change the output - but it changes no value, so no row moves.
    /// </summary>
    [Fact]
    public void Editing_a_column_comment_moves_the_schema_and_no_rows()
    {
        var before = ModelFingerprint.Of(Default());

        var documented = Default();
        documented.Tables[0].Fields[2].Comment = "Base attack power.";

        var after = ModelFingerprint.Of(documented);

        Assert.NotEqual(before.Tables[0].SchemaHash, after.Tables[0].SchemaHash);
        Assert.Equal(before.Tables[0].Rows[0].Hash, after.Tables[0].Rows[0].Hash);
    }

    /// <summary>
    /// Where the data came from is metadata, not content. Moving a table to another
    /// sheet must not read as every row having been rewritten.
    /// </summary>
    [Fact]
    public void Moving_a_table_to_another_sheet_changes_nothing()
    {
        var before = ModelFingerprint.Of(Default());

        var moved = Default();
        moved.Tables[0].Location = new Location
        {
            Filename = "elsewhere.xlsx", Sheet = "Other", Column = 4, Row = 9,
        };

        Assert.Equal(before.Hash, ModelFingerprint.Of(moved).Hash);
    }

    /// <summary>
    /// Declaration order is not content either: a workbook renamed changes the order
    /// sources are read in, and that must not read as a changed model.
    /// </summary>
    [Fact]
    public void Declaring_tables_in_another_order_changes_nothing()
    {
        var first = ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 });
        var second = ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 });

        Assert.Equal(
            ModelFingerprint.Of(ModelFactory.Of(first, second)).Hash,
            ModelFingerprint.Of(ModelFactory.Of(second, first)).Hash);
    }

    [Fact]
    public void A_row_is_keyed_by_its_primary_index()
    {
        var fingerprint = ModelFingerprint.Of(Default());

        Assert.Equal(new[] { "1", "2" }, fingerprint.Tables[0].Rows.Select(r => r.Key));
    }

    /// <summary>
    /// Cells are read back on demand rather than held, because a project's sheets hold
    /// millions of them and only the changed rows are ever looked at.
    /// </summary>
    [Fact]
    public void Cells_are_read_back_with_their_value_and_their_place_in_the_sheet()
    {
        var fingerprint = ModelFingerprint.Of(Default());
        var table = fingerprint.Tables[0];

        var cells = table.CellsOf(table.Rows[0]).ToList();

        Assert.Equal(new[] { "id", "name", "power" }, cells.Select(c => c.Field));
        Assert.Equal(new[] { "1", "Sword", "10" }, cells.Select(c => c.Value));

        Assert.All(cells, cell => Assert.Equal("Item", cell.Location.Sheet));
    }

    [Fact]
    public void An_empty_cell_reads_back_as_nothing_rather_than_as_empty_text()
    {
        var fingerprint = ModelFingerprint.Of(Sample(new object[] { 1, null, 10 }));
        var table = fingerprint.Tables[0];

        Assert.Null(table.CellsOf(table.Rows[0]).ElementAt(1).Value);
    }

    [Fact]
    public void A_table_with_no_rows_still_has_a_schema()
    {
        var fingerprint = ModelFingerprint.Of(Sample());

        Assert.Empty(fingerprint.Tables[0].Rows);
        Assert.NotNull(fingerprint.Tables[0].SchemaHash);
    }
}
