using Newtonsoft.Json;

namespace Tabbit.Models;

/// <summary>
/// What makes a table the values of a grid: which field is the row axis, which group holds
/// one row of cells, and where the column axis keys are.
/// </summary>
/// <remarks>
/// **A grid is two ordinary tables and this link between them.** The values table holds one
/// row per row-axis key and an array of cells beside it; the column table holds one row per
/// column-axis key and the position that key has in every one of those arrays. Nothing below
/// this - the wire, the exporters, the runtimes - learns a new shape, because there is no new
/// shape: an array column and a table with an index are what they already carry.
///
/// So this exists for the generators alone. What it buys is the accessor that composes the
/// two lookups, and the check that the two tables were built from the same declaration.
///
/// Named rather than referenced, so a projection that rebuilds the tables does not have to
/// rebuild the link with them - the same way a field names the enum it is typed with.
/// spec/layout/matrix-declaration.md.
/// </remarks>
public sealed class MatrixShape
{
    /// <summary>The values table's field holding the row axis key, which is its index.</summary>
    public required string RowKeyField { get; init; }

    /// <summary>The values table's array group holding one row of cells.</summary>
    public required string GridField { get; init; }

    /// <summary>The table whose rows are the column axis keys.</summary>
    public required string ColumnTable { get; init; }

    /// <summary>That table's field holding the key, which is its index.</summary>
    public required string ColumnKeyField { get; init; }

    /// <summary>That table's field holding the position in the grid arrays.</summary>
    public required string ColumnAtField { get; init; }

    /// <summary>The column table for this shape, or null when the model has no such table.</summary>
    [JsonIgnore]
    public Table? ColumnTableOrNull
        => Model.Current?.Tables.Find(table => table.Name == ColumnTable);
}
