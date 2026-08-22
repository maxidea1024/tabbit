using System;
using System.Reflection;
using Tabbit.History;
using Tabbit.Models;
using Tabbit.Recipe;

namespace Tabbit.Targets;

/// <summary>
/// Implemented by every recipe entry that produces output.
///
/// Exists so the registry can read an entry's target side without reflection, which
/// also means a new entry type that forgets the property does not compile.
/// </summary>
public interface IOutputRecipe
{
    /// <summary>
    /// Which side of the data this entry is built for: `cs` for both, or `c` / `s`.
    ///
    /// Text rather than the enum because it comes from JSON, and a typo should be
    /// reported against the recipe section it appeared in rather than silently
    /// deserializing to the default.
    /// </summary>
    string TargetSide { get; }
}

/// <summary>
/// One unit of work: a single recipe entry, with the model already narrowed for it.
/// </summary>
public sealed class TargetContext
{
    private readonly Lazy<CommitInfo> _commit;

    public TargetContext(
        Options options,
        RecipeModel recipe,
        Model model,
        Model fullModel,
        Lazy<CommitInfo> commit,
        IOutputRecipe entry,
        string? section)
    {
        Options = options;
        Recipe = recipe;
        Model = model;
        FullModel = fullModel;
        Entry = entry;
        Section = section;

        _commit = commit;
    }

    /// <summary>Command line options for the run.</summary>
    public Options Options { get; }

    /// <summary>The whole recipe, for the settings that apply across targets.</summary>
    public RecipeModel Recipe { get; }

    /// <summary>
    /// The model narrowed to <see cref="Entry"/>'s target side.
    ///
    /// The registry projects it, so a target cannot forget to - which used to be a
    /// line copied into each one.
    /// </summary>
    public Model Model { get; }

    /// <summary>
    /// Everything the sheets declared, before any target-side narrowing.
    ///
    /// Almost every target wants <see cref="Model"/>: output is built for one side and
    /// must not carry the other's fields. This exists for the targets that describe the
    /// data rather than emit it, where narrowing is not a filter but a falsehood - a
    /// history recorded from a client build would report every server-only table as
    /// deleted, and the next server build would report them all as added again.
    ///
    /// The same instance for every entry of a run, and the same one
    /// <see cref="Model.Current"/> points at.
    /// </summary>
    public Model FullModel { get; }

    /// <summary>
    /// Which commit this conversion is of, and who made it.
    ///
    /// Resolved on first use and shared by every entry of the run. Deferred because
    /// resolving it spawns git, and the targets that care are the ones describing the
    /// data rather than the ones emitting it.
    /// </summary>
    public CommitInfo Commit => _commit.Value;

    /// <summary>The recipe entry being run.</summary>
    public IOutputRecipe Entry { get; }

    /// <summary>Dotted recipe path of the section this entry came from.</summary>
    public string? Section { get; }
}

/// <summary>
/// A target, as the registry sees it. Implement <see cref="Target{TEntry}"/> rather
/// than this.
/// </summary>
public interface ITarget
{
    /// <summary>
    /// The recipe entry type this target takes its settings from.
    ///
    /// The registry needs it for both entry sources: to check that the target's own
    /// recipe section is a list of this type, and to deserialize a `Targets` entry into
    /// it.
    /// </summary>
    Type EntryType { get; }

    /// <summary>Runs one entry.</summary>
    void Run(TargetContext context);
}

