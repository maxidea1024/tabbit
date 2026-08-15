using System.Collections.Generic;

namespace Tabbit.Validation;

/// <summary>
/// The tables and columns as something to walk, for a rule whose subject is not one table.
/// </summary>
/// <remarks>
/// The counterpart to the generated accessor: that one is typed and needs a name, this one
/// enumerates and does not. A convention over every table can only be written against this.
/// </remarks>
public interface ISchemaView
{
    /// <summary>Every table of the model, in the order it was read.</summary>
    IReadOnlyList<ITableSchema> Tables { get; }

    /// <summary>One table by name, or null when the model has none.</summary>
    ITableSchema? Table(string name);

    /// <summary>Whether the model has a table of this name.</summary>
    bool Has(string name);

    /// <summary>Every column of every table, flattened.</summary>
    IEnumerable<IFieldSchema> Fields { get; }
}

/// <summary>One table, as much of it as a rule about conventions needs.</summary>
public interface ITableSchema
{
    /// <summary>Name as the generated code spells it.</summary>
    string Name { get; }

    /// <summary>Name as the sheet spells it, which is also what the data file is called.</summary>
    string RawName { get; }

    /// <summary>The comment the sheet carries, or empty.</summary>
    string Comment { get; }

    /// <summary>How many rows were read.</summary>
    int RowCount { get; }

    /// <summary>Which side this table is built for: `c`, `s`, or both.</summary>
    /// <remarks>
    /// A string rather than the model's own enum, so a rule reads a value it can compare
    /// without the tool's types being in scope.
    /// </remarks>
    string TargetSide { get; }

    /// <summary>Every column, in sheet order.</summary>
    IReadOnlyList<IFieldSchema> Fields { get; }

    /// <summary>The primary index column, or null for a table with no columns.</summary>
    IFieldSchema? Index { get; }

    /// <summary>One column by name, or null.</summary>
    IFieldSchema? Field(string name);
}

/// <summary>One column.</summary>
public interface IFieldSchema
{
    /// <summary>The table it belongs to.</summary>
    ITableSchema Table { get; }

    /// <summary>Name as the generated code spells it.</summary>
    string Name { get; }

    /// <summary>The declared type, as the sheet spells it.</summary>
    string TypeName { get; }

    /// <summary>Whether a blank is refused.</summary>
    bool IsRequired { get; }

    /// <summary>Whether this is an index column.</summary>
    bool IsIndex { get; }

    /// <summary>Whether the column holds an array.</summary>
    bool IsArray { get; }

    /// <summary>Whether the column points at another table.</summary>
    bool IsReference { get; }

    /// <summary>The table it points at, or null when it points at nothing.</summary>
    ITableSchema? References { get; }

    /// <summary>Which side this column is built for: `c`, `s`, or both.</summary>
    string TargetSide { get; }

    /// <summary>The comment the sheet carries, or empty.</summary>
    string Comment { get; }
}
