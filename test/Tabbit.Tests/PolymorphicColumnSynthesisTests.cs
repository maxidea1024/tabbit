using System.Collections.Generic;
using System.Linq;
using Tabbit;
using Tabbit.Cooking;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Tabbit.Sources;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A polymorphic group whose sheet left out the column of a variant member no row uses.
/// </summary>
/// <remarks>
/// The abstract type is one type across every table that names it, and the generated code
/// builds each row's variant from that table's flat entry by reading every declared member.
/// So a table with fewer columns than the declaration has members used to be a table whose
/// generated code did not compile - or, met first, a table that took members out of the
/// shared type. The cooking now adds the missing columns blank, and these are the shapes that
/// has to hold and the two it must still refuse. The end-to-end reading through every
/// language is `PolymorphicRecordTests`; this is the model. spec/types/polymorphism.md
/// section 5.2.
/// </remarks>
public class PolymorphicColumnSynthesisTests
{
    private const string Schema = """
        enum Band
            value None = 0
            value Rare = 1
            value Common = 2

        abstract struct Effect
            field chance int

        struct DamageEffect extends Effect @1
            field damage int
            field elementId foreign Element

        struct HealEffect extends Effect @2
            field amount int
            field band Band
        """;

    private static RawSheet Sheet(string name, params string[][] rows)
    {
        var sheet = new RawSheet
        {
            Layout = new SheetLayout("tabbit", DuplicateIndexPolicy.Error),
            Location = new Location { Filename = "memory.xlsx", Sheet = name, Column = 0, Row = 0 },
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
                        Sheet = name,
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

    private static RawSheet ElementSheet() => Sheet("Element",
        [":table Element", "what a damaging effect is made of"],
        [":field", "code", "Name"],
        [":type", "int", "string"],
        ["", "1", "Fire"],
        ["", "2", "Ice"]);

    /// <summary>Every column of the union, filled the way the fixture fills it.</summary>
    private static RawSheet SkillSheet() => Sheet("Skill",
        [":table Skill", "every column"],
        [":field", "index", "Name", "Effect.$type", "Effect.Chance", "Effect.Damage",
            "Effect.ElementId", "Effect.Amount", "Effect.Band"],
        [":type", "int", "string", "Effect", "", "", "", "", ""],
        ["", "1", "Slash", "DamageEffect", "30", "50", "1", "", ""],
        ["", "2", "Mend", "HealEffect", "100", "", "", "20", "Common"]);

    /// <summary>No `Band` and no `ElementId`, and no row of the variant that has the reference.</summary>
    private static RawSheet BoonSheet(string tags = "") => Sheet("Boon",
        [":table Boon", "some columns"],
        [":field", "index" + Tag(tags, 1), "Name" + Tag(tags, 2), "Effect.$type" + Tag(tags, 3),
            "Effect.Chance" + Tag(tags, 4), "Effect.Damage" + Tag(tags, 5),
            "Effect.Amount" + Tag(tags, 6)],
        [":type", "int", "string", "Effect", "", "", ""],
        ["", "1", "Bless", "HealEffect", "80", "", "15"],
        ["", "2", "Renew", "HealEffect", "40", "", "5"]);

    private static string Tag(string tags, int number)
        => tags.Length == 0 ? "" : $"@{number}";

    private static Model Cook(params RawSheet[] sheets)
    {
        var raw = new RawModel();

        raw.SchemaFiles.Add(new RawSchemaFile { Name = "effect.tbs", Text = Schema });

        foreach (var sheet in sheets)
            raw.Sheets.Add(sheet);

        return new ModelCooker().Cook(new Options(), new RecipeModel(), raw);
    }

    private static string Reported(params RawSheet[] sheets)
    {
        var problem = Assert.Throws<TabbitException>(() => Cook(sheets));

        return string.Join(
            System.Environment.NewLine,
            new[] { problem.Message }.Concat(problem.Details.Select(d => d.Message)));
    }

    private static Table TableNamed(Model model, string name)
        => model.Tables.Single(table => table.Name == name);

    [Fact]
    public void A_member_column_the_sheet_left_out_is_added_blank_at_the_end_of_the_group()
    {
        var model = Cook(ElementSheet(), BoonSheet());
        var boon = TableNamed(model, "Boon");

        string[] names = boon.Fields.Select(field => field.Name).ToArray();

        // After every column the sheet wrote, in the order the variants declare them.
        Assert.Equal(
            ["Index", "Name", "EffectType", "EffectChance", "EffectDamage", "EffectAmount",
             "EffectElementId", "EffectBand"],
            names);

        var band = boon.Fields.Single(field => field.Name == "EffectBand");

        Assert.True(band.Synthesized);
        Assert.False(band.IsRequired);
        Assert.Equal(ValueType.Enum, band.Type);
        Assert.Equal("Band", band.TypeName);
        Assert.Equal(["HealEffect"], band.VariantsDeclaringThis);

        // Blank in every row: a cell that is there and holds nothing, as a written blank does.
        Assert.All(boon.Data, row => Assert.False(row[band.Index].HasValue));

        // And a wire column like any other, with the ordinal tag its position gives it.
        int[] tags = boon.WireColumns.Select(column => column.TagCarrier.WireTag!.Value).ToArray();
        Assert.Equal(Enumerable.Range(1, tags.Length).ToArray(), tags);
    }

    [Fact]
    public void The_shared_type_has_every_declared_member_whichever_table_comes_first()
    {
        string[] Members(Model model) => model.PolymorphicTypes.Single().Variants
            .Single(variant => variant.Name == "HealEffect").Members
            .Select(field => field.Name)
            .ToArray();

        var fullFirst = Cook(ElementSheet(), SkillSheet(), BoonSheet());
        var subsetFirst = Cook(ElementSheet(), BoonSheet(), SkillSheet());

        Assert.Equal(["EffectAmount", "EffectBand"], Members(fullFirst));
        Assert.Equal(Members(fullFirst), Members(subsetFirst));

        // The two tables' groups have the same member columns, which is what the generators
        // rely on when they read the shared type's members off either table's entry.
        static string[] GroupColumns(Model model, string table)
            => TableNamed(model, table).Fields
                .Where(field => field.GroupName == "Effect")
                .Select(field => field.Name)
                .OrderBy(name => name)
                .ToArray();

        Assert.Equal(GroupColumns(subsetFirst, "Skill"), GroupColumns(subsetFirst, "Boon"));
    }

    [Fact]
    public void A_base_field_with_no_column_is_refused()
    {
        var sheet = Sheet("Boon",
            [":table Boon", "no chance column"],
            [":field", "index", "Name", "Effect.$type", "Effect.Amount", "Effect.Band"],
            [":type", "int", "string", "Effect", "", ""],
            ["", "1", "Bless", "HealEffect", "15", "Rare"]);

        string reported = Reported(ElementSheet(), sheet);

        Assert.Contains("has no column for its member `chance`", reported);
    }

    [Fact]
    public void A_table_that_wrote_its_tags_out_is_refused_a_column_nobody_wrote()
    {
        string reported = Reported(ElementSheet(), BoonSheet(tags: "@"));

        Assert.Contains("has no column for its member `band`", reported);
        Assert.Contains("has no column for its member `elementId`", reported);
    }

    [Fact]
    public void A_row_of_the_variant_that_has_the_reference_is_refused_without_its_column()
    {
        var sheet = Sheet("Boon",
            [":table Boon", "a damaging row with no element column"],
            [":field", "index", "Name", "Effect.$type", "Effect.Chance", "Effect.Damage",
                "Effect.Amount"],
            [":type", "int", "string", "Effect", "", "", ""],
            ["", "1", "Smite", "DamageEffect", "60", "25", ""]);

        string reported = Reported(ElementSheet(), sheet);

        Assert.Contains("has no column for `Effect.ElementId`", reported);
        Assert.Contains("reference to `Element`", reported);
    }
}
