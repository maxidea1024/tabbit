using System;

namespace Tabbit.Messages;

/// <summary>
/// Marks a class that declares message ids, and says which prefix they all carry.
/// </summary>
/// <remarks>
/// The prefix is what keeps this out of the core. A single enum of every id would mean a
/// layout parser cannot name its own reports without editing a file in the core, and the one
/// test this repository applies to a layout - delete its file and see whether anything is
/// left behind - would stop passing. So ids are strings, and whoever declares them owns a
/// prefix: the run's steps for the core (`cook`, `recipe`, `validate`, `export`, `import`),
/// and its own layout id for a layout.
///
/// The same shape as <see cref="Cooking.Layouts.TabbitLayoutAttribute"/> and for the same
/// reason: found by scanning, so there is no registration list anywhere to keep in step.
///
/// The attribute states the prefix once and <see cref="MessageRegistry"/> checks every
/// constant in the class against it, so a copied line that kept the wrong prefix is a
/// startup failure rather than a report nobody can find in a catalog.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TabbitMessagesAttribute : Attribute
{
    public TabbitMessagesAttribute(string prefix)
    {
        Prefix = prefix;
    }

    /// <summary>The prefix every id in the marked class begins with, without its dot.</summary>
    public string Prefix { get; }
}
