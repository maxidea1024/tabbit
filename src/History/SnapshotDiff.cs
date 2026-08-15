using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Tabbit.Models;

namespace Tabbit.History;

/// <summary>
/// Works out what changed between what the history holds and what a conversion just
/// produced.
///
/// Descends one level at a time, because every level costs a round trip to a remote
/// database. Table hashes first; rows only for the tables whose hash moved; cells only
/// for the rows whose hash moved. A conversion that changed nothing reads one hash per
/// table and stops.
///
/// The descent is also why the hashes have to be exactly as sensitive as they are: one
/// that misses a change means this never looks, and the edit is lost with no symptom
/// at all. <see cref="ModelFingerprint"/> is where that is held.
/// </summary>
public static class SnapshotDiff
{
    /// <summary>
    /// How many rows' cells are read in one query.
    ///
    /// A parameter list of unbounded length is a query the server may refuse and a plan
    /// it cannot reuse. Chunking also bounds the memory a single changed table costs.
    /// </summary>
    private const int RowChunk = 500;

    /// <summary>
    /// Compares a conversion against the branch's current state.
    /// </summary>
    public static SnapshotChanges Compute(ModelFingerprint fingerprint, IHistoryState state)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(state);

        var schema = new List<SchemaChange>();
        var rows = new List<RowChange>();
        var cells = new List<CellChange>();

        DiffTables(fingerprint, state, schema, rows, cells);
        DiffEntities(fingerprint, state, schema);

        RecogniseRenames(schema, cells);

