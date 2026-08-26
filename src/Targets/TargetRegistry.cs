using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Tabbit.Caching;
using Tabbit.Helpers;
using Tabbit.History;
using Tabbit.Models;
using Tabbit.Recipe;

namespace Tabbit.Targets;

/// <summary>One registered target and the metadata its attribute declared.</summary>
public sealed class TargetDescriptor
{
    internal TargetDescriptor(string id, TargetKind kind, int order, bool deterministic, ITarget target)
    {
        Id = id;
        Kind = kind;
        Order = order;
        Deterministic = deterministic;
        Target = target;
    }

    /// <summary>Stable short name, such as `binary` or `csharp`.</summary>
    public string Id { get; }

    /// <summary>What the target produces.</summary>
    public TargetKind Kind { get; }

    /// <summary>Sort key within a kind.</summary>
    public int Order { get; }

    /// <summary>
    /// Whether the same model produces the same bytes twice.
    /// See <see cref="TabbitTargetAttribute.Deterministic"/>.
    /// </summary>
    public bool Deterministic { get; }

    /// <summary>The target itself.</summary>
    public ITarget Target { get; }

    /// <summary>The entry type this target's settings deserialize into.</summary>
    public Type EntryType => Target.EntryType;

    public override string ToString() => Id;
}

/// <summary>
/// One recipe entry paired with the target that will run it.
/// </summary>
public readonly struct PlannedTarget
{
    internal PlannedTarget(TargetDescriptor descriptor, IOutputRecipe entry, string section, TargetSide side)
    {
        Descriptor = descriptor;
        Entry = entry;
        Section = section;
        Side = side;
    }

    public TargetDescriptor Descriptor { get; }

    public IOutputRecipe Entry { get; }

    /// <summary>
    /// Where in the recipe this entry came from, including its index - `Targets[0]`.
    /// Quoted in diagnostics, so a message points at the one entry that caused it rather
    /// than at the list holding all of them.
    /// </summary>
    public string? Section { get; }

    /// <summary>
    /// The side this entry will actually be built for: what it declares, narrowed by
    /// `--target-side` if that was given.
    /// </summary>
    public TargetSide Side { get; }
}

