using System;
using Tabbit.Models.Raw;
using Tabbit.Recipe;

namespace Tabbit.Sources;

/// <summary>
/// One unit of import work: a single recipe entry, and the raw model to add to.
/// </summary>
public sealed class SourceContext
{
    public SourceContext(Options options, RecipeModel recipe, RawModel model, object entry, string section)
    {
        Options = options;
        Recipe = recipe;
        Model = model;
        Entry = entry;
        Section = section;
    }

    /// <summary>Command line options for the run.</summary>
    public Options Options { get; }

    /// <summary>The whole recipe, for the settings that apply across sources.</summary>
    public RecipeModel Recipe { get; }

    /// <summary>
    /// The raw model every source appends to.
    ///
    /// Shared rather than one per source, because a project may spread its tables
    /// across workbooks and Google Sheets documents and they cook as one model.
    /// </summary>
    public RawModel Model { get; }

    /// <summary>The recipe entry being imported.</summary>
    public object Entry { get; }

    /// <summary>
    /// Where in the recipe this entry came from, including its index - `Sources.Xlsx[0]`.
    /// </summary>
    public string Section { get; }
}

/// <summary>
/// A source, as the registry sees it. Implement <see cref="Source{TEntry}"/> rather
/// than this.
/// </summary>
public interface ISource
{
    /// <summary>The recipe entry type this source takes its settings from.</summary>
    Type EntryType { get; }

    /// <summary>Imports one entry.</summary>
    void Import(SourceContext context);
}

/// <summary>
/// Base for a source driven by one recipe entry type.
///
/// A source implements <see cref="Import(SourceContext, TEntry)"/> and nothing else; the
/// registry collects its entries from the section its attribute names.
/// </summary>
public abstract class Source<TEntry> : ISource
    where TEntry : class
{
    /// <summary>
    /// Imports one entry into <see cref="SourceContext.Model"/>.
    ///
    /// Called once per entry. An entry that is not configured - the fields it needs left
    /// blank, which is how an entry is commented out in practice - should return without
    /// doing anything, the same way the file exporters treat a blank path.
    /// </summary>
    protected abstract void Import(SourceContext context, TEntry entry);

    Type ISource.EntryType => typeof(TEntry);

    void ISource.Import(SourceContext context) => Import(context, (TEntry)context.Entry);
}
