using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Extensions;
using Tabbit.Models;
using Tabbit.Recipe;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The spelling a recipe requires of the names in its sheets, and the two checks that run
/// whether or not it requires one.
/// </summary>
/// <remarks>
/// What makes this worth checking in the core rather than leaving to a project's own rules:
/// the casing normalizer takes its word boundaries from the spelling it is given, so one
/// name written three ways becomes three members and every consumer has to know which table
/// spells it which way. In a language without declarations, reading the wrong one of two
/// spellings is not an error but an absent value.
///
/// spec/targets/naming-conventions.md.
/// </remarks>
public class NamingConventionTests
{
    #region Fixtures

    /// <summary>
    /// What the layout parsers do to a field name, so a fixture's `Name` is what a sheet
    /// spelling that way would really have produced.
    /// </summary>
    private static string NormalizeField(string raw)
    {
        string text = raw.StartsWith("*", StringComparison.Ordinal) ? raw[1..].Trim() : raw;

        return string.Concat(text.Split('.').Select(part => part.ToPascalCase()));
    }

    /// <summary>A model of tables holding nothing but the named columns.</summary>
    private static Model ModelOfFields(params (string Table, string[] Fields)[] tables)
    {
        var model = new Model();

        foreach (var (tableName, fields) in tables)
        {
            var table = ModelFactory.Table(
                tableName,
                fields.Select(name => (NormalizeField(name), ValueType.Int32)).ToList());

            for (int at = 0; at < fields.Length; at++)
            {
                table.Fields[at].RawName = fields[at];
                table.Fields[at].Name = NormalizeField(fields[at]);
            }

            model.Tables.Add(table);
        }

        return model;
    }

    /// <summary>A model of one enum, its labels spelled as given.</summary>
    private static Model ModelOfLabels(string enumName, params string[] labels)
    {
        var enumm = ModelFactory.Enum(enumName, labels.Select(l => l.ToPascalCase()).ToArray());

        for (int at = 0; at < labels.Length; at++)
        {
            enumm.Labels[at].RawName = labels[at];
            enumm.Labels[at].Name = labels[at].ToPascalCase();
        }

        var model = new Model();
        model.Enums.Add(enumm);

        return model;
    }

    private static NamingRules Rules(
        string field = "", string entity = "", string label = "", string constant = "",
        string onViolation = "error", string onSpellingConflict = "warn",
        string onConsecutiveUnderscores = "warn", params string[] exempt)
        => NamingRules.From(new NamingRecipe
        {
            Field = field,
            Entity = entity,
            Label = label,
            Constant = constant,
            OnViolation = onViolation,
            OnSpellingConflict = onSpellingConflict,
            OnConsecutiveUnderscores = onConsecutiveUnderscores,
            Exempt = exempt.ToList(),
        });

    /// <remarks>
    /// The id travels beside the text. These reports are the most composed ones this tool
    /// writes - a sentence built from named phrases - so a test that reads only the wording is
    /// a test that breaks when any one of those phrases is reworded.
    /// </remarks>
    private static List<(Severity Severity, string Message, string MessageId, Location Location)> Check(
        Model model, NamingRules rules)
    {
        var diagnostics = new Diagnostics();
        ModelCooker.ValidateNaming(model, rules, diagnostics);

        return diagnostics.Entries
            .Select(entry => (entry.Item1, entry.Item2.Message, entry.Item2.MessageId, entry.Item2.Location))
            .ToList();
    }

    #endregion

    #region Spelling conflicts