/// <summary>
/// Every output target in this assembly, found by attribute.
///
/// This replaced a run of hand-written `if (recipe.X.Y.Count > 0)` blocks in
/// <see cref="Program"/>, plus a second hand-written list in the validation pass that
/// had to name the same sections again. The two lists had drifted: all four database
/// sections were missing from the validation one, so a recipe whose only server-side
/// output was a database export had its cross-side references left unchecked. Deriving
/// both from one registry is what stops that from recurring.
///
/// A target's entries all come from the recipe's `Targets` list, and it reads them not
/// at all: the registry collects the entries naming its id and hands them over one at a
/// time. That is what lets a target be added without extending
/// <see cref="RecipeModel"/> - the recipe schema does not grow a section per target, and
/// so a target can be deleted by deleting its file.
/// </summary>
public static class TargetRegistry
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    /// <summary>Field of a `Targets` entry that names the target. Matched case-insensitively.</summary>
    private const string TypeField = "Type";

    /// <summary>
    /// Deserializes a `Targets` entry into its target's entry type.
    ///
    /// <see cref="MissingMemberHandling.Error"/> on purpose: without it a misspelled
    /// setting is dropped and the target runs on the default, which looks like the
    /// option having no effect. There is no case where silently ignoring a field
    /// somebody wrote in a recipe is the helpful answer.
    /// </summary>
    private static readonly JsonSerializer DynamicEntryReader = JsonSerializer.Create(
        new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });

    private static readonly Lazy<IReadOnlyList<TargetDescriptor>> LazyAll =
        new Lazy<IReadOnlyList<TargetDescriptor>>(Discover);

    /// <summary>
    /// All registered targets, ordered by kind, then <see cref="TargetDescriptor.Order"/>,
    /// then id.
    /// </summary>
    public static IReadOnlyList<TargetDescriptor> All => LazyAll.Value;

    /// <summary>
    /// Every entry this run will build, in the order they will run.
    ///
    /// Both the run and the validation pass read this, so they cannot disagree about
    /// what the recipe requested - and, since <paramref name="requested"/> is applied
    /// here, a narrowed run is not validated against the side it is not building.
    /// </summary>
    /// <param name="requested">
    /// The side the run is narrowed to. <see cref="TargetSide.Both"/> narrows nothing.
    /// </param>
    public static IEnumerable<PlannedTarget> Plan(RecipeModel recipe, TargetSide requested)
    {
        // Ahead of any work, so an unknown target id is reported even if no other
        // entry would have produced output.
        VerifyDynamicEntries(recipe);

        foreach (var descriptor in All)
        {
            foreach (var (entry, section) in EntriesOf(descriptor, recipe))
            {
                var declared = RecipeTargetSide.Of(entry.TargetSide, section);

                // Overlap rather than equality: an entry declared for both sides
                // belongs in a client run and in a server run alike, while a
                // server-only entry belongs in neither a client run nor its output.
                if (!TargetSides.Includes(requested, declared))
                    continue;

                // The intersection, so a `cs` entry in a server run produces the
                // server cut rather than everything, and a `c` entry is unaffected by
                // a client run because it is already that narrow.
                yield return new PlannedTarget(descriptor, entry, section, declared & requested);
            }
        }
    }

    /// <summary>
    /// Runs every entry this run builds.
    ///
    /// The model is narrowed here rather than inside each target, so a target reads
    /// only what its entry is entitled to and none of them can forget to project.
    /// </summary>
    public static void RunAll(
        Options options, RecipeModel recipe, Model model, RunTimings timings, BuildCache cache)
    {
        var requested = CommandLineTargetSide.Of(options);

        if (requested != TargetSide.Both)
            Log.Information($"Narrowed to the {TargetSides.Describe(requested)} side by --target-side.");

        // Once for the run, and only if something asks: resolving it spawns git, and
        // most conversions have no target that records anything.
        var commit = new Lazy<CommitInfo>(() => CommitInfo.Resolve(options, recipe));

        // Asked before anything is built, because narrowing the model is work too. A false
        // answer has already declared that entry's previous output as still standing -
        // otherwise the sweep would delete it for not having been written.
        var building = Plan(recipe, requested).Where(cache.ShouldRun).ToList();

        // The projections, made once per side rather than once per entry.
        //
        // Two reasons, and the second is the one that matters. It is less work - a recipe
        // naming ten client-side outputs was narrowing the same model ten times - and
        // `ProjectTo` cannot be called from several threads at once: it publishes the
        // projected model as `Model.Current` and puts the previous one back when it is done,
        // which is not a thing two threads can do to one static field.
        var sided = new Dictionary<TargetSide, Model>();

        foreach (var planned in building)
        {
            if (!sided.ContainsKey(planned.Side))
                sided[planned.Side] = model.ProjectTo(planned.Side);
        }

        // Every table's derived column lists, built before anything reads them.
        //
        // The exporters and the generators all read `WireColumns` and `SerialFields`, and
        // those are built on first use - which is not something two entries can do at once.
        // Built here, once, they are read-only for the rest of the run.
        foreach (var projection in sided.Values)
        {
            foreach (var table in projection.Tables)
                table.BuildDerivedColumns();
        }

        // Built in parallel, grouped by target.
        //
        // **The entries of one target run in order, and the targets run beside each other.**
        // A target is one object serving every entry that names it - it holds its manifest
        // and its keys in fields - so two of its entries at once would be two runs sharing
        // that state. Different targets share nothing but the staging ledger, which locks.
        //
        // What each entry staged is attributed by name rather than by counting the ledger
        // before and after, which is what made this sequential: a slice of a shared list is
        // an answer only while one entry is writing to it.
        // spec/ops/conversion-time.md section 5.
        var byTarget = building.GroupBy(planned => planned.Descriptor).ToList();

        System.Threading.Tasks.Parallel.ForEach(byTarget, group =>
        {
            foreach (var planned in group)
            {
                using (StagingFiles.Attributing(planned.Section!))
                using (timings.MeasureEntry($"{planned.Section} {planned.Descriptor.Id}"))
                {
                    planned.Descriptor.Target.Run(new TargetContext(
                        options, recipe, sided[planned.Side], model, commit,
                        planned.Entry, planned.Section));
                }
            }
        });

        // Told to the cache afterwards, in the order the recipe lists the entries. What goes
        // into the seal is then the same whichever order the entries finished in.
        foreach (var planned in building)
        {
            cache.Wrote(
                planned.Section!, StagingFiles.StagedBy(planned.Section!),
                planned.Descriptor.Deterministic);
        }
    }

    /// <summary>Ids of every registered target, for help text and error messages.</summary>
    public static string KnownIds => string.Join(", ", All.Select(d => d.Id));

    // ------------------------------------------------------------ entries

    /// <summary>
    /// One target's entries: the `Targets` entries naming it, each paired with where in
    /// the recipe it came from.
    /// </summary>
    private static IEnumerable<(IOutputRecipe Entry, string Section)> EntriesOf(
        TargetDescriptor descriptor, RecipeModel recipe)
    {
        var dynamicEntries = recipe.Targets;
        if (dynamicEntries is null)
            yield break;

        for (int index = 0; index < dynamicEntries.Count; index++)
        {
            var json = dynamicEntries[index];
            if (json is null)
                continue;

            string? id = TypeOf(json);
            if (!string.Equals(id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            string section = $"Targets[{index}]";

            yield return (Materialize(json, descriptor, section), section);
        }
    }

    /// <summary>The `Type` field of a `Targets` entry, or null when it has none.</summary>
    private static string? TypeOf(JObject json)
    {
        var property = json.Properties()
                           .FirstOrDefault(p => string.Equals(p.Name, TypeField, StringComparison.OrdinalIgnoreCase));

        return property?.Value?.Type == JTokenType.String ? (string?)property.Value : null;
    }

    /// <summary>
    /// Reads a `Targets` entry into its target's entry type.
    /// </summary>
    private static IOutputRecipe Materialize(JObject json, TargetDescriptor descriptor, string section)
    {
        // `Type` selected the target and is not one of its settings, so it would trip
        // the missing-member check that catches the settings that really are misspelled.
        var settings = (JObject)json.DeepClone();

        foreach (var property in settings.Properties()
                                         .Where(p => string.Equals(p.Name, TypeField, StringComparison.OrdinalIgnoreCase))
                                         .ToList())
        {
            property.Remove();
        }

        try
        {
            return (IOutputRecipe)settings.ToObject(descriptor.EntryType, DynamicEntryReader)!;
        }
        catch (JsonException ex)
        {
                throw new TabbitException(null,
                    Messages.Message.Of(Recipe.RecipeMessages.SectionCouldNotBeRead,
                        ("Section", section), ("Target", descriptor.Id),
                        ("Detail", ex.Message)));
        }
    }

    /// <summary>
    /// Checks that every `Targets` entry names a target that exists.
    ///
    /// Separate from <see cref="EntriesOf"/> because that one is asked about a single
    /// target at a time and so cannot tell "not mine" from "nobody's".
    /// </summary>
    private static void VerifyDynamicEntries(RecipeModel recipe)
    {
        var dynamicEntries = recipe.Targets;
        if (dynamicEntries is null)
            return;

        for (int index = 0; index < dynamicEntries.Count; index++)
        {
            var json = dynamicEntries[index];
            if (json is null)
                continue;

            string? id = TypeOf(json);

            if (string.IsNullOrWhiteSpace(id))
            {
                    throw new TabbitException(null,
                        Messages.Message.Of(Recipe.RecipeMessages.TargetEntryHasNoType,
                            ("Index", index), ("Known", KnownIds)));
            }

            if (!All.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                    throw new TabbitException(null,
                        Messages.Message.Of(Recipe.RecipeMessages.TargetUnknown,
                            ("Index", index), ("Target", id), ("Known", KnownIds)));
            }
        }
    }

    // ----------------------------------------------------------- discovery

    private static IReadOnlyList<TargetDescriptor> Discover()
    {
        var descriptors = new List<TargetDescriptor>();

        foreach (var type in typeof(TargetRegistry).Assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<TabbitTargetAttribute>();
            if (attribute is null)
                continue;

            if (type.IsAbstract || !typeof(ITarget).IsAssignableFrom(type))
            {
                    throw new TabbitDefectException(
                        $"`{type.Name}` is marked [TabbitTarget] but is not a concrete {nameof(ITarget)}.");
            }

            var target = (ITarget)Activator.CreateInstance(type)!;

            descriptors.Add(new TargetDescriptor(
                attribute.Id, attribute.Kind, attribute.Order, attribute.Deterministic, target));
        }

        var duplicate = descriptors.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                                   .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
                throw new TabbitDefectException($"Two targets both claim the id `{duplicate.Key}`.");

        descriptors.Sort((left, right) =>
        {
            int byKind = left.Kind.CompareTo(right.Kind);
            if (byKind != 0)
                return byKind;

            int byOrder = left.Order.CompareTo(right.Order);
            if (byOrder != 0)
                return byOrder;

            return string.CompareOrdinal(left.Id, right.Id);
        });

        Log.Debug($"Registered {descriptors.Count} target(s): {string.Join(", ", descriptors.Select(d => d.Id))}");

        return descriptors;
    }

}
