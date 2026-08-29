using Tabbit.Models;
using System.Linq;

namespace Tabbit.CodeGeneration;

/// <summary>
/// The two tables a grid declaration produced, and the fields an accessor over it composes.
/// </summary>
/// <remarks>
/// **Every language builds its surface from this, so none of them re-derives the shape.** The
/// values table and the column table are ordinary tables with ordinary lookups by the time a
/// generator sees them; what a grid adds is that one of those lookups answers with a position
/// into the other's array, and that is the whole of what this holds.
///
/// The same arrangement <see cref="KeyPlans"/> has for keys: one plan computed once, and a
/// template per language that writes one block from it.
/// </remarks>
internal sealed class MatrixPlan
{
    /// <summary>The table holding one row per row-axis key.</summary>
    public required Table Values { get; init; }

    /// <summary>The table holding one row per column-axis key and its position.</summary>
    public required Table Columns { get; init; }

    /// <summary>The values table's index, which is the row axis.</summary>
    public required SerialField RowKey { get; init; }

    /// <summary>The array group holding one row of cells.</summary>
    public required SerialField Grid { get; init; }

    /// <summary>The column table's index, which is the column axis.</summary>
    public required SerialField ColumnKey { get; init; }

    /// <summary>The column table's position field.</summary>
    public required Field At { get; init; }

    /// <summary>Whether a cell of the grid may have no value.</summary>
    public bool CellsAreOptional => Grid.ElementMayBeAbsent;
}

/// <summary>Finds the grid a table is the values of, when it is one.</summary>
internal static class MatrixPlans
{
    /// <summary>
    /// The plan for this table, or null when it is an ordinary table.
    /// </summary>
    /// <remarks>
    /// Null is also the answer when the link points at something this model does not hold -
    /// a projection that kept one of the two tables, or a model assembled by something other
    /// than the layout. A generator then writes the two tables and no accessor over them,
    /// which is the state the tool was in before grids existed and is therefore a state
    /// every template already handles.
    /// </remarks>
    public static MatrixPlan? Of(Table table, Model model)
    {
        if (table.Matrix is not { } shape)
            return null;

        var columns = model.Tables.Find(other => other.Name == shape.ColumnTable);

        if (columns is null)
            return null;

        // The serial fields rather than the plain ones, because a key is spelled by the type
        // its lookup is keyed by: a `foreign` column's field is the row it points at, and the
        // dictionary is keyed by the key it stores.
        var rowKey = table.SerialFields.Find(group => group.Name == shape.RowKeyField);
        var grid = table.SerialFields.Find(group => group.Name == shape.GridField);
        var columnKey = columns.SerialFields.Find(group => group.Name == shape.ColumnKeyField);
        var at = columns.Fields.Find(field => field.Name == shape.ColumnAtField);

        if (rowKey is null || grid is null || columnKey is null || at is null)
            return null;

        return new MatrixPlan
        {
            Values = table,
            Columns = columns,
            RowKey = rowKey,
            Grid = grid,
            ColumnKey = columnKey,
            At = at,
        };
    }

    /// <summary>Whether this table is the column axis of some grid in the model.</summary>
    /// <remarks>
    /// Asked by a generator that wires the two together, so the wiring is written once per
    /// grid rather than once per table that happens to be named like one.
    /// </remarks>
    public static bool IsColumnTable(Table table, Model model)
        => model.Tables.Any(other => other.Matrix?.ColumnTable == table.Name);
}
