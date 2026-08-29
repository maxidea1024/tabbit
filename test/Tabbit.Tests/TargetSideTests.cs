using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tabbit.Models;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Target-side filtering: building for one side leaves out whatever the sheet
/// marked for the other.
///
/// The markers were parsed and validated for years but never applied to any
/// output - they only showed up as a column in the HTML documentation.
///
/// Filtering happens by handing each exporter and generator a projected view of
/// the model rather than by teaching each of them to filter, so these tests check
/// the projection through the artifacts every consumer actually produces.
/// </summary>
[Collection("core-server-tree")]
public class TargetSideTests
{
    private static string[] TableNames(string scenario)
        => Directory.GetFiles(Path.Combine(RepoLayout.OutputDir(scenario), "json-named"), "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !name.StartsWith("manifest"))
                    .OrderBy(name => name)
                    .ToArray();

    private static string[] FieldNames(string scenario, string table)
    {
        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(scenario), "json-named", table + ".json"));

        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement[0].EnumerateObject().Select(p => p.Name).ToArray();
    }

    [Fact]
    public void Client_build_drops_server_entities_and_columns()
    {
        var result = TabbitRunner.Convert("core-client");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var tables = TableNames("core-client");
        Assert.Contains("ClientStrings", tables);
        Assert.DoesNotContain("ServerTuning", tables);

        var fields = FieldNames("core-client", "TestFieldTypes");
        Assert.Contains("boolField", fields);      // marked c
        Assert.DoesNotContain("intField", fields); // marked s
        Assert.Contains("index", fields);          // primary index always survives

        Assert.DoesNotContain("price", FieldNames("core-client", "Item"));
    }

    [Fact]
    public void Server_build_drops_client_entities_and_columns()
    {
        var result = TabbitRunner.Convert("core-server");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var tables = TableNames("core-server");
        Assert.Contains("ServerTuning", tables);
        Assert.DoesNotContain("ClientStrings", tables);

        var fields = FieldNames("core-server", "TestFieldTypes");
        Assert.Contains("intField", fields);        // marked s
        Assert.DoesNotContain("boolField", fields); // marked c
        Assert.Contains("index", fields);

        Assert.Contains("price", FieldNames("core-server", "Item"));
    }

    /// <summary>
    /// Filtering has to reach the binary tables too, not just the readable
    /// artifacts - the generated readers are built against the same column set.
    /// </summary>
    [Fact]
    public void Binary_tables_reflect_the_filtered_column_set()
    {
        TabbitRunner.Convert("core");
        long both = new FileInfo(Path.Combine(RepoLayout.OutputDir("core"), "binary", "TestFieldTypes.tcb")).Length;

        TabbitRunner.Convert("core-client");
        long client = new FileInfo(Path.Combine(RepoLayout.OutputDir("core-client"), "binary", "TestFieldTypes.tcb")).Length;

        Assert.True(client < both,
            $"Client binary ({client} bytes) should be smaller than the unfiltered one ({both} bytes).");

        Assert.False(File.Exists(Path.Combine(RepoLayout.OutputDir("core-client"), "binary", "ServerTuning.tcb")),
            "A server-only table was written into the client build.");
    }

    /// <summary>
    /// An unrecognized side in a recipe is a configuration mistake, and the error
    /// has to name the entry rather than a cell, because there is no cell.
    /// </summary>
    [Fact]
    public void Unrecognized_recipe_target_side_is_rejected()
    {
        var ex = Assert.Throws<TabbitException>(
            () => Tabbit.Recipe.RecipeTargetSide.Of("both", "Targets[1]"));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.TargetSideUnknown, ex.MessageId);
        Assert.Contains("Targets[1]", ex.Message);
    }

    /// <summary>
    /// `--target-side` narrows a recipe that asks for no side in particular.
    ///
    /// Every entry in `core-cli-side` defaults to both sides, so nothing but the
    /// option can account for a difference in what comes out.
    /// </summary>
    [Fact]
    public void Command_line_target_side_narrows_a_recipe_that_declares_no_side()
    {
        var result = TabbitRunner.Convert("core-cli-side", null, "--target-side", "server");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var tables = TableNames("core-cli-side");
        Assert.Contains("ServerTuning", tables);
        Assert.DoesNotContain("ClientStrings", tables);

        var fields = FieldNames("core-cli-side", "TestFieldTypes");
        Assert.Contains("intField", fields);        // marked s
        Assert.DoesNotContain("boolField", fields); // marked c

        // The same recipe, unnarrowed, has to produce both - otherwise the assertions
        // above would pass for a recipe that was never building the client side.
        Assert.True(TabbitRunner.Convert("core-cli-side").Succeeded);

        var unfiltered = TableNames("core-cli-side");
        Assert.Contains("ServerTuning", unfiltered);
        Assert.Contains("ClientStrings", unfiltered);
    }

    /// <summary>
    /// An entry built for one side is skipped entirely by a run narrowed to the other,
    /// rather than being built with an empty model.
    /// </summary>
    [Fact]
    public void Command_line_target_side_skips_entries_built_for_the_other_side()
    {
        var result = TabbitRunner.Convert("core-server", null, "--target-side", "client");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        // Every entry in core-server declares "s", so a client run has no work at all
        // and must not leave a half-written output tree behind.
        string json = Path.Combine(RepoLayout.OutputDir("core-server"), "json-named");

        Assert.False(Directory.Exists(json) && Directory.GetFiles(json, "*.json").Length > 0,
            "A client-narrowed run produced output from server-only recipe entries.");
    }

    /// <summary>
    /// A misspelled side has to fail rather than quietly falling back to both, which
    /// would hand a build server the wrong artifacts without saying so.
    /// </summary>
    [Fact]
    public void Command_line_target_side_rejects_an_unknown_value()
    {
        var result = TabbitRunner.Convert("core-cli-side", null, "--target-side", "sever");

        Assert.False(result.Succeeded, "A misspelled --target-side value was accepted.");
        Assert.Contains("--target-side", result.StdOut);
    }

    // ------------------------------------------------------------------ what a narrowing is

    /// <summary>
    /// A narrowed table keeps every setting that is the table's own.
    /// </summary>
    /// <remarks>
    /// **The narrowing is about fields, and nothing else.** The projection built the narrowed
    /// table by naming the properties to carry, so every one it did not name arrived at its
    /// default - and a default is a different answer, not a missing one.
    ///
    /// Two of them were visible from outside. `TrimTrailingArrayElements` off wrote an array
    /// to its declared length rather than the row's, which for a polymorphic group meant
    /// writing an element the sheet never filled in - and that failed inside the binary writer
    /// with a cast between two type names, naming neither the table nor the column. The keys
    /// went the other way and said nothing at all: a narrowed build generated a table without
    /// the composite lookup the whole build has.
    /// </remarks>
    [Fact]
    public void A_narrowed_table_keeps_what_the_table_says_about_itself()
    {
        var model = new Model();

        var table = new Table
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "S" },
            RawName = "Stage",
            Name = "Stage",
            Comment = "",
            TargetSide = TargetSide.Both,
            TrimTrailingArrayElements = true,
            AllowArrayGaps = true,
            PrimaryIndexName = "Id",
            MetaTags = new Dictionary<string, string> { { "owner", "combat" } },
            Keys = [new TableKey { FieldNames = ["Id"], IsPrimary = true }],
        };

        table.Fields.Add(new Field
        {
            OwnerTable = table,
            NameLocation = table.Location,
            TypeLocation = table.Location,
            DetailTypeLocation = table.Location,
            TargetSideLocation = table.Location,
            TargetSide = TargetSide.Both,
            Index = 0,
            Comment = "",
            RawName = "id",
            Name = "Id",
            TypeName = "int",
            Type = Tabbit.Models.ValueType.Int32,
        });

        model.Tables.Add(table);

        var narrowed = model.ProjectTo(TargetSide.ClientOnly).Tables[0];

        Assert.True(narrowed.TrimTrailingArrayElements);
        Assert.True(narrowed.AllowArrayGaps);
        Assert.Equal("Id", narrowed.PrimaryIndexName);
        Assert.Equal("combat", narrowed.MetaTags["owner"]);

        // The key survives because its column does.
        Assert.Single(narrowed.Keys);
        Assert.Equal("Id", narrowed.Keys[0].FieldNames[0]);
    }

    /// <summary>
    /// A key whose column this side does not have is dropped rather than left dangling.
    /// </summary>
    /// <remarks>
    /// A key is written as names and answered from the field list, so one naming a column
    /// that is not there is a lookup nothing can build - and the generated code for it would
    /// take an argument for a member the record does not have.
    /// </remarks>
    [Fact]
    public void A_key_whose_column_the_side_drops_goes_with_it()
    {
        var model = new Model();

        var table = new Table
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "S" },
            RawName = "Stage",
            Name = "Stage",
            Comment = "",
            TargetSide = TargetSide.Both,
            Keys =
            [
                new TableKey { FieldNames = ["Id"], IsPrimary = true },
                new TableKey { FieldNames = ["Id", "ServerOnly"], IsPrimary = false },
            ],
        };

        table.Fields.Add(Column(table, index: 0, name: "Id", side: TargetSide.Both));
        table.Fields.Add(Column(table, index: 1, name: "ServerOnly", side: TargetSide.ServerOnly));

        model.Tables.Add(table);

        var narrowed = model.ProjectTo(TargetSide.ClientOnly).Tables[0];

        Assert.Single(narrowed.Keys);
        Assert.True(narrowed.Keys[0].IsPrimary);
    }

    private static Field Column(Table table, int index, string name, TargetSide side)
        => new()
        {
            OwnerTable = table,
            NameLocation = table.Location,
            TypeLocation = table.Location,
            DetailTypeLocation = table.Location,
            TargetSideLocation = table.Location,
            TargetSide = side,
            Index = index,
            Comment = "",
            RawName = name,
            Name = name,
            TypeName = "int",
            Type = Tabbit.Models.ValueType.Int32,
        };
}
