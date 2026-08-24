using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.History;

/// <summary>
/// A content hash of the whole model, at three levels: the model, each table, each row.
///
/// Three levels because the store is a remote database and a round trip is what costs.
/// A conversion that changed nothing compares one hash per table and stops; one that
/// touched a single row reads that table's row hashes and then that row's cells. The
/// alternative - sending every cell and letting the server work it out - is the same
/// answer at the cost of the whole dataset crossing the network on every build.
///
/// Cells are produced on demand rather than held. A project's sheets hold millions of
/// them and only the rows whose hash moved are ever read, so materialising them all
/// would spend the memory of the entire dataset to look at a handful of rows.
///
/// What is hashed is content, not where it came from. Moving a table to another sheet
/// does not change its hash, because nothing about the data changed. The location
/// travels alongside as metadata - it is what lets the report link to the cell.
/// </summary>
public sealed class ModelFingerprint
{
    private ModelFingerprint(
        string hash,
        IReadOnlyList<TableFingerprint> tables,
        IReadOnlyList<EntityFingerprint> enums,
        IReadOnlyList<EntityFingerprint> constantSets)
    {
        Hash = hash;
        Tables = tables;
        Enums = enums;
        ConstantSets = constantSets;
    }

    /// <summary>Hash of everything below. Equal hashes mean an identical model.</summary>
    public string Hash { get; }

    public IReadOnlyList<TableFingerprint> Tables { get; }

    public IReadOnlyList<EntityFingerprint> Enums { get; }

    public IReadOnlyList<EntityFingerprint> ConstantSets { get; }

    /// <summary>
    /// Fingerprints a model.
    ///
    /// Pass the model the sheets declared, never one narrowed by target side: a
    /// snapshot taken from a client build would record every server-only table as
    /// deleted, and the next server build would record them all as added again.
    /// </summary>
    public static ModelFingerprint Of(Model model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var tables = model.Tables.Select(TableFingerprint.Of).ToList();
        var enums = model.Enums.Select(EnumFingerprint).ToList();
        var constantSets = model.ConstantSets.Select(ConstantSetFingerprint).ToList();

        using var hash = new Fingerprint();

        // Sorted by name, so a model whose entities were declared in a different order
        // - a workbook renamed, a sheet moved - is recognised as the same model.
        foreach (var table in tables.OrderBy(t => t.Name, StringComparer.Ordinal))
            hash.Add(table.Name).AddDigest(table.Hash);

        foreach (var entity in enums.OrderBy(e => e.Name, StringComparer.Ordinal))
            hash.Add(entity.Name).AddDigest(entity.Hash);

        foreach (var entity in constantSets.OrderBy(e => e.Name, StringComparer.Ordinal))
            hash.Add(entity.Name).AddDigest(entity.Hash);

        return new ModelFingerprint(hash.Complete(), tables, enums, constantSets);
    }

    private static EntityFingerprint EnumFingerprint(Models.Enum enumm)
    {
        using var hash = new Fingerprint();

        hash.Add(enumm.Name);

        var members = new List<MemberFingerprint>();

        foreach (var label in enumm.Labels)
        {
            string value = label.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            hash.Add(label.Name).Add(value);

            members.Add(new MemberFingerprint(label.Name, "enum", value, label.Comment, label.Location));
        }

        return new EntityFingerprint(enumm.Name, "enum", hash.Complete(), members, enumm.Location);
    }

    private static EntityFingerprint ConstantSetFingerprint(ConstantSet constantSet)
    {
        using var hash = new Fingerprint();

        hash.Add(constantSet.Name);

        var members = new List<MemberFingerprint>();

        foreach (var constant in constantSet.Constants)
        {
            string? value = ConstantValueOf(constant);

            hash.Add(constant.Name).Add(constant.TypeName).Add(value);

            members.Add(new MemberFingerprint(
                constant.Name, constant.TypeName, value, constant.Comment, constant.Location));
        }

        return new EntityFingerprint(
            constantSet.Name, "constants", hash.Complete(), members, constantSet.Location);
    }

