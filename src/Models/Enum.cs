using Newtonsoft.Json;
using System.Collections.Generic;
using Tabbit.Extensions;
using System.Globalization;

namespace Tabbit.Models;

/// <summary>
/// An enumeration declared in the sheets with a `~~enum:Name~~` marker.
///
/// Table columns typed `enum` refer to one of these, and cells hold a label name.
/// Only the label's integer value is ever stored, so a label can be renamed without
/// touching exported data - but changing its value silently reinterprets every row
/// that used it.
/// </summary>
public class Enum
{
    /// <summary>
    /// One named value of an enumeration.
    /// </summary>
    public class Label
    {
        /// <summary>Cell the label was declared in.</summary>
        [JsonIgnore] public Location Location { get; set; } = null!;

        /// <summary>Name exactly as written in the sheet.</summary>
        public required string RawName { get; set; }

        /// <summary>Name normalized to Pascal case, which is what generated code uses.</summary>
        public required string Name { get; set; }

        /// <summary>The integer this label stands for. This is what gets stored and exported.</summary>
        public int Value { get; set; }

        /// <summary>Description from the sheet, emitted as a doc comment.</summary>
        public required string Comment { get; set; }

        /// <summary>
        /// A second name a data cell may write this label by, or empty.
        /// </summary>
        /// <remarks>
        /// **A spelling, not a name.** <see cref="Name"/> is what generated code says and
        /// <see cref="RawName"/> is what the declaration said; this is a third thing a cell is
        /// allowed to say and resolves to the same label. Nothing downstream sees it - only the
        /// integer is stored - so an alias can be added or dropped without touching exported
        /// data. What it does change is which text resolves, so the cells written with it have
        /// to be found and changed when it goes.
        ///
        /// It is its own field rather than a second value in `RawName`, which would be the
        /// obvious shortcut and is wrong twice: `RawName` is the spelling the naming-convention
        /// report holds a sheet to, and a label's own spelling would stop resolving the moment
        /// an alias replaced it.
        /// </remarks>
        public string Alias { get; set; } = "";

        /// <summary>
        /// Whether the tool wrote this label rather than a sheet - the zero label inserted
        /// into an enum that declared nothing at zero.
        /// </summary>
        /// <remarks>
        /// Kept so that checks about how a sheet is written can leave it out. Holding a
        /// generated name to a naming convention reports a spelling nobody chose and offers
        /// nobody a cell to fix, and the label carries its enum's location, so a report about
        /// it would point at the declaration rather than at any name.
        /// </remarks>
        [JsonIgnore]
        public bool Synthesized { get; set; }
    }

    /// <summary>Cell holding the entity marker that declared this enum.</summary>
    [JsonIgnore] public Location Location { get; set; } = null!;

    /// <summary>Target side filtering option</summary>
    public TargetSide TargetSide { get; set; }

    /// <summary>Name exactly as written in the sheet.</summary>
    public required string RawName { get; set; }

    /// <summary>Name normalized to Pascal case, which is what generated code uses.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The labels, in declaration order.
    ///
    /// A `None = 0` label is inserted at the front when the sheet declares neither
    /// the name nor the value zero, so a default-constructed field of this type means
    /// something.
    /// </summary>
    public List<Label> Labels { get; set; } = new List<Label>();

    /// <summary>Description from the sheet, emitted as a doc comment.</summary>
    public required string Comment { get; set; }

    /// <summary>
    /// Whether the tool declared this enumeration rather than a sheet.
    /// </summary>
    /// <remarks>
    /// A column reaching several tables carries one of them, and which one is answered by an
    /// enumeration of its targets. That is a type the generated code needs and no sheet
    /// writes, so the cooker declares it here - and then every generator emits it through the
    /// machinery it already has for enumerations, which is the whole reason it lives in the
    /// model rather than in each generator.
    ///
    /// Kept so that reports about how the sheets are written can leave it out: holding a
    /// generated name to a naming convention names a spelling nobody chose and offers nobody
    /// a cell to fix. spec/multi-target-accessors.md.
    /// </remarks>
    [JsonIgnore]
    public bool Synthesized { get; set; }

    /// <summary>
    /// Whether a label with this name or value exists.
    /// </summary>
    public bool Contains(object labelNameOrValue) => FindLabel(labelNameOrValue) is not null;

    /// <summary>
    /// Finds a label, or throws naming the cell that asked for it.
    /// </summary>
    public Label GetLabel(object labelNameOrValue, Location? callerLocation)
    {
        var found = FindLabel(labelNameOrValue);
        if (found is null)
        {
            if (labelNameOrValue is string name)
                    throw new TabbitException(callerLocation,
                        Messages.Message.Of(Cooking.CookingMessages.EnumLabelNotFound,
                            ("Label", name), ("Enum", Name)));
            else if (labelNameOrValue is int value)
                    throw new TabbitException(callerLocation,
                        Messages.Message.Of(Cooking.CookingMessages.EnumValueNotFound,
                            ("Value", value), ("Enum", Name)));
            else
                    throw new TabbitDefectException(
                        "An enum label was looked up with neither a name nor a value, which the "
                        + "check above is supposed to have refused already.");
        }

        return found;
    }

    /// <summary>
    /// Finds a label by whatever a caller has: the text from a cell, or a value read
    /// back from storage.
    /// </summary>
    public Label? FindLabel(object labelNameOrValue)
    {
        if (labelNameOrValue is int value)
            return FindLabelByValue(value);

        if (labelNameOrValue is string text)
        {
            var byName = FindLabelByName(text);
            if (byName is not null)
                return byName;

            // A cell holding the number instead of the label name. Designers do
            // write `1` rather than `Common`, and refusing it would be pedantry when
            // the intent is unambiguous.
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
                return FindLabelByValue(numeric);

            return null;
        }

            throw new TabbitDefectException(
                $"Enum `{Name}` was looked up with a {labelNameOrValue?.GetType().Name ?? "null"}, " +
                $"but only a label name or an integer value can identify a label.");
    }

    /// <summary>
    /// Finds a label by the text written in a cell.
    ///
    /// Labels are stored under their Pascal-cased name, but data cells are authored
    /// by hand and naturally repeat whatever the enum declaration looked like. An
    /// enum declared as `fire_ball` is stored as `FireBall`, so a table cell saying
    /// `fire_ball` used to fail to resolve and the sheet had to spell the label
    /// differently from its own definition.
    ///
    /// Matching therefore proceeds from most to least specific: the stored name,
    /// then the original text as written in the declaration, then an alias the
    /// declaration gave it, and finally the caller's text normalized the same way
    /// the declaration was.
    /// </summary>
    public Label? FindLabelByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var byName = Labels.Find(x => x.Name == name);
        if (byName is not null)
            return byName;

        var byRawName = Labels.Find(x => x.RawName == name);
        if (byRawName is not null)
            return byRawName;

        // Ahead of the normalizing pass below and behind both real names, so an alias can
        // never take a cell that one of the labels' own spellings would have answered.
        var byAlias = Labels.Find(x => x.Alias.Length > 0 && x.Alias == name);
        if (byAlias is not null)
            return byAlias;

        string normalized = name.ToPascalCase();
        return Labels.Find(x => x.Name == normalized);
    }

    /// <summary>
    /// Finds a label by its integer value, which is how stored data refers to it.
    /// </summary>
    public Label? FindLabelByValue(int value) => Labels.Find(x => x.Value == value);
}
