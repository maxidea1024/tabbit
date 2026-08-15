using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit;

/// <summary>
/// How much a report weighs.
/// </summary>
/// <remarks>
/// Three rather than two, because the reports a validation writes are not all judgements.
/// The Lua validators this replaces used `Log:Trace` 87 times - second only to `Log:Error` -
/// and none of those were defects: they said what was being checked. With two levels every
/// one of them would have had to become a warning or disappear.
///
/// What separates <see cref="Info"/> from <see cref="Warning"/> is that it cannot be
/// promoted. `TreatWarningsAsErrors` reaches warnings and stops there.
/// </remarks>
public enum Severity
{
    /// <summary>Stops the run. The data broke a rule.</summary>
    Error,

    /// <summary>
    /// Does not stop the run, unless a recipe asks for warnings to be promoted. Worth
    /// looking at without being certainly wrong - orphaned data, a table with no comment.
    /// </summary>
    Warning,

    /// <summary>
    /// Never stops the run and is never promoted. What the validation did: how many rows
    /// were compared, which rule did not apply and why.
    /// </summary>
    Info,
}

/// <summary>
/// Collects problems found while checking a cooked model, so a run can report
/// everything wrong with a workbook at once.
///
/// Throwing on the first bad cell makes fixing a sheet a serial process: correct
/// one value, re-run, discover the next. Since the checks here are independent of
/// each other, there is no reason to stop at the first.
///
/// TabbitException has carried a Details list all along and Program prints it;
/// nothing ever filled it in.
/// </summary>
public sealed class Diagnostics
{
    private readonly List<(Severity Severity, TabbitException.Detail Detail)> _entries = [];

    /// <summary>
    /// Whether a warning counts as an error when the run decides whether to continue.
    /// </summary>
    /// <remarks>
    /// Set from the recipe rather than asked of it here, so this class stays the collector
    /// and the policy stays in one place. <see cref="Severity.Info"/> is unaffected.
    /// </remarks>
    public bool PromoteWarnings { get; set; }

    /// <summary>Number of problems that will stop the run.</summary>
    public int Count => _entries.Count(entry => Stops(entry.Severity));

    /// <summary>Reports recorded at each severity, whatever the promotion setting.</summary>
    public int ErrorCount => _entries.Count(entry => entry.Severity == Severity.Error);

    public int WarningCount => _entries.Count(entry => entry.Severity == Severity.Warning);

    public int InfoCount => _entries.Count(entry => entry.Severity == Severity.Info);

    /// <summary>Records a problem and carries on.</summary>
    public void Error(Location? location, string message) => Add(Severity.Error, location, message);

    /// <summary>Records something worth seeing that does not stop the run on its own.</summary>
    public void Warn(Location? location, string message) => Add(Severity.Warning, location, message);

    /// <summary>Records what the run did. Never stops it.</summary>
    public void Info(Location? location, string message) => Add(Severity.Info, location, message);

    /// <summary>The same, for a report about the run rather than about a cell.</summary>
    public void Info(string message) => Add(Severity.Info, null, message);

    /// <summary>Records one report at the given severity.</summary>
    public void Add(Severity severity, Location? location, string message)
    {
        // Locked because the table rules run in parallel and every one of them reports
        // through the same instance. A List that two threads append to loses entries
        // rather than failing, which is the worst way for a validation to be wrong.
        lock (_entries)
        {
            _entries.Add((severity, new TabbitException.Detail { Location = location, Message = message }));
        }
    }

    /// <summary>
    /// Every report recorded, in the order they were made.
    /// </summary>
    /// <remarks>
    /// For a caller that wants to print the ones that do not stop the run - which is all
    /// of them, since a warning nobody sees is a warning nobody writes.
    /// </remarks>
    public IReadOnlyList<(Severity Severity, TabbitException.Detail Detail)> Entries
    {
        get { lock (_entries) return _entries.ToList(); }
    }

    /// <summary>
    /// Puts the reports in a fixed order: by file, then sheet, then row, then column.
    /// </summary>
    /// <remarks>
    /// For the stages that run in parallel. Without it the order is whichever thread finished
    /// first, so two runs over identical data print the same reports in a different order - and
    /// a CI log diff then shows a change on every run, which is the fastest way to teach people
    /// to stop reading it.
    ///
    /// Called by the caller that needs it rather than done on insertion, because sorting per
    /// report would be quadratic and the collector is appended to from several threads.
    /// </remarks>
    public void SortByLocation()
    {
        lock (_entries)
        {
            var sorted = _entries
                .OrderBy(entry => entry.Detail.Location?.Filename ?? "", StringComparer.Ordinal)
                .ThenBy(entry => entry.Detail.Location?.Sheet ?? "", StringComparer.Ordinal)
                .ThenBy(entry => entry.Detail.Location?.Row ?? 0)
                .ThenBy(entry => entry.Detail.Location?.Column ?? 0)
                .ThenBy(entry => entry.Detail.Message, StringComparer.Ordinal)
                .ToList();

            _entries.Clear();
            _entries.AddRange(sorted);
        }
    }

    /// <summary>
    /// Throws a single exception carrying every recorded problem, or returns
    /// quietly if there were none.
    /// </summary>
    /// <param name="summary">
    /// Headline shown above the list. Should say what was being checked, since the
    /// individual entries carry their own locations.
    /// </param>
    public void ThrowIfAny(string summary)
    {
        var stopping = _entries.Where(entry => Stops(entry.Severity))
                               .Select(entry => entry.Detail)
                               .ToList();

        if (stopping.Count == 0)
            return;

        string headline = stopping.Count == 1
            ? summary
            : $"{summary} ({stopping.Count} problems)";

        throw new TabbitException(headline) { Details = stopping };
    }

    /// <summary>Whether a report at this severity ends the run.</summary>
    private bool Stops(Severity severity)
        => severity == Severity.Error || (PromoteWarnings && severity == Severity.Warning);
}