/// <summary>
/// Base for a target driven by one recipe entry type.
///
/// A target implements <see cref="Run(TargetContext, TEntry)"/> and nothing else. It
/// does not read the recipe: the registry collects its entries, from its own section
/// and from the `Targets` list, and narrows the model for each one. So a target has no
/// way to disagree with the registry about which section it belongs to, and a target
/// for a new language needs no recipe section at all.
///
/// The generic parameter keeps the cast to the entry type here instead of at the top of
/// every target.
/// </summary>
public abstract class Target<TEntry> : ITarget
    where TEntry : class, IOutputRecipe
{
    /// <summary>
    /// Runs one entry against <see cref="TargetContext.Model"/>, which is already
    /// narrowed to the entry's target side.
    /// </summary>
    protected abstract void Run(TargetContext context, TEntry entry);

    /// <summary>
    /// Whether this target knows what to do with a record group - a field folded from
    /// several columns by the `Group.Member` notation.
    /// </summary>
    /// <remarks>
    /// False by default, and each target opts in as it learns the shape. The default has
    /// to be the refusing one: a target that does not know about records would otherwise
    /// reach <see cref="SerialField.FirstField"/>, get the null a record group answers
    /// with, and fail somewhere that says nothing about the cause - or worse, emit a
    /// plausible file built from one arbitrary member.
    ///
    /// The point of the flag is that the targets need not be converted at once, and one
    /// that has not been produces a message naming itself - a far better answer than output
    /// that differs from the other twelve for reasons nobody can see.
    ///
    /// All the code generators now say true, so this is what a new one would meet
    /// before it had learned. The exporters that cannot express a record still refuse.
    /// </remarks>
    protected virtual bool SupportsNestedFields => false;

    /// <summary>
    /// Whether this target knows what to do with a record whose member is itself a record -
    /// nesting more than one level deep.
    /// </summary>
    /// <remarks>
    /// A separate flag from <see cref="SupportsNestedFields"/> and for the same reason it
    /// exists at all: the shape reaches the wire without the format changing, so a target
    /// that has not learned it would emit something plausible rather than fail. A generator
    /// that declares a member from the leaf's name alone produces `record.Star[j].X` for a
    /// column that is `Star1.Position.X` - code that compiles against a type that does not
    /// exist, or worse, against one that does and means something else.
    ///
    /// The model and the wire are done; the targets are converted one at a time, and one
    /// that has not been says so by name. spec/nested-multi-level.md.
    /// </remarks>
    protected virtual bool SupportsDeepNestedFields => false;

    /// <summary>
    /// Whether this target knows what to do with a column that may have no value - a field
    /// whose type carries a trailing `?`.
    /// </summary>
    /// <remarks>
    /// False by default and opted into per target, for the same reason as
    /// <see cref="SupportsNestedFields"/>: a target that does not know would produce output
    /// where absent and empty look the same, which is precisely the distinction the marker
    /// exists to make. Silently losing it is worse than a message naming the target.
    /// </remarks>
    protected virtual bool SupportsOptionalFields => false;

    /// <summary>
    /// Whether this target can say that one element of an array has no value.
    /// </summary>
    /// <remarks>
    /// A second flag rather than a widening of <see cref="SupportsOptionalFields"/>, because
    /// the two are answered by different code: a target that carries a presence bit per row
    /// still has nowhere to put one per element. Opted into as each learns the shape, which
    /// is the order spec/nullable-array-elements.md sets out.
    /// </remarks>
    protected virtual bool SupportsOptionalElements => false;

    Type ITarget.EntryType => typeof(TEntry);

    void ITarget.Run(TargetContext context)
    {
        RefuseNestedFieldsIfUnsupported(context);
        RefuseDeepNestedFieldsIfUnsupported(context);
        RefuseOptionalFieldsIfUnsupported(context);
        RefuseOptionalElementsIfUnsupported(context);

        Run(context, (TEntry)context.Entry);
    }

    /// <summary>
    /// Stops before a target that cannot express absence is handed a column that has it.
    /// </summary>
    private void RefuseOptionalFieldsIfUnsupported(TargetContext context)
    {
        if (SupportsOptionalFields)
            return;

        foreach (var table in context.Model.Tables)
        {
            foreach (var wire in table.WireColumns)
            {
                if (!wire.IsNullable)
                    continue;

                string id = GetType().GetCustomAttribute<TabbitTargetAttribute>()?.Id ?? GetType().Name;

                throw new TabbitException(wire.TagCarrier.TypeLocation,
                    Messages.Message.Of(Exporters.ExportMessages.TargetNoOptionalFields,
                        ("Target", id), ("Table", table.Name), ("Column", wire.Name),
                        ("Type", wire.TagCarrier.TypeName)));
            }
        }
    }

    /// <summary>
    /// Stops before a target that cannot say an element is absent is handed a column of them.
    /// </summary>
    private void RefuseOptionalElementsIfUnsupported(TargetContext context)
    {
        if (SupportsOptionalElements)
            return;

        foreach (var table in context.Model.Tables)
        {
            foreach (var wire in table.WireColumns)
            {
                if (!wire.HasOptionalElements)
                    continue;

                string id = GetType().GetCustomAttribute<TabbitTargetAttribute>()?.Id ?? GetType().Name;

                throw new TabbitException(wire.TagCarrier.TypeLocation,
                    Messages.Message.Of(Exporters.ExportMessages.TargetNoOptionalElements,
                        ("Target", id), ("Table", table.Name), ("Column", wire.Name),
                        ("Type", wire.TagCarrier.TypeName)));
            }
        }
    }

    /// <summary>
    /// Stops before a target that does not understand records is handed one.
    /// </summary>
    private void RefuseNestedFieldsIfUnsupported(TargetContext context)
    {
        if (SupportsNestedFields)
            return;

        foreach (var table in context.Model.Tables)
        {
            foreach (var group in table.SerialFields)
            {
                if (!group.IsRecord)
                    continue;

                // Named from the attribute rather than passed in, so a target cannot
                // report an id a recipe could not have written.
                string id = GetType().GetCustomAttribute<TabbitTargetAttribute>()?.Id ?? GetType().Name;

                throw new TabbitException(group.AnyField?.NameLocation,
                    Messages.Message.Of(Exporters.ExportMessages.TargetNoNestedFields,
                        ("Target", id), ("Table", table.Name), ("Field", group.Name),
                        ("Count", group.Members.Count),
                        ("Separator", Helpers.NestedName.MemberSeparator)));
            }
        }
    }

    /// <summary>
    /// Stops before a target that understands a record but not a record inside one is handed
    /// the second.
    /// </summary>
    private void RefuseDeepNestedFieldsIfUnsupported(TargetContext context)
    {
        if (SupportsDeepNestedFields)
            return;

        foreach (var table in context.Model.Tables)
        {
            foreach (var group in table.SerialFields)
            {
                if (!group.IsRecord)
                    continue;

                var deep = group.Members.Find(member => !member.IsLeaf);
                if (deep is null)
                    continue;

                string id = GetType().GetCustomAttribute<TabbitTargetAttribute>()?.Id ?? GetType().Name;

                throw new TabbitException(deep.FirstField?.NameLocation,
                    Messages.Message.Of(Exporters.ExportMessages.TargetNoRecordInRecord,
                        ("Target", id), ("Table", table.Name), ("Field", group.Name),
                        ("Member", deep.Name), ("Count", deep.Members.Count),
                        ("Separator", Helpers.NestedName.MemberSeparator),
                        ("Inner", deep.Members[0].Name)));
            }
        }
    }
}
