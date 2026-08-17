using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mabbit;

/// <summary>
/// The table geometry, read from a file another tool wrote.
/// </summary>
/// <remarks>
/// Where a table starts and ends is not something a sheet says. The answer lives in whatever
/// convention the sheets were written to, and the program that knows that convention is the
/// one that converts them - so it writes the answer out (`tabbit --dump-schema`) and this
/// reads it.
///
/// A file rather than a library call, and that is the point: this program links against
/// nothing but a spreadsheet reader, and the two can be built, shipped and versioned apart.
/// What crosses between them is a few numbers per table.
///
/// Matched by file name rather than by path. A merge driver is handed the two sides as
/// temporary files with generated names, so the only name that means anything is the one the
/// repository knows the file by - which arrives as `--path`.
/// </remarks>
internal sealed class SchemaFile : ITableSchema
{
    private readonly List<TableRegion> _regions;
    private readonly string _matched;

    private SchemaFile(string matched, List<TableRegion> regions)
    {
        _matched = matched;
        _regions = regions;
    }

    /// <param name="path">The file `--dump-schema` wrote.</param>
    /// <param name="workbook">
    /// The name of the workbook being merged, as the repository knows it.
    /// </param>
    public static SchemaFile Read(string path, string workbook)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(workbook);

        if (!File.Exists(path))
            throw new MabbitException($"`{path}` does not exist, so there is no schema to read.");

        Document? document;

        try
        {
            document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException error)
        {
            throw new MabbitException($"`{path}` is not a schema this can read: {error.Message}");
        }

        if (document?.Tables is null || document.Tables.Count == 0)
            throw new MabbitException($"`{path}` names no tables.");

        string wanted = Path.GetFileName(workbook);

        var forWorkbook = document.Tables
            .Where(t => string.Equals(Path.GetFileName(t.Workbook), wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (forWorkbook.Count == 0)
        {
            // Named rather than counted. Being handed the schema of a different project is
            // the likely mistake here, and "no tables" would read as "this workbook is empty".
            string known = string.Join(", ", document.Tables
                .Select(t => Path.GetFileName(t.Workbook))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .Take(8));

            throw new MabbitException(
                $"`{path}` has no tables in `{wanted}`. It describes: {known}"
                + (document.Tables.Count > 8 ? ", and more." : "."));
        }

        var regions = forWorkbook
            .Select(t => new TableRegion(
                Name: t.Name,
                Sheet: t.Sheet,
                HeaderRow: t.HeaderRow,
                FirstDataRow: t.FirstDataRow,
                LastDataRow: t.LastDataRow,
                FirstColumn: t.FirstColumn,
                LastColumn: t.LastColumn,
                KeyColumn: t.KeyColumn))
            .ToList();

        return new SchemaFile(wanted, regions);
    }

    public IReadOnlyList<TableRegion> TablesIn(WorkbookGrid workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        // Only the tables whose sheet this file actually has. The three sides of a merge are
        // three versions of one workbook and one of them may not have a sheet yet, which is a
        // thing the merge judges rather than something to fail on here.
        return _regions.Where(r => workbook.Sheet(r.Sheet) is not null).ToList();
    }

    public override string ToString() => $"{_matched}: {_regions.Count} table(s)";

    private sealed class Document
    {
        [JsonPropertyName("tables")]
        public List<Entry>? Tables { get; set; }
    }

    private sealed class Entry
    {
        public string Name { get; set; } = "";
        public string Workbook { get; set; } = "";
        public string Sheet { get; set; } = "";
        public int HeaderRow { get; set; }
        public int FirstDataRow { get; set; }
        public int LastDataRow { get; set; }
        public int FirstColumn { get; set; }
        public int LastColumn { get; set; }
        public int KeyColumn { get; set; }
    }
}
