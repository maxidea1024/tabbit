using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;
using Tabbit.Models;

namespace Tabbit;

/// <summary>
/// Where each table sits in the sheet it came from.
/// </summary>
/// <remarks>
/// The one thing this program knows that a tool working on the same workbooks cannot work out
/// for itself: which rectangle of which sheet is a table, and which of its columns identifies
/// a row. A sheet does not say so - the answer is in the layout the recipe names, and reaching
/// it needs every workbook of the source open together, because a column's type may name an
/// enum declared in a different file.
///
/// Written as a file rather than exposed as a library. Whatever reads it does not link against
/// this program, does not carry its dependencies, and does not have to be rebuilt when it
/// changes - which is the whole point of the tools that consume it being separate programs.
///
/// Geometry only. No types, no enum labels, no references: a tool that needs those is a tool
/// that should be cooking the workbooks itself.
/// </remarks>
public sealed class SheetSchema
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    [JsonProperty("tool")]
    public string Tool { get; set; } = "";

    [JsonProperty("tables")]
    public List<SheetSchemaTable> Tables { get; set; } = [];

    /// <summary>Reads the geometry of every table of a cooked model.</summary>
    public static SheetSchema Of(Model model, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(model);

        var schema = new SheetSchema { Tool = toolVersion };

        foreach (var table in model.Tables)
        {
            var described = Describe(table);

            if (described is not null)
                schema.Tables.Add(described);
        }

        return schema;
    }

    /// <summary>
    /// One table's rectangle, or null when it has no cell that says where it is.
    /// </summary>
    /// <remarks>
    /// The bounds come from the cells themselves rather than from anything a layout recorded,
    /// because a layout is free to find its tables however it likes and every one of them
    /// ends up with cells that know where they are. A table with none - built by a test, or
    /// read from somewhere that is not a sheet - has no geometry to report and is left out.
    /// </remarks>
    private static SheetSchemaTable? Describe(Table table)
    {
        int firstRow = int.MaxValue, lastRow = -1;
        int firstColumn = int.MaxValue, lastColumn = -1;
        int headerRow = int.MaxValue;

        string workbook = "", sheet = "";

        foreach (var field in table.Fields)
        {
            var at = field.NameLocation;

            if (at is null || string.IsNullOrEmpty(at.Sheet))
                continue;

            workbook = at.Filename;
            sheet = at.Sheet;

            headerRow = Math.Min(headerRow, at.Row);
            firstColumn = Math.Min(firstColumn, at.Column);
            lastColumn = Math.Max(lastColumn, at.Column);
        }

        foreach (var row in table.Data)
        {
            foreach (var field in table.Fields)
            {
                var at = row[field.Index]?.RawCell?.Location;

                if (at is null || string.IsNullOrEmpty(at.Sheet))
                    continue;

                firstRow = Math.Min(firstRow, at.Row);
                lastRow = Math.Max(lastRow, at.Row);
                firstColumn = Math.Min(firstColumn, at.Column);
                lastColumn = Math.Max(lastColumn, at.Column);
            }
        }

        if (lastColumn < 0 || string.IsNullOrEmpty(sheet))
            return null;

        // A table with a header and no rows still has a rectangle worth reporting: a merge
        // that takes the first row into it needs to know where the first row would go.
        if (lastRow < 0)
        {
            firstRow = headerRow + 1;
            lastRow = headerRow;
        }

        var keyField = table.Fields.Count > 0 ? table.Fields[0] : null;

        return new SheetSchemaTable
        {
            Name = table.Name,
            Workbook = workbook,
            Sheet = sheet,
            HeaderRow = headerRow == int.MaxValue ? firstRow - 1 : headerRow,
            FirstDataRow = firstRow == int.MaxValue ? headerRow + 1 : firstRow,
            LastDataRow = lastRow,
            FirstColumn = firstColumn,
            LastColumn = lastColumn,

            // Field zero is the primary index by construction, and the cooker has already
            // refused duplicates in it - which is what lets a key address exactly one row.
            KeyColumn = keyField?.NameLocation?.Column ?? firstColumn,
        };
    }

    /// <summary>Writes the schema and says where it went.</summary>
    public static void Write(Model model, string path, string toolVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var schema = Of(model, toolVersion);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonConvert.SerializeObject(schema, Formatting.Indented));

        Log.Information(
            $"Wrote the geometry of {schema.Tables.Count} table(s) to `{path}`, as --dump-schema asks.");
    }
}

/// <summary>One table's rectangle, in the zero based coordinates a sheet counts in.</summary>
public sealed class SheetSchemaTable
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>The file it came from, with `/` separators.</summary>
    [JsonProperty("workbook")]
    public string Workbook { get; set; } = "";

    [JsonProperty("sheet")]
    public string Sheet { get; set; } = "";

    [JsonProperty("headerRow")]
    public int HeaderRow { get; set; }

    [JsonProperty("firstDataRow")]
    public int FirstDataRow { get; set; }

    [JsonProperty("lastDataRow")]
    public int LastDataRow { get; set; }

    [JsonProperty("firstColumn")]
    public int FirstColumn { get; set; }

    [JsonProperty("lastColumn")]
    public int LastColumn { get; set; }

    /// <summary>The column whose value identifies a row.</summary>
    [JsonProperty("keyColumn")]
    public int KeyColumn { get; set; }
}
