using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tabbit.Schema;

namespace Tabbit.Lsp;

/// <summary>What one directory's schema files say, as of the last time they were read.</summary>
internal sealed record DirectoryAnalysis(
    IReadOnlyList<SchemaFile> Files, SchemaDeclarations Declarations);

/// <summary>
/// Reads a directory's `.tbs` files and publishes what is wrong with them.
/// </summary>
/// <remarks>
/// **A directory is the unit.** Everything in one folder is checked as one set, and folders
/// are checked apart from one another - which is what the recipe does, and what keeps two
/// unrelated sample projects in one workspace from reporting each other's names as declared
/// twice. Section 4.2 of spec/ops/lsp.md, where the limit of that choice is written down too.
///
/// **No workbook is opened.** Parsing, the duplicate names and everything `LinkVariants`
/// checks are answered by the files alone; the checks that need sheets live in
/// <see cref="SchemaDeclarations.Resolve"/> and are never called from here.
/// </remarks>
internal sealed class SchemaWorkspace : IDisposable
{
    private readonly DocumentStore _documents;
    private readonly Action<string, IReadOnlyList<LspDiagnostic>> _publish;
    private readonly int _debounceMilliseconds;

    private readonly object _gate = new();
    private readonly Dictionary<string, DirectoryAnalysis> _analyses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Timer> _waiting =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which files each directory was last published about.</summary>
    /// <remarks>
    /// Kept so that a file which has left a directory - deleted, or renamed - is told that it
    /// has nothing wrong with it any more. A client that is never told leaves the last
    /// underline where it was, on a file that no longer exists.
    /// </remarks>
    private readonly Dictionary<string, HashSet<string>> _published =
        new(StringComparer.OrdinalIgnoreCase);

    public SchemaWorkspace(
        DocumentStore documents,
        Action<string, IReadOnlyList<LspDiagnostic>> publish,
        int debounceMilliseconds = 250)
    {
        _documents = documents;
        _publish = publish;
        _debounceMilliseconds = debounceMilliseconds;
    }

    /// <summary>
    /// Says that a file changed, so its directory is read again.
    /// </summary>
    /// <param name="immediate">
    /// True for opening, saving, closing and for a change made outside the editor. Only typing
    /// waits, and only so that a directory is not re-read once per keystroke.
    /// </param>
    public void Touched(string path, bool immediate)
    {
        string directory = DocumentStore.DirectoryOf(path);

        if (immediate || _debounceMilliseconds <= 0)
        {
            Recompute(directory);
            return;
        }

        lock (_gate)
        {
            if (_waiting.TryGetValue(directory, out var running))
            {
                running.Change(_debounceMilliseconds, Timeout.Infinite);
                return;
            }

            _waiting[directory] = new Timer(
                _ => Recompute(directory), null, _debounceMilliseconds, Timeout.Infinite);
        }
    }

    /// <summary>
    /// What this file's directory declares, read again first if a keystroke is still waiting.
    /// </summary>
    /// <remarks>
    /// A request is answered from the current text, never from the text of a quarter of a
    /// second ago: someone who presses F12 on a name they have just typed would otherwise be
    /// sent to where that name used to be.
    /// </remarks>
    public DirectoryAnalysis AnalysisFor(string path)
    {
        string directory = DocumentStore.DirectoryOf(path);

        lock (_gate)
        {
            // A keystroke still waiting means the reading on hand is of text the author has
            // already changed. Take the wait out and read now.
            if (_waiting.TryGetValue(directory, out var running))
            {
                running.Dispose();
                _waiting.Remove(directory);
            }
            else if (_analyses.TryGetValue(directory, out var known))
            {
                return known;
            }

            return Recompute(directory);
        }
    }

    /// <summary>Reads every file of one directory and publishes what they say.</summary>
    private DirectoryAnalysis Recompute(string directory)
    {
        // One directory at a time. Two rounds publishing at once would interleave their
        // reports about the same file, and the second to finish would not be the second to
        // be believed.
        lock (_gate)
        {
            _waiting.Remove(directory);

            var files = _documents.FilesIn(directory);
            var diagnostics = new Diagnostics();

            var parsed = files
                .Select(file => SchemaParser.Parse(file.Text, file.Path, diagnostics))
                .ToList();

            var declarations = SchemaDeclarations.Gather(parsed, diagnostics);
            var analysis = new DirectoryAnalysis(parsed, declarations);

            _analyses[directory] = analysis;
            Publish(directory, files, diagnostics);

            return analysis;
        }
    }

    /// <summary>
    /// Sends one message per file, including the files that have nothing wrong with them.
    /// </summary>
    /// <remarks>
    /// **A clean file is published as an empty list rather than passed over.** Saying nothing
    /// is how a report that has just been fixed stays underlined, and the messages are small
    /// enough that saying it every round costs less than working out when it is needed.
    /// </remarks>
    private void Publish(
        string directory, IReadOnlyList<(string Path, string Text)> files, Diagnostics diagnostics)
    {
        var reports = new Dictionary<string, List<LspDiagnostic>>(StringComparer.OrdinalIgnoreCase);
        var ranges = new Dictionary<string, TokenRanges>(StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, text) in files)
        {
            reports[path] = [];
            texts[path] = text;
        }

        foreach (var (severity, detail) in diagnostics.Entries)
        {
            // A report with no place is about the run rather than about a file, and this
            // server has no run to speak about. The schema checks all carry one.
            if (detail.Location is null)
                continue;

            if (!reports.TryGetValue(detail.Location.Filename, out var list))
                continue;

            if (!ranges.TryGetValue(detail.Location.Filename, out var measured))
            {
                measured = TokenRanges.Of(texts[detail.Location.Filename], detail.Location.Filename);
                ranges[detail.Location.Filename] = measured;
            }

            list.Add(new LspDiagnostic
            {
                Range = measured.RangeAt(detail.Location),
                Severity = SeverityOf(severity),
                Code = detail.MessageId,
                Message = detail.Message,
            });
        }

        foreach (var (path, list) in reports)
            _publish(_documents.UriOf(path), list);

        if (_published.TryGetValue(directory, out var before))
        {
            foreach (string gone in before)
            {
                if (!reports.ContainsKey(gone))
                    _publish(_documents.UriOf(gone), []);
            }
        }

        _published[directory] = new HashSet<string>(reports.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>How heavily the editor should mark a report.</summary>
    private static int SeverityOf(Severity severity) => severity switch
    {
        Severity.Error => 1,
        Severity.Warning => 2,
        _ => 3,
    };

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var waiting in _waiting.Values)
                waiting.Dispose();

            _waiting.Clear();
        }
    }
}