    /// <summary>
    /// Three spellings of one field, spread over three tables, are one report - and it says
    /// the generated code is split, because these three do not normalize together.
    /// </summary>
    [Fact]
    public void One_name_written_three_ways_is_reported_once()
    {
        var found = Check(
            ModelOfFields(
                ("Item", ["Id", "maxHitPoints"]),
                ("Ship", ["Id", "maxhitpoints"]),
                ("Mate", ["Id", "maxhitPoints"])),
            Rules());

        var conflict = Assert.Single(found, f => f.Message.Contains("is written"));

        Assert.Equal(Severity.Warning, conflict.Severity);
        Assert.Equal(Tabbit.Cooking.NamingMessages.SpellingConflict, conflict.MessageId);
        Assert.Contains("written 3 ways", conflict.Message);
        Assert.Contains("`maxHitPoints`", conflict.Message);
        Assert.Contains("`maxhitpoints`", conflict.Message);
        Assert.Contains("`maxhitPoints`", conflict.Message);

        // The three normalize apart, so this is not only a sheet disagreeing with itself.
        Assert.Contains("separate member for each spelling", conflict.Message);
    }

    /// <summary>
    /// Two spellings that normalize to the same name are still reported, and the report says
    /// the output is unaffected. The sheets disagree, and the next spelling may not be so
    /// harmless.
    /// </summary>
    [Fact]
    public void Spellings_that_normalize_together_are_reported_as_not_splitting_the_output()
    {
        var found = Check(
            ModelOfFields(
                ("Item", ["Id", "my_flag"]),
                ("Ship", ["Id", "myFlag"])),
            Rules());

        var conflict = Assert.Single(found, f => f.Message.Contains("is written"));

        Assert.Equal(Tabbit.Cooking.NamingMessages.SpellingConflict, conflict.MessageId);
        Assert.Contains("written 2 ways", conflict.Message);
        Assert.Contains("normalize to the same name", conflict.Message);
        Assert.DoesNotContain("separate member", conflict.Message);
    }

    /// <summary>
    /// A conflict the generated code carries weighs more than one it does not, and the
    /// recipe's setting names the weight of the first.
    /// </summary>
    /// <remarks>
    /// The distinction is what keeps a `TreatWarningsAsErrors` build from stopping over
    /// sheets that spell one name two ways without consequence, while still stopping over
    /// the spellings that become two members. `Info` is the level that cannot be promoted,
    /// which is exactly the property wanted here.
    /// </remarks>
    [Fact]
    public void A_conflict_that_splits_the_output_outweighs_one_that_does_not()
    {
        var splits = ModelOfFields(("Item", ["Id", "myFlag"]), ("Ship", ["Id", "myflag"]));
        var doesNot = ModelOfFields(("Item", ["Id", "myFlag"]), ("Ship", ["Id", "my_flag"]));

        Assert.Equal(Severity.Warning, Assert.Single(Check(splits, Rules())).Severity);
        Assert.Equal(Severity.Info, Assert.Single(Check(doesNot, Rules())).Severity);

        // Raising the setting raises both, and keeps the order between them.
        Assert.Equal(
            Severity.Error,
            Assert.Single(Check(splits, Rules(onSpellingConflict: "error"))).Severity);
        Assert.Equal(
            Severity.Warning,
            Assert.Single(Check(doesNot, Rules(onSpellingConflict: "error"))).Severity);
    }

    /// <summary>
    /// The report points at a cell somebody has to edit, not at the spelling it recommends.
    /// </summary>
    /// <remarks>
    /// Every spelling and its location is in the message, so this is only about where an
    /// editor jumps to - and jumping to the correct cell reads as the report accusing it.
    /// </remarks>
    [Fact]
    public void The_report_points_at_a_cell_that_has_to_change()
    {
        var found = Check(
            ModelOfFields(
                ("Item", ["Id", "maxHitPoints"]),
                ("Item2", ["Id", "maxHitPoints"]),
                ("Ship", ["Id", "maxhitpoints"])),
            Rules());

        var conflict = Assert.Single(found, f => f.Message.Contains("is written"));

        Assert.Equal(Tabbit.Cooking.NamingMessages.SpellingConflict, conflict.MessageId);
        Assert.Contains("Settle on `maxHitPoints`", conflict.Message);
        Assert.Equal("Ship", conflict.Location.Sheet);
    }

