using System.Collections.Generic;
using System.Linq;
using Tabbit;
using Tabbit.Cooking;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Tabbit.Sources;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The `rescue` sheet layout: one table per sheet, named by the sheet tab less its
/// trailing `Table`, with three header rows and no entity markers.
///
/// Written against raw sheets built here rather than against a committed workbook. The
/// questions are all about how a particular arrangement of cells is read - a commented-out
/// row, a string key, two columns whose names collide once normalized - and each of those
/// as a workbook would mean another .xlsx that has to be opened in Excel to review.
/// The end-to-end path is covered by converting the real project instead.
/// </summary>
public class RescueLayoutTests
{
    #region Building sheets

    /// <summary>
    /// A sheet in the rescue layout: descriptions, names, types, then data.
    /// </summary>
    private static RawSheet Sheet(
        string sheetName, string[] comments, string[] names, string[] types, params string[][] rows)
    {
        var sheet = new RawSheet
        {
            Layout = SheetLayout.Default,
            Location = new Location { Filename = "memory.xlsx", Sheet = sheetName, Column = 0, Row = 0 },
        };

        var all = new List<string[]> { comments, names, types };
        all.AddRange(rows);

        int width = all.Max(row => row.Length);

        for (int rowIndex = 0; rowIndex < all.Count; rowIndex++)
        {
            var cells = new List<RawCell>(width);

            for (int column = 0; column < width; column++)
            {
                cells.Add(new RawCell
                {
                    Location = new Location
                    {
                        Filename = "memory.xlsx",
                        Sheet = sheetName,
                        Column = column,
                        Row = rowIndex,
                    },
                    Value = column < all[rowIndex].Length ? all[rowIndex][column] : "",
                });
            }

            sheet.Rows.Add(cells);
        }

        sheet.ColumnCount = width;

        return sheet;
    }

    /// <summary>
    /// The enum-collection sheet, whose name is what marks it as one. Each enum is a
    /// column: description, name, then labels.
    /// </summary>
    private static RawSheet EnumSheet(params (string Name, string[] Labels)[] enums)
    {
        int height = enums.Max(e => e.Labels.Length);

        var comments = enums.Select(_ => "").ToArray();
        var names = enums.Select(e => e.Name).ToArray();

        var rows = new List<string[]>();
        for (int i = 0; i < height; i++)
            rows.Add(enums.Select(e => i < e.Labels.Length ? e.Labels[i] : "").ToArray());

        // The label rows start on the type row, so the first one is passed as `types`.
        return Sheet("TableEnums", comments, names, rows[0], rows.Skip(1).ToArray());
    }

    private static Model Cook(DuplicateIndexPolicy policy, params RawSheet[] sheets)
    {
        var raw = new RawModel();

        foreach (var sheet in sheets)
        {
            sheet.Layout = new SheetLayout("rescue", policy);
            raw.Sheets.Add(sheet);
        }

        return new ModelCooker().Cook(new Options(), new RecipeModel(), raw);
    }

    private static Model Cook(params RawSheet[] sheets) => Cook(DuplicateIndexPolicy.Error, sheets);

    private static object[] Column(Table table, string field)
    {
        var f = table.GetField(field, null);
        return table.Data.Select(row => row[f.Index].Value).ToArray();
    }

    #endregion


    [Fact]
    public void A_sheet_becomes_a_table_named_after_its_tab()
    {
        var model = Cook(Sheet("ItemTable",
            new[] { "아이디", "이름" },
            new[] { "Id", "Name" },
            new[] { "int", "string" },
            new[] { "1", "sword" },
            new[] { "2", "shield" }));

        var table = Assert.Single(model.Tables);

        // Less the tab's trailing `Table`: the generators append that word to build the
        // container type's name, so keeping it would generate `ItemTableTable`.
        Assert.Equal("Item", table.Name);
        Assert.Equal("ItemTable", table.RawName);
        Assert.Equal(2, table.Data.Count);

        // Row 1 is the column's description, which is the only place this layout has for one.
        Assert.Equal("아이디", table.Fields[0].Comment);

        // The first column indexes, the way the first column of an entity does in the
        // layout Tabbit defines.
        Assert.True(table.Fields[0].Indexing);
        Assert.False(table.Fields[1].Indexing);
    }

