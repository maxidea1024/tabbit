using System;
using System.IO;
using Newtonsoft.Json;
using Tabbit.Exporters;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The converter's refusal to write data that would break a reader already deployed.
///
/// The generated readers refuse a column they cannot read, which is the guard that
/// matters and the one that cannot be forgotten - but it fires in the client's process,
/// after the data shipped. The baseline moves the same judgment to conversion time,
/// against a record of the previous schema kept in source control.
///
/// Every test here drives the CLI with a recipe written on the spot, because the
/// question is about a file that persists between runs rather than about any one
/// scenario's output.
/// </summary>
public class SchemaBaselineTests
{
    /// <summary>Somewhere transient to keep a recipe, a baseline and some data.</summary>
    private static string WorkDir(string name)
    {
        string dir = Path.Combine(RepoLayout.OutputDir("_baseline"), name);

        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// A recipe that exports one fixture's binary and nothing else, with the baseline
    /// check on.
    /// </summary>
    private static string Recipe(string dir, string xlsxScenario, params string[] accepted)
    {
        string accept = accepted.Length == 0
            ? ""
            : string.Join(", ", Array.ConvertAll(accepted, one => $"\"{one}\""));

        string path = Path.Combine(dir, "recipe.json");

        File.WriteAllText(path, $@"{{
  ""Sources"": {{
""Xlsx"": [ {{ ""Path"": ""test/fixtures/xlsx/{xlsxScenario}"" }} ]
  }},
  ""Targets"": [
  {{
    ""Type"": ""binary"",
    ""Path"": ""{Escape(Path.Combine(dir, "binary"))}"",
    ""SchemaBaseline"": ""{Escape(Path.Combine(dir, "schema-baseline.json"))}"",
    ""AcceptSchemaChanges"": [ {accept} ]
  }}
  ]
}}");

        return path;
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");

    private static string BaselinePath(string dir) => Path.Combine(dir, "schema-baseline.json");

    private static RunResult Convert(string recipe)
        => TabbitRunner.Invoke("--recipe", recipe, "--debug");

    // ------------------------------------------------------------------ first run

    /// <summary>
    /// With nothing to compare against, the run writes the baseline and says so. A
    /// missing baseline is a first run, not an error: refusing here would mean nobody
    /// could ever turn the check on.
    /// </summary>
    [Fact]
    public void A_first_run_records_the_baseline_and_succeeds()
    {
        string dir = WorkDir("first-run");
        var run = Convert(Recipe(dir, "evolution-v1"));

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(BaselinePath(dir)), "The baseline was not written.");

        string baseline = File.ReadAllText(BaselinePath(dir));

        // Keyed by tag, and the tags are the sheet's own `@N`.
        Assert.Contains("\"Evolution\"", baseline);
        Assert.Contains("\"Label\"", baseline);
        Assert.Contains("Commit it", baseline);
    }

    /// <summary>Converting the same schema twice changes nothing and complains about nothing.</summary>
    [Fact]
    public void An_unchanged_schema_passes()
    {
        string dir = WorkDir("unchanged");
        string recipe = Recipe(dir, "evolution-v1");

        Assert.Equal(0, Convert(recipe).ExitCode);

        string first = File.ReadAllText(BaselinePath(dir));

        Assert.Equal(0, Convert(recipe).ExitCode);
        Assert.Equal(first, File.ReadAllText(BaselinePath(dir)));
    }

    // ------------------------------------------------------------------ type changes

    /// <summary>
    /// v1's baseline against v2's schema: three columns changed type, and each one is a
    /// column an already-deployed reader refuses rather than reads.
    ///
    /// The widenings are in there deliberately. A widening is lossless in the direction
    /// the reader promotes - old file, new code - and useless in the other, so shipping
    /// one means shipping regenerated code with it. That is a decision, so it is
    /// something to acknowledge rather than something to infer.
    /// </summary>
    [Fact]
    public void A_type_change_stops_the_run_naming_every_column()
    {
        string dir = WorkDir("type-change");

        Assert.Equal(0, Convert(Recipe(dir, "evolution-v1")).ExitCode);

        var run = Convert(Recipe(dir, "evolution-v2"));

        Assert.NotEqual(0, run.ExitCode);

        string message = run.StdOut + run.StdErr;

        Assert.Contains("Promoted.Amount", message);
        Assert.Contains("Promoted.Ratio", message);
        Assert.Contains("Refused.Code", message);
        Assert.Contains("AcceptSchemaChanges", message);

        // What it was and what it became, both in words, because the point of the
        // message is that somebody can decide from it.
        Assert.Contains("32 bit integer", message);
        Assert.Contains("64 bit integer", message);
    }

