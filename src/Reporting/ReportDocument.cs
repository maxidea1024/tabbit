using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Tabbit.Reporting;

/// <summary>What a run turned out to be, in one word.</summary>
/// <remarks>
/// Written into the report as text rather than as a number, because the reader is a build
/// pipeline's script and `"outcome": "stopped-by-validation"` needs no table to read. The
/// exit code stays what it always was; this says which kind of ending produced it.
/// </remarks>
public static class ReportOutcome
{
    public const string Success = "success";
    public const string NothingToDo = "nothing-to-do";
    public const string StoppedByValidation = "stopped-by-validation";
    public const string Failed = "failed";
}

/// <summary>What became of a report since the run before this one.</summary>
public static class ReportFate
{
    /// <summary>Not in the previous report.</summary>
    public const string New = "new";

    /// <summary>In the previous report too.</summary>
    public const string Persisting = "persisting";

    /// <summary>There was no previous report, so the question was not asked.</summary>
    public const string Uncompared = "uncompared";
}

/// <summary>Where a report points, as the report file carries it.</summary>
/// <remarks>
/// A shape of its own rather than <see cref="Models.Location"/> serialized, because the two
/// answer to different readers. This one is read by whatever a project points at its report
/// - so it holds the cell in both notations, the machine's and the one a person types into
/// a spreadsheet, and it holds the deep link separately rather than as the whole of itself.
/// </remarks>
public sealed class ReportLocation
{
    /// <summary>Workbook or document the report is about.</summary>
    public string File { get; set; } = "";

    /// <summary>Sheet within it. Empty for a report about the file as a whole.</summary>
    public string Sheet { get; set; } = "";

    /// <summary>Zero based, as everything inside this tool counts.</summary>
    public int Row { get; set; }

    /// <summary>Zero based.</summary>
    public int Column { get; set; }

    /// <summary>The same place in the notation a person reads: `C7`.</summary>
    public string Cell { get; set; } = "";

    /// <summary>
    /// A link that opens this cell, for the sources that have one.
    /// </summary>
    /// <remarks>
    /// Empty for a workbook on disk. There is no portable url that opens a local file at a
    /// cell, and a link that opens nothing is worse than a location that is only text: one
    /// of them can be copied and pasted where it works.
    /// </remarks>
    public string Url { get; set; } = "";

    /// <summary>Whether this is a position in a rule file rather than in a sheet.</summary>
    public bool InTextFile { get; set; }

    /// <summary>Everything that decides whether two runs are reporting the same place.</summary>
    [JsonIgnore]
    public string Key => $"{File}\u001f{Sheet}\u001f{Row}\u001f{Column}";
}

/// <summary>One report, as the report file carries it.</summary>
public sealed class ReportEntry
{
    /// <summary>`error`, `warning` or `info`.</summary>
    public string Severity { get; set; } = "";

    /// <summary>
    /// Which report this is. Null for a call site that still writes its own text.
    /// </summary>
    /// <remarks>
    /// What makes a pipeline's filter survive an edit to the wording, and what the comparison
    /// with the previous run keys on. spec/message-ids.md.
    /// </remarks>
    public string? Id { get; set; }

    /// <summary>The report, in whatever language the run was asked for.</summary>
    public string Message { get; set; } = "";

    /// <summary>New, still here, or not compared. <see cref="ReportFate"/>.</summary>
    public string Fate { get; set; } = ReportFate.Uncompared;

    /// <summary>Null for a report about the run rather than about a cell.</summary>
    public ReportLocation? Location { get; set; }

    /// <summary>
    /// What identifies this report across runs: which report, and where.
    /// </summary>
    /// <remarks>
    /// The id rather than the text, so that rewording a message does not empty the "still
    /// here" column - and the text as a fallback, because the call sites that have not been
    /// named yet still have to be comparable with themselves.
    ///
    /// A location is part of it because the same rule breaks in many cells, and each of them
    /// is a separate thing somebody has to go and fix.
    /// </remarks>
    [JsonIgnore]
    public string Key => (Id ?? Message) + "\u001f" + (Location?.Key ?? "");
}

