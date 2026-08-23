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

    /// <summary>
    /// Every schema file read, in the order the recipe named their directories.
    /// </summary>
    /// <remarks>
    /// Text, not declarations. These are read before a workbook is opened - a sheet's type
    /// cell may name what they declare - and interpreted afterwards, once every file and
    /// every sheet is here. Empty for a project that declares its types in its sheets, which
    /// is every project that existed before these files did.
    /// </remarks>
    public List<RawSchemaFile> SchemaFiles { get; set; } = new List<RawSchemaFile>();
}