    /// <summary>
    /// Nothing is written when the check refuses. A run that both wrote the data and
    /// complained would be the worst of the two outcomes.
    /// </summary>
    [Fact]
    public void A_refused_run_writes_no_data_and_leaves_the_baseline_alone()
    {
        string dir = WorkDir("refused-writes-nothing");

        Assert.Equal(0, Convert(Recipe(dir, "evolution-v1")).ExitCode);

        string before = File.ReadAllText(BaselinePath(dir));
        string data = Path.Combine(dir, "binary", "Promoted.tcb");
        byte[] dataBefore = File.ReadAllBytes(data);

        Assert.NotEqual(0, Convert(Recipe(dir, "evolution-v2")).ExitCode);

        Assert.Equal(before, File.ReadAllText(BaselinePath(dir)));
        Assert.Equal(dataBefore, File.ReadAllBytes(data));
    }

    /// <summary>
    /// The same change, acknowledged, goes through - and the baseline then holds the new
    /// shape, so the acknowledgment is spent and can come back out of the recipe.
    /// </summary>
    [Fact]
    public void An_acknowledged_type_change_goes_through_and_is_then_spent()
    {
        string dir = WorkDir("acknowledged");

        Assert.Equal(0, Convert(Recipe(dir, "evolution-v1")).ExitCode);

        var accepted = Convert(Recipe(
            dir, "evolution-v2", "Promoted.Amount", "Promoted.Ratio", "Refused.Code"));

        Assert.Equal(0, accepted.ExitCode);

        // No acknowledgment this time, and nothing to acknowledge.
        Assert.Equal(0, Convert(Recipe(dir, "evolution-v2")).ExitCode);
    }

    // --------------------------------------------------------------- deletions

    /// <summary>
    /// A column that vanished without being tombstoned. Its tag would be free for the
    /// next column to take, and then a reader built before the deletion reads the new
    /// column as the old one - so the deletion has to be recorded in the sheet.
    /// </summary>
    [Fact]
    public void A_deleted_column_wants_a_tombstone()
    {
        string dir = WorkDir("deleted");

        Assert.Equal(0, Convert(Recipe(dir, "evolution-v1")).ExitCode);

        // A column the schema never had, put into the baseline as though it once did.
        // Cheaper than a third workbook, and it is the same comparison either way.
        Amend(dir, baseline => baseline.Tables["Evolution"]["9"] = new SchemaBaseline.Column
        {
            Name = "Removed",
            Element = 6,
            Kind = 0,
            ExplicitTag = true,
        });

        var run = Convert(Recipe(dir, "evolution-v1"));

        Assert.NotEqual(0, run.ExitCode);

        string message = run.StdOut + run.StdErr;

        Assert.Contains("Evolution.Removed", message);
        Assert.Contains("#Removed@9", message);
    }

    /// <summary>
    /// v2 deletes `Doomed` and tombstones it, which is the whole of what the check asks
    /// for - `Doomed` is not among the columns the run below has to acknowledge.
    /// </summary>
    [Fact]
    public void A_tombstoned_deletion_needs_no_acknowledgment()
    {
        string dir = WorkDir("tombstoned");

        Assert.Equal(0, Convert(Recipe(dir, "evolution-v1")).ExitCode);

        var run = Convert(Recipe(
            dir, "evolution-v2", "Promoted.Amount", "Promoted.Ratio", "Refused.Code"));

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("Doomed", run.StdErr);

        // And the tag stays spoken for, so nothing can take it later.
        var baseline = Read(dir);
        Assert.True(baseline.Tables["Evolution"]["4"].Retired,
            "The deleted column's tag was not retired.");
    }

    // ------------------------------------------------------------- tag reuse

