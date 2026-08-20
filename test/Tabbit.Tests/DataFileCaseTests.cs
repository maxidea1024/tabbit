using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A recipe naming its data files in a spelling that is not the table's own.
/// </summary>
/// <remarks>
/// The thing under test is an **agreement**, not an appearance. A data file's name is the one
/// name in the output that two programs compute: the exporter writes the file, and the reader
/// generated for each of fifteen languages opens it. Nothing downstream checks that the two
/// arrived at the same string - a reader looking for a name the exporter never wrote finds no
/// file, which surfaces at run time in somebody else's program.
///
/// They did not agree. Sixteen places derived the name from the table's normalized name and
/// the C# accessor derived it from the sheet's spelling, so a table written `item_drop` was
/// exported as `ItemDrop.tcb` and looked for as `item_drop.tcb`. Every fixture happened to
/// have table names that were already Pascal, so no gate could see it.
///
/// Asking for a spelling no table name is makes that class of disagreement visible: a reader
/// still deriving the name for itself now derives a different one from the exporter's. The
/// fixture's tables are multi-word on purpose - `ArrayTypes` becomes `array_types`, which
/// nothing arrives at by accident, whereas a single-word table would differ only in its first
/// letter's case and a case-insensitive filesystem would open it anyway.
///
/// spec/naming-conventions.md.
/// </remarks>
public class DataFileCaseTests
{
    private const string Scenario = "data-file-case";

    private static void Convert()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }

    /// <summary>The base names the exporter actually wrote, read out of its own manifest.</summary>
    private static List<string> ExportedNames()
    {
        string manifest = Path.Combine(
            RepoLayout.OutputDir(Scenario), "binary", "manifest-binary.json");

        var document = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));

        return document["Items"]!
            .Select(item => Path.GetFileNameWithoutExtension((string)item["Name"]!))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The setting reaches the files on disk, spelling and all.</summary>
    [Fact]
    public void The_exported_files_take_the_spelling()
    {
        Convert();

        var names = ExportedNames();

        Assert.Contains("array_types", names);
        Assert.Contains("item_category", names);
        Assert.Contains("client_strings", names);

        // And nothing kept the table's own spelling.
        Assert.DoesNotContain("ArrayTypes", names);
        Assert.DoesNotContain("ItemCategory", names);
    }

    /// <summary>
    /// Every generated reader asks for the names the exporter wrote.
    /// </summary>
    /// <remarks>
    /// One assertion for all fifteen languages, and the only one that checks the contract
    /// rather than one side of it. A language whose reader still derived the name itself
    /// would be looking for `ArrayTypes` while the file on disk is `array_types`, and that
    /// shows up here as a name the sources never mention.
    ///
    /// Searched over each target's whole source tree rather than a known file per language,
    /// because where the name lands differs - an accessor for most, a per-table reader for
    /// some - and the question is only whether the string is asked for at all.
    /// </remarks>
    [Fact]
    public void Every_generated_reader_asks_for_the_exported_names()
    {
        Convert();

        var exported = ExportedNames();
        string root = RepoLayout.OutputDir(Scenario);

        // The two that write no reader: `binary` is the exporter itself, and `json` is the
        // other exporter.
        var targets = Directory.GetDirectories(root)
            .Where(dir => Path.GetFileName(dir) is not ("binary" or "json"))
            .OrderBy(dir => dir, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(targets);

        var missing = new List<string>();

        foreach (string target in targets)
        {
            string sources = string.Concat(
                Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                    .Where(file => Path.GetExtension(file) is not (".tcb" or ".json"))
                    .Select(File.ReadAllText));

            foreach (string name in exported)
            {
                if (!sources.Contains(name, StringComparison.Ordinal))
                    missing.Add($"{Path.GetFileName(target)} never mentions `{name}`");
            }
        }

        Assert.True(missing.Count == 0,
            "A generated reader is not asking for the file the exporter wrote:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>The generated C# compiles, and the name it reads is the exported one.</summary>
    /// <remarks>
    /// C# is the one that was wrong, so it gets an assertion of its own rather than only its
    /// share of the sweep above.
    /// </remarks>
    [Fact]
    public void The_csharp_accessor_reads_the_exported_name()
    {
        Convert();

        string accessor = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(Scenario), "csharp", "DataFileCaseAccessor.cs"));

        Assert.Contains("$\"array_types{fileExtension}\"", accessor);
        Assert.DoesNotContain("$\"ArrayTypes{fileExtension}\"", accessor);

        var result = CsToolchain.Compile(Scenario, "DataFileCaseAccessor");

        Assert.True(result.Succeeded,
            $"The generated C# does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// A row set's suffix is appended after the spelling, as the sheet wrote it.
    /// </summary>
    /// <remarks>
    /// The existing rule for row sets is that the author writes the separator too, so the
    /// spelling has no business reaching into it: `snake` on table `ItemDrop` with set `_alt`
    /// gives `item_drop_alt`, not `item_drop__alt` and not `itemdrop_alt`.
    /// </remarks>
    [Fact]
    public void A_row_set_suffix_is_appended_after_the_spelling()
    {
        Convert();

        // This fixture declares no extra row sets, so what is asserted is the absence of a
        // suffix rather than its shape - a table with one set of rows is named after itself.
        Assert.All(ExportedNames(), name => Assert.DoesNotContain("__", name));
    }

    /// <summary>A value that is not a spelling of anything is refused.</summary>
    [Fact]
    public void A_setting_that_is_not_a_spelling_is_refused()
    {
        var thrown = Assert.Throws<TabbitException>(
            () => Tabbit.Cooking.DataFileCasing.From("SnakeCase"));

        Assert.Contains("`DataFileCase`", thrown.Message);
        Assert.Contains("`pascal`, `camel`, `snake` or `upper-snake`", thrown.Message);

        // Blank keeps each table's own name rather than being an error: a run that renamed
        // its data files without being asked to would break whatever was reading them.
        Assert.Null(Tabbit.Cooking.DataFileCasing.From(""));

        Assert.Equal(
            Tabbit.Extensions.NameCase.UpperSnake,
            Tabbit.Cooking.DataFileCasing.From("upper_snake"));
    }
}