    /// <summary>One spelling used in many places is not a conflict, however many places.</summary>
    [Fact]
    public void One_spelling_in_many_tables_is_not_reported()
    {
        var found = Check(
            ModelOfFields(
                ("Item", ["Id", "maxHitPoints"]),
                ("Ship", ["Id", "maxHitPoints"]),
                ("Mate", ["Id", "maxHitPoints"])),
            Rules());

        Assert.Empty(found);
    }

    /// <summary>
    /// A record member and a flat field are not one name written twice, so the nesting
    /// separator survives the fold.
    /// </summary>
    [Fact]
    public void A_nested_level_does_not_collide_with_a_flat_field_of_the_joined_name()
    {
        var found = Check(
            ModelOfFields(
                ("Item", ["Id", "slot.id"]),
                ("Ship", ["Id", "slotId"])),
            Rules());

        Assert.Empty(found);
    }

    /// <summary>
    /// The count decides which spelling to settle on only when the recipe has not said.
    /// A convention outranks a majority, because a spelling spreads by being copied: a
    /// family of sheets written from one another carries a wrong spelling as readily as a
    /// right one, and three tables agreeing is not evidence.
    /// </summary>
    [Fact]
    public void The_declared_convention_outranks_the_majority()
    {
        var model = ModelOfFields(
            ("A", ["Id", "MaxHitPoints"]),
            ("B", ["Id", "MaxHitPoints"]),
            ("C", ["Id", "MaxHitPoints"]),
            ("D", ["Id", "maxHitPoints"]));

        var withoutConvention = Check(model, Rules());
        var conflict = Assert.Single(withoutConvention, f => f.Message.Contains("is written"));
        Assert.Equal(Tabbit.Cooking.NamingMessages.SpellingConflict, conflict.MessageId);
        Assert.Contains("Settle on `MaxHitPoints`", conflict.Message);

        var withConvention = Check(model, Rules(field: "camel", onViolation: "warn"));
        conflict = Assert.Single(withConvention, f => f.Message.Contains("is written"));
        Assert.Contains("Settle on `maxHitPoints`", conflict.Message);
    }

    /// <summary>
    /// When several of a group's spellings satisfy the convention, the count decides among
    /// them - the convention has nothing left to say.
    /// </summary>
    /// <remarks>
    /// This is the same blind spot the round trip has, seen from the other side: `applypoint`
    /// reads as one word, so `applypointId` is as much camel case as `applyPointId` is.
    /// A convention judges spelling; it cannot judge where the words in a name are.
    /// </remarks>
    [Fact]
    public void A_convention_cannot_settle_a_group_whose_spellings_all_follow_it()
    {
        Assert.True(NamingRules.Follows("applypointId", NameCase.Camel));
        Assert.True(NamingRules.Follows("applyPointId", NameCase.Camel));

        var found = Check(
            ModelOfFields(
                ("A", ["Id", "applypointId"]),
                ("B", ["Id", "applypointId"]),
                ("C", ["Id", "applyPointId"])),
            Rules(field: "camel"));

        var conflict = Assert.Single(found, f => f.Message.Contains("is written"));
        Assert.Equal(Tabbit.Cooking.NamingMessages.SpellingConflict, conflict.MessageId);
        Assert.Contains("Settle on `applypointId`", conflict.Message);
    }

    /// <summary>`ignore` switches the check off; `error` stops the run.</summary>
    [Fact]
    public void The_conflict_severity_is_the_recipe_s_to_choose()
    {
        var model = ModelOfFields(("Item", ["Id", "myFlag"]), ("Ship", ["Id", "Myflag"]));

        Assert.Empty(Check(model, Rules(onSpellingConflict: "ignore")));

        var asError = Check(model, Rules(onSpellingConflict: "error"));
        Assert.Equal(Severity.Error, Assert.Single(asError).Severity);
    }

