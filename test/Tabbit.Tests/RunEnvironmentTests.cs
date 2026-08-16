using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// `--env`: the word that both labels a run and decides the paths it uses.
/// </summary>
/// <remarks>
/// Kept together on purpose. A flag for the label and a variable for the paths is a pair
/// that can disagree, and output stamped `live` that was built from the development
/// sheets is worse than output with no label at all - it answers the question wrongly
/// instead of leaving it open.
/// </remarks>
public class RunEnvironmentTests
{
    private const string Scenario = "run-environment";

    private static string Recipe(string outputRoot)
    {
        string path = Path.Combine(RepoLayout.OutputDir(Scenario), "recipe.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path));

        File.WriteAllText(path,
            @"{
  ""Sources"": { ""Xlsx"": [ { ""Path"": ""test/fixtures/xlsx/core"" } ] },
  ""Targets"": [
    { ""Type"": ""json"", ""Path"": """ + outputRoot.Replace('\\', '/') + @"/${TABBIT_ENV}/data"" },
    { ""Type"": ""summary"", ""Path"": """ + outputRoot.Replace('\\', '/') + @"/${TABBIT_ENV}/summary"" }
  ]
}");

        return path;
    }

    /// <summary>
    /// The one word both steers the conversion and ends up in the summary, so the two
    /// cannot disagree about what was built.
    /// </summary>
    [Theory]
    [InlineData("dev")]
    [InlineData("live")]
    public void The_environment_names_the_paths_and_labels_the_summary(string environment)
    {
        string outputRoot = Path.Combine(RepoLayout.OutputDir(Scenario), "out-" + environment);

        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);

        var run = TabbitRunner.Invoke("--recipe", Recipe(outputRoot), "--env", environment);

        Assert.True(run.Succeeded, $"The conversion failed.{Environment.NewLine}{run.Describe()}");

        // The paths the recipe asked for, which are the ones `--env` filled in.
        Assert.True(
            Directory.Exists(Path.Combine(outputRoot, environment, "data")),
            $"`{environment}` did not become the output directory.");

        string summary = Path.Combine(outputRoot, environment, "summary", "summary.json");
        Assert.True(File.Exists(summary), "No summary was written.");

        using var document = JsonDocument.Parse(File.ReadAllText(summary));

        Assert.Equal(
            environment,
            document.RootElement.GetProperty("run").GetProperty("environment").GetString());
    }

    /// <summary>
    /// A run that says nothing is recorded as saying nothing, rather than as the default -
    /// there is no default, and writing one in would be a claim nobody made.
    /// </summary>
    [Fact]
    public void A_run_that_names_no_environment_records_none()
    {
        string outputRoot = Path.Combine(RepoLayout.OutputDir(Scenario), "out-unlabelled");
        string path = Path.Combine(RepoLayout.OutputDir(Scenario), "unlabelled.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path));

        File.WriteAllText(path,
            @"{
  ""Sources"": { ""Xlsx"": [ { ""Path"": ""test/fixtures/xlsx/core"" } ] },
  ""Targets"": [ { ""Type"": ""summary"", ""Path"": """ + outputRoot.Replace('\\', '/') + @"/summary"" } ]
}");

        var run = TabbitRunner.Invoke("--recipe", path);

        Assert.True(run.Succeeded, $"The conversion failed.{Environment.NewLine}{run.Describe()}");

        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outputRoot, "summary", "summary.json")));

        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("run").GetProperty("environment").ValueKind);
    }

    /// <summary>
    /// The disagreement this design exists to prevent. Refused rather than resolved
    /// either way: whichever one won, the run would be labelled by one and built by the
    /// other for as long as nobody looked.
    /// </summary>
    [Fact]
    public void An_environment_that_contradicts_the_variable_is_refused()
    {
        string outputRoot = Path.Combine(RepoLayout.OutputDir(Scenario), "out-conflict");

        var environment = new Dictionary<string, string> { { "TABBIT_ENV", "dev" } };

        var run = TabbitRunner.Invoke(environment,
            "--recipe", Recipe(outputRoot), "--env", "live");

        Assert.False(run.Succeeded, "A contradicting environment was accepted.");

        string output = run.StdOut + run.StdErr;

        Assert.Contains("TABBIT_ENV", output);
        Assert.Contains("live", output);
        Assert.Contains("dev", output);
    }

    /// <summary>
    /// Agreeing is not contradicting. A CI job that exports the variable and also passes
    /// the flag is doing nothing wrong.
    /// </summary>
    [Fact]
    public void An_environment_that_matches_the_variable_is_accepted()
    {
        string outputRoot = Path.Combine(RepoLayout.OutputDir(Scenario), "out-agree");

        var environment = new Dictionary<string, string> { { "TABBIT_ENV", "live" } };

        var run = TabbitRunner.Invoke(environment,
            "--recipe", Recipe(outputRoot), "--env", "live");

        Assert.True(run.Succeeded, $"The conversion failed.{Environment.NewLine}{run.Describe()}");
    }

    /// <summary>
    /// And the variable alone still works, which is what a recipe using `${TABBIT_ENV}`
    /// does on a machine that exports it once.
    /// </summary>
    [Fact]
    public void The_variable_alone_labels_the_run()
    {
        string outputRoot = Path.Combine(RepoLayout.OutputDir(Scenario), "out-variable");

        var environment = new Dictionary<string, string> { { "TABBIT_ENV", "staging" } };

        var run = TabbitRunner.Invoke(environment, "--recipe", Recipe(outputRoot));

        Assert.True(run.Succeeded, $"The conversion failed.{Environment.NewLine}{run.Describe()}");

        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(outputRoot, "staging", "summary", "summary.json")));

        Assert.Equal(
            "staging",
            document.RootElement.GetProperty("run").GetProperty("environment").GetString());
    }
}