    /// <summary>
    /// A constant's value as text.
    ///
    /// An enum-typed constant is the one case that cannot go straight through
    /// <see cref="CanonicalValue"/>: a table cell of enum type holds the resolved
    /// integer, but a constant holds whatever the sheet wrote - the label's name or its
    /// number. The label name is recorded, because that is what the sheet says and what
    /// every generated language emits. A change to the number behind it is a change to
    /// the enum, and shows up there.
    /// </summary>
    private static string? ConstantValueOf(ConstantSet.Constant constant)
    {
        if (constant.Type != ValueType.Enum)
            return CanonicalValue.OfScalar(constant.Value!, constant.Type);

        if (constant.Value is null)
            return null;

        return constant.Enum.GetLabel(constant.Value, constant.Location).Name;
    }
}

/// <summary>One table's hash, its schema, and a hash per row.</summary>
public sealed class TableFingerprint
{
    private readonly Table _table;

    private TableFingerprint(
        Table table,
        string hash,
        string schemaHash,
        IReadOnlyList<FieldFingerprint> fields,
        IReadOnlyList<RowFingerprint> rows)
    {
        _table = table;

        Hash = hash;
        SchemaHash = schemaHash;
        Fields = fields;
        Rows = rows;
    }

    public string Name => _table.Name;

    /// <summary>Where the table was declared. Metadata, not part of the hash.</summary>
    public Location Location => _table.Location;

    /// <summary>Schema and rows together. Unchanged means nothing in the table moved.</summary>
    public string Hash { get; }

    /// <summary>Fields only, so a schema change can be told from a data change.</summary>
    public string SchemaHash { get; }

    public IReadOnlyList<FieldFingerprint> Fields { get; }

    public IReadOnlyList<RowFingerprint> Rows { get; }

    internal static TableFingerprint Of(Table table)
    {
        var fields = table.Fields.Select(FieldFingerprint.Of).ToList();

        string schemaHash = SchemaHashOf(table, fields);
        var rows = RowsOf(table);

        using var hash = new Fingerprint();

        hash.AddDigest(schemaHash);

        // In sheet order, not sorted. Reordering rows changes this hash and sends the
        // comparison down to the row level, where every row hash matches and nothing is
        // reported - one wasted descent, in exchange for not having to sort millions of
        // hashes on every build.
        foreach (var row in rows)
            hash.AddDigest(row.Hash);

        return new TableFingerprint(table, hash.Complete(), schemaHash, fields, rows);
    }

    /// <summary>
    /// The cells of one row, read from the model on demand.
    /// </summary>
    public IEnumerable<CellFingerprint> CellsOf(RowFingerprint row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var data = _table.Data[row.Index];

        foreach (var field in _table.Fields)
        {
            var cell = data[field.Index];

            yield return new CellFingerprint(
                field.Name,
                CanonicalValue.Of(cell?.Value, field),
                cell?.RawCell is null ? _table.Location : LocationOf(cell));
        }
    }

    private static Location LocationOf(Cell cell) => cell.RawCell.Location;

    private static string SchemaHashOf(Table table, IReadOnlyList<FieldFingerprint> fields)
    {
        using var hash = new Fingerprint();

        hash.Add(table.Name).Add(table.TargetSide.ToString());

        foreach (var field in fields)
            hash.AddDigest(field.Hash);

        return hash.Complete();
    }

    private static List<RowFingerprint> RowsOf(Table table)
    {
        var rows = new List<RowFingerprint>(table.Data.Count);

        if (table.Fields.Count == 0)
            return rows;

        // Field 0 is the primary index by construction, and the cooker has already
        // rejected duplicates in it - so a key addresses exactly one row, which is what
        // the store relies on to follow a row across snapshots.
        var indexField = table.PrimaryIndexField!;

        for (int index = 0; index < table.Data.Count; index++)
        {
            var data = table.Data[index];

            string key = CanonicalValue.Of(data[indexField.Index]?.Value, indexField) ?? "";

            using var hash = new Fingerprint();

            hash.Add(key);

            // Field names are hashed with their values. A renamed column therefore
            // rewrites every row, which is noisy but correct: the old field is gone and
            // the new one is new. Hashing values alone would leave the rename invisible
            // at this level and the stored cells would keep the old name for ever.
            foreach (var field in table.Fields)
                hash.Add(field.Name).Add(CanonicalValue.Of(data[field.Index]?.Value, field));

            rows.Add(new RowFingerprint(key, hash.Complete(), index));
        }

        return rows;
    }
}

