using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandLine;
using Tabbit.Caching;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What the build cache is allowed to skip, and what it must not.
/// </summary>
/// <remarks>
/// The defect a cache has is not that it is slow: it is that it decided nothing changed when
/// something did, and the symptom is stale output that looks exactly like correct output.
/// A golden comparison cannot find it, because the whole failure is that the output did not
/// move.
///
/// So the tests here are almost all of one shape: change one thing, run, and assert on which
/// step the run says it did. The list of things is meant to be the list of ways an input can
/// move - contents, presence, the recipe, the command line - and a row missing from it is a
/// way to be wrong that nothing checks.
///
/// Each test builds its own tree under the output directory rather than using a fixture
/// recipe. The fixtures are converted by other tests in parallel, and this one needs to edit
/// its workbook and its recipe.
/// </remarks>
public class BuildCacheTests
{
    // ---------------------------------------------------------------- the scenario

    /// <summary>
    /// One throwaway conversion: a workbook, a recipe naming two targets, and a cache.
    /// </summary>
    private sealed class Scenario : IDisposable
    {
        public Scenario(string name)
        {
            Root = Path.Combine(RepoLayout.OutputDir("build-cache"), name);

            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);

            Directory.CreateDirectory(Workbooks);
            Directory.CreateDirectory(Output);

            File.Copy(
                Path.Combine(RepoLayout.Root, "test", "fixtures", "xlsx", "core", "core.xlsx"),
                Workbook);

            WriteRecipe(indented: true);
        }

        public string Root { get; }

        public string Workbooks => Path.Combine(Root, "xlsx");

        public string Workbook => Path.Combine(Workbooks, "core.xlsx");

        public string Output => Path.Combine(Root, "out");

        public string Recipe => Path.Combine(Root, "recipe.json");

        public string Cache => Path.Combine(Root, "cache");

        /// <summary>
        /// Writes the recipe, optionally with one target's setting changed.
        /// </summary>
        /// <remarks>
        /// `Indented` is the setting the tests move, because it changes one target's output
        /// and nothing else's - which is the case the per-entry key exists for.
        ///
        /// The exclusion is here so that adding a second workbook is a change to the
        /// directory and not to what is read. That is the case the listing exists for: a
        /// workbook appearing changes no file the previous run recorded.
        /// </remarks>
        public void WriteRecipe(bool indented)
        {
            string json = $$"""
            {
              // Written by BuildCacheTests.
              "Sources": {
                "Xlsx": [
                  {
                    "Path": "{{Escape(Workbooks)}}",
                    "ExcludeWorkbooks": [ "added" ]
                  }
                ],
                "GoogleSheets": []
              },
              "Targets": [
                { "Type": "json", "Path": "{{Escape(Path.Combine(Output, "json"))}}", "Indented": {{(indented ? "true" : "false")}} },
                { "Type": "csharp", "Path": "{{Escape(Path.Combine(Output, "csharp"))}}" }
              ]
            }
            """;

            File.WriteAllText(Recipe, json);
        }

        public void AppendToRecipe(string text) => File.AppendAllText(Recipe, text);

        /// <summary>Runs the conversion, and nothing else.</summary>
        public RunResult Run(params string[] extra)
        {
            var args = new List<string> { "--recipe", Recipe, "--cache-dir", Cache };

            args.AddRange(extra);

            return TabbitRunner.Invoke(args.ToArray());
        }

