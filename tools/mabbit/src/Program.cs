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
          --key <s>:<c>     Which column identifies a row, as `sheet:heading` or
                            `sheet:letter`. Repeatable. The first column when left out.
          --format <f>      `text` or `json`. Text when left out.
          --out <file>      Where to write the report. Standard output when left out.
          --help            This.

        Reads .xlsx, .xlsm, .xlsb and .xls.

        `--merge` judges and reports. It does not write a workbook, so it must not be
        registered as a version control merge driver yet.

        Exit codes: 0 done, 1 the merge has conflicts, 2 could not run.
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

    private static int Diff(Options options)
    {
        string first = Required(options.Base, "--base");
        string second = Required(options.Mine, "--mine");

        var schema = new HeuristicSchema(KeyColumns(options.Key));

        // The repository path names the format for both sides. A conflict hands its tools two
        // temporary files of the same tracked path, so one answer covers both.
        var before = WorkbookGrid.Read(first, options.Path, reportAs: Shown(first, options.Path));
        var after = WorkbookGrid.Read(second, options.Path, reportAs: Shown(second, options.Path));

        var result = WorkbookDiff.Compare(
            before.Name, TableViews.Of(before, schema),
            after.Name, TableViews.Of(after, schema));

        Write(options, string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase)
            ? DiffReport.Json(result)
            : DiffReport.Text(result));

        return Ran;
    }

    private static int Merge(Options options)
    {
        // Refused rather than ignored. Somebody who passes it is wiring this up as a merge
        // driver, and a driver that reports success without writing the result is how a
        // day's work disappears.
        if (!string.IsNullOrEmpty(options.Result))
        {
            throw new MabbitException(
                "`--result` asks for a merged workbook to be written, and this build does not "
                + "write one. Leave it out to get the judgement, and do not register mabbit as "
                + "a merge driver until it does.");
        }

        string ancestor = Required(options.Base, "--base");
        string here = Required(options.Mine, "--mine");
        string there = Required(options.Theirs, "--theirs");

        var schema = new HeuristicSchema(KeyColumns(options.Key));

        var inBase = WorkbookGrid.Read(ancestor, options.Path, reportAs: Shown(ancestor, options.Path));
        var mine = WorkbookGrid.Read(here, options.Path, reportAs: Shown(here, options.Path));
        var theirs = WorkbookGrid.Read(there, options.Path, reportAs: Shown(there, options.Path));

        var plan = WorkbookMerge.Judge(
            inBase.Name, TableViews.Of(inBase, schema),
            mine.Name, TableViews.Of(mine, schema),
            theirs.Name, TableViews.Of(theirs, schema));

        plan = new MergePlan
        {
            BaseName = plan.BaseName,
            MineName = plan.MineName,
            TheirsName = plan.TheirsName,
            Tables = plan.Tables,
            Notes = plan.Notes,
            Outside = WorkbookMerge.OutsideTables(inBase, mine, theirs, schema),
        };

        Write(options, string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase)
            ? MergeReport.Json(plan)
            : MergeReport.Text(plan));

        return plan.HasConflicts ? Conflicted : Ran;
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
