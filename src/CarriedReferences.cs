using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Tabbit;

/// <summary>
/// The assemblies this tool carries to compile other people's code against.
/// </summary>
/// <remarks>
/// Two sets, because there are two questions and they have different answers.
///
/// A validation rule runs inside this process, so it is compiled against this process's own
/// framework. Generated code is compiled for somebody else's project - a game on Unity, a server
/// on .NET - so it is compiled against netstandard2.1, which is the surface both accept.
///
/// Carried rather than read off disk, because a self-contained single-file publish has no path to
/// read an assembly from: they live inside the executable. That was the whole reason validation
/// refused to run in that shape.
/// spec/validation-usability-and-assembly-output.md sections 4 and 7.
/// </remarks>
internal static class CarriedReferences
{
    /// <summary>What a validation rule is compiled against: this framework, plus the contract.</summary>
    internal const string ForRules = "Tabbit.RuleReferences.zip";

    /// <summary>What generated code is compiled against when a recipe asks for an assembly.</summary>
    internal const string ForGeneratedCode = "Tabbit.NetStandardReferences.zip";

    private static readonly Dictionary<string, IReadOnlyList<MetadataReference>> Cache =
        new Dictionary<string, IReadOnlyList<MetadataReference>>(StringComparer.Ordinal);

    /// <summary>
    /// One set, as references a compilation can take.
    /// </summary>
    /// <remarks>
    /// Read once and kept: a set is over a hundred assemblies, and a run compiles many times.
    /// </remarks>
    internal static IReadOnlyList<MetadataReference> Of(string archive)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(archive, out var cached))
                return cached;

            var references = new List<MetadataReference>();

            foreach (var (name, bytes) in Entries(archive))
            {
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                references.Add(MetadataReference.CreateFromStream(new MemoryStream(bytes)));
            }

            if (references.Count == 0)
            {
                throw new TabbitException(
                    $"This build of Tabbit carries no `{archive}`, so it cannot compile against "
                    + $"anything. The build writes it - see `EmbedRuleCompilationReferences` in "
                    + $"src/Tabbit.csproj.");
            }

            Cache[archive] = references;

            return references;
        }
    }

    /// <summary>One carried file by name, or null when the set holds none of that name.</summary>
    /// <remarks>
    /// For the contract, which the editor's project needs as a file beside the rules rather than
    /// as metadata in memory.
    /// </remarks>
    internal static byte[]? File(string archive, string name)
        => Entries(archive)
            .Where(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Bytes)
            .FirstOrDefault();

    private static IEnumerable<(string Name, byte[] Bytes)> Entries(string archive)
    {
        using var stream = typeof(CarriedReferences).Assembly.GetManifestResourceStream(archive);

        if (stream is null)
            yield break;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            using var content = entry.Open();
            using var bytes = new MemoryStream((int)entry.Length);

            content.CopyTo(bytes);

            yield return (entry.Name, bytes.ToArray());
        }
    }
}
