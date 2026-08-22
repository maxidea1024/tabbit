using System.Collections.Generic;
using CommandLine;
using Tabbit.Caching;

namespace Tabbit;

public class Options
{
    [Cache(CacheRelevance.Identity)]
    [Option('r', "recipe", HelpText = "Recipe file.")]
    public string? RecipeFilename { get; set; }

    /// <summary>
    /// Writes a starting recipe and exits.
    ///
    /// Every list comes out holding one entry with its defaults filled in, so the file
    /// shows what each target takes rather than only that the section exists.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
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
    [Cache(CacheRelevance.NotAConversion)]
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
    [Cache(CacheRelevance.Output)]
    [Option("target-side",
        HelpText = "Narrow the run to one side: `client`, `server`, or `both` (the default).")]
    public string? TargetSide { get; set; }

    /// <summary>
    /// Which time zone every sheet's `datetime` cells were written in, overriding whatever
    /// the recipe says - both its own setting and each source entry's.
    /// </summary>
    /// <remarks>
    /// Forced rather than a default, because what this is for is a run whose recipe is
    /// wrong about it: sheets that turn out to have been written to another office's clock,
    /// or a re-conversion of an archive from before the recipe said anything. A default
    /// would be overridden by the very entries that are wrong.
    ///
    /// Takes what the recipe takes - a zone name or a fixed offset - and is reported once at
    /// the start of the run, because a flag that moves every date in the output should not be
    /// something a reader has to reconstruct from the command line.
    /// </remarks>
    [Cache(CacheRelevance.Output)]
    [Option("time-zone", HelpText =
        "Time zone the sheets' dates were written in, forced over the recipe: "
        + "`Asia/Seoul` or `+09:00`.")]
    public string? TimeZone { get; set; }

    /// <summary>
    /// Which environment this run is for.
    /// </summary>
    /// <remarks>
    /// Two things at once, and on purpose. It is recorded in the summary, so a build can
    /// be told from its output rather than from whoever remembers launching it; and it
    /// becomes `TABBIT_ENV` for the recipe's `${NAME}` substitution, so the paths a run
    /// reads and writes come from the same word that labels it.
    ///
    /// Splitting those - a flag for the label and a variable for the paths - is a pair
    /// that can disagree, and the disagreement is the failure worth preventing: output
    /// stamped `live` that was built from the development sheets says the opposite of
    /// what happened. A `TABBIT_ENV` already set to something else is refused rather
    /// than overwritten.
    /// </remarks>
    [Cache(CacheRelevance.Output)]
    [Option("env", HelpText =
        "Environment this run is for. Recorded in the summary, and available as ${TABBIT_ENV}.")]
    public string? EnvironmentName { get; set; }

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
    [Cache(CacheRelevance.Commit)]
    [Option("commit", HelpText = "Commit this conversion is of. Read from git when left out.")]
    public string? Commit { get; set; }

    /// <summary>
    /// Branch this snapshot belongs to.
    ///
    /// Snapshots are chained per branch, so this decides which history the conversion
    /// extends. Read from the working copy when left out - but a detached HEAD, which
    /// is what most CI checkouts produce, is not a branch and yields nothing.
    /// </summary>
    [Cache(CacheRelevance.Commit)]
    [Option("branch", HelpText = "Branch this snapshot belongs to. Read from git when left out.")]
    public string? Branch { get; set; }

    /// <summary>
    /// Who made the change, as `Name &lt;email&gt;`.
    ///
    /// For the build systems that know the author without a git checkout to read it
    /// from. Overrides what the commit says.
    /// </summary>
    [Cache(CacheRelevance.Commit)]
    [Option("commit-author", HelpText = "Author of the change, as `Name <email>`. Overrides git.")]
    public string? CommitAuthor { get; set; }

    /// <summary>When the change was made, as an ISO 8601 timestamp. Overrides git.</summary>
    [Cache(CacheRelevance.Commit)]
    [Option("commit-date", HelpText = "When the change was made, ISO 8601. Overrides git.")]
    public string? CommitDate { get; set; }

