using System;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Tabbit.Messages;

namespace Tabbit.Sources;

/// <summary>
/// The settings every sheet-reading source shares, read out of one recipe entry.
/// </summary>
/// <remarks>
/// Read once per entry rather than per sheet, so a malformed setting is reported before any
/// sheet is read instead of on whichever one happened to reach it first.
/// </remarks>
public sealed class SheetImportSettings
{
    private SheetImportSettings(SheetFilter filter, SheetLayout layout)
    {
        Filter = filter;
        Layout = layout;
    }

    /// <summary>Which workbooks of the source to read, and which of their sheets.</summary>
    public SheetFilter Filter { get; }

    /// <summary>How to read them, stamped onto every sheet this entry produces.</summary>
    public SheetLayout Layout { get; }

    /// <summary>
    /// Reads one entry's settings, rejecting values that are not spellings of anything.
    /// </summary>
    /// <param name="section">Recipe path of the entry, for messages.</param>
    public static SheetImportSettings From(SheetSourceRecipe recipe, string section)
    {
        if (recipe is null)
            return new SheetImportSettings(SheetFilter.All, SheetLayout.Default);

        string layoutId = (recipe.Layout ?? "").Trim();
        if (layoutId.Length == 0)
            layoutId = SheetLayout.Default.Id;

        return new SheetImportSettings(
            SheetFilter.From(recipe, section),
            new SheetLayout(
                layoutId.ToLowerInvariant(),
                ParseDuplicateIndexPolicy(recipe.OnDuplicateIndex, section),
                ParseArrayDelimiter(recipe.ArrayDelimiter, section),
                ParseFormulaErrorPolicy(recipe.OnFormulaError, section),

                // Passed through without inspection. Which keys mean anything is the
                // layout's business, and keeping that out of here is what stops a layout's
                // settings from becoming part of the core recipe.
                recipe.LayoutOptions,

                recipe.FoldSerialFields,
                recipe.TrimTrailingArrayElements,
                recipe.AllowArrayGaps,
                (recipe.TableRowSets ?? "").Trim(),
                ParseBlankCellPolicy(recipe.OnBlankCell, section),

                // Resolved here so a name no zone answers to is reported before a workbook
                // is opened, and so the machine's zone list is consulted once for the entry
                // rather than once for every dated cell in it.
                Helpers.TimeZones.OfEntry(recipe.TimeZone, section)));
    }

    private static BlankCellPolicy ParseBlankCellPolicy(string value, string section)
    {
        // Blank is the strict default rather than an error, as above: an entry written
        // before this setting existed holds it.
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return BlankCellPolicy.Error;

        switch (text.ToLowerInvariant())
        {
            case "error": return BlankCellPolicy.Error;
            case "empty": return BlankCellPolicy.Empty;
        }

        throw new TabbitException(null,
            Message.Of(Recipe.RecipeMessages.OnBlankCellUnknown, ("Section", section), ("Value", text)));
    }

    private static FormulaErrorPolicy ParseFormulaErrorPolicy(string value, string section)
    {
        // Blank is the default rather than an error, for the same reason as above: it is
        // what an entry written before the setting existed holds.
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return FormulaErrorPolicy.Error;

        switch (text.ToLowerInvariant())
        {
            case "error": return FormulaErrorPolicy.Error;
            case "empty": return FormulaErrorPolicy.Empty;
        }

        throw new TabbitException(null,
            Message.Of(Recipe.RecipeMessages.OnFormulaErrorUnknown, ("Section", section), ("Value", text)));
    }

    /// <summary>
    /// Reads an entry's array delimiter, or null when it does not set one.
    /// </summary>
    /// <remarks>
    /// Blank means "whatever the recipe says" rather than "no delimiter": an entry written
    /// before this setting existed holds blank, and so does one where the line was deleted.
    /// A value that is present but not one character is an error, because the alternative is
    /// splitting on the first character of it and reporting nothing.
    /// </remarks>
    private static char? ParseArrayDelimiter(string value, string section)
    {
        string text = value ?? "";
        if (text.Length == 0)
            return null;

        if (text.Length != 1)
        {
            throw new TabbitException(null,
                Message.Of(Recipe.RecipeMessages.EntryArrayDelimiterNotOneCharacter,
                    ("Section", section), ("Value", text)));
        }

        return text[0];
    }

    private static DuplicateIndexPolicy ParseDuplicateIndexPolicy(string value, string section)
    {
        // Blank is the default rather than an error: it is what an entry written before
        // the setting existed holds, and what deleting the line leaves behind.
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return DuplicateIndexPolicy.Error;

        switch (text.ToLowerInvariant().Replace("_", "-"))
        {
            case "error": return DuplicateIndexPolicy.Error;
            case "keep-first": return DuplicateIndexPolicy.KeepFirst;
            case "keep-last": return DuplicateIndexPolicy.KeepLast;
        }

        throw new TabbitException(null,
            Message.Of(Recipe.RecipeMessages.OnDuplicateIndexUnknown, ("Section", section), ("Value", text)));
    }
}
