using System.IO;

namespace Tabbit;

/// <summary>
/// What `--help` prints.
/// </summary>
/// <remarks>
/// Written out rather than built from the options, and that is the decision this file is.
/// CommandLineParser's `HelpText.AutoBuild` can produce a list of options and nothing else:
/// no usage forms, no examples, no grouping, no epilogue, and a blank line between every
/// entry that turns forty-four options into ninety lines. None of those are settings it
/// has - they are things it does not do.
///
/// What a generated screen cannot say is which options belong together. Six of the options
/// here choose a mode, and most of the rest mean nothing outside one of them: `--from` is
/// noise unless `--history` is present, and a flat alphabetical list is the one shape that
/// hides that. The groups below are the information.
///
/// The risk of writing it out is that it goes stale when an option is added. That is closed
/// by `CliHelpTests`, which walks <see cref="Options"/> by reflection and fails if a
/// declared option is missing from this text or this text names one that does not exist.
/// So the cost of the decision is one test, and the test is the reason the decision is
/// safe.
///
/// spec/cli-help.md.
/// </remarks>
internal static class HelpScreen
{
    /// <summary>
    /// The whole screen: which build this is, then <see cref="Body"/>.
    /// </summary>
    /// <remarks>
    /// The header comes from <see cref="ToolVersion"/> rather than being written into the
    /// text, so the two lines a reader compares against a log's opening lines are the same
    /// two lines.
    /// </remarks>
    public static void Write(TextWriter writer)
    {
        writer.WriteLine(ToolVersion.Banner);
        writer.WriteLine(ToolVersion.Runtime);
        writer.WriteLine();
        writer.Write(Body);
    }

    /// <summary>
    /// What `--version` prints.
    /// </summary>
    /// <remarks>
    /// The same two lines the help screen and every run open with, plus the build's
    /// timestamp when there is one. The timestamp is only here: a run's opening lines
    /// answer "which build is this", and the version and commit already do that. Who wants
    /// a build time is somebody holding a binary and asking what it is, and what that
    /// person types is `--version`.
    /// </remarks>
    public static void WriteVersion(TextWriter writer)
    {
        writer.WriteLine(ToolVersion.Banner);
        writer.WriteLine(ToolVersion.Runtime);

        if (ToolVersion.Built is { } built)
            writer.WriteLine($"built {built}");
    }

    /// <summary>
    /// What a misuse prints: what was wrong, how to invoke it, and where the rest is.
    /// </summary>
    /// <remarks>
    /// Three lines rather than the whole screen. The old behaviour printed the error and
    /// then ninety lines of options under it, which pushes the error off the top of the
    /// terminal - so the one thing the reader needed is the one thing scrolled away. This
    /// is what every Unix tool does instead, and it is why `--help` exists as a separate
    /// request.
    ///
    /// To standard error, because it is a diagnostic and the caller's standard output may
    /// be somebody's input.
    /// </remarks>
    public static void WriteUsageError(TextWriter writer, string problem)
    {
        writer.WriteLine($"tabbit: {problem}");
        writer.WriteLine("Usage: tabbit -r RECIPE [OPTION]...");
        writer.WriteLine("Try 'tabbit --help' for more information.");
    }

