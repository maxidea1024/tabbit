using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using Tabbit.Exporters;
using Tabbit.Models;
using Tabbit.Recipe;
using Tabbit.Targets;

namespace Tabbit.History;

/// <summary>
/// The reading side of the command line: `--history` and `--stats`.
///
/// Both go through <see cref="HistoryQuery"/> and serialise what it returns, exactly as
/// the HTTP API does. The point of that is a promise worth keeping: a number this prints
/// and the same number on the web page cannot disagree, because neither computes it.
///
/// The connection comes from the recipe rather than from options of its own. It is
/// already there, it already resolves `${NAME}` from the environment, and a second place
/// to write an address is a second place for it to be wrong.
/// </summary>
public static class HistoryCommand
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Recording;

    private static readonly JsonSerializerSettings Format = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        Converters = { new StringEnumConverter() },
    };

    /// <summary>Reports what changed between two commits.</summary>
    public static int RunHistory(Options options, RecipeModel recipe)
    {
        // Before the connection, so a misspelled --format is reported immediately
        // rather than after a query has run.
        var format = FormatOf(options);

        var (connectionString, projectKey) = Connection(options, recipe);

        using var query = HistoryQuery.Open(connectionString);

        query.RepositoryPath = RepositoryFor(options, recipe);

        string? branch = options.Branch ?? query.DefaultBranch(projectKey);

        if (branch is null)
        {
            Log.Error($"The history holds nothing for project `{projectKey}`. " +
                      $"Run a conversion with the history target enabled first.");
            return 1;
        }

        int limit = options.Limit <= 0 ? HistoryQuery.DefaultLimit : options.Limit;

        // The page needs the statistics and the trends beside the changes, so the whole
        // dashboard is assembled for it - and the same object is what the server sends.
        // json and text want only the changes, and asking for the rest would be several
        // queries whose answers are thrown away.
        if (format == ReportFormat.Html)
        {
            var dashboard = query.Dashboard(
                projectKey, branch, options.From, options.To,
                options.Table, options.Field, options.Author, limit);

            Write(options, HistoryView.SelfContained(dashboard));

            return 0;
        }

        var document = query.Diff(
            projectKey, branch,
            options.From, options.To,
            options.Table, options.Field, options.Author,
            limit);

        Write(options, format == ReportFormat.Text
            ? HistoryText.Render(document)
            : Serialize(document));

        return 0;
    }

    /// <summary>Reports the statistics of one commit.</summary>
    public static int RunStats(Options options, RecipeModel recipe)
    {
        var format = FormatOf(options);

        var (connectionString, projectKey) = Connection(options, recipe);

        using var query = HistoryQuery.Open(connectionString);

        query.RepositoryPath = RepositoryFor(options, recipe);

        string? branch = options.Branch ?? query.DefaultBranch(projectKey);

        if (branch is null)
        {
            Log.Error($"The history holds nothing for project `{projectKey}`. " +
                      $"Run a conversion with the history target enabled first.");
            return 1;
        }

        var summary = query.Stats(projectKey, branch, options.At);

        if (summary is null)
        {
            Log.Error(options.At is null
                ? $"Branch `{branch}` of `{projectKey}` has no snapshots."
                : $"The history has no snapshot for `{options.At}` on branch `{branch}`.");

            return 1;
        }

        if (format == ReportFormat.Html)
        {
            // The same page as `--history --format html --to <at>`. There is one page,
            // and it already leads with the statistics; a second layout of the same
            // numbers is a second thing to keep in step.
            Write(options, HistoryView.SelfContained(
                query.Dashboard(projectKey, branch, to: options.At,
                                limit: options.Limit <= 0 ? HistoryQuery.DefaultLimit : options.Limit)));

            return 0;
        }

        Write(options, format == ReportFormat.Text
            ? HistoryText.Render(summary, branch)
            : Serialize(summary));

        return 0;
    }

    /// <summary>Removes the change detail of old snapshots.</summary>
    public static int RunPrune(Options options, RecipeModel recipe)
    {
        // Before the connection: an age that does not parse should be reported now
        // rather than after a database has been opened and locked.
        var before = HistoryMaintenance.ParseCutoff(options.Before);

        if (before is null && options.Keep <= 0)
        {
            throw new TabbitException(
                "--prune with neither --before nor --keep would remove every snapshot's " +
                "detail. Say how far back to go.");
        }

        var (connectionString, projectKey) = Connection(options, recipe);

        using var connection = new MySqlConnector.MySqlConnection(connectionString);
        connection.Open();

        string? branch = options.Branch;

        if (branch is null)
        {
            using var query = HistoryQuery.Open(connectionString);
            branch = query.DefaultBranch(projectKey);
        }

        if (branch is null)
        {
            Log.Error($"The history holds nothing for project `{projectKey}`.");
            return 1;
        }

        HistoryMaintenance.Prune(connection, projectKey, branch, before, options.Keep);

        return 0;
    }

    /// <summary>
    /// The working copy a query resolves tag names against.
    ///
    /// The same places a conversion looks: whatever `--repository` names, then the
    /// sheets' own source directories, then the working directory. Reading it from one
    /// place means `--from v1.2.0` means the same thing whether it is being recorded or
    /// asked about.
    /// </summary>
    private static string? RepositoryFor(Options options, RecipeModel recipe)
        => CommitInfo.Resolve(options, recipe).RepositoryPath;

    /// <summary>The document, exactly as the API serves it.</summary>
    public static string Serialize(object document)
        => JsonConvert.SerializeObject(document, Format).Replace("\r\n", "\n") + "\n";

    /// <summary>How a report is to be rendered.</summary>
    private enum ReportFormat
    {
        Json,
        Text,
        Html,
    }

    private static ReportFormat FormatOf(Options options)
    {
        switch ((options.Format ?? "json").Trim().ToLowerInvariant())
        {
            case "json": return ReportFormat.Json;
            case "text": return ReportFormat.Text;
            case "html": return ReportFormat.Html;

            default:
                throw new TabbitException(
                    $"`--format {options.Format}` is not a format. Use `json`, `text` or `html`.");
        }
    }

    private static void Write(Options options, string content)
    {
        if (string.IsNullOrEmpty(options.Out))
        {
            // Through the console rather than the log, because this is the answer rather
            // than a note about producing it - and a caller may be piping it.
            //
            // Onto the raw stream in UTF-8 rather than through Console.Out, whose
            // encoding on Windows is the console codepage. A report is full of author
            // names and cell values, and that codepage turns every non-ASCII one into
            // question marks - in a file somebody redirected it into, where nothing will
            // ever say what happened.
            using var stdout = Console.OpenStandardOutput();
            using var writer = new StreamWriter(stdout, new UTF8Encoding(false));

            writer.Write(content);
            writer.Flush();

            return;
        }

        string path = Path.GetFullPath(options.Out);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written directly rather than staged: a report is not a build artifact, and a
        // failure part-way through leaves nothing worth rolling back.
        File.WriteAllText(path, content, new UTF8Encoding(false));

        Log.Information($"Wrote the report to `{path}`");
    }

    /// <summary>
    /// Where the history is, and which project to read - from the recipe's history
    /// entry.
    /// </summary>
    internal static (string ConnectionString, string ProjectKey) Connection(
        Options options, RecipeModel recipe)
    {
        var planned = TargetRegistry.Plan(recipe, TargetSide.Both)
                                    .Where(p => p.Entry is HistoryRecipe)
                                    .ToList();

        if (planned.Count == 0)
        {
            throw new TabbitException(
                "This recipe has no `history` target, so there is nothing to read. Add one, or " +
                "point --recipe at the recipe the conversions use.");
        }

        if (planned.Count > 1 && options.Project is null)
        {
            var keys = planned.Select(p => ((HistoryRecipe)p.Entry).ProjectKey).Distinct().ToList();

            throw new TabbitException(
                $"This recipe has {planned.Count} history targets ({string.Join(", ", keys)}). " +
                $"Name the one to read with --project.");
        }

        var chosen = options.Project is null
            ? planned[0]
            : planned.FirstOrDefault(p => string.Equals(
                  ((HistoryRecipe)p.Entry).ProjectKey, options.Project, StringComparison.OrdinalIgnoreCase));

        if (chosen.Entry is null)
        {
            throw new TabbitException(
                $"This recipe has no history target for project `{options.Project}`.");
        }

        var entry = (HistoryRecipe)chosen.Entry;

        return (ConnectionString.Resolve(entry.ConnectionString, chosen.Section), entry.ProjectKey);
    }
}
