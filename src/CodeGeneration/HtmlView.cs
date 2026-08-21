using System;
using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// What every generated page carries: its title, the shell around it, and the line
/// recording when it was built.
/// </summary>
/// <remarks>
/// The shell members are on the base rather than on each page because the head and foot
/// templates read them, and a template reads what the view it was handed exposes. They
/// have defaults rather than being required for the same reason <see cref="CreatedAt"/>
/// does: the generator fills them in one place, after the page-specific parts are built.
/// </remarks>
internal abstract class HtmlPageView
{
    public required string Title { get; set; }

    /// <summary>
    /// Build time, already formatted. The golden comparison normalizes it away.
    /// </summary>
    /// <remarks>
    /// Stamped by `Write` rather than by whoever built the view, so it is the same on
    /// every page of one run.
    /// </remarks>
    public string CreatedAt { get; set; } = "";

    /// <summary>
    /// How to get from this page to the output root: empty for a page at the root,
    /// `../` for one in a subdirectory.
    /// </summary>
    /// <remarks>
    /// Every link in the shell is built from this, so a page in `tables/` and a page at
    /// the root can share one template. The alternative - absolute paths - would only
    /// work while the output sits where it was generated.
    /// </remarks>
    public string Root { get; set; } = "";

    /// <summary>Which entry of the top bar is the current one.</summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Whether this page is one table, and so should fill the window rather than scroll.
    /// </summary>
    /// <remarks>
    /// A page whose body is a single table gives the table the height that is left and
    /// lets it scroll inside that, which is what holds the column names on screen. A page
    /// of counters, or of several tables, scrolls the ordinary way - there is no single
    /// table for the height to belong to.
    /// </remarks>
    public bool MainFills { get; set; }

    public IReadOnlyList<HtmlCrumbView> Breadcrumb { get; set; } = Array.Empty<HtmlCrumbView>();

    /// <summary>What the list down the side is a list of. Shown above it.</summary>
    public string SideTitle { get; set; } = "";

    /// <summary>
    /// Which symbol the side list draws beside each entry, by the id of a symbol the head
    /// declares - `i-table`, `i-enum`, `i-const`.
    /// </summary>
    /// <remarks>
    /// The kind, drawn once per entry from one definition per page. Not an emoji: an emoji
    /// is a font's opinion, and the same character is a different picture on every
    /// platform the pages are read on.
    /// </remarks>
    public string SideIcon { get; set; } = "i-table";

    /// <summary>
    /// The page's siblings, so any page can reach any other of its kind.
    ///
    /// Carried by every page rather than looked up from one place, because a page is
    /// meant to stay useful on its own - somebody sends one file, and the navigation
    /// has to be in it.
    /// </summary>
    public IReadOnlyList<HtmlSideItemView> SideItems { get; set; } = Array.Empty<HtmlSideItemView>();

    /// <summary>
    /// Every enum this page mentions, once each, for the card that appears when a
    /// reader hovers an enum-valued cell.
    ///
    /// Once per page rather than once per cell: a cell names its enum and its label,
    /// and the text lives here. Putting each cell's tooltip in a `title` attribute
    /// would repeat a whole enum per cell, and a table can have 100,000 of them.
    /// </summary>
    public IReadOnlyList<HtmlEnumDefView> EnumDefs { get; set; } = Array.Empty<HtmlEnumDefView>();
}

internal sealed class HtmlCrumbView
{
    public required string Text { get; set; }

    /// <summary>Empty for the last crumb, which is the page the reader is on.</summary>
    public required string Href { get; set; }
}

internal sealed class HtmlSideItemView
{
    public required string Name { get; set; }
    public required string Href { get; set; }
    public required bool Current { get; set; }
}

/// <summary>One enum as the hover card shows it: every label, with values and comments.</summary>
internal sealed class HtmlEnumDefView
{
    public required string Name { get; set; }
    public required string Comment { get; set; }
    public required IReadOnlyList<HtmlEnumLabelView> Labels { get; set; }
}

// ------------------------------------------------------------------ the overview

