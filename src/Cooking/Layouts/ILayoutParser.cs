using System;
using System.Collections.Generic;
using Tabbit.Models.Raw;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// Reads a set of raw sheets into the model, according to one way of arranging a sheet.
/// </summary>
/// <remarks>
/// The seam between importing and cooking. Everything above it - the sources - produces a
/// grid of cells and knows nothing about what the cells mean; everything below it - cross
/// reference resolution, validation, every exporter and all thirteen generators - sees only
/// a <see cref="Models.Model"/> and knows nothing about where it came from. A layout is the
/// single step in between, and adding one is adding a file with
/// <see cref="TabbitLayoutAttribute"/> on it.
///
/// Parsing is in two passes because a table's columns may be typed with an enum, and the
/// enum may be declared in a workbook read under a different layout. Every parser is asked
/// for its declarations before any parser is asked for its tables, so the order sheets
/// happen to arrive in cannot decide whether a type resolves.
///
/// An instance is created per run and may keep state between the two calls.
/// </remarks>
public interface ILayoutParser
{
    /// <summary>
    /// Reads the entities a table may refer to by name: enums and constant sets.
    /// </summary>
    void ParseDeclarations(CookingContext context, IReadOnlyList<RawSheet> sheets);

    /// <summary>
    /// Reads the tables. Called once every layout's declarations are in the model.
    /// </summary>
    void ParseTables(CookingContext context, IReadOnlyList<RawSheet> sheets);
}

/// <summary>
/// Marks a class as a layout parser and gives it the id a recipe names it by.
/// </summary>
/// <remarks>
/// The same shape as <see cref="Sources.TabbitSourceAttribute"/> and
/// <see cref="Targets.TabbitTargetAttribute"/>, so there is one idea to learn rather than
/// three.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TabbitLayoutAttribute : Attribute
{
    public TabbitLayoutAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>Stable short name, lower case. What a recipe entry's `Layout` holds.</summary>
    public string Id { get; }

    /// <summary>One line on what the layout is, for the message that lists them.</summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// Whether this layout takes a workbook's defined names as its table boundaries.
    /// </summary>
    /// <remarks>
    /// Declared here so the importer can ask before it does the work: resolving every name
    /// of a workbook means parsing every reference, and a workbook can hold hundreds that
    /// no layout will ever look at.
    /// </remarks>
    public bool UsesNamedRanges { get; set; }
}