        return new SnapshotChanges(schema, rows, cells);
    }

    /// <summary>
    /// Turns a dropped column and an added one holding the same values into one rename.
    ///
    /// To the data a rename is a drop and an add: every cell of the old column goes and
    /// every cell of the new one arrives. A five thousand row table therefore produces
    /// ten thousand cell changes for an edit that changed no value at all, and the edits
    /// that did change something are somewhere in the middle of them.
    ///
    /// The cell changes stay. They are what moves the stored state from the old column
    /// to the new one, and dropping them would leave the old name in the store for ever.
    /// What changes is that the schema log says "renamed", so a report can say so too
    /// and fold the carry-over away.
    ///
    /// Only an exact match counts: same rows, same values, every one of them. A column
    /// renamed and edited in the same commit is left as a drop and an add, which is
    /// less tidy and cannot mislead.
    /// </summary>
    private static void RecogniseRenames(List<SchemaChange> schema, List<CellChange> cells)
    {
        var dropped = schema.Where(s => s.EntityKind == EntityKind.Field
                                        && s.Kind == ChangeKind.Removed).ToList();

        if (dropped.Count == 0)
            return;

        var added = schema.Where(s => s.EntityKind == EntityKind.Field
                                      && s.Kind == ChangeKind.Added).ToList();

        if (added.Count == 0)
            return;

        // Indexed once: a table with several columns dropped and added at once would
        // otherwise walk the whole cell list for every pair.
        var byColumn = new Dictionary<(string Table, string Field), Dictionary<string, string?>>();

        foreach (var cell in cells)
        {
            if (cell.Kind == ChangeKind.Modified)
                continue;

            var key = (cell.Table, cell.Field);

            if (!byColumn.TryGetValue(key, out var values))
                byColumn[key] = values = new Dictionary<string, string?>(StringComparer.Ordinal);

            values[cell.RowKey] = cell.Kind == ChangeKind.Removed ? cell.OldValue : cell.NewValue;
        }

        var claimed = new HashSet<SchemaChange>();

        foreach (var arrival in added)
        {
            foreach (var departure in dropped)
            {
                if (claimed.Contains(departure)
                    || !string.Equals(arrival.EntityName, departure.EntityName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!SameValues(byColumn, arrival.EntityName, departure.MemberName, arrival.MemberName))
                    continue;

                arrival.Kind = ChangeKind.Modified;
                arrival.RenamedFrom = departure.MemberName;
                arrival.Before = departure.Before;

                claimed.Add(departure);
                break;
            }
        }

        schema.RemoveAll(claimed.Contains);
    }

    private static bool SameValues(
        IReadOnlyDictionary<(string, string), Dictionary<string, string?>> byColumn,
        string table,
        string? before,
        string? after)
    {
        // A column with no cells either side says nothing - an empty table renames
        // nothing detectably, and calling that a match would pair columns at random.
        if (before is null || after is null
            || !byColumn.TryGetValue((table, before), out var was)
            || !byColumn.TryGetValue((table, after), out var now)
            || was.Count == 0
            || was.Count != now.Count)
        {
            return false;
        }

        foreach (var pair in was)
        {
            if (!now.TryGetValue(pair.Key, out string? value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // --------------------------------------------------------------- tables

    private static void DiffTables(
        ModelFingerprint fingerprint,
        IHistoryState state,
        List<SchemaChange> schema,
        List<RowChange> rows,
        List<CellChange> cells)
    {
        var stored = state.ReadTables();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in fingerprint.Tables)
        {
            seen.Add(table.Name);

            if (!stored.TryGetValue(table.Name, out var before))
            {
                schema.Add(new SchemaChange
                {
                    EntityKind = EntityKind.Table,
                    EntityName = table.Name,
                    Kind = ChangeKind.Added,
                    After = table.SchemaHash,
                    Location = LocationOf(table.Location),
                });

                // Everything in a new table is new. No read: there is nothing stored to
                // compare against, and asking would be a round trip for an empty answer.
                AddWholeTable(table, rows, cells);

                DiffFields(table, EmptyFields, schema);
                continue;
            }

            // The one comparison that decides whether this table costs anything at all.
            if (string.Equals(before.Hash, table.Hash, StringComparison.Ordinal))
                continue;

            if (!string.Equals(before.SchemaHash, table.SchemaHash, StringComparison.Ordinal))
                DiffFields(table, state.ReadFields(table.Name), schema);

            DiffRows(table, state, rows, cells);
        }

        foreach (var gone in stored.Keys.Where(name => !seen.Contains(name)).OrderBy(n => n, StringComparer.Ordinal))
        {
            schema.Add(new SchemaChange
            {
                EntityKind = EntityKind.Table,
                EntityName = gone,
                Kind = ChangeKind.Removed,
                Before = stored[gone].SchemaHash,
            });

            // The rows go with it, and each is reported so a range query over a table
            // that no longer exists still says what was in it.
            foreach (var rowKey in state.ReadRowHashes(gone).Keys.OrderBy(k => k, StringComparer.Ordinal))
                rows.Add(new RowChange { Table = gone, RowKey = rowKey, Kind = ChangeKind.Removed });
        }
    }

    private static readonly IReadOnlyDictionary<string, StoredField> EmptyFields =
        new Dictionary<string, StoredField>(StringComparer.Ordinal);

    private static void AddWholeTable(
        TableFingerprint table, List<RowChange> rows, List<CellChange> cells)
    {
        foreach (var row in table.Rows)
        {
            rows.Add(new RowChange { Table = table.Name, RowKey = row.Key, Kind = ChangeKind.Added });

            foreach (var cell in table.CellsOf(row))
            {
                // A blank cell in a new row is not a change. There was nothing, and
                // there is nothing; recording it would fill the history with rows
                // saying so.
                if (cell.Value is null)
                    continue;

                cells.Add(new CellChange
                {
                    Table = table.Name,
                    RowKey = row.Key,
                    Field = cell.Field,
                    Kind = ChangeKind.Added,
                    NewValue = cell.Value,
                    Location = LocationOf(cell.Location),
                });
            }
        }
    }

    private static void DiffRows(
        TableFingerprint table, IHistoryState state, List<RowChange> rows, List<CellChange> cells)
    {
        var stored = state.ReadRowHashes(table.Name);

        var changed = new List<RowFingerprint>();
        var added = new List<RowFingerprint>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            seen.Add(row.Key);

            if (!stored.TryGetValue(row.Key, out string? before))
            {
                added.Add(row);
                rows.Add(new RowChange { Table = table.Name, RowKey = row.Key, Kind = ChangeKind.Added });
            }
            else if (!string.Equals(before, row.Hash, StringComparison.Ordinal))
            {
                changed.Add(row);
                rows.Add(new RowChange { Table = table.Name, RowKey = row.Key, Kind = ChangeKind.Modified });
            }
        }

        var removed = stored.Keys.Where(key => !seen.Contains(key))
                                 .OrderBy(k => k, StringComparer.Ordinal)
                                 .ToList();

        foreach (var key in removed)
            rows.Add(new RowChange { Table = table.Name, RowKey = key, Kind = ChangeKind.Removed });

        // An added row needs nothing read: there is no stored cell to compare with.
        foreach (var row in added)
        {
            foreach (var cell in table.CellsOf(row))
            {
                if (cell.Value is null)
                    continue;

                cells.Add(new CellChange
                {
                    Table = table.Name,
                    RowKey = row.Key,
                    Field = cell.Field,
                    Kind = ChangeKind.Added,
                    NewValue = cell.Value,
                    Location = LocationOf(cell.Location),
                });
            }
        }

        DiffCells(table, state, changed, cells);
        RemovedCells(table.Name, state, removed, cells);
    }

    private static void DiffCells(
        TableFingerprint table, IHistoryState state, List<RowFingerprint> changed, List<CellChange> cells)
    {
        foreach (var chunk in Chunks(changed, RowChunk))
        {
            var stored = state.ReadCells(table.Name, chunk.Select(r => r.Key).ToList());

            foreach (var row in chunk)
            {
                var present = new HashSet<string>(StringComparer.Ordinal);

                foreach (var cell in table.CellsOf(row))
                {
                    present.Add(cell.Field);

                    bool had = stored.TryGetValue(new CellAddress(row.Key, cell.Field), out string? before);

                    if (had && string.Equals(before, cell.Value, StringComparison.Ordinal))
                        continue;

                    // A column that did not exist is an addition even where the cell is
                    // blank now, because the row's shape changed. A column that did
                    // exist and is now blank is a modification to nothing, which is a
                    // real edit and has to be recorded as one.
                    if (!had && cell.Value is null)
                        continue;

                    cells.Add(new CellChange
                    {
                        Table = table.Name,
                        RowKey = row.Key,
                        Field = cell.Field,
                        Kind = had ? ChangeKind.Modified : ChangeKind.Added,
                        OldValue = had ? before : null,
                        NewValue = cell.Value,
                        Location = LocationOf(cell.Location),
                    });
                }

                // Columns the row used to have and no longer does - a dropped column,
                // seen from the row's side.
                foreach (var address in stored.Keys.Where(a =>
                             string.Equals(a.RowKey, row.Key, StringComparison.Ordinal)
                             && !present.Contains(a.Field))
                         .OrderBy(a => a.Field, StringComparer.Ordinal))
                {
                    cells.Add(new CellChange
                    {
                        Table = table.Name,
                        RowKey = row.Key,
                        Field = address.Field,
                        Kind = ChangeKind.Removed,
                        OldValue = stored[address],
                    });
                }
            }
        }
    }

    private static void RemovedCells(
        string table, IHistoryState state, IReadOnlyList<string> removed, List<CellChange> cells)
    {
        foreach (var chunk in Chunks(removed, RowChunk))
        {
            var stored = state.ReadCells(table, chunk);

            foreach (var address in stored.Keys.OrderBy(a => a.RowKey, StringComparer.Ordinal)
                                               .ThenBy(a => a.Field, StringComparer.Ordinal))
            {
                // What the row held is recorded on the way out. Without it a range
                // query can say a row was deleted but not what was lost, which is the
                // question actually asked when one goes missing.
                if (stored[address] is null)
                    continue;

                cells.Add(new CellChange
                {
                    Table = table,
                    RowKey = address.RowKey,
                    Field = address.Field,
                    Kind = ChangeKind.Removed,
                    OldValue = stored[address],
                });
            }
        }
    }

    // --------------------------------------------------------------- schema

    private static void DiffFields(
        TableFingerprint table, IReadOnlyDictionary<string, StoredField> stored, List<SchemaChange> schema)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in table.Fields)
        {
            seen.Add(field.Name);

            string descriptor = DescriptorOf(field);

            if (!stored.TryGetValue(field.Name, out var before))
            {
                schema.Add(new SchemaChange
                {
                    EntityKind = EntityKind.Field,
                    EntityName = table.Name,
                    MemberName = field.Name,
                    Kind = ChangeKind.Added,
                    After = descriptor,
                    Location = LocationOf(field.Location),
                });

                continue;
            }

            if (string.Equals(before.Hash, field.Hash, StringComparison.Ordinal))
                continue;

            schema.Add(new SchemaChange
            {
                EntityKind = EntityKind.Field,
                EntityName = table.Name,
                MemberName = field.Name,
                Kind = ChangeKind.Modified,
                Before = before.Descriptor,
                After = descriptor,
                Location = LocationOf(field.Location),
            });
        }

        foreach (var gone in stored.Keys.Where(name => !seen.Contains(name))
                                        .OrderBy(n => n, StringComparer.Ordinal))
        {
            schema.Add(new SchemaChange
            {
                EntityKind = EntityKind.Field,
                EntityName = table.Name,
                MemberName = gone,
                Kind = ChangeKind.Removed,
                Before = stored[gone].Descriptor,
            });
        }
    }

    private static void DiffEntities(
        ModelFingerprint fingerprint, IHistoryState state, List<SchemaChange> schema)
    {
        var stored = state.ReadEntities();
        var seen = new HashSet<EntityAddress>();

        foreach (var (entity, kind, memberKind) in Entities(fingerprint))
        {
            var address = new EntityAddress(kind, entity.Name);
            seen.Add(address);

            var members = entity.Members.ToDictionary(m => m.Name, m => m.Value ?? "", StringComparer.Ordinal);

            if (!stored.TryGetValue(address, out var before))
            {
                schema.Add(new SchemaChange
                {
                    EntityKind = kind,
                    EntityName = entity.Name,
                    Kind = ChangeKind.Added,
                    Location = LocationOf(entity.Location),
                });

                foreach (var member in entity.Members)
                {
                    schema.Add(new SchemaChange
                    {
                        EntityKind = memberKind,
                        EntityName = entity.Name,
                        MemberName = member.Name,
                        Kind = ChangeKind.Added,
                        After = member.Value,
                        Location = LocationOf(member.Location),
                    });
                }

                continue;
            }

            if (string.Equals(before.Hash, entity.Hash, StringComparison.Ordinal))
                continue;

            DiffMembers(entity, memberKind, state.ReadMembers(address), members, schema);
        }

        foreach (var gone in stored.Keys.Where(a => !seen.Contains(a))
                                        .OrderBy(a => a.Kind).ThenBy(a => a.Name, StringComparer.Ordinal))
        {
            schema.Add(new SchemaChange
            {
                EntityKind = gone.Kind,
                EntityName = gone.Name,
                Kind = ChangeKind.Removed,
            });
        }
    }

    private static void DiffMembers(
        EntityFingerprint entity,
        EntityKind memberKind,
        IReadOnlyDictionary<string, string> stored,
        IReadOnlyDictionary<string, string> current,
        List<SchemaChange> schema)
    {
        foreach (var member in entity.Members)
        {
            if (!stored.TryGetValue(member.Name, out string? before))
            {
                schema.Add(new SchemaChange
                {
                    EntityKind = memberKind,
                    EntityName = entity.Name,
                    MemberName = member.Name,
                    Kind = ChangeKind.Added,
                    After = member.Value,
                    Location = LocationOf(member.Location),
                });

                continue;
            }

            if (string.Equals(before, member.Value ?? "", StringComparison.Ordinal))
                continue;

            schema.Add(new SchemaChange
            {
                EntityKind = memberKind,
                EntityName = entity.Name,
                MemberName = member.Name,
                Kind = ChangeKind.Modified,
                Before = before,
                After = member.Value,
                Location = LocationOf(member.Location),
            });
        }

        foreach (var gone in stored.Keys.Where(name => !current.ContainsKey(name))
                                        .OrderBy(n => n, StringComparer.Ordinal))
        {
            schema.Add(new SchemaChange
            {
                EntityKind = memberKind,
                EntityName = entity.Name,
                MemberName = gone,
                Kind = ChangeKind.Removed,
                Before = stored[gone],
            });
        }
    }

    private static IEnumerable<(EntityFingerprint Entity, EntityKind Kind, EntityKind MemberKind)> Entities(
        ModelFingerprint fingerprint)
    {
        foreach (var entity in fingerprint.Enums)
            yield return (entity, EntityKind.Enum, EntityKind.EnumLabel);

        foreach (var entity in fingerprint.ConstantSets)
            yield return (entity, EntityKind.Constants, EntityKind.Constant);
    }

    // -------------------------------------------------------------- helpers

    /// <summary>
    /// A column's attributes as JSON, which is what a schema change reports on either
    /// side of a modification.
    ///
    /// A descriptor rather than one row per changed attribute: a column usually changes
    /// in one way at a time, and when it does not, "type and side both changed" reads
    /// better as one entry than as two.
    /// </summary>
    public static string DescriptorOf(FieldFingerprint field)
    {
        var descriptor = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "type", field.TypeName },
            { "side", SideOf(field.TargetSide) },
        };

        if (field.IsRef)
        {
            descriptor["refTable"] = field.RefTableName ?? "";

            if (!string.IsNullOrEmpty(field.RefFieldName))
                descriptor["refField"] = field.RefFieldName;
        }

        if (!string.IsNullOrEmpty(field.Comment))
            descriptor["comment"] = field.Comment;

        return JsonConvert.SerializeObject(descriptor, Formatting.None);
    }

    private static string SideOf(TargetSide side)
    {
        return side switch
        {
            TargetSide.ClientOnly => "c",
            TargetSide.ServerOnly => "s",
            _ => "cs",
        };
    }

    private static SummaryLocation? LocationOf(Location? location)
    {
        if (location is null)
            return null;

        return new SummaryLocation
        {
            File = location.Filename?.Replace('\\', '/') ?? "",
            Sheet = location.Sheet,
            Cell = location.CellRange,
            Url = string.IsNullOrEmpty(location.SheetUrl) ? null : location.SheetUrl,
        };
    }

    private static IEnumerable<IReadOnlyList<T>> Chunks<T>(IReadOnlyList<T> items, int size)
    {
        for (int start = 0; start < items.Count; start += size)
            yield return items.Skip(start).Take(size).ToList();
    }
}
