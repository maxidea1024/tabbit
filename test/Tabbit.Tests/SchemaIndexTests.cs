using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Lsp;
using Tabbit.Schema;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Pointing at a name: where it was declared, and what it says.
/// </summary>
/// <remarks>
/// Text in and answers out, with no server and no protocol - the index is where "go to
/// definition" is actually decided, and everything above it only carries the answer.
///
/// The names here are looked up the way a file does, through the declarations table. The case
/// of a written name is the part worth a test of its own: that table is matched without case
/// after Pascal-casing, so a lookup done any other way disagrees with the checking.
/// </remarks>
public class SchemaIndexTests
{
    private static SchemaIndex Build(params (string Path, string Text)[] files)
    {
        var diagnostics = new Diagnostics();

        var parsed = files
            .Select(file => SchemaParser.Parse(file.Text, file.Path, diagnostics))
            .ToList();

        Assert.True(diagnostics.Count == 0, string.Join(
            "\n", diagnostics.Entries.Select(entry => entry.Detail.Message)));

        return SchemaIndex.Build(parsed, SchemaDeclarations.Gather(parsed, diagnostics));
    }

    /// <summary>Where a word sits in a file, counted the way the protocol counts.</summary>
    private static (int Line, int Character) Where(string text, string word, int which = 1)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int seen = 0;

        for (int line = 0; line < lines.Length; line++)
        {
            for (int at = lines[line].IndexOf(word, StringComparison.Ordinal); at >= 0;
                 at = lines[line].IndexOf(word, at + 1, StringComparison.Ordinal))
            {
                if (++seen == which)
                    return (line, at);
            }
        }