    /// <summary>
    /// Working copy to read commit information from.
    ///
    /// Left out, the sheets' own source directories are tried and then the working
    /// directory. Given, it is the only place looked at: falling through to somewhere
    /// else would record another repository's commits against this data.
    /// </summary>
    [Cache(CacheRelevance.Commit)]
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
    [Cache(CacheRelevance.NotAConversion)]
    [Option("history", HelpText = "Report what changed between two commits, and exit.")]
    public bool History { get; set; }

    /// <summary>Reports the statistics of one commit instead of converting.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("stats", HelpText = "Report the statistics of a commit, and exit.")]
    public bool Stats { get; set; }

    /// <summary>
    /// The commit a range starts after.
    ///
    /// Exclusive: it is the state being compared from, so its own changes belong to the
    /// range before this one. Left out, the range starts at the branch's first snapshot.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("from", HelpText = "Commit the range starts after. Exclusive.")]
    public string? From { get; set; }

    /// <summary>The commit a range ends at, inclusive. Left out, the branch's head.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("to", HelpText = "Commit the range ends at. Inclusive.")]
    public string? To { get; set; }

    /// <summary>Which commit `--stats` describes. Left out, the branch's head.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("at", HelpText = "Commit to report statistics for. The head when left out.")]
    public string? At { get; set; }

    /// <summary>Narrows a report to one table.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("table", HelpText = "Only report changes to this table.")]
    public string? Table { get; set; }

    /// <summary>Narrows a report to one column.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("field", HelpText = "Only report changes to this column.")]
    public string? Field { get; set; }

    /// <summary>Narrows a report to one person, by name or address.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("author", HelpText = "Only report changes by this person.")]
    public string? Author { get; set; }

    /// <summary>
    /// Which project's history to read, when the recipe's entry is not the one wanted.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("project", HelpText = "Project whose history to read. From the recipe when left out.")]
    public string? Project { get; set; }

    /// <summary>How to render a report: `json` or `text`.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("format", HelpText = "Report format: `json` or `text`.")]
    public string? Format { get; set; }

    /// <summary>Where to write a report. Standard output when left out.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("out", HelpText = "File to write the report to. Standard output when left out.")]
    public string? Out { get; set; }

    /// <summary>
    /// The most changes a report will carry.
    ///
    /// A range over a busy month is hundreds of thousands of cells. What is cut is
    /// reported as cut rather than left to be noticed.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
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
    [Cache(CacheRelevance.NotAConversion)]
    [Option("prune", HelpText = "Remove the change detail of old snapshots, and exit.")]
    public bool Prune { get; set; }

    /// <summary>
    /// What counts as old: an ISO 8601 date, or an age such as `90d`.
    ///
    /// An age is what a scheduled job wants. A date would have to be recomputed by
    /// whatever runs it, and one that is not is a job that prunes nothing after the
    /// first time.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("before", HelpText = "Prune snapshots older than this: a date, or an age like `90d`.")]
    public string? Before { get; set; }

    /// <summary>
    /// How many of the branch's most recent snapshots to leave alone whatever their age.
    ///
    /// A floor under `--before` rather than an alternative to it: a branch nobody has
    /// touched for a year would otherwise lose every snapshot's detail and become a
    /// history with no history in it.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("keep", Default = 100, HelpText = "Most recent snapshots to leave alone. 100 by default.")]
    public int Keep { get; set; }

    // -------------------------------------------------------------- serving

    /// <summary>
    /// Puts the history behind an HTTP API and a page, and stays running.
    ///
    /// Read-only: the server never writes, and the account in the recipe need not be
    /// able to.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("serve", HelpText = "Serve the history over HTTP and stay running.")]
    public bool Serve { get; set; }

