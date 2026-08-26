using Tabbit.Recipe;
using Tabbit.Models;
using Tabbit.Targets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using System.Text;
using Tabbit.Helpers;
using Tabbit.Extensions;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Browsable documentation of the converted data.
///
/// Not consumed by any program: it exists so the data that reached a build can
/// be checked by eye, with links back to the cell each value came from.
/// </summary>
public class HtmlRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Whether generated files this run did not write are removed from
    /// <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// On, because the output is a file per table: delete a table from the sheets
    /// and its file stays behind naming types nothing declares any more. Only
    /// files carrying this tool's own header are removed, so a directory holding
    /// your own source is safe.
    ///
    /// Turn it off if you edit the generated files, which is a decision worth a
    /// line in a recipe.
    /// </remarks>
    public bool Sweep { get; set; } = true;

    /// <summary>
    /// Which side this output is built for: "c", "s", or "cs"/blank for
    /// both. Entities and fields marked for the other side are left out.
    ///
    /// Declare the same side on the exporter and on the code generator
    /// that reads its files: the two must agree on the column set or the
    /// generated reader will not match the data.
    /// </summary>
    public string TargetSide { get; set; } = "cs";

    /// <summary>
    /// How many rows of each table a page shows. Zero shows every row.
    /// </summary>
    /// <remarks>
    /// A cap exists because table size is the data's business and page size is the
    /// reader's. One committed sample has a 103,395-row table, which is not a strange
    /// table - and rendering it whole produced a 37 MB page, which is a page nobody
    /// opens. When the cap applies, the page says how many rows it is showing of how
    /// many; a truncation nobody is told about reads as the whole table.
    /// </remarks>
    public int MaxRowsPerTable { get; set; } = 1000;
}

/// <summary>
/// Emits human-readable documentation of the converted data.
///
/// An overview with the counters describing the whole conversion, a column index, one
/// page per table, one per enum, and a page for the constant sets. Every entity links
/// back to the cell it was declared in, which is what makes the pages useful when a
/// designer asks why a value came out the way it did.
///
/// The markup lives in templates/html-*.sbn. This file works out the cell contents,
/// which is where the type-dependent decisions are, and the links, which is where the
/// decisions about where a page lives are.
/// </summary>
// Not deterministic: the page carries the time it was generated, so the same model
// produces different bytes on every run. spec/build-cache.md §5.
[TabbitTarget("html", TargetKind.CodeGeneration, Order = 40, Deterministic = false)]
public partial class HtmlCodeGenerator : CodeGenerator<HtmlRecipe>
{
    /// <summary>
    /// Past this many rows on a page, the headings stop sorting it.
    ///
    /// Sorting happens in the page, on the reader's machine, and a sort of a hundred
    /// thousand rows there looks like a page that has stopped responding. A control that
    /// appears to do nothing is worse than one that is not offered.
    /// </summary>
    private const int SortableRowLimit = 5000;

    /// <summary>
    /// Past this many edges the reference graph drops its per-edge arrowheads and tooltips.
    /// </summary>
    /// <remarks>
    /// Both are paid per edge on every frame - a marker instance to paint and a curve to
    /// hit-test - and at 637 edges that was a page which could not be scrolled. The
    /// number is where the decoration stops being affordable rather than a measurement of
    /// any one machine.
    /// </remarks>
    private const int DetailedGraphEdges = 80;

    /// <summary>
    /// Every shape the sheets can hold, because this target documents columns rather than
    /// structures.
    /// </summary>
    /// <remarks>
    /// The four flags default to refusing, and each target opts in as it learns the shape.
    /// This one had opted into none of them, so a workbook using a record group, a record
    /// inside one, an optional column or an optional element produced no documentation at
    /// all - and those are the sheets whose data most wants looking at.
    ///
    /// What it takes here is nothing structural. The page shows the sheet's own columns,
    /// which is what a record group is at the sheet level: `Group.Member` is a column, and
    /// the group it folds into is named in the heading's tooltip. Absence is drawn rather
    /// than dropped - a column with no value in a row reads as a dash, and an empty string
    /// reads as a pair of quotes, so the distinction the marker exists to make survives.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <inheritdoc cref="SupportsNestedFields"/>
    protected override bool SupportsDeepNestedFields => true;

    /// <inheritdoc cref="SupportsNestedFields"/>
    protected override bool SupportsOptionalFields => true;

    /// <inheritdoc cref="SupportsNestedFields"/>
    protected override bool SupportsOptionalElements => true;

    /// <summary>Composite keys, which cost this target nothing.</summary>
    /// <remarks>
    /// **The flag exists for a lookup surface, and this target has none.** What the other
    /// four gate is a shape the page has to draw; what this one gates is `FindByKey(a, b)` -
    /// a method every language needs its own map for. A page offers no method: a composite
    /// key is columns, and columns are what it already draws.
    ///
    /// Refusing it meant a project with one such table got no documentation for any of its
    /// tables, over a surface this target was never going to emit.
    /// </remarks>
    protected override bool SupportsCompositeKeys => true;

    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private HtmlRecipe _htmlRecipe = null!;

    /// <summary>
    /// Which columns are typed as each enum, and which point at each table.
    ///
    /// Built once per generation because every enum page and every table page asks the
    /// question of the whole model, and asking it per page is the model walked once per
    /// entity. Keyed by name rather than by instance: a reference names a table that may
    /// not have resolved.
    /// </summary>
    private readonly Dictionary<string, List<Models.Field>> _enumUsers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Models.Field>> _tableReferrers = new(StringComparer.Ordinal);

    /// <summary>Broken keys per entry, by table and entry name. See <see cref="BrokenKeys"/>.</summary>
    private readonly Dictionary<(string Table, string Entry), Dictionary<string, int>> _brokenKeys = new();

    /// <summary>
    /// The keys each table's page will carry an anchor for.
    ///
    /// A reference cell links to the row it names, and the row has an anchor only if that
    /// page shows it - `MaxRowsPerTable` means it may not. Collected for every table
    /// before any page is written, because a table's references point at tables written
    /// later; bounded by the cap, so this holds at most that many keys per table.
    /// </summary>
    /// <summary>Every key each table holds, filled on demand by <see cref="KeysOf"/>.</summary>
    private readonly Dictionary<string, HashSet<string>> _allKeys =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    private readonly Dictionary<string, HashSet<string>> _anchoredRows = new(StringComparer.Ordinal);

    /// <summary>
    /// Which rows of which tables each table's page points at: source table, then target
    /// table, then the keys its shown rows name.
    ///
    /// Collected before any page is written so each referenced table is walked once for the
    /// whole run rather than once per page that points into it.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _referencedKeys =
        new(StringComparer.Ordinal);

    /// <summary>A few columns of each referenced row, by table and then by key.</summary>
    private readonly Dictionary<string, RowDigest> _digests = new(StringComparer.Ordinal);

    /// <summary>
    /// How many columns of a referenced row the preview shows, and how long a value in it
    /// may be. Both are there to keep the data a page carries proportional to the page: a
    /// preview is an answer to "what is this key", not a second copy of the other table.
    /// </summary>
    private const int PreviewColumns = 5;
    private const int PreviewValueLength = 48;

    private sealed class RowDigest
    {
        public required List<string> Columns { get; init; }
        public required Dictionary<string, string[]> Rows { get; init; }
    }

    protected override void Run(TargetContext context, HtmlRecipe htmlRecipe)
    {
        // A blank Path means the entry is switched off, as it does for every other
        // target. This one was missing it, and Path.Combine("", "index.html") is
        // "index.html" - so the skeleton recipe, whose entries are all blank and are
        // meant to be inert, quietly wrote three pages into the working directory.
        if (string.IsNullOrEmpty(htmlRecipe.Path))
            return;

        SweepStaleOutput(htmlRecipe.Path, htmlRecipe.Sweep);

        _htmlRecipe = htmlRecipe;

        // Already narrowed to the side this entry is built for. Both (the default)
        // leaves the model unchanged.
        _model = context.Model;

        GenerateHtml();
    }

    private void GenerateHtml()
    {
        IndexUsages();

        GenerateOverview();
        GenerateTableList();
        GenerateReferenceGraph();
        GenerateColumnIndex();
        GenerateEnumList();
        GenerateEnums();
        GenerateConstantSets();
        GenerateTables();
    }

    /// <summary>
    /// Collects, in one walk, what uses each enum and what points at each table.
    /// </summary>
    private void IndexUsages()
    {
        _enumUsers.Clear();
        _tableReferrers.Clear();
        _brokenKeys.Clear();
        _anchoredRows.Clear();
        _allKeys.Clear();
        _referencedKeys.Clear();
        _digests.Clear();

        foreach (var table in _model.Tables)
        {
            _anchoredRows[table.Name] = AnchoredKeysOf(table);
            _referencedKeys[table.Name] = ReferencedKeysOf(table);

            foreach (var field in table.Fields)
            {
                if (field.EnumOrNull is not null)
                    Add(_enumUsers, field.Enum.Name, field);

                if (!field.IsRef)
                    continue;

                // Every declared target, not just the resolved one: a multi-target
                // reference names several, and each of those tables is pointed at.
                foreach (var target in RefTargetsOf(field))
                    Add(_tableReferrers, target, field);
            }
        }

        BuildDigests();

        static void Add(Dictionary<string, List<Models.Field>> index, string key, Models.Field field)
        {
            if (!index.TryGetValue(key, out var list))
                index[key] = list = new List<Models.Field>();

            list.Add(field);
        }
    }

    /// <summary>
    /// Which rows of which other tables a table's page names, from the rows it shows.
    /// </summary>
    private Dictionary<string, HashSet<string>> ReferencedKeysOf(Models.Table table)
    {
        var byTarget = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var references = table.Fields.Where(field => NamedTablesOf(field).Count > 0).ToList();

        if (references.Count == 0)
            return byTarget;

        foreach (var row in ShownRows(table))
        {
            foreach (var field in references)
            {
                var value = row[field.Index].Value;

                if (value is null)
                    continue;

                string key = value.ToString() ?? "";

                foreach (var target in NamedTablesOf(field))
                {
                    if (_model.FindTable(target) is null)
                        continue;

                    if (!byTarget.TryGetValue(target, out var keys))
                        byTarget[target] = keys = new HashSet<string>(StringComparer.Ordinal);

                    keys.Add(key);
                }
            }
        }

        return byTarget;
    }

    /// <summary>
    /// The first few columns of every row anything points at, so a reference can be read
    /// without following it.
    /// </summary>
    /// <remarks>
    /// One walk per referenced table, keeping only the keys some page names. Rows past a
    /// table's own row cap are included: the cap is about how large a page may be, and this
    /// is a handful of values.
    /// </remarks>
    private void BuildDigests()
    {
        var needed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var byTarget in _referencedKeys.Values)
        {
            foreach (var pair in byTarget)
            {
                if (!needed.TryGetValue(pair.Key, out var keys))
                    needed[pair.Key] = keys = new HashSet<string>(StringComparer.Ordinal);

                keys.UnionWith(pair.Value);
            }
        }

        foreach (var pair in needed)
        {
            var table = _model.FindTable(pair.Key);

            if (table is null || table.Fields.Count == 0)
                continue;

            var columns = table.Fields.Take(PreviewColumns).ToList();
            var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var row in table.Data)
            {
                string key = row[table.PrimaryIndexField!.Index].Value?.ToString() ?? "";

                if (!pair.Value.Contains(key) || rows.ContainsKey(key))
                    continue;

                rows[key] = columns.Select(field => Clip(Plain(field, row[field.Index]))).ToArray();
            }