/// <summary>One column, as the history sees it.</summary>
public sealed class FieldFingerprint
{
    private FieldFingerprint(Field field, string hash)
    {
        Name = field.Name;
        RawName = field.RawName;
        TypeName = field.TypeName;
        Type = field.Type;
        Comment = field.Comment;
        TargetSide = field.TargetSide;
        IsRef = field.IsRef;
        RefTableName = field.RefTableName;
        RefFieldName = field.RefFieldName;
        Location = field.NameLocation;
        Hash = hash;
    }

    public string Name { get; }
    public string? RawName { get; }
    public string TypeName { get; }
    public ValueType Type { get; }
    public string? Comment { get; }
    public TargetSide TargetSide { get; }
    public bool IsRef { get; }
    public string? RefTableName { get; }
    public string? RefFieldName { get; }
    public Location Location { get; }
    public string Hash { get; }

    internal static FieldFingerprint Of(Field field)
    {
        using var hash = new Fingerprint();

        // The comment is in here on purpose. It is emitted into every generated
        // language as documentation, so editing it does change the output - and a
        // designer who rewrote a column's meaning has made a change worth seeing.
        hash.Add(field.Name)
            .Add(field.TypeName)
            .Add(field.TargetSide.ToString())
            .Add(field.IsRef)
            .Add(field.RefTableName)
            .Add(field.RefFieldName)
            .Add(field.Indexing)
            .Add(field.Comment);

        return new FieldFingerprint(field, hash.Complete());
    }
}

/// <summary>One row's key and hash. Its cells are read through the table.</summary>
public sealed class RowFingerprint
{
    internal RowFingerprint(string key, string hash, int index)
    {
        Key = key;
        Hash = hash;
        Index = index;
    }

    /// <summary>Canonical text of the primary index, which is what follows a row across snapshots.</summary>
    public string Key { get; }

    public string Hash { get; }

    /// <summary>Position in the table's data, for reading the cells back.</summary>
    public int Index { get; }
}

/// <summary>One cell: its column, its value, and where to find it in the sheet.</summary>
public sealed class CellFingerprint
{
    internal CellFingerprint(string field, string? value, Location? location)
    {
        Field = field;
        Value = value;
        Location = location;
    }

    public string Field { get; }

    /// <summary>Canonical text, or null when the cell holds nothing.</summary>
    public string? Value { get; }

    /// <summary>
    /// The cell in the sheet. Not hashed - this is what the report links to, so a
    /// value that moved to another sheet is the same value with a new address.
    /// </summary>
    public Location? Location { get; }
}

/// <summary>An enum or a constant set: things with named members and no rows.</summary>
public sealed class EntityFingerprint
{
    internal EntityFingerprint(
        string name, string kind, string hash, IReadOnlyList<MemberFingerprint> members, Location location)
    {
        Name = name;
        Kind = kind;
        Hash = hash;
        Members = members;
        Location = location;
    }

    public string Name { get; }

    /// <summary>`enum` or `constants`.</summary>
    public string Kind { get; }

    public string Hash { get; }

    public IReadOnlyList<MemberFingerprint> Members { get; }

    public Location Location { get; }
}

/// <summary>One label of an enum, or one constant of a set.</summary>
public sealed class MemberFingerprint
{
    internal MemberFingerprint(
        string name, string typeName, string? value, string? comment, Location? location)
    {
        Name = name;
        TypeName = typeName;
        Value = value;
        Comment = comment;
        Location = location;
    }

    public string Name { get; }
    public string TypeName { get; }
    public string? Value { get; }
    public string? Comment { get; }
    public Location? Location { get; }
}
