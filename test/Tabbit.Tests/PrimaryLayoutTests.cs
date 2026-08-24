using System.Collections.Generic;
using System.Linq;
using Tabbit;
using Tabbit.Cooking;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The primary layout: entities begin at a `:table` cell, and that cell's column is the
/// entity's marker column.
/// </summary>
/// <remarks>
/// Written against raw sheets built here rather than against a committed workbook, for the
/// reason the other layout tests give: every question is about how one arrangement of cells is
/// read, and each arrangement as a workbook would be another .xlsx nobody can review in a diff.
/// The equivalence gate that needs a real workbook is a separate fixture - section 15 gate 1 of
/// the spec.
/// </remarks>
public class PrimaryLayoutTests
{
    #region Building sheets

    /// <summary>
    /// A sheet holding rows exactly as given, with the layout under test stamped on it.
    /// </summary>
    /// <remarks>
    /// Rows are written as arrays including the marker column, so a test reads like the
    /// spreadsheet it stands for - the first cell of each row is what the marker column holds.
    /// </remarks>
    private static RawSheet Sheet(params string[][] rows)
    {
        var sheet = new RawSheet
        {
            Layout = new SheetLayout("primary", DuplicateIndexPolicy.Error),
            Location = new Location { Filename = "memory.xlsx", Sheet = "Data", Column = 0, Row = 0 },
        };

        int width = rows.Length == 0 ? 0 : rows.Max(row => row.Length);

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var cells = new List<RawCell>(width);

            for (int column = 0; column < width; column++)
            {
                cells.Add(new RawCell
                {
                    Location = new Location
                    {
                        Filename = "memory.xlsx",
                        Sheet = "Data",
                        Column = column,
                        Row = rowIndex,
                    },
                    Value = column < rows[rowIndex].Length ? rows[rowIndex][column] : "",
                });
            }

            sheet.Rows.Add(cells);
        }

        sheet.ColumnCount = width;

