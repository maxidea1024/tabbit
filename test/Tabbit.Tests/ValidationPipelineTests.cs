using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The validation gate: what a rule file can report, and what each severity costs.
/// </summary>
/// <remarks>
/// The property worth pinning is not that a rule runs but where it runs. Validation sits ahead
/// of every output target, so a failed run has written no file and swapped no database shadow -
/// there is nothing to roll back because nothing was produced. Asserting on the absence of the
/// export is what checks that, and it is the reason these scenarios export anything at all.
///
/// spec/validation-pipeline.md.
/// </remarks>
public class ValidationPipelineTests
{
    /// <summary>
    /// Rules that report without erring let the conversion through, and their reports are
    /// printed rather than collected and dropped.
    /// </summary>
    [Fact]
    public void Reports_that_do_not_stop_the_run_are_printed_and_the_output_is_written()
    {
        var result = TabbitRunner.Convert("validation-pass");

        Assert.True(result.Succeeded, $"Conversion should have succeeded.\n{result.Describe()}");

        // Every stage ran, and each one said so through its own severity.
        Assert.Contains("Pre-validation ran with Locale=KR.", result.StdOut);

        // The row count comes from the generated reader, filled from the encoded tables - so
        // this line is the memory round trip having worked.
        Assert.Contains("Item rules ran over 3 row(s).", result.StdOut);
        // The enumerating view walked every table, which is what a typed accessor cannot do.
        Assert.Contains("Global rules ran over 7 table(s).", result.StdOut);
        Assert.Contains("A warning does not stop the run", result.StdOut);

        // Shared code was compiled into the rule that used it.
        Assert.Contains("of at most 1000", result.StdOut);

        // The accessor reached through the context rather than through its static name, which is
        // the generated assembly's extension property resolving. The rule also compares the two
        // roots, so this line appearing means they agreed - a mismatch would be an error instead.
        Assert.Contains("read 3 row(s) through context.Tables", result.StdOut);

        // The starting files sitting in `rules/tables/` and `rules/shared/` were passed over. Named
        // `.cs.template` for exactly this: a folder that holds its own instructions can only do so
        // if what it holds is not compiled. Were they scanned, `Tables.Example` alone would stop
        // the run - so this passing is the check.
        Assert.DoesNotContain("_TableRules", result.StdOut);
        Assert.DoesNotContain("_Helpers", result.StdOut);

        // A folder and a JSON document from outside the sheets, reached through paths the recipe
        // passed as free options - the core knows neither the key nor what a `.png` is.
        Assert.Contains("Scanned 1 icon(s) and 1 banned name(s).", result.StdOut);

        // The editor's project, which this recipe does not ask for: it is written by default,
        // because the accessor's sources are written either way and this is the file that makes
        // them reachable. At the validation root rather than beside the generated code - an editor
        // looks for a project where the files it opens are, and skips a dot folder while looking,
        // so a project under `.generated/` is one no editor finds.
        string root = Path.Combine(
            RepoLayout.Root, "test", "fixtures", "validation", "pass");

        string project = Path.Combine(root, "Validation.csproj");

        Assert.True(File.Exists(project), $"The editor's project should be at `{project}`.");

        string text = File.ReadAllText(project);

        // It names the generated accessor as an assembly and compiles the rules against it, which
        // is what makes `Tables` resolve while somebody is typing rather than only once the run
        // compiles it. The sources it was built from are not here: nothing edits them, so a
        // validation folder holding one per table was a hundred files for one name.
        Assert.Contains("<HintPath>lib/Tabbit.Rules.Data.dll</HintPath>", text);
        Assert.Contains("rules/**/*.cs", text);

        Assert.False(Directory.Exists(Path.Combine(root, ".generated")),
            "The accessor's sources should not be left in the validation folder.");

        Assert.True(File.Exists(Path.Combine(root, "lib", "Tabbit.Rules.Data.dll")),
            "The generated accessor should be written as an assembly.");

        // And it names the contract by a path inside this folder. That is what makes the project
        // worth committing: it used to name wherever the tool happened to be on the machine that
        // wrote it, so a clone had completion only after somebody ran a conversion.
        Assert.Contains("<HintPath>lib/Tabbit.Validation.dll</HintPath>", text);
        Assert.DoesNotContain(":/", text);
        Assert.DoesNotContain(":\\", text);

        // The contract is beside it, with the summaries an editor shows.
        Assert.True(File.Exists(Path.Combine(root, "lib", "Tabbit.Validation.dll")),
            "The contract should have been written into the validation folder.");

        Assert.True(File.Exists(Path.Combine(root, "lib", "Tabbit.Validation.xml")),
            "The contract's summaries should have been written beside it.");

        // And the conversion produced its output, because nothing stopped it.
        Assert.True(
            File.Exists(Path.Combine(RepoLayout.OutputDir("validation-pass"), "json", "Item.json")),
            "The JSON export should exist after a passing validation.");
    }