internal sealed class HtmlIndexPageView : HtmlPageView
{
    public required HtmlStatsView Stats { get; set; }

    /// <summary>Columns by element type, largest first.</summary>
    public required IReadOnlyList<HtmlBarView> TypeDistribution { get; set; }

    /// <summary>Columns by what they are beyond their type - reference, array, optional.</summary>
    public required IReadOnlyList<HtmlBarView> RoleDistribution { get; set; }

    public required IReadOnlyList<HtmlBarView> SideDistribution { get; set; }

    /// <summary>The tables worth knowing the size of before opening one.</summary>
    public required IReadOnlyList<HtmlBarView> LargestTables { get; set; }

    public required IReadOnlyList<HtmlSourceSheetView> SourceSheets { get; set; }
}

/// <summary>
/// The tables, as a list.
/// </summary>
/// <remarks>
/// A page of its own rather than a column on the overview. The lists there grew with the
/// project until the counters were above a wall of names, and a list long enough to need
/// filtering and sorting is a list that wants the tools the other table pages have.
/// </remarks>
internal sealed class HtmlTableListPageView : HtmlPageView
{
    /// <summary>Whether any row has a description. The column is dropped when none has.</summary>
    public required bool HasComments { get; set; }

    public required string RecordTotal { get; set; }
    public required string ColumnTotal { get; set; }
    public required IReadOnlyList<HtmlTableListRowView> Rows { get; set; }
}

internal sealed class HtmlTableListRowView
{
    public required string Name { get; set; }
    public required string Href { get; set; }
    public required string RecordCount { get; set; }
    public required string ColumnCount { get; set; }

    /// <summary>Which sheet of which workbook, so one list answers where each table lives.</summary>
    public required string Sheet { get; set; }

    public required string Comment { get; set; }
}

internal sealed class HtmlEnumListPageView : HtmlPageView
{
    /// <summary>Whether any row has a description. The column is dropped when none has.</summary>
    public required bool HasComments { get; set; }

    public required string LabelTotal { get; set; }
    public required IReadOnlyList<HtmlEnumListRowView> Rows { get; set; }
}

internal sealed class HtmlEnumListRowView
{
    public required string Name { get; set; }
    public required string Href { get; set; }
    public required string LabelCount { get; set; }

    /// <summary>
    /// How many columns are typed as this enum.
    ///
    /// The column that earns this page: an enum nothing uses is either a leftover or a
    /// column typed wrong, and neither is visible from the enum's own page.
    /// </summary>
    public required string UserCount { get; set; }

    public required string Sheet { get; set; }
    public required string Comment { get; set; }
}

/// <summary>
/// The reference graph: which table points at which, drawn.
/// </summary>
/// <remarks>
/// The lists answer "what points at this table" one table at a time. The shape of the
/// whole thing - what is at the root, what everything hangs off, what is isolated - is a
/// different question and no list answers it.
///
/// Drawn by this repository rather than by a layout library, because the pages fetch
/// nothing: the coordinates are computed while generating and the page carries the result
/// as plain SVG. That also makes the picture the same every time, which a force-directed
/// layout would not be.
/// </remarks>
internal sealed class HtmlGraphPageView : HtmlPageView
{
    public required IReadOnlyList<HtmlGraphNodeView> Nodes { get; set; }
    public required IReadOnlyList<HtmlGraphEdgeView> Edges { get; set; }

    /// <summary>Canvas size, in the units the coordinates are in.</summary>
    public required int Width { get; set; }

    public required int Height { get; set; }

    /// <summary>Tables that neither point at anything nor are pointed at.</summary>
    public required IReadOnlyList<HtmlSummaryEntryView> Unconnected { get; set; }

    public required string EdgeCount { get; set; }

