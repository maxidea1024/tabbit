using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;
using Tabbit.Caching;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Recipe;

namespace Tabbit.Reporting;

/// <summary>
/// Everything one run found, kept until the run ends and then written where the person who
/// can fix it will see it.
/// </summary>
/// <remarks>
/// **This adds no checking.** Every report it carries was already made, already printed and
/// already thrown away - the stages build a <see cref="Diagnostics"/>, print what does not
/// stop the run, throw what does, and let the collector go. What was missing was a place for
/// it to arrive: the console scrolls, the log file is read by nobody who owns a sheet, and an
/// exception carries as much as fits on a screen.
///
/// **It is not a target.** A target runs after importing, cooking and validation have all
/// succeeded, and the run this report is most needed for is the one that did not get that
/// far. So it is wired into the run itself and written on the way out of every ending,
/// including the ones that threw.
///
/// **It never changes the outcome.** A report that could not be written costs its reader a
/// list and nothing else, so it is caught and logged. Nothing here touches the exit code.
///
/// spec/ops/build-report.md.
/// </remarks>
public sealed class BuildReport
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static ILogger Log => LogCategory.Reporting;

    private readonly Options _options;
    private readonly ReportRecipe _recipe;
    private readonly ReportOpening.Policy _policy;

    private readonly DateTimeOffset _started = DateTimeOffset.Now;

    private readonly List<ReportEntry> _entries = [];
    private readonly List<ReportEntry> _known = [];

    /// <summary>
    /// What has already been taken, so the same report is not carried twice.
    /// </summary>
    /// <remarks>
    /// A stopping report arrives twice by design: the stage takes its whole collector before
    /// throwing, and then the exception carries the stopping half of that same collector up
    /// to the handler. Both paths are wanted - one of them is what a failing run has - and
    /// keying them means neither has to know about the other.
    /// </remarks>
    private readonly HashSet<string> _taken = new HashSet<string>(StringComparer.Ordinal);

    private string? _failure;
    private bool _stoppedByData;
    private ReportDefect? _defect;

    private BuildReport(Options options, ReportRecipe recipe, ReportOpening.Policy policy)
    {
        _options = options;
        _recipe = recipe;
        _policy = policy;
    }

    /// <summary>Where the machine-readable half goes.</summary>
    public string JsonPath => PathFor(_options, _recipe, ".report.json");

    /// <summary>Where the half a person reads goes.</summary>
    public string HtmlPath => PathFor(_options, _recipe, ".report.html");

    // ------------------------------------------------------------------ opening

    /// <summary>
    /// A collector for this run, or null when the recipe asked for no report.
    /// </summary>
    /// <remarks>
    /// The setting is read here, before a workbook is opened, so a misspelled
    /// `OpenInBrowser` is refused with no work done rather than after the conversion.
    /// </remarks>
    public static BuildReport? Create(Options options, RecipeModel recipe)
    {
        var settings = recipe.Report ?? new ReportRecipe();

        if (!settings.Enabled || string.IsNullOrEmpty(options.RecipeFilename))
            return null;

        return new BuildReport(options, settings, ReportOpening.PolicyOf(settings.OpenInBrowser));
    }

    // --------------------------------------------------------------- collecting

    /// <summary>
    /// Takes everything a stage found, whether or not it stops the run.
    /// </summary>
    /// <remarks>
    /// Called where the stage prints, rather than instead of it. The console still says what
    /// it always said: this is a third destination for the same reports, not a replacement
    /// for the two that exist.
    /// </remarks>
    public void Take(Diagnostics diagnostics)
    {
        foreach (var (severity, detail) in diagnostics.Entries)
            Add(severity, detail);
    }

    /// <summary>
    /// Records the failure that ended the run, and anything it was carrying.
    /// </summary>
    /// <remarks>
    /// Three kinds arrive here and they are not one thing. A collected failure carries the
    /// list that stopped the run, which is already taken and is deduplicated back out. A
    /// single refusal carries one report and a place, which becomes an entry so that it
    /// appears in the list like everything else. A defect carries neither, and is put
    /// somewhere of its own - the person holding the workbook cannot act on it, and the worst
    /// outcome is that they go looking through their sheets for a cause that is not there.
    /// </remarks>
    public void Failed(Exception failure)
    {
        _failure = failure.Message;

        if (failure is TabbitDefectException defect)
        {
            _defect = new ReportDefect { Message = defect.Message, Stack = defect.StackTrace };
            return;
        }

        if (failure is not TabbitException refusal)
            return;

        if (refusal.Details.Count > 0)
        {
            _stoppedByData = true;

            foreach (var detail in refusal.Details)
                Add(Severity.Error, detail);

            return;
        }

        Add(Severity.Error, new TabbitException.Detail
        {
            Location = refusal.Location,
            Message = refusal.Message,
            MessageId = refusal.MessageId,
        });
    }

    private void Add(Severity severity, TabbitException.Detail detail)
    {
        var entry = new ReportEntry
        {
            Severity = NameOf(severity),
            Id = detail.MessageId,
            Message = detail.Message,
            Location = LocationOf(detail.Location),
        };

        if (!_taken.Add(entry.Key))
            return;

        // The written-down ones are apart rather than filtered out. A list of things somebody
        // has already decided about, mixed in with the things nobody has, makes the second
        // kind harder to find - and that is the whole job of the page.
        if (detail.MessageId == Cooking.NamingMessages.KnownProblemNoted)
            _known.Add(entry);
        else
            _entries.Add(entry);
    }

    private static string NameOf(Severity severity)
        => severity switch
        {
            Severity.Error => "error",
            Severity.Warning => "warning",
            _ => "info",
        };

    private static ReportLocation? LocationOf(Location? location)
    {
        if (location is null)
            return null;

        return new ReportLocation
        {
            File = location.Filename,
            Sheet = location.Sheet,
            Row = location.Row,
            Column = location.Column,

            // Both notations, because the two readers are different. A script compares the
            // numbers; a person types `C7` into the box at the top of a spreadsheet.
            Cell = location.InTextFile
                ? $"({location.Row + 1},{location.Column + 1})"
                : location.CellRange,

            Url = location.SheetUrl,
            InTextFile = location.InTextFile,
        };
    }

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// Writes both halves, says where they are, and opens the page if anybody is there to
    /// look at it.
    /// </summary>
    /// <returns>The page's path, or null if nothing could be written.</returns>
    public string? Write(int exitCode, RunTimings? timings)
    {
        try
        {
            var document = Build(exitCode, timings);

            // Read before the write, because the write is to the same path: this is the only
            // moment the previous run's report still exists.
            Compare(document, Load(JsonPath));

            WriteJson(document);

            string html = ReportHtml.Render(
                document, MessageCatalog.Current, _recipe.MaxHtmlEntries, Path.GetFileName(JsonPath));

            WriteText(HtmlPath, html);

            Announce(document);
            OpenIfWanted(document);

            return HtmlPath;
        }
        catch (Exception failure)
        {
            // A report that could not be written costs its reader a list. It does not cost
            // the run its result, and a conversion that succeeded still succeeded.
            Log.Warning($"The build report could not be written: {failure.Message}");
            return null;
        }
    }

    private ReportDocument Build(int exitCode, RunTimings? timings)
    {
        var document = new ReportDocument
        {
            Tool = ToolVersion.Current,
            Recipe = Path.GetFullPath(_options.RecipeFilename!),
            StartedAt = _started.ToString("o"),
            Elapsed = Math.Round((DateTimeOffset.Now - _started).TotalSeconds, 3),
            Outcome = OutcomeOf(exitCode),
            Failure = _failure,
            Entries = _entries,
            KnownProblems = _known,
            Defect = _defect,
        };

        if (timings is not null)
        {
            document.Phases = timings.Phases
                .Select(phase => new ReportPhase
                {
                    Name = phase.Name,
                    Seconds = Math.Round(phase.Elapsed.TotalSeconds, 3),
                })
                .ToList();
        }

        document.Counts.Errors = _entries.Count(entry => entry.Severity == "error");
        document.Counts.Warnings = _entries.Count(entry => entry.Severity == "warning");
        document.Counts.Notes = _entries.Count(entry => entry.Severity == "info");

        return document;
    }

    private string OutcomeOf(int exitCode)
    {
        if (_stoppedByData)
            return ReportOutcome.StoppedByValidation;

        if (_failure is not null || _defect is not null)
            return ReportOutcome.Failed;

        return exitCode switch
        {
            ExitCode.Success => ReportOutcome.Success,
            ExitCode.NothingToDo => ReportOutcome.NothingToDo,
            _ => ReportOutcome.Failed,
        };
    }

    // --------------------------------------------------------------- comparison

    /// <summary>
    /// Marks each problem new or still here, and lists what has gone.
    /// </summary>
    /// <remarks>
    /// The point of the whole report is that problems do not pile up, so whether they are
    /// piling up has to be a number rather than an impression. What has gone is listed too:
    /// seeing a fix land is part of what makes the next one happen.
    ///
    /// Notes are left out of it. They say what was checked, so they arrive every run by
    /// definition and would fill "still here" with things nobody has to do anything about.
    ///
    /// **The keying has a limit and it is stated on the page.** A row inserted above a
    /// problem moves it, and a moved problem reads as one fixed and one new. Nothing here
    /// decides anything on that basis - no exit code, no gate - precisely because of it.
    /// </remarks>
    private static void Compare(ReportDocument document, ReportDocument? previous)
    {
        var problems = document.Entries.Where(IsProblem).ToList();

        if (previous is null)
        {
            document.Counts.Compared = false;

            foreach (var entry in problems)
                entry.Fate = ReportFate.Uncompared;

            return;
        }

        document.Counts.Compared = true;

        var before = previous.Entries.Where(IsProblem).ToList();
        var beforeKeys = before.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var nowKeys = problems.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in problems)
        {
            entry.Fate = beforeKeys.Contains(entry.Key) ? ReportFate.Persisting : ReportFate.New;

            if (entry.Fate == ReportFate.New)
                document.Counts.New++;
            else
                document.Counts.Persisting++;
        }

        document.Resolved = before.Where(entry => !nowKeys.Contains(entry.Key)).ToList();
        document.Counts.Resolved = document.Resolved.Count;
    }

    private static bool IsProblem(ReportEntry entry)
        => entry.Severity == "error" || entry.Severity == "warning";

    // ------------------------------------------------------------------- output

    private void WriteJson(ReportDocument document)
        => WriteText(JsonPath, JsonConvert.SerializeObject(document, Formatting.Indented));

    private static void WriteText(string path, string text)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Written aside and moved, the way the build seal is: a reader that opened the
        // previous report while this one was being written gets one of the two rather
        // than half of each.
        string temporary = path + "." + Environment.ProcessId + ".tmp";

        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The report a previous run left at this path, or null.</summary>
    /// <remarks>
    /// A file that cannot be read is treated as no file. It costs this run its comparison
    /// columns, which is the correct price for a report that is corrupt or was written by a
    /// version whose shape this one does not know.
    /// </remarks>
    private static ReportDocument? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var document = JsonConvert.DeserializeObject<ReportDocument>(File.ReadAllText(path));

            return document?.Version == ReportDocument.CurrentVersion ? document : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Announce(ReportDocument document)
    {
        string counted = document.Counts.Errors > 0 || document.Counts.Warnings > 0
            ? $" - {document.Counts.Errors} error(s), {document.Counts.Warnings} warning(s)"
            : "";

        Log.Information($"Build report: {Path.GetFullPath(HtmlPath)}{counted}");
    }

    private void OpenIfWanted(ReportDocument document)
    {
        if (!ReportOpening.Wanted(_policy, document.HasProblems))
            return;

        var suppressed = ReportOpening.SuppressedHere(_options.Silent);

        if (suppressed != ReportOpening.Suppression.None)
        {
            Log.Debug($"Not opening the report: {suppressed}.");
            return;
        }

        if (!ReportOpening.Opener(HtmlPath))
            Log.Warning("The build report could not be opened in a browser. Its path is above.");
    }

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// Opens the report the last run left, without running anything.
    /// </summary>
    /// <remarks>
    /// The other half of writing it to a fixed path. "Where was that report" is a question
    /// with one answer per recipe, so it can be a flag rather than a path somebody has to
    /// have kept.
    /// </remarks>
    public static int ShowLast(Options options, RecipeModel recipe)
    {
        string path = Path.GetFullPath(PathFor(options, recipe.Report ?? new ReportRecipe(), ".report.html"));

        if (!File.Exists(path))
        {
            Log.Error($"No build report at {path}. Run the conversion once and it will be there.");
            return ExitCode.Failed;
        }

        Log.Information($"Build report: {path}");

        if (!ReportOpening.Opener(path))
        {
            Log.Warning("It could not be opened in a browser. Its path is above.");
            return ExitCode.Failed;
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// Where this recipe's report goes.
    /// </summary>
    /// <remarks>
    /// Beside the build seal under the same stem, so both of a run's own files are found the
    /// same way and a second recipe of the same name in another checkout does not overwrite
    /// either. A recipe naming a folder puts them there instead, which is for the build
    /// pipelines that collect artifacts from a fixed place.
    /// </remarks>
    private static string PathFor(Options options, ReportRecipe recipe, string suffix)
    {
        string path = CacheFiles.PathFor(options, suffix);

        return string.IsNullOrWhiteSpace(recipe.Path)
            ? path
            : Path.Combine(recipe.Path, Path.GetFileName(path));
    }
}
