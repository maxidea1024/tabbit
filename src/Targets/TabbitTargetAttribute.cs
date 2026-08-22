using System;

namespace Tabbit.Targets;

/// <summary>
/// What a target produces, which decides when it runs.
///
/// Exports run before code generation because the generated readers are written to
/// expect the data files that the exporters produce; when a run is inspected by hand
/// it reads better for the data to already be there.
/// </summary>
public enum TargetKind
{
    /// <summary>Writes data: files or database storage.</summary>
    Export,

    /// <summary>Writes source code, or documentation about the data.</summary>
    CodeGeneration,

    /// <summary>
    /// Describes the conversion rather than producing something a build consumes:
    /// statistics, and the change history.
    ///
    /// Last, so it can describe a run that has already happened. It is also the only
    /// kind that reads <see cref="ITarget"/>'s unnarrowed model, because a description
    /// of one side of the data presented as a description of all of it is not a
    /// narrower answer but a wrong one.
    /// </summary>
    Description,
}

/// <summary>
/// Marks a class as a Tabbit output target and gives the registry what it needs to
/// drive it.
///
/// Adding a target means adding one file with this attribute on it. Nothing else in
/// the codebase is edited - not <see cref="Program"/>, not the validation pass. That
/// is the point: the old shape needed a target's name written out in three separate
/// places, and the database exporters shipped with one of the three missing, so their
/// target side was never validated.
///
/// Discovery is a scan of this assembly, deliberately. Loading targets from external
/// assemblies would mean a plugin contract to keep stable across versions, for a tool
/// whose targets all live in this repository anyway.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TabbitTargetAttribute : Attribute
{
    public TabbitTargetAttribute(string id, TargetKind kind)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Kind = kind;
    }

    /// <summary>
    /// Stable short name, lower case. This is what a recipe's `Targets` entry names in
    /// its `Type` field, so changing one is a breaking change to every recipe using it.
    /// </summary>
    public string Id { get; }

    /// <summary>What the target produces.</summary>
    public TargetKind Kind { get; }

    /// <summary>
    /// Sort key within a kind; lower runs first. Ties break on <see cref="Id"/> so the
    /// order is total and a run's log is reproducible.
    ///
    /// Targets are independent - each writes to its own destination - so this exists
    /// to keep output stable rather than to satisfy a dependency.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether the same model produces the same bytes twice.
    /// </summary>
    /// <remarks>
    /// True for every target that writes only what the model says, which is nearly all of
    /// them - and it is what lets the build cache verify that a previous run's output is
    /// still intact, by hashing what is there and comparing.
    ///
    /// A target that writes when the run happened cannot be checked that way: its output is
    /// supposed to differ, so a hash that matches would be the surprising outcome. Declaring
    /// it here rather than listing the exceptions elsewhere keeps the fact next to the code
    /// that causes it - a target that stops stamping the time deletes one line and is
    /// verified from then on.
    /// </remarks>
    public bool Deterministic { get; set; } = true;
}
