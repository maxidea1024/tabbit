using System.Collections.Generic;

namespace Tabbit.History;

/// <summary>What happened to something between two snapshots.</summary>
public enum ChangeKind
{
    Added,
    Modified,
    Removed,
}

/// <summary>What kind of thing a schema change happened to.</summary>
public enum EntityKind
{
    Table,
    Field,
    Enum,
    EnumLabel,
    Constants,
    Constant,
}

/// <summary>
/// Everything that moved between the previous snapshot of a branch and this one.
///
/// Three lists rather than one, because they answer different questions and are read
/// separately. A schema change is a handful of rows a person reads in full; a cell
/// change can be hundreds of thousands and is always filtered.
/// </summary>
public sealed class SnapshotChanges
{
    public SnapshotChanges(
        IReadOnlyList<SchemaChange> schema,
        IReadOnlyList<RowChange> rows,
        IReadOnlyList<CellChange> cells)
    {
        Schema = schema;
        Rows = rows;
        Cells = cells;
    }

    public IReadOnlyList<SchemaChange> Schema { get; }

    public IReadOnlyList<RowChange> Rows { get; }

    public IReadOnlyList<CellChange> Cells { get; }

    public bool IsEmpty => Schema.Count == 0 && Rows.Count == 0 && Cells.Count == 0;

    public int Count => Schema.Count + Rows.Count + Cells.Count;

    public override string ToString()
        => $"{Schema.Count} schema, {Rows.Count} row, {Cells.Count} cell change(s)";
}

/// <summary>A table, column, enum, label or constant that appeared, changed or went.</summary>
public sealed class SchemaChange
{
    public EntityKind EntityKind { get; set; }

    /// <summary>The table, enum or constant set this concerns.</summary>
    public required string EntityName { get; set; }

    /// <summary>The column, label or constant within it. Null when the entity itself changed.</summary>
    public string? MemberName { get; set; }

    public ChangeKind Kind { get; set; }

    /// <summary>How it was. Null for something that was added.</summary>
    public string? Before { get; set; }

    /// <summary>How it is. Null for something that was removed.</summary>
    public string? After { get; set; }

    /// <summary>Where in the sheets it is now, so a report can link to it.</summary>
    public SummaryLocation? Location { get; set; }

    /// <summary>
    /// The name this column used to have, when it was renamed rather than replaced.
    ///
    /// A rename is a drop and an add as far as the data is concerned - every cell of
    /// the old column goes and every cell of the new one arrives - so a 5000-row table
    /// produces ten thousand cell changes for an edit that changed no value at all.
    /// Recognising it is what stops that burying the edits that did.
    /// </summary>
    public string? RenamedFrom { get; set; }
}

/// <summary>A row that appeared, changed or went.</summary>
public sealed class RowChange
{
    public required string Table { get; set; }

    /// <summary>The row's primary index, as text.</summary>
    public required string RowKey { get; set; }

    public ChangeKind Kind { get; set; }
}

/// <summary>
/// One cell, before and after.
///
/// This is the answer to who changed what and when - who and when come from the
/// snapshot this belongs to, and what is here.
/// </summary>
public sealed class CellChange
{
    public required string Table { get; set; }

    public required string RowKey { get; set; }

    public required string Field { get; set; }

    public ChangeKind Kind { get; set; }

    /// <summary>The value before. Null means the cell was blank, or is new.</summary>
    public string? OldValue { get; set; }

    /// <summary>The value after. Null means the cell is blank now, or the row is gone.</summary>
    public string? NewValue { get; set; }

    /// <summary>The cell in the sheet, which is what a report links to.</summary>
    public SummaryLocation? Location { get; set; }
}
