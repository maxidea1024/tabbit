using System.Collections.Generic;
using CommandLine;

namespace Tabbit;

public class Options
{
    [Option('r', "recipe", HelpText = "Recipe file.")]
    public string? RecipeFilename { get; set; }

    /// <summary>
    /// Writes a starting recipe and exits.
    ///
    /// Every list comes out holding one entry with its defaults filled in, so the file
    /// shows what each target takes rather than only that the section exists.
    /// </summary>
    [Option("new-recipe", HelpText = "Write a starting recipe file and exit.")]
    public string? NewRecipeFilename { get; set; }

    /// <summary>
    /// Which starting recipe `--new-recipe` writes.
    /// </summary>
    /// <remarks>
    /// Left out, the file holds every setting at its default. That answers what a target
    /// takes and not what to write for a given situation - and a page holding forty options
    /// at their defaults is its own kind of blank page.
    ///
    /// A template is a recipe for a situation, carrying the settings that situation needs
    /// and a comment on each saying what it is for.
    /// </remarks>
    [Option("template", HelpText =
        "Which starting recipe --new-recipe writes. Omit for one holding every setting.")]
    public string? RecipeTemplate { get; set; }

    /// <summary>
    /// Narrows the whole run to one side of the data.
    ///
    /// Two things follow from it. Output entries built for the other side are skipped,
    /// and the entries that do run see only the tables, columns and rows that belong to
    /// the requested side - so `--target-side server` on a recipe whose entries are
    /// marked `cs` produces the server cut of that output rather than everything.
    ///
    /// Left out, the run is not narrowed at all and each entry is built for whatever
    /// side it declares, which is what happened before this option existed.
    /// </summary>
    [Option("target-side",
        HelpText = "Narrow the run to one side: `client`, `server`, or `both` (the default).")]
    public string? TargetSide { get; set; }

    /// <summary>
    /// The commit this conversion is of.
    ///
    /// What the history files a snapshot under, and what a range query names. Left out,
    /// it is read from the working copy the sheets are in - so a developer converting
    /// locally needs nothing, while a CI job that checked out a detached HEAD can say
    /// exactly which commit it built.
    ///
    /// Not required to be a git hash. A project keeping its sheets somewhere without
    /// commits can pass any stable identifier and the history treats it as opaque.
    /// </summary>
    [Option("commit", HelpText = "Commit this conversion is of. Read from git when left out.")]
    public string? Commit { get; set; }

    /// <summary>
    /// Branch this snapshot belongs to.
    ///
    /// Snapshots are chained per branch, so this decides which history the conversion
    /// extends. Read from the working copy when left out - but a detached HEAD, which
    /// is what most CI checkouts produce, is not a branch and yields nothing.
    /// </summary>
    [Option("branch", HelpText = "Branch this snapshot belongs to. Read from git when left out.")]
    public string? Branch { get; set; }

    /// <summary>
    /// Who made the change, as `Name &lt;email&gt;`.
    ///
    /// For the build systems that know the author without a git checkout to read it
    /// from. Overrides what the commit says.
    /// </summary>
    [Option("commit-author", HelpText = "Author of the change, as `Name <email>`. Overrides git.")]
    public string? CommitAuthor { get; set; }

    /// <summary>When the change was made, as an ISO 8601 timestamp. Overrides git.</summary>
    [Option("commit-date", HelpText = "When the change was made, ISO 8601. Overrides git.")]
    public string? CommitDate { get; set; }

    /// <summary>
    /// Working copy to read commit information from.
    ///
    /// Left out, the sheets' own source directories are tried and then the working
    /// directory. Given, it is the only place looked at: falling through to somewhere
    /// else would record another repository's commits against this data.
    /// </summary>
    [Option("repository", HelpText = "Working copy to read commit information from.")]
    public string? Repository { get; set; }

    // ------------------------------------------------------------- querying

