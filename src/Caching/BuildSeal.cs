using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Tabbit.Caching;

/// <summary>One input file, as it was when the sealed run read it.</summary>
public sealed class SealedFile
{
    public string Path { get; set; } = "";

    public long Size { get; set; }

    /// <summary>
    /// Last write time, in UTC ticks.
    /// </summary>
    /// <remarks>
    /// Compared first, and on its own it decides nothing: a file whose size and time both
    /// match is taken as unchanged, and one where either moved is hashed. A checkout sets
    /// every file's time to now, so a cache that stopped at the timestamp would be a cache
    /// that never survived one.
    /// </remarks>
    public long ModifiedTicks { get; set; }

    public string Hash { get; set; } = "";
}

/// <summary>Which files were under a directory when the sealed run looked.</summary>
/// <remarks>
/// A file being added changes no existing file, so a list of files cannot notice it. This
/// is what does.
/// </remarks>
public sealed class SealedListing
{
    public string Root { get; set; } = "";

    /// <summary>Extensions the listing was taken with, as the recipe wrote them.</summary>
    public string Extensions { get; set; } = "";

    public string Hash { get; set; } = "";

    public int Count { get; set; }
}

/// <summary>
/// An input that is not a file, and says its own version.
/// </summary>
/// <remarks>
/// A hosted document has no size and no modification time this tool can read from a
/// filesystem. What it has is a version the service reports, and comparing that is the
/// same question asked of something we cannot open.
/// </remarks>
public sealed class SealedRemote
{
    /// <summary>Which source this belongs to, by its registered id.</summary>
    public string Source { get; set; } = "";

    /// <summary>The document's identifier, as the recipe names it.</summary>
    public string Id { get; set; } = "";

    /// <summary>Whatever the service calls its version. Opaque to this tool.</summary>
    public string Version { get; set; } = "";
}

/// <summary>One file the sealed run wrote, and which recipe entry wrote it.</summary>
public sealed class SealedOutput
{
    /// <summary>Recipe entry that produced it - `Targets[3]`.</summary>
    public string Section { get; set; } = "";

    public string Path { get; set; } = "";

    public long Size { get; set; }

    /// <summary>
    /// Empty when the target that wrote it does not produce the same bytes twice.
    /// </summary>
    /// <remarks>
    /// A target that stamps the time of the run into its output cannot be verified by
    /// content, because the content is supposed to differ. Existence is all that can be
    /// asked, and the target says which of the two it is - see
    /// <see cref="Targets.TabbitTargetAttribute.Deterministic"/>.
    /// </remarks>
    public string Hash { get; set; } = "";
}

/// <summary>
/// What a previous run read, produced, and was asked to do.
/// </summary>
/// <remarks>
/// The whole point is the comparison, so the parts are kept apart rather than folded into
/// one number. A single hash of everything answers "something changed" and nothing else,
/// and a cache that cannot say which workbook changed is a cache nobody believes -
/// spec/build-cache.md §7.
///
/// Machine-local by construction: the paths are absolute, because a run's inputs and
/// outputs are where they are and resolving them against a working directory recorded
/// somewhere else is one more thing that can disagree. That is also why this file is not
/// something a checkout should carry.
/// </remarks>
public sealed class BuildSeal
{
    /// <summary>
    /// Layout of this file.
    /// </summary>
    /// <remarks>
    /// A seal written by a different layout is discarded rather than migrated. It describes
    /// one run that has already happened, and the cost of throwing it away is that run
    /// happening once more.
    /// </remarks>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Which build of this tool wrote it.</summary>
    public string Tool { get; set; } = "";

    /// <summary>The recipe this is about, absolute.</summary>
    public string Recipe { get; set; } = "";

    /// <summary>When it was written, for a person reading the file.</summary>
    public DateTime WrittenUtc { get; set; }

    /// <summary>
    /// The recipe's settings other than validation and the output entries.
    /// </summary>
    public string RecipeKey { get; set; } = "";

    /// <summary>The options that change what the conversion produces.</summary>
    public string OptionsKey { get; set; } = "";

    /// <summary>The validation section and the switches that narrow it.</summary>
    public string ValidationKey { get; set; } = "";

    /// <summary>Each output entry's own settings, by recipe section.</summary>
    public Dictionary<string, string> TargetKeys { get; set; } = [];

    public List<SealedFile> Files { get; set; } = [];

    public List<SealedListing> Listings { get; set; } = [];

    public List<SealedRemote> Remotes { get; set; } = [];

    public List<SealedOutput> Outputs { get; set; } = [];

    /// <summary>
    /// Directories the sealed run was asked to remove its stale output from.
    /// </summary>
    /// <remarks>
    /// Recorded because a run that skips everything never reaches a target, and a target is
    /// the only thing that knows where its output goes. Without these, the one thing a
    /// skipped run still has to do - remove a generated file that is no longer produced -
    /// could not be done at all.
    /// </remarks>
    public List<string> SweepRoots { get; set; } = [];

    /// <summary>
    /// Reads a seal, or null when there is none to read.
    /// </summary>
    /// <remarks>
    /// A file that cannot be parsed is treated as absent rather than as an error. Two runs
    /// can write this at once, and a half-written seal is a reason to do the work again -
    /// not a reason to refuse to.
    /// </remarks>
    public static BuildSeal? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var seal = JsonConvert.DeserializeObject<BuildSeal>(File.ReadAllText(path));

            return seal is null || seal.Version != CurrentVersion ? null : seal;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the seal, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// Through a temporary file in the same directory and then a move, so a reader never
    /// sees a partial one. Two conversions of one recipe at once is an ordinary thing on a
    /// build machine, and the loser of that race should leave a seal that describes its own
    /// run rather than half of each.
    ///
    /// A failure to write is not a failure of the run. The conversion has already produced
    /// everything it was asked for; all that is lost is the next run's chance to skip.
    /// </remarks>
    public void Save(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporary = path + "." + Environment.ProcessId + ".tmp";

        File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented));
        File.Move(temporary, path, overwrite: true);
    }
}