    /// <summary>
    /// The screen under the header.
    /// </summary>
    /// <remarks>
    /// Laid out on the conventions a reader of `cp --help` already has: descriptions in one
    /// column, no blank line between options, group titles as the separator, and single
    /// quotes rather than backticks - a terminal renders no markdown, so a backtick is a
    /// backtick.
    ///
    /// Descriptions start at column 28 and nothing runs past column 80. `CliHelpTests`
    /// checks both, because an option added later is added by hand.
    /// </remarks>
    public const string Body =
        """
        Reads spreadsheets and writes them out as code and data files. A recipe says
        which sheets to read and which outputs to build.

        Usage: tabbit -r RECIPE [OPTION]...                    convert
           or: tabbit -r RECIPE --history [--from A] [--to B]   report what changed
           or: tabbit -r RECIPE --stats [--at COMMIT]           report one commit
           or: tabbit -r RECIPE --serve [--port N]              serve, and stay up
           or: tabbit -r RECIPE --prune --before AGE            drop old change detail
           or: tabbit --new-recipe FILE [--template NAME]       write a starting recipe
           or: tabbit @ARGFILE                                  read options from a file

        Examples:
          tabbit -r recipe.json
          tabbit -r recipe.json --env live --target-side server
          tabbit -r recipe.json --validate-only
          tabbit -r recipe.json --full -v
          tabbit -r recipe.json --history --from HEAD~10 --format json -o out.json
          tabbit --new-recipe recipe.json --template binary

        Recipe and run:
          -r, --recipe=FILE       The recipe to run.
          -e, --env=NAME          Environment this run is for. Recorded in the
                                    summary, and available as ${TABBIT_ENV}.
              --target-side=SIDE  Narrow the run to one side: 'client', 'server', or
                                    'both' (the default).
              --time-zone=ZONE    Time zone the sheets' dates were written in, forced
                                    over the recipe: 'Asia/Seoul' or '+09:00'.
              --variant=SPEC      Variant of a field to build, as 'Table.Field=name'.
                                    Repeatable, and it overrides the recipe.
        Cache:
              --full              Convert everything, ignoring what the cache says.
              --force-output      Run every output entry, whatever the cache says.
              --cache-dir=DIR     Where to keep the build cache. '.tabbit/' when
                                    left out.
              --detailed-exit-code
                                  Exit with 2 when the run had nothing to do, instead
                                    of 0. For a pipeline that publishes next.
        Validation:
              --validate-only     Validate and exit. No output target is run.
              --skip-runtime-validation
                                  Skip the rules that read an external store.
              --list-validators   Print the rules in the order they run, and exit.
        Write a file and exit:
              --new-recipe=FILE   Write a starting recipe file.
              --template=NAME     Which starting recipe --new-recipe writes. Omit for
                                    one holding every setting.
              --new-validator=TABLE
                                  Write a starting validation rule for this table.
              --new-encryption-key
                                  Write a new encryption key, to --out or stdout.
              --dump-schema=FILE  Write where each table sits in its sheet, as JSON,
                                    for tools that read these workbooks without
                                    cooking them.
              --show-report       Open the last build report for this recipe.
        What a conversion records in the history:
              --commit=ID         Commit this conversion is of. Read from git when
                                    left out.
              --branch=NAME       Branch this snapshot belongs to. Read from git when
                                    left out.
              --commit-author=WHO
                                  Author of the change, as 'Name <email>'. Overrides
                                    git.
              --commit-date=WHEN  When the change was made, ISO 8601. Overrides git.
              --repository=DIR    Working copy to read commit information from.
        Reading the history, with --history, --stats, or --prune:
              --history           Report what changed between two commits, and exit.
              --stats             Report the statistics of a commit, and exit.
              --prune             Remove the change detail of old snapshots, and exit.
              --from=COMMIT       Commit the range starts after. Exclusive.
              --to=COMMIT         Commit the range ends at. Inclusive.
              --at=COMMIT         Commit to report statistics for. The head when
                                    left out.
              --before=WHEN       Prune snapshots older than this: a date, or an age
                                    like '90d'.
              --keep=N            Most recent snapshots to leave alone. 100 by default.
              --table=NAME        Only report changes to this table.
              --field=NAME        Only report changes to this column.
              --author=WHO        Only report changes by this person.
              --project=NAME      Project whose history to read. From the recipe when
                                    left out.
              --limit=N           Most changes to report. What is cut is reported
                                    as cut.
        Serving the history, with --serve:
              --serve             Serve the history over HTTP and stay running.
              --port=N            Port to serve on. 8080 when left out.
              --bind=ADDRESS      Address to serve on. 127.0.0.1 when left out.
                                    Anything else needs TABBIT_SERVE_TOKEN.
        Reporting:
          -o, --out=FILE          Where to write a report. Standard output when
                                    left out.
              --format=FORMAT     Report format: 'json' or 'text'.
              --messages=LANG     Language for this tool's own reports: en, ko, ja,
                                    zh-Hans, zh-Hant. English by default, and also
                                    read from TABBIT_MESSAGES.
          -v, --verbose           Print the debug log as well.
          -q, --silent            Print nothing below ERROR.
              --debug             Print the call stack when something fails.
          -h, --help              Display this help and exit.
              --version           Output version information and exit.

        Exit codes:
          0   the run did what it was asked to
          1   the run failed, and said why
          2   nothing had changed, so nothing was produced
                (only with --detailed-exit-code)

        Environment:
          TABBIT_ENV           what --env sets. A value that disagrees is refused,
                                 not overwritten
          TABBIT_MESSAGES      default for --messages
          TABBIT_SERVE_TOKEN   required when --bind is not loopback

        An argument of @FILE is replaced by the lines of that file, one option per
        line.

        Full documentation: doc/cli.md

        """;
}