    [Fact]
    public void A_tab_named_just_Table_or_without_the_suffix_keeps_its_name()
    {
        var model = Cook(
            Sheet("Table",
                new[] { "", "" }, new[] { "Id", "Name" }, new[] { "int", "string" },
                new[] { "1", "a" }),
            Sheet("Hero",
                new[] { "", "" }, new[] { "Id", "Name" }, new[] { "int", "string" },
                new[] { "1", "b" }));

        // `Table` alone has nothing left once the word comes off, and `Hero` never had it.
        Assert.Equal(new[] { "Table", "Hero" }, model.Tables.Select(t => t.Name));
    }

    [Fact]
    public void Two_tabs_that_yield_one_table_name_collide()
    {
        // `Item` and `ItemTable` are different tabs to whoever named them, and only
        // become the same table here - so the message says how the names are made.
        var error = Assert.Throws<TabbitException>(() => Cook(
            Sheet("Item",
                new[] { "", "" }, new[] { "Id", "Name" }, new[] { "int", "string" },
                new[] { "1", "a" }),
            Sheet("ItemTable",
                new[] { "", "" }, new[] { "Id", "Name" }, new[] { "int", "string" },
                new[] { "1", "b" })));

        Assert.Contains("already defined", error.ToString());
        Assert.Contains("trailing `Table`", error.ToString());
    }

    [Fact]
    public void A_hash_on_the_description_drops_the_column()
    {
        var model = Cook(Sheet("ItemTable",
            new[] { "아이디", "#작업용", "이름" },
            new[] { "Id", "Scratch", "Name" },
            new[] { "int", "int", "string" },
            new[] { "1", "999", "sword" }));

        var table = Assert.Single(model.Tables);

        Assert.Equal(new[] { "Id", "Name" }, table.Fields.Select(f => f.Name));
        Assert.Equal(new object[] { "sword" }, Column(table, "Name"));
    }

    [Theory]
    [InlineData("Int", "7", ValueType.Int32)]
    [InlineData("int", "7", ValueType.Int32)]
    [InlineData("String", "seven", ValueType.String)]
    [InlineData("Float", "7.5", ValueType.Float)]
    [InlineData("Bool", "TRUE", ValueType.Bool)]
    [InlineData("long", "7", ValueType.Int64)]
    [InlineData("intArray", "1;2", ValueType.Int32Array)]
    [InlineData("Intarray", "1;2", ValueType.Int32Array)]
    [InlineData("stringArray", "a;b", ValueType.StringArray)]
    public void Type_names_are_read_however_the_sheet_spells_them(
        string spelling, string value, ValueType expected)
    {
        var model = Cook(Sheet("T",
            new[] { "", "" },
            new[] { "Id", "Value" },
            new[] { "int", spelling },
            new[] { "1", value }));

        Assert.Equal(expected, model.Tables[0].Fields[1].Type);
    }

    [Fact]
    public void An_enum_column_resolves_against_the_enum_sheet()
    {
        var model = Cook(
            EnumSheet(("GradeType", new[] { "N", "R", "SR" })),
            Sheet("HeroTable",
                new[] { "", "" },
                new[] { "Id", "Grade" },
                new[] { "int", "enum:GradeType" },
                new[] { "1", "SR" },
                new[] { "2", "N" }));

        var grade = Assert.Single(model.Enums);

        // No values in the sheet, so they are the order the labels appear in - with a
        // zero left for the `None` the recipe inserts.
        Assert.Equal(new[] { "None", "N", "R", "SR" }, grade.Labels.Select(l => l.Name));
        Assert.Equal(new[] { 0, 1, 2, 3 }, grade.Labels.Select(l => l.Value));

        Assert.Equal(new object[] { 3, 1 }, Column(model.Tables[0], "Grade"));
    }

