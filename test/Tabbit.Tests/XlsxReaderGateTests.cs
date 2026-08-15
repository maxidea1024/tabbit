using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Tabbit.Importers.Xlsx;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What every fixture workbook reads as, recorded cell for cell, so that replacing the
/// workbook reader has to reproduce it.
/// </summary>
/// <remarks>
/// The goldens already pin what a conversion produces, but they pin it through the layout
/// parsers and the generators, so a reader-level regression arrives there as a diff in
/// generated code and has to be traced back. This pins the reader's own output instead: a
/// count of what was read and a hash of the values, per workbook.
///
/// It exists because the reader is a dependency that may be swapped again - the candidates
/// and the measurements are in `spec/streaming-workbook-reader.md`, and one of them was
/// rejected for a defect this comparison is what found. The counts matter as much as the
/// hash: a reader that is fast because it read less is not faster.
///
/// Re-record with TABBIT_UPDATE_GOLDEN=1, the same switch the conversion goldens use.
/// </remarks>
public class XlsxReaderGateTests
{
    private static string RecordPath
        => Path.Combine(RepoLayout.Root, "test", "fixtures", "golden", "xlsx-reader.tsv");

    private static string WorkbookDir
        => Path.Combine(RepoLayout.Root, "test", "fixtures", "xlsx");

    private static bool Recording
        => Environment.GetEnvironmentVariable("TABBIT_UPDATE_GOLDEN") == "1";

    [Fact]
    public void Every_fixture_workbook_reads_as_it_was_recorded()
    {
        var measured = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (string path in Directory
            .EnumerateFiles(WorkbookDir, "*.xlsx", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetRelativePath(WorkbookDir, path).Replace('\\', '/');
            measured[name] = Measure(path);
        }

        Assert.NotEmpty(measured);

        if (Recording)
        {
            var lines = measured.Select(entry => $"{entry.Key}\t{entry.Value}");
            File.WriteAllLines(RecordPath, lines);
            return;
        }

        Assert.True(File.Exists(RecordPath),
            $"No reader record at `{RecordPath}`. "
            + "Run the suite once with TABBIT_UPDATE_GOLDEN=1 to record one.");

        var recorded = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(RecordPath))
        {
            if (line.Trim().Length == 0) continue;

            int tab = line.IndexOf('\t');
            Assert.True(tab > 0, $"Malformed line in `{RecordPath}`: `{line}`");
            recorded[line.Substring(0, tab)] = line.Substring(tab + 1);
        }

        // Reported together rather than failing on the first, because a change to the
        // fixture generator moves many workbooks at once and one line at a time is no way
        // to read that.
        var differences = new List<string>();

        foreach (var entry in measured)
        {
            if (!recorded.TryGetValue(entry.Key, out string was))
            {
                differences.Add($"  new workbook `{entry.Key}`: {entry.Value}");
                continue;
            }

            if (was != entry.Value)
                differences.Add($"  `{entry.Key}`\n      recorded: {was}\n      read now: {entry.Value}");
        }

        foreach (string name in recorded.Keys.Where(k => !measured.ContainsKey(k)))
            differences.Add($"  workbook `{name}` is recorded but no longer present");

        Assert.True(differences.Count == 0,
            "The workbook reader no longer reads the fixtures the way it was recorded to."
            + Environment.NewLine + string.Join(Environment.NewLine, differences)
            + Environment.NewLine
            + "If this is a deliberate change, re-record with TABBIT_UPDATE_GOLDEN=1 "
            + "and review the diff.");
    }

    /// <summary>
    /// Everything about a workbook that a replacement reader would have to get right.
    /// </summary>
    private static string Measure(string path)
    {
        long sheets = 0, rows = 0, cells = 0, chars = 0;
        ulong hash = 14695981039346656037UL;

        try
        {
            using var reader = SheetGridReader.Open(path);

            while (reader.MoveToNextSheet())
            {
                sheets++;
                while (reader.ReadRow())
                {
                    rows++;
                    int columnCount = reader.ColumnCount;
                    for (int column = 0; column < columnCount; column++)
                    {
                        // An error cell is a value the reader declines to render, and what
                        // becomes of it is the importer's decision - so what is recorded here
                        // is that it was seen as one.
                        string value = reader.IsFormulaError(column, out string excelText)
                            ? excelText
                            : reader.Text(column);

                        if (value.Length == 0) continue;

                        cells++;
                        chars += value.Length;

                        // FNV-1a rather than string.GetHashCode, which is salted per process
                        // and so cannot be recorded at all.
                        foreach (char c in value) { hash ^= c; hash *= 1099511628211UL; }
                        hash ^= 1; hash *= 1099511628211UL;
                    }
                }
            }
        }
        catch (Exception e)
        {
            return $"unreadable:{e.GetType().Name}";
        }

        // Names and notes come from the package rather than the cell reader, and a reader
        // swap has to keep them too - they are the doc comments of everything a sheet
        // declares, and the table boundaries of the layouts that read names.
        int names, skipped, notes;
        try
        {
            var package = WorkbookPackage.Read(path, _ => true);
            names = package.DefinedNames.Count;
            skipped = package.SkippedNames.Count;
            notes = CountNotes(package, path);
        }
        catch (Exception e)
        {
            return $"cells={cells} package-unreadable:{e.GetType().Name}";
        }

        return string.Join(" ",
            $"sheets={sheets}",
            $"rows={rows}",
            $"cells={cells}",
            $"chars={chars}",
            $"names={names}",
            $"skipped-names={skipped}",
            $"notes={notes}",
            $"hash={hash.ToString("x16", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// How many cells of the workbook carry a note.
    /// </summary>
    /// <remarks>
    /// Counted by asking about the cells that exist, because the package reader answers per
    /// cell rather than handing out its table - which is what the importer needs of it.
    /// </remarks>
    private static int CountNotes(WorkbookPackage package, string path)
    {
        if (!package.HasNotes) return 0;

        int found = 0;
        using var reader = SheetGridReader.Open(path);

        while (reader.MoveToNextSheet())
        {
            string sheetName = reader.SheetName.Trim();
            while (reader.ReadRow())
            {
                int columnCount = reader.ColumnCount;
                for (int column = 0; column < columnCount; column++)
                {
                    if (package.Note(sheetName, reader.RowIndex, column).Length > 0)
                        found++;
                }
            }
        }

        return found;
    }
}
