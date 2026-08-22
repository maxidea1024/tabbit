using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Serilog;
using Tabbit.Helpers;
using Tabbit.Recipe;
using Tabbit.Sources;
using Tabbit.Targets;

namespace Tabbit.Caching;

/// <summary>What a run still has to do, once the cache has been consulted.</summary>
public enum CachePlan
{
    /// <summary>Everything. Either the cache is off, or something the run depends on moved.</summary>
    Everything,

    /// <summary>
    /// The model is what it was, so only the output entries whose own settings changed -
    /// or whose files are no longer intact - have anything left to produce.
    /// </summary>
    ChangedTargetsOnly,

    /// <summary>Nothing at all. The previous run's output is this run's output.</summary>
    Nothing,
}

/// <summary>
/// Decides what a run can skip, and records what it did for the next one.
/// </summary>
/// <remarks>
/// The design and its reasoning are in spec/build-cache.md. Three things about this type are
/// worth knowing before reading it.
///
/// **It keys on inputs, not on the model.** There is already a content hash of the model -
/// <see cref="History.ModelFingerprint"/> - and reusing it here would be wrong. That one
/// answers "is this the same data", which is not "is this the same output": an enum label's
/// comment is absent from it and present in every language's generated code, so a sheet whose
/// only edit was a comment would keep its old code for ever. Keying on inputs needs only that
/// the conversion be deterministic, and does not need a maintained list of everything the
/// output depends on - because that list is what would rot.
///
/// **A miss is always explained.** A tool that decides to do nothing has to say why, and one
/// that decides to do everything has to say why too. Otherwise the cache is either not
/// trusted or not noticed, and both end with `--full` in everybody's script.
///
/// **What is skipped still counts as written.** <see cref="StagingFiles.Sweep"/> deletes
/// generated files this run did not write, so an entry that is skipped has to declare its
/// previous output or the sweep removes precisely the files that were correct.
/// </remarks>
public sealed class BuildCache
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static ILogger Log => LogCategory.Caching;

    /// <summary>Most changed inputs to name before summarising the rest.</summary>
    private const int NamesShown = 5;

    /// <summary>
    /// Stands in for an output's hash until the commit has happened.
    /// </summary>
    /// <remarks>
    /// An output is recorded as its target produces it, which is while it is still in
    /// staging under a name of its own - so there is nothing at the recorded path to measure
    /// until the commit. These two say which of the measurements is still owed, and they are
    /// spellings no hash can take.
    /// </remarks>
    private const string Unmeasured = "?";

    /// <summary>The same, for a target whose output cannot be compared by content.</summary>
    private const string UnmeasuredAndUnhashable = "?!";

    private readonly Options _options;
    private readonly RecipeModel _recipe;
    private readonly string? _sealPath;
    private readonly BuildSeal? _previous;

    private readonly string _tool;
    private readonly string _recipeKey;
    private readonly string _optionsKey;
    private readonly string _validationKey;
    private readonly Dictionary<string, string> _targetKeys;

    private readonly List<SealedOutput> _outputs = [];

    /// <summary>Entries whose previous output stands, by recipe section.</summary>
    private readonly Dictionary<string, List<SealedOutput>> _reusable = [];

    private CachePlan _plan = CachePlan.Everything;

    private BuildCache(
        Options options,
        RecipeModel recipe,
        string? sealPath,
        BuildSeal? previous,
        string tool,
        string recipeKey,
        string optionsKey,
        string validationKey,
        Dictionary<string, string> targetKeys)
    {
        _options = options;
        _recipe = recipe;
        _sealPath = sealPath;
        _previous = previous;
        _tool = tool;
        _recipeKey = recipeKey;
        _optionsKey = optionsKey;
        _validationKey = validationKey;
        _targetKeys = targetKeys;
    }

    /// <summary>What this run read. Sources record into it as they go.</summary>
    public InputLedger Inputs { get; } = new InputLedger();

    /// <summary>Whether a cache is in use at all.</summary>
    public bool Enabled => _sealPath is not null;

    // ------------------------------------------------------------------ opening

    /// <summary>
    /// A cache that decides nothing and records nothing.
    /// </summary>
    /// <remarks>
    /// For the runs that are not conversions - `--validate-only`, `--dump-schema` - and for
    /// the tests that drive a conversion directly. A null object rather than a null, so
    /// nothing downstream has to ask whether there is a cache before telling it something.
    /// </remarks>
    public static BuildCache Off(Options options, RecipeModel recipe)
        => new BuildCache(options, recipe, null, null, "", "", "", "", []);

    /// <summary>
    /// Works out this run's keys and reads what the last one left.
    /// </summary>
    /// <param name="document">
    /// The recipe as parsed, after `${}` substitution. The keys come from this rather than
    /// from <see cref="RecipeModel"/> so that a setting added to the recipe schema later is
    /// covered without anybody adding it here - and so that editing a comment costs nothing,
    /// since the parser has already dropped them.
    /// </param>
    public static BuildCache Open(Options options, RecipeModel recipe, JObject? document)
    {
        // A conversion is the only thing with output to reuse. Validation is a gate and
        // gates do not get to pass on a previous answer; a schema dump is not output.
        if (document is null || options.ValidateOnly || !string.IsNullOrEmpty(options.DumpSchema))
            return Off(options, recipe);

        string tool = Tabbit.ToolVersion.Current;

        // Whether the commit belongs in the key. Asked of the target list rather than
        // hard-coded, so nothing here knows the name of the target that records history.
        var planned = TargetRegistry.Plan(recipe, CommandLineTargetSide.Of(options)).ToList();
        bool withCommit = planned.Any(entry => entry.Descriptor.Kind == TargetKind.Description);

        var targetKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in planned)
            targetKeys[entry.Section ?? ""] = RecipeKeys.Target(entry.Entry, entry.Side);

        string sealPath = SealPath(options);

        return new BuildCache(
            options,
            recipe,
            sealPath,
            BuildSeal.Load(sealPath),
            tool,
            RecipeKeys.Recipe(document),
            RecipeKeys.Options(options, withCommit),
            RecipeKeys.Validation(document, options),
            targetKeys);
    }

    /// <summary>
    /// Where this recipe's seal is kept.
    /// </summary>
    /// <remarks>
    /// One file per recipe, under the naming every per-recipe file of this run's own shares.
    /// The build report sits beside it under the same stem. <see cref="CacheFiles"/>.
    /// </remarks>
    private static string SealPath(Options options) => CacheFiles.PathFor(options, ".seal.json");

    // ----------------------------------------------------------------- deciding

    /// <summary>
    /// Decides what is left to do, and says why.
    /// </summary>
    /// <remarks>
    /// Called before anything is imported. Everything it looks at is either recorded in the
    /// seal or cheap to ask again - file sizes and times, a directory listing, one metadata
    /// call per hosted document - so a run that turns out to have nothing to do costs that
    /// and not a workbook.
    /// </remarks>
    public CachePlan Decide()
    {
        _plan = Determine();

        return _plan;
    }

    private CachePlan Determine()
    {
        if (!Enabled)
            return CachePlan.Everything;

        if (_options.Full)
        {
            Log.Information("Converting everything: --full was asked for.");
            return CachePlan.Everything;
        }

        if (_previous is null)
        {
            Log.Information("Converting everything: nothing is cached for this recipe yet.");
            return CachePlan.Everything;
        }

        if (_previous.Tool != _tool)
        {
            Log.Information(
                $"Converting everything: this cache was written by a different build "
                + $"(`{_previous.Tool}`, now `{_tool}`).");
            return CachePlan.Everything;
        }

        if (_previous.RecipeKey != _recipeKey)
        {
            Log.Information(
                "Converting everything: the recipe changed, outside its `Validation` and `Targets` sections.");
            return CachePlan.Everything;
        }

        if (_previous.OptionsKey != _optionsKey)
        {
            Log.Information("Converting everything: the command line asks for different output than last time.");
            return CachePlan.Everything;
        }

        if (_previous.ValidationKey != _validationKey)
        {
            Log.Information("Converting everything: the validation the recipe asks for changed.");
            return CachePlan.Everything;
        }

        if (!ListingsHold() || !FilesHold() || !RemotesHold())
            return CachePlan.Everything;

        // From here the model is what it was: every input is unchanged, and the conversion
        // is deterministic. What is left to check belongs to the output entries one at a
        // time.
        if (_options.ForceOutput)
        {
            Log.Information("Nothing changed, but --force-output asks for every output entry to run.");
            return CachePlan.ChangedTargetsOnly;
        }

        var stale = StaleTargets();

        if (stale.Count == 0)
        {
            Log.Information(
                $"Nothing to do. {Count(_previous.Files.Count, "input file", "input files")}, "
                + $"{Count(_previous.Listings.Count, "source directory", "source directories")} and "
                + $"{Count(_targetKeys.Count, "output entry", "output entries")} are unchanged.");

            Log.Information(
                $"{_previous.Outputs.Count:N0} output files are intact. Pass --full to convert anyway.");

            return CachePlan.Nothing;
        }

        Log.Information(
            $"Reusing what is unchanged. {Count(stale.Count, "output entry", "output entries")} to run: {string.Join(", ", stale)}.");

        return CachePlan.ChangedTargetsOnly;
    }

    /// <summary>
    /// Output entries that have something to do, even though the model has not changed.
    /// </summary>
    /// <remarks>
    /// Two reasons an entry can be stale while everything upstream of it is not: its own
    /// settings changed, or its files are no longer what it wrote. An entry the recipe no
    /// longer has is not stale - it is gone, and what is left of it is handled by the sweep.
    /// </remarks>
    private List<string> StaleTargets()
    {
        var stale = new List<string>();

        foreach (var (section, key) in _targetKeys.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!_previous!.TargetKeys.TryGetValue(section, out var previousKey) || previousKey != key)
            {
                stale.Add(section);
                continue;
            }

            var produced = _previous.Outputs.Where(output => output.Section == section).ToList();

            // An entry that produced no file has nothing this tool can verify, so it runs.
            // That is what keeps a database export and a history recording out of the
            // skipping - their output is in a store whose state the seal cannot check, and
            // "the seal says it was loaded once" is not the same as "it is loaded now".
            //
            // Derived rather than declared: a target that writes files can be checked and
            // one that does not cannot, and nothing here has to know which is which.
            if (produced.Count == 0)
            {
                stale.Add(section);
                continue;
            }

            if (!OutputsHold(produced))
            {
                stale.Add(section);
                continue;
            }

            _reusable[section] = produced;
        }

        return stale;
    }

    /// <summary>Whether every file an entry wrote is still there and still itself.</summary>
    private static bool OutputsHold(IEnumerable<SealedOutput> outputs)
    {
        foreach (var output in outputs)
        {
            var info = new FileInfo(output.Path);

            if (!info.Exists || info.Length != output.Size)
                return false;

            // No hash recorded means the target does not write the same bytes twice, so
            // there is nothing to compare - existence and size are the whole of what can
            // be asked about it.
            if (output.Hash.Length == 0)
                continue;

            if (ContentHash.OfFile(output.Path) != output.Hash)
                return false;
        }

        return true;
    }

    /// <summary>Whether the source directories hold the same files they did.</summary>
    private bool ListingsHold()
    {
        foreach (var listing in _previous!.Listings)
        {
            if (!Directory.Exists(listing.Root))
            {
                Log.Information($"Converting everything: `{listing.Root}` is not there any more.");
                return false;
            }

            var extensions = SourceFiles.Extensions(listing.Extensions);
            var names = SourceFiles.Candidates(listing.Root, extensions).Select(candidate => candidate.Name);

            string hash = ContentHash.OfNames(names, out int count);

            if (hash == listing.Hash)
                continue;

            string difference = count == listing.Count
                ? $"{Count(count, "file", "files")}, renamed or replaced"
                : $"{listing.Count} files before, {count} now";

            Log.Information($"Converting everything: the files under `{listing.Root}` changed - {difference}.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether every file the previous run read still holds what it held.
    /// </summary>
    /// <remarks>
    /// Size and time first, and a hash only where one of those moved. That second step is
    /// what makes the cache survive a checkout: git writes every file it restores with the
    /// time of the checkout, so a run right after one has thousands of files whose timestamp
    /// says "changed" and whose contents say nothing of the sort. Excel does the same to a
    /// workbook that was opened and saved without an edit.
    /// </remarks>
    private bool FilesHold()
    {
        var changed = new List<string>();
        var gone = new List<string>();

        foreach (var file in _previous!.Files)
        {
            var info = new FileInfo(file.Path);

            if (!info.Exists)
            {
                gone.Add(file.Path);
                continue;
            }

            if (info.Length == file.Size && info.LastWriteTimeUtc.Ticks == file.ModifiedTicks)
                continue;

            if (ContentHash.OfFile(file.Path) == file.Hash)
                continue;

            changed.Add(file.Path);
        }

        if (gone.Count == 0 && changed.Count == 0)
            return true;

        if (changed.Count > 0)
            Log.Information($"Converting everything: {Count(changed.Count, "input file", "input files")} changed{Names(changed)}.");

        if (gone.Count > 0)
            Log.Information($"Converting everything: {Count(gone.Count, "input file", "input files")} no longer there{Names(gone)}.");

        return false;
    }

    /// <summary>
    /// Whether every hosted document is still at the version it was.
    /// </summary>
    /// <remarks>
    /// A document cannot be measured the way a file can, so the source that reads it is
    /// asked for a version instead. A source that cannot answer - the credential is not
    /// allowed to ask, the service is not reachable - does not fail the run: the document is
    /// taken as changed and imported. What the answer costs then is one slow run, and the
    /// source says what would make it fast again.
    /// </remarks>
    private bool RemotesHold()
    {
        if (_previous!.Remotes.Count == 0)
            return true;

        var current = RemoteVersions.Read(_options, _recipe);

        foreach (var remote in _previous.Remotes)
        {
            var found = current.FirstOrDefault(
                version => version.Source == remote.Source && version.Id == remote.Id);

            if (found.Id is null)
            {
                Log.Information(
                    $"Converting everything: the recipe no longer reads `{remote.Id}` from {remote.Source}.");
                return false;
            }

            if (found.Version is null)
            {
                // The source has already said, once, what cannot be read and what to do
                // about it. Repeating it per document would bury it.
                Log.Information($"Converting everything: `{remote.Id}` could not be asked for its version.");
                return false;
            }

            if (found.Version != remote.Version)
            {
                Log.Information($"Converting everything: `{remote.Id}` changed at {remote.Source}.");
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------- running

    /// <summary>
    /// Whether one output entry has to run.
    /// </summary>
    /// <remarks>
    /// Answers true whenever the cache did not conclude that the model is unchanged, which
    /// is every run where anything at all moved upstream of the output.
    ///
    /// A false answer carries an obligation: the entry's previous files have to be declared
    /// to <see cref="StagingFiles"/>, or the sweep deletes them for not having been written
    /// this time. That is done here rather than left to the caller, because a caller that
    /// forgets produces an output directory that empties itself.
    /// </remarks>
    public bool ShouldRun(PlannedTarget planned)
    {
        if (_plan != CachePlan.ChangedTargetsOnly || _options.ForceOutput)
            return true;

        if (!_reusable.TryGetValue(planned.Section ?? "", out var produced))
            return true;

        foreach (var output in produced)
            StagingFiles.Keep(output.Path);

        // Carried into this run's seal unchanged: the files are still there and still
        // hashed the same, so re-measuring them would produce the same three numbers.
        _outputs.AddRange(produced);

        Log.Information(
            $"`{planned.Section}` ({planned.Descriptor.Id}) is unchanged; keeping its "
            + $"{Count(produced.Count, "file", "files")}.");

        return false;
    }

    /// <summary>Notes what one output entry produced.</summary>
    /// <param name="deterministic">
    /// Whether the entry's target writes the same bytes for the same model. False leaves the
    /// files recorded without a hash, so a later run checks that they exist and no more.
    /// </param>
    public void Wrote(string section, IReadOnlyList<string> paths, bool deterministic)
    {
        if (!Enabled)
            return;

        foreach (var path in paths)
        {
            _outputs.Add(new SealedOutput
            {
                Section = section,
                Path = path,
                Size = 0,
                Hash = deterministic ? Unmeasured : UnmeasuredAndUnhashable,
            });
        }
    }

    // ------------------------------------------------------------------- sealing

    /// <summary>
    /// Records what this run read and wrote, for the next one.
    /// </summary>
    /// <remarks>
    /// After the commit, because until then the output is in staging and the files whose
    /// contents are being recorded are not at the paths being recorded.
    ///
    /// Nothing here can fail the run. The conversion has already produced everything it was
    /// asked for, and a seal that could not be written costs the next run its shortcut and
    /// nothing else.
    /// </remarks>
    public void Seal()
    {
        if (!Enabled || _plan == CachePlan.Nothing)
            return;

        try
        {
            var seal = new BuildSeal
            {
                Tool = _tool,
                Recipe = Path.GetFullPath(_options.RecipeFilename!),
                WrittenUtc = DateTime.UtcNow,
                RecipeKey = _recipeKey,
                OptionsKey = _optionsKey,
                ValidationKey = _validationKey,
                TargetKeys = new Dictionary<string, string>(_targetKeys, StringComparer.Ordinal),
                Files = Inputs.Files(),
                Listings = Inputs.Listings(),
                Remotes = Inputs.Remotes(),
                Outputs = Measured(),
                SweepRoots = SweepRoots(),
            };

            seal.Save(_sealPath!);

            Log.Debug(
                $"Sealed {seal.Files.Count:N0} input file(s), {seal.Listings.Count:N0} listing(s) "
                + $"and {seal.Outputs.Count:N0} output file(s) into `{_sealPath}`.");
        }
        catch (Exception ex)
        {
            Log.Warning(Messages.Message.Of(Cooking.CookingMessages.LogCacheSealUnwritable,
                ("Path", _sealPath), ("Detail", ex.Message)).In(Messages.MessageCatalog.Current));
        }
    }

    /// <summary>
    /// Where stale output is to be removed from, for the next run.
    /// </summary>
    /// <remarks>
    /// This run's roots together with the previous seal's. An entry this run skipped never
    /// declared its own, and an entry the recipe no longer has still left files behind that
    /// somebody will want removed - so forgetting a root is how an output directory comes to
    /// hold a generated file nothing will ever delete.
    /// </remarks>
    private List<string> SweepRoots()
    {
        var roots = new List<string>(StagingFiles.DeclaredSweepRoots);

        foreach (var root in _previous?.SweepRoots ?? [])
        {
            if (!roots.Contains(root, PathNames.Comparer))
                roots.Add(root);
        }

        return roots;
    }

    /// <summary>
    /// Removes generated files that are no longer produced, on a run that produced nothing.
    /// </summary>
    /// <remarks>
    /// A run deciding it has nothing to do still owes this. The alternative is that whether
    /// a stale file is removed depends on whether anything happened to change that day, and
    /// a file that names a table nothing declares any more does not become harmless for
    /// having survived a quiet week.
    ///
    /// The permission is what it always is: a file is removed only if it is under a
    /// directory a target asked to have swept and says in its own header that this tool
    /// wrote it. What stands in for "this run wrote it" is the seal's output list, which is
    /// the same set of files this run has just verified.
    /// </remarks>
    public void SweepUnchanged()
    {
        if (!Enabled || _previous is null || _previous.SweepRoots.Count == 0)
            return;

        foreach (var root in _previous.SweepRoots)
            StagingFiles.SweepDirectory(root);

        var written = new HashSet<string>(
            _previous.Outputs.Select(output => output.Path), PathNames.Comparer);

        var removed = StagingFiles.Sweep(written);

        if (removed.Count == 0)
            return;

        Log.Information(
            $"Removed {Count(removed.Count, "generated file", "generated files")} that this recipe "
            + $"no longer produces{Names(removed.ToList())}.");
    }

    /// <summary>
    /// The output list with its sizes and hashes filled in.
    /// </summary>
    /// <remarks>
    /// A file that is not there is dropped rather than recorded as missing. It means
    /// something outside this run removed it between the commit and now, and recording it
    /// as absent would make the next run report a missing file that this run never had.
    /// </remarks>
    private List<SealedOutput> Measured()
    {
        var measured = new List<SealedOutput>(_outputs.Count);

        foreach (var output in _outputs)
        {
            bool hashable = output.Hash == Unmeasured;

            // Carried over from an entry this run skipped: the file is where it was and
            // hashed the same, so measuring it again would produce the same three numbers.
            if (!hashable && output.Hash != UnmeasuredAndUnhashable)
            {
                measured.Add(output);
                continue;
            }

            var info = new FileInfo(output.Path);

            if (!info.Exists)
                continue;

            measured.Add(new SealedOutput
            {
                Section = output.Section,
                Path = output.Path,
                Size = info.Length,
                Hash = hashable ? ContentHash.OfFile(output.Path) : "",
            });
        }

        return measured;
    }

    // -------------------------------------------------------------------- saying

    /// <summary>
    /// A count with its noun, singular where that is what it is.
    /// </summary>
    /// <remarks>
    /// The plural is passed rather than made by adding an `s`, because the nouns this counts
    /// include "output entry" and "source directory".
    /// </remarks>
    private static string Count(int how, string one, string many)
        => how == 1 ? $"1 {one}" : $"{how:N0} {many}";

    /// <summary>
    /// The first few names in a list, so a message points at something.
    /// </summary>
    /// <remarks>
    /// A few rather than all of them: a checkout of a branch that touched two hundred
    /// workbooks is one fact, and two hundred lines saying it is not more informative than
    /// five and a number.
    /// </remarks>
    private static string Names(List<string> paths)
    {
        var shown = paths.Take(NamesShown).Select(Path.GetFileName);

        return paths.Count <= NamesShown
            ? $" ({string.Join(", ", shown)})"
            : $" ({string.Join(", ", shown)} and {paths.Count - NamesShown:N0} more)";
    }
}
