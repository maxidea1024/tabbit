using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.Streaming;

namespace Canopy.Gen;

/// <summary>
/// Builds the `canopy` corpus: a live-service title's workbooks, where a table is a defined
/// name rather than a marked-up sheet.
/// </summary>
/// <remarks>
/// Every value here is synthesised. What is reproduced from a real corpus of this size is its
/// shape - the table count, the column count, how much of it is nested, and the notation the
/// sheets are written in. notes/synthetic-samples.md says why that is the thing worth
/// reproducing.
///
/// <code>
///   dotnet run --project samples/canopy/gen -- --scale small   # the committed corpus
///   dotnet run --project samples/canopy/gen -- --scale live    # the full-size corpus
/// </code>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        string scale = Argument(args, "--scale") ?? "small";
        if (scale is not ("small" or "live"))
        {
            Console.Error.WriteLine($"--scale is `small` or `live`, not `{scale}`.");
            return 1;
        }

        string? root = FindRoot(AppContext.BaseDirectory);
        if (root is null)
        {
            Console.Error.WriteLine("samples/canopy not found above the executable.");
            return 1;
        }

        string outDir = Argument(args, "--out") ?? Path.Combine(root, "xlsx");
        Directory.CreateDirectory(outDir);

        var plan = Entry.Read(Path.Combine(root, "gen", "tables.tsv"));
        var grids = plan
            .Where(e => !e.IsWorkingSheet)
            .Select(e => e.Grid)
            .Distinct()
            .ToDictionary(g => g, g => Grid.Read(Path.Combine(root, "schema", g + ".tsv")));

        var synth = new Synth(Domains(plan, grids, scale), NoEnums);

        int tables = 0, tiles = 0, working = 0;
        long cells = 0, rows = 0;

        foreach (var group in plan.GroupBy(e => e.Workbook))
        {
            // The streaming writer: at full scale one of these workbooks is a hundred sheets
            // and the in-memory one holds every cell object until the file is written.
            using var workbook = new SXSSFWorkbook(null, 256);
            var palette = new Palette(workbook);

            foreach (var entry in group)
            {
                if (entry.IsWorkingSheet)
                {
                    WriteWorkingSheet(workbook.CreateSheet(entry.Name), palette);
                    working++;
                    continue;
                }

                var grid = grids[entry.Grid];
                var sheet = workbook.CreateSheet(entry.Name);
                int height = grid.IsTile
                    ? WriteTile(sheet, grid, entry, synth)
                    : WriteTable(sheet, grid, entry, scale, synth, palette);

                int width = grid.IsTile ? grid.TileWidth : grid.Width;
                Name(workbook, entry.Name, entry.Name, height, width);

                cells += (long)height * width;
                rows += grid.IsTile ? 0 : height - grid.HeaderRows;
                if (grid.IsTile) tiles++;
                tables++;
            }

            string path = Path.Combine(outDir, group.Key + ".xlsx");
            using (var stream = File.Create(path))
                workbook.Write(stream, leaveOpen: false);

            Console.WriteLine($"{group.Key + ".xlsx",-24} {group.Count(),3} sheets");
        }

        Console.WriteLine($"\n{scale}: {plan.Select(e => e.Workbook).Distinct().Count()} workbooks, "
            + $"{tables} tables ({tiles} tile), {working} working sheets, {rows:N0} rows, {cells:N0} cells");
        return 0;
    }

    /// <summary>
    /// Declares the rectangle as a workbook-level defined name. That declaration is the only
    /// thing that makes a sheet a table in this layout.
    /// </summary>
    private static void Name(IWorkbook workbook, string name, string sheet, int height, int width)
    {
        var reference = new AreaReference(
            new CellReference(0, 0), new CellReference(height - 1, width - 1),
            workbook.SpreadsheetVersion);

        var defined = workbook.CreateName();
        defined.NameName = name;
        defined.RefersToFormula = $"'{sheet}'!{reference.FormatAsString()}";
    }

    /// <summary>Writes two header rows, the constraint rows, and then the data.</summary>
    private static int WriteTable(
        ISheet sheet, Grid grid, Entry entry, string scale, Synth synth, Palette palette)
    {
        int total = entry.RowsFor(scale);
        int generated = Math.Max(0, total - grid.Literal.Count);

        Row(sheet, 0, grid.Names, palette.Name);
        Row(sheet, 1, grid.Types, palette.Type);

        int at = 2;
        foreach (var (key, cells) in grid.Constraints)
            Row(sheet, at++, [key, .. cells.Skip(1)], palette.Constraint);

        foreach (var literal in grid.Literal)
            Row(sheet, at++, literal, null);

        if (generated > 0)
        {
            var columns = new string[grid.Width][];
            for (int c = 0; c < grid.Width; c++)
                columns[c] = synth.Column(grid.Name, grid.Names[c], grid.Gen[c], generated, 1);

            var buffer = new string[grid.Width];
            for (int r = 0; r < generated; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                    buffer[c] = columns[c][r];

                Row(sheet, at++, buffer, null);
            }
        }

        return at;
    }

    /// <summary>
    /// Writes a tile grid: no header, no key, the whole rectangle an integer field.
    /// </summary>
    private static int WriteTile(ISheet sheet, Grid grid, Entry entry, Synth synth)
    {
        var buffer = new string[grid.TileWidth];
        for (int r = 0; r < grid.TileHeight; r++)
        {
            // One draw per row rather than per cell: a real tile map is regions of one value,
            // and a per-cell draw would be noise that no encoder can do anything with.
            var line = synth.Column($"{grid.Name}#{r}", "t", "int:0..7:zipf", grid.TileWidth, 1);
            Array.Copy(line, buffer, grid.TileWidth);
            Row(sheet, r, buffer, null);
        }

        return grid.TileHeight;
    }

    /// <summary>
    /// A sheet with content and no defined name. The layout logs it and moves on, which is the
    /// behaviour these workbooks need: notes and pivot scratch sit beside the data.
    /// </summary>
    private static void WriteWorkingSheet(ISheet sheet, Palette palette)
    {
        Row(sheet, 0, ["note", "owner", "updated"], palette.Name);
        for (int r = 1; r <= 12; r++)
            Row(sheet, r, [$"pending review {r}", "design", "2026-08-01"], null);
    }

    private static void Row(ISheet sheet, int index, string[] cells, ICellStyle? style)
    {
        var row = sheet.CreateRow(index);
        for (int c = 0; c < cells.Length; c++)
        {
            if (cells[c].Length == 0 && style is null)
                continue;

            var cell = row.CreateCell(c);
            cell.SetCellValue(cells[c]);
            if (style is not null)
                cell.CellStyle = style;
        }
    }

    /// <summary>
    /// The index values each table will hold, worked out before any of them are generated so
    /// that a `ref:` rule can name a table that comes later in the plan.
    /// </summary>
    private static Dictionary<string, IndexDomain> Domains(
        List<Entry> plan, Dictionary<string, Grid> grids, string scale)
    {
        var domains = new Dictionary<string, IndexDomain>();
        foreach (var entry in plan)
        {
            if (entry.IsWorkingSheet)
                continue;

            var grid = grids[entry.Grid];
            if (grid.IsTile)
                continue;

            var parts = grid.Gen[0].Split(':');
            if (parts[0] != "seq")
                continue;

            int start = parts.Length > 1 ? int.Parse(parts[1]) : 1;
            int step = parts.Length > 2 ? int.Parse(parts[2]) : 1;
            int count = Math.Max(1, entry.RowsFor(scale) - grid.Literal.Count);

            // Keyed by the defined name, not the grid: two tables sharing a grid are still two
            // tables, and a reference to one must not draw ids from the other's range.
            domains[entry.Name] = new IndexDomain(start, step, count);
        }

        return domains;
    }

    /// <summary>
    /// This layout declares no enums, so there are none to resolve. A column whose values come
    /// from a fixed set is a `number` with an `:enum` constraint row listing them.
    /// </summary>
    private static readonly Dictionary<string, string[]> NoEnums = [];

    private static string? Argument(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static string? FindRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "schema"))
                && File.Exists(Path.Combine(dir.FullName, "gen", "tables.tsv")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>Header styling. Cosmetic, and the layout ignores every bit of it.</summary>
internal sealed class Palette
{
    public Palette(IWorkbook workbook)
    {
        Name = Fill(workbook, NPOI.HSSF.Util.HSSFColor.LightCornflowerBlue.Index, bold: true);
        Type = Fill(workbook, NPOI.HSSF.Util.HSSFColor.LightYellow.Index);
        Constraint = Fill(workbook, NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index, italic: true);
    }

    public ICellStyle Name { get; }

    public ICellStyle Type { get; }

    public ICellStyle Constraint { get; }

    private static ICellStyle Fill(IWorkbook workbook, short colour, bool bold = false, bool italic = false)
    {
        var style = workbook.CreateCellStyle();
        style.FillForegroundColor = colour;
        style.FillPattern = FillPattern.SolidForeground;

        var font = workbook.CreateFont();
        font.IsBold = bold;
        font.IsItalic = italic;
        style.SetFont(font);

        return style;
    }
}
