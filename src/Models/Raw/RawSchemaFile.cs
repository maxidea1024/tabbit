namespace Tabbit.Models.Raw;

/// <summary>
/// One schema file, as text.
/// </summary>
/// <remarks>
/// Unparsed on purpose. What a declaration means needs every file in hand and the sheets
/// beside them, so the reading stops at the bytes and the meaning is settled where the whole
/// model is - the same division the sheets follow, where a cell arrives here as whatever was
/// typed in it.
/// </remarks>
public class RawSchemaFile
{
    /// <summary>
    /// The file's name relative to the directory the recipe named, separators normalized.
    /// </summary>
    /// <remarks>
    /// What every report about this file says, so it must not carry the absolute path this
    /// run happened to be given - two machines would then write different reports about the
    /// same mistake.
    /// </remarks>
    public required string Name { get; set; }

    /// <summary>The file's whole text.</summary>
    public required string Text { get; set; }
}
