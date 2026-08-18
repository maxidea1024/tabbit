using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Cooking.Layouts;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Reads a sheet written the way one project's workbooks are written, through the layout
/// that understands them.
/// </summary>
/// <remarks>
/// A layout parser takes a grid and a defined name, which is little enough to build here -
/// so these are the layout's own rules, checked without a workbook on disk.
///
/// The reason this file exists: a `[number]` column saying it had no value came out of the
/// parser as an empty string, and nothing noticed until every workbook was converted at
/// once and the binary exporter cast it. The parse of a value and the parse of no value had
/// drifted into two switches, and only one of them knew arrays existed.
/// </remarks>
public class UwoLayoutTests
{
    /// <summary>
    /// One table, laid out the way this layout wants it: names, types, then `:`-keyed
    /// constraint rows, then data.
    /// </summary>
    /// <param name="rows">Name row, type row, constraint rows and data rows, in that order.</param>
    private static Table Parse(params string[][] rows) => Assert.Single(ParseModel(rows).Tables);

    /// <summary>
    /// The same, kept whole - a grid produces a second table beside the one it was asked for.
    /// </summary>
    private static Model ParseModel(params string[][] rows)
    {
        var sheet = new RawSheet
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "Sheet1" },
            ColumnCount = rows[0].Length,
            Rows = rows.Select((cells, row) => cells.Select((text, column) => new RawCell
            {
                Location = new Location
                {
                    Filename = "book.xlsx",
                    Sheet = "Sheet1",
                    Row = row,
                    Column = column,
                },
                Value = text,
                Note = "",
            }).ToList()).ToList(),
            NamedRanges =
            {
                new RawNamedRange
                {
                    Name = "T",
                    Row = 0,
                    Column = 0,
                    Height = rows.Length,
                    Width = rows[0].Length,
                },
            },
        };

        _refusals = new Diagnostics();

        var context = new CookingContext(new Model(), new RecipeModel(), _refusals);
        var parser = new UwoLayoutParser();

        parser.ParseDeclarations(context, new[] { sheet });
        parser.ParseTables(context, new[] { sheet });

        return context.Model;
    }

    /// <summary>
    /// What the parser refused about the sheet just read.
    /// </summary>
    /// <remarks>
    /// A table this layout cannot read is reported and left out rather than ending the run,
    /// so a refusal is a diagnostic and not an exception - which is what lets one bad shape
    /// among six hundred tables be a line in a list.
    /// </remarks>
    private static Diagnostics _refusals = new Diagnostics();

    /// <summary>The single refusal the sheet produced, which is what these tests assert on.</summary>
    private static string Refusal()
        => Assert.Single(_refusals.Entries).Detail.Message;

    /// <summary>The cell of a row that has a value, by field name.</summary>
    private static object ValueOf(Table table, int row, string field)
    {
        int at = table.Fields.FindIndex(f => f.Name == field);
        return table.Data[row][at].Value;
    }

    /// <summary>
    /// A column with no value carries its type's empty value - and for an array that is an
    /// empty array, not an empty string.
    /// </summary>
    /// <remarks>
    /// This layout writes `-` for "no value", so it happens in ordinary data rather than at
    /// an edge, and every reader downstream indexes the result.
    /// </remarks>
    [Fact]
    public void A_dash_in_an_array_column_reads_as_an_empty_array()
    {
        var table = Parse(
            new[] { "Index", "Costs" },
            new[] { "key", "[number]" },
            new[] { ":required", "true", "" },
            new[] { "1", "10;20" },
            new[] { "2", "-" });

        Assert.Equal(new[] { 10d, 20d }, Assert.IsType<double[]>(ValueOf(table, 0, "Costs")));

        // The array's own empty value, not the empty string a scalar would get. This is the
        // whole of the bug: `""` here reached the binary exporter as something to cast.
        Assert.Empty(Assert.IsType<double[]>(ValueOf(table, 1, "Costs")));
    }

    /// <summary>A blank cell reads the same way, since the sheet said nothing either way.</summary>
    [Fact]
    public void A_blank_cell_in_an_array_column_reads_as_an_empty_array_too()
    {
        var table = Parse(
            new[] { "Index", "Costs" },
            new[] { "key", "[number]" },
            new[] { "1", "10;20" },
            new[] { "2", "" });

        Assert.Empty(Assert.IsType<double[]>(ValueOf(table, 1, "Costs")));
    }

    /// <summary>
    /// The scalar columns keep answering the way they did.
    /// </summary>
    /// <remarks>
    /// Here because the switch that got this right for scalars is the one that got it wrong
    /// for arrays: replacing it had to leave these alone.
    /// </remarks>
    [Fact]
    public void A_dash_in_a_scalar_column_reads_as_the_type_s_empty_value()
    {
        var table = Parse(
            new[] { "Index", "Count", "Label" },
            new[] { "key", "number", "string" },
            new[] { "1", "-", "-" });

        Assert.Equal(0d, ValueOf(table, 0, "Count"));
        Assert.Equal("", ValueOf(table, 0, "Label"));
    }

    /// <summary>
    /// `Name[0]`/`Name[1]` are one field, and the first element answers for it.
    /// </summary>
    /// <remarks>
    /// The end-to-end half of <see cref="ArrayOptionalityTests"/>: what the sheets carry is
    /// the first column marked and the rest left alone, and the marks on the later columns
    /// mean nothing.
    /// </remarks>
    [Fact]
    public void The_first_element_of_an_array_carries_its_requiredness()
    {
        var table = Parse(
            new[] { "Index", "Name[0]", "Name[1]" },
            new[] { "key", "string", "string" },
            new[] { ":required", "true", "true", "" },
            new[] { "1", "a", "b" });

        var name = table.SerialFields.Single(sf => sf.Name == "Name");

        Assert.All(name.Fields, field => Assert.True(field.IsRequired));
    }

    /// <summary>
    /// And an array ends where its values end, so trailing elements with no value go.
    /// </summary>
    /// <remarks>
    /// The sheets in this layout write an array at its widest and leave the rest `-`, so a
    /// row deciding its own length is the normal case rather than an optimization.
    /// </remarks>
    [Fact]
    public void Trailing_elements_with_no_value_are_trimmed()
    {
        var table = Parse(
            new[] { "Index", "Name[0]", "Name[1]", "Name[2]" },
            new[] { "key", "string", "string", "string" },
            new[] { "1", "a", "b", "c" },
            new[] { "2", "a", "-", "-" });

        var name = table.SerialFields.Single(sf => sf.Name == "Name");

        Assert.True(table.TrimTrailingArrayElements);
        Assert.Equal(3, table.ElementCountIn(name, table.Data[0]));
        Assert.Equal(1, table.ElementCountIn(name, table.Data[1]));
    }

    /// <summary>
    /// A column named by a number is one coordinate of a grid, so it folds into an array and
    /// the ids come back as a table of their own.
    /// </summary>
    /// <remarks>
    /// `24000000` is not an identifier, and no supported language has a property by that
    /// name - so the choice is a grid or a refusal, and a refusal is what it used to be.
    /// spec/matrix-tables.md.
    /// </remarks>
    [Fact]
    public void Numeric_column_names_become_one_array_and_a_table_of_ids()
    {
        var model = ParseModel(
            new[] { "id", "700", "701", "702" },
            new[] { "key", "number", "number", "number" },
            new[] { "1", "10", "20", "30" },
            new[] { "2", "40", "50", "60" });

        var grid = model.Tables.Single(t => t.Name == "T");
        var columns = model.Tables.Single(t => t.Name == "TColumn");

        var value = grid.SerialFields.Single(sf => sf.Name == "Value");
        Assert.True(value.IsArray);
        Assert.Equal(3, value.Fields.Count);

        Assert.Equal(new object[] { 10d, 20d, 30d }, grid.Data[0].Skip(1).Select(c => c.Value).ToArray());

        // The ids in sheet order, each paired with the element it names.
        Assert.Equal(3, columns.Data.Count);
        Assert.Equal(new object[] { 700, 0 }, columns.Data[0].Select(c => c.Value));
        Assert.Equal(new object[] { 702, 2 }, columns.Data[2].Select(c => c.Value));

        // And it can be looked up: the id is the table's primary index.
        Assert.True(columns.Fields[0].Indexing);
    }

    /// <summary>
    /// A column whose name is a name stays a column.
    /// </summary>
    /// <remarks>
    /// The shape a real sheet has: `id`, a `name` describing the row, and only then the
    /// grid. Reading "every column after the first is a coordinate" would have swallowed the
    /// description into the array.
    /// </remarks>
    [Fact]
    public void A_named_column_beside_the_grid_stays_a_field()
    {
        var model = ParseModel(
            new[] { "id", "name", "700", "701" },
            new[] { "key", "string", "number", "number" },
            new[] { "1", "first", "10", "20" });

        var grid = model.Tables.Single(t => t.Name == "T");

        Assert.Equal("first", ValueOf(grid, 0, "Name"));
        Assert.Equal(2, grid.SerialFields.Single(sf => sf.Name == "Value").Fields.Count);
        Assert.Equal(2, model.Tables.Single(t => t.Name == "TColumn").Data.Count);
    }

    /// <summary>
    /// A grid's array is never trimmed, however the layout reads every other array.
    /// </summary>
    /// <remarks>
    /// Position is the meaning here. Ending a row's array at its last value would leave the
    /// rows different lengths, and then the element a column id names is a different element
    /// per row - or missing.
    /// </remarks>
    [Fact]
    public void A_grid_is_not_trimmed()
    {
        var model = ParseModel(
            new[] { "id", "700", "701", "702" },
            new[] { "key", "number", "number", "number" },
            new[] { "1", "10", "20", "30" },
            new[] { "2", "40", "-", "-" });

        var grid = model.Tables.Single(t => t.Name == "T");

        Assert.False(grid.TrimTrailingArrayElements);
        Assert.Equal(3, grid.ElementCountIn(grid.SerialFields.Single(sf => sf.Name == "Value"), grid.Data[1]));
    }

    /// <summary>
    /// And the grid's columns have to agree on a type, because they are one array.
    /// </summary>
    [Fact]
    public void A_grid_whose_columns_disagree_on_type_is_refused()
    {
        ParseModel(
            new[] { "id", "700", "701" },
            new[] { "key", "number", "string" },
            new[] { "1", "10", "x" });

        Assert.Contains("not all one type", Refusal());
    }

    /// <summary>
    /// `name[0][1]` is an array of arrays, and it folds the same way a record whose members
    /// are arrays does - the outer index simply stands where a member name would.
    /// </summary>
    /// <remarks>
    /// This was refused outright until it turned out to be the shape next door with the
    /// names taken off. spec/nested-multi-level.md.
    /// </remarks>
    [Fact]
    public void An_array_of_arrays_folds_into_one_group()
    {
        var table = Parse(
            new[] { "id", "tag[0][0]", "tag[0][1]", "tag[1][0]", "tag[1][1]" },
            new[] { "key", "string", "string", "string", "string" },
            new[] { "1", "a", "b", "c", "d" });

        var tag = table.SerialFields.Single(sf => sf.Name == "Tag");

        Assert.True(tag.IsRecord);
        Assert.True(tag.MembersAreArrays);
        Assert.True(tag.MembersAreAnonymous);

        // Two outer elements of two inner each, and the group itself is not the array -
        // exactly as for named members.
        Assert.False(tag.IsArray);
        Assert.Equal(2, tag.Members.Count);
        Assert.Equal(2, tag.Members[0].Fields.Count);
        Assert.All(tag.Members, m => Assert.True(m.IsAnonymous));
    }

    /// <summary>
    /// The outer index orders the members, whatever order the sheet wrote the columns in.
    /// </summary>
    [Fact]
    public void The_outer_index_orders_the_members()
    {
        var table = Parse(
            new[] { "id", "tag[1][0]", "tag[0][0]", "tag[1][1]", "tag[0][1]" },
            new[] { "key", "string", "string", "string", "string" },
            new[] { "1", "c", "a", "d", "b" });

        var tag = table.SerialFields.Single(sf => sf.Name == "Tag");

        Assert.Equal(new[] { "0", "1" }, tag.Members.Select(m => m.Name).ToArray());
        Assert.Equal("a", table.Data[0][tag.Members[0].Fields[0].Index].Value);
        Assert.Equal("c", table.Data[0][tag.Members[1].Fields[0].Index].Value);
    }

    /// <summary>
    /// And a group cannot be half named and half numbered.
    /// </summary>
    [Fact]
    public void A_group_that_names_one_outer_level_and_numbers_another_is_refused()
    {
        ParseModel(
            new[] { "id", "tag[0][0]", "tag[\"M\"][0]" },
            new[] { "key", "string", "string" },
            new[] { "1", "a", "b" });

        Assert.Contains("element number", Refusal());
    }

    /// <summary>
    /// The `:min` and `:max` rows become the column's bounds, and `:enum` its whitelist.
    /// </summary>
    /// <remarks>
    /// These sheets declare what a column may hold and check it afterwards with a script
    /// over the exported JSON. Read here, the check happens where the cell is.
    /// spec/column-constraints.md.
    /// </remarks>
    [Fact]
    public void The_constraint_rows_become_the_column_s_bounds()
    {
        var table = Parse(
            new[] { "id", "Level", "Kind" },
            new[] { "key", "number", "string" },
            new[] { ":min", "1", "-" },
            new[] { ":max", "99", "-" },
            new[] { ":enum", "-", "\"a\",\"b\"" },
            new[] { "1", "5", "a" });

        var level = table.Fields.Single(f => f.Name == "Level");
        Assert.Equal(1d, level.Constraints.Minimum);
        Assert.Equal(99d, level.Constraints.Maximum);

        var kind = table.Fields.Single(f => f.Name == "Kind");
        Assert.Equal(new[] { "a", "b" }, kind.Constraints.AllowedValues);
    }

    /// <summary>
    /// An unquoted `:enum` cell on a text column declares nothing.
    /// </summary>
    /// <remarks>
    /// The rule the sheets' own exporter follows: it pulls the quoted runs out of the cell,
    /// and a cell with none contributes no list. Read any other way, a cell holding a bare
    /// `1` says the only value allowed is `1` - which one real sheet has, and which turned
    /// every row of that column into a finding.
    /// </remarks>
    [Fact]
    public void An_unquoted_enum_cell_on_a_text_column_declares_nothing()
    {
        var table = Parse(
            new[] { "id", "Kind" },
            new[] { "key", "string" },
            new[] { ":enum", "1" },
            new[] { "1", "a" });

        Assert.Null(table.Fields.Single(f => f.Name == "Kind").Constraints.AllowedValues);
    }

    /// <summary>
    /// A column of any other type carries one value rather than a quoted list.
    /// </summary>
    [Fact]
    public void An_enum_cell_on_a_number_column_is_the_one_value_it_holds()
    {
        var table = Parse(
            new[] { "id", "Level" },
            new[] { "key", "number" },
            new[] { ":enum", "7" },
            new[] { "1", "7" });

        Assert.Equal(new[] { "7" }, table.Fields.Single(f => f.Name == "Level").Constraints.AllowedValues);
    }

    /// <summary>
    /// The key column never takes one, whatever the rows hold beside it.
    /// </summary>
    /// <remarks>
    /// Column zero is where each constraint row writes its own name, so asking it for a
    /// bound answers `:min` - and every row of the table then fails a whitelist of one
    /// value spelled `:enum`. That is what happened the first time this ran over a real
    /// workbook: 269,426 reports, one per row.
    /// </remarks>
    [Fact]
    public void The_key_column_takes_no_constraint()
    {
        var table = Parse(
            new[] { "id", "Level" },
            new[] { "key", "number" },
            new[] { ":min", "1" },
            new[] { ":enum", "-" },
            new[] { "1", "5" });

        Assert.True(table.Fields[0].Constraints.IsEmpty);
    }

    /// <summary>A row that declares nothing leaves the column unconstrained.</summary>
    [Fact]
    public void A_dash_declares_nothing()
    {
        var table = Parse(
            new[] { "id", "Level" },
            new[] { "key", "number" },
            new[] { ":min", "-" },
            new[] { ":max", "-" },
            new[] { "1", "5" });

        Assert.True(table.Fields.Single(f => f.Name == "Level").Constraints.IsEmpty);
    }

    /// <summary>
    /// `:requiredInObject` — required where the record it belongs to exists.
    /// </summary>
    /// <remarks>
    /// A validation rule rather than a shape the wire carries: enforcing it means a record
    /// whose required member is blank never reaches a file, so there is nothing left for the
    /// format to express. spec/record-member-optionality.md.
    ///
    /// The sheets declare this 216 times and nothing read the row until now.
    /// </remarks>
    [Fact]
    public void A_record_member_required_inside_its_object_is_checked_where_the_record_exists()
    {
        // Two elements of one record. Row 1 fills both members of element 0 and leaves
        // element 1 empty entirely; row 2 gives element 0 an Id and no Count.
        var table = Parse(
            new[] { "id", "Slot[0][\"Id\"]", "Slot[0][\"Count\"]" },
            new[] { "key", "number", "number" },
            new[] { ":requiredInObject", "-", "1" },
            new[] { "1", "10", "2" },
            new[] { "2", "10", "-" });

        var diagnostics = new Diagnostics();
        Tabbit.Cooking.ModelCooker.ValidateRequiredInRecord(table, table.RowSets.First(), diagnostics);

        var reported = Assert.Single(diagnostics.Entries);

        Assert.Equal(Severity.Error, reported.Severity);
        Assert.Contains("required", reported.Detail.Message);
        Assert.Contains("Count", reported.Detail.Message);

        // And it points at the cell the author left empty rather than at the constraint row.
        Assert.Equal(4, reported.Detail.Location?.Row);
    }

    /// <summary>
    /// An element that does not exist at all asks nothing.
    /// </summary>
    [Fact]
    public void A_record_that_does_not_exist_is_not_asked_about_its_members()
    {
        var table = Parse(
            new[] { "id", "Slot[0][\"Id\"]", "Slot[0][\"Count\"]" },
            new[] { "key", "number", "number" },
            new[] { ":requiredInObject", "1", "1" },
            new[] { "1", "-", "-" });

        var diagnostics = new Diagnostics();
        Tabbit.Cooking.ModelCooker.ValidateRequiredInRecord(table, table.RowSets.First(), diagnostics);

        Assert.Empty(diagnostics.Entries);
    }
}