    /// <summary>
    /// Whether the drawing carries its per-edge decoration: an arrowhead on each curve and
    /// a tooltip naming the columns.
    /// </summary>
    /// <remarks>
    /// Off past a few dozen edges, and for one reason: both cost per edge at every frame.
    /// An arrowhead is a marker instance, and a tooltip means the edge takes the pointer -
    /// so a few hundred of them made the page hit-test hundreds of curves on every mouse
    /// move and repaint as many markers on every scroll. A model with 637 edges could not
    /// be scrolled.
    ///
    /// Nothing is lost that the drawing does not say another way: the layers run left to
    /// right, so direction is the layout, and hovering a table still picks out its own
    /// edges. What goes is decoration that only reads at a size where it is affordable.
    /// </remarks>
    public required bool Detailed { get; set; }

    /// <summary>
    /// The graph as data, for the page that explores it one table at a time rather than
    /// drawing all of it: `{ "Table": { "in": [["Other","Column"]], "out": [...] } }`.
    /// </summary>
    /// <remarks>
    /// Empty when the whole graph is drawn. Past a certain size drawing it whole is not a
    /// picture of anything - 323 tables and 637 edges is a bundle, and no layout engine
    /// makes that readable - so the page draws one table's neighbourhood instead, which is
    /// the question a reader actually has, and does it from this.
    /// </remarks>
    public required string Adjacency { get; set; }

    /// <summary>How many tables each one points at and is pointed at by, largest first.</summary>
    public required IReadOnlyList<HtmlDegreeRowView> Degrees { get; set; }

    /// <summary>
    /// How many tables sit in each layer, for the model whose whole graph is not drawn.
    /// </summary>
    /// <remarks>
    /// The shape of the thing, in the one form that stays readable at any size: a table is
    /// in layer 0 if nothing points at it, and in layer n+1 if the furthest thing pointing
    /// at it is in layer n. So layer 0 is what the data hangs off and the last layer is
    /// what nothing else needs.
    /// </remarks>
    public required IReadOnlyList<HtmlBarView> Layers { get; set; }
}

internal sealed class HtmlDegreeRowView
{
    public required string Name { get; set; }
    public required string Href { get; set; }
    public required string Out { get; set; }
    public required string In { get; set; }
}

internal sealed class HtmlGraphNodeView
{
    public required string Name { get; set; }
    public required string Href { get; set; }
    public required int X { get; set; }
    public required int Y { get; set; }
    public required int Width { get; set; }
    public required int Height { get; set; }

    /// <summary>What the node's tooltip says: how many columns in, how many out.</summary>
    public required string Title { get; set; }
}

internal sealed class HtmlGraphEdgeView
{
    /// <summary>The curve, as the `d` of a path.</summary>
    public required string Path { get; set; }

    /// <summary>
    /// The tables at each end, so hovering one node can pick out the edges that touch it.
    ///
    /// At a few hundred tables the middle of the drawing is a bundle nobody can follow;
    /// dimming everything but one table's own edges is what makes it readable, and it needs
    /// no second layout.
    /// </summary>
    public required string From { get; set; }

    public required string To { get; set; }

    /// <summary>Which columns this edge stands for.</summary>
    public required string Title { get; set; }

    /// <summary>A table pointing at itself, which is drawn as a loop rather than a curve.</summary>
    public required bool IsSelf { get; set; }
}

internal sealed class HtmlConstantSetListRowView
{
    public required string Name { get; set; }
    public required string Href { get; set; }
    public required string ConstantCount { get; set; }
    public required string Sheet { get; set; }
    public required string Comment { get; set; }
}

/// <summary>
/// The counters describing the whole conversion.
/// </summary>
/// <remarks>
/// Strings rather than numbers, formatted invariantly where they are counted: a page
/// generated on a Korean Windows and one generated on a Linux runner have to read the
/// same, and thousands separators are the kind of thing a machine's culture decides.
/// </remarks>
internal sealed class HtmlStatsView
{
    public required string Tables { get; set; }
    public required string Rows { get; set; }
    public required string Columns { get; set; }

    /// <summary>Columns of the workbook: a folded array has several of them per column.</summary>
    public required string SheetColumns { get; set; }

    public required string Cells { get; set; }
    public required string Enums { get; set; }
    public required string Labels { get; set; }
    public required string ConstantSets { get; set; }
    public required string Constants { get; set; }

