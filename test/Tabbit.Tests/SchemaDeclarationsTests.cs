using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Schema;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What a set of schema files means once every one of them has been read.
/// </summary>
/// <remarks>
/// The parser answers for one file and this answers for all of them: a member typed with a
/// struct declared three files later, a name two files both claim, a struct that comes round
/// to holding itself. None of those are questions one file can be asked.
///
/// **The two halves of the rule are the point.** Types are closed - every member's type has
/// to be found in these files or built in - and references are open, because a table is not
/// declared here and checking one would mean opening a workbook. That is what lets an editor
/// read a folder of these on its own.
///
/// notes/struct-dsl-design.md sections 4.4, 4.6, 4.7 and 9.
/// </remarks>
public class SchemaDeclarationsTests
{
    private sealed record Read(SchemaDeclarations Declarations, Model Model, Diagnostics Diagnostics);

    private static Read Gather(params string[] texts)
    {
        var diagnostics = new Diagnostics();
        var model = new Model();
        var context = new CookingContext(model, new Tabbit.Recipe.RecipeModel(), diagnostics);

        var files = texts
            .Select((text, at) => new RawSchemaFile { Name = $"file{at}.tbs", Text = text })
            .ToList();

        var declarations = SchemaDeclarations.Read(files, diagnostics);
        declarations.DeclareEnums(model, diagnostics);
        declarations.Resolve(model, context, diagnostics);

        return new Read(declarations, model, diagnostics);
    }

    private static string Refusal(params string[] texts)
    {
        var read = Gather(texts);

        Assert.True(read.Diagnostics.Count > 0, "The declarations were accepted.");
        return string.Join("\n", read.Diagnostics.Entries.Select(entry => entry.Detail.Message));
    }

    private static void Accepted(params string[] texts)
    {
        var read = Gather(texts);

        Assert.True(
            read.Diagnostics.Count == 0,
            string.Join("\n", read.Diagnostics.Entries.Select(entry => entry.Detail.Message)));
    }

    // -------------------------------------------------------------- what is gathered

    [Fact]
    public void Declarations_from_several_files_are_one_set()
    {
        var read = Gather(
            "struct Reward\n    field itemId int\n",
            "enum Element\n    value Fire = 1\n");

        Assert.Equal(["Reward"], read.Declarations.Structs.Keys);
        Assert.Equal(["Element"], read.Declarations.Enums.Keys);
    }

    /// <summary>
    /// Forwards, backwards and across files, because nothing resolves until every file is
    /// in - design section 4.6.
    /// </summary>
    [Fact]
    public void A_member_may_be_typed_with_something_declared_later_or_elsewhere()
        => Accepted(
            "struct Skill\n    field effect Effect\n    field grade Element\n",
            "struct Effect\n    field damage int\n",
            "enum Element\n    value Fire = 1\n");

    // ---------------------------------------------------------------------- enums

    [Fact]
    public void A_declared_enum_reaches_the_model_with_its_labels()
    {
        var model = Gather("""
            /// What it is made of.
            enum Element
                /// Burns.
                value Fire = 1
                value Ice  = 2
            """).Model;

        var declared = Assert.Single(model.Enums);
        Assert.Equal("Element", declared.Name);
        Assert.Equal("What it is made of.", declared.Comment);
        Assert.Equal(["Fire", "Ice"], declared.Labels.Select(label => label.Name));
        Assert.Equal([1, 2], declared.Labels.Select(label => label.Value));
        Assert.Equal("Burns.", declared.Labels[0].Comment);
    }

    /// <summary>
    /// So a type cell may name one. The check that a type name is recognized asks the model,
    /// which is why the enums go in before a sheet is read.
    /// </summary>
    [Fact]
    public void A_declared_enum_is_a_type_name_the_sheets_recognize()
    {
        var model = Gather("enum Element\n    value Fire = 1\n").Model;

        Assert.True(model.ContainsEnum("Element"));
    }

    [Fact]
    public void An_entry_with_no_number_counts_on_from_the_one_before_it()
    {
        var model = Gather("""
            enum Element
                value None
                value Fire
                value Ice = 10
                value Light
            """).Model;

        Assert.Equal([0, 1, 10, 11], model.Enums[0].Labels.Select(label => label.Value));
    }

    [Fact]
    public void An_entry_whose_number_will_not_fit_is_refused()
        => Assert.Contains(
            "32-bit number",
            Refusal("enum E\n    value Huge = 5000000000\n"));

    // ------------------------------------------------------------------ member types

    [Theory]
    [InlineData("int")]
    [InlineData("string?")]
    [InlineData("float[]")]
    [InlineData("bitset")]
    [InlineData("uuid?[]?")]
    public void A_built_in_type_is_a_member_type(string written)
        => Accepted($"struct S\n    field x {written}\n");

    [Fact]
    public void A_reference_is_not_checked_here_at_all()
        => Accepted("struct S\n    field itemId foreign NoSuchTable|NorThisOne\n");

    [Fact]
    public void A_type_nothing_declares_is_refused()
        => Assert.Contains(
            "neither a built-in type nor anything declared",
            Refusal("struct S\n    field x Nonesuch\n"));

