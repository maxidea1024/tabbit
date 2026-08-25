using System.Linq;
using Tabbit.Schema;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The schema notation - what `.tbs` files say, and what they are refused for.
/// </summary>
/// <remarks>
/// **The parser is a pure function, so this is where the notation is settled.** Everything
/// here is text in and declarations out, with no workbook, no recipe and no model - which is
/// what lets a question about the grammar be answered in a line rather than by building a
/// fixture workbook that writes the same thing in cells.
///
/// Two halves. The first says what the notation reads; the second says what it refuses, and
/// it is the longer one on purpose - a grammar tested only on what it accepts passes against
/// one that accepts everything, and the reports are the part somebody editing a file
/// actually meets.
///
/// notes/struct-dsl-design.md sections 4 to 6.
/// </remarks>
public class SchemaParserTests
{
    private static SchemaFile Parse(string source)
    {
        var diagnostics = new Diagnostics();
        var file = SchemaParser.Parse(source, "schema.tbs", diagnostics);

        Assert.True(diagnostics.Count == 0, Reported(diagnostics));
        return file;
    }

    private static string Refusal(string source)
    {
        var diagnostics = new Diagnostics();
        SchemaParser.Parse(source, "schema.tbs", diagnostics);

        Assert.True(diagnostics.Count > 0, "The notation was accepted.");
        return Reported(diagnostics);
    }

    private static string Reported(Diagnostics diagnostics)
        => string.Join("\n", diagnostics.Entries.Select(entry => entry.Detail.Message));

    private static int RefusalCount(string source)
    {
        var diagnostics = new Diagnostics();
        SchemaParser.Parse(source, "schema.tbs", diagnostics);
        return diagnostics.Count;
    }

    // --------------------------------------------------------------- declarations

    [Fact]
    public void A_struct_holds_the_members_written_under_it()
    {
        var file = Parse("""
            struct Reward
                field itemId int
                field count  int
            """);

        var declared = Assert.Single(file.Structs);
        Assert.Equal("Reward", declared.Name);
        Assert.Equal(["itemId", "count"], declared.Fields.Select(member => member.Name));
    }

    /// <summary>
    /// The three things polymorphism adds to a `struct` line - spec/polymorphism.md section 5.
    /// </summary>
    [Fact]
    public void A_struct_line_carries_abstract_extends_and_a_discriminator()
    {
        var file = Parse("""
            abstract struct Effect
                field chance int

            struct DamageEffect extends Effect @1
                field damage int

            struct HealEffect extends Effect @2
                field amount int
            """);

        var declared = file.Structs;
        Assert.Equal(3, declared.Count);

        Assert.True(declared[0].IsAbstract);
        Assert.Null(declared[0].BaseName);
        Assert.Equal(["chance"], declared[0].Fields.Select(member => member.Name));

        Assert.False(declared[1].IsAbstract);
        Assert.Equal("Effect", declared[1].BaseName);
        Assert.Equal(1, declared[1].VariantTag);
        Assert.Equal(["damage"], declared[1].Fields.Select(member => member.Name));

        Assert.Equal(2, declared[2].VariantTag);
    }

    /// <summary>
    /// An abstract struct with no members is the name of a set and nothing else, which section
    /// 3 of the spec keeps legal: the variants may share no surface at all.
    /// </summary>
    [Fact]
    public void An_abstract_struct_may_hold_no_members()
    {
        var file = Parse("abstract struct Reward");

        var declared = Assert.Single(file.Structs);
        Assert.True(declared.IsAbstract);
        Assert.Empty(declared.Fields);
    }

    [Fact]
    public void A_variant_may_carry_no_discriminator()
    {
        var file = Parse("""
            abstract struct Effect
            struct DamageEffect extends Effect
            """);

        Assert.Equal("Effect", file.Structs[1].BaseName);
        Assert.Equal(0, file.Structs[1].VariantTag);
    }

    [Fact]
    public void A_variant_takes_bracket_metadata_after_its_discriminator()
    {
        var file = Parse("""
            abstract struct Effect
            struct DamageEffect extends Effect @1 (sep=",")
                field damage int
            """);

        Assert.Equal(1, file.Structs[1].VariantTag);
        Assert.Equal(",", file.Structs[1].Meta.Value("sep"));
    }

