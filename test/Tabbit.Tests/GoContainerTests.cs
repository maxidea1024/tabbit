using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The generated Go for a `set` and a `map`: that it builds, and that it reads back.
/// </summary>
/// <remarks>
/// **Go has neither a set nor an ordered map**, so both come out as `map` and the slices
/// beside them are what hold the file's order. That makes the two layers genuinely separate
/// here, and it makes reading them back worth asserting: a map built from the wrong column
/// produces the same slices and the same exported JSON.
///
/// spec/types/set-and-map.md sections 7 and 9.
/// </remarks>
public class GoContainerTests
{
    private const string Scenario = "containers-target";

    [Fact]
    public void The_generated_containers_build()
    {
        Convert();

        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"Go toolchain required to build the generated Go. {why}");

        var build = ConformanceHarness.Execute(
            "go", GeneratedDir(), "build", "./...");

        Assert.True(build.Succeeded,
            $"The generated Go does not build.{Environment.NewLine}{build.Output}");
    }

    /// <summary>
    /// And reads the binary back into both layers.
    /// </summary>
    /// <remarks>
    /// The slices are the file's order, which is the sheet's - nothing sorts them, and a Go
    /// map would not have kept an order to sort. The maps answer about the keys this row
    /// holds and not about the ones it does not, which is what says each was built from its
    /// own row's column.
    /// </remarks>
    [Fact]
    public void The_generated_reader_fills_both_layers()
    {
        var values = RunHarness();

        Assert.Equal(
            ["new", "sale"],
            values.GetProperty("tags").EnumerateArray().ToArray().Select(v => v.GetString()));

        Assert.True(values.GetProperty("hasSale").GetBoolean());
        Assert.False(values.GetProperty("hasGone").GetBoolean());

        // A map of scalars answers with the value.
        Assert.True(values.GetProperty("priceOf11Set").GetBoolean());
        Assert.Equal(120, values.GetProperty("priceOf11").GetInt32());

        // A map of structs answers with the entry's position, and the fields are read at it.
        Assert.True(values.GetProperty("dropIndexOf2Set").GetBoolean());
        Assert.Equal(1, values.GetProperty("dropIndexOf2").GetInt32());
        Assert.Equal(102, values.GetProperty("dropItemAt2").GetInt32());
        Assert.Equal(3, values.GetProperty("dropCountAt2").GetInt32());

        // The slice keeps the order the sheet wrote.
        Assert.Equal(
            [10, 11],
            values.GetProperty("priceKeys").EnumerateArray().ToArray().Select(v => v.GetInt32()));
    }

    /// <summary>A row that wrote nothing has containers of no entries, not none.</summary>
    [Fact]
    public void A_row_with_no_entries_reads_as_empty()
    {
        var values = RunHarness();

        Assert.Equal(0, values.GetProperty("emptyTagCount").GetInt32());
        Assert.Equal(0, values.GetProperty("emptyPriceCount").GetInt32());
    }

    private static string GeneratedDir()
        => Path.Combine(RepoLayout.OutputDir(Scenario), "go");

    private static void Convert()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }

    /// <summary>
    /// Copies the driver into the generated module and runs it over the binary.
    /// </summary>
    /// <remarks>
    /// Inside the module, as a package of its own: Go has no relative imports and the
    /// generated code is only importable from within the module its go.mod declares. The
    /// conformance harness does the same, for the same reason.
    /// </remarks>
    private static JsonElement RunHarness()
    {
        Convert();

        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"Go toolchain required to run the generated Go. {why}");

        string moduleDir = GeneratedDir();
        string harnessDir = Path.Combine(moduleDir, "harness");

        Directory.CreateDirectory(harnessDir);

        File.Copy(
            Path.Combine(RepoLayout.Root, "test", "fixtures", "tools",
                         "go-check-containers", "main.go"),
            Path.Combine(harnessDir, "main.go"),
            overwrite: true);

        var run = ConformanceHarness.Execute(
            "go", moduleDir, "run", "./harness",
            Path.Combine(RepoLayout.OutputDir(Scenario), "binary"));

        Assert.True(run.Succeeded,
            $"The generated Go did not read the binary.{Environment.NewLine}{run.Output}");

        return JsonDocument.Parse(run.StdOut).RootElement.Clone();
    }
}
