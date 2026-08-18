using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tabbit.Cooking;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// Columns typed `text`: a string that is also gathered for translation.
/// </summary>
/// <remarks>
/// Two claims, and the second is the one worth the tests. The first is that the gathering
/// works - the right strings, once each, in the right file. The second is that it is the only
/// thing the type changes: `text` and `string` reach a build as the same column, on the wire,
/// in JSON and in generated code. That is why the role is not a `ValueType` - see StringRole -
/// and it is a claim that only stays true if something checks it.
/// </remarks>
public class TextRoleTests
{
    /// <summary>
    /// One gathered file, named the way the recipe entry that wrote it named files.
    /// </summary>
    /// <remarks>
    /// The fixture runs three entries over the same strings, which is what says the format is
    /// the recipe's rather than this target's. The directory and the extension both come from
    /// the entry, so both are passed here.
    /// </remarks>
    private static string Gathered(
        string group, string into = "textset", string extension = ".textset")
    {
        return File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir("text"), into, group + extension));
    }

    private static string[] GatheredLines(
        string group, string into = "textset", string extension = ".textset")
    {
        return Gathered(group, into, extension)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
    }

    /// <summary>
    /// Three of the other shapes the fixture writes the same strings into.
    /// </summary>
    /// <remarks>
    /// The json one has no helper on purpose: it is parsed rather than matched line by line,
    /// which is the only check that says anything about a format whose whole point is being
    /// a document rather than a list of lines.
    /// </remarks>
    private static string[] CsvLines(string group) => GatheredLines(group, "csv", ".csv");
    private static string[] TsvLines(string group) => GatheredLines(group, "tsv", ".tsv");
    private static string[] XmlLines(string group) => GatheredLines(group, "xml", ".xml");

    private static void Convert()
    {
        var result = TabbitRunner.Convert("text");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");
    }


    // ------------------------------------------------------------------ gathering

    /// <summary>
    /// A column that names no group is gathered under its table's name, which is the
    /// arrangement a set of sheets already has.
    /// </summary>
    [Fact]
    public void Ungrouped_columns_gather_under_the_table_name()
    {
        Convert();

        var lines = GatheredLines("Quest");

        Assert.Contains(lines, line => line.Contains("\"Lost Cargo\""));
        Assert.Contains(lines, line => line.Contains("\"Ask at the docks.\""));
    }

    /// <summary>
    /// A named group collects across tables, which is the whole reason for naming one.
    /// </summary>
    [Fact]
    public void A_named_group_collects_across_tables()
    {
        Convert();

        string common = Gathered("Common");

        // `Quest.Category` and `Item.Category` both say `text(Common,Shared)`.
        Assert.Contains("\"Delivery\"", common);
        Assert.Contains("\"Tools\"", common);

        // And `Item.Flavour`, which names the same group in the detail-type cell instead -
        // the row where this layout puts an enum's name and a reference's target.
        Assert.Contains("\"It burns whale oil.\"", common);
    }

    /// <summary>
    /// A grouped column's strings are in its group's file and not in its table's.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is the quiet one: gathering into both would give a
    /// translator the same string in two files and no way to tell which is authoritative.
    /// </remarks>
    [Fact]
    public void A_grouped_column_leaves_its_table_file()
    {
        Convert();

        string quest = Gathered("Quest");

        Assert.DoesNotContain("\"Delivery\"", quest);
        Assert.DoesNotContain("\"Hunt\"", quest);
    }

    /// <summary>
    /// The same string twice is one entry. The list exists to be worked through by hand.
    /// </summary>
    [Fact]
    public void Repeated_values_are_gathered_once()
    {
        Convert();

        // Two rows of `Quest` carry the title `Lost Cargo`.
        Assert.Single(GatheredLines("Quest"), line => line.Contains("\"Lost Cargo\""));
    }

    /// <summary>
    /// A blank cell contributes nothing rather than an empty entry.
    /// </summary>
    [Fact]
    public void Blank_cells_are_not_gathered()
    {
        Convert();

        // Two rows leave `Hint` blank, and one leaves it blank in a row that has a title.
        Assert.DoesNotContain(GatheredLines("Quest"), line => line.EndsWith("\"\")"));
    }

    /// <summary>
    /// Every element of a `text[]` cell is gathered, not the delimited cell as one string.
    /// </summary>
    /// <remarks>
    /// The role is a property of what the column holds, and a list holds more of the same
    /// thing. Gathering the joined cell would hand a translator `Hello;Goodbye` to translate
    /// as a sentence.
    /// </remarks>
    [Fact]
    public void Every_element_of_a_list_cell_is_gathered()
    {
        Convert();

        var lines = GatheredLines("Quest");

        Assert.Contains(lines, line => line.Contains("\"Hello\""));
        Assert.Contains(lines, line => line.Contains("\"Goodbye\""));
        Assert.Contains(lines, line => line.Contains("\"Farewell\""));
        Assert.DoesNotContain(lines, line => line.Contains("Hello;Goodbye"));
    }

    /// <summary>
    /// A `string` column is not gathered, however much it looks like one that should be.
    /// </summary>
    /// <remarks>
    /// The distinction the type exists to make. `Quest.ScriptId` holds prose-shaped values
    /// and is an identifier, and gathering it would put a key in front of a translator.
    /// </remarks>
    [Fact]
    public void Plain_string_columns_are_not_gathered()
    {
        Convert();

        Assert.DoesNotContain("quest_lost_cargo", Gathered("Quest"));
    }


    // ------------------------------------------------------------------ the format is the recipe's

    /// <summary>
    /// The line pattern in the recipe is what a file's lines look like.
    /// </summary>
    /// <remarks>
    /// The target ships no format. The fixture writes the same strings five ways, and none of
    /// them is more native to this target than the others.
    /// </remarks>
    [Fact]
    public void The_recipe_decides_what_a_line_looks_like()
    {
        Convert();

        Assert.Contains(
            GatheredLines("Quest"),
            line => line == "NSLOCTEXT(\"Game\", \"Lost Cargo\", \"Lost Cargo\")");

        Assert.Contains(CsvLines("Quest"), line => line == "1,\"Lost Cargo\",Quest,Title");
        Assert.Contains(TsvLines("Quest"), line => line == "Lost Cargo\tQuest\tTitle");

        Assert.Contains(
            XmlLines("Quest"),
            line => line == "  <text from=\"Quest.Title\">Lost Cargo</text>");
    }

    /// <summary>
    /// A header and a footer are written once for the file, and take what describes it.
    /// </summary>
    [Fact]
    public void A_header_and_a_footer_frame_the_entries()
    {
        Convert();

        var lines = CsvLines("Common");

        Assert.Equal("index,text,table,column", lines[0]);
        Assert.Equal("# Shared.Common: 5", lines[^1]);
    }

    /// <summary>
    /// A separator is written between the entries and not after the last, which is what a
    /// bracketed format needs to come out valid.
    /// </summary>
    /// <remarks>
    /// The one thing a per-line pattern cannot do for itself: it has no way to see which entry
    /// is last, and a comma after the last one is precisely what makes a JSON document
    /// invalid. Parsing the result is the check - a shape assertion would pass on a document
    /// no reader accepts.
    /// </remarks>
    [Fact]
    public void A_separator_goes_between_the_entries_and_not_after_the_last()
    {
        Convert();

        foreach (string group in new[] { "Quest", "Item", "Common" })
        {
            string json = Gathered(group, "json", ".json");

            var parsed = JsonDocument.Parse(json).RootElement;

            Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
            Assert.NotEmpty(parsed.EnumerateArray());
        }
    }

    /// <summary>
    /// The escaping comes from the extension, so one string reaches five files five ways.
    /// </summary>
    /// <remarks>
    /// No entry of the fixture says how to escape anything. `The "Blue" Whale` is the row that
    /// makes the difference visible, and every one of these would be a malformed file if the
    /// escaping were shared.
    /// </remarks>
    [Fact]
    public void The_extension_decides_the_escaping()
    {
        Convert();

        // `.textset` is not a format this tool knows, so it takes the backslash form.
        Assert.Contains(
            GatheredLines("Quest"),
            line => line == "NSLOCTEXT(\"Game\", \"The \\\"Blue\\\" Whale\", \"The \\\"Blue\\\" Whale\")");

        // A quoted csv field doubles its quotes.
        Assert.Contains(CsvLines("Quest"), line => line.Contains("\"The \"\"Blue\"\" Whale\""));

        // A tsv quotes nothing, so a quotation mark is just a character.
        Assert.Contains(TsvLines("Quest"), line => line.StartsWith("The \"Blue\" Whale\t"));

        // XML wants entities, where a backslash would mean nothing.
        Assert.Contains(XmlLines("Quest"), line => line.Contains("The &quot;Blue&quot; Whale"));

        // And JSON is parsed rather than matched, which is the only check that means anything.
        Assert.Contains(
            JsonDocument.Parse(Gathered("Quest", "json", ".json")).RootElement.EnumerateArray(),
            element => element.GetProperty("text").GetString() == "The \"Blue\" Whale");
    }

    /// <summary>
    /// A value holding braces is written through, not substituted into.
    /// </summary>
    /// <remarks>
    /// The failure a series of string replacements would have: `{0}` is what a sentence with a
    /// number in it looks like, and real display strings are full of them. A pattern parsed
    /// once cannot reach the values it filled in.
    /// </remarks>
    [Fact]
    public void Braces_in_a_value_are_left_alone()
    {
        Convert();

        Assert.Contains(
            GatheredLines("Quest"),
            line => line == "NSLOCTEXT(\"Game\", \"{0} Enthusiast\", \"{0} Enthusiast\")");
    }

    /// <summary>
    /// A namespace is the group's, so one file's strings are all in one.
    /// </summary>
    /// <remarks>
    /// A namespace holds groups rather than sitting inside one, which is why it is not per
    /// string: `Common` gathers from three columns and only two of them name a namespace, and
    /// the file is one namespace all the same. Per string it would be a file whose entries
    /// belong to two, and no pipeline that reads these files can say that.
    /// </remarks>
    [Fact]
    public void A_namespace_belongs_to_the_group()
    {
        Convert();

        // `Quest.Category` and `Item.Category` say `text(Common,Shared)`; `Item.Flavour`
        // gathers into the same group and names no namespace, so it is in `Shared` too.
        Assert.All(
            GatheredLines("Common"),
            line => Assert.StartsWith("NSLOCTEXT(\"Shared\", ", line));

        Assert.Contains(
            GatheredLines("Common"),
            line => line.Contains("\"Coarse but strong.\""));

        // Every other group took the recipe entry's setting, since no column spoke for them.
        Assert.All(
            GatheredLines("Quest"),
            line => Assert.StartsWith("NSLOCTEXT(\"Game\", ", line));
    }

    /// <summary>
    /// The namespace describes the file, so a header may name it.
    /// </summary>
    [Fact]
    public void A_header_may_name_the_namespace()
    {
        Convert();

        Assert.Equal(
            "<textset namespace=\"Shared\" group=\"Common\" count=\"5\">",
            XmlLines("Common")[1]);
    }

    /// <summary>
    /// A Scriban template is used in place of the line pattern.
    /// </summary>
    /// <remarks>
    /// For the shapes a line cannot say. The one that ships is the fixture's own pattern
    /// written out, so the two trees agreeing byte for byte is what says the two routes reach
    /// the same model.
    /// </remarks>
    [Fact]
    public void A_template_may_replace_the_line_pattern()
    {
        Convert();

        foreach (string group in new[] { "Quest", "Item", "Common" })
            Assert.Equal(Gathered(group), Gathered(group, "templated"));
    }


    // ------------------------------------------------------------------ and nothing else changed

    /// <summary>
    /// A `text` column reaches JSON as the string it is.
    /// </summary>
    /// <remarks>
    /// The claim the whole design rests on. If a role ever became a `ValueType`, this is
    /// where the first of thirteen generators would start disagreeing about what to emit.
    /// </remarks>
    [Fact]
    public void Text_columns_export_as_ordinary_strings()
    {
        Convert();

        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir("text"), "json-named", "Quest.json"));

        var rows = JsonDocument.Parse(json).RootElement;

        Assert.Equal("Lost Cargo", rows[0].GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.String, rows[0].GetProperty("category").ValueKind);

        // The optional one, which is absent rather than empty - the same as any other
        // optional column, and unaffected by the role.
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("hint").ValueKind);
    }

    /// <summary>
    /// The generated accessor for a `text` column is the one a `string` column gets.
    /// </summary>
    [Fact]
    public void Text_columns_generate_a_string_member()
    {
        Convert();

        string source = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("text"), "csharp", "tables", "QuestTable.cs"));

        Assert.Contains("public string Title =>", source);
        Assert.Contains("public string ScriptId =>", source);
    }


    // ------------------------------------------------------------------ the notation

    /// <summary>
    /// `text` is a type a sheet may declare, with or without a group.
    /// </summary>
    [Fact]
    public void Text_is_a_declarable_type()
    {
        var context = Context();

        Assert.True(context.IsValidTypeName("text"));
        Assert.True(context.IsValidTypeName("text(Achievement)"));
        Assert.True(context.IsValidTypeName("text(Achievement,Quests)"));
        Assert.True(context.IsValidTypeName("text[]"));
        Assert.True(context.IsValidTypeName("text(Achievement)[]"));
        Assert.True(context.IsValidTypeName("text(Achievement)[]?"));
    }

    /// <summary>
    /// A group on a type that is not gathered is refused rather than ignored.
    /// </summary>
    /// <remarks>
    /// Reading `int(Foo)` as `int` would accept a sheet that says something this tool does
    /// not do, and say nothing about it.
    /// </remarks>
    [Fact]
    public void A_group_on_an_ungathered_type_is_refused()
    {
        var context = Context();

        Assert.False(context.IsValidTypeName("int(Foo)"));
        Assert.False(context.IsValidTypeName("string(Foo)"));

        var failure = Assert.Throws<TabbitException>(
            () => context.SplitStringRole("int(Foo)", Somewhere(), out _, out _, out _));

        Assert.Contains("`text` takes a group and `asset` takes a kind", failure.Message);
    }

    /// <summary>
    /// Brackets opened and left empty are a typo, not a way of asking for the default.
    /// </summary>
    [Fact]
    public void An_empty_group_is_refused()
    {
        var failure = Assert.Throws<TabbitException>(
            () => Context().SplitStringRole("text()", Somewhere(), out _, out _, out _));

        // In the role's own words: `text` puts a group in those brackets, and the message for
        // `asset()` says `kind` instead.
        Assert.Contains("opens brackets and names no group", failure.Message);
    }

    /// <summary>
    /// A comma with nothing after it is a typo too.
    /// </summary>
    [Fact]
    public void An_empty_namespace_is_refused()
    {
        var failure = Assert.Throws<TabbitException>(
            () => Context().SplitStringRole("text(Achievement,)", Somewhere(), out _, out _, out _));

        Assert.Contains("names no namespace", failure.Message);
    }

    /// <summary>
    /// The role comes off the name, so what follows resolves an ordinary `string`.
    /// </summary>
    [Fact]
    public void The_role_leaves_the_type_a_string()
    {
        var context = Context();

        Assert.Equal("string", context.SplitStringRole(
            "text(Achievement,Quests)", Somewhere(), out var role,
            out string group, out string space));

        Assert.Equal(StringRole.Text, role);
        Assert.Equal("Achievement", group);
        Assert.Equal("Quests", space);

        Assert.Equal(ValueType.String, context.ParseValueType("text", Somewhere()));
        Assert.Equal(ValueType.StringArray, context.ParseValueType("text[]", Somewhere()));
    }

    /// <summary>
    /// The group and the namespace keep the case the author wrote. One names a file.
    /// </summary>
    [Fact]
    public void The_group_keeps_its_case()
    {
        Context().SplitStringRole(
            "text(AchievementPoint,GameText)", Somewhere(), out _,
            out string group, out string space);

        Assert.Equal("AchievementPoint", group);
        Assert.Equal("GameText", space);
    }

    private static CookingContext Context()
        => new CookingContext(new Model(), new Tabbit.Recipe.RecipeModel(), new Diagnostics());

    private static Location Somewhere()
        => new Location { Filename = "test", Sheet = "Sheet1", Column = 0, Row = 0 };
}