    [Fact]
    public void None_takes_zero_wherever_the_sheet_puts_it()
    {
        var model = Cook(
            EnumSheet(("StatType", new[] { "ATK", "None", "DEF" })),
            Sheet("T", new[] { "", "" }, new[] { "Id", "Stat" }, new[] { "int", "enum:StatType" },
                new[] { "1", "None" }));

        var stat = Assert.Single(model.Enums);

        Assert.Equal(0, stat.Labels.Single(l => l.Name == "None").Value);
        Assert.Equal(new[] { 1, 0, 2 }, stat.Labels.Select(l => l.Value));
        Assert.Equal(new object[] { 0 }, Column(model.Tables[0], "Stat"));
    }

    [Fact]
    public void A_hash_column_next_to_an_enum_describes_its_labels()
    {
        var sheet = Sheet("TableEnums",
            new[] { "등급", "#설명" },
            new[] { "GradeType", "" },
            new[] { "N", "보통" },
            new[] { "R", "희귀" });

        var model = Cook(sheet);

        var grade = Assert.Single(model.Enums);

        Assert.Equal(new[] { "N", "R" }, grade.Labels.Where(l => l.Name != "None").Select(l => l.Name));
        Assert.Equal("보통", grade.Labels.Single(l => l.Name == "N").Comment);
        Assert.Equal("희귀", grade.Labels.Single(l => l.Name == "R").Comment);
    }

    [Fact]
    public void A_hash_in_the_index_cell_comments_the_row_out()
    {
        var model = Cook(Sheet("T",
            new[] { "", "" },
            new[] { "Id", "Name" },
            new[] { "int", "string" },
            new[] { "1", "kept" },
            new[] { "#2", "commented out" },
            new[] { "3", "kept too" }));

        Assert.Equal(new object[] { 1, 3 }, Column(model.Tables[0], "Id"));
    }

    [Fact]
    public void A_row_with_no_index_but_other_content_is_dropped()
    {
        // The shape a half-written row takes in these sheets: somebody typed the Korean
        // name and stopped. It is not the end of the table - rows follow it.
        var model = Cook(Sheet("T",
            new[] { "", "" },
            new[] { "Id", "Name" },
            new[] { "int", "string" },
            new[] { "1", "done" },
            new[] { "", "being written" },
            new[] { "2", "done too" }));

        Assert.Equal(new object[] { 1, 2 }, Column(model.Tables[0], "Id"));
    }

    [Fact]
    public void Duplicate_index_values_are_reported_by_default()
    {
        var error = Assert.Throws<TabbitException>(() => Cook(Sheet("T",
            new[] { "", "" },
            new[] { "Id", "Name" },
            new[] { "int", "string" },
            new[] { "1", "first" },
            new[] { "1", "second" })));

        // Reported by the shared validation rather than by the layout: the rule is the
        // same whatever arrangement of cells the rows came out of.
        Assert.Contains(error.Details, detail => detail.Message.Contains("repeats the value"));
    }

    [Theory]
    [InlineData(DuplicateIndexPolicy.KeepFirst, "first")]
    [InlineData(DuplicateIndexPolicy.KeepLast, "second")]
    public void A_legacy_workbook_may_choose_which_duplicate_wins(DuplicateIndexPolicy policy, string kept)
    {
        var model = Cook(policy, Sheet("T",
            new[] { "", "" },
            new[] { "Id", "Name" },
            new[] { "int", "string" },
            new[] { "1", "first" },
            new[] { "1", "second" },
            new[] { "2", "other" }));

        var table = model.Tables[0];

        Assert.Equal(new object[] { 1, 2 }, Column(table, "Id"));
        Assert.Equal(kept, Column(table, "Name")[0]);
    }

