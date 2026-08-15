using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// What every generated page carries: its title and the line recording who built it.
/// </summary>
internal abstract class HtmlPageView
{
    public required string Title { get; set; }

    /// <summary>
    /// Build time, already formatted. The golden comparison normalizes it away.
    /// </summary>
    /// <remarks>
    /// Stamped by `Write` rather than by whoever built the view, so it is the same on
    /// every page of one run. Not required for that reason: no caller sets it.
    /// </remarks>
    public string CreatedAt { get; set; } = "";
}

internal sealed class HtmlIndexView : HtmlPageView
{
    public required IReadOnlyList<HtmlSummaryEntryView> Enums { get; set; }
    public required IReadOnlyList<HtmlSummaryEntryView> Tables { get; set; }
    public required IReadOnlyList<HtmlSummaryEntryView> ConstantSets { get; set; }
    public required IReadOnlyList<HtmlSourceSheetView> SourceSheets { get; set; }
}

internal sealed class HtmlSummaryEntryView
{
    public required string Name { get; set; }

    /// <summary>Escaped comment, or empty. The template decides whether to show a dash.</summary>
    public required string Comment { get; set; }

    /// <summary>
    /// Where the entry's own page and anchor are.
    ///
    /// Built by the generator rather than assembled in the template, because the
    /// template had no way to know where the generator writes an enum - and got it
    /// wrong. Every enum link pointed at `enums.html`, which this target has never
    /// written: the pages are `enums/&lt;name&gt;.html`, one per enum.
    /// </summary>
    public required string Href { get; set; }
}

internal sealed class HtmlSourceSheetView
{
    /// <summary>
    /// Where the sheet is, or empty for a workbook on disk.
    ///
    /// Only a Google Sheets source has one. The template shows the name as text when
    /// this is empty rather than wrapping it in `href=""`, which is a link to the page
    /// the reader is already on.
    /// </summary>
    public required string Url { get; set; }

    public required string Filename { get; set; }
}

internal sealed class HtmlEnumPageView : HtmlPageView
{
    public required string Name { get; set; }

    /// <summary>A rendered anchor back to the source sheet, or empty when there is none.</summary>
    public required string SourceLink { get; set; }

    public required string Comment { get; set; }

    public required IReadOnlyList<HtmlEnumLabelView> Labels { get; set; }
}

internal sealed class HtmlEnumLabelView
{
    public required int No { get; set; }
    public required string Name { get; set; }
    public required string SourceLink { get; set; }
    public required string Value { get; set; }
    public required string Comment { get; set; }
}

internal sealed class HtmlConstantSetsPageView : HtmlPageView
{
    public required IReadOnlyList<HtmlConstantSetView> Sets { get; set; }
}

internal sealed class HtmlConstantSetView
{
    public required string Name { get; set; }
    public required string SourceLink { get; set; }
    public required string Comment { get; set; }
    public required IReadOnlyList<HtmlConstantView> Constants { get; set; }
}

internal sealed class HtmlConstantView
{
    public required int No { get; set; }

    /// <summary>The constant's own name, which the row's anchor id is built from.</summary>
    public required string Name { get; set; }

    /// <summary>Rendered cell contents, because an enum constant shows links where a
    /// plain one shows text.</summary>
    public required string NameCell { get; set; }

    public required string TypeCell { get; set; }
    public required string ValueCell { get; set; }
    public required string Comment { get; set; }
}

internal sealed class HtmlTablesPageView : HtmlPageView
{
    public required IReadOnlyList<HtmlTableView> Tables { get; set; }
}

internal sealed class HtmlTableView
{
    public required string Name { get; set; }
    public required string SourceLink { get; set; }
    public required string Comment { get; set; }
    public required int RecordCount { get; set; }

    /// <summary>Complete `&lt;th&gt;` elements for the column-name row, one per line.</summary>
    public required IReadOnlyList<string> NameCells { get; set; }

    /// <summary>Complete `&lt;th&gt;` elements for the description row, one per line.</summary>
    public required IReadOnlyList<string> CommentCells { get; set; }

    /// <summary>
    /// Complete `&lt;th&gt;` elements for the type row.
    ///
    /// These go on one line, unlike the rows above, because the printer used Print
    /// rather than PrintLine for them - and `&lt;/thead&gt;` lands at the end of that
    /// same line as a result. Reproduced rather than tidied, so the golden pages do
    /// not move.
    /// </summary>
    public required IReadOnlyList<string> TypeCells { get; set; }

    /// <summary>Complete `&lt;th&gt;` elements for the target-side row, one per line.</summary>
    public required IReadOnlyList<string> SideCells { get; set; }

    public required IReadOnlyList<HtmlRowView> Rows { get; set; }
}

internal sealed class HtmlRowView
{
    /// <summary>
    /// Complete `&lt;td&gt;` elements, rendered here because a cell's markup depends on
    /// the field's type. They go on one line, with `&lt;/tr&gt;` at the end of it.
    /// </summary>
    public required IReadOnlyList<string> Cells { get; set; }
}