    /// <summary>Port to listen on. 8080 when left out.</summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("port", HelpText = "Port to serve on. 8080 when left out.")]
    public int Port { get; set; }

    /// <summary>
    /// Address to listen on. Loopback when left out.
    ///
    /// Anything else needs a token in TABBIT_SERVE_TOKEN, and is refused without one:
    /// what an open port exposes here is every value in the project's design data and
    /// the name of everyone who touched it.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("bind", HelpText = "Address to serve on. 127.0.0.1 when left out.")]
    public string? Bind { get; set; }

    /// <summary>
    /// Writes where each table sits in its sheet, and stops.
    /// </summary>
    /// <remarks>
    /// For the tools that read the same workbooks this does but have no business cooking
    /// them - a comparison, a merge. What they need is the geometry: which rectangle of which
    /// sheet is a table, and which of its columns identifies a row. Working that out needs
    /// every workbook of the source open at once, because a column's type may name an enum
    /// declared in another one, so it cannot be worked out from the one file being looked at.
    ///
    /// A file rather than a library, so nothing has to link against this program to use it.
    /// </remarks>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("dump-schema", HelpText =
        "Write where each table sits in its sheet as JSON, and exit. For tools that read the "
        + "same workbooks without cooking them.")]
    public string? DumpSchema { get; set; }

    // ----------------------------------------------------------- validation

    /// <summary>
    /// Validates and stops, without running any output target.
    ///
    /// What a pull request check wants: the answer is the exit code, and nothing is
    /// written or loaded anywhere. There is no matching option to skip validation - a
    /// gate that can be turned off from a command line is a gate nobody can rely on, and
    /// clearing `Validation.Path` in the recipe is the deliberate, reviewable way to do it.
    /// </summary>
    [Cache(CacheRelevance.Control)]
    [Option("validate-only", HelpText = "Validate and exit, without running any output target.")]
    public bool ValidateOnly { get; set; }

    /// <summary>
    /// Opens the report the last run of this recipe left, and exits.
    ///
    /// The other half of writing the report to a fixed path. "Where was that report" has one
    /// answer per recipe, so it can be a flag rather than a path somebody has to have kept -
    /// and the report of a run that failed is the one most likely to be wanted again later.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
    [Option("show-report", HelpText = "Open the last build report for this recipe, and exit.")]
    public bool ShowReport { get; set; }

    /// <summary>
    /// Writes a starting rule file for one table and exits.
    ///
    /// The header of a rule file is two lines that exist only for the IDE, and the run neither
    /// needs nor reads them - so having a command write them is what makes them worth having at
    /// all. Refuses to overwrite a file that is already there.
    /// </summary>
    [Cache(CacheRelevance.NotAConversion)]
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
    [Cache(CacheRelevance.NotAConversion)]
    [Option("list-validators", HelpText = "Print the validation rules in the order they run, and exit.")]
    public bool ListValidators { get; set; }

    /// <summary>
    /// Skips the `rules/runtime/` rules, which are the ones that reach a database or a cache.
    ///
    /// For a machine that has no access to those stores. The rules that read only the
    /// sheets are unaffected, and the run reports how many rules were skipped rather than
    /// leaving that to be noticed.
    /// </summary>
    [Cache(CacheRelevance.Validation)]
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
    [Cache(CacheRelevance.NotAConversion)]
    [Option("new-encryption-key", HelpText =
        "Write a new encryption key and exit. To --out, or to standard output.")]
    public bool NewEncryptionKey { get; set; }

    // -------------------------------------------------------------- caching

    /// <summary>
    /// Converts everything, without consulting what a previous run recorded.
    /// </summary>
    /// <remarks>
    /// For the case where the cache is what is suspected. Every way of deciding that an
    /// input is unchanged has a state it cannot tell apart from unchanged - a file restored
    /// with its old size and its old timestamp, a hosted document whose version did not
    /// move - and this is the answer to all of them at once, without anybody having to work
    /// out which one they are in.
    ///
    /// The seal is still written. A run that did all the work has the most accurate record
    /// of what the inputs were, and discarding it would mean the next run has nothing
    /// either.
    /// </remarks>
    [Cache(CacheRelevance.Control)]
    [Option("full", HelpText = "Convert everything, ignoring what the cache says.")]
    public bool Full { get; set; }

    /// <summary>
    /// Runs every output entry whatever the cache says, while still trusting it about the
    /// inputs.
    /// </summary>
    /// <remarks>
    /// A different question from <see cref="Full"/>, which is why it is a different option.
    /// `--full` doubts the cache; this one believes it and wants the output anyway - because
    /// something outside this tool reads those files and has its own reasons, or because a
    /// directory was moved by hand and the recipe that describes it did not change.
    ///
    /// Keeping them apart costs one option and saves the time of reading every source again,
    /// which is half of a run. spec/build-cache.md §7.1.
    /// </remarks>
    [Cache(CacheRelevance.Control)]
    [Option("force-output", HelpText = "Run every output entry, whatever the cache says.")]
    public bool ForceOutput { get; set; }

    /// <summary>
    /// Where the build cache is kept. `.tabbit/` beside the working directory when left out.
    /// </summary>
    /// <remarks>
    /// Named for the build machines, where the working directory is thrown away between
    /// jobs and the cache has to be somewhere that is mounted. It holds absolute paths and
    /// the state of one machine's files, so it is not something a checkout should carry.
    /// </remarks>
    [Cache(CacheRelevance.Control)]
    [Option("cache-dir", HelpText = "Where to keep the build cache. `.tabbit/` when left out.")]
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Tells a run that had nothing to do apart from one that converted, by exit code.
    /// </summary>
    /// <remarks>
    /// For the build pipelines whose next step is a publish. "The conversion succeeded" does
    /// not say whether there is anything new to publish, and working it out from the log is
    /// parsing English out of a tool that could simply have said so.
    ///
    /// Behind a flag because almost everything that invokes a command line tool treats any
    /// non-zero code as a failure. A skipped run is not a failure, and making it non-zero by
    /// default would break every script that chains a step after this one - on the day the
    /// cache first worked, which is the worst day for it to look like a new bug.
    ///
    /// <see cref="ExitCode"/> lists the codes.
    /// </remarks>
    [Cache(CacheRelevance.Control)]
    [Option("detailed-exit-code", HelpText =
        "Exit with 2 when the run had nothing to do, instead of 0. For a pipeline whose next "
        + "step is a publish.")]
    public bool DetailedExitCode { get; set; }

    /// <summary>
    /// Which language this run's own reports come out in.
    /// </summary>
    /// <remarks>
    /// Irrelevant to the cache, and that is the point: the language a person reads decides
    /// nothing about what is written to disk. Two people running one recipe get the same
    /// output and different reports.
    ///
    /// Empty means English, not the machine's locale. A run whose language followed the
    /// machine would produce CI logs that differ between runners, and a log diff that shows a
    /// change every time is a log nobody reads. spec/message-ids.md §5.
    /// </remarks>
    [Cache(CacheRelevance.Irrelevant)]
    [Option("messages", HelpText =
        "Language for this tool's own reports: en, ko, ja, zh-Hans, zh-Hant. English by "
        + "default. Also read from TABBIT_MESSAGES.")]
    public string Messages { get; set; } = "";

    [Cache(CacheRelevance.Irrelevant)]
    [Option("verbose", HelpText = "Sets whether to output debugging log messages.")]
    public bool Verbose { get; set; }

    [Cache(CacheRelevance.Irrelevant)]
    [Option("silent", HelpText = "Suppress all logging message except ERROR/FATAL.")]
    public bool Silent { get; set; }
    
    [Cache(CacheRelevance.Irrelevant)]
    [Option("debug", HelpText = "Enables or disables internal debugging.")]
    public bool Debugging { get; set; }
}