    [Fact]
    /// <summary>
    /// A table keyed by a string keeps that column as its primary index.
    /// </summary>
    /// <remarks>
    /// An ordinal `Index` column used to be inserted in front of it and the sheet's own key
    /// demoted to a secondary index. The reason given was that the binary format and the
    /// generated readers address a row by an integer, and that turned out not to be so - the
    /// format has no index of its own and the lookup is a dictionary over whatever the field
    /// is. What a non-`int` key really costs is being *referenced*, and nothing references
    /// this table; a reference that did would be refused by name where references are
    /// checked.
    ///
    /// So the column count is the thing to assert: a synthesized column would show up as a
    /// third field the sheet never wrote.
    /// </remarks>
    public void A_string_key_stays_the_primary_index()
    {
        var model = Cook(Sheet("ConfigTable",
            new[] { "", "" },
            new[] { "Id", "Value" },
            new[] { "String", "String" },
            new[] { "MAX_LEVEL", "99" },
            new[] { "MIN_LEVEL", "1" }));

        var table = model.Tables[0];

        Assert.Equal(new[] { "Id", "Value" }, table.Fields.Select(f => f.Name));
        Assert.Equal(ValueType.String, table.Fields[0].Type);
        Assert.True(table.Fields[0].Indexing);
        Assert.False(table.Fields[1].Indexing);

        Assert.Equal(new object[] { "MAX_LEVEL", "MIN_LEVEL" }, Column(table, "Id"));
        Assert.Equal(new[] { 0, 1 }, table.Fields.Select(f => f.Index));
    }

    [Fact]
    public void Numbered_columns_do_not_fold_into_an_array()
    {
        // The reason: in this layout the numbers are part of the names rather than a
        // convention, and one real table numbers three columns that are three different
        // enums. Folding them is not a nicer API but a conversion that refuses to run.
        var model = Cook(
            EnumSheet(("JobType", new[] { "Melee" }), ("GradeType", new[] { "N" })),
            Sheet("T",
                new[] { "", "", "" },
                new[] { "Id", "Condition_1", "Condition_2" },
                new[] { "int", "enum:JobType", "enum:GradeType" },
                new[] { "1", "Melee", "N" }));

        var table = model.Tables[0];

        Assert.Equal(new[] { "Id", "Condition1", "Condition2" }, table.SerialFields.Select(s => s.Name));
        Assert.All(table.SerialFields, serial => Assert.False(serial.IsArray));
    }

    [Fact]
    public void Two_columns_that_normalize_to_one_name_are_reported_with_both_spellings()
    {
        var error = Assert.Throws<TabbitException>(() => Cook(Sheet("T",
            new[] { "", "아이콘", "아이콘 경로" },
            new[] { "Id", "IconPath", "Icon_Path" },
            new[] { "int", "String", "String" },
            new[] { "1", "", "a" })));

        string message = error.ToString();

        // Both as the sheet spells them: the author is looking at two headings that do
        // not match, and a message naming only the normalized one sends them hunting.
        Assert.Contains("IconPath", message);
        Assert.Contains("Icon_Path", message);
    }

    [Fact]
    public void A_sheet_that_is_not_a_table_is_left_alone()
    {
        // Reference tabs and working notes sit next to the data in these workbooks, and
        // nothing in a sheet says which it is. A header that is not one is the evidence -
        // and it is the type row that carries the evidence, because a reference tab's
        // heading row is full of perfectly good identifiers.
        var model = Cook(
            Sheet("ItemTable",
                new[] { "", "" }, new[] { "Id", "Name" }, new[] { "int", "string" },
                new[] { "1", "sword" }),
            Sheet("캐릭터 리스트참고용",
                new[] { "", "" },
                new[] { "CharacterType", "CharacterID" },
                new[] { "PC", "10101" },
                new[] { "PC", "10201" }));

        Assert.Equal(new[] { "Item" }, model.Tables.Select(t => t.Name));
    }

    [Fact]
    public void An_enum_the_sheets_never_declared_is_reported_against_the_column_that_wanted_it()
    {
        var error = Assert.Throws<TabbitException>(() => Cook(Sheet("T",
            new[] { "", "" },
            new[] { "Id", "Channel" },
            new[] { "int", "enum:ChannelType" },
            new[] { "1", "All" })));

        Assert.Contains("ChannelType", error.ToString());
        Assert.Contains("TableEnums", error.ToString());
    }

    [Fact]
    public void Array_cells_split_on_the_recipe_delimiter()
    {
        var model = Cook(Sheet("T",
            new[] { "", "" },
            new[] { "Id", "SpawnIds" },
            new[] { "int", "intArray" },
            new[] { "1", "120101;120102;120112" },
            new[] { "2", "" }));

        var table = model.Tables[0];

        Assert.Equal(new[] { 120101, 120102, 120112 }, (int[])Column(table, "SpawnIds")[0]);
        Assert.Empty((int[])Column(table, "SpawnIds")[1]);
    }

