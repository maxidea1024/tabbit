using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Serilog;

namespace Tabbit;

/// <summary>
/// How long each step of a run took.
/// </summary>
/// <remarks>
/// A run reports its total and nothing else, so every question about where the time goes has
/// to be answered from outside: run the conversion three times with three different stopping
/// points, then read the file log's timestamps and subtract. That is how the numbers in
/// spec/ops/build-cache.md were obtained, and it is not a measurement anybody will repeat often
/// enough to notice a regression.
///
/// The steps are measured by wrapping the calls rather than by reading the log, which is the
/// reason this exists as a type instead of a script over `logs/`. A log line's category says
/// which step wrote it, not which step was running: the layout registry writes under
/// `Cooking` while the importers are still reading, so import and cook cannot be separated
/// from the log at all.
///
/// Two levels, because a recipe may name twenty-five output entries. The phases go out at
/// `Information`, where they are six lines; the per-entry detail goes out at `Debug`, which
/// the file log always keeps and the console shows only under `--verbose`.
/// </remarks>
public sealed class RunTimings
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static ILogger Log => LogCategory.Timing;

    /// <summary>Below this, a step is not worth a line of its own.</summary>
    private static readonly TimeSpan Negligible = TimeSpan.FromMilliseconds(50);

    private readonly List<Entry> _phases = [];
    private readonly List<Entry> _details = [];

    /// <summary>
    /// Runs from construction, so what the phases do not account for is visible.
    /// </summary>
    /// <remarks>
    /// Without it the percentages are shares of the sum of the phases, which is a number
    /// that always adds to 100% however much of the run went somewhere nobody measured.
    /// </remarks>
    private readonly Stopwatch _wall = Stopwatch.StartNew();

    /// <summary>Names of the phases, so the report and the callers cannot drift apart.</summary>
    public static class Phase
    {
        public const string Deciding = "Deciding";
        public const string Rules = "Rules";
        public const string Importing = "Importing";
        public const string Cooking = "Cooking";
        public const string Validating = "Validating";
        public const string Output = "Output";
        public const string Committing = "Committing";
        public const string Sealing = "Sealing";
    }

    /// <summary>
    /// Measures one phase until the returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// Measuring the same phase twice adds to it rather than listing it twice - validation
    /// runs once before the sources are read and once after the model is built, and those
    /// are one phase asked about at two moments.
    /// </remarks>
    public IDisposable Measure(string name) => new Scope(this, name, _phases);

    /// <summary>Measures one output entry, reported only under `--verbose`.</summary>
    public IDisposable MeasureEntry(string name) => new Scope(this, name, _details);

    /// <summary>
    /// What each phase took, in the order they ran.
    /// </summary>
    /// <remarks>
    /// For the build report, which carries these into a file rather than a log line. The
    /// per-entry detail is not here: it is a `--verbose` reading of one recipe entry, and the
    /// report answers "where did the run go" rather than "which of my twenty-five outputs is
    /// slow".
    /// </remarks>
    public IReadOnlyList<(string Name, TimeSpan Elapsed)> Phases
        => _phases.Select(entry => (entry.Name, entry.Elapsed)).ToList();

    /// <summary>
    /// Writes what each step took.
    /// </summary>
    /// <remarks>
    /// Called only when the run succeeded. The phases of a run that stopped part-way are a
    /// reading of how far it got rather than of what the work costs, and they would be
    /// printed under the message saying what went wrong - where they compete with it.
    /// </remarks>
    public void Report()
    {
        var wall = _wall.Elapsed;

        foreach (var entry in _phases)
            Log.Information(Line(entry.Name, entry.Elapsed, wall));

        // What no phase claimed. Printed only when it is large enough to be worth
        // explaining, which is the case this line exists for - a run whose time is
        // somewhere none of the phases above cover.
        var accounted = TimeSpan.Zero;
        foreach (var entry in _phases)
            accounted += entry.Elapsed;

        var unaccounted = wall - accounted;
        if (unaccounted > Negligible)
            Log.Information(Line("Elsewhere", unaccounted, wall));

        foreach (var entry in _details)
            Log.Debug(Line("  " + entry.Name, entry.Elapsed, wall));
    }

    private static string Line(string name, TimeSpan elapsed, TimeSpan wall)
    {
        double share = wall > TimeSpan.Zero ? elapsed.TotalSeconds / wall.TotalSeconds * 100.0 : 0.0;

        return string.Format(
            CultureInfo.InvariantCulture, "{0,-14} {1,8:0.00} s  {2,3:0}%",
            name, elapsed.TotalSeconds, share);
    }

    /// <summary>
    /// Records one span, adding to the entry of that name when there already is one.
    /// </summary>
    /// <remarks>
    /// Locked because the output entries are timed while they run beside each other, so
    /// several of these close at once. A List that two threads append to loses entries -
    /// which here would be a timing quietly missing from the report rather than a failure.
    ///
    /// The per-entry detail is printed in whatever order the entries finished, which is what
    /// it should say: it is a reading of one recipe entry's own cost, and a run where the
    /// slowest finished first is a run worth seeing that way.
    /// </remarks>
    private void Add(List<Entry> into, string name, TimeSpan elapsed)
    {
        lock (_lock)
        {
            int at = into.FindIndex(entry => entry.Name == name);

            if (at < 0)
                into.Add(new Entry(name, elapsed));
            else
                into[at] = new Entry(name, into[at].Elapsed + elapsed);
        }
    }

    private readonly object _lock = new object();

    private readonly record struct Entry(string Name, TimeSpan Elapsed);

    private sealed class Scope : IDisposable
    {
        private readonly RunTimings _owner;
        private readonly List<Entry> _into;
        private readonly string _name;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _closed;

        public Scope(RunTimings owner, string name, List<Entry> into)
        {
            _owner = owner;
            _name = name;
            _into = into;
        }

        public void Dispose()
        {
            // A `using` cannot dispose twice, but this is also handed out through an
            // interface - and adding the same span a second time would be a wrong number
            // rather than a failure, which is the kind that gets believed.
            if (_closed)
                return;

            _closed = true;
            _stopwatch.Stop();
            _owner.Add(_into, _name, _stopwatch.Elapsed);
        }
    }
}
