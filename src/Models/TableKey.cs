using System.Collections.Generic;
using Newtonsoft.Json;

namespace Tabbit.Models;

/// <summary>
/// A key rows are addressed by: one column, or several taken together.
/// </summary>
/// <remarks>
/// **SQL's key system, which is the one everybody already knows.** The first key a table
/// declares is its PRIMARY KEY and the rest are UNIQUE keys; either kind may be one column or
/// several, and a column may take part in more than one key. spec/primary-layout.md section 3.5.
///
/// A table that declares none is not keyless - its first column is the primary key, which is
/// what every sheet written before this notation existed relies on. So an empty list means
/// "the default", not "no keys", and the places that need the primary key ask
/// <see cref="Table.PrimaryIndexField"/> rather than reading this.
///
/// Names rather than <see cref="Field"/> references, because a key is written before the
/// columns are read and because the name is what the sheet said. The lookup is
/// <see cref="Table.GetField"/>.
/// </remarks>
public sealed class TableKey
{
    /// <summary>The columns that make up the key, in the order they were written.</summary>
    /// <remarks>
    /// Order is part of the key: a lookup generated for it takes its arguments this way round,
    /// and `stage,slot` is a different key from `slot,stage` to anyone reading the sheet.
    /// </remarks>
    public required List<string> FieldNames { get; set; }

    /// <summary>Whether this is the key the rows are identified by.</summary>
    /// <remarks>
    /// The first one declared. What separates it from the others is not uniqueness - they are
    /// all unique - but that references point at it, the history files rows under it, and a
    /// multi-row record begins where its cell has a value.
    /// </remarks>
    public bool IsPrimary { get; set; }

    /// <summary>Whether the key is made of more than one column.</summary>
    [JsonIgnore]
    public bool IsComposite => FieldNames.Count > 1;

    /// <summary>How the key reads in a report.</summary>
    public override string ToString() => string.Join(", ", FieldNames);
}
