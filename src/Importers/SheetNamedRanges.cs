using System;
using System.Collections.Generic;
using Serilog;
using Tabbit.Models.Raw;
using Tabbit.Sources;
using Tabbit.Messages;

namespace Tabbit.Importers;

/// <summary>
/// A defined name resolved to one rectangle of one sheet, in that sheet's own coordinates.
/// </summary>
/// <remarks>
/// What each source has to produce for itself, and the only part of reading names that is
/// different between them: a workbook spells a reference as a string that has to be parsed,
/// and a document API hands the four numbers over directly.
///
/// Both ends are inclusive, as a workbook's own reference is. A source whose range ends
/// exclusively converts here rather than downstream, so the one convention holds from this
/// point on.
/// </remarks>
internal sealed record SheetNamedRange(
    string Name, string Reference,
    int FirstRow, int FirstColumn, int LastRow, int LastColumn);

/// <summary>
/// Puts a sheet's defined names onto its cell grid.
/// </summary>
/// <remarks>
/// Shared by every source that reads names, which is what keeps them reading alike. The
/// filtering, the translation and the clamping below are decisions about what a defined name
/// means to a recipe, and none of them is about where the sheet came from - so a source
/// keeping its own copy would be a place for the two to drift apart.
/// </remarks>
internal static class SheetNamedRanges
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Importing;

    /// <summary>
    /// Translates each name into the grid's coordinates and attaches it.
    /// </summary>
    /// <remarks>
    /// Translation rather than absolute coordinates, because <see cref="RawSheet.Optimize"/>
    /// has just trimmed the blank margins and everything downstream indexes the trimmed
    /// grid. The top-left cell knows where it came from, which is what the offset is.
    /// </remarks>
    /// <param name="rawSheet">The sheet, already squared off by <see cref="RawSheet.Optimize"/>.</param>
    /// <param name="names">
    /// The names that point into this sheet. Which ones those are is the caller's to decide,
    /// because a workbook says so by sheet name and a document API by sheet id.
    /// </param>
    /// <param name="workbook">The document as the recipe names it, for the filter to match.</param>
    /// <param name="source">What to call the document in a diagnostic.</param>
    public static void Attach(
        RawSheet rawSheet,
        IReadOnlyList<SheetNamedRange> names,
        SheetFilter filter,
        string workbook,
        string source)
    {
        if (names.Count == 0)
            return;

        // Where the trimmed grid sits in the sheet, so a name's cells can be found in it.
        var topLeft = rawSheet.Rows[0][0].Location;

        foreach (var named in names)
        {
            // The filter applies to the name as well as to the sheet, because in a layout
            // that reads defined names the name is what a table is called - and a document
            // holds names that are not tables. A single-column range behind a data-validation
            // dropdown is the common one.
            if (!filter.Includes(workbook, named.Name))
            {
                Log.Information(
                    $"Skipping defined name `{named.Name}` of `{source}`: "
                    + "the recipe does not ask for it.");
                continue;
            }

            int row = named.FirstRow - topLeft.Row;
            int column = named.FirstColumn - topLeft.Column;

            // A name may cover rows or columns the grid no longer has - trailing blanks
            // are exactly what Optimize removes, and a range drawn generously over them is
            // ordinary. Clamped rather than refused, so the table is the cells that exist.
            int height = Math.Min(named.LastRow - named.FirstRow + 1, rawSheet.Rows.Count - row);
            int width = Math.Min(named.LastColumn - named.FirstColumn + 1, rawSheet.ColumnCount - column);

            if (row < 0 || column < 0 || height <= 0 || width <= 0)
            {
                Log.Warning(Message.Of(ImportMessages.LogDefinedNameOutsideSheet,
                    ("Name", named.Name), ("Source", source),
                    ("Range", named.Reference),
                    ("Sheet", rawSheet.Location.Sheet)).In(MessageCatalog.Current));
                continue;
            }

            rawSheet.NamedRanges.Add(new RawNamedRange
            {
                Name = named.Name,
                Row = row,
                Column = column,
                Height = height,
                Width = width,
            });
        }
    }
}
