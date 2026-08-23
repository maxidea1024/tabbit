namespace Tabbit.Recipe;

/// <summary>
/// A directory of schema files - the type declarations a sheet's type cell names.
/// </summary>
/// <remarks>
/// A section of its own rather than another kind of source, because these are not sheets and
/// nothing about a source entry fits them: there is no layout to read them under, no target
/// side, no array delimiter and no index column. What they share with a source is only that a
/// run reads them, and the build cache has to know it did.
///
/// **The path is part of the cache's input.** A build whose declarations changed and whose
/// workbooks did not is a different build, and a cache that only watched the workbooks would
/// hand back the previous run's output for it.
///
/// notes/struct-dsl-design.md section 7.4.
/// </remarks>
public class SchemaSourceRecipe
{
    /// <summary>
    /// Directory to search, including subdirectories.
    ///
    /// Any file or directory whose name begins with `#` is skipped, which is how work in
    /// progress is kept out of a build - the same rule the workbook directories follow.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Semicolon-separated extensions to pick up. Everything else in the directory is
    /// ignored.
    /// </summary>
    /// <remarks>
    /// `.tbs`, and not `.tab`: that one is already tab-separated values, and a file whose
    /// extension means two things is a file no editor can be configured for.
    /// </remarks>
    public string FileExtensionPatterns { get; set; } = ".tbs";
}
