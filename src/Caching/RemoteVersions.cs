using System.Collections.Generic;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Tabbit.Sources;

namespace Tabbit.Caching;

/// <summary>
/// Asks every source whether its inputs can say what version they are at.
/// </summary>
/// <remarks>
/// Through the registry rather than by naming the sources that read documents, so nothing
/// here knows which services exist. A source that reads files does not implement
/// <see cref="ISourceVersions"/> and its inputs are compared as files instead.
/// </remarks>
internal static class RemoteVersions
{
    public static List<SourceVersion> Read(Options options, RecipeModel recipe)
    {
        var versions = new List<SourceVersion>();

        foreach (var (descriptor, entry, section) in SourceRegistry.Entries(recipe))
        {
            if (descriptor.Source is not ISourceVersions asking)
                continue;

            // A scratch model, because this is asked before the run has one and because
            // answering must not import anything. A source that added to it would be
            // adding to a model nothing reads.
            var context = new SourceContext(options, recipe, new RawModel(), entry, section);

            versions.AddRange(asking.Versions(context));
        }

        return versions;
    }
}
