using System.Collections.Generic;

namespace Tabbit.History;

/// <summary>
/// What the history already holds for one project and branch.
///
/// An interface so the comparison can be exercised without a database. The MySQL
/// implementation and the in-memory one used by the tests answer the same questions,
/// and the logic that decides what changed does not know which it is talking to.
///
/// Every method is a read that costs a round trip, and the shape reflects it: tables
/// first, then rows only for the tables whose hash moved, then cells only for the rows
/// whose hash moved. Asking for everything up front would send the whole dataset across
/// the network on every build to discover that nothing changed.
/// </summary>
public interface IHistoryState
{
    /// <summary>Every table the branch holds, by name.</summary>
    IReadOnlyDictionary<string, StoredTable> ReadTables();

    /// <summary>The columns of one table, by name.</summary>
    IReadOnlyDictionary<string, StoredField> ReadFields(string table);

    /// <summary>Row key to row hash, for one table.</summary>
    IReadOnlyDictionary<string, string> ReadRowHashes(string table);

    /// <summary>
    /// The cells of the named rows of one table.
    ///
    /// Keyed by row and column together. A blank cell is present with a null value,
    /// which is how "the cell was blank" is told from "the column did not exist".
    /// </summary>
    IReadOnlyDictionary<CellAddress, string?> ReadCells(string table, IReadOnlyCollection<string> rowKeys);

    /// <summary>Every enum and constant set, keyed by kind and name.</summary>
    IReadOnlyDictionary<EntityAddress, StoredEntity> ReadEntities();

    /// <summary>The labels or constants of one entity, by name.</summary>
    IReadOnlyDictionary<string, string> ReadMembers(EntityAddress entity);
}

/// <summary>One cell's address within a table.</summary>
public readonly struct CellAddress : System.IEquatable<CellAddress>
{
    public CellAddress(string rowKey, string field)
    {
        RowKey = rowKey;
        Field = field;
    }

    public string RowKey { get; }
    public string Field { get; }

    public bool Equals(CellAddress other)
        => string.Equals(RowKey, other.RowKey, System.StringComparison.Ordinal)
           && string.Equals(Field, other.Field, System.StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CellAddress other && Equals(other);

    public override int GetHashCode() => System.HashCode.Combine(RowKey, Field);

    public override string ToString() => $"{RowKey}.{Field}";
}

/// <summary>An enum or a constant set, named.</summary>
public readonly struct EntityAddress : System.IEquatable<EntityAddress>
{
    public EntityAddress(EntityKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public EntityKind Kind { get; }
    public string Name { get; }

    public bool Equals(EntityAddress other)
        => Kind == other.Kind && string.Equals(Name, other.Name, System.StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is EntityAddress other && Equals(other);

    public override int GetHashCode() => System.HashCode.Combine(Kind, Name);

    public override string ToString() => $"{Kind}:{Name}";
}

/// <summary>A table as the history last saw it.</summary>
public sealed class StoredTable
{
    public required string Name { get; set; }
    public required string Hash { get; set; }
    public required string SchemaHash { get; set; }
}

/// <summary>A column as the history last saw it.</summary>
public sealed class StoredField
{
    public required string Name { get; set; }
    public required string Hash { get; set; }

    /// <summary>The column's attributes, as the JSON a schema change reports.</summary>
    public required string Descriptor { get; set; }
}

/// <summary>An enum or constant set as the history last saw it.</summary>
public sealed class StoredEntity
{
    public EntityKind Kind { get; set; }
    public required string Name { get; set; }
    public required string Hash { get; set; }
}