    /// <summary>
    /// Indentation is decoration. A member joins the struct above it because of where it is,
    /// not because of how far in it starts - design section 9.3.
    /// </summary>
    [Fact]
    public void Indentation_says_nothing()
    {
        var indented = Parse("""
            struct Reward
                    field itemId int
            """);

        var flat = Parse("""
            struct Reward
            field itemId int
            """);

        Assert.Equal(
            indented.Structs[0].Fields.Select(member => member.Name),
            flat.Structs[0].Fields.Select(member => member.Name));
    }

    [Fact]
    public void A_second_struct_ends_the_first()
    {
        var file = Parse("""
            struct A
                field x int
            struct B
                field y int
            """);

        Assert.Equal(["A", "B"], file.Structs.Select(declared => declared.Name));
        Assert.Equal("x", Assert.Single(file.Structs[0].Fields).Name);
        Assert.Equal("y", Assert.Single(file.Structs[1].Fields).Name);
    }

    [Fact]
    public void An_enum_holds_the_entries_written_under_it()
    {
        var file = Parse("""
            enum Element
                value Fire  = 1
                value Ice   = 2
                value Light = 3
            """);

        var declared = Assert.Single(file.Enums);
        Assert.Equal("Element", declared.Name);
        Assert.Equal([1L, 2L, 3L], declared.Values.Select(entry => entry.Number));
    }

    [Fact]
    public void An_enum_entry_may_be_written_without_a_number()
    {
        var file = Parse("""
            enum Element
                value Fire
            """);

        Assert.Null(Assert.Single(file.Enums[0].Values).Number);
    }

    [Fact]
    public void Structs_and_enums_may_be_written_in_any_order_in_one_file()
    {
        var file = Parse("""
            struct A
                field grade Element
            enum Element
                value Fire = 1
            struct B
                field x int
            """);

        Assert.Equal(["A", "B"], file.Structs.Select(declared => declared.Name));
        Assert.Equal("Element", Assert.Single(file.Enums).Name);
    }

    // --------------------------------------------------------------- descriptions

    [Fact]
    public void A_doc_comment_block_belongs_to_the_declaration_under_it()
    {
        var file = Parse("""
            /// A payout.
            /// One row of it.
            struct Reward
                /// Which item.
                field itemId int
                field count  int
            """);

        Assert.Equal("A payout.\nOne row of it.", file.Structs[0].Comment);
        Assert.Equal("Which item.", file.Structs[0].Fields[0].Comment);
        Assert.Equal("", file.Structs[0].Fields[1].Comment);
    }

    [Fact]
    public void A_blank_line_does_not_take_a_description_away_from_what_follows()
    {
        var file = Parse("""
            /// A payout.

            struct Reward
                field itemId int
            """);

        Assert.Equal("A payout.", file.Structs[0].Comment);
    }

    /// <summary>
    /// `//` and `/* */` are notes to whoever edits the file and do not reach generated code -
    /// design section 5.
    /// </summary>
    [Fact]
    public void Ordinary_comments_are_not_kept()
    {
        var file = Parse("""
            // waiting on the art list
            struct Reward
                /* not settled */
                field itemId int  // one of the item tables
            """);

        Assert.Equal("", file.Structs[0].Comment);
        Assert.Equal("", file.Structs[0].Fields[0].Comment);
    }

    [Fact]
    public void A_block_comment_may_cross_lines()
    {
        var file = Parse("""
            struct Reward
            /* two
               lines */
                field itemId int
            """);

        Assert.Equal("itemId", Assert.Single(file.Structs[0].Fields).Name);
    }

    /// <summary>
    /// Four slashes is not three. A rule of separators drawn with slashes still being a
    /// comment rather than the description of what follows it.
    /// </summary>
    [Fact]
    public void Four_slashes_is_an_ordinary_comment()
    {
        var file = Parse("""
            //// ----------------------------
            struct Reward
                field itemId int
            """);

        Assert.Equal("", file.Structs[0].Comment);
    }

    // ---------------------------------------------------------------------- types

