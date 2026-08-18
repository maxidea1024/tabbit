using System.Collections.Generic;

namespace Tabbit.Models;

/// <summary>
/// One of a table's sets of rows, and the tail that distinguishes its file.
/// </summary>
/// <remarks>
/// A table usually has one set of rows and this is invisible. Some sheets give a table
/// several - the same columns filled in more than once, so that a build can be made with one
/// set or another - and that is still one table: one schema, one generated type, several
/// files of data.
///
/// <see cref="Name"/> is the tail the file name carries, exactly as written and separator
/// included, so a table `Item` and a set named `_alt` produce `Item_alt`. Keeping the
/// separator inside the string is what keeps this from being an opinion about notation: the
/// tail is whatever the source's pattern captured, and this program does not interpret it.
///
/// spec/table-row-sets.md.
/// </remarks>
public sealed class RowSet
{
    /// <summary>What the file name carries, or empty for the table's own set.</summary>
    public string Name { get; set; } = "";

    /// <summary>The rows, in the same shape as <see cref="Table.Data"/>.</summary>
    public List<List<Cell>> Rows { get; set; } = new List<List<Cell>>();
}
