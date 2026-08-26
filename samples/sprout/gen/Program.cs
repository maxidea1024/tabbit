using NPOI.SS.UserModel;
using NPOI.XSSF.Streaming;

namespace Sprout.Gen;

/// <summary>
/// Builds the `sprout` corpus: seventeen workbooks in this project's own layout, from the
/// grids under `schema/`.
/// </summary>
/// <remarks>
/// Every value here is synthesised. What is reproduced from a real corpus is its *shape* -
/// table count, row count, column types, and the statistical properties the binary format's
/// encoders work against. notes/synthetic-samples.md says why that is the thing worth
/// reproducing and what the targets are.
///
/// <code>
///   dotnet run --project samples/sprout/gen -- --scale small   # the committed corpus
///   dotnet run --project samples/sprout/gen -- --scale live    # the benchmark corpus
/// </code>
/// </remarks>
internal static class Program
{
    /// <summary>The enum sheet's tab name. The layout finds declarations by this.</summary>
    private const string EnumSheet = "TableEnums";

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
            Console.Error.WriteLine("samples/sprout not found above the executable.");
            return 1;
        }

        string outDir = Argument(args, "--out") ?? Path.Combine(root, "xlsx");
        Directory.CreateDirectory(outDir);

        var plan = Placement.Read(Path.Combine(root, "gen", "workbooks.tsv"));
        var schemas = plan
            .Select(p => p.Grid)
            .Distinct()
            .ToDictionary(grid => grid, grid => Schema.Read(Path.Combine(root, "schema", grid + ".tsv")));

        var enums = ReadEnums(Path.Combine(root, "schema", EnumSheet + ".tsv"));
        var domains = Domains(plan, schemas, scale);
        var synth = new Synth(domains, enums);

        int tables = 0;
        long cells = 0;
        foreach (var group in plan.GroupBy(p => p.Workbook))
        {
            // The streaming writer, because one sheet here is six figures of rows and the
            // in-memory one holds every cell object until the file is written.
            using var workbook = new SXSSFWorkbook(null, 256);
            var palette = new Palette(workbook);

            foreach (var placement in group)
            {
                var sheet = workbook.CreateSheet(placement.Tab);
                cells += placement.Tab == EnumSheet
                    ? WriteEnumSheet(sheet, schemas[placement.Grid], palette)
                    : WriteTableSheet(sheet, schemas[placement.Grid], placement, scale, synth, palette);

                tables++;
            }

            string path = Path.Combine(outDir, group.Key + ".xlsx");
            using (var stream = File.Create(path))
                workbook.Write(stream, leaveOpen: false);
            Console.WriteLine($"{group.Key + ".xlsx",-22} {group.Count(),2} tabs");
        }

        Console.WriteLine($"\n{scale}: {plan.Select(p => p.Workbook).Distinct().Count()} workbooks, "
            + $"{tables - 1} tables, {Rows(plan, scale):N0} rows, {cells:N0} cells");
        return 0;
    }

    private static long Rows(List<Placement> plan, string scale) =>
        plan.Where(p => p.Tab != EnumSheet).Sum(p => (long)p.RowsFor(scale));

    /// <summary>
    /// Writes one table: three header rows, then the data.
    /// </summary>
    /// <remarks>
    /// Literal rows come first and synthesised ones after, so a hand-written row that a test
    /// depends on keeps its position when the row count changes.
    /// </remarks>
    private static long WriteTableSheet(
        ISheet sheet, Schema schema, Placement placement, string scale, Synth synth, Palette palette)
    {
        int total = placement.RowsFor(scale);
        int generated = Math.Max(0, total - schema.Literal.Count);

        Row(sheet, 0, schema.Desc, palette.Desc);
        Row(sheet, 1, schema.Names, palette.Name);
        Row(sheet, 2, schema.Types, palette.Type);

        int at = 3;
        foreach (var literal in schema.Literal)
            Row(sheet, at++, literal, null);

        if (generated > 0)
        {
            var columns = new string[schema.Width][];
            for (int c = 0; c < schema.Width; c++)
                columns[c] = synth.Column(schema.Name, schema.Names[c], schema.Gen[c], generated, IndexStart(schema));

            var buffer = new string[schema.Width];
            for (int r = 0; r < generated; r++)
            {
                for (int c = 0; c < schema.Width; c++)
                    buffer[c] = columns[c][r];

                Row(sheet, at++, buffer, null);
            }
        }

        return (long)(total + 3) * schema.Width;
    }

    /// <summary>
    /// Writes the enum sheet verbatim. It has no types and no data rows - two header rows
    /// and then labels, with values assigned by the order they appear.
    /// </summary>
    private static long WriteEnumSheet(ISheet sheet, Schema schema, Palette palette)
    {
        Row(sheet, 0, schema.Desc, palette.Desc);
        Row(sheet, 1, schema.Names, palette.Name);

        int at = 2;
        foreach (var labels in schema.Literal)
            Row(sheet, at++, labels, null);

        return (long)at * schema.Width;
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

    /// <summary>Where this sheet's index column starts, so two tables do not share ids.</summary>
    private static int IndexStart(Schema schema)
    {
        var parts = schema.Gen[0].Split(':');
        return parts[0] == "seq" && parts.Length > 1 ? int.Parse(parts[1]) : 1;
    }

    /// <summary>
    /// The index values each sheet will hold, worked out before any of them are generated so
    /// that `ref:` can name a sheet that comes later in the plan.
    /// </summary>
    private static Dictionary<string, IndexDomain> Domains(
        List<Placement> plan, Dictionary<string, Schema> schemas, string scale)
    {
        var domains = new Dictionary<string, IndexDomain>();
        foreach (var placement in plan)
        {
            if (placement.Tab == EnumSheet)
                continue;

            var schema = schemas[placement.Grid];
            var parts = schema.Gen[0].Split(':');
            if (parts[0] != "seq")
                continue;

            int start = parts.Length > 1 ? int.Parse(parts[1]) : 1;
            int step = parts.Length > 2 ? int.Parse(parts[2]) : 1;
            int count = Math.Max(1, placement.RowsFor(scale) - schema.Literal.Count);
            domains[schema.Name] = new IndexDomain(start, step, count);
        }

        return domains;
    }

    /// <summary>
    /// Reads the enum sheet into name to labels. Column 0 of each row holds the first enum's
    /// label, column 1 the second's, and so on; a `#` in the description row marks a column
    /// that describes the enum to its left rather than declaring one.
    /// </summary>
    private static Dictionary<string, string[]> ReadEnums(string path)
    {
        var schema = Schema.Read(path);
        var enums = new Dictionary<string, string[]>();

        for (int c = 0; c < schema.Width; c++)
        {
            if (schema.Desc[c].StartsWith('#') || schema.Names[c].Length == 0)
                continue;

            var labels = schema.Literal
                .Select(row => c < row.Length ? row[c] : string.Empty)
                .Where(label => label.Length > 0)
                .ToArray();

            if (labels.Length > 0)
                enums[schema.Names[c]] = labels;
        }

        return enums;
    }

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
                && File.Exists(Path.Combine(dir.FullName, "gen", "workbooks.tsv")))
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
        Desc = Fill(workbook, NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index, italic: true);
        Name = Fill(workbook, NPOI.HSSF.Util.HSSFColor.LightCornflowerBlue.Index, bold: true);
        Type = Fill(workbook, NPOI.HSSF.Util.HSSFColor.LightYellow.Index);
    }

    public ICellStyle Desc { get; }

    public ICellStyle Name { get; }

    public ICellStyle Type { get; }

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
