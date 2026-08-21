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

    /// <summary>
    /// Takes the reports a recipe has written down out of the way of the run.
    /// </summary>
    /// <remarks>
    /// Each matched report becomes <see cref="Severity.Info"/> with the entry's reason beside
    /// it, so it is still printed on every run - the list says "not now", not "not a problem".
    ///
    /// **What is added rather than removed** is the reporting about the list itself: an entry
    /// matching nothing, or matching a different number of reports than it claims, is an error.
    /// Without those two an entry covering a sheet would hide the next defect in that sheet,
    /// and a list nobody prunes eventually covers a workbook.
    ///
    /// Applied in one pass after every check has run rather than as reports arrive, because
    /// counting is the point and a count is only right once the counting has finished.
    /// spec/known-problems.md.
    /// </remarks>
    public void ApplyKnownProblems(IReadOnlyList<Recipe.KnownProblemRecipe> known)
    {
        if (known.Count == 0)
            return;

        lock (_entries)
        {
            var matched = new int[known.Count];

            for (int at = 0; at < _entries.Count; at++)
            {
                var (severity, detail) = _entries[at];

                if (severity == Severity.Info)
                    continue;

                int entry = FirstMatch(known, detail.Location);

                if (entry < 0)
                    continue;

                matched[entry]++;

                _entries[at] = (Severity.Info, new TabbitException.Detail
                {
                    Location = detail.Location,
                    Message = $"{detail.Message} (Known problem: {known[entry].Reason})",
                });
            }

            for (int entry = 0; entry < known.Count; entry++)
            {
                var item = known[entry];

                // A wholly blank entry is not an entry. The skeleton a new recipe is written
                // from fills every list with one so that the shape is visible, and that
                // skeleton has to run - an entry saying nothing at all is that placeholder.
                // Half of one is a mistake, and stays an error below.
                if (item.At.Length == 0 && item.Reason.Length == 0)
                    continue;

                if (item.Reason.Length == 0 || item.At.Length == 0)
                {
                    _entries.Add((Severity.Error, new TabbitException.Detail
                    {
                        Message = $"`Validation.KnownProblems` entry {entry + 1} needs both `At` "
                            + $"and `Reason`. An entry without a place covers everything, and one "
                            + $"without a reason is a switch rather than a note.",
                    }));

                    continue;
                }

                if (matched[entry] == 0)
                {
                    _entries.Add((Severity.Error, new TabbitException.Detail
                    {
                        Message = $"`Validation.KnownProblems` names `{item.At}`, and nothing was "
                            + $"reported there. Either it is fixed or the place is wrong; both are "
                            + $"reasons to take the entry out. (`{item.Reason}`)",
                    }));

                    continue;
                }

                if (item.Count > 0 && matched[entry] != item.Count)
                {
                    _entries.Add((Severity.Error, new TabbitException.Detail
                    {
                        Message = $"`Validation.KnownProblems` says `{item.At}` accounts for "
                            + $"{item.Count} report(s) and it accounts for {matched[entry]}. "
                            + $"{(matched[entry] > item.Count ? "Something new is wrong there" : "Some of it is fixed")}, "
                            + $"so the entry no longer says what is known. (`{item.Reason}`)",
                    }));
                }
            }
        }
    }

    /// <summary>
    /// Which written-down place a report's location sits in, or -1 for none.
    /// </summary>
    /// <remarks>
    /// The first match wins, so a narrow entry written above a wide one takes its own reports.
    /// A report with no location matches nothing: it is about the run rather than about a cell,
    /// and there is no place for a list of places to name.
    /// </remarks>
    private static int FirstMatch(
        IReadOnlyList<Recipe.KnownProblemRecipe> known, Location? location)
    {
        if (location is null)
            return -1;

        for (int at = 0; at < known.Count; at++)
        {
            if (known[at].At.Length > 0 && known[at].Reason.Length > 0
                && Covers(known[at].At, location))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>Whether a written-down place covers this location.</summary>
    /// <remarks>
    /// Three forms, widest first: the file, a sheet of it, one cell of that sheet. The file is
    /// matched by the end of the path so that the same list works wherever the folder is.
    /// </remarks>
    private static bool Covers(string place, Location location)
    {
        var parts = place.Split(':');

        for (int at = 0; at < parts.Length; at++)
            parts[at] = parts[at].Trim();

        if (parts.Length == 0 || parts[0].Length == 0)
            return false;

        string filename = (location.Filename ?? "").Replace('\\', '/');
        string wanted = parts[0].Replace('\\', '/');

        if (!filename.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
            return false;

        if (parts.Length == 1)
            return true;

        if (!string.Equals(location.Sheet ?? "", parts[1], StringComparison.Ordinal))
            return false;

        if (parts.Length == 2)
            return true;

        return string.Equals(location.CellRange, parts[2], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a report at this severity ends the run.</summary>
    private bool Stops(Severity severity)
        => severity == Severity.Error || (PromoteWarnings && severity == Severity.Warning);
}
