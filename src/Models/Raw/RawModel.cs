using System.Collections.Generic;

namespace Tabbit.Models.Raw;

/// <summary>
/// Everything the importers read, before any of it is interpreted.
///
/// Sources are flattened into one list on purpose: several workbooks and Google
/// Sheets documents combine into a single model, so which file an entity came from
/// matters only for diagnostics.
/// </summary>
public class RawModel
{
    /// <summary>Every sheet read, in the order the importers produced them.</summary>
    public List<RawSheet> Sheets { get; set; } = new List<RawSheet>();
}
