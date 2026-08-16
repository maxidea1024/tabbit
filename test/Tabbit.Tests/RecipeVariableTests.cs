using System;
using System.IO;

using Tabbit.Recipe;

using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The `${NAME}` placeholders a recipe may carry, and which of them are filled when.
/// </summary>
/// <remarks>
/// This is what lets one committed recipe describe two environments. The thing it must
/// never do is substitute nothing and carry on: a blank where a document id or an output
/// path should be does not fail where it was written, it fails somewhere else, later, as
/// a missing directory or an empty conversion.
/// </remarks>
public class RecipeVariableTests : IDisposable
{
    private readonly string _dir;

    public RecipeVariableTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tabbit-recipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp directory */ }
    }

    private string Write(string json)
    {
        string path = Path.Combine(_dir, "recipe.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static void With(string name, string value, Action body)
    {
        Environment.SetEnvironmentVariable(name, value);

        try { body(); }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }

    // ------------------------------------------------------------ what is filled

    /// <summary>
    /// A source path, which is the setting that separates one environment's sheets from
    /// another's.
    /// </summary>
    [Fact]
    public void A_source_path_is_filled_from_the_environment()
    {
        With("TABBIT_TEST_ENV", "live", () =>
        {
            var recipe = RecipeModel.LoadFromFile(Write(
                @"{ ""Sources"": { ""Xlsx"": [ { ""Path"": ""./sheets/${TABBIT_TEST_ENV}"" } ] } }"));

            Assert.Equal("./sheets/live", recipe!.Sources.Xlsx[0].Path);
        });
    }

    /// <summary>
    /// And an output path, so the two environments cannot write over each other.
    /// </summary>
    [Fact]
    public void A_target_path_is_filled_from_the_environment()
    {
        With("TABBIT_TEST_ENV", "dev", () =>
        {
            var recipe = RecipeModel.LoadFromFile(Write(
                @"{ ""Targets"": [ { ""Type"": ""json"", ""Path"": ""./out/${TABBIT_TEST_ENV}"" } ] }"));

            Assert.Equal("./out/dev", (string)recipe!.Targets[0]["Path"]!);
        });
    }

    /// <summary>
    /// Inside an array too - the sheet and workbook filters are lists.
    /// </summary>
    [Fact]
    public void A_value_inside_an_array_is_filled()
    {
        With("TABBIT_TEST_ENV", "live", () =>
        {
            var recipe = RecipeModel.LoadFromFile(Write(
                @"{ ""Sources"": { ""Xlsx"": [ {
                       ""Path"": ""./sheets"",
                       ""ExcludeWorkbooks"": [ ""${TABBIT_TEST_ENV}/*"" ] } ] } }"));

            Assert.Equal(new[] { "live/*" }, recipe!.Sources.Xlsx[0].ExcludeWorkbooks);
        });
    }

    /// <summary>
    /// More than one in a value, and more than one value.
    /// </summary>
    [Fact]
    public void Several_placeholders_in_one_value_are_all_filled()
    {
        With("TABBIT_TEST_A", "one", () => With("TABBIT_TEST_B", "two", () =>
        {
            var recipe = RecipeModel.LoadFromFile(Write(
                @"{ ""Sources"": { ""Xlsx"": [
                     { ""Path"": ""./${TABBIT_TEST_A}/${TABBIT_TEST_B}"" } ] } }"));

            Assert.Equal("./one/two", recipe!.Sources.Xlsx[0].Path);
        }));
    }

    /// <summary>
    /// A value holding a quote or a backslash. Substituting into the recipe's text rather
    /// than into the parsed document would have to escape this back into JSON, and
    /// getting that wrong produces a file that no longer parses.
    /// </summary>
    [Fact]
    public void A_value_that_would_need_escaping_survives()
    {
        With("TABBIT_TEST_ENV", @"C:\a\b ""quoted""", () =>
        {
            var recipe = RecipeModel.LoadFromFile(Write(
                @"{ ""Sources"": { ""Xlsx"": [ { ""Path"": ""${TABBIT_TEST_ENV}"" } ] } }"));

            Assert.Equal(@"C:\a\b ""quoted""", recipe!.Sources.Xlsx[0].Path);
        });
    }

    /// <summary>
    /// Comments still work. They are most of what a recipe in this repository is.
    /// </summary>
    [Fact]
    public void Comments_are_still_accepted()
    {
        With("TABBIT_TEST_ENV", "live", () =>
        {
            var recipe = RecipeModel.LoadFromFile(Write(
                "{\n  // which environment this build is for\n" +
                "  \"Sources\": { \"Xlsx\": [ { \"Path\": \"./${TABBIT_TEST_ENV}\" } ] }\n}"));

            Assert.Equal("./live", recipe!.Sources.Xlsx[0].Path);
        });
    }

    // ----------------------------------------------------------- what is missing

    [Fact]
    public void An_unset_variable_is_an_error_that_names_it_and_where_it_is()
    {
        var ex = Assert.Throws<TabbitException>(() => RecipeModel.LoadFromFile(Write(
            @"{ ""Sources"": { ""Xlsx"": [ { ""Path"": ""./${TABBIT_TEST_NOT_SET}"" } ] } }")));

        Assert.Contains("TABBIT_TEST_NOT_SET", ex.Message);
        Assert.Contains("Sources.Xlsx[0].Path", ex.Message);
    }

    /// <summary>
    /// Every one of them, not the first. Somebody setting up a machine has all of them to
    /// set, and one run per variable is how that goes wrong.
    /// </summary>
    [Fact]
    public void All_the_unset_variables_are_reported_together()
    {
        var ex = Assert.Throws<TabbitException>(() => RecipeModel.LoadFromFile(Write(
            @"{ ""Sources"": { ""Xlsx"": [ { ""Path"": ""./${TABBIT_TEST_ONE}"" } ] },
                 ""Targets"": [ { ""Type"": ""json"", ""Path"": ""./${TABBIT_TEST_TWO}"" } ] }")));

        Assert.Contains("TABBIT_TEST_ONE", ex.Message);
        Assert.Contains("TABBIT_TEST_TWO", ex.Message);
    }

    // -------------------------------------------------- what is left for later

    /// <summary>
    /// A connection string keeps its own behaviour: the target resolves it when it runs.
    ///
    /// Which means a recipe that also exports to a database can still be validated by
    /// somebody who does not hold that password - the ordinary case for a pull request
    /// check, and the reason this exception exists.
    /// </summary>
    [Fact]
    public void A_connection_string_is_left_for_the_target_that_runs_it()
    {
        var recipe = RecipeModel.LoadFromFile(Write(
            @"{ ""Targets"": [ { ""Type"": ""mysql"",
                 ""ConnectionString"": ""Server=db;Pwd=${TABBIT_TEST_NOT_SET}"" } ] }"));

        Assert.Equal(
            "Server=db;Pwd=${TABBIT_TEST_NOT_SET}",
            (string)recipe!.Targets[0]["ConnectionString"]!);
    }

    /// <summary>
    /// And the validation connections, which `--skip-runtime-validation` exists to not open.
    /// </summary>
    [Fact]
    public void A_validation_connection_is_left_for_the_rule_that_opens_it()
    {
        var recipe = RecipeModel.LoadFromFile(Write(
            @"{ ""Validation"": { ""Path"": ""./validation"", ""Connections"": {
                 ""Live"": ""mysql:Server=db;Pwd=${TABBIT_TEST_NOT_SET}"" } } }"));

        Assert.Equal(
            "mysql:Server=db;Pwd=${TABBIT_TEST_NOT_SET}",
            recipe!.Validation.Connections["Live"]);
    }

    /// <summary>
    /// A recipe that says nothing still reads as nothing rather than as a parse failure -
    /// which is what it did before the document was parsed here.
    /// </summary>
    [Fact]
    public void An_empty_recipe_is_nothing_rather_than_a_failure()
    {
        Assert.Null(RecipeModel.LoadFromFile(Write("   ")));
    }
}
