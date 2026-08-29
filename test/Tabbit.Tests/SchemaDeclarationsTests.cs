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

    // ---------------------------------------------------------------- the containers

    /// <summary>
    /// The shapes a container may be written in - spec/types/set-and-map.md sections 2 and 6.
    /// A struct is a value and not a key: its columns are several, and uniqueness across
    /// several columns is a different question from what a key is.
    /// </summary>
    [Theory]
    [InlineData("set<int>")]
    [InlineData("set<string>")]
    [InlineData("set<Element>")]
    [InlineData("map<int,int>")]
    [InlineData("map<string,Reward>")]
    [InlineData("map<Element,int>")]
    [InlineData("map<int(min=1),int(max=99)>(size=1..3)")]
    [InlineData("map<int,int>?")]
    public void A_container_a_column_can_hold_is_accepted(string written)
        => Accepted(Around(written));

    /// <summary>
    /// A key has to be a type whose equality is in the value itself - section 6.1. Floating
    /// point is spelling-dependent, `datetime` reaches a value through a timezone reading,
    /// and a `bitset` is a list of flag names the same set can be written several orders of.
    /// </summary>
    [Theory]
    [InlineData("map<float,int>")]
    [InlineData("map<double,int>")]
    [InlineData("map<datetime,int>")]
    [InlineData("map<timespan,int>")]
    [InlineData("map<bitset,int>")]
    [InlineData("map<Reward,int>")]
    public void A_key_whose_equality_is_not_in_the_value_is_refused(string written)
        => Assert.Contains("equality is in the value itself", Refusal(Around(written)));

    [Theory]
    [InlineData("set<int,int>")]
    [InlineData("map<int>")]
    public void A_container_given_the_wrong_number_of_arguments_is_refused(string written)
        => Assert.Contains("type argument", Refusal(Around(written)));

    /// <summary>
    /// Section 10: the file could hold each of these and the cell notation could not, so the
    /// first release names them rather than half-reading them.
    /// </summary>
    [Theory]
    [InlineData("map<int,set<int>>", "container")]
    [InlineData("map<int,foreign Reward>", "reference")]
    [InlineData("map<int,int[]>", "array")]
    [InlineData("map<int,int?>", "optional")]
    public void An_argument_shape_the_first_release_leaves_out_is_named(
        string written, string what)
        => Assert.Contains(what, Refusal(Around(written)));

    [Theory]
    [InlineData("set<int>[]")]
    [InlineData("map<int,int>[]")]
    public void An_array_of_containers_is_refused(string written)
        => Assert.Contains(
            "array whose elements are containers", Refusal(Around(written)));

    /// <summary>
    /// Section 2.2. `map` has two element positions and `min` does not say which one it is
    /// for, so the constraint goes on the argument rather than on the member.
    /// </summary>
    [Fact]
    public void An_element_constraint_written_outside_a_container_is_refused()
        => Assert.Contains(
            "go on the argument they are about",
            Refusal(Around("map<int,int>(min=1)")));

    [Fact]
    public void A_name_that_is_not_a_container_taking_arguments_is_refused()
        => Assert.Contains("takes no type arguments", Refusal(Around("list<int>")));

    /// <summary>One member of the given type, with a struct and an enum to point at.</summary>
    private static string Around(string written)
        => "enum Element\n    value Fire = 1\n"
           + "struct Reward\n    field x int\n"
           + $"struct S\n    field c {written}\n";

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

    /// <summary>
    /// An enum a sheet has already declared is refused, and the declaration's own is not.
    /// </summary>
    /// <remarks>
    /// The backstop under the layout's own check. A layout that adds an enum without asking
    /// whether the name is taken would otherwise leave the pair unreported - which is what
    /// the primary layout did. The second half matters as much: every enum these files
    /// declared is in the same list by the time this runs, so a check that did not tell them
    /// apart would report each declaration against itself.
    /// </remarks>
    [Fact]
    public void An_enum_name_a_sheet_already_has_is_refused()
    {
        var diagnostics = new Diagnostics();
        var model = new Model();
        var context = new CookingContext(model, new Tabbit.Recipe.RecipeModel(), diagnostics);

        model.Enums.Add(new Tabbit.Models.Enum
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "E" },
            RawName = "Grade",
            Name = "Grade",
            Comment = "",
        });

        var declarations = SchemaDeclarations.Read(
            [new RawSchemaFile
            {
                Name = "s.tbs",
                Text = "enum Grade\n    Common\nenum Rank\n    First\n",
            }],
            diagnostics);

        declarations.DeclareEnums(model, diagnostics);
        declarations.Resolve(model, context, diagnostics);

        string reported = string.Join(
            System.Environment.NewLine,
            diagnostics.Entries.Select(entry => entry.Detail.Message));

        Assert.Contains("a sheet already declares an enum of that name", reported);

        // `Rank` is only in these files. A check that did not skip what it put in the model
        // would report it against itself.
        Assert.DoesNotContain("Rank", reported);
    }

    // ------------------------------------------------------------------ variant sets

    /// <summary>
    /// Across files and in either order, the same way every other name resolves here -
    /// spec/types/polymorphism.md section 3.
    /// </summary>
    [Fact]
    public void Variants_join_their_base_whichever_file_declared_it()
    {
        var read = Gather(
            "struct HealEffect extends Effect @2\n    field amount int\n",
            "abstract struct Effect\n    field chance int\n",
            "struct DamageEffect extends Effect @1\n    field damage int\n");

        Assert.Equal(0, read.Diagnostics.Count);

        var variants = read.Declarations.VariantsOf("Effect");
        Assert.Equal(["HealEffect", "DamageEffect"], variants.Select(v => v.Name));
        Assert.Equal([2, 1], variants.Select(read.Declarations.DiscriminatorOf));
    }

    /// <summary>
    /// A set that numbers nothing is numbered by declaration order, which is the rule members
    /// already follow.
    /// </summary>
    [Fact]
    public void An_unnumbered_set_takes_its_numbers_from_the_order_declared()
    {
        var read = Gather("""
            abstract struct Effect
            struct DamageEffect extends Effect
            struct HealEffect extends Effect
            """);

        Assert.Equal(0, read.Diagnostics.Count);
        Assert.Equal(
            [1, 2],
            read.Declarations.VariantsOf("Effect").Select(read.Declarations.DiscriminatorOf));
    }

    [Fact]
    public void An_abstract_struct_with_no_members_is_a_set_and_nothing_else()
        => Accepted("abstract struct Reward\nstruct ItemReward extends Reward\n    field id int\n");

    [Fact]
    public void Extending_a_name_nothing_declares_is_refused()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct DamageEffect extends Effect
            struct HealEffect extends Efect
            """);

        Assert.Contains("nothing here declares `Efect`", reported);
        Assert.Contains("Effect", reported);
    }

    [Fact]
    public void Extending_a_plain_struct_is_refused()
    {
        string reported = Refusal("""
            struct Effect
                field chance int
            struct DamageEffect extends Effect
                field damage int
            """);

        Assert.Contains("a plain `struct`", reported);
    }

    [Fact]
    public void Extending_an_enum_is_refused()
    {
        string reported = Refusal("""
            enum Effect
                value Fire = 1
            struct DamageEffect extends Effect
                field damage int
            """);

        Assert.Contains("an enum", reported);
    }

    /// <summary>
    /// A set is the name of what fills it, and nothing fills this one. Reported here rather
    /// than in the parser because a variant may be a table, and the tables are not in yet
    /// when one file is read.
    /// </summary>
    [Fact]
    public void An_abstract_struct_nothing_extends_is_refused()
        => Assert.Contains("nothing extends it", Refusal("abstract struct Reward"));

    [Fact]
    public void Two_variants_claiming_one_discriminator_are_refused()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct DamageEffect extends Effect @1
            struct HealEffect extends Effect @1
            """);

        Assert.Contains("both extend `Effect` under `@1`", reported);
    }

    /// <summary>
    /// The number a dropped variant holds is not handed to a new one.
    /// </summary>
    /// <remarks>
    /// **The whole reason the notation exists.** A reader built while the dropped variant was
    /// there still reads that number as that shape, and rows written then still carry it - so
    /// a new variant given the number would be read as the old one, with no error anywhere.
    /// spec/types/polymorphism.md section 5.1.1.
    /// </remarks>
    [Fact]
    public void A_discriminator_a_tombstone_holds_is_refused()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct HealEffect extends Effect @2 (removed)
            struct ShieldEffect extends Effect @2
            """);

        Assert.Contains("holds that number as a tombstone", reported);
    }

    /// <summary>
    /// And the tombstone written after the variant that reaches for its number, which is the
    /// order a declaration file is more likely to be in.
    /// </summary>
    [Fact]
    public void A_tombstone_reserves_its_number_whichever_order_it_is_written_in()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct ShieldEffect extends Effect @2
            struct HealEffect extends Effect @2 (removed)
            """);

        Assert.Contains("holds that number as a tombstone", reported);
    }

    /// <summary>A tombstone is not a variant: nothing may hold it and nothing is generated.</summary>
    [Fact]
    public void A_tombstone_is_not_a_member_of_its_set()
    {
        var gathered = Gather("""
            abstract struct Effect
            struct DamageEffect extends Effect @1
            struct HealEffect extends Effect @2 (removed)
            """);

        var live = Assert.Single(gathered.Declarations.VariantsOf("Effect"));
        Assert.Equal("DamageEffect", live.Name);

        // Not a type either - a column naming it finds nothing.
        Assert.Null(gathered.Declarations.FindStruct("HealEffect"));

        var gone = Assert.Single(gathered.Declarations.RemovedVariants);
        Assert.Equal("HealEffect", gone.Name);
    }

    /// <summary>
    /// A set whose only numbers are on tombstones still numbers its live variants.
    /// </summary>
    /// <remarks>
    /// The all-or-none rule reads the tombstones too. Left out, a set with one dropped variant
    /// and one unnumbered live one would let the live one take 1 from its position - which is
    /// the number the dropped one may have been holding. Section 5.1.1.
    /// </remarks>
    [Fact]
    public void A_tombstone_makes_the_set_a_numbered_one()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct DamageEffect extends Effect
            struct HealEffect extends Effect @2 (removed)
            """);

        Assert.Contains("Number all of them or none", reported);
    }

    [Fact]
    public void A_set_numbering_some_variants_and_not_others_is_refused()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct DamageEffect extends Effect @1
            struct HealEffect extends Effect
            """);

        Assert.Contains("Number all of them or none", reported);
    }

    /// <summary>
    /// And the same set written the other way round, which is the order that used to pass.
    /// </summary>
    /// <remarks>
    /// The check read the first variant to decide whether the set was numbered, so a set whose
    /// first variant carries no number was taken for an unnumbered set - and the numbers on the
    /// rest went unexamined. That is the order that collides: `DamageEffect` takes 1 from its
    /// position and `HealEffect` takes 1 from its `@1`, and both are 1.
    /// spec/types/polymorphism.md section 5.1.1.
    /// </remarks>
    [Fact]
    public void A_set_whose_first_variant_carries_no_number_is_refused_too()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct DamageEffect extends Effect
            struct HealEffect extends Effect @1
            """);

        Assert.Contains("Number all of them or none", reported);
    }

    /// <summary>
    /// Value embedding is stage 4 of the spec. The notation for the declaration is settled, so
    /// the refusal names what is missing and where the reference path reaches the same
    /// variants.
    /// </summary>
    [Fact]
    public void An_abstract_struct_written_as_a_member_type_is_refused()
    {
        string reported = Refusal("""
            abstract struct Effect
            struct DamageEffect extends Effect
                field damage int
            struct Skill
                field effect Effect
            """);

        Assert.Contains("not supported yet", reported);
        Assert.Contains("foreign Effect", reported);
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
