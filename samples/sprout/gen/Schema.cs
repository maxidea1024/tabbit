using System.Text;

namespace Sprout.Gen;

/// <summary>One sheet of the corpus, as `schema/&lt;name&gt;.tsv` spells it out.</summary>
/// <remarks>
/// The file holds the three header rows this layout reads, a fourth row this generator
/// consumes and does not write, and any literal data rows that follow.
///
/// <code>
/// row 1   @desc   column descriptions   -> sheet row 1
/// row 2   @name   column names          -> sheet row 2
/// row 3   @type   column types          -> sheet row 3
/// row 4   @gen    synthesis rules       -> consumed here
/// row 5+          literal rows          -> sheet rows 4+
/// </code>
///
/// Splitting the notation from the values is the point: a change to how this layout is
/// read shows up as a diff in these files, and a change to the data does not.
/// </remarks>
internal sealed class Schema
{
    public required string Name { get; init; }

    /// <summary>Column descriptions. A `#` here drops the column - the layout says so.</summary>
    public required string[] Desc { get; init; }

    public required string[] Names { get; init; }

    public required string[] Types { get; init; }

    /// <summary>Per-column synthesis rule, in the language <see cref="Synth"/> reads.</summary>
    public required string[] Gen { get; init; }

    /// <summary>Rows written verbatim, ahead of any synthesised ones.</summary>
    public required List<string[]> Literal { get; init; }

    public int Width => Names.Length;

    public static Schema Read(string path)
    {
        var rows = new List<string[]>();
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            // A blank line is a spacer; a line whose first cell is `//` is a note to the
            // author. Neither reaches the workbook, and neither is data.
            if (line.Length == 0 || line.StartsWith("//"))
                continue;

            rows.Add(line.Split('\t'));
        }

        string name = Path.GetFileNameWithoutExtension(path);
        if (rows.Count < 4)
            throw new InvalidDataException($"{name}: needs four header rows, has {rows.Count}.");

        string[] Header(int index, string key)
        {
            var row = rows[index];
            if (row.Length == 0 || row[0] != key)
                throw new InvalidDataException($"{name}: row {index + 1} must start with `{key}`.");

            return row.Skip(1).ToArray();
        }

        var desc = Header(0, "@desc");
        var names = Header(1, "@name");
        var types = Header(2, "@type");
        var gen = Header(3, "@gen");

        // Ragged header rows are the one mistake that produces a plausible-looking workbook
        // with the wrong column count, so they end the run rather than being padded.
        foreach (var (row, key) in new[] { (desc, "@desc"), (types, "@type"), (gen, "@gen") })
        {
            if (row.Length != names.Length)
                throw new InvalidDataException(
                    $"{name}: `{key}` has {row.Length} cells, `@name` has {names.Length}.");
        }

        return new Schema
        {
            Name = name,
            Desc = desc,
            Names = names,
            Types = types,
            Gen = gen,
            Literal = rows.Skip(4).Select(row => Pad(row, names.Length)).ToList(),
        };
    }

    private static string[] Pad(string[] row, int width)
    {
        if (row.Length == width)
            return row;

        var padded = new string[width];
        for (int i = 0; i < width; i++)
            padded[i] = i < row.Length ? row[i] : string.Empty;

        return padded;
    }
}

/// <summary>Which grid becomes which tab of which workbook, and how many rows it gets.</summary>
/// <remarks>
/// `gen/workbooks.tsv` holds one line per sheet: workbook, tab, grid, and the row count at
/// each scale. Two scales because the committed corpus and the benchmark corpus are the same
/// sheets at different sizes - see notes/synthetic-samples.md.
/// </remarks>
internal sealed record Placement(string Workbook, string Tab, string Grid, int Small, int Live)
{
    public int RowsFor(string scale) => scale == "live" ? Live : Small;

    public static List<Placement> Read(string path)
    {
        var plan = new List<Placement>();
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("@"))
                continue;

            var cells = line.Split('\t');
            if (cells.Length < 5)
                throw new InvalidDataException($"workbooks.tsv: `{line}` has {cells.Length} cells, needs 5.");

            plan.Add(new Placement(
                cells[0], cells[1], cells[2],
                int.Parse(cells[3]), int.Parse(cells[4])));
        }

        return plan;
    }
}
