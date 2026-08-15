using System;

namespace Tabbit.Sources;

/// <summary>
/// Marks a class as a Tabbit input source and gives the registry what it needs to
/// drive it.
///
/// The counterpart of <see cref="Targets.TabbitTargetAttribute"/>, and simpler:
/// a source has no target side to narrow and no model to project, because it runs
/// before there is a model at all.
///
/// Adding a source means adding one file with this attribute on it. Nothing else in
/// the codebase is edited, which is what the note left in Program.Process asked for.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TabbitSourceAttribute : Attribute
{
    public TabbitSourceAttribute(string id, string section)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Section = section ?? throw new ArgumentNullException(nameof(section));
    }

    /// <summary>Stable short name, lower case. Used in log lines and error messages.</summary>
    public string Id { get; }

    /// <summary>
    /// Dotted path of the recipe section this source reads, such as `Sources.Xlsx`.
    ///
    /// Required, unlike a target's: there is no dynamic list for sources, because the
    /// reason targets have one is a language per target and sources do not multiply
    /// that way.
    ///
    /// The registry reads the section through this rather than asking the source for its
    /// entries, so a source cannot name one section here and read another.
    /// </summary>
    public string Section { get; }

    /// <summary>
    /// Sort key; lower runs first, ties broken on <see cref="Id"/> so the order is total
    /// and a run's log is reproducible.
    ///
    /// Sources all append to the same raw model, and a table may only be defined once,
    /// so the order changes which of two conflicting definitions is reported as the
    /// duplicate - not which of them wins.
    /// </summary>
    public int Order { get; set; }
}
