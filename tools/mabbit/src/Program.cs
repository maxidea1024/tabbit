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
    public bool Help { get; set; }

    public string? Base { get; set; }
    public string? Mine { get; set; }
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
            }

            string value = inline ?? Next(args, ref i, name);

            switch (name)
            {
                case "base": options.Base = value; break;
                case "mine": options.Mine = value; break;
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
    private const int CouldNotRun = 2;

    private const string Usage = """
        mabbit - compares and merges spreadsheet workbooks by table and row key.

          mabbit --diff --base <file> --mine <file> [options]

        Options
          --base <file>     The workbook to compare from.
          --mine <file>     The workbook to compare to.
          --path <path>     What the file is called in the repository. Says what format a
                            file that arrived under a temporary name is in.
          --key <s>:<c>     Which column identifies a row, as `sheet:heading` or
                            `sheet:letter`. Repeatable. The first column when left out.
          --format <f>      `text` or `json`. Text when left out.
          --out <file>      Where to write the report. Standard output when left out.
          --help            This.

        Reads .xlsx, .xlsm, .xlsb and .xls. Merging is not in this build yet.
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

            if (!options.Diff)
            {
                // Merging is section 5 stage 2 of spec/workbook-merge.md and is not written
                // yet. Saying so beats a usage message that lists an option this build does
                // not have.
                Console.Error.WriteLine(
                    "Nothing to do. This build compares two workbooks: pass `--diff` with "
                    + "`--base` and `--mine`.");

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

        string report = string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase)
            ? DiffReport.Json(result)
            : DiffReport.Text(result);

        if (string.IsNullOrEmpty(options.Out))
            Console.Out.Write(report);
        else
            File.WriteAllText(options.Out, report);

        return Ran;
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
