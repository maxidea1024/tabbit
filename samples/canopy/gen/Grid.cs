using System.Text;

namespace Canopy.Gen;

/// <summary>One table's rectangle, as `schema/&lt;name&gt;.tsv` spells it out.</summary>
/// <remarks>
/// This layout puts two header rows and then a variable number of constraint rows inside the
/// rectangle, so the grid file is laid out the same way and in the same order:
///
/// <code>
/// @name        property names        -> rectangle row 1
/// @type        types                 -> rectangle row 2
/// :required    per-column cells      -> rectangle row 3
/// :min         ...                      and so on, as many as this table declares
/// @gen         synthesis rules       -> consumed here
///              literal rows          -> data rows, ahead of the synthesised ones
/// </code>
///
/// The constraint rows are identified by their first cell rather than by position, which is
/// what lets one table have five of them and the next six. Keeping that variation in the grid
/// files is deliberate: a reader that went by row number would pass a corpus where every table
/// declared the same set.
/// </remarks>
internal sealed class Grid
{
    public required string Name { get; init; }

    public required string[] Names { get; init; }

    public required string[] Types { get; init; }

    /// <summary>Constraint rows in the order they appear, each keyed by its `:name`.</summary>
    public required List<(string Key, string[] Cells)> Constraints { get; init; }

    public required string[] Gen { get; init; }

    public required List<string[]> Literal { get; init; }

    /// <summary>A tile grid holds no header at all - the rectangle is integers. 0 when not one.</summary>
    public int TileWidth { get; init; }

    public int TileHeight { get; init; }

    public bool IsTile => TileWidth > 0;

    public int Width => Names.Length;

    /// <summary>Rows the rectangle spends before its data: two headers plus the constraints.</summary>
    public int HeaderRows => 2 + Constraints.Count;

    public static Grid Read(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var rows = new List<string[]>();
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line.StartsWith("//"))
                continue;

            rows.Add(line.Split('\t'));
        }

        if (rows.Count > 0 && rows[0][0] == "@tile")
        {
            return new Grid
            {
                Name = name,
                Names = [],
                Types = [],
                Constraints = [],
                Gen = [],
                Literal = [],
                TileWidth = int.Parse(rows[0][1]),
                TileHeight = int.Parse(rows[0][2]),
            };
        }

        if (rows.Count < 3 || rows[0][0] != "@name" || rows[1][0] != "@type")
            throw new InvalidDataException($"{name}: must open with `@name` and `@type`.");

        var names = rows[0].Skip(1).ToArray();
        var types = rows[1].Skip(1).ToArray();

        var constraints = new List<(string, string[])>();
        int at = 2;
        for (; at < rows.Count && rows[at][0].StartsWith(':'); at++)
        {
            // A backslash-n in a constraint cell is a real line break in the sheet. `:links`
            // writes one target per line, and a tab-separated file cannot hold the break.
            var cells = rows[at].Skip(1)
                .Select(cell => cell.Replace("\\n", "\n"))
                .ToArray();

            constraints.Add((rows[at][0], Pad(cells, names.Length)));
        }

        if (at >= rows.Count || rows[at][0] != "@gen")
            throw new InvalidDataException($"{name}: the constraint rows must be followed by `@gen`.");

        var gen = rows[at].Skip(1).ToArray();

        // A ragged header is the one mistake that yields a plausible workbook with the wrong
        // column count, so it ends the run rather than being padded over.
        foreach (var (row, key) in new[] { (types, "@type"), (gen, "@gen") })
        {
            if (row.Length != names.Length)
                throw new InvalidDataException(
                    $"{name}: `{key}` has {row.Length} cells, `@name` has {names.Length}.");
        }

        return new Grid
        {
            Name = name,
            Names = names,
            Types = types,
            Constraints = constraints,
            Gen = gen,
            Literal = rows.Skip(at + 1).Select(row => Pad(row, names.Length)).ToList(),
        };
    }

    private static string[] Pad(string[] row, int width)
    {
        if (row.Length == width)
            return row;

        var padded = new string[width];
        for (int i = 0; i < width; i++)
            padded[i] = i < row.Length ? row[i] : "-";

        return padded;
    }
}

/// <summary>
/// One defined name: which workbook it lives in, which grid fills it, and how many rows it
/// gets at each scale.
/// </summary>
/// <remarks>
/// The table's name is the defined name, and the sheet is named after it. A row whose grid is
/// `-` is a working sheet: content, no defined name, and this layout passes over it with a log
/// line. A corpus without any of those would not exercise that path, and these workbooks always
/// have some.
/// </remarks>
internal sealed record Entry(string Workbook, string Name, string Grid, int Small, int Live)
{
    public bool IsWorkingSheet => Grid == "-";

    public int RowsFor(string scale) => scale == "live" ? Live : Small;

    public static List<Entry> Read(string path)
    {
        var plan = new List<Entry>();
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith('@'))
                continue;

            var cells = line.Split('\t');
            if (cells.Length < 5)
                throw new InvalidDataException($"tables.tsv: `{line}` has {cells.Length} cells, needs 5.");

            plan.Add(new Entry(cells[0], cells[1], cells[2], int.Parse(cells[3]), int.Parse(cells[4])));
        }

        return plan;
    }
}
