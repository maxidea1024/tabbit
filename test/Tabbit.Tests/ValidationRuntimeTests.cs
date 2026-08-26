using System.Collections.Generic;
using System.IO;
using MySqlConnector;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The runtime stage: rules that read a store outside the sheets.
/// </summary>
/// <remarks>
/// Against the suite's real containers rather than a fake, because what is being checked is that
/// a rule can ask a question of a live store and that an unanswerable question fails the run. A
/// stub would answer both the same way.
///
/// spec/validation/validation-pipeline.md §6.
/// </remarks>
public class ValidationRuntimeTests
{
    private const string Scenario = "validation-runtime";

    /// <summary>
    /// A runtime rule queries MySQL and Redis, and the reports it makes carry the cells.
    /// </summary>
    [Fact]
    public void A_runtime_rule_reads_the_live_store()
    {
        GivenLiveProducts(1, 2);

        var result = TabbitRunner.Convert(Scenario, DatabaseFixture.ConverterEnvironment);

        Assert.True(result.Succeeded, $"Conversion should have succeeded. {result.Describe()}");

        // The query ran and the rule said what it compared against.
        Assert.Contains("Compared against 2 live product(s).", result.StdOut);

        // Redis answered too, through the name the recipe gave it.
        Assert.Contains("does not hold the probe key", result.StdOut);

        // And the row the live table does not list was reported against its own cell.
        Assert.Contains("This item is not listed in the live product table.", result.StdOut);
        Assert.Contains("core.xlsx : Refs :", result.StdOut);
    }

    /// <summary>
    /// `--skip-runtime-validation` leaves the stage out and records how much it left out.
    /// </summary>
    /// <remarks>
    /// The record is the point. A gate that is switched off has to say so, or a run that skipped
    /// it reads exactly like a run that passed it.
    /// </remarks>
    [Fact]
    public void Skipping_the_runtime_stage_is_recorded()
    {
        var result = TabbitRunner.Convert(
            Scenario, DatabaseFixture.ConverterEnvironment, "--skip-runtime-validation");

        Assert.True(result.Succeeded, $"Conversion should have succeeded. {result.Describe()}");

        Assert.Contains("Skipped 1 runtime rule(s)", result.StdOut);
        Assert.DoesNotContain("Compared against", result.StdOut);
    }

    /// <summary>
    /// A rule outside `rules/runtime/` cannot open a store, and the message says which folder it
    /// belongs in.
    /// </summary>
    /// <remarks>
    /// Otherwise `--skip-runtime-validation` would mean nothing: a table rule holding a
    /// connection fails on a machine with no access however much was skipped.
    /// </remarks>
    [Fact]
    public void Only_a_runtime_rule_may_open_a_store()
    {
        var result = TabbitRunner.Convert(
            "validation-misplaced-store", DatabaseFixture.ConverterEnvironment);

        Assert.False(result.Succeeded, $"Conversion should have failed. {result.Describe()}");

        Assert.Contains("only the `rules/runtime/` rules may do", result.StdOut);
        Assert.DoesNotContain("This should never be reached", result.StdOut);
    }

    /// <summary>Creates the table the fixture rule reads.</summary>
    private static void GivenLiveProducts(params int[] ids)
    {
        DatabaseFixture.EnsureRunning();

        using var connection = new MySqlConnection(DatabaseFixture.MySqlConnectionString);

        connection.Open();

        Execute(connection, "DROP TABLE IF EXISTS live_products");
        Execute(connection, "CREATE TABLE live_products (id INT PRIMARY KEY)");

        foreach (int id in ids)
            Execute(connection, $"INSERT INTO live_products (id) VALUES ({id})");
    }

    private static void Execute(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