    /// <summary>
    /// A tag that carried a column once and is now carrying another. This is the one
    /// change the tag scheme cannot survive - an old reader reads the new column as the
    /// retired one and has no way to know - so there is no acknowledgment for it.
    /// </summary>
    [Fact]
    public void A_retired_tag_cannot_come_back_even_acknowledged()
    {
        string dir = WorkDir("reuse");

        Assert.Equal(0, Convert(Recipe(dir, "evolution-v1")).ExitCode);

        // As though tag 2 had been something else and been given up, and `Label` had
        // then taken it.
        Amend(dir, baseline => baseline.Tables["Evolution"]["2"] = new SchemaBaseline.Column
        {
            Name = "Retired",
            Element = 6,
            Kind = 0,
            Retired = true,
            ExplicitTag = true,
        });

        var run = Convert(Recipe(dir, "evolution-v1", "Evolution.Label"));

        Assert.NotEqual(0, run.ExitCode);

        string message = run.StdOut + run.StdErr;

        Assert.Contains("Evolution.Label", message);
        Assert.Contains("Retired", message);
    }

    // ----------------------------------------------------- tables without tags

    /// <summary>
    /// A table whose tags come from column order, with a column that moved.
    ///
    /// This is the case explicit tags exist to prevent: delete or reorder a column and
    /// every tag after it means something else, so an old reader reads the wrong column
    /// and succeeds if the types happen to line up. A name that changed tag is the
    /// symptom, and in a table with no `@N` it is refused.
    /// </summary>
    [Fact]
    public void A_shifted_column_in_an_untagged_table_is_refused()
    {
        string dir = WorkDir("shifted");

        // `layout-edge` spells no tags out, so its columns are numbered by position.
        Assert.Equal(0, Convert(Recipe(dir, "layout-edge")).ExitCode);

        var baseline = Read(dir);
        string table = "SecondTable";

        Assert.False(baseline.Tables[table]["2"].ExplicitTag,
            "The fixture was expected to have no explicit tags.");

        Amend(dir, b => b.Tables[table]["2"].Name = "SomethingElse");

        var run = Convert(Recipe(dir, "layout-edge"));

        Assert.NotEqual(0, run.ExitCode);

        string message = run.StdOut + run.StdErr;

        Assert.Contains("no explicit tags", message);
        Assert.Contains("SomethingElse", message);
    }

    // -------------------------------------------------------------- the file itself

    /// <summary>
    /// A baseline that cannot be parsed is a stop rather than a fresh start: silently
    /// replacing it would give up the check exactly when somebody has broken the file.
    /// </summary>
    [Fact]
    public void An_unreadable_baseline_stops_the_run()
    {
        string dir = WorkDir("corrupt");

        Assert.Equal(0, Convert(Recipe(dir, "evolution-v1")).ExitCode);

        File.WriteAllText(BaselinePath(dir), "{ not json");

        var run = Convert(Recipe(dir, "evolution-v1"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("could not be read", run.StdOut + run.StdErr);
    }

    /// <summary>With no baseline configured the check does not run at all.</summary>
    [Fact]
    public void The_check_is_off_when_no_baseline_is_configured()
    {
        string dir = WorkDir("off");
        string recipe = Path.Combine(dir, "recipe.json");

        File.WriteAllText(recipe, $@"{{
  ""Sources"": {{
""Xlsx"": [ {{ ""Path"": ""test/fixtures/xlsx/evolution-v1"" }} ]
  }},
  ""Targets"": [ {{ ""Type"": ""binary"", ""Path"": ""{Escape(Path.Combine(dir, "binary"))}"" }} ]
}}");

        Assert.Equal(0, Convert(recipe).ExitCode);
        Assert.False(File.Exists(BaselinePath(dir)), "A baseline was written without being asked for.");
    }

    // ------------------------------------------------------------------ helpers

    private static SchemaBaseline Read(string dir)
        => JsonConvert.DeserializeObject<SchemaBaseline>(File.ReadAllText(BaselinePath(dir)));

    /// <summary>Edits the baseline in place, to stand in for a schema's earlier self.</summary>
    private static void Amend(string dir, Action<SchemaBaseline> edit)
    {
        var baseline = Read(dir);
        edit(baseline);

        File.WriteAllText(BaselinePath(dir),
            JsonConvert.SerializeObject(baseline, Formatting.Indented));
    }
}