    [Fact]
    public void Both_layouts_can_be_read_in_one_run()
    {
        // The case this is all for: a project part-way through being converted has some
        // workbooks in each layout, and a table in one may be typed with an enum declared
        // in the other.
        var rescue = Sheet("HeroTable",
            new[] { "", "" },
            new[] { "Id", "Grade" },
            new[] { "int", "enum:GradeType" },
            new[] { "1", "SR" });
        rescue.Layout = new SheetLayout("rescue", DuplicateIndexPolicy.Error);

        // The declaration-cell form: the declaration and its description, then the `:field`
        // row naming the columns, then the labels. `Sheet` writes rows as given, so the
        // marker column is the first cell of each.
        var tabbit = Sheet("Declarations",
            new[] { ":enum GradeType", "등급", "" },
            new[] { ":field", "label", "value" },
            new[] { "", "N", "1" },
            new[] { "", "SR", "2" });
        tabbit.Layout = SheetLayout.Default;

        var raw = new RawModel();
        raw.Sheets.Add(rescue);
        raw.Sheets.Add(tabbit);

        var model = new ModelCooker().Cook(new Options(), new RecipeModel(), raw);

        Assert.Equal(new[] { "GradeType" }, model.Enums.Select(e => e.Name));
        Assert.Equal(new[] { "Hero" }, model.Tables.Select(t => t.Name));
        Assert.Equal(new object[] { 2 }, Column(model.Tables[0], "Grade"));
    }

    [Fact]
    public void A_source_entry_can_name_its_own_array_delimiter()
    {
        var sheet = Sheet("ItemTable",
            new[] { "", "" },
            new[] { "Id", "Tags" },
            new[] { "int", "stringArray" },
            new[] { "1", "a|b|c" });

        sheet.Layout = new SheetLayout("rescue", DuplicateIndexPolicy.Error, '|');

        var raw = new RawModel();
        raw.Sheets.Add(sheet);

        var model = new ModelCooker().Cook(new Options(), new RecipeModel(), raw);

        Assert.Equal(
            new object[] { new[] { "a", "b", "c" } },
            Column(model.Tables[0], "Tags"));
    }

    [Fact]
    public void Two_entries_can_delimit_their_arrays_differently()
    {
        // The reason the setting is per entry: these two sets of sheets were written by
        // different people, and neither has to be rewritten for the other to be read.
        var pipes = Sheet("ItemTable",
            new[] { "", "" }, new[] { "Id", "Tags" }, new[] { "int", "stringArray" },
            new[] { "1", "a|b" });
        pipes.Layout = new SheetLayout("rescue", DuplicateIndexPolicy.Error, '|');

        var semicolons = Sheet("HeroTable",
            new[] { "", "" }, new[] { "Id", "Tags" }, new[] { "int", "stringArray" },
            new[] { "1", "x;y" });
        semicolons.Layout = new SheetLayout("rescue", DuplicateIndexPolicy.Error);

        var raw = new RawModel();
        raw.Sheets.Add(pipes);
        raw.Sheets.Add(semicolons);

        var model = new ModelCooker().Cook(new Options(), new RecipeModel(), raw);

        Assert.Equal(
            new object[] { new[] { "a", "b" } },
            Column(model.Tables.Single(t => t.Name == "Item"), "Tags"));

        Assert.Equal(
            new object[] { new[] { "x", "y" } },
            Column(model.Tables.Single(t => t.Name == "Hero"), "Tags"));
    }

    [Fact]
    public void An_entry_delimiter_that_is_not_one_character_is_reported()
    {
        var recipe = new RecipeModel.SourceRecipeGroup.XlsxRecipe { ArrayDelimiter = "::" };

        var error = Assert.Throws<TabbitException>(
            () => SheetImportSettings.From(recipe, "Sources.Xlsx[0]"));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.EntryArrayDelimiterNotOneCharacter, error.MessageId);
    }
}