    /// <summary>
    /// Reads the history back instead of converting: who changed what, between two
    /// commits.
    ///
    /// The recipe is still needed, because that is where the history's address and
    /// project name are - reading them from a second place is how the two come to
    /// disagree.
    /// </summary>
    [Option("history", HelpText = "Report what changed between two commits, and exit.")]
    public bool History { get; set; }

    /// <summary>Reports the statistics of one commit instead of converting.</summary>
    [Option("stats", HelpText = "Report the statistics of a commit, and exit.")]
    public bool Stats { get; set; }

    /// <summary>
    /// The commit a range starts after.
    ///
    /// Exclusive: it is the state being compared from, so its own changes belong to the
    /// range before this one. Left out, the range starts at the branch's first snapshot.
    /// </summary>
    [Option("from", HelpText = "Commit the range starts after. Exclusive.")]
    public string? From { get; set; }

    /// <summary>The commit a range ends at, inclusive. Left out, the branch's head.</summary>
    [Option("to", HelpText = "Commit the range ends at. Inclusive.")]
    public string? To { get; set; }

    /// <summary>Which commit `--stats` describes. Left out, the branch's head.</summary>
    [Option("at", HelpText = "Commit to report statistics for. The head when left out.")]
    public string? At { get; set; }

    /// <summary>Narrows a report to one table.</summary>
    [Option("table", HelpText = "Only report changes to this table.")]
    public string? Table { get; set; }

    /// <summary>Narrows a report to one column.</summary>
    [Option("field", HelpText = "Only report changes to this column.")]
    public string? Field { get; set; }

    /// <summary>Narrows a report to one person, by name or address.</summary>
    [Option("author", HelpText = "Only report changes by this person.")]
    public string? Author { get; set; }

    /// <summary>
    /// Which project's history to read, when the recipe's entry is not the one wanted.
    /// </summary>
    [Option("project", HelpText = "Project whose history to read. From the recipe when left out.")]
    public string? Project { get; set; }

    /// <summary>How to render a report: `json` or `text`.</summary>
    [Option("format", HelpText = "Report format: `json` or `text`.")]
    public string? Format { get; set; }

    /// <summary>Where to write a report. Standard output when left out.</summary>
    [Option("out", HelpText = "File to write the report to. Standard output when left out.")]
    public string? Out { get; set; }

    /// <summary>
    /// The most changes a report will carry.
    ///
    /// A range over a busy month is hundreds of thousands of cells. What is cut is
    /// reported as cut rather than left to be noticed.
    /// </summary>
    [Option("limit", HelpText = "Most changes to report. Anything cut is reported as cut.")]
    public int Limit { get; set; }

    /// <summary>
    /// Removes the change detail of old snapshots, and collects the values that then
    /// refer to nothing.
    ///
    /// The change log is what grows without bound - one row per edited cell per commit,
    /// for ever. A pruned snapshot keeps its row, its statistics and its stored summary;
    /// only the cell-by-cell detail goes, and a query over a range holding one says so.
    /// </summary>
    [Option("prune", HelpText = "Remove the change detail of old snapshots, and exit.")]
    public bool Prune { get; set; }

    /// <summary>
    /// What counts as old: an ISO 8601 date, or an age such as `90d`.
    ///
    /// An age is what a scheduled job wants. A date would have to be recomputed by
    /// whatever runs it, and one that is not is a job that prunes nothing after the
    /// first time.
    /// </summary>
    [Option("before", HelpText = "Prune snapshots older than this: a date, or an age like `90d`.")]
    public string? Before { get; set; }

    /// <summary>
    /// How many of the branch's most recent snapshots to leave alone whatever their age.
    ///
    /// A floor under `--before` rather than an alternative to it: a branch nobody has
    /// touched for a year would otherwise lose every snapshot's detail and become a
    /// history with no history in it.
    /// </summary>
    [Option("keep", Default = 100, HelpText = "Most recent snapshots to leave alone. 100 by default.")]
    public int Keep { get; set; }

    // -------------------------------------------------------------- serving

