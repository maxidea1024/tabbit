using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.Validation;

/// <summary>
/// The tables and columns as something to walk, rather than as types to name.
/// </summary>
/// <remarks>
/// The counterpart to `Tables`, and needed because the two answer opposite questions. A typed
/// accessor is the better half whenever a rule knows which table it means - the field is typed
/// and a misspelling is a compile error - but a property only exists for a name somebody wrote,
/// so "every table" cannot be asked of it at all.
///
/// That is what a convention rule needs: `*ItemId` must be a reference, every table must have a
/// comment, no two tables may share an id band. spec/validation-pipeline.md §4.
///
/// A wrapper rather than the model's own types. Rule files are the long-lived side of this - a
/// project will have a hundred of them - so what they read has to be a contract rather than
/// whatever the cooker happens to expose this month.
/// </remarks>
public sealed class SchemaView : ISchemaView
{
    private readonly Dictionary<string, TableSchema> _byName;

    internal SchemaView(Model model)
    {
        var tables = model.Tables.Select(table => new TableSchema(table)).ToList();

        Tables = tables;
        _byName = tables.ToDictionary(table => table.Name, StringComparer.Ordinal);

        // After every table exists, because a reference points at one of them.
        foreach (var table in tables)
            table.ResolveReferences(_byName);
    }

    /// <summary>Every table the sheets declared, in the order their markers were found.</summary>
    public IReadOnlyList<TableSchema> Tables { get; }

    /// <summary>One table by name, or null.</summary>
    public TableSchema? Table(string name)
        => name is not null && _byName.TryGetValue(name, out var found) ? found : null;

    /// <summary>Whether a table of this name exists.</summary>
    public bool Has(string name) => Table(name) is not null;

    /// <summary>Every column of every table, for a rule that is about columns.</summary>
    public IEnumerable<FieldSchema> Fields => Tables.SelectMany(table => table.Fields);
    // The contract answers with its own types; these forward to the concrete ones so the host
    // keeps working with what it built. Only the members whose type differs need a line here.
    IReadOnlyList<ITableSchema> ISchemaView.Tables => Tables;
    ITableSchema? ISchemaView.Table(string name) => Table(name);
    IEnumerable<IFieldSchema> ISchemaView.Fields => Fields;
}

/// <summary>One table, as a rule about tables sees it.</summary>
public sealed class TableSchema : ITableSchema
{
    private readonly Table _table;

    internal TableSchema(Table table)
    {
        _table = table;

        Fields = table.Fields.Select(field => new FieldSchema(this, field)).ToList();
    }

    /// <summary>Name as generated code uses it.</summary>
    public string Name => _table.Name;

    /// <summary>Name exactly as the sheet wrote it, which may differ in case or spacing.</summary>
    public string RawName => _table.RawName;

    /// <summary>Description from the sheet. Empty when nobody wrote one.</summary>
    public string Comment => _table.Comment ?? "";

    /// <summary>How many rows the sheet gave it.</summary>
    public int RowCount => _table.Data.Count;

    /// <summary>Which build this table is included in: `c`, `s`, or both.</summary>
    public string TargetSide => _table.TargetSide.ToString();

    /// <summary>Columns, excluding any the sheet commented out.</summary>
    public IReadOnlyList<FieldSchema> Fields { get; }

    /// <summary>The primary index column, or null for a table with no columns at all.</summary>
    public FieldSchema? Index => Fields.Count > 0 ? Fields[0] : null;

    /// <summary>One column by name, or null.</summary>
    public FieldSchema? Field(string name)
        => Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal));

    /// <summary>The cell the table's marker is in, for a report about the table itself.</summary>
    internal Location? Location => _table.Location;

    internal void ResolveReferences(Dictionary<string, TableSchema> byName)
    {
        foreach (var field in Fields)
            field.ResolveReference(byName);
    }

    public override string ToString() => Name;
    IReadOnlyList<IFieldSchema> ITableSchema.Fields => Fields;
    IFieldSchema? ITableSchema.Index => Index;
    IFieldSchema? ITableSchema.Field(string name) => Field(name);
}

/// <summary>One column, as a rule about columns sees it.</summary>
public sealed class FieldSchema : IFieldSchema
{
    private readonly Field _field;

    internal FieldSchema(TableSchema owner, Field field)
    {
        Table = owner;
        _field = field;
    }

    /// <summary>The table this column belongs to.</summary>
    public TableSchema Table { get; }

    /// <summary>Name as generated code uses it.</summary>
    public string Name => _field.Name;

    /// <summary>Type as the sheet wrote it, including a trailing `?` when optional.</summary>
    public string TypeName => _field.TypeName;

    /// <summary>Whether every row must have a value.</summary>
    public bool IsRequired => _field.IsRequired;

    /// <summary>Whether rows can be looked up by this column.</summary>
    public bool IsIndex => _field.Indexing;

    /// <summary>Whether the column holds an array rather than one value.</summary>
    public bool IsArray => _field.IsArray;

    /// <summary>Whether the column points at a row of another table.</summary>
    public bool IsReference => _field.IsRef;

    /// <summary>
    /// The table this column points at, or null when it points at nothing.
    /// </summary>
    /// <remarks>
    /// This is what a naming convention checks - that a column called `~ItemId` really does
    /// reach `Item` - so it answers with the table rather than with its name.
    /// </remarks>
    public TableSchema? References { get; private set; }

    /// <summary>Which build this column is included in: `c`, `s`, or both.</summary>
    public string TargetSide => _field.TargetSide.ToString();

    /// <summary>Description from the sheet. Empty when nobody wrote one.</summary>
    public string Comment => _field.Comment ?? "";

    /// <summary>The header cell this column was declared in.</summary>
    internal Location Location => _field.NameLocation;

    internal void ResolveReference(Dictionary<string, TableSchema> byName)
    {
        if (_field.IsRef
            && _field.RefTableName is not null
            && byName.TryGetValue(_field.RefTableName, out var target))
        {
            References = target;
        }
    }

    public override string ToString() => $"{Table.Name}.{Name}";
    ITableSchema IFieldSchema.Table => Table;
    ITableSchema? IFieldSchema.References => References;
}
