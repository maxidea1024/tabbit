using System.Collections.Generic;
using System.Linq;
using Tabbit.Lsp;
using Tabbit.Schema;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What the editor offers to write next.
/// </summary>
/// <remarks>
/// The cursor is written as `|` in these, which reads the way it looks on screen. What decides
/// the answer is which words are already on the line, so every case here is one line and a
/// place in it.
///
/// **The offers are checked against the tables they come from, not against a list written
/// here.** A test that repeats the scalar names would be the fourth copy of them, and it would
/// pass on the day the notation grew a name and the offers did not.
/// </remarks>
public class SchemaCompletionTests
{
    private const string Declared = """
        /// One thing a row hands out.
        struct Reward
            field itemId int

        /// Something a skill does.
        abstract struct Effect
            field chance int

        struct DamageEffect extends Effect @1
            field damage int

        /// What a reward is made of.
        enum Element
            /// Burns.
            value Fire = 1
            value Ice = 2
        """;

    private static SchemaDeclarations Declarations(string source = Declared)
    {
        var diagnostics = new Diagnostics();
        var parsed = SchemaParser.Parse(source, "declared.tbs", diagnostics);

        Assert.True(diagnostics.Count == 0, string.Join(
            "\n", diagnostics.Entries.Select(entry => entry.Detail.Message)));

        return SchemaDeclarations.Gather([parsed], diagnostics);
    }

    /// <summary>Offers for a line with the cursor written into it as `|`.</summary>
    private static IReadOnlyList<LspCompletionItem> Offered(string lineWithCursor)
    {
        int cursor = lineWithCursor.IndexOf('|');
        Assert.True(cursor >= 0, "Say where the cursor is with `|`.");

        return SchemaCompletion.For(lineWithCursor.Remove(cursor, 1), cursor, Declarations());
    }

    private static IReadOnlyList<string> Labels(string lineWithCursor)
        => Offered(lineWithCursor).Select(item => item.Label).ToList();

    // ------------------------------------------------------------------ what opens a line

    [Fact]
    public void An_empty_line_offers_the_words_that_begin_one()
    {
        var offered = Labels("|");

        Assert.Equal(
            ["struct", "abstract", "field", "enum", "value"], offered);
    }

    [Fact]
    public void A_half_typed_opener_asks_the_same_question()
    {
        // The word under the cursor is what the editor filters by, not one of the words that
        // decide what the offers are.
        Assert.Equal(Labels("|"), Labels("str|"));
        Assert.Equal(Labels("    |"), Labels("    fie|"));
    }

    // ------------------------------------------------------------------ types

    [Fact]
    public void A_member_with_a_name_offers_what_it_may_be_typed_with()
    {
        var offered = Labels("    field grade |");

        // The scalars and composites come from the tables that define them, so this asks the
        // tables rather than repeating their contents.
        foreach (string name in Models.ScalarTypes.ByName.Keys)
            Assert.Contains(name, offered);

        foreach (var composite in Models.CompositeTypes.All)
            Assert.Contains(composite.Name, offered);

        Assert.Contains("set", offered);
        Assert.Contains("map", offered);
        Assert.Contains("foreign", offered);

        // And what this folder declares.
        Assert.Contains("Reward", offered);
        Assert.Contains("Element", offered);
    }

    [Fact]
    public void An_abstract_struct_is_not_offered_as_a_members_type()
    {
        // It names a set of variants rather than a shape a value may hold.
        Assert.DoesNotContain("Effect", Labels("    field what |"));
        Assert.Contains("DamageEffect", Labels("    field what |"));
    }

    [Fact]
    public void The_wire_tag_does_not_count_as_the_type()
    {
        Assert.Contains("int", Labels("    field grade @3 |"));
    }

    [Fact]
    public void Nothing_is_offered_once_the_type_is_written()
    {
        Assert.Empty(Labels("    field grade int |"));
    }

    [Fact]
    public void The_members_own_name_is_not_offered()
    {
        Assert.Empty(Labels("    field |"));
    }

    // ------------------------------------------------------------------ extends

    [Fact]
    public void After_extends_only_the_abstract_structs_are_offered()
    {
        var offered = Labels("struct Burn extends |");

        Assert.Equal(["Effect"], offered);
    }

    [Fact]
    public void A_named_struct_is_offered_the_word_that_may_follow_it()
    {
        Assert.Equal(["extends"], Labels("struct Burn |"));
    }

    // ------------------------------------------------------------------ default values

    [Fact]
    public void A_default_for_an_enum_offers_that_enums_entries()
    {
        var offered = Offered("    field grade Element = |");

        Assert.Equal(["Fire", "Ice"], offered.Select(item => item.Label));

        // The entry's own `///` travels with it.
        Assert.Contains("Burns.", offered[0].Documentation!.Value);
    }

    [Fact]
    public void A_default_for_anything_else_has_nothing_to_offer()
    {
        // There is no list of the literals an `int` may be.
        Assert.Empty(Labels("    field count int = |"));
    }

    // ------------------------------------------------------------------ metadata

    [Fact]
    public void Inside_brackets_the_keys_that_declaration_may_carry_are_offered()
    {
        var onMember = Labels("    field count int (|");
        var onStruct = Labels("struct Reward (|");
        var onEntry = Labels("    value Fire = 1 (|");

        Assert.Contains("min", onMember);
        Assert.Contains("regex", onMember);
        Assert.Contains("sep", onStruct);

        // `sep` belongs to a struct and `min` to a member, and neither is offered on the other.
        Assert.DoesNotContain("sep", onMember);
        Assert.DoesNotContain("min", onStruct);

        // Every key an entry may carry is one this build does not act on, so there is nothing
        // to offer there.
        Assert.Empty(onEntry);
    }

    [Fact]
    public void A_key_the_notation_defines_but_this_build_ignores_is_not_offered()
    {
        // Offering `uniqueBy` and then reporting it as a key that does nothing is two answers
        // to one question.
        Assert.DoesNotContain("uniqueBy", Labels("    field count int (|"));
    }

    [Fact]
    public void A_key_this_build_holds_without_reading_is_offered()
    {
        // `tag` is a label for something outside this tool, and holding it is acting on it -
        // what the rule above refuses is offering a key that goes nowhere.
        // spec/layout/tags.md section 6.
        Assert.Contains("tag", Labels("    field count int (|"));
    }

    [Fact]
    public void A_closed_bracket_is_not_inside_it_any_more()
    {
        Assert.Empty(Labels("    field count int (min=1) |"));
    }

    [Fact]
    public void A_containers_argument_carries_a_members_keys()
    {
        Assert.Contains("min", Labels("    field prices map<int,int(|"));
    }
}