    /// <summary>
    /// Puts the history behind an HTTP API and a page, and stays running.
    ///
    /// Read-only: the server never writes, and the account in the recipe need not be
    /// able to.
    /// </summary>
    [Option("serve", HelpText = "Serve the history over HTTP and stay running.")]
    public bool Serve { get; set; }

    /// <summary>Port to listen on. 8080 when left out.</summary>
    [Option("port", HelpText = "Port to serve on. 8080 when left out.")]
    public int Port { get; set; }

    /// <summary>
    /// Address to listen on. Loopback when left out.
    ///
    /// Anything else needs a token in TABBIT_SERVE_TOKEN, and is refused without one:
    /// what an open port exposes here is every value in the project's design data and
    /// the name of everyone who touched it.
    /// </summary>
    [Option("bind", HelpText = "Address to serve on. 127.0.0.1 when left out.")]
    public string? Bind { get; set; }

    // ----------------------------------------------------------- validation

    /// <summary>
    /// Validates and stops, without running any output target.
    ///
    /// What a pull request check wants: the answer is the exit code, and nothing is
    /// written or loaded anywhere. There is no matching option to skip validation - a
    /// gate that can be turned off from a command line is a gate nobody can rely on, and
    /// clearing `Validation.Path` in the recipe is the deliberate, reviewable way to do it.
    /// </summary>
    [Option("validate-only", HelpText = "Validate and exit, without running any output target.")]
    public bool ValidateOnly { get; set; }

    /// <summary>
    /// Writes a starting rule file for one table and exits.
    ///
    /// The header of a rule file is two lines that exist only for the IDE, and the run neither
    /// needs nor reads them - so having a command write them is what makes them worth having at
    /// all. Refuses to overwrite a file that is already there.
    /// </summary>
    [Option("new-validator", HelpText = "Write a starting validation rule for this table, and exit.")]
    public string? NewValidator { get; set; }

    /// <summary>
    /// Prints the rules in the order they would run, and exits.
    /// </summary>
    /// <remarks>
    /// What a listing of tiers is for. Priority is declared on the rule itself, which keeps it from
    /// disagreeing with the rule it is about but leaves the whole order in no one place - so this
    /// prints it, and unlike a file that lists the order, what it prints is what runs.
    /// </remarks>
    [Option("list-validators", HelpText = "Print the validation rules in the order they run, and exit.")]
    public bool ListValidators { get; set; }

    /// <summary>
    /// Skips the `rules/runtime/` rules, which are the ones that reach a database or a cache.
    ///
    /// For a machine that has no access to those stores. The rules that read only the
    /// sheets are unaffected, and the run reports how many rules were skipped rather than
    /// leaving that to be noticed.
    /// </summary>
    [Option("skip-runtime-validation", HelpText = "Skip the validation rules that read an external store.")]
    public bool SkipRuntimeValidation { get; set; }

    // ----------------------------------------------------------- encryption

    /// <summary>
    /// Writes a new encryption key and exits.
    /// </summary>
    /// <remarks>
    /// Here rather than left to whoever needs one, because the ways a key is usually
    /// improvised are the ways that make it not a key: a passphrase, a hash of something
    /// memorable, a shortened run of hexadecimal. This draws the bytes from the operating
    /// system's random source and prints them in the one form the recipe accepts.
    ///
    /// To standard output unless `--out` names a file, so it can go straight into a secret
    /// store without ever being a file on disk. A file that is already there is not
    /// overwritten: replacing a key silently would leave every file written with the old one
    /// unreadable, with nothing to say why.
    /// </remarks>
    [Option("new-encryption-key", HelpText =
        "Write a new encryption key and exit. To --out, or to standard output.")]
    public bool NewEncryptionKey { get; set; }

    [Option("verbose", HelpText = "Sets whether to output debugging log messages.")]
    public bool Verbose { get; set; }

    [Option("silent", HelpText = "Suppress all logging message except ERROR/FATAL.")]
    public bool Silent { get; set; }
    
    [Option("debug", HelpText = "Enables or disables internal debugging.")]
    public bool Debugging { get; set; }
}