        /// <summary>Every produced file, by path, with its contents and its write time.</summary>
        public Dictionary<string, (byte[] Bytes, DateTime Written)> Snapshot()
        {
            var snapshot = new Dictionary<string, (byte[], DateTime)>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories))
                snapshot[path] = (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));

            return snapshot;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A throwaway tree left behind is untidy; failing a test that passed for
                // being unable to remove it is worse. The next run deletes it.
            }
        }

        private static string Escape(string path) => path.Replace('\\', '/');
    }

    /// <summary>Whether a run decided it had nothing at all to do.</summary>
    private static bool DidNothing(RunResult run) => run.StdOut.Contains("Nothing to do.");

    /// <summary>Whether a run decided to convert from the sources up.</summary>
    private static bool DidEverything(RunResult run) => run.StdOut.Contains("Converting everything:");

    private static void Succeeded(RunResult run) => Assert.True(run.Succeeded, run.Describe());

    // ------------------------------------------------------------- nothing changed

    /// <summary>
    /// The case the whole feature is for: run twice, do the work once.
    /// </summary>
    [Fact]
    public void ASecondRunWithNothingChangedDoesNothing()
    {
        using var scenario = new Scenario(nameof(ASecondRunWithNothingChangedDoesNothing));

        Succeeded(scenario.Run());

        var after = scenario.Snapshot();

        Assert.NotEmpty(after);

        var second = scenario.Run();

        Succeeded(second);
        Assert.True(DidNothing(second), second.StdOut);

        // Not one file rewritten, and not one file's timestamp moved. The second half
        // matters on its own: a run that rewrites identical bytes still makes every
        // consumer that watches timestamps do its work again.
        Assert.Equal(after.Keys.OrderBy(key => key), scenario.Snapshot().Keys.OrderBy(key => key));

        foreach (var (path, (bytes, written)) in scenario.Snapshot())
        {
            Assert.Equal(after[path].Bytes, bytes);
            Assert.Equal(after[path].Written, written);
        }
    }

    /// <summary>
    /// A cached run and a full run produce the same tree, byte for byte.
    /// </summary>
    /// <remarks>
    /// The claim the cache rests on. Everything else here is about noticing changes; this is
    /// about the output being the same output when nothing is noticed.
    /// </remarks>
    [Fact]
    public void WhatTheCacheLeavesIsWhatAFullRunWouldWrite()
    {
        using var scenario = new Scenario(nameof(WhatTheCacheLeavesIsWhatAFullRunWouldWrite));

        Succeeded(scenario.Run());

        var cached = scenario.Snapshot();

        Succeeded(scenario.Run());
        Succeeded(scenario.Run("--full"));

        var full = scenario.Snapshot();

        Assert.Equal(cached.Keys.OrderBy(key => key), full.Keys.OrderBy(key => key));

        foreach (var (path, (bytes, _)) in full)
            Assert.Equal(cached[path].Bytes, bytes);
    }

    /// <summary>
    /// A comment, a blank line and the order of settings are not changes.
    /// </summary>
    /// <remarks>
    /// The recipe is keyed on the parsed document, which has no comments in it by the time
    /// the key is taken. Half of a real recipe is comments explaining why each exclusion is
    /// there, and editing one should not cost a conversion.
    /// </remarks>
    [Fact]
    public void EditingTheRecipesCommentsChangesNothing()
    {
        using var scenario = new Scenario(nameof(EditingTheRecipesCommentsChangesNothing));

        Succeeded(scenario.Run());

        scenario.AppendToRecipe(Environment.NewLine + "// why this exclusion is here" + Environment.NewLine);

        var second = scenario.Run();

        Succeeded(second);
        Assert.True(DidNothing(second), second.StdOut);
    }

    /// <summary>
    /// A workbook whose timestamp moved but whose contents did not is not a change.
    /// </summary>
    /// <remarks>
    /// This is what makes the cache survive everyday work. `git checkout` writes every file
    /// it restores with the time of the checkout, and Excel rewrites a workbook that was
    /// opened and closed without an edit. Without the hash behind the timestamp, a cache
    /// would be defeated by both.
    /// </remarks>
    [Fact]
    public void TouchingAWorkbookWithoutChangingItIsNotAChange()
    {
        using var scenario = new Scenario(nameof(TouchingAWorkbookWithoutChangingItIsNotAChange));

        Succeeded(scenario.Run());

        File.SetLastWriteTimeUtc(scenario.Workbook, DateTime.UtcNow.AddMinutes(5));

        var second = scenario.Run();

        Succeeded(second);
        Assert.True(DidNothing(second), second.StdOut);
    }

    // ------------------------------------------------------------ something changed

    [Fact]
    public void ChangingAWorkbookConvertsEverything()
    {
        using var scenario = new Scenario(nameof(ChangingAWorkbookConvertsEverything));

        Succeeded(scenario.Run());

        // Appended rather than edited through a spreadsheet library: what is being tested is
        // that the contents are compared, and a zip with a byte on the end is a different
        // file by every measure the cache uses. The run that follows is expected to fail on
        // it, which is why nothing here asserts it succeeded.
        using (var stream = new FileStream(scenario.Workbook, FileMode.Append))
            stream.WriteByte(0);

        var second = scenario.Run();

        Assert.True(DidEverything(second), second.StdOut);
        Assert.Contains("1 input file changed", second.StdOut);
    }

    /// <summary>
    /// A workbook appearing in the directory is a change, though no file the previous run
    /// read is different.
    /// </summary>
    /// <remarks>
    /// The case a list of files cannot see, and the reason the cache records the listing as
    /// well. The workbook added here is one the recipe excludes, so the run still succeeds -
    /// which is deliberate: the listing is taken before the recipe's own include and exclude
    /// lists are applied, because tomorrow the recipe may stop excluding it.
    /// </remarks>
    [Fact]
    public void AddingAWorkbookIsNoticedEvenWhenTheRecipeExcludesIt()
    {
        using var scenario = new Scenario(nameof(AddingAWorkbookIsNoticedEvenWhenTheRecipeExcludesIt));

        Succeeded(scenario.Run());

        File.Copy(scenario.Workbook, Path.Combine(scenario.Workbooks, "added.xlsx"));

        var second = scenario.Run();

        Succeeded(second);
        Assert.True(DidEverything(second), second.StdOut);
        Assert.Contains("the files under", second.StdOut);
    }

    [Fact]
    public void AWorkbookGoingMissingConvertsEverything()
    {
        using var scenario = new Scenario(nameof(AWorkbookGoingMissingConvertsEverything));

        Succeeded(scenario.Run());

        File.Delete(scenario.Workbook);

        var second = scenario.Run();

        Assert.True(DidEverything(second), second.StdOut);
    }

    // --------------------------------------------------------------- one entry only

    /// <summary>
    /// One output entry's setting changing runs that entry, and leaves the rest alone.
    /// </summary>
    /// <remarks>
    /// The dangerous case, and the reason <see cref="Tabbit.Helpers.StagingFiles.Keep"/>
    /// exists. The sweep deletes generated files this run did not write; an entry that is
    /// skipped wrote none of its own, so without the keep its whole output directory is
    /// emptied by the run that decided it was already correct.
    /// </remarks>
    [Fact]
    public void ChangingOneEntryRunsThatEntryAndKeepsTheOthers()
    {
        using var scenario = new Scenario(nameof(ChangingOneEntryRunsThatEntryAndKeepsTheOthers));

        Succeeded(scenario.Run());

        var before = scenario.Snapshot();

        scenario.WriteRecipe(indented: false);

        var second = scenario.Run();

        Succeeded(second);
        Assert.False(DidEverything(second), second.StdOut);
        Assert.Contains("Reusing what is unchanged.", second.StdOut);

        var after = scenario.Snapshot();

        // Nothing deleted. This is the assertion that fails when the sweep is let loose on
        // a skipped entry, and it fails by thousands of files rather than by one.
        Assert.Equal(before.Keys.OrderBy(key => key), after.Keys.OrderBy(key => key));

        // The generated C# is untouched, down to its timestamp: it was kept, not rewritten.
        foreach (var (path, (bytes, written)) in after.Where(entry => entry.Key.Contains("csharp")))
        {
            Assert.Equal(before[path].Bytes, bytes);
            Assert.Equal(before[path].Written, written);
        }

        // And the entry that changed did change.
        Assert.Contains(
            after.Where(entry => entry.Key.Contains("json")),
            entry => !before[entry.Key].Bytes.SequenceEqual(entry.Value.Bytes));
    }

    /// <summary>
    /// An output file removed by hand brings its own entry back, and only that one.
    /// </summary>
    [Fact]
    public void DeletingAnOutputFileRunsTheEntryThatWroteIt()
    {
        using var scenario = new Scenario(nameof(DeletingAnOutputFileRunsTheEntryThatWroteIt));

        Succeeded(scenario.Run());

        string generated = Directory
            .EnumerateFiles(Path.Combine(scenario.Output, "csharp"), "*.cs", SearchOption.AllDirectories)
            .First();

        File.Delete(generated);

        var second = scenario.Run();

        Succeeded(second);
        Assert.Contains("Reusing what is unchanged.", second.StdOut);
        Assert.True(File.Exists(generated), "the deleted file was not written again");

        // And the run after that has nothing left to do, which is what says the entry was
        // repaired rather than merely re-run.
        var third = scenario.Run();

        Succeeded(third);
        Assert.True(DidNothing(third), third.StdOut);
    }

    /// <summary>
    /// An output file changed by hand is restored, because the cache compares contents.
    /// </summary>
    [Fact]
    public void ChangingAnOutputFileRunsTheEntryThatWroteIt()
    {
        using var scenario = new Scenario(nameof(ChangingAnOutputFileRunsTheEntryThatWroteIt));

        Succeeded(scenario.Run());

        string generated = Directory
            .EnumerateFiles(Path.Combine(scenario.Output, "json"), "*.json", SearchOption.AllDirectories)
            .First();

        string original = File.ReadAllText(generated);

        File.WriteAllText(generated, original + Environment.NewLine);

        var second = scenario.Run();

        Succeeded(second);
        Assert.Contains("Reusing what is unchanged.", second.StdOut);
        Assert.Equal(original, File.ReadAllText(generated));
    }

    /// <summary>
    /// A run with nothing to do still removes a generated file that is no longer produced.
    /// </summary>
    /// <remarks>
    /// The alternative was found by <see cref="SweepTests"/> failing: the first version of
    /// the cache returned before the output stage, so the sweep never ran and a stale file
    /// survived every quiet run. Whether a file naming a deleted table is removed would then
    /// depend on whether anything else happened to change that day.
    /// </remarks>
    [Fact]
    public void ARunWithNothingToDoStillRemovesAFileItNoLongerProduces()
    {
        using var scenario = new Scenario(nameof(ARunWithNothingToDoStillRemovesAFileItNoLongerProduces));

        Succeeded(scenario.Run());

        string stale = Path.Combine(scenario.Output, "csharp", "DeletedTable.cs");

        File.WriteAllText(stale, "// Generated by Tabbit. DO NOT EDIT." + Environment.NewLine);

        var second = scenario.Run();

        Succeeded(second);
        Assert.True(DidNothing(second), second.StdOut);
        Assert.False(File.Exists(stale), "a stale generated file survived a run that skipped everything");
    }

    /// <summary>
    /// And a file this tool did not write survives that same run.
    /// </summary>
    /// <remarks>
    /// The permission is the header, on a skipped run as much as on a full one. Without this
    /// the cache would have turned an output directory somebody also keeps their own code in
    /// into a directory that empties itself when nothing changes.
    /// </remarks>
    [Fact]
    public void ARunWithNothingToDoLeavesAHandWrittenFileAlone()
    {
        using var scenario = new Scenario(nameof(ARunWithNothingToDoLeavesAHandWrittenFileAlone));

        Succeeded(scenario.Run());

        string mine = Path.Combine(scenario.Output, "csharp", "MyOwnHelper.cs");
        const string content = "// Mine, hand written.\n";

        File.WriteAllText(mine, content);

        var second = scenario.Run();

        Succeeded(second);
        Assert.True(DidNothing(second), second.StdOut);
        Assert.True(File.Exists(mine), "a hand-written file was deleted by a skipped run");
        Assert.Equal(content, File.ReadAllText(mine).Replace("\r\n", "\n"));
    }

    // ------------------------------------------------------------------- the flags

    [Fact]
    public void FullConvertsEverythingAndSaysSo()
    {
        using var scenario = new Scenario(nameof(FullConvertsEverythingAndSaysSo));

        Succeeded(scenario.Run());

        var second = scenario.Run("--full");

        Succeeded(second);
        Assert.Contains("--full was asked for", second.StdOut);
    }

    /// <summary>
    /// `--force-output` produces the output again without doubting the cache.
    /// </summary>
    [Fact]
    public void ForceOutputRunsEveryEntryWithNothingChanged()
    {
        using var scenario = new Scenario(nameof(ForceOutputRunsEveryEntryWithNothingChanged));

        Succeeded(scenario.Run());

        var before = scenario.Snapshot();

        var second = scenario.Run("--force-output");

        Succeeded(second);
        Assert.Contains("--force-output", second.StdOut);
        Assert.False(DidNothing(second), second.StdOut);

        var after = scenario.Snapshot();

        Assert.Equal(before.Keys.OrderBy(key => key), after.Keys.OrderBy(key => key));

        // Rewritten rather than kept - the same bytes at a later time, which is exactly what
        // somebody passing this flag is asking for.
        foreach (var (path, (bytes, written)) in after)
        {
            Assert.Equal(before[path].Bytes, bytes);
            Assert.True(written >= before[path].Written, $"{path} was not written again");
        }
    }

    /// <summary>
    /// Two flags that ask for opposite things are refused rather than ranked.
    /// </summary>
    [Fact]
    public void ValidateOnlyAndForceOutputTogetherAreRefused()
    {
        using var scenario = new Scenario(nameof(ValidateOnlyAndForceOutputTogetherAreRefused));

        var run = scenario.Run("--validate-only", "--force-output");

        Assert.False(run.Succeeded, run.Describe());
        Assert.Contains("opposite things", run.StdOut + run.StdErr);
    }

    /// <summary>
    /// A gate that reads the whole conversion is not allowed to pass on a previous answer.
    /// </summary>
    [Fact]
    public void ValidateOnlyNeverSkips()
    {
        using var scenario = new Scenario(nameof(ValidateOnlyNeverSkips));

        Succeeded(scenario.Run());

        var check = scenario.Run("--validate-only");

        Succeeded(check);
        Assert.False(DidNothing(check), check.StdOut);
    }

    /// <summary>
    /// A run with nothing to do can be told apart from one that converted, by exit code.
    /// </summary>
    /// <remarks>
    /// What a pipeline whose next step is a publish needs. Asserting the codes here rather
    /// than only the flag, because the codes are the interface: a script reads the number,
    /// not the sentence beside it.
    /// </remarks>
    [Fact]
    public void DetailedExitCodeTellsASkippedRunApart()
    {
        using var scenario = new Scenario(nameof(DetailedExitCodeTellsASkippedRunApart));

        var first = scenario.Run("--detailed-exit-code");

        Assert.Equal(ExitCode.Success, first.ExitCode);

        var second = scenario.Run("--detailed-exit-code");

        Assert.True(DidNothing(second), second.StdOut);
        Assert.Equal(ExitCode.NothingToDo, second.ExitCode);

        // And a run that had something to do is a success, flag or no flag.
        Assert.Equal(ExitCode.Success, scenario.Run("--detailed-exit-code", "--full").ExitCode);
    }

    /// <summary>
    /// Without the flag, a skipped run is an ordinary success.
    /// </summary>
    /// <remarks>
    /// The compatibility half, and the reason the flag exists. Almost everything that invokes
    /// a command line tool treats a non-zero code as a failure, so a script that chains a
    /// step after this one must keep working the day the cache first skips something.
    /// </remarks>
    [Fact]
    public void WithoutTheFlagASkippedRunIsAnOrdinarySuccess()
    {
        using var scenario = new Scenario(nameof(WithoutTheFlagASkippedRunIsAnOrdinarySuccess));

        Succeeded(scenario.Run());

        var second = scenario.Run();

        Assert.True(DidNothing(second), second.StdOut);
        Assert.Equal(ExitCode.Success, second.ExitCode);
    }

    // ------------------------------------------------------------- the option ledger

    /// <summary>
    /// Every command line option says what it means to the cache.
    /// </summary>
    /// <remarks>
    /// The one test here that is not about behaviour, and the one that keeps the rest
    /// honest. An option added without a decision about caching would otherwise fall into
    /// whichever half the code happens to default to, and if that half is "does not matter"
    /// the result is a run reusing output that this option would have changed.
    ///
    /// Nothing is asserted about which classification is right - that is a judgement, and
    /// the point is that somebody made it.
    /// </remarks>
    [Fact]
    public void EveryOptionSaysWhatItMeansToTheCache()
    {
        var unclassified = typeof(Options)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<OptionAttribute>() is not null)
            .Where(property => property.GetCustomAttribute<CacheAttribute>() is null)
            .Select(property => property.Name)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            $"These options carry no [Cache(...)], so the build cache cannot know what they mean: "
            + $"{string.Join(", ", unclassified)}. Add one - see {nameof(CacheRelevance)}.");
    }
}