/// <summary>How many of each, over the whole run.</summary>
public sealed class ReportCounts
{
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int Notes { get; set; }

    /// <summary>Problems this run has that the one before it did not.</summary>
    public int New { get; set; }

    /// <summary>Problems both runs have.</summary>
    public int Persisting { get; set; }

    /// <summary>Problems the run before this one had and this one does not.</summary>
    public int Resolved { get; set; }

    /// <summary>Whether there was a previous report to answer the three above.</summary>
    public bool Compared { get; set; }
}

/// <summary>How long a step of the run took.</summary>
public sealed class ReportPhase
{
    public string Name { get; set; } = "";
    public double Seconds { get; set; }
}

/// <summary>A failure that is this tool's own rather than the data's.</summary>
/// <remarks>
/// Separate from the entries, and left in English, for the reason the console gives it a
/// paragraph of its own: the person holding the workbook cannot fix it, and the worst
/// outcome is that they go looking through their sheets for a cause that is not there.
/// A translated bug report is also one nobody can search this repository for.
/// </remarks>
public sealed class ReportDefect
{
    public string Message { get; set; } = "";
    public string? Stack { get; set; }
}

/// <summary>
/// One run's report, as it is written to `.report.json` and read back by the next run.
/// </summary>
/// <remarks>
/// Versioned because a build pipeline reads it, and a reader that cannot tell which shape it
/// has is a reader that breaks quietly. The next run reads it too - that is what the new /
/// still here / fixed columns are made of - and an older shape it does not understand is
/// simply not compared against, which costs those three columns and nothing else.
/// spec/build-report.md.
/// </remarks>
public sealed class ReportDocument
{
    /// <summary>The shape this file is in.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Which build of this tool produced it.</summary>
    public string Tool { get; set; } = "";

    /// <summary>The recipe that was run, as an absolute path.</summary>
    public string Recipe { get; set; } = "";

    /// <summary>When the run started, ISO 8601 with an offset.</summary>
    public string StartedAt { get; set; } = "";

    /// <summary>Seconds the run took.</summary>
    public double Elapsed { get; set; }

    /// <summary><see cref="ReportOutcome"/>.</summary>
    public string Outcome { get; set; } = ReportOutcome.Success;

    /// <summary>The headline of the failure that ended the run, if one did.</summary>
    public string? Failure { get; set; }

    public ReportCounts Counts { get; set; } = new ReportCounts();

    /// <summary>What each step took, for a reader asking where the time went.</summary>
    public List<ReportPhase> Phases { get; set; } = [];

    /// <summary>Everything found, worst first within each place.</summary>
    public List<ReportEntry> Entries { get; set; } = [];

    /// <summary>
    /// The reports the recipe has written down as known.
    /// </summary>
    /// <remarks>
    /// Apart from the rest so that a page can show them apart. A list of things somebody has
    /// already decided about, mixed in with the things nobody has, makes the second kind
    /// harder to find - which is the opposite of what a report is for.
    /// </remarks>
    public List<ReportEntry> KnownProblems { get; set; } = [];

    /// <summary>What the previous run reported and this one does not.</summary>
    public List<ReportEntry> Resolved { get; set; } = [];

    /// <summary>Set only when the run hit a defect in this tool.</summary>
    public ReportDefect? Defect { get; set; }

    /// <summary>Whether anything here stops a build.</summary>
    [JsonIgnore]
    public bool HasProblems => Counts.Errors > 0 || Counts.Warnings > 0 || Defect is not null;

    /// <summary>When the run started, for a page that prints it.</summary>
    [JsonIgnore]
    public DateTimeOffset? Started
        => DateTimeOffset.TryParse(StartedAt, out var when) ? when : null;
}