        throw new InvalidOperationException($"`{word}` is not written in this file.");
    }

    private static Occurrence At(SchemaIndex index, string path, string text, string word,
        int which = 1)
    {
        var (line, character) = Where(text, word, which);
        var found = index.At(path, line, character);

        Assert.True(found is not null, $"Nothing is written at `{word}`.");
        return found!;
    }

    private const string Rewards = """
        /// One thing a row hands out.
        struct Reward
            /// Which item, as that table's key.
            field itemId int
            /// What it is made of.
            field grade Element = Fire
            field where vec3f
            field owner foreign Player
        """;

    private const string Elements = """
        /// What a reward is made of.
        ///
        /// The two a row may say.
        enum Element
            /// Burns.
            value Fire = 1
            value Ice = 2
        """;

    // ------------------------------------------------------------------ go to definition

    [Fact]
    public void A_type_named_in_one_file_leads_to_the_file_that_declared_it()
    {
        var index = Build(("rewards.tbs", Rewards), ("elements.tbs", Elements));

        var declared = index.DefinitionOf(At(index, "rewards.tbs", Rewards, "Element"));

        Assert.NotNull(declared);
        Assert.Equal("elements.tbs", declared!.Value.Path);

        // The name itself, not the whole line: `Element` on the fourth line of that file.
        Assert.Equal(3, declared.Value.Range.Start.Line);
        Assert.Equal(5, declared.Value.Range.Start.Character);
        Assert.Equal(12, declared.Value.Range.End.Character);
    }

    [Fact]
    public void The_name_after_extends_leads_to_the_set_it_joins()
    {
        const string effects = """
            /// Something a skill does.
            abstract struct Effect
                field chance int
            struct DamageEffect extends Effect @1
                field damage int
            """;

        var index = Build(("effects.tbs", effects));

        var declared = index.DefinitionOf(At(index, "effects.tbs", effects, "Effect", which: 3));

        Assert.NotNull(declared);
        Assert.Equal(1, declared!.Value.Range.Start.Line);
    }

    [Fact]
    public void A_type_inside_a_container_leads_to_its_declaration()
    {
        const string bag = """
            struct Bag
                field drops map<int,Reward>
            struct Reward
                field itemId int
            """;

        var index = Build(("bag.tbs", bag));

        var declared = index.DefinitionOf(At(index, "bag.tbs", bag, "Reward"));

        Assert.NotNull(declared);
        Assert.Equal(2, declared!.Value.Range.Start.Line);
    }

    [Fact]
    public void A_name_written_in_another_case_still_leads_somewhere()
    {
        const string using_ = """
            struct Reward
                field grade element
            """;

        var index = Build(("using.tbs", using_), ("elements.tbs", Elements));

        // The declarations table matches without case, so the index has to as well - looking
        // a name up any other way answers "no such type" for a file that checks clean.
        var declared = index.DefinitionOf(At(index, "using.tbs", using_, "element"));

        Assert.NotNull(declared);
        Assert.Equal("elements.tbs", declared!.Value.Path);
    }

    [Fact]
    public void A_declaration_leads_to_itself()
    {
        var index = Build(("rewards.tbs", Rewards));

        var declared = index.DefinitionOf(At(index, "rewards.tbs", Rewards, "itemId"));

        Assert.NotNull(declared);
        Assert.Equal("rewards.tbs", declared!.Value.Path);
        Assert.Equal(3, declared.Value.Range.Start.Line);
    }

    [Fact]
    public void Nothing_is_written_between_the_words()
    {
        var index = Build(("rewards.tbs", Rewards));

        Assert.Null(index.At("rewards.tbs", 1, 0));          // the `struct` keyword
        Assert.Null(index.At("rewards.tbs", 0, 3));          // inside the `///` line
        Assert.Null(index.At("nowhere.tbs", 1, 7));          // a file the index never read
    }

    // ------------------------------------------------------------------ hover

    [Fact]
    public void Hovering_a_declaration_shows_it_and_its_comment()
    {
        var index = Build(("elements.tbs", Elements));

        var said = index.HoverOf(At(index, "elements.tbs", Elements, "Element"));

        Assert.NotNull(said);
        Assert.Contains("```tbs\nenum Element\n```", said);

        // The whole `///` block, blank line and all, not just its first line.
        Assert.Contains("What a reward is made of.", said);
        Assert.Contains("The two a row may say.", said);
    }

    [Fact]
    public void Hovering_a_type_shows_what_that_type_declared()
    {
        var index = Build(("rewards.tbs", Rewards), ("elements.tbs", Elements));

        var said = index.HoverOf(At(index, "rewards.tbs", Rewards, "Element"));

        Assert.NotNull(said);
        Assert.Contains("enum Element", said);
        Assert.Contains("What a reward is made of.", said);
    }

    [Fact]
    public void Hovering_a_member_shows_the_line_that_declared_it()
    {
        var index = Build(("rewards.tbs", Rewards), ("elements.tbs", Elements));

        var said = index.HoverOf(At(index, "rewards.tbs", Rewards, "grade"));

        Assert.NotNull(said);
        Assert.Contains("field grade Element = Fire", said);
        Assert.Contains("What it is made of.", said);
    }

    [Fact]
    public void Hovering_a_variant_shows_what_it_extends_and_the_number_it_travels_under()
    {
        const string effects = """
            abstract struct Effect
                field chance int
            struct DamageEffect extends Effect @1
                field damage int
            """;

        var index = Build(("effects.tbs", effects));

        var said = index.HoverOf(At(index, "effects.tbs", effects, "DamageEffect"));

        Assert.Contains("struct DamageEffect extends Effect @1", said);
    }

    [Fact]
    public void Hovering_a_composite_type_shows_what_a_cell_of_it_holds()
    {
        var index = Build(("rewards.tbs", Rewards), ("elements.tbs", Elements));

        var said = index.HoverOf(At(index, "rewards.tbs", Rewards, "vec3f"));

        // Taken from the composite table rather than from a list kept here, which is why
        // this one answers and a scalar does not.
        Assert.NotNull(said);
        Assert.Contains("vec3f", said);
        Assert.Contains("X · Y · Z", said);
    }

    [Fact]
    public void A_scalar_and_a_foreign_table_are_left_alone()
    {
        var index = Build(("rewards.tbs", Rewards), ("elements.tbs", Elements));

        // `int` needs no explaining, and keeping the list of built-in names here would be a
        // third copy of it.
        Assert.Null(index.HoverOf(At(index, "rewards.tbs", Rewards, "int")));

        // Which table `Player` is cannot be answered without a workbook, and this server
        // never opens one - section 3 of spec/ops/lsp.md.
        Assert.Null(index.At("rewards.tbs", Where(Rewards, "Player").Line,
                             Where(Rewards, "Player").Character));
    }
}