        return sheet;
    }

    private static Model Cook(params RawSheet[] sheets)
    {
        var raw = new RawModel();

        foreach (var sheet in sheets)
            raw.Sheets.Add(sheet);

        return new ModelCooker().Cook(new Options(), new RecipeModel(), raw);
    }

    private static TabbitException Refuses(params RawSheet[] sheets)
        => Assert.Throws<TabbitException>(() => Cook(sheets));

    /// <summary>
    /// Everything a refused conversion said, the collected reports included.
    /// </summary>
    /// <remarks>
    /// The checks a layout runs while reading a cell throw where they stand, and the ones that
    /// run over a finished column collect - so a whole sheet is reported rather than its first
    /// mistake. The exception carries both, and a test asserting on wording has to read both.
    /// </remarks>
    private static string Reported(params RawSheet[] sheets)
    {
        var problem = Refuses(sheets);

        return string.Join(
            System.Environment.NewLine,
            new[] { problem.Message }.Concat(problem.Details.Select(d => d.Message)));
    }

    /// <summary>A three-column table, as the smallest thing worth reading.</summary>
    private static RawSheet ItemSheet() => Sheet(
        [":table Item", "an item"],
        [":field", "code", "name"],
        [":type", "int", "string"],
        ["", "1", "sword"],
        ["", "2", "shield"]);

    #endregion


    #region The declaration cell and the entity's rectangle

    [Fact]
    public void A_declaration_cell_starts_a_table_and_the_cell_beside_it_describes_it()
    {
        var model = Cook(ItemSheet());

        var table = Assert.Single(model.Tables);

        Assert.Equal("Item", table.Name);
        Assert.Equal("an item", table.Comment);
        Assert.Equal(2, table.Fields.Count);
        Assert.Equal(2, table.Data.Count);

        // The first field column is the primary index, as it is in the layout this replaces.
        Assert.True(table.Fields[0].Indexing);
        Assert.False(table.Fields[1].Indexing);
    }

    [Fact]
    public void A_declaration_may_sit_anywhere_and_its_column_is_the_marker_column()
    {
        // Two blank columns to the left, so the entity does not begin at column zero.
        var model = Cook(Sheet(
            ["", "", ":table Item", "an item"],
            ["", "", ":field", "code", "name"],
            ["", "", ":type", "int", "string"],
            ["", "", "", "1", "sword"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal("Item", table.Name);
        Assert.Equal(2, table.Fields.Count);
        Assert.Single(table.Data);
    }

    [Fact]
    public void A_blank_row_ends_the_entity_so_a_note_below_it_is_not_read()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            [":type", "int", "string"],
            ["", "1", "sword"],
            ["", "", ""],
            ["", "remember to rebalance these"]));

        var table = Assert.Single(model.Tables);

        Assert.Single(table.Data);
    }

    [Fact]
    public void A_second_declaration_in_the_marker_column_ends_the_first_entity()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            [":type", "int", "string"],
            ["", "1", "sword"],
            [":table Hero", "a hero"],
            [":field", "code", "name"],
            [":type", "int", "string"],
            ["", "1", "knight"]));

        Assert.Equal(2, model.Tables.Count);
        Assert.Single(model.Tables[0].Data);
        Assert.Single(model.Tables[1].Data);
    }

    [Fact]
    public void Two_entities_side_by_side_are_bounded_by_the_next_marker_column()
    {
        var model = Cook(Sheet(
            [":table Item", "an item", "", ":table Hero", "a hero"],
            [":field", "code", "name", ":field", "code", "name"],
            [":type", "int", "string", ":type", "int", "string"],
            ["", "1", "sword", "", "1", "knight"]));

        Assert.Equal(2, model.Tables.Count);

        // The left table stops before the right one's marker column rather than swallowing it.
        Assert.Equal(2, model.Tables[0].Fields.Count);
        Assert.Equal(2, model.Tables[1].Fields.Count);
    }

    [Fact]
    public void A_hash_in_the_marker_column_leaves_the_row_out_without_ending_the_entity()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            [":type", "int", "string"],
            ["", "1", "sword"],
            ["#", "2", "not ready"],
            ["", "3", "bow"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(2, table.Data.Count);
        Assert.Equal(new object[] { 1, 3 }, table.Data.Select(row => row[0].Value).ToArray());
    }

    [Fact]
    public void An_entity_name_is_taken_once_whatever_the_kind_that_took_it()
    {
        var problem = Refuses(Sheet(
            [":enum Item", "a kind"],
            [":field", "label", "value"],
            ["", "One", "1"],
            ["", "", ""],
            [":table Item", "an item"],
            [":field", "code"],
            [":type", "int"],
            ["", "1"]));

        Assert.Contains("Item", problem.Message);
    }

    #endregion


    #region The marker column and the header rows

    [Fact]
    public void The_header_rows_may_be_written_in_any_order()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":type", "int", "string"],
            [":target", "c,s", "c"],
            [":desc", "the id", "shown"],
            [":field", "code", "name"],
            ["", "1", "sword"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal("the id", table.Fields[0].Comment);

        // The primary index is on both sides whatever the row says - every side addresses rows
        // by it - so the column that varies is the second one.
        Assert.Equal(TargetSide.Both, table.Fields[0].TargetSide);
        Assert.Equal(TargetSide.ClientOnly, table.Fields[1].TargetSide);
    }

    [Fact]
    public void A_header_row_below_the_data_is_reported_which_is_what_a_sorted_sheet_looks_like()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            ["", "1", "sword"],
            [":type", "int", "string"]));

        Assert.Contains(":type", problem.Message);
    }

    [Fact]
    public void An_unknown_marker_column_cell_is_reported_rather_than_logged()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            [":type", "int", "string"],
            [":tpye", "", ""],
            ["", "1", "sword"]));

        Assert.Contains(":tpye", problem.Message);
    }

    [Fact]
    public void A_table_needs_a_field_row_and_a_type_row()
    {
        Assert.Contains(":field", Refuses(Sheet(
            [":table Item", "an item"],
            [":type", "int"],
            ["", "1"])).Message);

        Assert.Contains(":type", Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code"],
            ["", "1"])).Message);
    }

    [Fact]
    public void The_same_header_row_written_twice_is_reported()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code"],
            [":field", "code"],
            [":type", "int"],
            ["", "1"]));

        Assert.Contains(":field", problem.Message);
    }

    #endregion


    #region Columns

    [Fact]
    public void A_field_cell_holding_only_a_hash_is_space_for_the_author()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "#", "name"],
            [":type", "int", "", "string"],
            ["", "1", "=VLOOKUP(...)", "sword"]));

        var table = Assert.Single(model.Tables);

        // The memo column leaves no trace: two fields, and the working note is not one of them.
        Assert.Equal(2, table.Fields.Count);
        Assert.Equal(["Code", "Name"], table.Fields.Select(f => f.Name));
    }

    [Fact]
    public void An_unnamed_column_with_data_under_it_is_reported_rather_than_dropped()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "", "name"],
            [":type", "int", "", "string"],
            ["", "1", "손잡이", "sword"]));

        // The message has to offer both ways out, because only the author knows which was meant.
        Assert.Contains("손잡이", problem.Message);
        Assert.Contains("#", problem.Message);
    }

    [Fact]
    public void An_unnamed_column_with_nothing_under_it_is_simply_not_a_column()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "", "name"],
            [":type", "int", "", "string"],
            ["", "1", "", "sword"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(2, table.Fields.Count);
    }

    [Fact]
    public void A_hash_before_a_name_is_a_tombstone_that_keeps_its_wire_tag_reserved()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code@1", "#old@4", "name@2"],
            [":type", "int", "string", "string"],
            ["", "1", "", "sword"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(2, table.Fields.Count);
        Assert.Contains(4, table.ReservedTags);
    }

    [Fact]
    public void A_star_marks_a_secondary_index()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "*sku", "name"],
            [":type", "int", "string", "string"],
            ["", "1", "A-1", "sword"]));

        var table = Assert.Single(model.Tables);

        Assert.True(table.Fields[0].Indexing);
        Assert.True(table.Fields[1].Indexing);
        Assert.False(table.Fields[2].Indexing);
    }

    [Fact]
    public void Two_columns_that_normalize_to_one_name_are_reported()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "Code"],
            [":type", "int", "int"],
            ["", "1", "2"]));

        Assert.Contains("Code", problem.Message);
    }

    #endregion


    #region Column paths - section 5

    [Fact]
    public void A_dotted_path_folds_columns_into_a_record()
    {
        var model = Cook(Sheet(
            [":table Star", "a star"],
            [":field", "code", "pos.x", "pos.y"],
            [":type", "int", "float", "float"],
            ["", "1", "1.5", "2.5"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal("Pos", table.Fields[1].GroupName);
        Assert.True(table.Fields[1].IsRecordMember);
        Assert.Equal("Pos", table.Fields[2].GroupName);
    }

    [Fact]
    public void A_bracketed_number_makes_the_columns_elements_of_one_array()
    {
        var model = Cook(Sheet(
            [":table Loadout", "a loadout"],
            [":field", "code", "slot[0].id", "slot[1].id"],
            [":type", "int", "int", ""],
            ["", "1", "10", "11"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal("Slot", table.Fields[1].GroupName);
        Assert.Equal(0, table.Fields[1].NamePath![0].Index);
        Assert.Equal(1, table.Fields[2].NamePath![0].Index);

        // The type is written once, at element zero, and the later element leaves it blank.
        Assert.Equal("Slot0Id", table.Fields[1].Name);
        Assert.Equal("Slot1Id", table.Fields[2].Name);
    }

    [Fact]
    public void A_scalar_array_is_written_as_numbered_columns_too()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "tag[0]", "tag[1]"],
            [":type", "int", "string", ""],
            ["", "1", "sharp", "long"]));

        var table = Assert.Single(model.Tables);

        Assert.True(table.Fields[1].IsArrayElement);
        Assert.Equal("Tag", table.Fields[1].NamePath![0].Name);
    }

    [Fact]
    public void Brackets_that_run_together_make_an_array_of_arrays()
    {
        var model = Cook(Sheet(
            [":table Guide", "a guide"],
            [":field", "code", "grid[0][0]", "grid[0][1]", "grid[1][0]", "grid[1][1]"],
            [":type", "int", "int", "", "", ""],
            ["", "1", "1", "2", "3", "4"]));

        var table = Assert.Single(model.Tables);

        // Two levels from one segment, and the inner one has no name of its own - there is no
        // word a consumer could write for it, so it indexes.
        var path = table.Fields[1].NamePath!;
        Assert.Equal(2, path.Count);
        Assert.Equal("Grid", path[0].Name);
        Assert.Equal(0, path[0].Index);
        Assert.True(path[1].IsAnonymous);
        Assert.Equal(0, path[1].Index);

        Assert.Equal("Grid", table.Fields[1].GroupName);
    }

    [Fact]
    public void Each_inner_array_counts_from_zero_on_its_own()
    {
        // The outer element is part of the group's identity, so `grid[1]` starting at `[1]` is
        // a gap in that row rather than something the run of `grid[0]` covers.
        var problem = Refuses(Sheet(
            [":table Guide", "a guide"],
            [":field", "code", "grid[0][0]", "grid[1][1]"],
            [":type", "int", "int", ""],
            ["", "1", "1", "2"]));

        Assert.Contains("0", problem.Message);
    }

    [Fact]
    public void A_nameless_level_is_written_with_brackets_rather_than_after_a_dot()
    {
        // One shape, one spelling. `grid.[1]` would be a second way to write what `grid[0][1]`
        // already says.
        var problem = Refuses(Sheet(
            [":table Guide", "a guide"],
            [":field", "code", "grid[0].[1]"],
            [":type", "int", "int"],
            ["", "1", "1"]));

        Assert.Contains("grid", problem.Message);
    }

    [Fact]
    public void Something_written_between_two_bracket_groups_is_reported()
    {
        var problem = Refuses(Sheet(
            [":table Guide", "a guide"],
            [":field", "code", "grid[0]x[1]"],
            [":type", "int", "int"],
            ["", "1", "1"]));

        Assert.Contains("grid", problem.Message);
    }

    [Fact]
    public void Element_numbers_count_from_zero()
    {
        var problem = Refuses(Sheet(
            [":table Loadout", "a loadout"],
            [":field", "code", "slot[1].id", "slot[2].id"],
            [":type", "int", "int", ""],
            ["", "1", "10", "11"]));

        // Excel counts rows from one and the old notation counted `Slot1` from one, so this is
        // the mistake to expect - and it is named rather than read as a gap at the front.
        Assert.Contains("0", problem.Message);
    }

    [Fact]
    public void Element_numbers_run_without_a_gap()
    {
        var problem = Refuses(Sheet(
            [":table Loadout", "a loadout"],
            [":field", "code", "slot[0].id", "slot[2].id"],
            [":type", "int", "int", ""],
            ["", "1", "10", "11"]));

        Assert.Contains("1", problem.Message);
    }

    [Fact]
    public void A_star_on_an_array_column_names_both_things_it_could_have_meant()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "*tag[0]"],
            [":type", "int", "string"],
            ["", "1", "sharp"]));

        // Someone arriving from a layout where `*` meant multi-row lands here, so the message
        // says what `*` is and what multi-row is written with.
        Assert.Contains("[]", problem.Message);
    }

    #endregion


    #region Multi-row - section 6

    [Fact]
    public void A_bracket_pair_on_a_name_takes_the_elements_from_the_rows_below()
    {
        var model = Cook(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "title", "reward[].itemId", "reward[].count"],
            [":type", "int", "string", "int", "int"],
            ["", "1", "first", "10", "1"],
            ["", "", "", "11", "2"],
            ["", "2", "second", "20", "5"]));

        var table = Assert.Single(model.Tables);

        // Two records, not three rows: the blank index cell extends the record above it.
        Assert.Equal(2, table.Data.Count);

        var group = table.SerialFields.Single(g => g.Name == "Reward");
        Assert.True(group.IsArray);

        // The longest record decides how many element columns the group has, and each row says
        // how many of them it filled.
        Assert.Equal(2, table.ElementCountIn(group, table.Data[0]));
        Assert.Equal(1, table.ElementCountIn(group, table.Data[1]));
    }

    [Fact]
    public void A_scalar_group_works_the_same_way()
    {
        var model = Cook(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "cost[]"],
            [":type", "int", "int"],
            ["", "1", "10"],
            ["", "", "20"],
            ["", "", "30"],
            ["", "2", "5"]));

        var table = Assert.Single(model.Tables);
        var group = table.SerialFields.Single(g => g.Name == "Cost");

        Assert.Equal(2, table.Data.Count);
        Assert.Equal(3, table.ElementCountIn(group, table.Data[0]));
        Assert.Equal(1, table.ElementCountIn(group, table.Data[1]));
    }

    [Fact]
    public void Two_groups_side_by_side_fill_up_independently()
    {
        // Section 6.1 rule 5, and section 8.6.5: the same row holding one element of each does
        // not pair them, and one group running out does not stop the other.
        var model = Cook(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "reward[].id", "cost[]"],
            [":type", "int", "int", "int"],
            ["", "1", "10", "5"],
            ["", "", "11", ""],
            ["", "", "12", "7"]));

        var table = Assert.Single(model.Tables);
        var rewards = table.SerialFields.Single(g => g.Name == "Reward");
        var costs = table.SerialFields.Single(g => g.Name == "Cost");

        Assert.Equal(3, table.ElementCountIn(rewards, table.Data[0]));
        Assert.Equal(2, table.ElementCountIn(costs, table.Data[0]));
    }

    [Fact]
    public void A_row_holding_none_of_a_group_columns_is_not_an_element_of_it()
    {
        // Rule 4. The middle row holds no reward, so the second reward is the third row - the
        // elements close up rather than leaving a hole.
        var model = Cook(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "reward[].id", "cost[]"],
            [":type", "int", "int", "int"],
            ["", "1", "10", "1"],
            ["", "", "", "2"],
            ["", "", "11", "3"]));

        var table = Assert.Single(model.Tables);
        var rewards = table.SerialFields.Single(g => g.Name == "Reward");

        Assert.Equal(2, table.ElementCountIn(rewards, table.Data[0]));
    }

    [Fact]
    public void A_scalar_value_on_an_extension_row_names_the_cell_holding_it()
    {
        // Section 6.2: the mistake and the message are in the same place. The layout this
        // notation came from turned the row into a new record and reported a missing key.
        var problem = Refuses(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "title", "reward[].id"],
            [":type", "int", "string", "int"],
            ["", "1", "first", "10"],
            ["", "", "second", "11"]));

        Assert.Contains("second", problem.Message);
        Assert.Contains("title", problem.Message);
    }

    [Fact]
    public void An_index_on_a_row_meant_as_an_extension_becomes_a_record_with_no_scalars()
    {
        // The other half of section 6.2. This one becomes a new record rather than an
        // extension, and what catches it is the blank-cell policy: the scalars the author did
        // not mean to write are empty, and a blank where a number belongs is already an error.
        // So neither direction of the mistake passes quietly.
        var problem = Refuses(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "weight", "reward[].id"],
            [":type", "int", "int", "int"],
            ["", "1", "5", "10"],
            ["", "2", "", "11"]));

        Assert.Contains("empty", problem.Message);
    }

    [Fact]
    public void A_hash_on_the_first_row_of_a_record_leaves_the_whole_record_out()
    {
        // Rule 8. Record 2 spans two rows, and the `#` on its first takes both.
        var model = Cook(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "reward[].id"],
            [":type", "int", "int"],
            ["", "1", "10"],
            ["#", "2", "20"],
            ["", "", "21"],
            ["", "3", "30"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(2, table.Data.Count);
        Assert.Equal([1, 3], table.Data.Select(row => row[0].Value).Cast<int>());
    }

    [Fact]
    public void A_hash_on_an_extension_row_leaves_only_that_row_elements_out()
    {
        var model = Cook(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "reward[].id"],
            [":type", "int", "int"],
            ["", "1", "10"],
            ["#", "", "11"],
            ["", "", "12"]));

        var table = Assert.Single(model.Tables);
        var rewards = table.SerialFields.Single(g => g.Name == "Reward");

        Assert.Single(table.Data);
        Assert.Equal(2, table.ElementCountIn(rewards, table.Data[0]));
    }

    [Fact]
    public void A_blank_row_still_ends_the_entity_so_records_cannot_be_spaced_apart()
    {
        // Rule 7. The blank row closes the table, so what follows is not read at all.
        var model = Cook(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "reward[].id"],
            [":type", "int", "int"],
            ["", "1", "10"],
            ["", "", ""],
            ["", "2", "20"]));

        Assert.Single(Assert.Single(model.Tables).Data);
    }

    [Fact]
    public void The_index_column_cannot_be_a_multi_row_column()
    {
        var problem = Refuses(Sheet(
            [":table Quest", "a quest"],
            [":field", "code[]", "title"],
            [":type", "int", "string"],
            ["", "1", "first"]));

        Assert.Contains("code[]", problem.Message);
    }

    [Fact]
    public void A_nested_multi_row_group_is_refused_by_name()
    {
        Assert.Contains("[]", Refuses(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "reward[].tag[]"],
            [":type", "int", "int"],
            ["", "1", "10"])).Message);

        // A `[]` beside a numbered level is the same shape.
        Assert.Contains("[]", Refuses(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "slot[0].tag[]"],
            [":type", "int", "int"],
            ["", "1", "10"])).Message);
    }

    [Fact]
    public void A_multi_row_column_typed_as_a_cell_array_is_refused_by_name()
    {
        var problem = Refuses(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "cost[]"],
            [":type", "int", "int[]"],
            ["", "1", "10;20"]));

        Assert.Contains("int[]", problem.Message);
    }

    [Fact]
    public void An_extension_row_before_any_record_is_reported()
    {
        var problem = Refuses(Sheet(
            [":table Quest", "a quest"],
            [":field", "code", "reward[].id"],
            [":type", "int", "int"],
            ["", "", "10"],
            ["", "1", "11"]));

        Assert.Contains("Quest", problem.Message);
    }

    [Fact]
    public void A_table_with_no_bracket_column_is_still_one_record_per_row()
    {
        var table = Assert.Single(Cook(ItemSheet()).Tables);

        Assert.Equal(2, table.Data.Count);
        Assert.False(table.TrimTrailingArrayElements);
    }

    #endregion

    #region The folded type expression - section 4

    [Fact]
    public void An_enum_is_named_in_the_type_cell_with_no_detail_row()
    {
        var model = Cook(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value"],
            ["", "Low", "1"],
            ["", "High", "2"],
            ["", "", ""],
            [":table Item", "an item"],
            [":field", "code", "grade"],
            [":type", "int", "Grade"],
            ["", "1", "High"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(ValueType.Enum, table.Fields[1].Type);
        Assert.Equal("Grade", table.Fields[1].TypeName);
    }

    [Fact]
    public void A_reference_names_its_target_in_the_type_cell()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            [":type", "int", "string"],
            ["", "1", "sword"],
            ["", "", ""],
            [":table Drop", "a drop"],
            [":field", "code", "item"],
            [":type", "int", "foreign Item"],
            ["", "1", "1"]));

        var drop = model.Tables.Single(t => t.Name == "Drop");

        Assert.True(drop.Fields[1].IsRef);
        Assert.Equal("Item", drop.Fields[1].RefTableName);
    }

    [Fact]
    public void A_reference_may_name_several_targets_with_a_bar()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code"],
            [":type", "int"],
            ["", "1"],
            ["", ""],
            [":table Gear", "gear"],
            [":field", "code"],
            [":type", "int"],
            ["", "5"],
            ["", ""],
            [":table Drop", "a drop"],
            [":field", "code", "prize"],
            [":type", "int", "foreign Item|Gear"],
            ["", "1", "5"]));

        var drop = model.Tables.Single(t => t.Name == "Drop");

        Assert.Equal(["Item", "Gear"], drop.Fields[1].RefTableNames!);
    }

    [Fact]
    public void An_array_is_a_cell_holding_a_delimited_list_when_the_brackets_are_on_the_type()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "tags"],
            [":type", "int", "string[]"],
            ["", "1", "sharp;long"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(ValueType.StringArray, table.Fields[1].Type);
    }

    [Fact]
    public void An_optional_column_is_marked_on_the_type()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "bonus"],
            [":type", "int", "int?"],
            ["", "1", "-"]));

        var table = Assert.Single(model.Tables);

        Assert.False(table.Fields[1].IsRequired);
        Assert.False(table.Data[0][1].HasValue);
    }

    [Fact]
    public void A_blank_type_cell_on_a_plain_column_is_reported()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            [":type", "int", ""],
            ["", "1", "sword"]));

        Assert.Contains("Name", problem.Message);
    }

    #endregion


    #region Bracket meta - section 4.2

    [Fact]
    public void Everything_from_the_first_bracket_is_meta()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "power", "name"],
            [":type", "int", "int (min=0, max=100)", "string[] (size=1..8)"],
            ["", "1", "50", "sword;bow"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(0, table.Fields[1].Constraints.Minimum);
        Assert.Equal(100, table.Fields[1].Constraints.Maximum);
        Assert.Equal(1, table.Fields[2].Constraints.MinimumLength);
        Assert.Equal(8, table.Fields[2].Constraints.MaximumLength);
    }

    [Fact]
    public void A_flag_key_is_written_with_no_value()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "name"],
            [":type", "int", "string (notDefault)"],
            ["", "1", "sword"]));

        Assert.True(Assert.Single(model.Tables).Fields[1].Constraints.NotDefault);
    }

    [Fact]
    public void The_allowed_key_takes_a_semicolon_list()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "kind"],
            [":type", "int", "string (allowed=weapon;armour)"],
            ["", "1", "weapon"]));

        var field = Assert.Single(model.Tables).Fields[1];

        Assert.Equal(["weapon", "armour"], field.Constraints.AllowedValues!);
    }

    [Fact]
    public void A_role_is_a_key_rather_than_a_type()
    {
        // The old notation put the role in the type name - `text(Common)`. Here the type is
        // `string` and what is done with the value is a key beside it, which is the same
        // spelling a declaration uses.
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "label", "icon"],
            [":type", "int", "string (text=Common, namespace=Shared)", "string (asset=icon)"],
            ["", "1", "Sword", "icon_sword"]));

        var table = Assert.Single(model.Tables);

        Assert.Equal(StringRole.Text, table.Fields[1].Role);
        Assert.Equal("Common", table.Fields[1].RoleGroup);
        Assert.Equal("Shared", table.Fields[1].RoleNamespace);

        Assert.Equal(StringRole.Asset, table.Fields[2].Role);
        Assert.Equal("icon", table.Fields[2].RoleGroup);
    }

    [Fact]
    public void The_text_key_is_also_a_flag()
    {
        var model = Cook(Sheet(
            [":table Item", "an item"],
            [":field", "code", "label"],
            [":type", "int", "string (text)"],
            ["", "1", "Sword"]));

        var field = Assert.Single(model.Tables).Fields[1];

        Assert.Equal(StringRole.Text, field.Role);
        Assert.Null(field.RoleGroup);
    }

    [Fact]
    public void An_unknown_meta_key_is_reported()
    {
        string reported = Reported(Sheet(
            [":table Item", "an item"],
            [":field", "code", "power"],
            [":type", "int", "int (mim=0)"],
            ["", "1", "50"]));

        Assert.Contains("mim", reported);
    }

    [Fact]
    public void The_old_bracket_on_a_type_lands_on_the_key_dictionary()
    {
        // `text(Common)` was the old spelling, and the first `(` starts meta here - so `Common`
        // reads as a key nobody defined. Section 4.2 asks the report to name it, which is what
        // sends the author to `(text=Common)`.
        string reported = Reported(Sheet(
            [":table Item", "an item"],
            [":field", "code", "label"],
            [":type", "int", "text(Common)"],
            ["", "1", "Sword"]));

        Assert.Contains("Common", reported);
    }

    [Fact]
    public void A_bracket_that_never_closes_is_reported()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "power"],
            [":type", "int", "int (min=0"],
            ["", "1", "50"]));

        Assert.Contains("min", problem.Message);
    }

    [Fact]
    public void A_namespace_on_an_asset_is_reported()
    {
        string reported = Reported(Sheet(
            [":table Item", "an item"],
            [":field", "code", "icon"],
            [":type", "int", "string (asset=icon, namespace=Shared)"],
            ["", "1", "icon_sword"]));

        Assert.Contains("namespace", reported);
    }

    [Fact]
    public void A_role_on_a_column_that_is_not_a_string_is_reported()
    {
        string reported = Reported(Sheet(
            [":table Item", "an item"],
            [":field", "code", "power"],
            [":type", "int", "int (text)"],
            ["", "1", "50"]));

        Assert.Contains("text", reported);
    }

    #endregion

    #region The declaration's brackets - section 3.4

    [Fact]
    public void The_side_meta_sets_the_entity_target()
    {
        var model = Cook(Sheet(
            [":table Item(side=s)", "an item"],
            [":field", "code"],
            [":type", "int"],
            ["", "1"]));

        Assert.Equal(TargetSide.ServerOnly, Assert.Single(model.Tables).TargetSide);
    }

    [Fact]
    public void The_side_meta_takes_a_comma_list_and_the_older_joined_spelling()
    {
        Assert.Equal(TargetSide.Both, Assert.Single(Cook(Sheet(
            [":table Item(side=\"c,s\")", "an item"],
            [":field", "code"], [":type", "int"], ["", "1"])).Tables).TargetSide);

        Assert.Equal(TargetSide.Both, Assert.Single(Cook(Sheet(
            [":table Item(side=cs)", "an item"],
            [":field", "code"], [":type", "int"], ["", "1"])).Tables).TargetSide);
    }

    [Fact]
    public void An_unknown_declaration_meta_key_is_reported_with_the_ones_that_exist()
    {
        var problem = Refuses(Sheet(
            [":table Item(sied=s)", "an item"],
            [":field", "code"],
            [":type", "int"],
            ["", "1"]));

        Assert.Contains("sied", problem.Message);
        Assert.Contains("side", problem.Message);
    }

    [Fact]
    public void A_declaration_with_no_name_is_reported()
    {
        Assert.Contains("table", Refuses(Sheet(
            [":table", "an item"],
            [":field", "code"],
            [":type", "int"],
            ["", "1"])).Message);
    }

    #endregion


    #region Enums and constant sets

    [Fact]
    public void An_enum_names_its_columns_and_has_no_type_row()
    {
        var model = Cook(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value", "desc"],
            ["", "Low", "1", "the low one"],
            ["", "High", "2", ""]));

        var declared = Assert.Single(model.Enums);

        Assert.Equal("Grade", declared.Name);
        Assert.Equal("a tier", declared.Comment);

        // `None = 0` is supplied when no label claims zero, as it always was.
        Assert.Contains(declared.Labels, label => label.Value == 1 && label.Name == "Low");
        Assert.Contains(declared.Labels, label => label.Value == 2 && label.Name == "High");
        Assert.Equal("the low one", declared.Labels.Single(l => l.Name == "Low").Comment);
    }

    [Fact]
    public void An_enum_column_order_is_free()
    {
        var model = Cook(Sheet(
            [":enum Grade", "a tier"],
            [":field", "value", "label"],
            ["", "1", "Low"]));

        Assert.Contains(Assert.Single(model.Enums).Labels, label => label.Name == "Low");
    }

    [Fact]
    public void An_unknown_enum_column_is_reported_with_the_ones_that_exist()
    {
        var problem = Refuses(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "vlaue"],
            ["", "Low", "1"]));

        Assert.Contains("vlaue", problem.Message);
        Assert.Contains("value", problem.Message);
    }

    [Fact]
    public void An_enum_needs_a_label_and_a_value_column()
    {
        Assert.Contains("value", Refuses(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label"],
            ["", "Low"])).Message);
    }

    [Fact]
    public void A_header_row_an_enum_has_no_use_for_is_reported()
    {
        // Accepted for a while and read by nothing, which is the quiet no-op this layout is
        // meant not to have. An enum's description belongs to a label, and the `desc` column
        // is where it goes.
        var problem = Refuses(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value"],
            [":desc", "the name", "the number"],
            ["", "Low", "1"]));

        Assert.Contains(":desc", problem.Message);
    }

    [Fact]
    public void A_type_row_on_an_enum_is_reported()
    {
        var problem = Refuses(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value"],
            [":type", "string", "int"],
            ["", "Low", "1"]));

        Assert.Contains(":type", problem.Message);
    }

    [Fact]
    public void An_alias_is_a_fourth_way_to_write_a_label_in_a_data_cell()
    {
        var model = Cook(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value", "alias"],
            ["", "low_grade", "1", "LO"],
            ["", "HighGrade", "2", ""],
            ["", "", "", ""],
            [":table Item", "an item"],
            [":field", "code", "grade"],
            [":type", "int", "Grade"],
            // The four spellings, one per row: the alias, the declaration's own text, the
            // Pascal name, and the number.
            ["", "1", "LO"],
            ["", "2", "low_grade"],
            ["", "3", "LowGrade"],
            ["", "4", "1"]));

        var declared = Assert.Single(model.Enums);
        Assert.Equal("LO", declared.Labels.Single(l => l.Name == "LowGrade").Alias);

        // The alias is a spelling, so the label's own name is untouched by it - which is what
        // the naming-convention report holds a sheet to.
        Assert.Equal("low_grade", declared.Labels.Single(l => l.Name == "LowGrade").RawName);

        var table = Assert.Single(model.Tables);
        Assert.Equal([1, 1, 1, 1], table.Data.Select(row => row[1].Value).Cast<int>());
    }

    [Fact]
    public void Two_labels_cannot_share_one_alias()
    {
        var problem = Refuses(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value", "alias"],
            ["", "Low", "1", "X"],
            ["", "High", "2", "X"]));

        Assert.Contains("X", problem.Message);
    }

    [Fact]
    public void An_alias_that_is_already_a_label_name_is_refused()
    {
        // A real name answers a cell first, so this alias would resolve nothing while looking
        // as though it did.
        var problem = Refuses(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value", "alias"],
            ["", "Low", "1", "High"],
            ["", "High", "2", ""]));

        Assert.Contains("High", problem.Message);
    }

    [Fact]
    public void A_constant_set_folds_the_type_and_its_detail_into_one_column()
    {
        var model = Cook(Sheet(
            [":enum Grade", "a tier"],
            [":field", "label", "value"],
            ["", "High", "2"],
            ["", "", ""],
            [":const Tuning", "knobs"],
            [":field", "name", "type", "value", "desc"],
            ["", "maxLevel", "int", "60", "the cap"],
            ["", "startGrade", "Grade", "High", ""]));

        var set = Assert.Single(model.ConstantSets);

        Assert.Equal("Tuning", set.Name);
        Assert.Equal(2, set.Constants.Count);
        Assert.Equal("the cap", set.Constants[0].Comment);

        // The enum names itself in the type column, which is what turns five columns into four.
        Assert.Equal(ValueType.Enum, set.Constants[1].Type);
    }

    [Fact]
    public void A_constant_cannot_be_optional()
    {
        var problem = Refuses(Sheet(
            [":const Tuning", "knobs"],
            [":field", "name", "type", "value"],
            ["", "maxLevel", "int?", "60"]));

        Assert.Contains("?", problem.Message);
    }

    #endregion


    #region Field variants - section 3.6

    private static Model CookWith(FieldVariants variants, params RawSheet[] sheets)
    {
        var raw = new RawModel();

        foreach (var sheet in sheets)
            raw.Sheets.Add(sheet);

        var recipe = new RecipeModel();

        foreach (string field in variants.Fields)
            recipe.Variants[field] = variants.Of(field.Split('.')[0], field.Split('.')[1])!;

        return new ModelCooker().Cook(new Options(), recipe, raw);
    }

    private static RawSheet PriceSheet() => Sheet(
        [":table Item", "an item"],
        [":field", "code", "price", "price", "price"],
        [":type", "int", "int", "", ""],
        [":variant", "", "", "kr", "jp"],
        ["", "1", "10", "12", "14"],
        ["", "2", "20", "22", "24"]);

    [Fact]
    public void A_field_with_no_variant_asked_for_takes_the_default_column()
    {
        var table = Assert.Single(Cook(PriceSheet()).Tables);

        // One field, whatever the sheet wrote. The others are not in the build at all.
        Assert.Equal(2, table.Fields.Count);
        Assert.Equal("Price", table.Fields[1].Name);
        Assert.Equal([10, 20], table.Data.Select(row => row[1].Value).Cast<int>());
    }

    [Fact]
    public void A_variant_asked_for_becomes_the_field()
    {
        var model = CookWith(
            FieldVariants.Of(null, ["Item.Price=kr"]), PriceSheet());

        var table = Assert.Single(model.Tables);

        // The same field, the same type, the same name - the other column's values.
        Assert.Equal(2, table.Fields.Count);
        Assert.Equal("Price", table.Fields[1].Name);
        Assert.Equal(ValueType.Int32, table.Fields[1].Type);
        Assert.Equal([12, 22], table.Data.Select(row => row[1].Value).Cast<int>());
    }

    [Fact]
    public void A_variant_column_reads_its_header_from_the_default_column()
    {
        // The `:type` cells of the `kr` and `jp` columns are blank, and the field is still an
        // `int` - the group states its header once. Section 3.6.
        var model = CookWith(FieldVariants.Of(null, ["Item.Price=jp"]), PriceSheet());

        Assert.Equal(ValueType.Int32, Assert.Single(model.Tables).Fields[1].Type);
    }

    [Fact]
    public void A_variant_nothing_answers_is_reported_with_the_ones_that_exist()
    {
        var problem = Assert.Throws<TabbitException>(() => CookWith(
            FieldVariants.Of(null, ["Item.Price=us"]), PriceSheet()));

        Assert.Contains("us", problem.Message);
        Assert.Contains("kr", problem.Message);
    }

    [Fact]
    public void A_field_whose_every_column_names_a_variant_has_to_be_chosen()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "price", "price"],
            [":type", "int", "int", ""],
            [":variant", "", "kr", "jp"],
            ["", "1", "10", "12"]));

        Assert.Contains("kr", problem.Message);
    }

    [Fact]
    public void Two_columns_of_one_field_cannot_claim_one_variant()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "price", "price"],
            [":type", "int", "int", ""],
            [":variant", "", "kr", "kr"],
            ["", "1", "10", "12"]));

        Assert.Contains("kr", problem.Message);
    }

    [Fact]
    public void A_variant_column_that_states_a_different_type_is_reported()
    {
        var problem = Assert.Throws<TabbitException>(() => CookWith(
            FieldVariants.Of(null, ["Item.Price=kr"]),
            Sheet(
                [":table Item", "an item"],
                [":field", "code", "price", "price"],
                [":type", "int", "int", "string"],
                [":variant", "", "", "kr"],
                ["", "1", "10", "12"])));

        Assert.Contains("string", problem.Message);
    }

    [Fact]
    public void A_variant_cannot_sit_on_a_column_the_row_is_addressed_by()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "code"],
            [":type", "int", ""],
            [":variant", "", "kr"],
            ["", "1", "2"]));

        Assert.Contains("code", problem.Message);
    }

    [Fact]
    public void A_variant_cannot_sit_on_a_member_of_a_group()
    {
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", "pos.x", "pos.x"],
            [":type", "int", "float", ""],
            [":variant", "", "", "kr"],
            ["", "1", "1.5", "2.5"]));

        Assert.Contains("pos.x", problem.Message);
    }

    [Fact]
    public void A_variant_option_that_is_not_a_field_and_a_name_is_reported()
    {
        Assert.Throws<TabbitException>(() => FieldVariants.Of(null, ["Item.Price"]));
        Assert.Throws<TabbitException>(() => FieldVariants.Of(null, ["Price=kr"]));
        Assert.Throws<TabbitException>(
            () => FieldVariants.Of(null, ["Item.Price=kr", "Item.Price=jp"]));
    }

    #endregion

    #region The key meta - section 3.5

    [Fact]
    public void The_key_meta_moves_the_primary_index_without_moving_a_column()
    {
        var model = Cook(Sheet(
            [":table Item(key=sku)", "an item"],
            [":field", "seq", "sku", "name"],
            [":type", "int", "string", "string"],
            ["", "1", "A-1", "sword"],
            ["", "2", "A-2", "shield"]));

        var table = Assert.Single(model.Tables);

        // The columns are where the sheet put them. What moved is which one addresses a row.
        Assert.Equal(["Seq", "Sku", "Name"], table.Fields.Select(f => f.Name));
        Assert.Equal("Sku", table.PrimaryIndexField!.Name);

        // And the first column is an ordinary field now.
        Assert.False(table.Fields[0].Indexing);
        Assert.True(table.Fields[1].Indexing);
    }

    [Fact]
    public void The_first_column_keeps_its_own_star_when_the_key_moves()
    {
        var model = Cook(Sheet(
            [":table Item(key=sku)", "an item"],
            [":field", "*seq", "sku"],
            [":type", "int", "string"],
            ["", "1", "A-1"]));

        var table = Assert.Single(model.Tables);

        // It asked to be an index for itself, so losing the primary one does not take that.
        Assert.True(table.Fields[0].Indexing);
        Assert.Equal("Sku", table.PrimaryIndexField!.Name);
    }

    [Fact]
    public void A_key_naming_a_column_that_is_not_there_is_reported_with_the_ones_that_are()
    {
        var problem = Refuses(Sheet(
            [":table Item(key=code)", "an item"],
            [":field", "seq", "sku"],
            [":type", "int", "string"],
            ["", "1", "A-1"]));

        Assert.Contains("code", problem.Message);
        Assert.Contains("Sku", problem.Message);
    }

    [Fact]
    public void A_key_naming_a_group_member_is_reported()
    {
        var problem = Refuses(Sheet(
            [":table Item(key=posX)", "an item"],
            [":field", "seq", "pos.x"],
            [":type", "int", "float"],
            ["", "1", "1.5"]));

        Assert.Contains("PosX", problem.Message);
    }

    [Fact]
    public void A_composite_key_is_refused_by_name()
    {
        // The lookup every generated reader offers changes shape, which is a step of its own.
        var problem = Refuses(Sheet(
            [":table Item(key=\"stage,slot\")", "an item"],
            [":field", "stage", "slot"],
            [":type", "int", "int"],
            ["", "1", "2"]));

        Assert.Contains("stage,slot", problem.Message);
    }

    [Fact]
    public void The_record_boundary_of_a_multi_row_table_moves_with_the_key()
    {
        // Section 6.1 rule 2 reads the key's cell, so naming another column moves the boundary
        // with it rather than leaving it on the first.
        var model = Cook(Sheet(
            [":table Quest(key=sku)", "a quest"],
            [":field", "seq", "sku", "reward[].id"],
            [":type", "int", "string", "int"],
            ["", "1", "A-1", "10"],

            // The extension row leaves every column but the `[]` one blank - rule 3 holds
            // whichever column the key is.
            ["", "", "", "11"],
            ["", "3", "A-2", "20"]));

        var table = Assert.Single(model.Tables);
        var rewards = table.SerialFields.Single(g => g.Name == "Reward");

        // Two records: the middle row leaves the key blank, so it extends the first.
        Assert.Equal(2, table.Data.Count);
        Assert.Equal(2, table.ElementCountIn(rewards, table.Data[0]));
    }

    #endregion

    #region Reserved columns - section 7

    [Theory]
    [InlineData(":type")]
    [InlineData(":key")]
    [InlineData(":value")]
    public void A_reserved_column_name_is_refused_by_name(string reserved)
    {
        // Held rather than left undefined. Each of the three is a notation a later spec settles,
        // and reserving the spelling now means that spec does not have to work around whatever a
        // sheet happened to use in the meantime.
        var problem = Refuses(Sheet(
            [":table Item", "an item"],
            [":field", "code", reserved],
            [":type", "int", "string"],
            ["", "1", "x"]));

        Assert.Contains(reserved, problem.Message);
    }

    #endregion

    #region What the layout leaves to the core

    [Fact]
    public void A_formula_error_in_a_memo_column_does_not_stop_the_conversion()
    {
        var sheet = Sheet(
            [":table Item", "an item"],
            [":field", "code", "#", "name"],
            [":type", "int", "", "string"],
            ["", "1", "", "sword"]);

        // What a broken formula in a working column looks like once the importer has read it:
        // the value is empty and the error travels beside it. Nothing reads this column, so
        // nothing should report it - which is the whole reason a memo column can be free space.
        sheet.Rows[3][2].FormulaError = "#N/A";

        var table = Assert.Single(Cook(sheet).Tables);

        Assert.Single(table.Data);
    }

    #endregion
}