    #endregion

    #region Consecutive underscores

    /// <summary>
    /// A run of underscores is reported, and a single one is not. The case rules read any
    /// run as one word boundary, so the difference reaches nothing downstream.
    /// </summary>
    [Fact]
    public void A_run_of_underscores_is_reported_and_a_single_one_is_not()
    {
        var found = Check(ModelOfFields(("Item", ["Id", "a__b"])), Rules());

        var report = Assert.Single(found);
        Assert.Equal(Severity.Warning, report.Severity);
        Assert.StartsWith(
            "Field `a__b` of table `Item` holds two or more underscores in a row",
            report.Message);

        // And says what both spellings arrive as, which is the whole point: the difference
        // is not carried anywhere.
        Assert.Equal(Tabbit.Cooking.NamingMessages.ConsecutiveUnderscores, report.MessageId);
        Assert.Contains("`AB`", report.Message);

        // And ends with the name to type, rather than leaving the reader to work it out.
        Assert.EndsWith("Write it as `a_b`.", report.Message);

        Assert.Empty(Check(ModelOfFields(("Item", ["Id", "a_b"])), Rules()));
    }

    /// <summary>
    /// The round trip cannot catch this one: spelling `a__b` in snake case gives `a__b`
    /// back, because interior underscores are preserved. Hence a check of its own.
    /// </summary>
    [Fact]
    public void A_run_of_underscores_survives_the_snake_case_round_trip()
    {
        Assert.True(NamingRules.Follows("a__b", NameCase.Snake));

        var found = Check(
            ModelOfFields(("Item", ["id", "a__b"])),
            Rules(field: "snake"));

        Assert.Single(found);
        Assert.Contains("underscores in a row", found[0].Message);
    }

    /// <summary>
    /// Leading and trailing runs survive into the generated code, so they are two names
    /// rather than two spellings of one, and are left alone.
    /// </summary>
    [Fact]
    public void Leading_and_trailing_underscores_are_not_reported()
    {
        Assert.Empty(Check(
            ModelOfFields(("Item", ["Id", "__reserved", "trailing__"])),
            Rules(onSpellingConflict: "ignore")));
    }

    /// <summary>
    /// The name it asks for keeps the leading run and collapses only the interior one, so
    /// the suggestion is the same name spelled unambiguously rather than a different name.
    /// </summary>
    [Fact]
    public void The_suggested_name_collapses_only_the_interior_run()
    {
        var report = Assert.Single(Check(
            ModelOfFields(("Item", ["Id", "__a__b"])),
            Rules(onSpellingConflict: "ignore")));

        Assert.Equal(Tabbit.Cooking.NamingMessages.ConsecutiveUnderscores, report.MessageId);
        Assert.EndsWith("Write it as `__a_b`.", report.Message);
    }

    #endregion

    #region Declared spelling

    /// <summary>
    /// A name that is not spelled the declared way is reported with the spelling it would
    /// have, so the report says what to type rather than only what is wrong.
    /// </summary>
    [Fact]
    public void A_name_off_the_declared_spelling_is_reported_with_its_correct_spelling()
    {
        var found = Check(
            ModelOfFields(("Item", ["id", "MaxHitPoints"])),
            Rules(field: "camel"));

        var report = Assert.Single(found);
        Assert.Equal(Severity.Error, report.Severity);

        // The name lands next to the word for what it is, and what it belongs to trails.
        Assert.Equal(Tabbit.Cooking.NamingMessages.SpellingViolation, report.MessageId);
        Assert.StartsWith("Field `MaxHitPoints` of table `Item` is not spelled", report.Message);
        Assert.Contains("`camel`", report.Message);
        Assert.EndsWith("Write it as `maxHitPoints`.", report.Message);
    }