    [Theory]
    [InlineData("int", false, false, false)]
    [InlineData("int?", false, false, true)]
    [InlineData("int[]", true, false, false)]
    [InlineData("int?[]", true, true, false)]
    [InlineData("int[]?", true, false, true)]
    [InlineData("int?[]?", true, true, true)]
    public void The_two_question_marks_answer_for_the_element_and_for_the_value(
        string written, bool array, bool elementsOptional, bool optional)
    {
        var type = Parse($"struct S\n    field x {written}\n").Structs[0].Fields[0].Type;

        Assert.Equal(array, type.IsArray);
        Assert.Equal(elementsOptional, type.ElementsAreOptional);
        Assert.Equal(optional, type.IsOptional);
        Assert.Equal(written, type.ToString());
    }

    [Fact]
    public void A_reference_names_the_tables_it_may_point_at()
    {
        var type = Parse("struct S\n    field itemId foreign Item|CEquip\n").Structs[0].Fields[0].Type;

        Assert.Equal(SchemaTypeForm.Foreign, type.Form);
        Assert.Equal(["Item", "CEquip"], type.ForeignTables);
    }

    [Fact]
    public void A_reference_may_be_an_optional_array()
    {
        var type = Parse("struct S\n    field itemIds foreign Item[]?\n").Structs[0].Fields[0].Type;

        Assert.Equal(SchemaTypeForm.Foreign, type.Form);
        Assert.True(type.IsArray);
        Assert.True(type.IsOptional);
    }

    /// <summary>
    /// `set` and `map` are read all the way into the declarations and refused further down,
    /// by name - design section 4.7. The parser is not where the container list lives, so it
    /// reads any name with arguments and leaves what that name means to resolution.
    /// </summary>
    [Fact]
    public void A_container_type_is_read_with_its_arguments()
    {
        var type = Parse("struct S\n    field prices map<int,Reward>[]\n").Structs[0].Fields[0].Type;

        Assert.Equal(SchemaTypeForm.Container, type.Form);
        Assert.Equal("map", type.Name);
        Assert.Equal(["int", "Reward"], type.Arguments.Select(argument => argument.Name));
        Assert.True(type.IsArray);
        Assert.Equal("map<int,Reward>[]", type.ToString());
    }

    // ----------------------------------------------------------------- wire tags

    /// <summary>
    /// With no tag written, the position is the tag - which is what makes moving a line a
    /// change to the file that comes out. Design section 4.5.
    /// </summary>
    [Fact]
    public void An_untagged_struct_numbers_its_members_by_position()
    {
        var declared = Parse("""
            struct Vector3
                field x float
                field y float
                field z float
            """).Structs[0];

        Assert.False(declared.TagsAreWritten);
        Assert.Equal([1, 2, 3], declared.Fields.Select(declared.TagOf));
    }

    [Fact]
    public void A_written_tag_is_the_tag()
    {
        var declared = Parse("""
            struct Vector3
                field x@7 float
                field y@8 float
            """).Structs[0];

        Assert.True(declared.TagsAreWritten);
        Assert.Equal([7, 8], declared.Fields.Select(declared.TagOf));
    }

    /// <summary>
    /// A gravestone keeps its position so that nothing else is given it, which is the whole
    /// of what it is for.
    /// </summary>
    [Fact]
    public void A_removed_member_holds_its_number_and_carries_no_data()
    {
        var declared = Parse("""
            struct Reward
                field itemId int
                field oldCount int (removed)
                field count int
            """).Structs[0];

        Assert.Equal(["itemId", "count"], declared.LiveFields.Select(member => member.Name));
        Assert.Equal(3, declared.TagOf(declared.Fields[2]));
    }

    // ----------------------------------------------------------------- metadata

    [Fact]
    public void Brackets_carry_flags_and_pairs()
    {
        var member = Parse("""
            struct Reward
                field count int (min=1, max=9999, notDefault)
            """).Structs[0].Fields[0];

        Assert.Equal("1", member.Meta.Value("min"));
        Assert.Equal("9999", member.Meta.Value("max"));
        Assert.True(member.Meta.Has("notDefault"));
        Assert.Equal("", member.Meta.Value("notDefault"));
        Assert.Null(member.Meta.Value("min2"));
    }

    /// <summary>
    /// A value holding a comma is quoted, and that is the whole of the escaping rule -
    /// design section 4.2.
    /// </summary>
    [Fact]
    public void A_quoted_value_may_hold_a_comma()
    {
        var member = Parse("""
            struct S
                field name string (regex="^a,b$")
            """).Structs[0].Fields[0];

        Assert.Equal("^a,b$", member.Meta.Value("regex"));
    }