    /// <summary>Reads as `3 workbooks`, because the sentence it sits in needs the noun.</summary>
    public required string Workbooks { get; set; }
}

/// <summary>One row of a distribution: a label, a count, and a bar width.</summary>
internal sealed class HtmlBarView
{
    public required string Name { get; set; }

    /// <summary>Formatted count, shown at the end of the row.</summary>
    public required string Count { get; set; }

    /// <summary>
    /// Bar width, as a percentage of the largest row rather than of the total.
    ///
    /// Of the total, a distribution with one dominant member renders every other bar as
    /// a line too short to compare - and comparing them is what the bars are for. The
    /// number at the end is the count, so nothing is lost by the bars being relative.
    /// </summary>
    public required int Percent { get; set; }

    /// <summary>Where the row's subject is, or empty when it is not a link.</summary>
    public string Href { get; set; } = "";
}

internal sealed class HtmlSummaryEntryView
{
    public required string Name { get; set; }

    /// <summary>Escaped comment, or empty.</summary>
    public required string Comment { get; set; }

    /// <summary>Size or shape, shown next to the name - `120 rows`, `8 labels`.</summary>
    public required string Detail { get; set; }

    /// <summary>
    /// Where the entry's own page and anchor are.
    ///
    /// Built by the generator rather than assembled in the template, because the
    /// template had no way to know where the generator writes an enum - and got it
    /// wrong. Every enum link pointed at `enums.html`, which this target has never
    /// written.
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

    /// <summary>How many tables came out of this workbook.</summary>
    public required string Detail { get; set; }
}

// ------------------------------------------------------------------ the pages

internal sealed class HtmlFieldsPageView : HtmlPageView
{
    public required string TableCount { get; set; }

    /// <summary>The workbook's own column count, beside the number of entries listed.</summary>
    public required string SheetColumnCount { get; set; }

    /// <summary>How many rows the page lists, formatted.</summary>
    public required string ColumnCount { get; set; }

    /// <summary>Whether any column has a description. The column is dropped when none has.</summary>
    public required bool HasComments { get; set; }
    public required IReadOnlyList<HtmlFieldRowView> Rows { get; set; }
}

internal sealed class HtmlFieldRowView
{
    public required string Name { get; set; }
    public required string Table { get; set; }
    public required string TableHref { get; set; }

    /// <summary>Rendered, because a type can be a link - to an enum, or to a table.</summary>
    public required string TypeCell { get; set; }

    public required string Side { get; set; }

    /// <summary>`required` or `optional`, which is a column's own property rather than a row's.</summary>
    public required string Presence { get; set; }
    public required string Comment { get; set; }
}

internal sealed class HtmlEnumPageView : HtmlPageView
{
    public required string Name { get; set; }

    /// <summary>A rendered anchor back to the source sheet, or empty when there is none.</summary>
    public required string SourceLink { get; set; }

    /// <summary>
    /// The sheet and cell the entity was declared in, or empty when the source does not
    /// address cells.
    ///
    /// Beside the name rather than instead of it. The pages promise to say where a value
    /// came from, and for a workbook on disk they were saying only which workbook - the
    /// sheet and the cell were in the model all along and never rendered.
    /// </summary>
    public required string SourceCell { get; set; }

    public required string Comment { get; set; }

    public required IReadOnlyList<HtmlEnumLabelView> Labels { get; set; }

    /// <summary>The columns typed as this enum, so the page answers "who uses this".</summary>
    public required IReadOnlyList<HtmlSummaryEntryView> UsedBy { get; set; }
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

    /// <summary>The same sets as a list, above them.</summary>
    public required IReadOnlyList<HtmlConstantSetListRowView> List { get; set; }

    /// <summary>Whether any set has a description. The column is dropped when none has.</summary>
    public required bool HasComments { get; set; }

    public required string ConstantTotal { get; set; }
}

internal sealed class HtmlConstantSetView
{
    public required string Name { get; set; }
    public required string SourceLink { get; set; }