    /// <summary>A kind nobody declared a spelling for is not judged.</summary>
    [Fact]
    public void An_undeclared_kind_is_not_judged()
    {
        Assert.Empty(Check(ModelOfFields(("Item", ["Id", "MaxHitPoints"])), Rules()));
    }

    /// <summary>
    /// A word without internal boundaries passes every spelling, which is the round trip
    /// being honest rather than lenient: nothing distinguishes a one-word name from a name
    /// whose boundaries were lost.
    /// </summary>
    [Fact]
    public void A_name_with_no_word_boundaries_passes_camel_case()
    {
        Assert.Empty(Check(ModelOfFields(("Item", ["id", "maxhitpoints"])), Rules(field: "camel")));
    }

    /// <summary>
    /// A nested name is judged one level at a time, and the report names the level so the
    /// author knows which part of the cell to edit.
    /// </summary>
    [Fact]
    public void A_nested_name_is_judged_and_reported_per_level()
    {
        var found = Check(
            ModelOfFields(("Item", ["id", "slot1.Id"])),
            Rules(field: "camel"));

        var report = Assert.Single(found);
        Assert.StartsWith(
            "Field `slot1.Id` of table `Item` has a level, `Id`, that is not spelled",
            report.Message);
        Assert.Equal(Tabbit.Cooking.NamingMessages.SpellingViolationInLevel, report.MessageId);
        Assert.EndsWith("Write it as `id`.", report.Message);
    }

    /// <summary>The `*` that marks a secondary index is not part of the name being judged.</summary>
    [Fact]
    public void The_secondary_index_marker_is_not_part_of_the_name()
    {
        Assert.Empty(Check(ModelOfFields(("Item", ["id", "*itemId"])), Rules(field: "camel")));

        var found = Check(ModelOfFields(("Item", ["id", "*ItemId"])), Rules(field: "camel"));
        Assert.Contains("`ItemId`", Assert.Single(found).Message);
    }

    /// <summary>
    /// A name this tool invented is not judged.
    /// </summary>
    /// <remarks>
    /// **The check is a claim about what people write.** A composite value type expands into
    /// one field per component and the component's part of the name is this tool's - `.X` on a
    /// `vec3f`, `.R` on a `color` - so a report about it names a spelling nobody chose and
    /// points at a cell that does not hold it. The enums have had the same exemption since
    /// they gained a synthesized zero label.
    ///
    /// A member of a declared struct is deliberately **not** exempt: somebody wrote that name
    /// in a `.tbs` file. That case is the test below.
    /// </remarks>
    [Fact]
    public void A_name_the_tool_invented_is_not_judged()
    {
        var model = ModelOfFields(("Item", ["offset.X", "offset.Y", "offset.Z"]));

        // Written the way the composite expansion writes them: the whole field, not the
        // level, is the tool's - so every one of these is skipped.
        Assert.NotEmpty(Check(model, Rules(field: "snake")));

        foreach (var field in model.Tables[0].Fields)
            field.Synthesized = true;

        Assert.Empty(Check(model, Rules(field: "snake")));
    }

