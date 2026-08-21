using System.Collections.Generic;

namespace Tabbit.Sources;

/// <summary>
/// One input that cannot be measured as a file, and what the service holding it calls its
/// version.
/// </summary>
/// <param name="Source">Registered id of the source that reads it.</param>
/// <param name="Id">The document's identifier, as the recipe names it.</param>
/// <param name="Version">
/// The version, or null when it could not be read.
/// </param>
public readonly record struct SourceVersion(string Source, string Id, string? Version);

/// <summary>
/// Implemented by a source whose inputs can say whether they changed without being read.
/// </summary>
/// <remarks>
/// A source that reads files needs none of this: a file has a size, a modification time and
/// contents that can be hashed, and the build cache compares those directly. A source that
/// reads a hosted document has none of the three, so the only way to answer "did this change"
/// without fetching the whole thing is to ask the service.
///
/// Optional on purpose. A source that cannot answer simply does not implement it, and its
/// documents are then fetched on every run - which is correct, only slower.
///
/// Returning a null version is the same as not implementing this for that one document: it
/// says the question could not be answered this time. Whoever could not answer it is
/// responsible for saying why, and for saying what would make it answerable - the point of
/// this interface is a fast run, and a person who does not know what to grant cannot grant
/// it.
/// </remarks>
public interface ISourceVersions
{
    /// <summary>
    /// The versions of the inputs one recipe entry reads.
    /// </summary>
    /// <remarks>
    /// Called before anything is imported, and given a context whose model is a scratch one:
    /// this must not import, and must not add to any model.
    /// </remarks>
    IEnumerable<SourceVersion> Versions(SourceContext context);
}
