using System;
using System.Collections.Generic;
using System.IO;

namespace Mabbit;

/// <summary>
/// What the command line said.
/// </summary>
/// <remarks>
/// Parsed by hand rather than by a package. This program has one job and a fixed handful of
/// options, all of them paths, and the arguments it is given are written once into a version
/// control configuration and then never typed again. A parsing library would be a dependency
/// carried for a screenful of code, in a program whose whole dependency list is one reader.
/// </remarks>
internal sealed class Options
{
    public bool Diff { get; set; }
    public bool Merge { get; set; }
    public bool Help { get; set; }

    public string? Base { get; set; }
    public string? Mine { get; set; }
    public string? Theirs { get; set; }
    public string? Result { get; set; }
    public string? Path { get; set; }
    public string? Schema { get; set; }
    public string? Format { get; set; }
    public string? Out { get; set; }

    public List<string> Key { get; } = [];

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new MabbitException($"`{arg}` is not an option. Run `mabbit --help`.");

            // Both spellings, because a version control configuration is written by hand and
            // both are what somebody writes.
            int equals = arg.IndexOf('=', StringComparison.Ordinal);

            string name = equals < 0 ? arg[2..] : arg[2..equals];
            string? inline = equals < 0 ? null : arg[(equals + 1)..];

            switch (name)
            {
                case "help": options.Help = true; continue;
                case "diff": options.Diff = true; continue;
                case "merge": options.Merge = true; continue;
            }

            string value = inline ?? Next(args, ref i, name);

            switch (name)
            {
                case "base": options.Base = value; break;
                case "mine": options.Mine = value; break;
                case "theirs": options.Theirs = value; break;
                case "result": options.Result = value; break;
                case "path": options.Path = value; break;
                case "schema": options.Schema = value; break;
                case "format": options.Format = value; break;
                case "out": options.Out = value; break;
                case "key": options.Key.Add(value); break;

                default:
                    throw new MabbitException($"`--{name}` is not an option. Run `mabbit --help`.");
            }
        }

        return options;
    }

    private static string Next(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length)
            throw new MabbitException($"`--{name}` needs a value after it.");

        return args[++i];
    }
}

public static class Program
{
    /// <summary>
    /// Zero when the comparison ran, whether or not it found anything; two when it could not
    /// run at all.
    /// </summary>
    /// <remarks>
    /// Finding a difference is not a failure. A version control system runs a comparison tool
    /// through `difftool`, and one that reported a non-zero code for "these files differ"
    /// would have the system announce a failed command every time it was asked to do the one
    /// thing it is for.
    /// </remarks>
    private const int Ran = 0;
    private const int Conflicted = 1;
    private const int CouldNotRun = 2;

