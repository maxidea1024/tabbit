using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Input sources, and the recipe skeleton that names them.
///
/// Sources are discovered by attribute the same way output targets are, which is what
/// the note in Program.Process asked for. The interesting cases are the ones the
/// registry made consistent: an entry that is present but switched off, and an entry
/// that points somewhere that is not there.
/// </summary>
public class SourceRegistryTests
{
    /// <summary>
    /// A source entry emptied out rather than deleted is skipped.
    ///
    /// This is how a source gets commented out in practice, and the Google Sheets
    /// importer did not handle it: a blank client secret filename reached a FileStream,
    /// so the recipe failed instead of running its remaining source.
    /// </summary>
    [Fact]
    public void An_emptied_out_source_entry_is_skipped()
    {
        var result = TabbitRunner.Convert("source-commented-out");

        Assert.True(result.Succeeded,
            $"A recipe with an emptied-out Google Sheets entry failed.{Environment.NewLine}{result.Describe()}");

        // The Excel source beside it still ran.
        string json = Path.Combine(RepoLayout.OutputDir("source-commented-out"), "json-named");
        Assert.True(Directory.GetFiles(json, "*.json").Length > 0, "No tables were imported.");
    }

    /// <summary>
    /// A source directory that is not there is reported against the recipe entry that
    /// named it, because a recipe may name several and the framework's own message says
    /// only which path was missing.
    /// </summary>
    [Fact]
    public void A_missing_source_directory_names_the_recipe_entry()
    {
        var result = TabbitRunner.Convert("source-missing-dir");

        Assert.False(result.Succeeded, "A source directory that does not exist was accepted.");
        Assert.Contains("Sources.Xlsx[0]", result.StdOut);
        Assert.Contains("no-such-directory", result.StdOut);
    }

    /// <summary>
    /// `--new-recipe` has to produce a file this build can read back and run.
    ///
    /// It writes one entry per list with the defaults filled in, and the whole point of
    /// those defaults is that the result is inert - so running it has to succeed while
    /// producing nothing, rather than failing on the blank paths.
    /// </summary>
    [Fact]
    public void New_recipe_writes_a_file_that_loads_and_runs()
    {
        string filename = Path.Combine(Path.GetTempPath(), $"tabbit-skeleton-{Guid.NewGuid():N}.json");

        // The whole root, before the run. Compared afterwards rather than looking for
        // one extension: the assertion below used to check `*.html` only, having been
        // written when the HTML target was the one at fault, so the C# and TypeScript
        // targets went on writing four files and a directory into the repository root
        // for months with this test passing.
        var before = RootEntries();

        try
        {
            var written = TabbitRunner.Invoke("--new-recipe", filename);
            Assert.True(written.Succeeded, $"--new-recipe failed.{Environment.NewLine}{written.Describe()}");

            string skeleton = File.ReadAllText(filename);

            // The ids come from the registries, so this is also a check that both of
            // them found their members.
            Assert.Contains("// Sources: xlsx, googlesheets", skeleton);
            Assert.Contains("binary, json", skeleton);
            Assert.Contains("typescript", skeleton);

            // Every list carries a filled-in entry rather than being `[]`, which is what
            // made the previous skeleton useless: it named the sections and nothing else.
            Assert.Contains("\"FileExtensionPatterns\": \".xls;.xlsx\"", skeleton);
            Assert.Contains("\"FileExtension\": \".tcb\"", skeleton);
            Assert.DoesNotContain("\"Binary\": []", skeleton);

            var ran = TabbitRunner.Invoke("--recipe", filename, "--debug");
            Assert.True(ran.Succeeded,
                $"The generated recipe did not run.{Environment.NewLine}{ran.Describe()}");

            // Inert means it wrote nothing, not merely that it exited zero. A target
            // without a blank-path guard turns `Path.Combine("", "index.html")` into a
            // relative path and writes into the working directory - and because the run
            // succeeds, the files go unnoticed long enough to be committed. Three of
            // them were.
            var appeared = RootEntries().Except(before, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.True(appeared.Count == 0,
                $"Running the skeleton recipe wrote into the repository root:" +
                $"{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", appeared)}");
        }
        finally
        {
            if (File.Exists(filename))
                File.Delete(filename);
        }
    }

    /// <summary>
    /// The repository root's own files and directories, not its contents.
    ///
    /// A name is enough: what is being detected is something appearing that was not
    /// there, and walking the whole tree would take in every build directory.
    /// </summary>
    private static IReadOnlyCollection<string> RootEntries()
        => Directory.GetFileSystemEntries(RepoLayout.Root)
                    .Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