    /// <summary>
    /// An error stops the run before any target, so the export the recipe asks for is not
    /// there at all.
    /// </summary>
    [Fact]
    public void An_error_stops_the_run_and_leaves_no_output()
    {
        var result = TabbitRunner.Convert("validation-fail");

        Assert.False(result.Succeeded, $"Conversion should have failed.\n{result.Describe()}");

        // The message names the rule file that reported it.
        Assert.Contains("rules/tables/ItemRules.cs", result.StdOut);
        Assert.Contains("This fixture rule always fails", result.StdOut);

        // And it points at the cell the value came from, which is the whole reason validation
        // lives inside the converter: the record the rule held carries no location, so this is
        // the reverse lookup - record type to table, primary index to row, field name to
        // column - having landed on the right cell.
        Assert.Contains("core.xlsx : Refs : O9", result.StdOut);

        // A report about the schema points at the header cell instead, which is the column's
        // own place in the workbook.
        Assert.Contains("`Item.Price` is a int", result.StdOut);
        Assert.Contains("core.xlsx : Refs : O4", result.StdOut);

        Assert.False(
            Directory.Exists(Path.Combine(RepoLayout.OutputDir("validation-fail"), "json")),
            "A run stopped by validation should have written no export.");
    }

    /// <summary>
    /// A tier that reported stops the ones after it, and the report says what did not run.
    /// </summary>
    /// <remarks>
    /// The two rules are named against their tiers - the one that must not run sorts first - so a
    /// run that honoured only the collected order would report the wrong one. That is the whole
    /// claim being checked: the tiers decide, not the file names.
    /// </remarks>
    [Fact]
    public void A_failed_tier_stops_the_ones_after_it()
    {
        var result = TabbitRunner.Convert("validation-tiers");

        Assert.False(result.Succeeded, $"Conversion should have failed.\n{result.Describe()}");

        // The earlier tier ran, though its file sorts last.
        Assert.Contains("FOUNDATION-FAILED", result.StdOut);

        // And the later one did not, though its file sorts first.
        Assert.DoesNotContain("DEPENDENT-RAN", result.StdOut);

        // Never silently: a stage half of which did not run has to say so, or it reads exactly
        // like a stage that passed.
        Assert.Contains("Skipped 1 rule(s)", result.StdOut);
        Assert.Contains("rules/global/ADependentRules.cs", result.StdOut);
    }

    /// <summary>
    /// `TreatWarningsAsErrors` turns the tolerated warning into a stopped run, and changes
    /// nothing else: the same folder, one recipe line apart.
    /// </summary>
    [Fact]
    public void Promoted_warnings_stop_the_run()
    {
        var result = TabbitRunner.Convert("validation-strict");

        Assert.False(result.Succeeded, $"Conversion should have failed.\n{result.Describe()}");
        Assert.Contains("A warning does not stop the run", result.StdOut);

        Assert.False(
            Directory.Exists(Path.Combine(RepoLayout.OutputDir("validation-strict"), "json")),
            "A run stopped by a promoted warning should have written no export.");
    }

    /// <summary>
    /// A rule named after a table that does not exist is an error, because the alternative is
    /// a rule that silently stops running when a table is renamed.
    /// </summary>
    [Fact]
    public void A_rule_for_a_table_that_does_not_exist_is_refused()
    {
        var result = TabbitRunner.Convert("validation-unknown-table");

        Assert.False(result.Succeeded, $"Conversion should have failed.\n{result.Describe()}");

        Assert.Contains("NoSuchTable", result.StdOut);
        Assert.Contains("which this model does not have", result.StdOut);

        // The rule itself must not have run: refusing the file name is the whole point.
        Assert.DoesNotContain("This rule should never run", result.StdOut);
    }

    /// <summary>
    /// A rule asking for what its stage does not carry fails to compile, and the report names the
    /// folder that carries it.
    /// </summary>
    /// <remarks>
    /// Each stage hands over a different context type, so this is the case the split exists for: a
    /// misplaced rule used to compile and fail while running, or - for `pre` - run and find the
    /// data missing. What is checked here is the message, because the compiler's own says only
    /// that a member is absent, and an author who knew which type to reach for would not have
    /// written it.
    /// </remarks>
    [Fact]
    public void A_rule_reaching_across_stages_is_told_which_folder_has_it()
    {
        // A store from a table rule: named, and pointed at the folder that has one.
        var store = TabbitRunner.Convert("validation-wrong-stage");

        Assert.False(store.Succeeded, $"Conversion should have failed.\n{store.Describe()}");

        Assert.Contains("rules/tables/ItemRules.cs", store.StdOut);
        Assert.Contains("`Db` is on the context `rules/runtime/` hands over", store.StdOut);

        // And the data from a pre rule, which is missing for a different reason - there is no
        // accessor yet - so it gets the reason rather than `Tables does not exist`.
        var early = TabbitRunner.Convert("validation-wrong-stage-pre");

        Assert.False(early.Succeeded, $"Conversion should have failed.\n{early.Describe()}");

        Assert.Contains("rules/pre/EarlyRules.cs", early.StdOut);
        Assert.Contains("runs before a sheet is opened", early.StdOut);

        // Neither ran. Only our own wording is asserted throughout: the compiler's half of each
        // message is localized, so a machine in another language would fail on it.
        Assert.DoesNotContain("This should never be reached", store.StdOut);
        Assert.DoesNotContain("This should never be reached", early.StdOut);
    }