    private const string Usage = """
        mabbit - compares and merges spreadsheet workbooks by table and row key.

          mabbit --diff  --base <file> --mine <file> [options]
          mabbit --merge --base <file> --mine <file> --theirs <file> [options]

        Options
          --base <file>     The common ancestor, for a merge. The workbook to compare
                            from, for a comparison.
          --mine <file>     This side.
          --theirs <file>   The other side. Merge only.
          --path <path>     What the file is called in the repository. Says what format a
                            file that arrived under a temporary name is in.
          --schema <file>   Where the tables are, as written by `tabbit --dump-schema`.
                            Without one, each sheet is taken as one table whose first
                            column identifies a row - a guess, and a loud one when wrong.
          --key <s>:<c>     Which column identifies a row, as `sheet:heading` or
                            `sheet:letter`. Repeatable. Ignored when --schema is given.
          --format <f>      `text`, `json`, or `html` for a page to open in a browser.
                            Text when left out. A merge report is the one worth opening:
                            it puts the three values of a conflict side by side.
          --out <file>      Where to write the report. Standard output when left out.
          --help            This.

        Reads .xlsx, .xlsm, .xlsb and .xls.

        `--merge` with `--result` writes the merged workbook; without one it judges and
        reports and writes nothing. It writes cell values only: a row arriving is appended
        below the table when there is room, and anything that would move what sits below or
        beside a table is refused with its reason.

        As a git merge driver:

          .gitattributes    *.xlsx merge=mabbit
          .git/config       [merge "mabbit"]
                                name = Mabbit workbook merge
                                driver = mabbit --merge --base %O --mine %A \
                                         --theirs %B --result %A --path %P

        Exit codes: 0 merged, 1 conflicts to settle by hand, 2 could not run.
        """;

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);

            if (options.Help || args.Length == 0)
            {
                Console.Out.WriteLine(Usage);
                return options.Help ? Ran : CouldNotRun;
            }

            if (options.Diff && options.Merge)
            {
                throw new MabbitException(
                    "`--diff` and `--merge` are different jobs. Ask for one of them.");
            }

            if (options.Merge)
                return Merge(options);

            if (!options.Diff)
            {
                Console.Error.WriteLine(
                    "Nothing to do. Pass `--diff` or `--merge`. Run `mabbit --help`.");

                return CouldNotRun;
            }

            return Diff(options);
        }
        catch (MabbitException error)
        {
            Console.Error.WriteLine(error.Message);
            return CouldNotRun;
        }
    }

    /// <summary>
    /// Where the tables are: from the file that says so, or guessed.
    /// </summary>
    /// <remarks>
    /// The guess is right for a sheet that is nothing but a table, which is most of them, and
    /// wrong loudly rather than quietly when it is not - every row reports as changed. The
    /// file is right always, and costs a conversion having been run.
    /// </remarks>
    private static ITableSchema Schema(Options options, string workbook)
        => string.IsNullOrEmpty(options.Schema)
            ? new HeuristicSchema(KeyColumns(options.Key))
            : SchemaFile.Read(options.Schema, options.Path ?? workbook);

    private static int Diff(Options options)
    {
        string first = Required(options.Base, "--base");
        string second = Required(options.Mine, "--mine");

        var schema = Schema(options, second);

        // The repository path names the format for both sides. A conflict hands its tools two
        // temporary files of the same tracked path, so one answer covers both.
        var before = WorkbookGrid.Read(first, options.Path, reportAs: Shown(first, options.Path));
        var after = WorkbookGrid.Read(second, options.Path, reportAs: Shown(second, options.Path));

        var result = WorkbookDiff.Compare(
            before.Name, TableViews.Of(before, schema),
            after.Name, TableViews.Of(after, schema));

        Write(options, Format(options) switch
        {
            "json" => DiffReport.Json(result),
            _ => DiffReport.Text(result),
        });

        return Ran;
    }

    private static int Merge(Options options)
    {
        string ancestor = Required(options.Base, "--base");
        string here = Required(options.Mine, "--mine");
        string there = Required(options.Theirs, "--theirs");

        var schema = Schema(options, here);

        var inBase = WorkbookGrid.Read(ancestor, options.Path, reportAs: Shown(ancestor, options.Path));
        var mine = WorkbookGrid.Read(here, options.Path, reportAs: Shown(here, options.Path));
        var theirs = WorkbookGrid.Read(there, options.Path, reportAs: Shown(there, options.Path));

        var mineTables = TableViews.Of(mine, schema);

        var judged = WorkbookMerge.Judge(
            inBase.Name, TableViews.Of(inBase, schema),
            mine.Name, mineTables,
            theirs.Name, TableViews.Of(theirs, schema));

        var plan = new MergePlan
        {
            BaseName = judged.BaseName,
            MineName = judged.MineName,
            TheirsName = judged.TheirsName,
            Tables = judged.Tables,
            Notes = judged.Notes,
            Outside = WorkbookMerge.OutsideTables(inBase, mine, theirs, schema),
        };

        var write = string.IsNullOrEmpty(options.Result)
            ? null
            : MergeWriter.Prepare(plan, mineTables, mine);

        Write(options, Format(options) switch
        {
            "json" => MergeReport.Json(plan),
            "html" => HtmlReport.Of(plan, write),
            _ => MergeReport.Text(plan, write),
        });

        if (write is null)
            return plan.HasConflicts ? Conflicted : Ran;

        if (!write.CanWrite)
            return Conflicted;

        // The result is written from this side's file, so everything neither side touched
        // survives as the bytes it already was.
        XlsxPatcher.Apply(here, options.Result!, write.Edits);

        return Ran;
    }

    /// <summary>The report format asked for, lower case, `text` when nothing was said.</summary>
    private static string Format(Options options)
    {
        string asked = (options.Format ?? "text").ToLowerInvariant();

        if (asked is "text" or "json" or "html")
            return asked;

        throw new MabbitException(
            $"`--format {asked}` is not a format this writes. It writes `text`, `json` and "
            + "`html`.");
    }

    private static void Write(Options options, string report)
    {
        if (string.IsNullOrEmpty(options.Out))
            Console.Out.Write(report);
        else
            File.WriteAllText(options.Out, report);
    }

    /// <summary>
    /// What to call a file in the report.
    /// </summary>
    /// <remarks>
    /// A temporary name says nothing about which side it is, so when the repository path is
    /// known both are shown against it. Without one there is only the path each was given.
    /// </remarks>
    private static string Shown(string file, string? repositoryPath)
        => string.IsNullOrEmpty(repositoryPath) ? file : $"{repositoryPath} ({file})";

    private static IReadOnlyDictionary<string, string> KeyColumns(IEnumerable<string> asked)
    {
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in asked)
        {
            // The last colon separates them, so a sheet whose name holds one still works.
            int split = entry.LastIndexOf(':');

            if (split <= 0 || split == entry.Length - 1)
            {
                throw new MabbitException(
                    $"`--key {entry}` is not a sheet and a column. Write it as "
                    + "`--key <sheet>:<heading>` or `--key <sheet>:<column letter>`.");
            }

            columns[entry[..split]] = entry[(split + 1)..];
        }

        return columns;
    }

    private static string Required(string? value, string option)
    {
        if (string.IsNullOrEmpty(value))
            throw new MabbitException($"`{option}` is required and was not given.");

        return value;
    }
}