    /// <summary>
    /// The sheet and cell the entity was declared in, or empty when the source does not
    /// address cells.
    ///
    /// Beside the name rather than instead of it. The pages promise to say where a value
    /// came from, and for a workbook on disk they were saying only which workbook - the
    /// sheet and the cell were in the model all along and never rendered.
    /// </summary>
    public required string SourceCell { get; set; }

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

internal sealed class HtmlTablePageView : HtmlPageView
{
    public required string Name { get; set; }
    public required string SourceLink { get; set; }

    /// <summary>
    /// The sheet and cell the entity was declared in, or empty when the source does not
    /// address cells.
    ///
    /// Beside the name rather than instead of it. The pages promise to say where a value
    /// came from, and for a workbook on disk they were saying only which workbook - the
    /// sheet and the cell were in the model all along and never rendered.
    /// </summary>
    public required string SourceCell { get; set; }

    public required string Comment { get; set; }

    /// <summary>Rows the table has, formatted.</summary>
    public required string RecordCount { get; set; }

    /// <summary>Rows this page shows, formatted. Fewer than the above when capped.</summary>
    public required string ShownCount { get; set; }

    public required string ColumnCount { get; set; }

    /// <summary>
    /// The sheet's own column count, or empty when it is the same as the above - which it is
    /// for every table that folds nothing.
    /// </summary>
    public required string SheetColumnCount { get; set; }

    /// <summary>Whether the page says it is showing part of the table.</summary>
    public required bool Truncated { get; set; }

    /// <summary>
    /// Whether the headings sort the table.
    ///
    /// Off past a row count where sorting in the page would take long enough to look
    /// like the page has stopped responding. A control that appears to do nothing is
    /// worse than one that is not there.
    /// </summary>
    public required bool Sortable { get; set; }

    /// <summary>The columns pointing at this table, from anywhere in the model.</summary>
    public required IReadOnlyList<HtmlSummaryEntryView> ReferencedBy { get; set; }

    /// <summary>Complete `&lt;th&gt;` elements for the column-name row, one per line.</summary>
    public required IReadOnlyList<string> NameCells { get; set; }

    /// <summary>Complete `&lt;th&gt;` elements for the description row, one per line.</summary>
    public required IReadOnlyList<string> CommentCells { get; set; }

    /// <summary>
    /// Whether any column has a description at all.
    ///
    /// The row is dropped when none has: a sheet that documents no column produced an
    /// empty band between the names and the types, which reads as a rendering fault
    /// rather than as an absence of comments.
    /// </summary>
    public required bool HasColumnComments { get; set; }


    /// <summary>Complete `&lt;th&gt;` elements for the type row, one per line.</summary>
    public required IReadOnlyList<string> TypeCells { get; set; }

    /// <summary>Complete `&lt;th&gt;` elements for the target-side row, one per line.</summary>
    public required IReadOnlyList<string> SideCells { get; set; }

    public required IReadOnlyList<HtmlRowView> Rows { get; set; }

    /// <summary>
    /// The rows this page's references point at, as JSON the page carries and the script
    /// reads: `{ "Table": { "cols": [...], "rows": { "key": [...] } } }`.
    /// </summary>
    /// <remarks>
    /// A few columns of each referenced row rather than the row entire, and only the keys
    /// this page actually names. The question a reader has at a reference cell is "what is
    /// 22111001", and the first columns of that row answer it - the whole row is what the
    /// link is for.
    ///
    /// In a hidden element rather than a script tag, because a value from a sheet may
    /// contain anything a sheet may contain, including something that reads like a url -
    /// and the check that no page fetches anything looks at script tags.
    /// </remarks>
    public required string RowPreviews { get; set; }
}

internal sealed class HtmlRowView
{
    /// <summary>
    /// Complete `&lt;td&gt;` elements, rendered here because a cell's markup depends on
    /// the field's type. They go on one line, with the row's tags around them: a page
    /// carries one of these per row, and a line per cell is a page nobody can scroll.
    /// </summary>
    public required IReadOnlyList<string> Cells { get; set; }
}