    /// <summary>
    /// A sheet's type row writes `enum` and says which one in a cell beside it. There is no
    /// cell beside a member, so the bare word names nothing here. `foreign` on its own is
    /// refused a step earlier, by the grammar, which wants the table on the same line.
    /// </summary>
    [Fact]
    public void The_sheet_word_that_names_no_particular_enum_is_refused()
        => Assert.Contains("neither a built-in type", Refusal("struct S\n    field x enum\n"));

    /// <summary>
    /// Refused rather than resolved: a set of schema files whose types can only be worked out
    /// by opening a workbook is a set no editor can read - design section 4.4.
    /// </summary>
    [Fact]
    public void An_enum_a_sheet_declared_is_not_a_member_type()
    {
        var diagnostics = new Diagnostics();
        var model = new Model();
        var context = new CookingContext(model, new Tabbit.Recipe.RecipeModel(), diagnostics);

        model.Enums.Add(new Models.Enum
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "T" },
            RawName = "Grade",
            Name = "Grade",
            Comment = "",
        });

        var declarations = SchemaDeclarations.Read(
            [new RawSchemaFile { Name = "s.tbs", Text = "struct S\n    field g Grade\n" }],
            diagnostics);

        declarations.DeclareEnums(model, diagnostics);
        declarations.Resolve(model, context, diagnostics);

        Assert.Contains(
            "which a sheet declares",
            string.Join("\n", diagnostics.Entries.Select(entry => entry.Detail.Message)));
    }

    /// <summary>
    /// Read all the way into the declarations and refused by name, so that the notation does
    /// not have to be settled a second time when the containers are carried - design section
    /// 4.7.
    /// </summary>
    [Theory]
    [InlineData("set<int>")]
    [InlineData("map<int,Reward>")]
    public void A_container_is_read_and_then_refused_by_name(string written)
        => Assert.Contains(
            "does not carry it yet",
            Refusal($"struct Reward\n    field x int\nstruct S\n    field c {written}\n"));

    // -------------------------------------------------------------------- the graph

    [Fact]
    public void A_struct_that_holds_itself_is_refused()
        => Assert.Contains("holds itself", Refusal("struct A\n    field a A\n"));

    [Fact]
    public void A_cycle_the_long_way_round_is_refused_and_named()
        => Assert.Contains(
            "A -> B -> C -> A",
            Refusal("""
                struct A
                    field b B
                struct B
                    field c C
                struct C
                    field a A
                """));

    /// <summary>
    /// Two members of one type is not a cycle. A walk that marked a struct as visited and
    /// never unmarked it would call this one.
    /// </summary>
    [Fact]
    public void One_struct_used_twice_is_not_a_cycle()
        => Accepted("""
            struct Point
                field x float
            struct Line
                field from Point
                field to Point
            """);

    /// <summary>
    /// A relationship that loops is a reference, and a reference is not an embedding -
    /// design section 9.2.
    /// </summary>
    [Fact]
    public void A_loop_through_a_reference_is_not_a_cycle()
        => Accepted("""
            struct Node
                field parent foreign Nodes
            """);

    // ---------------------------------------------------------------------- names

    [Fact]
    public void One_name_declared_twice_is_refused()
        => Assert.Contains(
            "declared twice",
            Refusal("struct Reward\n    field x int\n", "struct Reward\n    field y int\n"));

    /// <summary>
    /// Structs and enums share one set of names, because a type cell names either and two of
    /// them called the same thing leave that cell with nothing to mean - design section 9.1.
    /// </summary>
    [Fact]
    public void A_struct_and_an_enum_may_not_share_a_name()
        => Assert.Contains(
            "declared twice",
            Refusal("struct Reward\n    field x int\nenum Reward\n    value Fire = 1\n"));

    [Fact]
    public void A_name_a_table_already_has_is_refused()
    {
        var diagnostics = new Diagnostics();
        var model = new Model();
        var context = new CookingContext(model, new Tabbit.Recipe.RecipeModel(), diagnostics);

        model.Tables.Add(new Table
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "T" },
            RawName = "Reward",
            Name = "Reward",
            Comment = "",
        });

        var declarations = SchemaDeclarations.Read(
            [new RawSchemaFile { Name = "s.tbs", Text = "struct Reward\n    field x int\n" }],
            diagnostics);

        declarations.DeclareEnums(model, diagnostics);
        declarations.Resolve(model, context, diagnostics);

        Assert.Contains(
            "a sheet already declares a table of that name",
            string.Join("\n", diagnostics.Entries.Select(entry => entry.Detail.Message)));
    }

    // ------------------------------------------------------------------ doing nothing

    /// <summary>
    /// A recipe that names no schema files reads none, and everything below this behaves as
    /// it did before they existed. Every project that predates them is that case.
    /// </summary>
    [Fact]
    public void A_run_with_no_schema_files_declares_nothing()
    {
        var read = Gather();

        Assert.True(read.Declarations.IsEmpty);
        Assert.Empty(read.Model.Enums);
        Assert.Equal(0, read.Diagnostics.Count);
    }
}