    /// <summary>
    /// An unquoted value is several tokens to the lexer - `^[a-z]+$` is five - and is put
    /// back together because they were written touching.
    /// </summary>
    [Theory]
    [InlineData("regex=^[a-z]+$", "regex", "^[a-z]+$")]
    [InlineData("refs=Item;CEquip", "refs", "Item;CEquip")]
    [InlineData("allowed=1;2;3", "allowed", "1;2;3")]
    [InlineData("size=1..3", "size", "1..3")]
    [InlineData("x.path=art/icons", "x.path", "art/icons")]
    public void An_unquoted_value_is_what_was_written(string entry, string key, string value)
    {
        var member = Parse($"struct S\n    field x int ({entry})\n").Structs[0].Fields[0];

        Assert.Equal(value, member.Meta.Value(key));
    }

    /// <summary>
    /// The parser does not know what a key means and does not check one - design section 6.4,
    /// which is the policy `LayoutOptions` already runs on.
    /// </summary>
    [Fact]
    public void An_unknown_key_is_carried_rather_than_refused()
    {
        var member = Parse("struct S\n    field x int (whatever=7)\n").Structs[0].Fields[0];

        Assert.Equal("7", member.Meta.Value("whatever"));
    }

    /// <summary>
    /// And a key beginning `x.` is never reported as unclaimed, which is what makes a
    /// project's own tag usable at all.
    /// </summary>
    [Fact]
    public void A_key_nobody_claims_is_found_unless_it_is_a_project_tag()
    {
        var member = Parse("struct S\n    field x int (min=1, typo=2, x.own=3)\n").Structs[0].Fields[0];

        Assert.Equal(["typo"], member.Meta.Beyond("min").Select(entry => entry.Key));
    }

    [Fact]
    public void An_enum_entry_carries_its_own_metadata()
    {
        var entry = Parse("""
            enum Element
                value Fire = 1 (alias="불")
            """).Enums[0].Values[0];

        Assert.Equal("불", entry.Meta.Value("alias"));
    }

