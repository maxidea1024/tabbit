using System.Collections.Generic;
using Newtonsoft.Json;

namespace Tabbit.Models;

/// <summary>
/// Unused. A table's rows are plain `List&lt;Cell&gt;` instead, because nothing needed
/// a row to carry anything beyond its cells and the cells already know where they are.
/// </summary>
public class Row
{
    /// <summary>Where the row starts.</summary>
    [JsonIgnore]
    public required Location Location { get; set; }

    /// <summary>Cells of the row.</summary>
    public required List<Cell> Cells { get; set; }
}