            _digests[pair.Key] = new RowDigest
            {
                Columns = columns.Select(field => field.Name).ToList(),
                Rows = rows,
            };
        }

        static string Clip(string text)
            => text.Length <= PreviewValueLength ? text : text.Substring(0, PreviewValueLength) + "\u2026";
    }

    /// <summary>
    /// A value as the preview shows it: text, culture-independent, arrays joined.
    /// </summary>
    private static string Plain(Models.Field field, Cell cell)
    {
        object? value = cell.HasValue ? cell.Value : null;

        if (value is null)
            return "null";

        if (field.IsArray && value is Array elements)
        {
            var parts = new List<string>();

            foreach (var element in elements)
                parts.Add(PlainValue(field.ElementType, element!) ?? "");

            return string.Join(", ", parts);
        }

        return PlainValue(field.ElementType, value) ?? "";
    }

    /// <summary>
    /// The primary keys of the rows a table's page renders, as text, in the spelling the
    /// anchors use.
    /// </summary>
    private HashSet<string> AnchoredKeysOf(Models.Table table)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var index = table.PrimaryIndexField;

        if (index is null)
            return keys;

        foreach (var row in ShownRows(table))
            keys.Add(row[index.Index].Value?.ToString() ?? "");

        return keys;
    }

    /// <summary>
    /// The rows a table's page shows: all of them, or the first <see cref="HtmlRecipe.MaxRowsPerTable"/>.
    /// </summary>
    private IReadOnlyList<List<Cell>> ShownRows(Models.Table table)
    {
        int cap = _htmlRecipe.MaxRowsPerTable;

        return cap > 0 && table.Data.Count > cap
            ? table.Data.Take(cap).ToList()
            : table.Data;
    }

    private static IEnumerable<string> RefTargetsOf(Models.Field field)
    {
        return field.RefTableName is null
            ? Array.Empty<string>()
            : new[] { field.RefTableName };
    }


    /// <summary>
    /// Every table a column's values may be a row of - resolved reference or sheet
    /// declaration alike.
    /// </summary>
    /// <remarks>
    /// The two are one fact to a reader. A reference is promoted where the generated code
    /// can carry an accessor, and a record member naming several tables is held back - so
    /// the columns with the most targets to explain were the ones the page said least
    /// about. Both kinds come through here.
    /// </remarks>
    private static List<string> NamedTablesOf(Models.Field field)
    {
        if (field.IsRef)
            return RefTargetsOf(field).Distinct(StringComparer.Ordinal).ToList();

        return field.Constraints.ReferencedTables is { Count: > 0 } declared
            ? declared.Distinct(StringComparer.Ordinal).ToList()
            : new List<string>();
    }

    // ------------------------------------------------------------- pages

    private void GenerateOverview()
    {
        var byWorkbook = _model.Tables
                               .GroupBy(table => (table.Location.Filename, table.Location.SheetUrl))
                               .OrderBy(group => group.Key.Filename, StringComparer.Ordinal)
                               .ToList();

        var view = new HtmlIndexPageView
        {
            Title = "데이터 정의",
            Stats = BuildStats(byWorkbook.Count),
            TypeDistribution = TypeDistribution(),
            RoleDistribution = RoleDistribution(),
            SideDistribution = SideDistribution(),
            LargestTables = LargestTables(),

            // Only the sheets tables were found in. Listing every sheet the conversion
            // touched would be better and there is no route to that from here.
            //
            // Grouped, because one workbook usually holds every table and the list was
            // otherwise the same filename repeated once per table.
            SourceSheets = byWorkbook
                           .Select(group => new HtmlSourceSheetView
                           {
                               Url = group.Key.SheetUrl,
                               Filename = group.Key.Filename,
                               Detail = Counted(group.Count(), "테이블"),
                           })
                           .ToList(),
        };

        Dress(view, kind: "index", root: "", crumbs: new[] { Crumb("개요", "") });

        Write("index.html", "html-index.sbn", view);
    }

    private void GenerateTableList()
    {
        var view = new HtmlTableListPageView
        {
            Title = "테이블",
            HasComments = _model.Tables.Any(table => !string.IsNullOrEmpty(table.Comment)),
            RecordTotal = Num(_model.Tables.Sum(table => (long)table.Data.Count)),
            ColumnTotal = Num(_model.Tables.Sum(table => (long)table.SerialFields.Count)),

            Rows = ByName(_model.Tables, table => table.Name)
                         .Select(table => new HtmlTableListRowView
                         {
                             Name = Esc(table.Name),
                             Href = HtmlLinks.Table(table.Name, root: ""),
                             RecordCount = Num(table.Data.Count),
                             ColumnCount = Num(table.SerialFields.Count),
                             Sheet = SheetName(table.Location),
                             Comment = Esc(table.Comment),
                         })
                         .ToList(),
        };

        Dress(view, kind: "tables", root: "",
              crumbs: new[] { Crumb("개요", "index.html"), Crumb("테이블", "") },
              fills: true);

        Write("tables.html", "html-tables.sbn", view);
    }

    /// <summary>
    /// The reference graph, laid out here and drawn as SVG by the template.
    /// </summary>
    /// <remarks>
    /// Layered left to right: a table sits one layer right of the furthest thing that
    /// points at it, so every edge runs forwards and the picture has a direction. The
    /// cooker rejects reference cycles, so that ordering always exists; a table pointing
    /// at itself is the one exception and is drawn as a loop.
    ///
    /// Within a layer the order is the average height of the nodes pointing into it,
    /// falling back to the name - one pass of the usual crossing-reduction heuristic,
    /// which is enough to read and, unlike a physical simulation, gives the same picture
    /// every run.
    /// </remarks>
    private void GenerateReferenceGraph()
    {
        const int rowHeight = 30;
        const int rowGap = 12;
        const int layerGap = 96;
        const int columnGap = 60;
        const int charWidth = 8;
        const int namePadding = 24;

        // A layer taller than this continues in a column beside itself, which keeps the
        // drawing roughly as wide as it is tall instead of a strip thousands of pixels
        // long. Safe by construction: no edge joins two tables in the same layer, so the
        // columns of one layer have nothing to draw between them.
        //
        // Only for a graph too large to draw plainly; a small one is one column per layer,
        // which reads better. Set once the edges are counted, below.
        int maxRowsPerColumn;

        // One edge per pair, carrying every column that makes it.
        var edges = new Dictionary<(string From, string To), List<string>>();

        foreach (var table in _model.Tables)
        {
            foreach (var field in table.Fields.Where(f => f.IsRef))
            {
                foreach (var target in RefTargetsOf(field))
                {
                    if (_model.FindTable(target) is null)
                        continue;

                    var key = (table.Name, target);

                    if (!edges.TryGetValue(key, out var columns))
                        edges[key] = columns = new List<string>();

                    columns.Add(field.Name);
                }
            }
        }

        var connected = edges.Keys
                             .SelectMany(pair => new[] { pair.From, pair.To })
                             .Distinct(StringComparer.Ordinal)
                             .ToHashSet(StringComparer.Ordinal);

        bool detailed = edges.Count <= DetailedGraphEdges;

        maxRowsPerColumn = detailed ? int.MaxValue : 30;

        // Longest path from something nothing points at. Self edges are skipped: a table
        // pointing at itself would otherwise have to sit right of itself.
        var incoming = connected.ToDictionary(
            name => name,
            name => edges.Keys.Where(pair => pair.To == name && pair.From != name)
                              .Select(pair => pair.From)
                              .ToList(),
            StringComparer.Ordinal);

        var layerOf = new Dictionary<string, int>(StringComparer.Ordinal);

        int LayerOf(string name)
        {
            if (layerOf.TryGetValue(name, out int known))
                return known;

            // Placed before recursing, so a cycle the cooker somehow let through stops
            // here rather than running out of stack.
            layerOf[name] = 0;

            int layer = incoming[name].Count == 0
                ? 0
                : incoming[name].Max(LayerOf) + 1;

            return layerOf[name] = layer;
        }

        foreach (var name in connected)
            LayerOf(name);

        int width(string name) => Math.Max(90, name.Length * charWidth + namePadding);

        var layers = layerOf.GroupBy(pair => pair.Value)
                            .OrderBy(group => group.Key)
                            .ToList();

        var placed = new Dictionary<string, HtmlGraphNodeView>(StringComparer.Ordinal);
        var nodes = new List<HtmlGraphNodeView>();

        int x = 8;

        foreach (var layer in layers)
        {
            var ordered = layer.Select(pair => pair.Key)
                               .OrderBy(name => Barycenter(name), Comparer<double>.Default)
                               .ThenBy(name => name, StringComparer.Ordinal)
                               .ToList();

            for (int taken = 0; taken < ordered.Count; taken += maxRowsPerColumn)
            {
                var column = ordered.Skip(taken).Take(maxRowsPerColumn).ToList();
                int columnWidth = column.Max(width);
                int y = 8;

                foreach (var name in column)
                {
                    var node = new HtmlGraphNodeView
                    {
                        Name = Esc(name),
                        Href = HtmlLinks.Table(name, root: ""),
                        X = x,
                        Y = y,
                        Width = columnWidth,
                        Height = rowHeight,
                        Title = Esc(NodeTitle(name)),
                    };

                    placed[name] = node;
                    nodes.Add(node);

                    y += rowHeight + rowGap;
                }

                bool lastOfLayer = taken + maxRowsPerColumn >= ordered.Count;

                x += columnWidth + (lastOfLayer ? layerGap : columnGap);
            }
        }

        var drawn = new List<HtmlGraphEdgeView>();

        foreach (var pair in edges.OrderBy(e => e.Key.From, StringComparer.Ordinal)
                                  .ThenBy(e => e.Key.To, StringComparer.Ordinal))
        {
            var from = placed[pair.Key.From];
            var to = placed[pair.Key.To];

            string title = $"{pair.Key.From} \u2192 {pair.Key.To} " +
                           $"({string.Join(", ", pair.Value.Distinct(StringComparer.Ordinal))})";

            drawn.Add(new HtmlGraphEdgeView
            {
                Path = pair.Key.From == pair.Key.To ? SelfLoop(from) : Curve(from, to),
                From = Esc(pair.Key.From),
                To = Esc(pair.Key.To),
                Title = Esc(title),
                IsSelf = pair.Key.From == pair.Key.To,
            });
        }

        var view = new HtmlGraphPageView
        {
            Title = "참조",
            // The whole drawing, when it is one somebody can read. Three hundred tables
            // fitted to a window is a grey smear with two-pixel labels: it looks like a
            // picture and answers nothing, so past a size the page offers the layers and
            // one table's neighbourhood instead.
            Nodes = detailed ? nodes : Array.Empty<HtmlGraphNodeView>(),
            Edges = detailed ? drawn : Array.Empty<HtmlGraphEdgeView>(),
            Width = nodes.Count == 0 ? 200 : nodes.Max(node => node.X + node.Width) + 40,
            Height = nodes.Count == 0 ? 80 : nodes.Max(node => node.Y + node.Height) + 20,
            Adjacency = Adjacency(edges),
            Degrees = Degrees(edges, connected),
            Layers = detailed ? Array.Empty<HtmlBarView>() : LayerSizes(layerOf),
            EdgeCount = Num(edges.Count),
            Detailed = detailed,

            Unconnected = _model.Tables
                                .Where(table => !connected.Contains(table.Name))
                                .OrderBy(table => table.Name, StringComparer.Ordinal)
                                .Select(table => Summarize(
                                    table.Name, "", Counted(table.Data.Count, "행"),
                                    HtmlLinks.Table(table.Name, root: "")))
                                .ToList(),
        };

        // Not a filling page: the drawing has the list of unconnected tables under it, and
        // a box that takes the rest of the window would push that list off the bottom.
        Dress(view, kind: "references", root: "",
              crumbs: new[] { Crumb("개요", "index.html"), Crumb("참조", "") });

        Write("references.html", "html-references.sbn", view);

        // --- the pieces above, in the order they read

        double Barycenter(string name)
            => incoming[name].Where(placed.ContainsKey)
                             .Select(source => (double)placed[source].Y)
                             .DefaultIfEmpty(double.MaxValue)
                             .Average();

        string NodeTitle(string name)
        {
            int outgoing = edges.Keys.Count(pair => pair.From == name);
            int inbound = edges.Keys.Count(pair => pair.To == name);

            return $"{name} — 가리킴 {outgoing}, 가리켜짐 {inbound}";
        }

        static string Curve(HtmlGraphNodeView from, HtmlGraphNodeView to)
        {
            int x1 = from.X + from.Width;
            int y1 = from.Y + from.Height / 2;
            int x2 = to.X;
            int y2 = to.Y + to.Height / 2;
            int bend = Math.Max(24, (x2 - x1) / 2);

            return $"M{x1},{y1} C{x1 + bend},{y1} {x2 - bend},{y2} {x2},{y2}";
        }

        static string SelfLoop(HtmlGraphNodeView node)
        {
            int x1 = node.X + node.Width;
            int y1 = node.Y + node.Height / 2;

            return $"M{x1},{y1 - 6} C{x1 + 34},{y1 - 26} {x1 + 34},{y1 + 26} {x1},{y1 + 6}";
        }
    }

    /// <summary>
    /// The graph as data: for each table, what points at it and what it points at, with the
    /// column that makes each edge.
    /// </summary>
    private string Adjacency(Dictionary<(string From, string To), List<string>> edges)
    {
        var byTable = new Dictionary<string, Dictionary<string, List<string[]>>>(StringComparer.Ordinal);

        Dictionary<string, List<string[]>> Slot(string name)
        {
            if (!byTable.TryGetValue(name, out var slot))
            {
                byTable[name] = slot = new Dictionary<string, List<string[]>>(StringComparer.Ordinal)
                {
                    ["in"] = new List<string[]>(),
                    ["out"] = new List<string[]>(),
                };
            }

            return slot;
        }

        foreach (var pair in edges.OrderBy(edge => edge.Key.From, StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(edge => edge.Key.To, StringComparer.OrdinalIgnoreCase))
        {
            string columns = string.Join(", ", pair.Value.Distinct(StringComparer.Ordinal));

            Slot(pair.Key.From)["out"].Add(new[] { pair.Key.To, columns });
            Slot(pair.Key.To)["in"].Add(new[] { pair.Key.From, columns });
        }

        return Esc(JsonConvert.SerializeObject(byTable));
    }

    /// <summary>
    /// How many tables each layer holds, largest first is not the order here - the layers
    /// are in their own order, because that is the shape being described.
    /// </summary>
    private static IReadOnlyList<HtmlBarView> LayerSizes(Dictionary<string, int> layerOf)
    {
        // Past a few layers the list stops describing a shape and starts being a list, so
        // the tail is one row. A chain 28 deep is worth knowing about as a depth, not as
        // 22 rows of one or two tables.
        const int shown = 6;

        var sizes = layerOf.GroupBy(pair => pair.Value)
                           .OrderBy(group => group.Key)
                           .Select(group => (Layer: group.Key, Count: group.Count()))
                           .ToList();

        int max = sizes.Count == 0 ? 1 : sizes.Max(size => size.Count);

        var bars = sizes.Where(size => size.Layer < shown)
                        .Select(size => new HtmlBarView
                        {
                            Name = size.Layer == 0 ? "0층 (가리켜지지 않음)" : $"{size.Layer}층",
                            Count = Num(size.Count),
                            Percent = Percent(size.Count, max),
                        })
                        .ToList();

        var deeper = sizes.Where(size => size.Layer >= shown).ToList();

        if (deeper.Count > 0)
        {
            int total = deeper.Sum(size => size.Count);

            bars.Add(new HtmlBarView
            {
                Name = $"{shown}층 이상 ({deeper.Max(size => size.Layer)}층까지)",
                Count = Num(total),
                Percent = Percent(total, max),
            });
        }

        return bars;
    }

    /// <summary>
    /// How connected each table is, which is the list that says where to start looking.
    /// </summary>
    private IReadOnlyList<HtmlDegreeRowView> Degrees(
        Dictionary<(string From, string To), List<string>> edges, HashSet<string> connected)
        => connected.Select(name => new
                    {
                        Name = name,
                        Out = edges.Keys.Count(pair => pair.From == name),
                        In = edges.Keys.Count(pair => pair.To == name),
                    })
                    .OrderByDescending(row => row.Out + row.In)
                    .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new HtmlDegreeRowView
                    {
                        Name = Esc(row.Name),
                        Href = HtmlLinks.Table(row.Name, root: ""),
                        Out = Num(row.Out),
                        In = Num(row.In),
                    })
                    .ToList();

    private void GenerateEnumList()
    {
        var view = new HtmlEnumListPageView
        {
            Title = "enum",
            HasComments = _model.Enums.Any(x => !string.IsNullOrEmpty(x.Comment)),
            LabelTotal = Num(_model.Enums.Sum(x => (long)x.Labels.Count)),

            Rows = ByName(_model.Enums, x => x.Name)
                         .Select(x => new HtmlEnumListRowView
                         {
                             Name = Esc(x.Name),
                             Href = HtmlLinks.Enum(x.Name, root: ""),
                             LabelCount = Num(x.Labels.Count),
                             UserCount = Num(Users(_enumUsers, x.Name, root: "").Count),
                             Sheet = SheetName(x.Location),
                             Comment = Esc(x.Comment),
                         })
                         .ToList(),
        };

        Dress(view, kind: "enums", root: "",
              crumbs: new[] { Crumb("개요", "index.html"), Crumb("enum", "") },
              fills: true);

        // The names on this page offer the card, so the page carries every enum.
        view.EnumDefs = EnumDefs(_model.Enums);

        Write("enums.html", "html-enums.sbn", view);
    }

    private void GenerateColumnIndex()
    {
        var rows = new List<HtmlFieldRowView>();

        foreach (var table in _model.Tables)
        {
            // One row per entry, which is what a column is on the page this links to. Per
            // sheet column, a folded array arrived as `accumulateExp[0]`, `[1]`, `[2]` -
            // rows that read as three columns of one table when they are three elements of
            // one column, and that no table page has a heading for.
            foreach (var entry in table.SerialFields)
            {
                var head = HeadOf(entry);

                if (head is null)
                    continue;

                string caption = EntryCaption(entry, head);
                int folded = FieldsOf(entry).Count();

                var notes = new List<string>();

                if (entry.Name != caption)
                    notes.Add($"생성 이름 {entry.Name}");

                if (folded > 1)
                    notes.Add($"시트 컬럼 {folded}개");

                rows.Add(new HtmlFieldRowView
                {
                    // The sheet's spelling, with the generated name in the tooltip: this
                    // index is read against the workbook and against the generated types,
                    // and the two names differ wherever a column is part of a record.
                    Name = notes.Count > 0
                        ? $"<span title=\"{Esc(string.Join(" · ", notes))}\">{Esc(caption)}</span>"
                        : Esc(caption),
                    Table = Esc(table.Name),
                    TableHref = HtmlLinks.Column(table.Name, head.Name, root: ""),
                    TypeCell = EntryTypeMarkup(entry, root: ""),
                    Side = SideName(head.TargetSide),
                    // The same tick and cross the data pages draw for a `bool`, because
                    // this is one: two spellings of a yes/no in one document make the reader
                    // learn the document rather than read it.
                    Presence = IsRequiredEntry(entry)
                        ? "<span class=\"yes\" title=\"필수\">&#x2714;</span>"
                        : "<span class=\"no\" title=\"옵셔널\">&#x2718;</span>",
                    Comment = Esc(CommentOf(entry)),
                });
            }
        }

        // By column name, which is the order that answers the question this page exists for:
        // the same name in adjacent rows is where a type or a side that disagrees between
        // tables becomes visible. The table name breaks ties so the order is defined.
        rows = ByName(rows, row => row.Name)
                   .ThenBy(row => row.Table, StringComparer.Ordinal)
                   .ToList();

        var view = new HtmlFieldsPageView
        {
            Title = "컬럼",
            TableCount = Num(_model.Tables.Count),
            SheetColumnCount = Num(_model.Tables.Sum(table => (long)table.Fields.Count)),
            ColumnCount = Num(rows.Count),
            HasComments = rows.Any(row => row.Comment.Length > 0),
            Rows = rows,
        };

        Dress(view, kind: "fields", root: "",
              crumbs: new[] { Crumb("개요", "index.html"), Crumb("컬럼", "") },
              fills: true);

        view.EnumDefs = EnumDefs(_model.Tables.SelectMany(t => t.Fields)
                                              .Select(f => f.EnumOrNull)
                                              .Where(e => e is not null)!);

        Write("fields.html", "html-fields.sbn", view);
    }

    private void GenerateEnums()
    {
        foreach (var enumm in _model.Enums)
            GenerateEnum(enumm);
    }

    private void GenerateEnum(Models.Enum enumm)
    {
        int no = 0;

        var view = new HtmlEnumPageView
        {
            Title = enumm.Name,
            Name = enumm.Name,
            SourceLink = SourceSheetLink(enumm.Location, enumm.Name),
            SourceCell = SourceCell(enumm.Location),
            Comment = Esc(enumm.Comment),
            Labels = enumm.Labels.Select(label => new HtmlEnumLabelView
            {
                No = ++no,
                Name = label.Name,
                SourceLink = SourceSheetLink(label.Location, label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = Esc(label.Comment),
            }).ToList(),

            UsedBy = Users(_enumUsers, enumm.Name, root: "../"),
        };

        Dress(view, kind: "enums", root: "../",
              crumbs: new[]
              {
                  Crumb("개요", "../index.html"),
                  Crumb("enum", "../enums.html"),
                  Crumb(enumm.Name, ""),
              },
              sideTitle: "enum",
              sideItems: EnumSideItems("../", enumm.Name),
              sideIcon: "i-enum",
              fills: true);

        Write(HtmlLinks.EnumPage(enumm.Name), "html-enum.sbn", view);
    }

    private void GenerateConstantSets()
    {
        var view = new HtmlConstantSetsPageView
        {
            Title = "상수 세트",
            Sets = ByName(_model.ConstantSets, x => x.Name).Select(BuildConstantSet).ToList(),
            HasComments = _model.ConstantSets.Any(x => !string.IsNullOrEmpty(x.Comment)),
            ConstantTotal = Num(_model.ConstantSets.Sum(x => (long)x.Constants.Count)),

            List = ByName(_model.ConstantSets, x => x.Name)
                         .Select(x => new HtmlConstantSetListRowView
                         {
                             Name = Esc(x.Name),
                             Href = HtmlLinks.ConstantSet(x.Name, root: ""),
                             ConstantCount = Num(x.Constants.Count),
                             Sheet = SheetName(x.Location),
                             Comment = Esc(x.Comment),
                         })
                         .ToList(),
        };

        Dress(view, kind: "constantsets", root: "",
              crumbs: new[] { Crumb("개요", "index.html"), Crumb("상수 세트", "") },
              sideTitle: "상수 세트",
              // Anchors on this page rather than pages of their own: the sets are on one
              // page because a set holds a few dozen constants, not a few thousand rows.
              sideItems: ByName(_model.ConstantSets, x => x.Name)
                               .Select(x => new HtmlSideItemView
                               {
                                   Name = x.Name,
                                   Href = "#constantset_" + x.Name,
                                   Current = false,
                               })
                               .ToList(),
              sideIcon: "i-const");

        view.EnumDefs = EnumDefs(_model.ConstantSets
                                       .SelectMany(set => set.Constants)
                                       .Where(constant => constant.Type == Models.ValueType.Enum)
                                       .Select(constant => constant.Enum));

        Write("constantsets.html", "html-constantsets.sbn", view);
    }

    private HtmlConstantSetView BuildConstantSet(ConstantSet constantSet)
    {
        int no = 0;

        return new HtmlConstantSetView
        {
            Name = constantSet.Name,
            SourceLink = SourceSheetLink(constantSet.Location, constantSet.Name),
            SourceCell = SourceCell(constantSet.Location),
            Comment = Esc(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => BuildConstant(constantSet, constant, ++no)).ToList(),
        };
    }

    private HtmlConstantView BuildConstant(ConstantSet constantSet, ConstantSet.Constant constant, int no)
    {
        string typeCell;
        string valueCell;

        if (constant.Type == Models.ValueType.Enum)
        {
            // An enum constant shows where both its type and its label were declared,
            // because either is a place someone might want to go from here.
            var label = constant.Enum.GetLabel(constant.Value!, constant.Location);

            typeCell = SourceSheetLink(constant.Enum.Location, constant.Enum.Name);
            valueCell = $"{EnumValueLink(constant.Enum, label, root: "")} " +
                        $"<span class=\"hint\">({label.Value})</span>";
        }
        else
        {
            typeCell = $"<span class=\"type\">{Esc(constant.TypeName)}</span>";

            // Through the invariant renderer, not `object.ToString()`. That takes the
            // machine's culture, so a `datetime` constant came out of a Korean Windows as
            // `2022-03-01 오전 9:00:00` and out of a Linux runner as `03/01/2022 09:00:00`
            // - the same sheet, two different pages. Parsing has always been invariant here
            // and writing had not caught up.
            valueCell = Esc(PlainValue(constant.Type, constant.Value!));
        }

        return new HtmlConstantView
        {
            No = no,
            Name = constant.Name,
            NameCell = SourceSheetLink(constant.Location, constant.Name),
            Comment = Esc(constant.Comment),
            TypeCell = typeCell,
            ValueCell = valueCell,
        };
    }

    private void GenerateTables()
    {
        foreach (var table in _model.Tables)
            GenerateTable(table);
    }

    private void GenerateTable(Models.Table table)
    {
        var shown = ShownRows(table);

        // The page's columns are the table's entries, not the sheet's columns. A record
        // array is written across a column per member per element - `statEffect[0]["Id"]`,
        // `statEffect[0]["Value"]`, `statEffect[1]["Id"]` - and drawn that way it is a wall
        // of numbers with the structure taken out of it. One column per entry puts the
        // structure back: a record array is one column whose cells hold objects.
        var columns = table.SerialFields
                           .OrderBy(entry => entry.AnyField?.Index ?? int.MaxValue)
                           .ToList();

        bool hasComments = columns.Any(entry => !string.IsNullOrEmpty(CommentOf(entry)));

        var view = new HtmlTablePageView
        {
            Title = table.Name,
            Name = table.Name,
            SourceLink = SourceSheetLink(table.Location, table.Name),
            SourceCell = SourceCell(table.Location),
            Comment = Esc(table.Comment),
            RecordCount = Num(table.Data.Count),
            ShownCount = Num(shown.Count),
            // The sheet's columns, not the entries they fold into: how many columns a table
            // has is a fact about the table, and the column index lists the same set.
            ColumnCount = Num(table.SerialFields.Count),
            SheetColumnCount = table.SerialFields.Count == table.Fields.Count
                ? ""
                : Num(table.Fields.Count),
            Truncated = shown.Count < table.Data.Count,
            Sortable = shown.Count <= SortableRowLimit,
            ReferencedBy = Users(_tableReferrers, table.Name, root: "../"),

            NameCells = columns.Select((entry, index) => NameCell(table, entry, index)).ToList(),
            CommentCells = columns.Select(entry => $"<th>{Esc(CommentOf(entry))}</th>").ToList(),
            HasColumnComments = hasComments,
            TypeCells = columns.Select(entry => $"<th>{EntryTypeMarkup(entry, root: "../")}</th>").ToList(),

            SideCells = columns.Select(entry => $"<th>{SideName(entry.TargetSide)}</th>").ToList(),

            RowPreviews = PreviewsFor(table.Name),

            Rows = shown.Select(row => new HtmlRowView
            {
                Cells = columns
                        .Select((entry, index) => EntryCell(table, entry, row, index == 0, root: "../"))
                        .ToList(),
            }).ToList(),
        };

        Dress(view, kind: "tables", root: "../",
              crumbs: new[]
              {
                  Crumb("개요", "../index.html"),
                  Crumb("테이블", "../tables.html"),
                  Crumb(table.Name, ""),
              },
              sideTitle: "테이블",
              sideItems: TableSideItems("../", table.Name),
              sideIcon: "i-table",
              fills: true);

        view.EnumDefs = EnumDefs(table.Fields.Select(f => f.EnumOrNull).Where(e => e is not null)!);

        Write(HtmlLinks.TablePage(table.Name), "html-table.sbn", view);
    }

    /// <summary>
    /// The preview data one page needs, as JSON, or empty when it references nothing.
    /// </summary>
    private string PreviewsFor(string table)
    {
        if (!_referencedKeys.TryGetValue(table, out var byTarget) || byTarget.Count == 0)
            return "";

        var payload = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var pair in byTarget.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!_digests.TryGetValue(pair.Key, out var digest))
                continue;

            var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var key in pair.Value)
            {
                if (digest.Rows.TryGetValue(key, out var values))
                    rows[key] = values;
            }

            if (rows.Count == 0)
                continue;

            payload[pair.Key] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["cols"] = digest.Columns,
                ["rows"] = rows,
            };
        }

        // Escaped on the way out: this lands in an element's text, and a sheet value may
        // hold an angle bracket like any other character.
        return payload.Count == 0 ? "" : Esc(JsonConvert.SerializeObject(payload));
    }

    // ------------------------------------------------------------- the shell

    /// <summary>
    /// Puts the parts every page shares onto one - where it sits, what the top bar
    /// highlights, and what the list down the side holds.
    /// </summary>
    private static void Dress(
        HtmlPageView view,
        string kind,
        string root,
        IReadOnlyList<HtmlCrumbView> crumbs,
        string sideTitle = "",
        IReadOnlyList<HtmlSideItemView>? sideItems = null,
        string sideIcon = "i-table",
        bool fills = false)
    {
        view.MainFills = fills;
        view.Kind = kind;
        view.Root = root;
        view.Breadcrumb = crumbs;
        view.SideTitle = sideTitle;
        view.SideItems = sideItems ?? Array.Empty<HtmlSideItemView>();
        view.SideIcon = sideIcon;
    }

    private static HtmlCrumbView Crumb(string text, string href)
        => new HtmlCrumbView { Text = Esc(text), Href = href };

    private List<HtmlSideItemView> TableSideItems(string root, string current)
        => ByName(_model.Tables, x => x.Name)
                 .Select(x => new HtmlSideItemView
                 {
                     Name = Esc(x.Name),
                     Href = HtmlLinks.Table(x.Name, root),
                     Current = x.Name == current,
                 })
                 .ToList();

    private List<HtmlSideItemView> EnumSideItems(string root, string current)
        => ByName(_model.Enums, x => x.Name)
                 .Select(x => new HtmlSideItemView
                 {
                     Name = Esc(x.Name),
                     Href = HtmlLinks.Enum(x.Name, root),
                     Current = x.Name == current,
                 })
                 .ToList();

    /// <summary>
    /// The enums a page mentions, once each, in a stable order.
    /// </summary>
    private static IReadOnlyList<HtmlEnumDefView> EnumDefs(IEnumerable<Models.Enum?> enums)
        => enums.Where(e => e is not null)
                .Select(e => e!)
                .GroupBy(e => e.Name, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new HtmlEnumDefView
                {
                    Name = group.Key,
                    Comment = Esc(group.First().Comment),
                    Labels = group.First().Labels.Select(label => new HtmlEnumLabelView
                    {
                        No = 0,
                        Name = Esc(label.Name),
                        SourceLink = "",
                        Value = label.Value.ToString(CultureInfo.InvariantCulture),
                        Comment = Esc(label.Comment),
                    }).ToList(),
                })
                .ToList();

    /// <summary>
    /// The columns using something, as links to where each one is declared.
    /// </summary>
    private static IReadOnlyList<HtmlSummaryEntryView> Users(
        Dictionary<string, List<Models.Field>> index, string key, string root)
    {
        if (!index.TryGetValue(key, out var fields))
            return Array.Empty<HtmlSummaryEntryView>();

        // By entry rather than by sheet column. A folded array of references is one column
        // on the page and N in the workbook, so listing the columns answered "which column
        // points here" with names - `AdvSpcEffTerms0`, `AdvSpcEffTerms1` - that the table's
        // own page does not have. The first column of an entry is the one its heading is
        // anchored on, which is where the link has to land.
        return fields.GroupBy(field => (
                         field.OwnerTable.Name,
                         Entry: string.IsNullOrEmpty(field.GroupName) ? field.Name : field.GroupName!))
                     .Select(group => new HtmlSummaryEntryView
                     {
                         Name = Esc($"{group.Key.Name}.{group.Key.Entry}"),
                         Comment = "",
                         Detail = group.Count() > 1 ? $"컬럼 {group.Count()}개" : "",
                         Href = HtmlLinks.Column(group.Key.Name, group.First().Name, root),
                     })
                     .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                     .ToList();
    }

    // ------------------------------------------------------------- statistics

    private HtmlStatsView BuildStats(int workbooks)
    {
        long rows = _model.Tables.Sum(table => (long)table.Data.Count);

        // Columns as the pages draw them: one per entry. A folded array is one column of a
        // table and several of a sheet, so a counter that reports the sheet's number
        // disagrees with every page under it. The sheet's number is worth having as well -
        // it is the size of the workbook - so it is named as what it is rather than dropped.
        long columns = _model.Tables.Sum(table => (long)table.SerialFields.Count);
        long sheetColumns = _model.Tables.Sum(table => (long)table.Fields.Count);
        long cells = _model.Tables.Sum(table => (long)table.Data.Count * table.Fields.Count);

        return new HtmlStatsView
        {
            Tables = Num(_model.Tables.Count),
            Rows = Num(rows),
            Columns = Num(columns),
            SheetColumns = Num(sheetColumns),
            Cells = Num(cells),
            Enums = Num(_model.Enums.Count),
            Labels = Num(_model.Enums.Sum(x => (long)x.Labels.Count)),
            ConstantSets = Num(_model.ConstantSets.Count),
            Constants = Num(_model.ConstantSets.Sum(x => (long)x.Constants.Count)),
            Workbooks = Counted(workbooks, "워크북"),
        };
    }

    private IEnumerable<Models.Field> AllFields => _model.Tables.SelectMany(table => table.Fields);

    private IReadOnlyList<HtmlBarView> TypeDistribution()
        => Bars(AllFields.GroupBy(field => field.ElementType)
                         .Select(group => (Name: group.Key.ToString().ToLowerInvariant(), Count: group.Count())));

    /// <summary>
    /// What columns are beyond their type. A column can be several of these at once, so
    /// the rows do not sum to the column count - which is why `plain` is one of them:
    /// it is the answer to "how much of this model is none of the above".
    /// </summary>
    private IReadOnlyList<HtmlBarView> RoleDistribution()
    {
        var fields = AllFields.ToList();

        return Bars(new[]
        {
            (Name: "참조", Count: fields.Count(f => f.IsRef)),
            (Name: "배열", Count: fields.Count(f => f.IsArray)),
            (Name: "옵셔널", Count: fields.Count(f => !f.IsRequired)),
            (Name: "레코드 멤버", Count: fields.Count(f => f.IsRecordMember)),
            (Name: "번역 문자열", Count: fields.Count(f => f.Role == StringRole.Text)),
            (Name: "애셋 경로", Count: fields.Count(f => f.Role == StringRole.Asset)),
            (Name: "그 밖", Count: fields.Count(f => !f.IsRef && !f.IsArray && f.IsRequired
                                                     && !f.IsRecordMember && f.Role == StringRole.None)),
        });
    }

    private IReadOnlyList<HtmlBarView> SideDistribution()
        => Bars(AllFields.GroupBy(field => field.TargetSide)
                         .Select(group => (Name: SideCaption(group.Key), Count: group.Count())));

    private IReadOnlyList<HtmlBarView> LargestTables()
    {
        var largest = _model.Tables.OrderByDescending(table => table.Data.Count)
                                   .ThenBy(table => table.Name, StringComparer.Ordinal)
                                   .Take(10)
                                   .ToList();

        int max = largest.Count > 0 ? Math.Max(1, largest[0].Data.Count) : 1;

        return largest.Where(table => table.Data.Count > 0)
                      .Select(table => new HtmlBarView
                      {
                          Name = Esc(table.Name),
                          Count = Num(table.Data.Count),
                          Percent = Percent(table.Data.Count, max),
                          Href = HtmlLinks.Table(table.Name, root: ""),
                      })
                      .ToList();
    }

    /// <summary>
    /// A distribution as rows, largest first, with the bars relative to the largest.
    /// </summary>
    private static IReadOnlyList<HtmlBarView> Bars(IEnumerable<(string Name, int Count)> items)
    {
        var rows = items.Where(item => item.Count > 0)
                        .OrderByDescending(item => item.Count)
                        .ThenBy(item => item.Name, StringComparer.Ordinal)
                        .ToList();

        int max = rows.Count > 0 ? Math.Max(1, rows[0].Count) : 1;

        return rows.Select(item => new HtmlBarView
                   {
                       Name = Esc(item.Name),
                       Count = Num(item.Count),
                       Percent = Percent(item.Count, max),
                   })
                   .ToList();
    }

    /// <summary>
    /// A bar width. At least two, because a row whose bar is invisible reads as a row
    /// whose count is zero, and a zero row is not rendered at all.
    /// </summary>
    private static int Percent(long count, long max)
        => Math.Max(2, (int)Math.Round(count * 100.0 / max));

    private static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Orders anything named by its name, the way a reader reads a list.
    /// </summary>
    /// <remarks>
    /// Case-insensitive first, because an ordinal sort puts every capital ahead of every
    /// lower-case letter - so `Zone` would come before `armor` and a reader looking for one
    /// name would not find it where they looked. Ordinal breaks the ties, so the order is
    /// still defined for two names that differ only in case.
    /// </remarks>
    private static IOrderedEnumerable<T> ByName<T>(IEnumerable<T> items, Func<T, string> name)
        => items.OrderBy(name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name, StringComparer.Ordinal);

    /// <summary>
    /// A count with what is being counted, as the pages say it: `행 120개`.
    /// </summary>
    private static string Counted(int count, string noun) => $"{noun} {Num(count)}개";

    // ------------------------------------------------------------- cells

    /// <summary>
    /// Every field one entry of a table is built from, in column order.
    /// </summary>
    private static IEnumerable<Models.Field> FieldsOf(Models.SerialField entry)
        => entry.IsRecord
            ? entry.Leaves.SelectMany(leaf => leaf.Fields)
            : entry.Fields;

    /// <summary>The first field of an entry, which is where its shared facts are read from.</summary>
    private static Models.Field? HeadOf(Models.SerialField entry) => FieldsOf(entry).FirstOrDefault();

    /// <summary>The description of an entry: whatever its first column said.</summary>
    private static string CommentOf(Models.SerialField entry) => HeadOf(entry)?.Comment ?? "";

    private static bool IsRequiredEntry(Models.SerialField entry) => HeadOf(entry)?.IsRequired ?? true;

    /// <summary>
    /// The type of one entry: a value, an array of them, or a record with its members named.
    /// </summary>
    private string EntryTypeMarkup(Models.SerialField entry, string root)
    {
        if (!entry.IsRecord)
        {
            var field = HeadOf(entry);

            if (field is null)
                return "";

            // A folded array is several columns holding one value each, and the field's own
            // type says nothing about that - the entry is what knows it is an array.
            string brackets = entry.IsArray && !field.IsArray ? "[]" : "";

            return TypeMarkup(field, root) + brackets + BrokenMark(entry);
        }

        return MemberTypes(entry.Members, root) + (entry.IsArray ? "[]" : "") + BrokenMark(entry);
    }

    /// <summary>
    /// The mark a reference column carries when some of its keys name no row.
    /// </summary>
    /// <remarks>
    /// Per value the page already says it - the key wears a `?` and a tooltip naming the
    /// tables that do not have it - but the first thing a reader checking data wants is the
    /// column, not the row: "does this column have broken references, and how many". Finding
    /// that by scrolling is not finding it.
    ///
    /// A different mark from the `?` on the type on purpose. That `?` is the schema allowing
    /// an empty cell; this is the data being wrong, and one glyph for both would make the
    /// page unreadable on exactly the question it exists to answer.
    /// </remarks>
    private string BrokenMark(Models.SerialField entry)
    {
        var broken = BrokenKeys(entry);
        int total = broken.Values.Sum();

        if (total == 0)
            return "";

        // Per target, because a record's members point at different tables and one number
        // covering all of them says which column to look at and nothing more. The head field
        // is no help here either - the first member of a record is often not a reference.
        string detail = string.Join(", ",
            broken.OrderByDescending(pair => pair.Value)
                  .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                  .Select(pair => $"{pair.Key}에 없는 키 {Num(pair.Value)}개"));

        return $" <span class=\"broken\" title=\"{Esc(detail)}\">" +
               $"&#x26A0; {Num(total)}</span>";
    }

    /// <summary>
    /// Keys of one entry that no named table holds, over every row of the table.
    /// </summary>
    /// <remarks>
    /// Every row rather than the rows the page shows: a column's broken references are a
    /// fact about the data, and a count that stopped at the row cap would say a table is
    /// clean because the page is short. Counted once per entry - the table page and the
    /// column index both ask.
    /// </remarks>
    private Dictionary<string, int> BrokenKeys(Models.SerialField entry)
    {
        var head = HeadOf(entry);
        var table = head?.OwnerTable;

        if (table is null)
            return Empty;

        var at = (table.Name, entry.Name);

        if (_brokenKeys.TryGetValue(at, out var cached))
            return cached;

        var fields = FieldsOf(entry)
                     .Select(field => (Field: field, Named: TargetsInModel(field)))
                     .Where(pair => pair.Named.Count > 0)
                     .ToList();

        var broken = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in table.Data)
        {
            foreach (var (field, named) in fields)
            {
                var cell = row[field.Index];

                if (cell.Value is Array elements)
                {
                    for (int i = 0; i < elements.Length; i++)
                    {
                        bool present = cell.ElementHasValue is null
                                       || i >= cell.ElementHasValue.Length
                                       || cell.ElementHasValue[i];

                        if (present)
                            Count(field, named, elements.GetValue(i), broken);
                    }

                    continue;
                }

                if (cell.HasValue)
                    Count(field, named, cell.Value, broken);
            }
        }

        _brokenKeys[at] = broken;

        return broken;

        // Counted against the set of tables the column names rather than against each of
        // them. A column naming two tables has one fact to report - "in neither" - and
        // reporting it once per table would say 25 twice for the same 25 keys.
        void Count(Models.Field field, List<string> named, object? value, Dictionary<string, int> into)
        {
            if (value is null)
                return;

            string key = value.ToString() ?? "";

            // The two keys that are not references at all: nothing written, and the `0` a
            // sheet leaves in a numeric column it means to leave empty.
            if (key.Length == 0)
                return;

            if (field.ElementType != Models.ValueType.String && key == "0")
                return;

            if (named.Any(name => KeysOf(name).Contains(key)))
                return;

            string where = string.Join(" / ", named);

            into[where] = into.TryGetValue(where, out int had) ? had + 1 : 1;
        }
    }

    /// <summary>The tables a column names that this build actually holds.</summary>
    private List<string> TargetsInModel(Models.Field field)
        => NamedTablesOf(field).Where(name => _model.FindTable(name) is not null).ToList();

    private static readonly Dictionary<string, int> Empty = new(StringComparer.Ordinal);

    /// <summary>
    /// The `?` a column carries when a row may have no value for it.
    /// </summary>
    /// <remarks>
    /// On the type rather than on a row of its own. A row of `required`/`optional` says one
    /// thing per column, and a record is several columns in one - so it had to answer for
    /// all of them at once and answered for none. The sheet writes `?` on the type; so does
    /// the page.
    /// </remarks>
    private static string Optional(Models.Field field) => field.IsRequired ? "" : "?";

    /// <summary>
    /// The mark an index column's type carries.
    /// </summary>
    /// <remarks>
    /// The first column of a table is the row's key, and the type row said nothing about
    /// that - so the one column a reader navigates by looked like any other number. Marked
    /// on the type rather than in the heading, because being the key is a property of the
    /// column and not of its name.
    /// </remarks>
    private static string KeyMark(Models.Field field)
        => field.Indexing ? " <span class=\"keymark\" title=\"기본 인덱스\">🔑</span>" : "";

    /// <summary>
    /// The members of a record as a type: `{Type: double, Id: int?, Value: double}`.
    /// </summary>
    /// <remarks>
    /// With each member's own type, its own `?` and its own targets. The heading used to
    /// name the members and nothing else, which left the one thing a reader asks of a
    /// record - what is in it - to be guessed from the values.
    /// </remarks>
    private string MemberTypes(List<Models.RecordMember> members, string root)
    {
        var parts = members.Select(member =>
        {
            string name = $"<span class=\"mem\">{Esc(member.Name)}</span><span class=\"sep\">: </span>";

            if (!member.IsLeaf)
                return name + MemberTypes(member.Members, root);

            var field = member.Fields.FirstOrDefault();

            return field is null ? name : name + TypeMarkup(field, root);
        });

        return $"<span class=\"sep\">{{</span>{string.Join("<span class=\"sep\">, </span>", parts)}" +
               "<span class=\"sep\">}</span>";
    }

    /// <summary>
    /// One cell of the table: a value, an array, or the objects a record array holds.
    /// </summary>
    private string EntryCell(
        Models.Table table, Models.SerialField entry, List<Cell> row, bool isIndex, string root)
    {
        if (!entry.IsRecord)
        {
            var field = HeadOf(entry);

            if (field is null)
                return "<td></td>";

            // One column holding an array, or several columns holding one element each.
            if (entry.Fields.Count > 1)
                return FoldedArrayCell(table, entry, row, root);

            return DataCell(table, field, row[field.Index], isIndex, root);
        }

        return RecordCell(table, entry, row, root);
    }

    /// <summary>
    /// An array the sheet spread over a column per element - `Slot1`, `Slot2` - as one cell.
    /// </summary>
    private string FoldedArrayCell(
        Models.Table table, Models.SerialField entry, List<Cell> row, string root)
    {
        int count = table.ElementCountIn(entry, row);
        var parts = new List<string>();

        for (int i = 0; i < count && i < entry.Fields.Count; i++)
        {
            var field = entry.Fields[i];
            var cell = row[field.Index];

            parts.Add(cell.HasValue ? ScalarValueMarkup(field, cell.Value, root) : Absent());
        }

        string content = parts.Count == 0
            ? "<span class=\"empty\" title=\"원소 없음\">[]</span>"
            : $"<span class=\"sep\">[</span>{string.Join("<span class=\"sep\">, </span>", parts)}" +
              "<span class=\"sep\">]</span>";

        string badge = parts.Count > 1 ? $"<span class=\"n-of\">&times;{parts.Count}</span>" : "";

        return content.Length > 220
            ? $"<td class=\"text\"><span class=\"clip\">{content}</span>{badge}</td>"
            : $"<td>{content}{badge}</td>";
    }

    /// <summary>
    /// A record entry as the objects it holds: `[{Type: 0, Id: 1077, Value: 421}, ...]`.
    /// </summary>
    /// <remarks>
    /// Written as an object because that is what it is. Spread over a column per member per
    /// element, the same data is a row of numbers whose headings are the only thing saying
    /// which number belongs with which - and a reader checking a value has to count columns
    /// to find out.
    ///
    /// Long ones are clipped like any long value, and each element is its own element in the
    /// markup so that the expanded form puts one object per line.
    /// </remarks>
    private string RecordCell(
        Models.Table table, Models.SerialField entry, List<Cell> row, string root)
    {
        int count = table.ElementCountIn(entry, row);
        var elements = new List<string>();

        for (int i = 0; i < count; i++)
        {
            string body = RecordBody(entry.Members, row, i, root);

            elements.Add($"<span class=\"obj\"><span class=\"sep\">(</span>{body}" +
                         "<span class=\"sep\">)</span></span>");
        }

        if (elements.Count == 0)
            return $"<td><span class=\"empty\" title=\"원소 없음\">[]</span></td>";

        string content = entry.IsArray
            ? $"<span class=\"sep\">[</span>{string.Join("<span class=\"sep\">, </span>", elements)}" +
              "<span class=\"sep\">]</span>"
            : elements[0];

        string badge = entry.IsArray && elements.Count > 1
            ? $"<span class=\"n-of\">&times;{elements.Count}</span>"
            : "";

        // A record is wider than a value by definition, so it is clipped sooner than one.
        return content.Length > 200
            ? $"<td class=\"text record\"><span class=\"clip\">{content}</span>{badge}</td>"
            : $"<td class=\"record\">{content}{badge}</td>";
    }

    /// <summary>
    /// The members of one element, `name: value` each, nested as deep as the sheet wrote.
    /// </summary>
    private string RecordBody(List<Models.RecordMember> members, List<Cell> row, int element, string root)
    {
        var parts = new List<string>();

        foreach (var member in members)
        {
            string value;

            if (member.IsLeaf)
            {
                if (element >= member.Fields.Count)
                    continue;

                var field = member.Fields[element];
                var cell = row[field.Index];

                value = cell.HasValue ? ScalarValueMarkup(field, cell.Value, root) : Absent();
            }
            else
            {
                value = $"<span class=\"obj\"><span class=\"sep\">(</span>" +
                        RecordBody(member.Members, row, element, root) +
                        "<span class=\"sep\">)</span></span>";
            }

            parts.Add($"<span class=\"mem\">{Esc(member.Name)}</span>" +
                      $"<span class=\"msep\">: </span>{value}");
        }

        return string.Join("<span class=\"sep\">, </span>", parts);
    }

    /// <summary>
    /// The heading of one entry: the record's name where the sheet spread it over columns,
    /// and the column's own name where it did not.
    /// </summary>
    private static string NameCell(Models.Table table, Models.SerialField entry, int position)
    {
        var head = HeadOf(entry);

        if (head is null)
            return "<th></th>";

        // A single column the model reads as an array is still one entry, and its heading is
        // the entry - `worldBuffId`, not `worldBuffId[0]`. The sheet writes the element in
        // the column name because a sheet has nowhere else to put it; the page has one
        // column for the whole array, so the index in the heading names nothing.
        if (!entry.IsRecord && entry.Fields.Count <= 1 && !entry.IsArray)
            return NameCell(table, head, position);

        // A record, or an array: the entry has a name of its own and the columns under it are
        // its parts.
        var notes = new List<string>();

        if (entry.IsRecord)
        {
            notes.Add($"레코드 {entry.Members.Count}멤버");
            notes.AddRange(entry.Members.Select(member => MemberNote(member)));
        }
        else
        {
            notes.Add($"컬럼 {entry.Fields.Count}개를 접은 배열");
        }

        if (!IsRequiredEntry(entry))
            notes.Add("옵셔널");

        return $"<th id=\"{HtmlLinks.ColumnAnchor(table.Name, head.Name)}\" " +
               $"title=\"{Esc(string.Join(" · ", notes))}\">{Esc(EntryCaption(entry, head))}</th>";
    }

    /// <summary>
    /// What an entry is called, in the sheet's own spelling where the sheet has one.
    /// </summary>
    /// <remarks>
    /// A column name carries the element it holds - `worldBuffId[0]`, `statEffect[0]["Id"]` -
    /// because a sheet has one column per element and nowhere else to write which. The page
    /// has one column for the whole entry, so everything from the first bracket on names a
    /// part rather than the entry, and the heading drops it. Where the sheet spread an array
    /// over numbered names instead (`Slot1`, `Slot2`), the model's own name is the only one
    /// that covers them all.
    /// </remarks>
    private static string EntryCaption(Models.SerialField entry, Models.Field head)
    {
        string raw = head.RawName ?? "";
        int bracket = raw.IndexOf('[');

        if (bracket > 0)
            return raw.Substring(0, bracket);

        return entry.Fields.Count > 1 || raw.Length == 0 ? entry.Name : raw;
    }

    /// <summary>One member of a record, as the heading's tooltip names it.</summary>
    private static string MemberNote(Models.RecordMember member)
    {
        if (!member.IsLeaf)
            return $"{member.Name}: {{{string.Join(", ", member.Members.Select(inner => inner.Name))}}}";

        var field = member.Fields.FirstOrDefault();

        return field is null ? member.Name : $"{member.Name}: {field.TypeName}";
    }

    /// <summary>
    /// A column-name header, carrying the anchor the column index links to.
    ///
    /// What the column is beyond its name goes in a tooltip: the primary index, the
    /// array it folded into, whether it may be absent. The header shows the sheet's own
    /// columns rather than the folded arrays, so the page reads like the workbook it
    /// documents.
    /// </summary>
    private static string NameCell(Models.Table table, Models.Field field, int position)
    {
        var notes = new List<string>();

        if (position == 0)
            notes.Add("기본 인덱스");

        // The name the generated code uses, when the sheet spells the column differently -
        // `statEffect[0]["Id"]` is one column of a record array and `StatEffect0Id` is what
        // a reader will find in the generated type.
        if (field.RawName != field.Name)
            notes.Add($"생성 이름 {field.Name}");

        string? group = GroupNameOf(table, field);

        if (group is not null)
            notes.Add($"{group}으로 묶임");

        if (!field.IsRequired)
            notes.Add("옵셔널");

        notes.AddRange(ConstraintNotes(field));

        string title = notes.Count > 0 ? $" title=\"{Esc(string.Join(" · ", notes))}\"" : "";

        // The sheet's spelling rather than the normalized one: this page documents the
        // workbook, and a record array written `statEffect[0]["Id"]` flattened to
        // `StatEffect0Id` loses the one thing that says it is an array of records.
        string caption = string.IsNullOrEmpty(field.RawName) ? field.Name : field.RawName;

        return $"<th id=\"{HtmlLinks.ColumnAnchor(table.Name, field.Name)}\"{title}>{Esc(caption)}</th>";
    }

    /// <summary>
    /// What the sheet declared about a column beyond its type, as short notes.
    /// </summary>
    /// <remarks>
    /// These live in the model and no page showed them, which is a strange gap for pages
    /// whose purpose is checking data: a value out of its declared range is exactly what
    /// somebody is looking for.
    /// </remarks>
    private static IEnumerable<string> ConstraintNotes(Models.Field field)
    {
        var constraints = field.Constraints;

        if (constraints.Minimum is double min)
            yield return $"최소 {min.ToString(CultureInfo.InvariantCulture)}";

        if (constraints.Maximum is double max)
            yield return $"최대 {max.ToString(CultureInfo.InvariantCulture)}";

        if (constraints.AllowedValues is { Count: > 0 } allowed)
            yield return $"허용값 {allowed.Count}개";

        if (constraints.RequiredInRecord)
            yield return "레코드 안에서 필수";
    }

    private string DataCell(
        Models.Table table, Models.Field field, Cell cell, bool isIndex, string root)
    {
        // A row with no value for a column does not hold a null: it holds the type's empty
        // value with `HasValue` false beside it, which is how the wire carries absence. Read
        // as a value, that is a zero nobody typed - the exact confusion the marker exists to
        // prevent, drawn by the page whose job is preventing it.
        object? value = cell.HasValue ? cell.Value : null;

        string content = DataValueMarkup(field, value, root, cell.ElementHasValue);

        // The index cell is the row's anchor, so a reference elsewhere can name the row.
        // Escaped, because a string index is a value from the sheet and it is going into
        // an attribute.
        if (isIndex)
        {
            return $"<td class=\"key\" id=\"{Esc(HtmlLinks.RowAnchor(table.Name, value))}\">" +
                   $"<code>{content}</code></td>";
        }

        if (field.IsRef)
            return $"<td class=\"key\"><code>{content}</code></td>";

        // How many elements, outside whatever gets clipped, because the count is the thing
        // a reader is checking when a column holds a variable-length array.
        string badge = ElementCount(value) is int count && count > 1
            ? $"<span class=\"n-of\">&times;{count}</span>"
            : "";

        // A long value is clipped rather than allowed to set the column's width. One line
        // of dialogue is wider than a screen, and a table with a dialogue column in it had
        // every other column pushed off the side by that one. The whole value stays in the
        // page, so the panel that opens on hover has all of it.
        return IsLongText(field, value)
            ? $"<td class=\"text\"><span class=\"clip\">{content}</span>{badge}</td>"
            : $"<td{CellClass(field)}>{content}{badge}</td>";
    }

    /// <summary>
    /// Whether a cell holds more text than a column should be made wide for.
    /// </summary>
    /// <remarks>
    /// Decided from the value rather than in the stylesheet, so only the cells that need
    /// clipping carry the markup for it - and the clip is a display decision only: the
    /// whole value stays in the page, so filtering still matches it and copying still
    /// takes it.
    /// </remarks>
    private static bool IsLongText(Models.Field field, object? value)
    {
        const int longString = 60;
        const int longArray = 44;

        if (value is null)
            return false;

        if (value is string text)
            return field.ElementType == Models.ValueType.String && text.Length > longString;

        // An array of anything can be long: a hundred ids is as wide as a line of dialogue.
        // Measured on what the cell will hold rather than on the element count, because ten
        // long names and ten small numbers are not the same width.
        if (value is Array elements)
        {
            int total = 2;

            foreach (var element in elements)
                total += (element?.ToString()?.Length ?? 1) + 2;

            return total > longArray;
        }

        return false;
    }

    /// <summary>How many elements a cell holds, or null when it holds a single value.</summary>
    private static int? ElementCount(object? value) => value is Array elements ? elements.Length : null;

    /// <summary>
    /// The class a data cell carries, which is how a value's kind is shown now that the
    /// markup has no `font` elements in it.
    /// </summary>
    private static string CellClass(Models.Field field)
        => field.ElementType switch
        {
            Models.ValueType.Int32 or Models.ValueType.Int64
                or Models.ValueType.Float or Models.ValueType.Double => " class=\"num\"",
            _ => "",
        };

    /// <summary>
    /// The array or record this column is exposed as, when it folded into a group with
    /// others. Null for a column that stands alone.
    /// </summary>
    private static string? GroupNameOf(Models.Table table, Models.Field field)
    {
        // A record member carries its group in its own path. Asking the groups would not
        // answer: a record group's `Fields` is empty by design, since its columns belong
        // to its members rather than to it.
        if (field.IsRecordMember)
            return field.GroupName;

        foreach (var sf in table.SerialFields)
        {
            if (sf.Fields.Count > 1 && sf.Fields.Contains(field))
                return sf.Name;
        }

        return null;
    }

    private string TypeMarkup(Models.Field field, string root)
    {
        // Several targets, so the column's type is the key it carries and the arrow names

        if (field.IsRef)
        {
            // What it points at, as a link to that table's page.
            //
            // It used to read `ref?` in red bold, which is what a generator prints when it
            // has not decided - and a reader cannot tell that from an error. The name was
            // always to hand; only the rendering was missing.
            string? target = field.ResolvedRefTable is not null
                ? field.ResolvedRefTable!.Name
                : field!.RefTableName;

            string? caption = string.IsNullOrEmpty(field.RefFieldName)
                ? target
                : $"{target}.{field.RefFieldName}";

            string arrow = $"&#x2192; {Esc(caption)}";

            // The key's own type, and then what it points at. The arrow alone left the
            // column's type unsaid - a reader looking at `-> AdventureSpecialEffect` cannot
            // tell whether the cells hold a number or a name, which is exactly what they
            // need to know to read the values under it. The target's own key type is not
            // the answer either: this column carries what this sheet wrote.
            string keyType = KeySpelling(field);

            string left = string.IsNullOrEmpty(keyType)
                ? ""
                : $"<span class=\"type\">{Esc(keyType)}" +
                  $"{(field.IsArray ? "[]" : "")}{Optional(field)}</span> ";

            // Only as a link when the table it names is in this model. A reference whose
            // target was filtered out by side, or never resolved, would otherwise be a
            // link to a page this run did not write.
            return _model.FindTable(target) is not null
                ? $"{left}<a href=\"{HtmlLinks.Table(target!, root)}\" title=\"{Esc(caption)} 참조\">{arrow}</a>{KeyMark(field)}"
                : $"{left}<span class=\"flag\" title=\"{Esc(caption)} 참조\">{arrow}</span>{KeyMark(field)}";
        }

        // Element type drives the choice; the brackets are appended after, so an array
        // of enums still links to its declaration.
        string suffix = field.IsArray ? "[]" : "";

        if (field!.ElementType == Models.ValueType.Enum)
        {
            return $"<a href=\"{HtmlLinks.Enum(field.Enum.Name, root)}\" data-enum=\"{Esc(field.Enum.Name)}\">" +
                   $"enum.{Esc(field.Enum.Name)}</a>{suffix}{Optional(field)}{KeyMark(field)}";
        }

        // A role is how the sheet spelled the type: a localizable string is written `text`
        // and a file name `asset(icon)`, and both are carried as a string with the role
        // beside it. The page showed `string`, which is the value's type and not what the
        // column says - and the group, which is the whole point of the role, was nowhere.
        string spelling = field.Role switch
        {
            StringRole.Text => WithGroup("text", field),
            StringRole.Asset => WithGroup("asset", field),
            _ => field.TypeName,
        };

        // The type of an index column reads like any other type; what marks it is the key
        // beside it, not a colour of its own.
        return $"<span class=\"type\">{Esc(spelling)}{suffix}{Optional(field)}</span>" +
               KeyMark(field) + DeclaredTargets(field, root);
    }

    /// <summary>
    /// The tables a column's sheet says its value belongs to, when the column is not a
    /// resolved reference.
    /// </summary>
    /// <remarks>
    /// Some layouts declare a catalogue rather than a reference - "this id is a row of one
    /// of these tables" - and the cooker promotes that to a real reference wherever it can.
    /// What it holds back is a member of a record group naming several tables, because what
    /// that looks like inside a generated element has not been designed. The declaration is
    /// still the most useful thing on the page about such a column, and the page was showing
    /// `double` and nothing else.
    ///
    /// Linked when the named table is in this build, plain text when it is not - the
    /// conversion checks the ids either way, and a link to a page this run did not write is
    /// worse than no link.
    /// </remarks>
    private string DeclaredTargets(Models.Field field, string root)
    {
        // Nothing to add where the column is a reference: the type cell already names what
        // it points at, and for several targets it names all of them.
        if (field.IsRef || field.Constraints.ReferencedTables is not { Count: > 0 } named)
            return "";

        var parts = named.Select(name => _model.FindTable(name) is not null
            ? $"<a href=\"{HtmlLinks.Table(name, root)}\">{Esc(name)}</a>"
            : Esc(name));

        string title = named.Count > 1
            ? "시트가 이 값이 이 테이블들 중 하나의 행이라고 선언합니다. 변환이 대조하고, 참조로 승격되지는 않습니다"
            : "시트가 이 값이 이 테이블의 행이라고 선언합니다. 변환이 대조합니다";

        return $" <span class=\"declared\" title=\"{Esc(title)}\">&#x21e2; " +
               $"{string.Join(" <span class=\"sep\">|</span> ", parts)}</span>";
    }

    /// <summary>
    /// A role as the sheet writes it, with the group and namespace when the column named
    /// them: `text`, `text(Common)`, `text(Achievement,Quests)`.
    /// </summary>
    private static string WithGroup(string role, Models.Field field)
    {
        if (string.IsNullOrEmpty(field.RoleGroup))
            return role;

        return string.IsNullOrEmpty(field.RoleNamespace)
            ? $"{role}({field.RoleGroup})"
            : $"{role}({field.RoleGroup},{field.RoleNamespace})";
    }

    private string DataValueMarkup(
        Models.Field field, object? value, string root, bool[]? elementsPresent = null)
    {
        // Absent rather than empty. An optional column that this row does not fill has no
        // value at all, and rendering that as a blank cell - or as the zero the wire carries
        // for it - makes it the same as a value somebody wrote.
        if (value is null)
            return Absent();

        if (NamedTablesOf(field).Count > 0 && !field.IsArray)
            return KeyMarkup(field, value, root);

        // A delimited cell holds an array, so render its elements. Falling into the
        // scalar switch below would try to cast the array to the element type.
        if (field.IsArray && value is Array elements)
        {
            if (elements.Length == 0)
                return "<span class=\"empty\" title=\"원소 없음\">[]</span>";

            // In brackets, because a cell holding `1, 2, 3` and a cell holding one value
            // that happens to contain commas read the same otherwise.
            var rendered = new StringBuilder("<span class=\"sep\">[</span>");

            for (int i = 0; i < elements.Length; i++)
            {
                if (i > 0)
                    rendered.Append("<span class=\"sep\">, </span>");

                // Per element, from the same flag the wire's element bitmap is written from.
                bool present = elementsPresent is null || i >= elementsPresent.Length || elementsPresent[i];

                rendered.Append(present
                    ? ScalarValueMarkup(field, elements.GetValue(i), root)
                    : Absent());
            }

            return rendered.Append("<span class=\"sep\">]</span>").ToString();
        }

        return ScalarValueMarkup(field, value, root);
    }

    /// <summary>How the page draws a value that is not there.</summary>
    private static string Absent() => "<span class=\"null\" title=\"값 없음\">null</span>";

    /// <summary>
    /// A reference's stored key, as a way to the row it names.
    /// </summary>
    /// <remarks>
    /// The key rather than the value it points at. Following the reference and rendering
    /// the target's value was attempted and abandoned - a chain that leads back on itself
    /// recursed without bound - and showing the key is the honest thing for a page
    /// documenting what is stored. What was missing is that the key is a place: clicking
    /// it now lands on that row of that table's page.
    ///
    /// A column may declare several targets, and then the key alone does not say which of
    /// them holds the row - but the model does, so the cell names the one that has it. The
    /// anchor is only used when that page really carries the row - the row cap means it
    /// may not - and otherwise the link is to the page.
    ///
    /// **This used to offer every target and let the reader pick.** That was honest while
    /// nothing knew the answer; the generated code answers it now, and a page that shows
    /// two candidates where the accessors show one is the page being vaguer than the data.
    /// spec/multi-target-accessors.md.
    /// </remarks>
    private string RefValueMarkup(Models.Field field, object value, string root)
    {
        string key = value.ToString() ?? "";

        var targets = RefTargetsOf(field)
                      .Where(name => _model.FindTable(name) is not null)
                      .Distinct(StringComparer.Ordinal)
                      .ToList();

        if (targets.Count == 0)
            return Esc(key);

        if (targets.Count > 1)
        {
            // The one that actually holds this key. Conversion refuses two targets holding
            // one id, so there is at most one - and none when the cell points at nothing.
            var holders = targets.Where(name => KeysOf(name).Contains(key)).ToList();

            if (holders.Count == 0)
            {
                return $"{Esc(key)} <span class=\"hint\" title=\"어느 대상에도 없습니다\">" +
                       $"&#x2192; &mdash;</span>";
            }

            targets = holders;
        }

        if (targets.Count == 1)
        {
            return $"<a href=\"{RowHref(targets[0], key, root)}\" " +
                   $"data-ref=\"{Esc(targets[0])}\" data-key=\"{Esc(key)}\">{Esc(key)}</a>";
        }

        var choices = targets.Select(name =>
            $"<a href=\"{RowHref(name, key, root)}\" " +
            $"data-ref=\"{Esc(name)}\" data-key=\"{Esc(key)}\">{Esc(name)}</a>");

        return $"{Esc(key)} <span class=\"hint\">&#x2192; " +
               $"{string.Join(" <span class=\"sep\">&middot;</span> ", choices)}</span>";
    }

    /// <summary>
    /// Every key one table holds, as text, built once per table.
    /// </summary>
    /// <remarks>
    /// Only the tables a multi-target column names ever ask, and each asks per cell - so the
    /// set is cached rather than the rows walked again. As text because that is what the cell
    /// is rendering: a key is compared against what the page will print, and a boxed `10.0`
    /// is not a boxed `10`.
    ///
    /// Every row, not the shown ones: which target holds a key is a fact about the data, and
    /// the row cap is about the page. spec/multi-target-accessors.md.
    /// </remarks>
    private HashSet<string> KeysOf(string table)
    {
        if (_allKeys.TryGetValue(table, out var keys))
            return keys;

        keys = new HashSet<string>(StringComparer.Ordinal);
        _allKeys[table] = keys;

        var target = _model.FindTable(table);
        var index = target?.PrimaryIndexField;

        if (index is null)
            return keys;

        foreach (var row in target!.Data)
            keys.Add(row[index.Index].Value?.ToString() ?? "");

        return keys;
    }

    /// <summary>
    /// A stored key, as a way to the row it names and to every table that might hold it.
    /// </summary>
    /// <remarks>
    /// The column may name several tables - the sheets say "this id is a row of
    /// `StatOperator` or of `WorldPassiveEffect`" - and which one holds a given row is a
    /// question about the value, not about the column. So the value is asked: the tables
    /// are looked in, the one that has the row is what the link goes to, and every named
    /// table is carried on the link so the panel can show what each of them says. A reader
    /// hovering the key sees the row it found and, beside it, that the other table does not
    /// have it - which is the answer, rather than a choice handed back to them.
    ///
    /// Zero is the conventional "points at nothing" and is left as it is: index values start
    /// at one, so it can never be a row.
    /// </remarks>
    /// <summary>
    /// How a reference's own cells are spelled - which is not always how its type is.
    /// </summary>
    /// <remarks>
    /// A reference to a whole row is a `ForeignRecord`, and the model names that type after
    /// the table it resolves to: `AdvSpcEffTerms.Record`. Printed left of the arrow that
    /// already names the table, it said the table twice and the cells' own type not at all.
    /// The cells hold the target's primary index, so the target's own sheet is what spells
    /// it - the page never has to invent a name for a type.
    ///
    /// A dotted reference (`Table.Field`) resolves to a value instead, and the field's type
    /// name is already that value's.
    /// </remarks>
    private static string KeySpelling(Models.Field field)
    {
        if (field.ElementType != Models.ValueType.ForeignRecord)
            return field.TypeName;

        return field.ResolvedRefTable?.PrimaryIndexField?.TypeName ?? "";
    }

    /// <summary>
    /// A string as a value: in quotes, so `0` and `"0"` are different cells and a value
    /// with a comma in it is one value rather than two. An empty string is then the quotes
    /// with nothing between them, which is what it is.
    /// </summary>
    private static string StringMarkup(string text)
        => text.Length == 0
            ? "<span class=\"empty\" title=\"빈 문자열\">&quot;&quot;</span>"
            : $"<span class=\"str\">&quot;{Esc(text)}&quot;</span>";

    private string KeyMarkup(Models.Field field, object value, string root)
    {
        string key = value.ToString() ?? "";

        // A key is a value before it is a link, and a string key wears the quotes every
        // other string on the page wears - without them a column of names read as bare
        // words while the column beside it, the same strings but not a reference, read as
        // strings. The quotes only, without the string colour: the colour here is the
        // link's, which is what says the cell can be followed.
        bool textual = field.ElementType == Models.ValueType.String;

        // Two spellings of one key: as a value on its own, and as the text of a link. The
        // link's colour is what says the cell can be followed, so inside one the string
        // wears the quotes without the string colour.
        string plain = textual ? StringMarkup(key) : Esc(key);
        string linked = textual ? $"&quot;{Esc(key)}&quot;" : Esc(key);

        if (key.Length == 0)
            return plain;

        // `0` in a numeric key is how a sheet leaves a reference empty. In a string key it
        // is a key.
        if (!textual && key == "0")
            return plain;

        // Every route out of here that is not a link still spells the value the way the rest
        // of the page spells it. A target this build does not hold is the common one, and
        // those cells read as bare words beside a column of the same strings that is not a
        // reference and does wear quotes.
        var named = NamedTablesOf(field).Where(name => _model.FindTable(name) is not null).ToList();

        if (named.Count == 0)
            return plain;

        var holders = named.Where(name => KeysOf(name).Contains(key)).ToList();

        // Where the link goes: the table that has the row, or the first named one when none
        // does - a page is still better than nothing, and the panel says what happened.
        string destination = holders.Count > 0 ? holders[0] : named[0];

        string carried = string.Join("|", named);

        string mark = holders.Count == 0
            ? $" <span class=\"flag\" title=\"{Esc(string.Join(", ", named))}에 이 행이 없습니다\">?</span>"
            : "";

        // The table's name beside the key when the column names more than one, so the answer
        // is on the page and not only in the panel.
        string which = named.Count > 1 && holders.Count > 0
            ? $" <span class=\"hint\">&#x2192; {Esc(holders[0])}</span>"
            : "";

        return $"<a href=\"{RowHref(destination, key, root)}\" " +
               $"data-refs=\"{Esc(carried)}\" data-key=\"{Esc(key)}\">{linked}</a>{which}{mark}";
    }

    /// <summary>
    /// A link to one row of a table's page, or to the page when that row is past the cap.
    /// </summary>
    private string RowHref(string table, string key, string root)
        => _anchoredRows.TryGetValue(table, out var keys) && keys.Contains(key)
            // The fragment is escaped and the anchor it names is not. A key can be a string,
            // and a string key can hold a space, a quote or a `#` - characters that end an
            // attribute or a url early. The browser unescapes a fragment before matching it
            // against an id, so the readable form is the one that stays on the element.
            ? $"{root}{HtmlLinks.TablePage(table)}#{Uri.EscapeDataString(HtmlLinks.RowAnchor(table, key))}"
            : $"{root}{HtmlLinks.TablePage(table)}";

    /// <summary>
    /// A value as text, in a form that does not depend on the machine's culture.
    /// </summary>
    /// <remarks>
    /// For the types whose default `ToString()` is culture-sensitive - the numbers, the
    /// clock types - and a passthrough for the rest. Everything written into a page goes
    /// through here or through <see cref="ScalarValueMarkup"/>, so the same sheet produces
    /// the same page wherever it is converted.
    /// </remarks>
    private static string? PlainValue(Models.ValueType type, object value)
    {
        if (value is null)
            return "";

        return type switch
        {
            Models.ValueType.Int32 => ((int)value!).ToString(CultureInfo.InvariantCulture),
            Models.ValueType.Int64 => ((long)value!).ToString(CultureInfo.InvariantCulture),
            Models.ValueType.Float => ((float)value!).ToString(CultureInfo.InvariantCulture),
            Models.ValueType.Double => ((double)value!).ToString(CultureInfo.InvariantCulture),
            Models.ValueType.DateTime => ((DateTime)value!).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Models.ValueType.TimeSpan => ((TimeSpan)value!).ToString(null, CultureInfo.InvariantCulture),
            Models.ValueType.Uuid => ((Guid)value!).ToString(),
            Models.ValueType.Bool => (bool)value ? "true" : "false",
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// One value of a field's element type.
    /// </summary>
    private string ScalarValueMarkup(Models.Field field, object? value, string root)
    {
        if (value is null)
            return Absent();

        // A record member and a folded array element are values of a field too, so a key
        // inside one is a key: it links to its row like any other.
        if (NamedTablesOf(field).Count > 0)
            return KeyMarkup(field, value, root);

        switch (field!.ElementType)
        {
            case Models.ValueType.String:
                return StringMarkup((string)value);

            case Models.ValueType.Bool:
                // A tick or a cross, rather than the words: a column of them reads as a
                // pattern, which is what someone scanning the page is looking for.
                //
                // False used to be nothing at all, which is the same as a cell with no value
                // and the same as one nobody filled in - three different things drawn alike
                // on a page whose whole purpose is telling them apart.
                return ((bool)value!)
                    ? "<span class=\"yes\">&#x2714;</span>"
                    : "<span class=\"no\">&#x2718;</span>";

            case Models.ValueType.Int32:
                return ((int)value!).ToString(CultureInfo.InvariantCulture);

            case Models.ValueType.Int64:
                return ((long)value!).ToString(CultureInfo.InvariantCulture);

            case Models.ValueType.Float:
                return ((float)value!).ToString(CultureInfo.InvariantCulture);

            case Models.ValueType.Double:
                return ((double)value!).ToString(CultureInfo.InvariantCulture);

            case Models.ValueType.DateTime:
                return ((DateTime)value!).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            case Models.ValueType.TimeSpan:
                return ((TimeSpan)value!).ToString(null, CultureInfo.InvariantCulture);

            case Models.ValueType.Uuid:
                return ((Guid)value!).ToString();

            case Models.ValueType.Enum:
            {
                var label = field.Enum!.GetLabel((int)value!, null);

                // `data-enum` is what the hover card is built from, so the whole enum can
                // be read without leaving the row it was read from.
                return EnumValueLink(field.Enum, label, root);
            }

            case Models.ValueType.ForeignRecord:
                // A reference whose target is not in this build: there is nowhere to link,
                // and the stored key is the whole of what the page can say.
                return Esc(value.ToString());

            default:
                throw new TabbitDefectException($"unsupported type `{field.Type}`");
        }
    }

    /// <summary>
    /// One enum label, as a link to its declaration and as the handle the hover card
    /// reads: the enum it belongs to and which of its labels this is.
    /// </summary>
    /// <remarks>
    /// No `title`. It named the same enum and label the card shows, so hovering produced
    /// the card and the browser's own tooltip over it - two answers to one question, one
    /// of them drawn by the operating system.
    /// </remarks>
    private static string EnumValueLink(Models.Enum enumm, Models.Enum.Label label, string root)
        => $"<a class=\"enum\" href=\"{HtmlLinks.EnumLabel(enumm.Name, label.Name, root)}\" " +
           $"data-enum=\"{Esc(enumm.Name)}\" data-label=\"{Esc(label.Name)}\">{Esc(label.Name)}</a>";

    // ----------------------------------------------------------- helpers

    private static HtmlSummaryEntryView Summarize(string name, string comment, string detail, string href)
        => new HtmlSummaryEntryView
        {
            Name = Esc(name),
            Comment = Esc(comment),
            Detail = detail,
            Href = href,
        };

    /// <summary>
    /// Which sheet and which cell, for the line under a page's title.
    ///
    /// Without the filename: the workbook is on the overview, and a page documenting one
    /// entity is asked the narrower question. Empty when the source addresses no cell,
    /// which is how a page for a source that has none says nothing rather than `` : ``.
    /// </summary>
    /// <summary>
    /// Where a declaration is, as a reader would go looking for it: the workbook, the sheet,
    /// and the cell.
    /// </summary>
    /// <remarks>
    /// The sheet alone was not enough to find anything. A project keeps its sheets in a
    /// dozen workbooks and the sheet names do not say which - so "declared in `Shop`" left
    /// the reader opening files until one of them had that tab.
    ///
    /// The workbook by its file name, with the path it came from in the tooltip: the path is
    /// the same for every row of a list, and a column of identical text is a column nobody
    /// reads.
    /// </remarks>
    private static string SourceCell(Models.Location location)
        => string.IsNullOrEmpty(location.Sheet)
            ? ""
            : $"{WorkbookName(location)} &middot; " +
              $"<svg class=\"i\"><use href=\"#i-sheet\"></use></svg>" +
              $"{Esc(location.Sheet)} : {Esc(location.CellRange)}";

    /// <summary>Which sheet of which workbook, for a list that names one per row.</summary>
    private static string SheetName(Models.Location location)
        => string.IsNullOrEmpty(location.Sheet)
            ? WorkbookName(location)
            : $"{WorkbookName(location)} <span class=\"hint\">&middot;</span> {Esc(location.Sheet)}";

    /// <summary>
    /// The workbook a location is in, by file name, carrying its path as a tooltip.
    /// </summary>
    private static string WorkbookName(Models.Location location)
    {
        string path = location.Filename ?? "";

        if (path.Length == 0)
            return "";

        int slash = path.LastIndexOf('/');
        string name = slash < 0 ? path : path.Substring(slash + 1);

        return $"<span class=\"book\" title=\"{Esc(path)}\">" +
               $"<svg class=\"i\"><use href=\"#i-workbook\"></use></svg>{Esc(name)}</span>";
    }

    /// <summary>The side a column goes to, in the vocabulary a recipe writes it in.</summary>
    private static string SideName(Models.TargetSide side)
        => side switch
        {
            Models.TargetSide.ClientOnly => "c",
            Models.TargetSide.ServerOnly => "s",
            Models.TargetSide.Both => "cs",
            _ => "",
        };

    /// <summary>The same thing spelled out, for a chart label.</summary>
    private static string SideCaption(Models.TargetSide side)
        => side switch
        {
            Models.TargetSide.ClientOnly => "클라 전용",
            Models.TargetSide.ServerOnly => "서버 전용",
            Models.TargetSide.Both => "양쪽",
            _ => "어느 쪽도 아님",
        };

    /// <summary>
    /// Escapes text that came from the spreadsheet before it reaches the page.
    ///
    /// Comments and string cells are written by designers, so an ampersand or an
    /// angle bracket in a perfectly ordinary description used to break the
    /// generated documentation - the text was interpolated into the markup raw.
    /// </summary>
    private static string Esc(string? text)
        => string.IsNullOrEmpty(text) ? "" : WebUtility.HtmlEncode(text);

    /// <summary>
    /// The caption for something, as an anchor back to the cell it was declared in when
    /// the source has an addressable url, and as plain text when it does not.
    ///
    /// Google Sheets links open where they point. A workbook on disk does not - and this
    /// used to return the empty string in that case, which took the caption with it. So
    /// an Excel-sourced model produced enum pages whose heading read `Enumeration:` with
    /// no name after it and whose rows had an empty cell where each label's name should
    /// be. Every model in the fixtures is Excel-sourced, and the golden pages recorded
    /// the blanks as correct.
    ///
    /// The text is what matters here; the link is a convenience on top of it.
    /// </summary>
    private static string SourceSheetLink(Models.Location location, string caption = "")
    {
        string text = Esc(string.IsNullOrEmpty(caption) ? location.ToString() : caption);

        if (string.IsNullOrEmpty(location.SheetUrl))
            return text;

        return $"<a href=\"{location.SheetUrl}\" title=\"원본 시트로\">{text}</a>";
    }

    private void Write(string filename, string templateName, HtmlPageView view)
    {
        view.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH':'mm':'ss");

        string fullPath = Path.Combine(_htmlRecipe.Path, filename);

        StagingFiles.WriteAllTextToFile(
            fullPath, TemplateEngine.Render(templateName, view));
    }
}

/// <summary>
/// Where each page is, and what each anchor in it is called.
///
/// One place, because the callers used to disagree with the generator: every enum link
/// was written as `enums.html`, and this target has never produced a file by that name -
/// it writes `enums/&lt;kebab-name&gt;.html`, one per enum. So every enum link in the
/// generated documentation was a dead one, on the index page and in every type column
/// and every enum-valued cell.
///
/// A golden comparison cannot catch that. It checks that the markup has not changed,
/// which it had not: the link had been wrong since it was written. The same trap was
/// waiting for the tables, whose type columns pointed at an anchor on the page they were
/// already on - correct only while every table shared one page.
///
/// `root` is how a page reaches the output root: empty for a page at the root, `../` for
/// one in a subdirectory.
/// </summary>
internal static class HtmlLinks
{
    public static string TablePage(string table) => $"tables/{table.ToKebabCase()}.html";

    public static string EnumPage(string enumName) => $"enums/{enumName.ToKebabCase()}.html";

    /// <summary>
    /// A table's page. No fragment: the page is the table, and an anchor on it landed the
    /// reader below the title with the sticky bar over the header row.
    /// </summary>
    public static string Table(string table, string root)
        => $"{root}{TablePage(table)}";

    public static string Column(string table, string field, string root)
        => $"{root}{TablePage(table)}#{ColumnAnchor(table, field)}";

    public static string Enum(string enumName, string root)
        => $"{root}{EnumPage(enumName)}#enum_{enumName}";

    public static string EnumLabel(string enumName, string label, string root)
        => $"{root}{EnumPage(enumName)}#const_{enumName}.{label}";

    public static string ConstantSet(string set, string root)
        => $"{root}constantsets.html#constantset_{set}";

    public static string ColumnAnchor(string table, string field) => $"col_{table}.{field}";

    public static string RowAnchor(string table, object? key) => $"row_{table}.{key}";
}
