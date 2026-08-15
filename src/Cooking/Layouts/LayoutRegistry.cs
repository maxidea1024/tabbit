using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Serilog;

namespace Tabbit.Cooking.Layouts;

/// <summary>One registered layout parser and what its attribute declared.</summary>
public sealed class LayoutDescriptor
{
    internal LayoutDescriptor(string id, string summary, bool usesNamedRanges, Type type)
    {
        Id = id;
        Summary = summary;
        UsesNamedRanges = usesNamedRanges;
        Type = type;
    }

    /// <summary>Stable short name, which is what a recipe entry's `Layout` holds.</summary>
    public string Id { get; }

    /// <summary>One line on what the layout is.</summary>
    public string Summary { get; }

    /// <summary>Whether this layout takes a workbook's defined names as table boundaries.</summary>
    public bool UsesNamedRanges { get; }

    private Type Type { get; }

    /// <summary>
    /// Builds a parser for one run.
    /// </summary>
    /// <remarks>
    /// A new instance each time rather than a shared one: a parser keeps what it scanned
    /// between its two passes, and two runs in one process - which the test suite does
    /// constantly - would otherwise see each other's sheets.
    /// </remarks>
    public ILayoutParser CreateParser() => (ILayoutParser)Activator.CreateInstance(Type)!;

    public override string ToString() => Id;
}

/// <summary>
/// Every layout parser in this assembly, found by attribute.
/// </summary>
public static class LayoutRegistry
{
    private static readonly Lazy<IReadOnlyList<LayoutDescriptor>> LazyAll =
        new Lazy<IReadOnlyList<LayoutDescriptor>>(Discover);

    /// <summary>All registered layouts, ordered by id.</summary>
    public static IReadOnlyList<LayoutDescriptor> All => LazyAll.Value;

    /// <summary>Ids of every registered layout, for help text and error messages.</summary>
    public static string KnownIds => string.Join(", ", All.Select(d => d.Id));

    /// <summary>
    /// Finds the layout a sheet asks for, or throws naming the ones that exist.
    /// </summary>
    public static LayoutDescriptor Get(string id)
    {
        var found = All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
            return found;

        throw new TabbitException(
            $"A recipe source asks for the sheet layout `{id}`, which does not exist. " +
            $"Use one of: {KnownIds}.");
    }

    /// <summary>
    /// Whether the named layout wants a workbook's defined names collected for it.
    /// </summary>
    /// <remarks>
    /// Asked by the importers before they resolve any, and tolerant of an unknown id: the
    /// recipe's own validation reports that, and answering false here means an importer
    /// does no work rather than throwing a second, worse-placed message about it.
    /// </remarks>
    public static bool UsesNamedRanges(string id)
        => All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
              ?.UsesNamedRanges ?? false;

    private static IReadOnlyList<LayoutDescriptor> Discover()
    {
        var descriptors = new List<LayoutDescriptor>();

        foreach (var type in typeof(LayoutRegistry).Assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<TabbitLayoutAttribute>();
            if (attribute is null)
                continue;

            if (type.IsAbstract || !typeof(ILayoutParser).IsAssignableFrom(type))
            {
                throw new TabbitException(
                    $"`{type.Name}` is marked [TabbitLayout] but is not a concrete {nameof(ILayoutParser)}.");
            }

            descriptors.Add(new LayoutDescriptor(
                attribute.Id, attribute.Summary, attribute.UsesNamedRanges, type));
        }

        var duplicate = descriptors.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                                   .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new TabbitException($"Two layouts both claim the id `{duplicate.Key}`.");

        descriptors.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

        Log.Debug($"Registered {descriptors.Count} layout(s): {string.Join(", ", descriptors.Select(d => d.Id))}");

        return descriptors;
    }
}
