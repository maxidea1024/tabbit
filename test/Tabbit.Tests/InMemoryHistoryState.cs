using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.History;

namespace Tabbit.Tests;

/// <summary>
/// What the history holds, held in a dictionary.
///
/// For exercising the comparison without a database. It is seeded from a fingerprint,
/// so a test says "the history holds this model" in one line and then asks what changed
/// after an edit - which is the question the comparison exists to answer, and the one a
/// database makes slow to ask.
///
/// It counts its reads. The comparison's whole shape is about not making a round trip
/// it does not need, and a claim about round trips is only worth making if something
/// checks it.
/// </summary>
internal sealed class InMemoryHistoryState : IHistoryState
{
    private readonly Dictionary<string, StoredTable> _tables = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Dictionary<string, StoredField>> _fields =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, Dictionary<string, string>> _rows = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Dictionary<CellAddress, string>> _cells =
        new(StringComparer.Ordinal);

    private readonly Dictionary<EntityAddress, StoredEntity> _entities = new();

    private readonly Dictionary<EntityAddress, Dictionary<string, string>> _members = new();

    /// <summary>An empty history, as a branch has before its first snapshot.</summary>
    public static InMemoryHistoryState Empty() => new InMemoryHistoryState();

    /// <summary>A history holding exactly this model.</summary>
    public static InMemoryHistoryState Of(ModelFingerprint fingerprint)
    {
        var state = new InMemoryHistoryState();

        foreach (var table in fingerprint.Tables)
        {
            state._tables[table.Name] = new StoredTable
            {
                Name = table.Name,
                Hash = table.Hash,
                SchemaHash = table.SchemaHash,
            };

            state._fields[table.Name] = table.Fields.ToDictionary(
                f => f.Name,
                f => new StoredField
                {
                    Name = f.Name,
                    Hash = f.Hash,
                    Descriptor = SnapshotDiff.DescriptorOf(f),
                },
                StringComparer.Ordinal);

            state._rows[table.Name] = table.Rows.ToDictionary(r => r.Key, r => r.Hash, StringComparer.Ordinal);

            var cells = new Dictionary<CellAddress, string>();

            foreach (var row in table.Rows)
            {
                foreach (var cell in table.CellsOf(row))
                    cells[new CellAddress(row.Key, cell.Field)] = cell.Value;
            }

            state._cells[table.Name] = cells;
        }

        foreach (var (entity, kind, _) in Entities(fingerprint))
        {
            var address = new EntityAddress(kind, entity.Name);

            state._entities[address] = new StoredEntity
            {
                Kind = kind,
                Name = entity.Name,
                Hash = entity.Hash,
            };

            state._members[address] = entity.Members.ToDictionary(
                m => m.Name, m => m.Value ?? "", StringComparer.Ordinal);
        }

        return state;
    }

    private static IEnumerable<(EntityFingerprint Entity, EntityKind Kind, EntityKind MemberKind)> Entities(
        ModelFingerprint fingerprint)
    {
        foreach (var entity in fingerprint.Enums)
            yield return (entity, EntityKind.Enum, EntityKind.EnumLabel);

        foreach (var entity in fingerprint.ConstantSets)
            yield return (entity, EntityKind.Constants, EntityKind.Constant);
    }

    // --------------------------------------------------------------- counts

    /// <summary>Tables whose rows were read. One entry per read, so repeats show.</summary>
    public List<string> RowReads { get; } = new List<string>();

    /// <summary>Tables whose columns were read.</summary>
    public List<string> FieldReads { get; } = new List<string>();

    /// <summary>Row keys whose cells were read.</summary>
    public List<string> CellReads { get; } = new List<string>();

    // ---------------------------------------------------------------- reads

    public IReadOnlyDictionary<string, StoredTable> ReadTables() => _tables;

    public IReadOnlyDictionary<string, StoredField> ReadFields(string table)
    {
        FieldReads.Add(table);

        return _fields.TryGetValue(table, out var fields)
            ? fields
            : new Dictionary<string, StoredField>(StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string> ReadRowHashes(string table)
    {
        RowReads.Add(table);

        return _rows.TryGetValue(table, out var rows)
            ? rows
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<CellAddress, string> ReadCells(
        string table, IReadOnlyCollection<string> rowKeys)
    {
        foreach (var key in rowKeys)
            CellReads.Add(key);

        var result = new Dictionary<CellAddress, string>();

        if (!_cells.TryGetValue(table, out var cells))
            return result;

        var wanted = new HashSet<string>(rowKeys, StringComparer.Ordinal);

        foreach (var pair in cells.Where(c => wanted.Contains(c.Key.RowKey)))
            result[pair.Key] = pair.Value;

        return result;
    }

    public IReadOnlyDictionary<EntityAddress, StoredEntity> ReadEntities() => _entities;

    public IReadOnlyDictionary<string, string> ReadMembers(EntityAddress entity)
        => _members.TryGetValue(entity, out var members)
            ? members
            : new Dictionary<string, string>(StringComparer.Ordinal);
}
