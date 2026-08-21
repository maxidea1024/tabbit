using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Serilog;
using Tabbit.Models.Raw;
using Tabbit.Recipe;

namespace Tabbit.Sources;

/// <summary>One registered source and the metadata its attribute declared.</summary>
public sealed class SourceDescriptor
{
    internal SourceDescriptor(string id, string section, int order, ISource source,
                              Func<RecipeModel, IEnumerable> sectionEntries)
    {
        Id = id;
        Section = section;
        Order = order;
        Source = source;
        SectionEntries = sectionEntries;
    }

    /// <summary>Stable short name, such as `xlsx` or `googlesheets`.</summary>
    public string Id { get; }

    /// <summary>Dotted recipe path this source reads.</summary>
    public string Section { get; }

    /// <summary>Sort key.</summary>
    public int Order { get; }

    /// <summary>The source itself.</summary>
    public ISource Source { get; }

    internal Func<RecipeModel, IEnumerable> SectionEntries { get; }

    public override string ToString() => $"{Id} ({Section})";
}

/// <summary>
/// Every input source in this assembly, found by attribute.
///
/// Program.Process used to open with a note asking for a factory, above two hand-written
/// `if (recipe.Sources.X.Count > 0)` blocks. This is
/// that, and the same shape as <see cref="Targets.TargetRegistry"/> so there is one idea
/// to learn rather than two.
/// </summary>
public static class SourceRegistry
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Importing;

    private static readonly Lazy<IReadOnlyList<SourceDescriptor>> LazyAll =
        new Lazy<IReadOnlyList<SourceDescriptor>>(Discover);

    /// <summary>All registered sources, ordered by <see cref="SourceDescriptor.Order"/> then id.</summary>
    public static IReadOnlyList<SourceDescriptor> All => LazyAll.Value;

    /// <summary>Ids of every registered source, for help text and error messages.</summary>
    public static string KnownIds => string.Join(", ", All.Select(d => d.Id));

    /// <summary>
    /// Imports every source entry the recipe lists, into one raw model.
    /// </summary>
    public static void ImportAll(Options options, RecipeModel recipe, RawModel model)
    {
        foreach (var descriptor in All)
        {
            int index = 0;

            foreach (var entry in descriptor.SectionEntries(recipe))
            {
                string section = $"{descriptor.Section}[{index++}]";

                // A null in the list means the recipe had a stray comma or a bare
                // `null`; skipping it beats a NullReferenceException from the source.
                if (entry is null)
                    continue;

                descriptor.Source.Import(new SourceContext(options, recipe, model, entry, section));
            }
        }
    }

    private static IReadOnlyList<SourceDescriptor> Discover()
    {
        var descriptors = new List<SourceDescriptor>();

        foreach (var type in typeof(SourceRegistry).Assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<TabbitSourceAttribute>();
            if (attribute is null)
                continue;

            if (type.IsAbstract || !typeof(ISource).IsAssignableFrom(type))
            {
                throw new TabbitException(
                    $"`{type.Name}` is marked [TabbitSource] but is not a concrete {nameof(ISource)}.");
            }

            var source = (ISource)Activator.CreateInstance(type)!;

            descriptors.Add(new SourceDescriptor(
                attribute.Id,
                attribute.Section,
                attribute.Order,
                source,
                RecipeSectionReader.Build(attribute.Section, source.EntryType, type)));
        }

        var duplicate = descriptors.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                                   .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new TabbitException($"Two sources both claim the id `{duplicate.Key}`.");

        descriptors.Sort((left, right) =>
        {
            int byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(left.Id, right.Id);
        });

        Log.Debug($"Registered {descriptors.Count} source(s): {string.Join(", ", descriptors.Select(d => d.Id))}");

        return descriptors;
    }
}