    /// <summary>
    /// A scaffolded rule file compiles and runs as it is written, and the command refuses to
    /// overwrite one that exists.
    /// </summary>
    /// <remarks>
    /// What it writes is the shape every rule has: two usings, a class named for what it is about,
    /// and a `Validate` that takes the context. So this checks the one thing the shape exists for -
    /// that a file written for an editor to bind is also a file the run compiles, with nothing
    /// added or removed in between.
    /// </remarks>
    [Fact]
    public void Scaffolding_writes_a_rule_that_runs_and_refuses_to_replace_one()
    {
        string folder = Path.Combine(
            RepoLayout.Root, "test", "fixtures", "validation", "pass", "rules", "tables");

        string path = Path.Combine(folder, "LocalizationRules.cs");

        if (File.Exists(path))
            File.Delete(path);

        try
        {
            var written = TabbitRunner.Convert("validation-pass", null, "--new-validator", "Localization");

            Assert.True(written.Succeeded, $"Scaffolding should have succeeded. {written.Describe()}");
            Assert.True(File.Exists(path), "The rule file should have been written.");

            string source = File.ReadAllText(path);

            // Two ordinary usings and a class - the shape an editor binds without being told
            // anything. Both names the rule uses are named in the file, so nothing depends on
            // what the host happens to inject.
            Assert.Contains("using Tabbit.Rules;", source);
            Assert.Contains("using Tabbit.Validation;", source);
            Assert.Contains("internal static class LocalizationRules", source);
            Assert.Contains("public static void Validate(ITableContext context)", source);
            Assert.Contains("foreach (var row in context.Tables.Localization.Records)", source);

            // It compiles and runs with that header in place, which is the point of writing it.
            var converted = TabbitRunner.Convert("validation-pass", null, "--validate-only");

            Assert.True(converted.Succeeded,
                $"The scaffolded rule should compile and run. {converted.Describe()}");

            // And a second attempt refuses rather than replacing somebody's work.
            var again = TabbitRunner.Convert("validation-pass", null, "--new-validator", "Localization");

            Assert.False(again.Succeeded, "Scaffolding should refuse to overwrite.");
            Assert.Contains("already exists", again.StdOut);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// A rule that reports more than anyone can read is capped, and the cap is announced.
    /// </summary>
    /// <remarks>
    /// The announcement is the point. Silently keeping the first hundred would make a rule that
    /// is wrong about everything look like a rule that found a hundred problems - and the port of
    /// a live project's shop rule produced 4,400 on its first run, which was the rule rather
    /// than the data.
    /// </remarks>
    [Fact]
    public void A_rule_that_reports_too_much_is_capped_and_says_so()
    {
        var result = TabbitRunner.Convert("validation-flood");

        Assert.False(result.Succeeded, $"Conversion should have failed. {result.Describe()}");

        // The cap held: the hundredth is there and the hundred-and-first is not.
        Assert.Contains("Report 99,", result.StdOut);
        Assert.DoesNotContain("Report 100,", result.StdOut);

        // And the run says how much it left out.
        Assert.Contains("made 50 more report(s) than the 100 shown", result.StdOut);

        // This recipe is also the one that declines the editor's project, and this folder is its
        // own - so the file's absence is `EmitIdeProject: false` being honoured rather than a
        // scenario that has not run yet. The rules still compiled, which is what makes it a
        // setting about an editor and nothing else.
        string project = Path.Combine(
            RepoLayout.Root, "test", "fixtures", "validation", "flood", "Validation.csproj");

        Assert.False(File.Exists(project),
            $"`EmitIdeProject: false` still wrote `{project}`.");
    }

    /// <summary>
    /// `--validate-only` answers with the exit code and produces nothing.
    /// </summary>
    [Fact]
    public void Validate_only_produces_no_output()
    {
        var result = TabbitRunner.Convert("validation-pass", null, "--validate-only");

        Assert.True(result.Succeeded, $"Conversion should have succeeded.\n{result.Describe()}");
        Assert.Contains("Stopping before any output", result.StdOut);

        Assert.False(
            Directory.Exists(Path.Combine(RepoLayout.OutputDir("validation-pass"), "json")),
            "--validate-only should have written no export.");
    }
}