    /// <summary>Entities, labels and constants each answer to their own setting.</summary>
    [Fact]
    public void Each_kind_answers_to_its_own_setting()
    {
        var model = ModelOfLabels("Grade", "high", "low");

        Assert.Empty(Check(model, Rules(entity: "pascal", field: "camel")));

        var found = Check(model, Rules(label: "pascal"));
        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.Contains("enum labels", f.Message));
        Assert.StartsWith("Label `high` of enum `Grade` is not spelled", found[0].Message);
    }

    /// <summary>`upper-snake` is a spelling the round trip can judge.</summary>
    [Fact]
    public void Upper_snake_is_judged_like_the_rest()
    {
        Assert.True(NamingRules.Follows("MAX_HIT_POINTS", NameCase.UpperSnake));
        Assert.False(NamingRules.Follows("maxHitPoints", NameCase.UpperSnake));

        // One function both spells and judges, so an acronym cannot be spelled one way and
        // judged another - which is what a second pattern of its own would have risked.
        Assert.Equal("HTTP_SERVER", "HTTPServer".ToUpperSnakeCase());
        Assert.True(NamingRules.Follows("HTTP_SERVER", NameCase.UpperSnake));
    }

    #endregion

    #region The exempt list

    /// <summary>
    /// A listed spelling is left out of every check here, and taking it off the list brings
    /// it back. This is what lets a project declare a convention it does not yet meet: the
    /// existing names go on the list, the setting goes to `error`, and a new violation is
    /// stopped rather than buried among the old ones.
    /// </summary>
    [Fact]
    public void An_exempt_spelling_is_left_out_of_every_check()
    {
        var model = ModelOfFields(
            ("Item", ["id", "MaxHitPoints", "a__b"]),
            ("Ship", ["id", "maxhitpoints"]));

        Assert.Empty(Check(
            model,
            Rules(field: "camel", exempt: ["MaxHitPoints", "maxhitpoints", "a__b"])));

        // The same model, one name off the list: the checks it was hiding from come back.
        var found = Check(model, Rules(field: "camel", exempt: ["maxhitpoints", "a__b"]));
        Assert.Contains(found, f => f.Message.EndsWith("Write it as `maxHitPoints`."));
    }

    /// <summary>
    /// Listing the name covers the column written with an index marker, because the marker
    /// is not part of the name and a recipe naming one and not the other is not drawing a
    /// distinction.
    /// </summary>
    [Fact]
    public void An_exempt_name_covers_the_marked_spelling_of_it()
    {
        Assert.Empty(Check(
            ModelOfFields(("Item", ["id", "*ItemId"])),
            Rules(field: "camel", exempt: ["ItemId"])));
    }

    #endregion

    #region What the tool wrote itself

    /// <summary>
    /// The zero label the tool inserts into an enum is not a spelling anybody chose, so
    /// holding it to a convention would report a name with no cell to fix.
    /// </summary>
    [Fact]
    public void The_inserted_zero_label_is_not_judged()
    {
        var model = ModelOfLabels("Grade", "high", "low");

        model.Enums[0].Labels.Insert(0, new Models.Enum.Label
        {
            Name = "None",
            RawName = "None",
            Value = 0,
            Location = model.Enums[0].Location,
            Comment = "None (automatically inserted by Tabbit)",
            Synthesized = true,
        });

        // `None` is pascal case and the labels here are camel: were it judged, it would be
        // the one report this produces.
        Assert.Empty(Check(model, Rules(label: "camel")));
    }

    #endregion

    #region Recipe parsing

    /// <summary>
    /// A misspelled setting names the values it takes. Reported before any name is judged,
    /// so it does not arrive as a verdict about whichever name reached it first.
    /// </summary>
    [Fact]
    public void A_setting_that_is_not_a_spelling_of_anything_is_refused()
    {
        var thrown = Assert.Throws<TabbitException>(() => Rules(field: "PascalCase"));
        Assert.Equal(Tabbit.Recipe.RecipeMessages.NamingCaseUnknown, thrown.MessageId);

        thrown = Assert.Throws<TabbitException>(() => Rules(onSpellingConflict: "shout"));
        Assert.Contains("`error`, `warn` or `ignore`", thrown.Message);

        // `OnViolation` has no `ignore`: leaving the kind blank is how a kind goes unchecked,
        // and two ways to say it is one more than a reader can derive a rule from.
        thrown = Assert.Throws<TabbitException>(() => Rules(onViolation: "ignore"));
        Assert.Contains("leave its spelling blank", thrown.Message);
    }

    /// <summary>
    /// Hyphen and underscore are the same separator in a setting's value, as they are for
    /// the other policy settings a recipe carries.
    /// </summary>
    [Fact]
    public void A_setting_takes_either_separator()
    {
        Assert.Equal(NameCase.UpperSnake, Rules(constant: "upper_snake").DeclaredFor(NameKind.Constant));
        Assert.Equal(NameCase.UpperSnake, Rules(constant: "UPPER-SNAKE").DeclaredFor(NameKind.Constant));
    }

    /// <summary>
    /// A recipe with no naming section still runs the two checks that need no convention,
    /// and judges no spelling. This is what makes the section optional without making the
    /// mistake it exists for invisible.
    /// </summary>
    [Fact]
    public void A_recipe_with_no_naming_section_still_checks_what_needs_no_convention()
    {
        var rules = NamingRules.From(new NamingRecipe());

        Assert.True(rules.HasAnyCheck);
        Assert.Null(rules.DeclaredFor(NameKind.Field));
        Assert.Equal(Severity.Warning, rules.OnSpellingConflict);
        Assert.Equal(Severity.Warning, rules.OnConsecutiveUnderscores);

        var found = Check(
            ModelOfFields(("Item", ["Id", "myFlag"]), ("Ship", ["Id", "Myflag"])),
            rules);

        Assert.Single(found);
    }

    /// <summary>
    /// The section binds from the recipe file the way it is written in the documentation.
    /// </summary>
    /// <remarks>
    /// Asserted through the recipe loader rather than by constructing the class, because the
    /// class is not the part that could be wrong: a property the JSON never reaches holds
    /// its default silently, and every test that builds the object by hand would still pass.
    /// </remarks>
    [Fact]
    public void The_section_binds_from_a_recipe_document()
    {
        var document = Newtonsoft.Json.Linq.JObject.Parse("""
            {
              "Naming": {
                "Field": "camel",
                "Constant": "upper-snake",
                "OnViolation": "warn",
                "OnConsecutiveUnderscores": "error",
                "Exempt": [ "Icon_Path", "IconPath" ]
              }
            }
            """);

        var recipe = document.ToObject<RecipeModel>()!;
        var rules = NamingRules.From(recipe.Naming);

        Assert.Equal(NameCase.Camel, rules.DeclaredFor(NameKind.Field));
        Assert.Equal(NameCase.UpperSnake, rules.DeclaredFor(NameKind.Constant));
        Assert.Null(rules.DeclaredFor(NameKind.Entity));
        Assert.Equal(Severity.Warning, rules.OnViolation);
        Assert.Equal(Severity.Error, rules.OnConsecutiveUnderscores);

        // Not written in the document, so it holds its default rather than being switched off.
        Assert.Equal(Severity.Warning, rules.OnSpellingConflict);

        Assert.True(rules.IsExempt("Icon_Path"));
        Assert.False(rules.IsExempt("icon_path"));
    }

    /// <summary>
    /// A recipe with no `Naming` key at all binds to the same thing as an empty section, so
    /// every recipe written before this existed keeps converting and still gets the two
    /// checks that need no convention.
    /// </summary>
    [Fact]
    public void A_recipe_document_without_the_section_still_gets_the_defaults()
    {
        var recipe = Newtonsoft.Json.Linq.JObject.Parse("""{ "ArrayDelimiter": ";" }""")
            .ToObject<RecipeModel>()!;

        var rules = NamingRules.From(recipe.Naming);

        Assert.True(rules.HasAnyCheck);
        Assert.Null(rules.DeclaredFor(NameKind.Field));
        Assert.Equal(Severity.Warning, rules.OnSpellingConflict);
    }

    /// <summary>Switching both off leaves nothing to ask, and the pass says so.</summary>
    [Fact]
    public void Switching_everything_off_leaves_no_check()
    {
        var rules = Rules(onSpellingConflict: "ignore", onConsecutiveUnderscores: "ignore");

        Assert.False(rules.HasAnyCheck);
        Assert.Empty(Check(ModelOfFields(("Item", ["Id", "a__b"])), rules));
    }

    #endregion
}