    [Fact]
    public void A_default_value_is_kept_as_it_was_written()
    {
        var member = Parse("struct S\n    field grade Element = Fire\n").Structs[0].Fields[0];

        Assert.Equal("Fire", member.DefaultValue);
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void A_line_that_starts_with_nothing_known_is_refused()
        => Assert.Contains("is not a declaration", Refusal("record Reward"));

    [Fact]
    public void Abstract_without_struct_after_it_is_refused()
        => Assert.Contains("`struct` has to follow it", Refusal("abstract Effect"));

    /// <summary>
    /// Refused by name rather than left to the unknown-keyword report: the word exists here,
    /// and being told it does not would send somebody looking for a spelling mistake.
    /// </summary>
    [Fact]
    public void Extends_on_a_line_of_its_own_is_refused()
        => Assert.Contains("written on the `struct` line", Refusal("extends Effect"));

    /// <summary>
    /// One level, enforced by the grammar - spec/polymorphism.md section 5.1.
    /// </summary>
    [Fact]
    public void A_variant_that_is_itself_abstract_is_refused()
    {
        string reported = Refusal("""
            abstract struct Effect
            abstract struct DamageEffect extends Effect
            """);

        Assert.Contains("cannot itself be abstract", reported);
    }

    [Fact]
    public void A_discriminator_on_a_struct_that_extends_nothing_is_refused()
        => Assert.Contains("extends nothing", Refusal("struct Reward @2"));

    [Fact]
    public void A_member_outside_a_struct_is_refused()
        => Assert.Contains("no `struct` above it", Refusal("field itemId int"));

    [Fact]
    public void A_member_inside_an_enum_is_refused()
        => Assert.Contains("`enum Element`", Refusal("enum Element\n    field itemId int\n"));

    [Fact]
    public void An_entry_inside_a_struct_is_refused()
        => Assert.Contains("`struct Reward`", Refusal("struct Reward\n    value Fire = 1\n"));

    [Fact]
    public void A_declaration_with_no_name_is_refused()
        => Assert.Contains("needs a name", Refusal("struct"));

    [Fact]
    public void A_name_that_is_not_an_identifier_is_refused()
        => Assert.Contains("cannot be a name", Refusal("struct 3Rewards"));

    [Fact]
    public void A_member_with_no_type_is_refused()
        => Assert.Contains("needs a type", Refusal("struct S\n    field x\n"));

    [Fact]
    public void An_array_of_arrays_is_refused()
        => Assert.Contains("array of arrays", Refusal("struct S\n    field x int[][]\n"));

    [Fact]
    public void A_reference_with_no_table_is_refused()
        => Assert.Contains("needs a name", Refusal("struct S\n    field x foreign\n"));

    [Fact]
    public void Two_members_of_one_name_are_refused()
        => Assert.Contains("declares `x` twice", Refusal("struct S\n    field x int\n    field x int\n"));

    [Fact]
    public void Two_entries_of_one_name_are_refused()
        => Assert.Contains(
            "declares `Fire` twice",
            Refusal("enum E\n    value Fire = 1\n    value Fire = 2\n"));

    [Fact]
    public void Two_entries_of_one_number_are_refused()
        => Assert.Contains(
            "already carries",
            Refusal("enum E\n    value Fire = 1\n    value Ice = 1\n"));

    [Fact]
    public void An_entry_whose_value_is_not_a_number_is_refused()
        => Assert.Contains("whole number", Refusal("enum E\n    value Fire = hot\n"));

    [Fact]
    public void A_tag_that_is_not_a_number_is_refused()
        => Assert.Contains("whole number", Refusal("struct S\n    field x@a int\n"));

    [Fact]
    public void A_tag_of_zero_is_refused()
        => Assert.Contains("count from one", Refusal("struct S\n    field x@0 int\n"));

    [Fact]
    public void Two_members_under_one_tag_are_refused()
        => Assert.Contains(
            "already carries",
            Refusal("struct S\n    field x@1 int\n    field y@1 int\n"));

    /// <summary>
    /// All or none. With some members tagged, an untagged one's number would depend on how
    /// many members before it happened to be tagged - design section 4.5.
    /// </summary>
    [Fact]
    public void A_struct_that_tags_some_members_is_refused()
        => Assert.Contains(
            "Tag all of them or none",
            Refusal("struct S\n    field x@1 int\n    field y int\n"));

    [Fact]
    public void Metadata_that_is_never_closed_is_refused()
        => Assert.Contains("never closed", Refusal("struct S\n    field x int (min=1\n"));

    [Fact]
    public void A_key_written_twice_is_refused()
        => Assert.Contains("written twice", Refusal("struct S\n    field x int (min=1, min=2)\n"));

    [Fact]
    public void A_key_with_an_empty_value_is_refused()
        => Assert.Contains("nothing after it", Refusal("struct S\n    field x int (min=)\n"));

    /// <summary>
    /// The one key that is an error rather than an unknown key, because there is somewhere
    /// else to write a description and it is better - design section 3.
    /// </summary>
    [Fact]
    public void A_comment_key_is_refused_and_points_at_the_notation_that_works()
        => Assert.Contains("`///`", Refusal("struct S\n    field x int (comment=\"level\")\n"));

    [Fact]
    public void An_unquoted_value_holding_a_space_is_refused()
        => Assert.Contains(
            "`words` is where `,` or `)` should be",
            Refusal("struct S\n    field x int (x.note=two words)\n"));

    [Fact]
    public void A_string_with_no_closing_quote_is_refused()
        => Assert.Contains("no closing quote", Refusal("struct S\n    field x int (regex=\"^a$)\n"));

    [Fact]
    public void A_block_comment_with_no_end_is_refused()
        => Assert.Contains("no `*/`", Refusal("struct S\n/* forgotten\n    field x int\n"));

    [Fact]
    public void A_description_with_no_declaration_after_it_is_refused()
        => Assert.Contains("no declaration after it", Refusal("struct S\n    field x int\n/// dangling\n"));

    [Fact]
    public void Something_written_past_the_end_of_a_declaration_is_refused()
        => Assert.Contains("should be", Refusal("struct S extra\n"));

    /// <summary>
    /// A file with six mistakes says six things. A parser that stopped at the first would
    /// make correcting a file a matter of one run per mistake.
    /// </summary>
    [Fact]
    public void Every_mistake_in_a_file_is_reported()
        => Assert.Equal(3, RefusalCount("""
            struct 3Bad
            field loose int
            enum E
                value Fire = hot
            """));
}
