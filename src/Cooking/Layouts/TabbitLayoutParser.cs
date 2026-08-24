using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Serilog;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Models.Raw;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The layout this tool means people to write: entities begin at a declaration cell, and the
/// column that cell sits in is the entity's marker column.
/// </summary>
/// <remarks>
/// <code>
///     :table Item(side=s) | an item                &lt;- declaration, then its description
///     :field              | *code | name  | grade
///     :type               | int   | string| Grade
///     :desc               | the id| shown | tier
///                         | 1     | Sword | High   &lt;- data; the marker column is blank
///     #                   | 2     | Bow   | Low    &lt;- left out of the conversion
/// </code>
///
/// **What separates it from the layout it replaces is that every position means one thing.**
/// The marker column says what a row is, a header row key says what a header row is, and the
/// order of the header rows is free - so a sheet sorted with its header rows in the selection
/// is reported at the row that moved rather than read as data.
///
/// The notation is defined in `spec/primary-layout.md`, and the sections below name the part
/// of it each rule comes from.
/// </remarks>
[TabbitLayout("tabbit",
    Summary = "Entities declared with `:table` cells, whose column is the entity's marker column.")]
public sealed class TabbitLayoutParser : ILayoutParser
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Cooking;

    #region The notation's words

    private const string KindTable = "table";
    private const string KindEnum = "enum";
    private const string KindConst = "const";

    private const string RowKeyField = ":field";
    private const string RowKeyType = ":type";
    private const string RowKeyDesc = ":desc";
    private const string RowKeyTarget = ":target";
    private const string RowKeyVariant = ":variant";

    /// <summary>Marks a memo column, and a row the conversion leaves out.</summary>
    private const string OmitMark = "#";

    /// <summary>The second spelling of <see cref="OmitMark"/>, for a marker-column cell.</summary>
    private const string OmitMarkAlternate = "//";

    private static readonly string[] AllRowKeys =
        [RowKeyField, RowKeyType, RowKeyDesc, RowKeyTarget, RowKeyVariant];

    /// <summary>
    /// The header rows an enum or a constant set may carry, which is `:field` alone.
    /// </summary>
    /// <remarks>
    /// Their columns are fixed and named, so every other row key has nothing to say - and a
    /// description belongs to a label rather than to a column here, which is what the `desc`
    /// **column** is for. A `:desc` row was accepted for a while and read by nothing, which is
    /// the quiet no-op this layout is meant not to have.
    /// </remarks>
    private static readonly string[] EntityRowKeys = [RowKeyField];

    private static readonly string[] DeclarationMetaKeys = ["side", "key", "extends"];

    /// <summary>The declaration keys only a `:table` takes, and what each is for in a report.</summary>
    /// <remarks>
    /// An index and a variant set are both facts about rows, and an enum and a set of
    /// constants have none - so writing either on one is a mistake worth a name rather than a
    /// key that quietly does nothing. spec/polymorphism.md section 3.
    /// </remarks>
    private static readonly Dictionary<string, string> TableOnlyMetaKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = "an index is a column of a table's rows",
            ["extends"] = "a variant set is a set of tables",
        };

    /// <summary>
    /// The `:field` names this layout reserves - section 7.
    /// </summary>
    /// <remarks>
    /// Recognized and refused rather than left undefined. Each one is a notation a later spec
    /// settles - a polymorphic record's discriminator, a map's key column, a variant's packed
    /// value - and holding the name now means those specs do not have to choose a spelling
    /// around whatever a sheet happened to use in the meantime.
    /// </remarks>
    private static readonly string[] ReservedColumnNames = [":type", ":key", ":value"];

    /// <summary>The columns of an enum, by the name written in `:field`.</summary>
    private const string EnumColumnLabel = "label";
    private const string EnumColumnValue = "value";
    private const string EnumColumnAlias = "alias";
    private const string EnumColumnDesc = "desc";

    private static readonly string[] EnumColumns =
        [EnumColumnLabel, EnumColumnValue, EnumColumnAlias, EnumColumnDesc];

    /// <summary>The columns of a constant set.</summary>
    private const string ConstColumnName = "name";
    private const string ConstColumnType = "type";
    private const string ConstColumnValue = "value";
    private const string ConstColumnDesc = "desc";

    private static readonly string[] ConstColumns =
        [ConstColumnName, ConstColumnType, ConstColumnValue, ConstColumnDesc];

    #endregion

    /// <summary>One row below an entity's headers, and whether `#` left it out.</summary>
    private readonly struct DataRow(int row, bool omitted)
    {
        public int Row { get; } = row;

        public bool Omitted { get; } = omitted;
    }

    /// <summary>One `key=value` from a declaration's brackets, and where it was written.</summary>
    private readonly struct MetaEntry(string value, Location at)
    {
        public string Value { get; } = value;

        public Location At { get; } = at;
    }

    /// <summary>
    /// One entity: where it was declared, and the rectangle of the sheet it covers.
    /// </summary>
    private sealed class EntityBlock
    {
        public RawSheet Sheet = null!;
        public Location Location = null!;
        public string Kind = "";
        public string RawName = "";
        public string Name = "";
        public string Comment = "";
        public TargetSide TargetSide;

        /// <summary>Column the declaration cell sits in, reserved down the entity's height.</summary>
        public int MarkerColumn;

        /// <summary>First and last column of the entity's body, both inclusive.</summary>
        public int FirstColumn;
        public int LastColumn;

        public int DeclarationRow;

        /// <summary>Last row of the entity, inclusive.</summary>
        public int LastRow;

        public Dictionary<string, MetaEntry> Meta = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Row index of each header row, by its key.</summary>
        public Dictionary<string, int> HeaderRows = new(StringComparer.Ordinal);

        /// <summary>
        /// The entity's rows below its headers, in sheet order, with the omitted ones marked.
        /// </summary>
        /// <remarks>
        /// A `#` row is kept rather than dropped, because where it sits decides what it leaves
        /// out. In a multi-row table a `#` on a record's first row takes the whole record and a
        /// `#` on an extension row takes only that row's elements - section 6.1 rule 8 - and a
        /// dropped row cannot say which it was.
        /// </remarks>
        public List<DataRow> Rows = [];

        /// <summary>The rows the conversion reads, which is every row not marked `#`.</summary>
        public IEnumerable<int> DataRows => Rows.Where(r => !r.Omitted).Select(r => r.Row);

        /// <summary>What the declaration cell said, for a report that quotes it.</summary>
        public string Written = "";

        public int Width => LastColumn - FirstColumn + 1;
    }

    private CookingContext _context = null!;

    /// <summary>
    /// What the declaration scan found, kept between the two passes so the sheets are walked
    /// once rather than once per entity kind.
    /// </summary>
    private List<EntityBlock> _blocks = [];

    private Model Model => _context.Model;

    public void ParseDeclarations(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;
        _blocks = ScanBlocks(sheets);

        // Enums and constant sets first, and in that order: a constant may be typed with an
        // enum, and a table may be typed with either.
        foreach (var block in _blocks.Where(b => b.Kind == KindEnum))
            Model.Enums.Add(ParseEnum(block));

        foreach (var block in _blocks.Where(b => b.Kind == KindConst))
            Model.ConstantSets.Add(ParseConstantSet(block));
    }

    public void ParseTables(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;

        foreach (var block in _blocks.Where(b => b.Kind == KindTable))
            Model.Tables.Add(ParseTable(block));
    }

    #region Finding the entities - spec section 3.1 and 3.2

    /// <summary>
    /// Every declaration cell on every sheet, with the rectangle each one covers.
    /// </summary>
    private List<EntityBlock> ScanBlocks(IReadOnlyList<RawSheet> sheets)
    {
        var blocks = new List<EntityBlock>();

        foreach (var sheet in sheets)
        {
            // Found before any is measured. A declaration's width runs to the next marker
            // column, so the columns of the sheet's other declarations are part of the
            // question - which means they all have to be in hand first.
            var found = DeclarationCells(sheet);

            foreach (var (row, column, kind, written) in found)
            {
                var cell = sheet.Rows[row][column];
                var block = ReadDeclaration(sheet, cell, kind, written, row, column);

                Measure(block, sheet, found);
                ReadMarkerColumn(block);

                foreach (var earlier in blocks)
                {
                    // Whatever their kind: the generated code has one name for an entity, so
                    // an enum and a table cannot both be `Item`.
                    if (earlier.Name == block.Name)
                    {
                        throw new TabbitException(cell.Location,
                            Message.Of(TabbitLayoutMessages.EntityNameDuplicated,
                                ("Name", block.Name), ("Kind", block.Kind)));
                    }
                }

                blocks.Add(block);
            }
        }

        return blocks;
    }

    private static List<(int Row, int Column, string Kind, string Written)> DeclarationCells(
        RawSheet sheet)
    {
        var found = new List<(int, int, string, string)>();

        for (int row = 0; row < sheet.Rows.Count; row++)
        {
            var cells = sheet.Rows[row];

            for (int column = 0; column < cells.Count; column++)
            {
                if (DeclaredKindOf(cells[column].Value) is { } kind)
                    found.Add((row, column, kind, cells[column].Value.Trim()));
            }
        }

        return found;
    }

    /// <summary>
    /// The kind a cell declares, or null when it declares none.
    /// </summary>
    /// <remarks>
    /// The keyword has to end the word rather than merely begin the cell, so a table named
    /// `:tableize` is not read as `:table` followed by a name. A keyword with nothing after it
    /// is a declaration with no name, which is reported where the name is read rather than
    /// passed over here - the author wrote `:table` and meant to declare one.
    /// </remarks>
    private static string? DeclaredKindOf(string value)
    {
        string text = (value ?? "").Trim();

        foreach (string kind in new[] { KindTable, KindEnum, KindConst })
        {
            string keyword = ":" + kind;

            if (!text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                continue;

            if (text.Length == keyword.Length)
                return kind;

            char next = text[keyword.Length];
            if (char.IsWhiteSpace(next) || next == '(')
                return kind;
        }

        return null;
    }

    /// <summary>
    /// Reads `:table Item(side=s)` and the description beside it.
    /// </summary>
    private EntityBlock ReadDeclaration(
        RawSheet sheet, RawCell cell, string kind, string written, int row, int column)
    {
        string rest = written.Substring(kind.Length + 1).Trim();

        var meta = new Dictionary<string, MetaEntry>(StringComparer.OrdinalIgnoreCase);

        // **Everything from the first `(` is meta.** The same one rule as a type cell's, so
        // there is one place a bracket can start meaning something and one thing it means.
        int open = rest.IndexOf('(');
        if (open >= 0)
        {
            if (!rest.EndsWith(")", StringComparison.Ordinal))
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.DeclarationMetaUnclosed, ("Written", written)));
            }

            ReadDeclarationMeta(
                rest.Substring(open + 1, rest.Length - open - 2), kind, written, cell, meta);

            rest = rest.Substring(0, open).Trim();
        }

        if (rest.Length == 0)
        {
            throw new TabbitException(cell.Location,
                Message.Of(TabbitLayoutMessages.DeclarationNeedsName,
                    ("Written", written), ("Kind", kind)));
        }

        string name = rest.ToPascalCase();
        _context.RequiresIdentifier(name, cell.Location);

        var cells = sheet.Rows[row];

        return new EntityBlock
        {
            Sheet = sheet,
            Location = cell.Location,
            Kind = kind,
            RawName = rest,
            Name = name,
            Written = written,

            // The cell to the right, which is blank for an entity with no description.
            Comment = column + 1 < cells.Count ? cells[column + 1].Value.Trim() : "",

            TargetSide = _context.ParseTargetSide(
                NormalizeTargetSide(meta.TryGetValue("side", out var side) ? side.Value : ""),
                meta.TryGetValue("side", out var at) ? at.At : cell.Location),

            MarkerColumn = column,
            FirstColumn = column + 1,
            DeclarationRow = row,
            Meta = meta,
        };
    }

    private static void ReadDeclarationMeta(
        string inside, string kind, string written, RawCell cell,
        Dictionary<string, MetaEntry> into)
    {
        foreach (string part in SplitMeta(inside))
        {
            int equals = part.IndexOf('=');
            string key = (equals < 0 ? part : part.Substring(0, equals)).Trim();
            string? value = equals < 0 ? null : Unquote(part.Substring(equals + 1).Trim());

            if (key.Length == 0)
                continue;

            if (!DeclarationMetaKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.DeclarationMetaKeyUnknown,
                        ("Written", written), ("Key", key), ("Kind", kind),
                        ("Known", string.Join(", ", DeclarationMetaKeys))));
            }

            if (into.ContainsKey(key))
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.DeclarationMetaKeyRepeated,
                        ("Written", written), ("Key", key)));
            }

            if (kind != KindTable && TableOnlyMetaKeys.TryGetValue(key, out string? because))
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.DeclarationMetaKeyNotOnKind,
                        ("Written", written), ("Key", key), ("Kind", kind),
                        ("Because", because)));
            }

            // Both keys this layout defines take a value, so a bare one is a mistake rather
            // than a flag. Reported with an example of the key that was actually written.
            if (value is null || value.Length == 0)
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.DeclarationMetaValueMissing,
                        ("Written", written), ("Key", key),
                        ("Example", key.ToLowerInvariant() == "side" ? "side=s" : "key=code")));
            }

            into[key] = new MetaEntry(value, cell.Location);
        }
    }

    /// <summary>
    /// Reads a type cell's brackets into the pairs the shared applier takes.
    /// </summary>
    /// <remarks>
    /// **This layout defines no keys of its own.** The dictionary is the declaration notation's
    /// - `SchemaMetadata` - so a key means the same thing wherever it is written, a typo is a
    /// typo in both, and a key this build does not carry yet says so rather than being ignored.
    /// Section 4.2 of the spec makes that the rule and this is the whole of keeping it.
    ///
    /// The splitting is the declaration's too: a comma separates entries, so a value holding one
    /// is quoted.
    /// </remarks>
    private static Schema.SchemaMeta ReadColumnMeta(string inside, Location at)
    {
        var entries = new List<Schema.SchemaMetaEntry>();

        foreach (string part in SplitMeta(inside))
        {
            int equals = part.IndexOf('=');
            string key = (equals < 0 ? part : part.Substring(0, equals)).Trim();

            if (key.Length == 0)
                continue;

            // A flag is a key with no value, which is how `(text)` and `(notDefault)` are
            // written. Null rather than empty, because `(text=)` is a name somebody meant to
            // write and left out - a different mistake, and the applier reports it as one.
            string? value = equals < 0 ? null : Unquote(part.Substring(equals + 1).Trim());

            entries.Add(new Schema.SchemaMetaEntry(key, value, at));
        }

        return new Schema.SchemaMeta(entries);
    }

    /// <summary>
    /// Splits `a=1, b="x,y"` on the commas that separate entries rather than on every comma.
    /// </summary>
    /// <remarks>
    /// A comma is the entry separator, so a value holding one is quoted - which is the DSL's
    /// rule, written here because the two notations use the same brackets and a reader who has
    /// learned one should not have to learn the other.
    /// </remarks>
    private static List<string> SplitMeta(string inside)
    {
        var parts = new List<string>();
        var built = new System.Text.StringBuilder();
        bool quoted = false;

        foreach (char here in inside)
        {
            if (here == '"')
            {
                quoted = !quoted;
                built.Append(here);
                continue;
            }

            if (here == ',' && !quoted)
            {
                parts.Add(built.ToString());
                built.Clear();
                continue;
            }

            built.Append(here);
        }

        parts.Add(built.ToString());

        return parts.Where(part => part.Trim().Length > 0).ToList();
    }

    private static string Unquote(string value)
        => value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal)
                             && value.EndsWith("\"", StringComparison.Ordinal)
            ? value.Substring(1, value.Length - 2)
            : value;

    /// <summary>
    /// Turns the comma list this layout writes into the spelling the core recognizes.
    /// </summary>
    /// <remarks>
    /// `c,s` is the notation, and section 3.4 keeps `cs` as a second spelling of it because
    /// every recipe and command line already writes that one. Order does not matter - a list
    /// naming both sides means both, whichever end it starts from.
    /// </remarks>
    private static string NormalizeTargetSide(string written)
    {
        var sides = written.Split(',')
            .Select(part => part.Trim().ToLowerInvariant())
            .Where(part => part.Length > 0)
            .ToList();

        // `cs` written together is one part rather than two, and means the same thing.
        if (sides.Count == 1 && sides[0] == "cs")
            return "cs";

        bool client = sides.Contains("c");
        bool server = sides.Contains("s");

        if (client && server)
            return "cs";

        if (client)
            return "c";

        if (server)
            return "s";

        // Anything else is handed on as written, so the core's own report names it rather than
        // this method quietly turning a typo into "both sides".
        return string.Join(",", sides);
    }

    /// <summary>
    /// Works out the rectangle an entity covers - section 3.2.
    /// </summary>
    /// <remarks>
    /// In the order the two axes depend on each other. A blank row is only blank across the
    /// entity's own columns, so the width has to be settled first; and the width runs to the
    /// next marker column, which is only a boundary where it overlaps this entity vertically.
    ///
    /// The circle is cut by measuring the height twice. The first pass looks only at the marker
    /// column, where another declaration ends this entity whatever the width turns out to be,
    /// and that gives a height no blank row can extend. The width is settled against the
    /// declarations inside that span, and the second pass then finds the blank row.
    /// </remarks>
    private static void Measure(
        EntityBlock block, RawSheet sheet,
        List<(int Row, int Column, string Kind, string Written)> found)
    {
        int lastRowOfSheet = sheet.Rows.Count - 1;

        int furthest = lastRowOfSheet;
        foreach (var (row, column, _, _) in found)
        {
            if (column == block.MarkerColumn && row > block.DeclarationRow)
                furthest = Math.Min(furthest, row - 1);
        }

        int lastColumn = sheet.ColumnCount - 1;
        foreach (var (row, column, _, _) in found)
        {
            if (column > block.MarkerColumn && row >= block.DeclarationRow && row <= furthest)
                lastColumn = Math.Min(lastColumn, column - 1);
        }

        block.LastColumn = lastColumn;

        int lastRow = furthest;
        for (int row = block.DeclarationRow + 1; row <= furthest; row++)
        {
            if (IsBlankAcross(sheet, row, block.MarkerColumn, block.LastColumn))
            {
                lastRow = row - 1;
                break;
            }
        }

        block.LastRow = lastRow;
    }

    /// <summary>
    /// Whether a row holds nothing between two columns, both included.
    /// </summary>
    /// <remarks>
    /// The marker column and the memo columns are inside the span on purpose - section 3.2. A
    /// row that holds only a memo is not blank, so a note under a table joins the table unless
    /// a blank row is left between them, and a row whose marker column says `#` keeps the
    /// entity going.
    /// </remarks>
    private static bool IsBlankAcross(RawSheet sheet, int row, int from, int to)
    {
        var cells = sheet.Rows[row];

        for (int column = from; column <= to && column < cells.Count; column++)
        {
            if (cells[column].Value.Length > 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the marker column down the entity: which rows are headers, and which are data.
    /// </summary>
    private void ReadMarkerColumn(EntityBlock block)
    {
        for (int row = block.DeclarationRow + 1; row <= block.LastRow; row++)
        {
            var cells = block.Sheet.Rows[row];
            var marker = block.MarkerColumn < cells.Count ? cells[block.MarkerColumn] : null;
            string value = (marker?.Value ?? "").Trim();

            if (value.Length == 0)
            {
                block.Rows.Add(new DataRow(row, omitted: false));
                continue;
            }

            // A row left out of the conversion. Kept in the list and marked, because where it
            // sits is what it means - and not counted as data, so it can also be the blank line
            // somebody wanted between two groups of rows.
            if (value == OmitMark || value == OmitMarkAlternate)
            {
                block.Rows.Add(new DataRow(row, omitted: true));
                continue;
            }

            string key = value.ToLowerInvariant();

            if (!AllRowKeys.Contains(key, StringComparer.Ordinal))
            {
                throw new TabbitException(marker!.Location,
                    Message.Of(TabbitLayoutMessages.MarkerColumnUnknown,
                        ("Entity", block.Name), ("Written", value),
                        ("Keys", string.Join(" · ", AllRowKeys))));
            }

            if (block.HeaderRows.ContainsKey(key))
            {
                throw new TabbitException(marker!.Location,
                    Message.Of(TabbitLayoutMessages.RowKeyRepeated,
                        ("Entity", block.Name), ("Key", key)));
            }

            // **The report a sorted sheet earns.** Sorting a sheet with the header rows inside
            // the selection scatters them through the data, and this is where that shows: a
            // header row below a row of data. Reported at the row that moved.
            if (block.Rows.Any(r => !r.Omitted))
            {
                throw new TabbitException(marker!.Location,
                    Message.Of(TabbitLayoutMessages.RowKeyBelowData,
                        ("Entity", block.Name), ("Key", key)));
            }

            block.HeaderRows[key] = row;
        }

        RequireHeaderRows(block);
    }

    private void RequireHeaderRows(EntityBlock block)
    {
        if (!block.HeaderRows.ContainsKey(RowKeyField))
        {
            throw new TabbitException(block.Location,
                Message.Of(TabbitLayoutMessages.FieldRowMissing, ("Entity", block.Name)));
        }

        if (block.Kind == KindTable)
        {
            if (!block.HeaderRows.ContainsKey(RowKeyType))
            {
                throw new TabbitException(block.Location,
                    Message.Of(TabbitLayoutMessages.TypeRowMissing, ("Entity", block.Name)));
            }

            return;
        }

        // An enum and a constant set have fixed columns, named in `:field`. A type row would
        // have nothing to say, and a `:target` row would say it per column where the entity
        // already says it once.
        if (block.HeaderRows.ContainsKey(RowKeyType))
        {
            throw new TabbitException(
                block.Sheet.Rows[block.HeaderRows[RowKeyType]][block.MarkerColumn].Location,
                Message.Of(TabbitLayoutMessages.TypeRowNotOnEntity,
                    ("Entity", block.Name), ("Kind", block.Kind)));
        }

        foreach (var (key, row) in block.HeaderRows)
        {
            if (EntityRowKeys.Contains(key, StringComparer.Ordinal))
                continue;

            throw new TabbitException(
                block.Sheet.Rows[row][block.MarkerColumn].Location,
                Message.Of(TabbitLayoutMessages.RowKeyNotOnEntity,
                    ("Entity", block.Name), ("Kind", block.Kind), ("Key", key),
                    ("Keys", string.Join(" · ", EntityRowKeys))));
        }
    }

    #endregion

    #region Columns - spec sections 3.3 and 5

    /// <summary>What one column of an entity turned out to be.</summary>
    private sealed class ColumnHeader
    {
        public int Column;
        public RawCell NameCell = null!;

        /// <summary>The name as written, less the `*`, the `@N` and any leading `#`.</summary>
        public string Written = "";

        /// <summary>Levels of the path, or null for a column that is one plain field.</summary>
        public List<FieldPathStep>? Path;

        public bool IsMemo;
        public bool IsTombstone;
        public bool Indexing;
        public int? WireTag;
        public bool HoldsArray;

        /// <summary>Whether the `:type` cell was blank, which is a statement of its own.</summary>
        public bool TypeWasBlank;

        /// <summary>What the type cell wrote after the first `(`, or nothing.</summary>
        public Schema.SchemaMeta Meta = Schema.SchemaMeta.Empty;

        /// <summary>The `:variant` cell, blank for the default column - section 3.6.</summary>
        public string Variant = "";

        /// <summary>
        /// The column the header rows are read from, which is this one unless a variant group
        /// wrote its header once on the default column.
        /// </summary>
        public int? HeaderColumn;

        /// <summary>Where `:type`, `:desc` and `:target` are read for this column.</summary>
        public int HeaderAt => HeaderColumn ?? Column;

        /// <summary>
        /// Which level of the path was written `[]`, or null when none was.
        /// </summary>
        /// <remarks>
        /// That level's elements come from the rows below rather than from columns beside, so
        /// its <see cref="FieldPathStep.Index"/> is left unset until the records are grouped
        /// and the element columns are built - section 6.
        /// </remarks>
        public int? MultiRowLevel;

        /// <summary>Whether this column takes its elements from the rows below.</summary>
        public bool IsMultiRow => MultiRowLevel is not null;

        /// <summary>
        /// Whether this is the first column of its group, which is the one that states the type.
        /// </summary>
        public bool IsGroupFirst;

        /// <summary>
        /// The path down to and including the `[]` level, which names the group.
        /// </summary>
        public string MultiRowGroup
            => string.Join(".", Path!.Take(MultiRowLevel!.Value + 1).Select(step => step.Name));
    }

    /// <summary>
    /// Reads the `:field` row into one header per column of the entity's body.
    /// </summary>
    private List<ColumnHeader> ReadColumns(EntityBlock block)
    {
        var fieldRow = block.Sheet.Rows[block.HeaderRows[RowKeyField]];
        var headers = new List<ColumnHeader>();

        for (int column = block.FirstColumn; column <= block.LastColumn; column++)
        {
            var cell = column < fieldRow.Count ? fieldRow[column] : null;
            string written = (cell?.Value ?? "").Trim();

            if (cell is null)
                continue;

            // **A column with no name is a column with no place.** Left silent, whatever its
            // data cells hold would be dropped, which is the failure this tool exists to
            // prevent - so the data is what makes it a report.
            if (written.Length == 0)
            {
                RefuseDataUnderUnnamedColumn(block, column);
                continue;
            }

            // `#` on its own is space for the sheet's author. Anything else behind a `#` is a
            // field that was taken out, and its wire tag stays reserved.
            if (written == OmitMark)
            {
                headers.Add(new ColumnHeader
                {
                    Column = column, NameCell = cell, Written = written, IsMemo = true,
                });
                continue;
            }

            if (ReservedColumnNames.Contains(written, StringComparer.OrdinalIgnoreCase))
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.ReservedColumnNotYetSupported,
                        ("Entity", block.Name), ("Column", written),
                        ("Reserved", string.Join(" · ", ReservedColumnNames))));
            }

            var header = new ColumnHeader { Column = column, NameCell = cell };

            if (written.StartsWith(OmitMark, StringComparison.Ordinal)
                || written.StartsWith(OmitMarkAlternate, StringComparison.Ordinal))
            {
                header.IsTombstone = true;
                written = written.TrimStart('#', '/').Trim();
            }

            (written, header.WireTag) = SplitWireTag(written, cell.Location);

            written = SplitIndexMark(written, block, cell, header);

            header.Written = written;

            if (header.IsTombstone)
            {
                headers.Add(header);
                continue;
            }

            header.Path = ReadPath(written, block, cell, header);

            headers.Add(header);
        }

        MarkFirstOfEachGroup(headers);

        return headers;
    }

    /// <summary>
    /// Marks the leftmost column of every group, which is where its type is written.
    /// </summary>
    /// <remarks>
    /// By position rather than by element number: a group's type is stated once, and which
    /// column says it is whichever comes first in the sheet. Reading it as "every level
    /// numbered zero" made the second member of element zero state a type too, which is exactly
    /// what the declared-struct notation leaves blank.
    /// </remarks>
    private static void MarkFirstOfEachGroup(List<ColumnHeader> headers)
    {
        var firsts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var header in headers)
        {
            if (header.Path is null || header.IsMemo || header.IsTombstone)
                continue;

            string group = header.Path[0].Name;

            if (!firsts.TryGetValue(group, out int at) || header.Column < at)
                firsts[group] = header.Column;
        }

        foreach (var header in headers)
        {
            header.IsGroupFirst = header.Path is not null
                                  && firsts.TryGetValue(header.Path[0].Name, out int at)
                                  && header.Column == at;
        }
    }

    /// <summary>
    /// Reports the first value written under a column the `:field` row left unnamed.
    /// </summary>
    private void RefuseDataUnderUnnamedColumn(EntityBlock block, int column)
    {
        foreach (int row in block.DataRows)
        {
            var cells = block.Sheet.Rows[row];
            if (column >= cells.Count)
                continue;

            var cell = cells[column];
            if (cell.Value.Length == 0)
                continue;

            throw new TabbitException(cell.Location,
                Message.Of(TabbitLayoutMessages.ColumnUnnamedWithData,
                    ("Entity", block.Name), ("Column", Location.ColumnName(column)),
                    ("Value", cell.Value)));
        }
    }

    /// <summary>Takes `@3` off the end of a name.</summary>
    private (string Name, int? Tag) SplitWireTag(string written, Location location)
    {
        int at = written.LastIndexOf('@');
        if (at < 0)
            return (written, null);

        string digits = written.Substring(at + 1).Trim();

        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag)
            || tag < 1)
        {
            throw new TabbitException(location,
                Message.Of(TabbitLayoutMessages.PathProblem,
                    ("Entity", ""), ("Column", written),
                    ("Detail", $"has `@{digits}` where a wire tag belongs, and a tag is a whole number from 1.")));
        }

        return (written.Substring(0, at).Trim(), tag);
    }

    /// <summary>Takes the `*` secondary-index mark off the front of a name.</summary>
    private string SplitIndexMark(
        string written, EntityBlock block, RawCell cell, ColumnHeader header)
    {
        if (!written.StartsWith("*", StringComparison.Ordinal))
            return written;

        string rest = written.Substring(1).Trim();

        // Exactly one. Stripping every `*` would accept `**code` as a typo for `*code`, and
        // leaving the extras in place reports an invalid identifier - the symptom rather than
        // the mistake.
        if (rest.StartsWith("*", StringComparison.Ordinal))
        {
            throw new TabbitException(cell.Location,
                Message.Of(TabbitLayoutMessages.RepeatedIndexMark,
                    ("Entity", block.Name), ("Column", written)));
        }

        header.Indexing = true;

        return rest;
    }

    /// <summary>
    /// Reads a column path - `id`, `pos.x`, `slots[0].id`, `tags[0]`, `costs[]`.
    /// </summary>
    /// <remarks>
    /// The path is where this layout's notation and the model meet:
    /// <see cref="FieldPathStep"/> already holds "what this level is called" and "which element
    /// of it", which is everything the brackets and the dots say. So nothing below this method
    /// knows which notation wrote a column.
    ///
    /// Null for a column that is one plain field, because that is what the model means by a
    /// field with no path.
    /// </remarks>
    private List<FieldPathStep>? ReadPath(
        string written, EntityBlock block, RawCell cell, ColumnHeader header)
    {
        var steps = new List<FieldPathStep>();
        bool anyBrackets = false;

        foreach (string part in written.Split('.'))
        {
            string text = part.Trim();

            if (text.Length == 0)
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.PathProblem,
                        ("Entity", block.Name), ("Column", written),
                        ("Detail", "has an empty level. A `.` separates one level of the path from the next, so there is a name missing on one side of it.")));
            }

            int open = text.IndexOf('[');

            if (open < 0)
            {
                steps.Add(new FieldPathStep { Name = text.ToPascalCase(), Index = null });
                continue;
            }

            anyBrackets = true;

            string name = text.Substring(0, open).Trim();

            if (name.Length == 0)
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.PathProblem,
                        ("Entity", block.Name), ("Column", written),
                        ("Detail", "has brackets with no name in front of them. A level with no name of its own is written by putting its brackets straight after the level above - `grid[0][1]` - so that one shape has one spelling.")));
            }

            // **A run of brackets is a run of levels.** `grid[0][1]` is element 1 of element 0
            // of `grid`, and the inner level has no name - which is the whole content of an
            // array of arrays: there is no word a consumer could write, so it indexes instead.
            // Section 5, and spec/nested-multi-level.md for what the shape reaches.
            string levelName = name.ToPascalCase();

            foreach (string digits in BracketGroups(text.Substring(open), block, written, cell))
            {
                if (digits.Length == 0)
                {
                    // `[]` says the elements come from the rows below rather than from columns
                    // beside. The level repeats and which element it holds is the row's answer,
                    // so it is left unnumbered here and numbered once the records are grouped.
                    if (header.MultiRowLevel is not null)
                    {
                        throw new TabbitException(cell.Location,
                            Message.Of(TabbitLayoutMessages.MultiRowNested,
                                ("Entity", block.Name), ("Column", written)));
                    }

                    header.MultiRowLevel = steps.Count;
                    steps.Add(new FieldPathStep { Name = levelName, Index = null });
                }
                else
                {
                    if (!int.TryParse(
                            digits, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out int number)
                        || number < 0)
                    {
                        throw new TabbitException(cell.Location,
                            Message.Of(TabbitLayoutMessages.ElementNumberNotInteger,
                                ("Entity", block.Name), ("Column", written), ("Written", digits)));
                    }

                    steps.Add(new FieldPathStep { Name = levelName, Index = number });
                }

                // Every bracket after the first belongs to a level the sheet did not name.
                levelName = "";
            }
        }


        if (header.Indexing && anyBrackets)
        {
            throw new TabbitException(cell.Location,
                Message.Of(TabbitLayoutMessages.IndexMarkOnArrayColumn,
                    ("Entity", block.Name), ("Column", written)));
        }

        if (header.Indexing && steps.Count > 1)
        {
            throw new TabbitException(cell.Location,
                Message.Of(TabbitLayoutMessages.IndexMarkOnGroupMember,
                    ("Entity", block.Name), ("Column", written), ("Group", steps[0].Name)));
        }

        // A nameless level has no identifier to hold to one: it is reached by number, which is
        // what having no name means here.
        foreach (var step in steps.Where(step => !step.IsAnonymous))
            _context.RequiresIdentifier(step.Name, cell.Location);

        // A `[]` level beside a numbered one replicates the whole column set per element, which
        // is the shape section 6.3 refuses along with nested multi-row. Refused by name rather
        // than half-read, so the notation stays what the document says it is.
        if (header.IsMultiRow && steps.Any(step => step.IsIndexed))
        {
            throw new TabbitException(cell.Location,
                Message.Of(TabbitLayoutMessages.MultiRowNested,
                    ("Entity", block.Name), ("Column", written)));
        }

        // One level, named, neither numbered nor `[]`: a plain field, which the model spells as
        // no path at all rather than as a path of one.
        if (steps.Count == 1 && !steps[0].IsIndexed && !header.IsMultiRow)
            return null;

        return steps;
    }

    /// <summary>
    /// Splits a run of brackets into what each one held: `[0][1]` gives `0` then `1`.
    /// </summary>
    /// <remarks>
    /// Empty for `[]`, which is a level whose elements come from the rows rather than a level
    /// with no number. The caller tells the two apart because they mean different things.
    /// </remarks>
    private static List<string> BracketGroups(
        string text, EntityBlock block, string written, RawCell cell)
    {
        var groups = new List<string>();
        int at = 0;

        while (at < text.Length)
        {
            if (text[at] != '[')
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.PathProblem,
                        ("Entity", block.Name), ("Column", written),
                        ("Detail", $"has `{text.Substring(at)}` after a closing bracket. Brackets run one after another - `grid[0][1]` - and a name goes before them, not between them.")));
            }

            int close = text.IndexOf(']', at + 1);

            if (close < 0)
            {
                throw new TabbitException(cell.Location,
                    Message.Of(TabbitLayoutMessages.PathProblem,
                        ("Entity", block.Name), ("Column", written),
                        ("Detail", "opens a bracket and does not close it. An element number is written `slots[0]`, and `[]` on its own puts the elements on the rows below.")));
            }

            groups.Add(text.Substring(at + 1, close - at - 1).Trim());
            at = close + 1;
        }

        return groups;
    }

    /// <summary>
    /// Checks that every numbered group counts from zero without a gap - section 5.
    /// </summary>
    /// <remarks>
    /// Asked of the group rather than of a column, because a number is only right or wrong
    /// beside the others in its group. Excel numbers its rows from one and the layout this one
    /// replaces numbered `Slot1` from one, so starting at `[1]` is the mistake to expect - and
    /// it is named as one instead of quietly producing an array with an empty first element.
    /// </remarks>
    private void RequireElementNumbering(EntityBlock block, List<ColumnHeader> headers)
    {
        var byGroup = new Dictionary<string, (SortedSet<int> Numbers, RawCell At)>(
            StringComparer.Ordinal);

        foreach (var header in headers)
        {
            if (header.Path is null || header.IsMemo || header.IsTombstone)
                continue;

            for (int level = 0; level < header.Path.Count; level++)
            {
                if (header.Path[level].Index is not { } number)
                    continue;

                // Keyed by the path down to the numbered level **with the outer numbers kept**,
                // so `grid[0][…]` and `grid[1][…]` are two runs rather than one - each inner
                // array counts from zero on its own. The level itself contributes its name only,
                // which is what makes `stars[0].pos` and `stars[1].pos` one group.
                string key = string.Join(
                    ".",
                    header.Path.Take(level).Select(step => step.ToString())
                        .Append(header.Path[level].Name));

                if (!byGroup.TryGetValue(key, out var group))
                    group = (new SortedSet<int>(), header.NameCell);

                group.Numbers.Add(number);
                byGroup[key] = group;
            }
        }

        foreach (var (group, (numbers, at)) in byGroup)
        {
            if (numbers.Min != 0)
            {
                throw new TabbitException(at.Location,
                    Message.Of(TabbitLayoutMessages.ElementNumbersNotFromZero,
                        ("Entity", block.Name), ("Group", group), ("First", numbers.Min)));
            }

            for (int expected = 0; expected < numbers.Count; expected++)
            {
                if (numbers.Contains(expected))
                    continue;

                throw new TabbitException(at.Location,
                    Message.Of(TabbitLayoutMessages.ElementNumbersNotConsecutive,
                        ("Entity", block.Name), ("Group", group),
                        ("Present", numbers.Max), ("Missing", expected)));
            }
        }
    }

    #endregion

    #region Tables

    /// <summary>
    /// Reads `extends=Reward` and checks that the name is an abstract struct.
    /// </summary>
    /// <remarks>
    /// Checked here rather than left to the pass that expands `foreign Reward`, because the
    /// mistake is in this cell: a table naming a set that is not one has written something
    /// wrong whether or not any column ever points at it.
    ///
    /// **What fills the set is not checked here.** Whether the struct's fields are all present
    /// as columns is a question about a table that may not have been read yet, so the model
    /// pass that has every table asks it. spec/polymorphism.md section 3.
    /// </remarks>
    private string? ReadVariantSet(EntityBlock block)
    {
        if (!block.Meta.TryGetValue("extends", out var extends))
            return null;

        string name = extends.Value.ToPascalCase();

        if (_context.Declarations?.FindAbstract(name) is null)
        {
            throw new TabbitException(extends.At,
                Message.Of(TabbitLayoutMessages.ExtendsNotAbstract,
                    ("Table", block.Name), ("Written", extends.Value)));
        }

        return name;
    }

    private Models.Table ParseTable(EntityBlock block)
    {
        Log.Information($"Parsing table `{block.Name}`. ({block.Location})");

        var table = new Models.Table
        {
            Location = block.Location,
            TargetSide = block.TargetSide,
            RawName = block.RawName,
            Name = block.Name,
            Comment = block.Comment,

            VariantOf = ReadVariantSet(block),
            VariantOfLocation = block.Meta.TryGetValue("extends", out var extends) ? extends.At : null,


            TrimTrailingArrayElements = block.Sheet.Layout.TrimTrailingArrayElements,
            AllowArrayGaps = block.Sheet.Layout.AllowArrayGaps,
        };

        var headers = SelectVariants(block, ReadColumns(block));
        RequireElementNumbering(block, headers);

        // **One `[]` column puts the whole table in multi-row mode** - section 6.1 rule 1. What
        // changes is where a record ends and where an array's elements come from; every other
        // rule of the notation is the same.
        var records = Records(block, headers);

        if (records is not null)
        {
            // The array's length is the record's rather than the table's, and trimming is how
            // the model says that: `ElementCountIn` counts back to the last element the sheet
            // gave a value for. **Not read from the source entry** - a multi-row table trims
            // whatever a recipe says, because its elements are the rows that exist.
            table.TrimTrailingArrayElements = true;
        }

        var sources = ParseFields(table, block, headers, records);

        if (sources.Count == 0)
        {
            throw new TabbitException(block.Location,
                Message.Of(TabbitLayoutMessages.NoFieldColumns, ("Entity", block.Name)));
        }

        InheritTypesFromElementZero(table, sources);
        ApplyKeyMeta(table, block, sources);

        // **A multi-row record begins where the primary key's cell has a value**, and a
        // combination spread over several columns has no such cell - the first component
        // repeats across records by design, so its blankness says nothing about where one
        // record ends. Refused rather than read under a guess. Section 6.1.
        if (records is not null
            && table.Keys.Find(key => key.IsPrimary) is { IsComposite: true } spread)
        {
            throw new TabbitException(block.Location,
                Message.Of(TabbitLayoutMessages.CompositeKeyMultiRow,
                    ("Entity", block.Name), ("Key", spread.ToString())));
        }

        // Grouped before the cells are read, because grouping is what gives every element of
        // an array the first one's answer about being optional, and reading a cell asks it.
        _ = table.SerialFields;

        if (records is null)
            ParseData(table, block, sources);
        else
            ParseMultiRowData(table, block, sources, records);

        _context.CheckPrimaryIndexValidity(table.PrimaryIndexField!);
        _context.AssignTags(table);

        return table;
    }

    /// <summary>
    /// The column a field came from, and which element of it - null when it is not one.
    /// </summary>
    /// <remarks>
    /// One header becomes several fields in multi-row mode: the sheet has one column per member
    /// and the model has one per element. This is the map back, so reading a cell knows which
    /// row of the record to take it from.
    /// </remarks>
    private sealed class FieldSource
    {
        public ColumnHeader Header = null!;

        /// <summary>Which element of the `[]` group, or null for an ordinary column.</summary>
        public int? Element;
    }

    /// <summary>
    /// Builds a field per column that carries one, and returns the columns in field order.
    /// </summary>
    /// <remarks>
    /// A `[]` group is expanded where its first member column sits, element-major - so the
    /// fields come out in the order the same table written with numbered columns would produce
    /// them. That order is the wire's, which is what lets the two spellings reach one file.
    /// </remarks>
    private List<FieldSource> ParseFields(
        Models.Table table, EntityBlock block, List<ColumnHeader> headers,
        List<Record>? records)
    {
        var typeRow = block.Sheet.Rows[block.HeaderRows[RowKeyType]];
        var descRow = RowOrNull(block, RowKeyDesc);
        var targetRow = RowOrNull(block, RowKeyTarget);

        var sources = new List<FieldSource>();
        var expanded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var header in headers)
        {
            if (header.IsMemo)
                continue;

            if (header.IsTombstone)
            {
                // The column is gone from the model and its tag stays reserved, so no later
                // field can be handed a number that already identified other data.
                if (header.WireTag is { } reserved)
                    table.ReservedTags.Add(reserved);

                continue;
            }

            if (!header.IsMultiRow)
            {
                AddField(table, block, header, null, header.Path,
                    typeRow, descRow, targetRow, sources);
                continue;
            }

            // The group as a whole, at the position of whichever of its members comes first.
            string group = header.MultiRowGroup;
            if (!expanded.Add(group))
                continue;

            var members = headers
                .Where(other => other.IsMultiRow && !other.IsMemo && !other.IsTombstone
                                && other.MultiRowGroup == group)
                .ToList();

            int elements = ElementColumnsFor(group, records!);

            for (int element = 0; element < elements; element++)
            {
                foreach (var member in members)
                {
                    var path = member.Path!
                        .Select(step => new FieldPathStep { Name = step.Name, Index = step.Index })
                        .ToList();

                    path[member.MultiRowLevel!.Value].Index = element;

                    AddField(table, block, member, element, path,
                        typeRow, descRow, targetRow, sources);
                }
            }
        }

        return sources;
    }

    /// <summary>
    /// How many element columns a `[]` group needs: as many as its longest record.
    /// </summary>
    /// <remarks>
    /// One at least, even where no record filled the group. Zero columns would take the member
    /// out of the model altogether, and "every row has none of these" is an empty array rather
    /// than an absent field.
    /// </remarks>
    private static int ElementColumnsFor(string group, List<Record> records)
    {
        int longest = 0;

        foreach (var record in records)
        {
            if (record.Elements.TryGetValue(group, out var rows) && rows.Count > longest)
                longest = rows.Count;
        }

        return System.Math.Max(1, longest);
    }

    /// <summary>Builds one field and records where it came from.</summary>
    private void AddField(
        Models.Table table, EntityBlock block, ColumnHeader header, int? element,
        List<FieldPathStep>? path,
        List<RawCell> typeRow, List<RawCell>? descRow, List<RawCell>? targetRow,
        List<FieldSource> sources)
    {
        var typeCell = Cell(typeRow, header.HeaderAt);
        var descCell = descRow is null ? null : Cell(descRow, header.HeaderAt);
        var targetCell = targetRow is null ? null : Cell(targetRow, header.HeaderAt);

        string name = NameOf(header, path);

        if (table.ContainsField(name))
        {
            throw new TabbitException(header.NameCell.Location,
                Message.Of(TabbitLayoutMessages.ColumnNameDuplicated,
                    ("Entity", block.Name), ("Column", name)));
        }

        var field = new Field
        {
            OwnerTable = table,
            NameLocation = header.NameCell.Location,
            TypeLocation = typeCell?.Location ?? header.NameCell.Location,

            // One cell holds the whole type in this layout, so a report about the type
            // points at it whether it is about the name or about what follows it.
            DetailTypeLocation = typeCell?.Location ?? header.NameCell.Location,

            TargetSideLocation = targetCell?.Location ?? block.Location,
            TargetSide = _context.ParseTargetSide(
                NormalizeTargetSide(targetCell?.Value ?? ""),
                targetCell?.Location ?? block.Location),
            Index = table.Fields.Count,
            Comment = descCell?.Value ?? "",
            RawName = header.Written,
            Name = name,

            // The tag names a wire column, and a member of a multi-row group is one wire
            // column however many elements a record holds - so it goes on element zero and
            // the later elements are the same column seen again.
            Tag = element is null or 0 ? header.WireTag : null,

            NamePath = path,

            // The first field column is the primary index until `key` moves it, which is
            // a step of its own - the declaration refuses that key for now.
            Indexing = table.Fields.Count == 0 || header.Indexing,
        };

        _context.RequiresIdentifier(name, header.NameCell.Location);

        ReadType(field, header, typeCell, block);
        ApplyColumnMeta(table, block, field, header);

        table.Fields.Add(field);
        sources.Add(new FieldSource { Header = header, Element = element });
    }

    /// <summary>
    /// Hands a column's brackets to the applier the declaration notation uses.
    /// </summary>
    /// <remarks>
    /// **Both, where both said something.** A struct declaration says what is true of the type
    /// wherever it is used and a column says what is true of that one column, so a value has to
    /// satisfy each - and the applier narrows rather than replaces, which is why the sheet is
    /// applied after the declaration has bound and can only tighten what it promised. DSL
    /// section 6.3.
    ///
    /// The keys a group's later members leave out are the first member's, like the type - so a
    /// column whose type cell was blank carries no brackets of its own and is left alone here.
    /// </remarks>
    private void ApplyColumnMeta(
        Models.Table table, EntityBlock block, Field field, ColumnHeader header)
    {
        if (header.Meta.Entries.Count == 0)
            return;

        string where = $"{block.Name}.{field.Name}";

        Schema.SchemaMetadata.CheckFieldKeys(header.Meta, where, block.Name, _context.Diagnostics);

        Schema.SchemaMetadata.Apply(
            table, field, header.Meta, where, field.TypeName,
            typeIsArray: Models.ValueTypes.IsArray(field.Type),
            _context.Diagnostics);
    }

    /// <summary>
    /// Hands each later element the type its element zero stated - section 4.3.
    /// </summary>
    /// <remarks>
    /// **The blank has to be filled in rather than merely allowed.** The file stores a record
    /// group's member as one column and states one type for it, so the model holds every
    /// element of a member to the same type - a later element left carrying "no type" is
    /// refused as a group whose member is two types at once.
    ///
    /// A member that has no element zero to inherit from is the other shape a blank writes:
    /// the second member of a group whose first column named a declared struct. That one keeps
    /// waiting, because the type is the declaration's answer and
    /// <see cref="ModelCooker"/> binds it once the declarations are in.
    /// </remarks>
    private void InheritTypesFromElementZero(
        Models.Table table, List<FieldSource> sources)
    {
        for (int at = 0; at < sources.Count; at++)
        {
            if (!sources[at].Header.TypeWasBlank)
                continue;

            var field = table.Fields[at];
            if (field.NamePath is null)
                continue;

            string wanted = MemberKey(field.NamePath);

            for (int other = 0; other < sources.Count; other++)
            {
                if (other == at || sources[other].Header.TypeWasBlank)
                    continue;

                var source = table.Fields[other];

                if (source.NamePath is null
                    || MemberKey(source.NamePath) != wanted
                    || !source.NamePath.All(step => (step.Index ?? 0) == 0))
                {
                    continue;
                }

                // **A declared struct is named once for the whole group.** Copying it would put
                // the name on two columns, and the binder then has two answers to "which struct
                // is this group" where the notation promised one. The later elements stay
                // waiting and the declaration covers them, which is what it is for.
                if (_context.IsDeferredTypeName(source.TypeName))
                    break;

                field.TypeName = source.TypeName;
                field.Type = source.Type;
                field.IsRequired = source.IsRequired;
                field.ElementsRequired = source.ElementsRequired;
                field.Role = source.Role;
                field.RoleGroup = source.RoleGroup;
                field.RoleNamespace = source.RoleNamespace;
                field.RefTableName = source.RefTableName;
                field.RefTableNames = source.RefTableNames;
                field.RefFieldName = source.RefFieldName;

                break;
            }
        }
    }

    /// <summary>
    /// A path with its element numbers left out, so two elements of one member match.
    /// </summary>
    /// <remarks>
    /// Which levels repeat is kept and which element they hold is dropped, so `slot[0].id` and
    /// `slot[1].id` are one member while `slot.id` - a record that does not repeat - is not
    /// mistaken for either.
    /// </remarks>
    private static string MemberKey(IReadOnlyList<FieldPathStep> path)
        => string.Join(".", path.Select(step => step.IsIndexed ? step.Name + "[]" : step.Name));

    /// <summary>
    /// The identifier a column becomes, with the element numbers kept in it.
    /// </summary>
    /// <remarks>
    /// One name rather than a structure, so duplicate detection, lookup and every language's
    /// spelling rules keep working untouched. The path is what a consumer sees - `Slots[0].Id`
    /// - and this is what the tool calls the column while it works.
    /// </remarks>
    private static string NameOf(ColumnHeader header, List<FieldPathStep>? path)
    {
        if (path is null)
            return header.Written.ToPascalCase();

        return string.Concat(path.Select(
            step => step.Name + (step.Index?.ToString(CultureInfo.InvariantCulture) ?? "")));
    }

    /// <summary>
    /// Reads a `:type` cell: the folded type expression - section 4.1.
    /// </summary>
    /// <remarks>
    /// **There is no detail row.** The layout this one replaces wrote a type and a detail as a
    /// pair - `enum` beside `Element`, `foreign` beside `Weapon|Armour` - and the DSL folded
    /// that pair into one expression. This reads the folded one, so a sheet and a declaration
    /// spell a type the same way.
    ///
    /// A blank cell is what a group's later members and an array's later elements write, since
    /// the first column of the group already stated the type. Anywhere else it is a column
    /// with no type, which is reported.
    /// </remarks>
    private void ReadType(Field field, ColumnHeader header, RawCell? typeCell, EntityBlock block)
    {
        string written = (typeCell?.Value ?? "").Trim();
        var at = typeCell?.Location ?? header.NameCell.Location;

        // **Everything from the first `(` is meta** - the one rule, section 4.2. Split off here
        // and applied once the type is resolved, because what a key may say depends on what the
        // column turned out to hold.
        int open = written.IndexOf('(');
        if (open >= 0)
        {
            if (!written.EndsWith(")", StringComparison.Ordinal))
            {
                throw new TabbitException(at,
                    Message.Of(TabbitLayoutMessages.ColumnMetaUnclosed,
                        ("Entity", block.Name), ("Column", field.Name), ("Written", written)));
            }

            header.Meta = ReadColumnMeta(
                written.Substring(open + 1, written.Length - open - 2), at);

            written = written.Substring(0, open).Trim();
        }

        if (written.Length == 0)
        {
            // The blank that is the notation rather than an omission - section 4.3. Two shapes
            // write it and both have a path: a member of a group whose first column named a
            // declared struct, and an element after the first. What each one inherits is
            // settled once every column is in hand, by InheritTypesFromElementZero.
            if (header.Path is not null && !IsFirstOfItsGroup(header))
            {
                header.TypeWasBlank = true;
                field.TypeName = "";
                field.Type = CookingContext.DeferredType;
                field.IsRequired = true;
                field.ElementsRequired = true;
                return;
            }

            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.ColumnTypeMissing,
                    ("Entity", block.Name), ("Column", field.Name)));
        }

        string expression = CookingContext.SplitOptionalMarkers(
            written, out bool required, out bool elementsRequired);

        field.IsRequired = required;
        field.ElementsRequired = elementsRequired;

        bool isArray = expression.EndsWith("[]", StringComparison.Ordinal);
        if (isArray)
            expression = expression.Substring(0, expression.Length - 2).Trim();

        if (!elementsRequired && !isArray)
        {
            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", $"is typed `{written}`, and a `?` inside the brackets says an element may be absent - which a column that is not an array has no element to say it of.")));
        }

        header.HoldsArray = isArray;

        // The name says the elements come from the rows below and the type says they come from
        // inside the cell. Both at once is an array of arrays per row, which section 5.1 refuses
        // for the first pass - and reading it as either one would be picking for the author.
        if (isArray && header.IsMultiRow)
        {
            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.MultiRowCellArray,
                    ("Entity", block.Name), ("Column", field.Name), ("Written", written)));
        }

        if (ReadReferenceType(field, expression, isArray, at, block))
            return;

        // Built-in names are lower case and a declared name keeps the case it was written in,
        // because that is the name an enum or a struct is found by.
        string pascal = expression.ToPascalCase();
        string resolved = Model.ContainsEnum(pascal) ? pascal : expression.ToLowerInvariant();

        if (_context.IsDeferredTypeName(expression))
            resolved = expression;

        field.TypeName = resolved;

        var elementType = _context.ParseValueType(resolved, at);

        if (!isArray)
        {
            field.Type = elementType;
            return;
        }

        var arrayType = Models.ValueTypes.ArrayOf(elementType);
        if (arrayType == Models.ValueType.None)
        {
            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", $"is typed `{written}`, and `{expression}` is not a type this tool puts in an array.")));
        }

        field.Type = arrayType;
    }

    /// <summary>
    /// Reads `foreign Item` and `foreign Item|CEquip`, or answers that this is not one.
    /// </summary>
    private bool ReadReferenceType(
        Field field, string expression, bool isArray, Location at, EntityBlock block)
    {
        const string keyword = "foreign";

        if (!expression.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            return false;

        string rest = expression.Substring(keyword.Length).Trim();

        if (rest.Length == 0)
        {
            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", "is typed `foreign` and names no table. Write the table after it - `foreign Item`, or `foreign Item|CEquip` for a key that is a row of either.")));
        }

        if (isArray)
        {
            // Deliberately unsupported rather than half-supported: a variable number of
            // targets per row is a shape the generated readers have none for, so letting it
            // parse would produce code that silently never resolves.
            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", "is typed as an array of references, which the generated readers have no shape for. Write the references as numbered columns, one reference each.")));
        }

        // Split before casing: a bar is not a word separator, so casing the whole cell would
        // leave the second name as written. Each half is cased on its own for the same reason -
        // a dot is not one either, and `ItemCategory.Name` has to stay two names.
        var written = rest.Split('|')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();

        var names = new List<string>();
        string? member = null;

        foreach (string target in written)
        {
            int dot = target.IndexOf('.');

            if (dot < 0)
            {
                string name = target.ToPascalCase();
                if (!names.Contains(name))
                    names.Add(name);

                continue;
            }

            // **`Table.Field` names a value inside the target** rather than the row - the
            // reference resolves to that field, so the column's type becomes that field's.
            if (written.Count > 1)
            {
                throw new TabbitException(at,
                    Message.Of(TabbitLayoutMessages.MultiTargetNamesAField,
                        ("Entity", block.Name), ("Column", field.Name), ("Written", rest),
                        ("Dotted", target)));
            }

            names.Add(target.Substring(0, dot).ToPascalCase());
            member = target.Substring(dot + 1).ToPascalCase();

            // `Table.Index` names the row's own key, which is the row - so it is cleared and
            // the reference resolves to the record, which is what the writer meant either way.
            if (member.ToLowerInvariant() == "index")
                member = "";
        }

        if (names.Count == 0)
        {
            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", "is typed `foreign` and names no table between its bars.")));
        }

        field.TypeName = "$Unresolved$";

        // Whatever the reference's shape, the cell holds the target's index - and what type
        // that is cannot be known here, because the target may not have been read yet and its
        // key may be a string as readily as an int. So the cell is kept as written and
        // `ModelCooker.ConvertReferenceCells` converts it once resolution has an answer.
        field.Type = Models.ValueType.String;

        field.RefTableNames = names;
        field.RefFieldName = member;

        // Left empty on purpose when there are several, because `IsRef` reads it and has to go
        // on meaning "resolves to exactly one record".
        if (names.Count == 1)
            field.RefTableName = names[0];

        return true;
    }

    /// <summary>
    /// Whether a column is the one its group writes the type in - section 4.3.
    /// </summary>
    /// <remarks>
    /// **The group's very first column**, which is the only one that has to state a type - a
    /// declared struct names itself there once and every other column of the group leaves the
    /// cell blank. What each blank inherits is settled afterwards: an element after the first
    /// takes element zero's, and a member with no element zero to take from waits for the
    /// declaration that covers it.
    ///
    /// A column with no path is its own group of one, so it always states its type.
    /// </remarks>
    private static bool IsFirstOfItsGroup(ColumnHeader header) => header.Path is null || header.IsGroupFirst;

    private void ParseData(Models.Table table, EntityBlock block, List<FieldSource> sources)
    {
        foreach (int rowIndex in block.DataRows)
        {
            var row = new List<Cell>();

            for (int at = 0; at < table.Fields.Count; at++)
            {
                row.Add(ReadCellAt(
                    table, block, table.Fields[at], rowIndex, sources[at].Header.Column));
            }

            table.Data.Add(row);
        }
    }

    /// <summary>
    /// Reads one cell of one row into the value its column's type says it is.
    /// </summary>
    /// <remarks>
    /// Shared by the two ways this layout walks the data - a row per record, or a record per
    /// group of rows - so what a cell says cannot come to depend on which one read it.
    /// </remarks>
    private Cell ReadCellAt(
        Models.Table table, EntityBlock block, Field field, int rowIndex, int column)
    {
        var cells = block.Sheet.Rows[rowIndex];

        var rawCell = Cell(cells, column)
                      ?? throw new TabbitDefectException(
                          $"Column {column} of `{table.Name}` is outside the row.");

        // What the cell says, decided in one place for every layout: `-` is no value, `\-` is
        // the one character `-`, and a blank is whatever the column's type reads a blank as.
        var reading = _context.ReadCell(
            field.Type, field.EnumOrNull, rawCell.Value, rawCell.Location,
            block.Sheet.Layout.ArrayDelimiter,
            required: field.IsRequired,
            onBlankCell: block.Sheet.Layout.OnBlankCell,
            isReference: field.IsRef,
            column: $"{table.Name}.{field.Name}",
            elementsRequired: field.ElementsRequired,
            formulaError: rawCell.FormulaError,
            onFormulaError: block.Sheet.Layout.OnFormulaError,
            timeZone: block.Sheet.Layout.TimeZone);

        return new Cell
        {
            RawCell = rawCell,
            Value = reading.Value,
            HasValue = reading.HasValue,
            ElementHasValue = reading.ElementHasValue,
        };
    }

    #endregion

    /// <summary>
    /// Moves the primary index onto the column the declaration named - section 3.5.
    /// </summary>
    /// <remarks>
    /// **The columns do not move.** What a `key` says is which column addresses a row, and that
    /// is a different question from where the column sits - so the wire carries exactly what it
    /// carried and only the index changes. `Table.PrimaryIndexName` is where the model holds the
    /// answer.
    ///
    /// A composite key is a list, and the lookup surface it implies is a step of its own - so a
    /// list is refused here by name rather than read as its first component.
    /// </remarks>
    private void ApplyKeyMeta(
        Models.Table table, EntityBlock block, List<FieldSource> sources)
    {
        if (!block.Meta.TryGetValue("key", out var key))
            return;

        string written = key.Value.Trim();

        // **A semicolon parts the keys and a comma parts one key's columns.** The same pair
        // `allowed=a;b;c` already uses, so nothing new is introduced to say "several of
        // several" - section 3.5.
        var declared = new List<Models.TableKey>();

        foreach (string one in written.Split(';'))
        {
            if (one.Trim().Length == 0)
                continue;

            declared.Add(ReadKey(table, block, key.At, written, one));
        }

        if (declared.Count == 0)
        {
            throw new TabbitException(key.At,
                Message.Of(TabbitLayoutMessages.KeyMetaEmpty,
                    ("Entity", block.Name), ("Written", written)));
        }

        declared[0].IsPrimary = true;

        RefuseRepeatedKeys(table, block, key.At, declared);

        table.Keys = declared;

        // The first key is the primary one - the row's identity, what a reference points at,
        // and where a multi-row record begins. A single column takes the index the first
        // column had; a composite one has no single column to give it to, and the places that
        // need "the key" read `Table.Keys` instead.
        if (!declared[0].IsComposite)
        {
            var field = table.Fields.Find(column => column.Name == declared[0].FieldNames[0])!;

            table.PrimaryIndexName = field.Name;
            field.Indexing = true;
        }

        // The first column stops being the index and becomes an ordinary field - unless it
        // also asked to be a secondary one, which is a `*` it wrote for itself.
        if (table.Fields[0].Name != table.PrimaryIndexName && !sources[0].Header.Indexing)
            table.Fields[0].Indexing = false;

        // A single-column key and a `*` on that same column are one declaration written twice,
        // and there is no reading under which they mean different things.
        foreach (var one in declared.Where(k => !k.IsComposite))
        {
            var field = table.Fields.Find(column => column.Name == one.FieldNames[0])!;

            if (!one.IsPrimary && sources.Any(
                    s => s.Header.Indexing && NameOf(s.Header, s.Header.Path) == field.Name))
            {
                throw new TabbitException(key.At,
                    Message.Of(TabbitLayoutMessages.KeyDeclaredTwice,
                        ("Entity", block.Name), ("Key", field.Name)));
            }

            field.Indexing = true;
        }
    }

    /// <summary>
    /// Reads one key's columns and holds each to the rules an index has always had.
    /// </summary>
    private Models.TableKey ReadKey(
        Models.Table table, EntityBlock block, Location at, string written, string one)
    {
        var names = new List<string>();

        foreach (string part in one.Split(','))
        {
            string text = part.Trim();

            if (text.Length == 0)
                continue;

            string name = text.ToPascalCase();
            var field = table.Fields.Find(column => column.Name == name);

            if (field is null)
            {
                throw new TabbitException(at,
                    Message.Of(TabbitLayoutMessages.KeyColumnNotFound,
                        ("Entity", block.Name), ("Key", text),
                        ("Known", string.Join(", ", table.Fields
                            .Where(column => column.NamePath is null)
                            .Select(column => column.Name)))));
            }

            // A key is one value of one column of the row itself: a member of a group is not a
            // column of its own, and an array is not one value. The rules the first column has
            // always been held to, applied to every component.
            if (field.NamePath is not null)
            {
                throw new TabbitException(at,
                    Message.Of(TabbitLayoutMessages.KeyColumnNotScalar,
                        ("Entity", block.Name), ("Key", field.Name)));
            }

            if (names.Contains(field.Name))
            {
                throw new TabbitException(at,
                    Message.Of(TabbitLayoutMessages.KeyComponentRepeated,
                        ("Entity", block.Name), ("Key", one.Trim()), ("Column", field.Name)));
            }

            names.Add(field.Name);
        }

        if (names.Count == 0)
        {
            throw new TabbitException(at,
                Message.Of(TabbitLayoutMessages.KeyMetaEmpty,
                    ("Entity", block.Name), ("Written", written)));
        }

        return new Models.TableKey { FieldNames = names };
    }

    /// <summary>
    /// Refuses two keys made of the same columns.
    /// </summary>
    /// <remarks>
    /// The same set rather than the same order: `stage,slot` and `slot,stage` hold the same
    /// rows unique and would generate two lookups over one index. **A column appearing in
    /// several keys is fine** - that is what a secondary key usually is.
    /// </remarks>
    private static void RefuseRepeatedKeys(
        Models.Table table, EntityBlock block, Location at, List<Models.TableKey> declared)
    {
        _ = table;

        for (int i = 0; i < declared.Count; i++)
        {
            for (int j = i + 1; j < declared.Count; j++)
            {
                if (!declared[i].FieldNames.OrderBy(n => n, StringComparer.Ordinal)
                        .SequenceEqual(
                            declared[j].FieldNames.OrderBy(n => n, StringComparer.Ordinal)))
                {
                    continue;
                }

                throw new TabbitException(at,
                    Message.Of(TabbitLayoutMessages.KeyDeclaredTwice,
                        ("Entity", block.Name), ("Key", declared[j].ToString())));
            }
        }
    }

    #region Field variants - spec section 3.6

    /// <summary>
    /// Drops every column of a variant group but the one this build asked for.
    /// </summary>
    /// <remarks>
    /// **The produced files know nothing about variants.** One column becomes the field and the
    /// rest are not in the build, so the model, the wire and every generated reader are the same
    /// as if the sheet had written one column - which is why this happens here, before a field
    /// is built, rather than anywhere downstream.
    ///
    /// A field with one column is not a variant group even if that column names a variant: there
    /// is nothing to choose between, and refusing it would make `:variant` a thing a sheet has to
    /// finish before it converts.
    /// </remarks>
    private List<ColumnHeader> SelectVariants(EntityBlock block, List<ColumnHeader> headers)
    {
        if (!block.HeaderRows.TryGetValue(RowKeyVariant, out int variantRow))
            return headers;

        var row = block.Sheet.Rows[variantRow];

        foreach (var header in headers)
        {
            if (header.IsMemo || header.IsTombstone)
                continue;

            header.Variant = (Cell(row, header.Column)?.Value ?? "").Trim();
        }

        var groups = headers
            .Where(header => !header.IsMemo && !header.IsTombstone)
            .GroupBy(header => NameOf(header, header.Path), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();

        var dropped = new HashSet<ColumnHeader>();

        foreach (var group in groups)
        {
            var columns = group.ToList();

            RefuseVariantsWhereTheyCannotGo(block, columns);

            foreach (var chosen in ChooseVariant(block, group.Key, columns))
                dropped.Add(chosen);
        }

        return headers.Where(header => !dropped.Contains(header)).ToList();
    }

    /// <summary>
    /// The columns of a variant group that this build does not take.
    /// </summary>
    /// <remarks>
    /// The default column is the one whose `:variant` cell is blank. A field whose every column
    /// names a variant has no default, so the build has to say which one it wants - and a name
    /// nothing answers is reported with the ones that exist rather than falling back, because a
    /// misspelled variant that quietly built the default is a build that lies about itself.
    /// </remarks>
    private IEnumerable<ColumnHeader> ChooseVariant(
        EntityBlock block, string field, List<ColumnHeader> columns)
    {
        var byVariant = new Dictionary<string, ColumnHeader>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (byVariant.TryGetValue(column.Variant, out var earlier))
            {
                throw new TabbitException(column.NameCell.Location,
                    Message.Of(TabbitLayoutMessages.VariantDuplicated,
                        ("Entity", block.Name), ("Column", field),
                        ("Variant", column.Variant.Length == 0 ? "-" : column.Variant)));
            }

            byVariant[column.Variant] = column;
            _ = earlier;
        }

        string? asked = _context.Variants.Of(block.Name, field);

        ColumnHeader? taken;

        if (asked is { Length: > 0 })
        {
            if (!byVariant.TryGetValue(asked, out taken))
            {
                throw new TabbitException(columns[0].NameCell.Location,
                    Message.Of(TabbitLayoutMessages.VariantNotFound,
                        ("Entity", block.Name), ("Column", field), ("Variant", asked),
                        ("Known", Named(byVariant.Keys))));
            }
        }
        else if (!byVariant.TryGetValue("", out taken))
        {
            throw new TabbitException(columns[0].NameCell.Location,
                Message.Of(TabbitLayoutMessages.VariantNotChosen,
                    ("Entity", block.Name), ("Column", field), ("Known", Named(byVariant.Keys))));
        }

        // The header of a variant group is written on the default column, so a column that left
        // its type cell blank takes the canon's - and one that wrote a different type is a
        // disagreement rather than a second opinion. Section 3.6.
        var canon = byVariant.TryGetValue("", out var byDefault) ? byDefault : columns[0];

        if (!ReferenceEquals(taken, canon))
            TakeHeaderFromCanon(block, field, taken!, canon);

        return columns.Where(column => !ReferenceEquals(column, taken));
    }

    /// <summary>
    /// Gives the chosen column the header the group wrote once, and refuses a disagreement.
    /// </summary>
    private void TakeHeaderFromCanon(
        EntityBlock block, string field, ColumnHeader taken, ColumnHeader canon)
    {
        var typeRow = block.Sheet.Rows[block.HeaderRows[RowKeyType]];

        string written = (Cell(typeRow, taken.Column)?.Value ?? "").Trim();
        string canonical = (Cell(typeRow, canon.Column)?.Value ?? "").Trim();

        if (written.Length > 0 && !string.Equals(written, canonical, StringComparison.Ordinal))
        {
            throw new TabbitException(
                Cell(typeRow, taken.Column)?.Location ?? taken.NameCell.Location,
                Message.Of(TabbitLayoutMessages.VariantHeaderDisagrees,
                    ("Entity", block.Name), ("Column", field),
                    ("Variant", taken.Variant), ("Written", written), ("Canonical", canonical)));
        }

        // What the chosen column reads its header from. Everything a variant column may leave
        // blank is read from here instead, so a group states its type, its description and its
        // side once.
        taken.HeaderColumn = canon.Column;
        taken.WireTag ??= canon.WireTag;
    }

    /// <summary>
    /// Refuses a variant group where a column set cannot be replicated - section 3.6.
    /// </summary>
    /// <remarks>
    /// A key column, because the row's identity would then differ per build and nothing could
    /// say two builds hold the same row. A group member or an array element, because the whole
    /// set of columns would have to be repeated per variant - refused for the first pass rather
    /// than half-read.
    /// </remarks>
    private static void RefuseVariantsWhereTheyCannotGo(
        EntityBlock block, List<ColumnHeader> columns)
    {
        foreach (var column in columns)
        {
            if (column.Indexing || column.Column == block.FirstColumn)
            {
                throw new TabbitException(column.NameCell.Location,
                    Message.Of(TabbitLayoutMessages.VariantOnKeyColumn,
                        ("Entity", block.Name), ("Column", column.Written)));
            }

            if (column.Path is not null)
            {
                throw new TabbitException(column.NameCell.Location,
                    Message.Of(TabbitLayoutMessages.VariantOnGroupColumn,
                        ("Entity", block.Name), ("Column", column.Written)));
            }
        }
    }

    private static string Named(IEnumerable<string> variants)
    {
        var named = variants.Where(variant => variant.Length > 0).OrderBy(v => v, StringComparer.Ordinal);

        return named.Any() ? string.Join(", ", named) : "none";
    }

    #endregion

    #region Multi-row - spec section 6

    /// <summary>
    /// One record of a multi-row table: the rows it spans, and which of them are elements.
    /// </summary>
    private sealed class Record
    {
        /// <summary>The row the primary index was written on, which the scalars come from.</summary>
        public int FirstRow;

        /// <summary>Every row of the record, the first one included, `#` rows left out.</summary>
        public List<int> Rows = [];

        /// <summary>
        /// Per `[]` group, the rows that hold an element of it - section 6.1 rule 4.
        /// </summary>
        /// <remarks>
        /// Per group and not per record, because the groups stack independently: two of them
        /// side by side can be different lengths and a row holding one is not holding the other.
        /// Rule 5, which is what a reader gets wrong first.
        /// </remarks>
        public Dictionary<string, List<int>> Elements = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Groups the entity's rows into records, or answers null when this table has no `[]`.
    /// </summary>
    /// <remarks>
    /// **The signal is the primary index cell** - section 6.1 rule 2. A row that fills it starts
    /// a record and a row that leaves it blank extends the one above, which narrows the question
    /// to one cell: the layout this notation came from asked whether every non-element column
    /// was blank, and under that rule a stray scalar on an extension row made it a new record
    /// and the report said the key was missing - pointing somewhere the author had not typed.
    ///
    /// Here a stray scalar is rule 3's report, at the cell holding it, and a key typed onto a
    /// row meant as an extension becomes a record whose scalars are blank - which the blank-cell
    /// policy catches. Section 6.2.
    /// </remarks>
    private List<Record>? Records(EntityBlock block, List<ColumnHeader> headers)
    {
        var groups = headers
            .Where(header => header.IsMultiRow && !header.IsMemo && !header.IsTombstone)
            .Select(header => header.MultiRowGroup)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (groups.Count == 0)
            return null;

        int indexColumn = PrimaryIndexColumn(block, headers);
        var records = new List<Record>();

        foreach (var row in block.Rows)
        {
            var cells = block.Sheet.Rows[row.Row];
            bool starts = (Cell(cells, indexColumn)?.Value ?? "").Trim().Length > 0;

            if (starts)
            {
                // A `#` on a record's first row takes the record with it, extension rows and
                // all - rule 8. It is added with no rows so that the extension rows below it
                // attach to it rather than to the record above.
                if (row.Omitted)
                {
                    records.Add(new Record { FirstRow = row.Row, Rows = [] });
                    continue;
                }

                var started = new Record { FirstRow = row.Row };
                started.Rows.Add(row.Row);
                records.Add(started);

                CollectElements(block, headers, groups, started, row.Row);
                continue;
            }

            // An extension row before any record has begun. Reported rather than dropped: the
            // author wrote values somewhere nothing can hold them.
            if (records.Count == 0)
            {
                throw new TabbitException(
                    Cell(cells, indexColumn)?.Location ?? block.Location,
                    Message.Of(TabbitLayoutMessages.ExtensionRowWithoutRecord,
                        ("Entity", block.Name)));
            }

            // A `#` on an extension row takes only that row's elements - rule 8.
            if (row.Omitted)
                continue;

            RefuseScalarsOnExtensionRow(block, headers, row.Row);

            var current = records[^1];

            // The record this extends was left out by a `#` of its own, so its rows are not
            // read either. Rule 3 was still applied above, because a value written where only
            // `[]` belongs is worth reporting whether or not the record survives.
            if (current.Rows.Count == 0)
                continue;

            current.Rows.Add(row.Row);
            CollectElements(block, headers, groups, current, row.Row);
        }

        return records.Where(record => record.Rows.Count > 0).ToList();
    }

    /// <summary>
    /// The column the primary index is written in, which is the first column carrying a field.
    /// </summary>
    private int PrimaryIndexColumn(EntityBlock block, List<ColumnHeader> headers)
    {
        // Whichever column the key is, which is the first one unless the declaration named
        // another - section 3.5. The record boundary is the key's cell, so it moves with it.
        string named = block.Meta.TryGetValue("key", out var key)
            ? key.Value.Trim().ToPascalCase()
            : "";

        var first = named.Length > 0
            ? headers.Find(header => !header.IsMemo && !header.IsTombstone
                                     && NameOf(header, header.Path) == named)
              ?? headers.Find(header => !header.IsMemo && !header.IsTombstone)
            : headers.Find(header => !header.IsMemo && !header.IsTombstone);

        if (first is null)
        {
            throw new TabbitException(block.Location,
                Message.Of(TabbitLayoutMessages.NoFieldColumns, ("Entity", block.Name)));
        }

        // An index addresses a row by one value, and an array is not one. Rule 9, which is the
        // existing rule about what may index a table rather than a new one.
        if (first.IsMultiRow)
        {
            throw new TabbitException(first.NameCell.Location,
                Message.Of(TabbitLayoutMessages.MultiRowOnIndexColumn,
                    ("Entity", block.Name), ("Column", first.Written)));
        }

        return first.Column;
    }

    /// <summary>
    /// Notes which `[]` groups this row holds an element of - section 6.1 rule 4.
    /// </summary>
    /// <remarks>
    /// A group's range holding any value makes the row one of its elements, and holding none
    /// makes it none - so two groups beside each other fill up independently and a row that is
    /// the third element of one may be the first of the other.
    /// </remarks>
    private static void CollectElements(
        EntityBlock block, List<ColumnHeader> headers, List<string> groups,
        Record record, int row)
    {
        var cells = block.Sheet.Rows[row];

        foreach (string group in groups)
        {
            bool any = headers.Any(
                header => header.IsMultiRow && !header.IsMemo && !header.IsTombstone
                          && header.MultiRowGroup == group
                          && (Cell(cells, header.Column)?.Value ?? "").Length > 0);

            if (!any)
                continue;

            if (!record.Elements.TryGetValue(group, out var rows))
            {
                rows = [];
                record.Elements[group] = rows;
            }

            rows.Add(row);
        }
    }

    /// <summary>
    /// Reports a value on an extension row in a column that is not `[]` - rule 3.
    /// </summary>
    /// <remarks>
    /// **The report points at the cell holding the value**, and says the row it belongs on.
    /// That is the whole of section 6.2: the mistake and the message are in the same place,
    /// where the layout this came from named a different row and a different problem.
    /// </remarks>
    private static void RefuseScalarsOnExtensionRow(
        EntityBlock block, List<ColumnHeader> headers, int row)
    {
        var cells = block.Sheet.Rows[row];

        foreach (var header in headers)
        {
            if (header.IsMemo || header.IsTombstone || header.IsMultiRow)
                continue;

            var cell = Cell(cells, header.Column);
            if (cell is null || cell.Value.Length == 0)
                continue;

            throw new TabbitException(cell.Location,
                Message.Of(TabbitLayoutMessages.ExtensionRowHasScalarValue,
                    ("Entity", block.Name), ("Column", header.Written),
                    ("Value", cell.Value)));
        }
    }

    /// <summary>
    /// Reads a multi-row table: one model row per record, elements taken from its rows.
    /// </summary>
    /// <remarks>
    /// An element the record does not reach is written as no value, which is what makes the
    /// array end where the rows did - `Table.ElementCountIn` counts back to the last element a
    /// sheet gave a value for, and this is the sheet saying it gave none.
    /// </remarks>
    private void ParseMultiRowData(
        Models.Table table, EntityBlock block, List<FieldSource> sources, List<Record> records)
    {
        foreach (var record in records)
        {
            var row = new List<Cell>();

            for (int at = 0; at < table.Fields.Count; at++)
            {
                var field = table.Fields[at];
                var source = sources[at];

                int? from = SourceRow(record, source);

                row.Add(from is null
                    ? NoValueCell(block, record.FirstRow, source, field)
                    : ReadCellAt(table, block, field, from.Value, source.Header.Column));
            }

            table.Data.Add(row);
        }
    }

    /// <summary>
    /// Which row of a record a field's value comes from, or null for an element it has not got.
    /// </summary>
    private static int? SourceRow(Record record, FieldSource source)
    {
        if (source.Element is not { } element)
            return record.FirstRow;

        return record.Elements.TryGetValue(source.Header.MultiRowGroup, out var rows)
               && element < rows.Count
            ? rows[element]
            : null;
    }

    /// <summary>An element this record has no row for.</summary>
    /// <remarks>
    /// `HasValue` is false rather than the type's blank being written, because those are
    /// different statements and only this one shortens the array. The cell it points at is the
    /// record's own, so nothing downstream holds a location from another record.
    /// </remarks>
    private static Cell NoValueCell(
        EntityBlock block, int anchorRow, FieldSource source, Field field)
    {
        var anchor = Cell(block.Sheet.Rows[anchorRow], source.Header.Column)
                     ?? block.Sheet.Rows[anchorRow][0];

        return new Cell
        {
            RawCell = anchor,
            Value = CookingContext.EmptyValueOfType(field.Type),
            HasValue = false,
        };
    }

    #endregion

    #region Enums and constant sets - spec sections 8.4 and 8.5

    /// <summary>
    /// Which column of an entity holds which named thing, from the `:field` row.
    /// </summary>
    private Dictionary<string, int> NamedColumns(
        EntityBlock block, string[] known, string unknownMessage)
    {
        var fieldRow = block.Sheet.Rows[block.HeaderRows[RowKeyField]];
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int column = block.FirstColumn; column <= block.LastColumn; column++)
        {
            var cell = Cell(fieldRow, column);
            string written = (cell?.Value ?? "").Trim();

            if (written.Length == 0 || written == OmitMark
                || written.StartsWith(OmitMark, StringComparison.Ordinal))
            {
                continue;
            }

            string name = written.ToLowerInvariant();

            if (!known.Contains(name, StringComparer.Ordinal))
            {
                throw new TabbitException(cell!.Location,
                    Message.Of(unknownMessage,
                        ("Entity", block.Name), ("Column", written),
                        ("Known", string.Join(", ", known))));
            }

            if (found.ContainsKey(name))
            {
                throw new TabbitException(cell!.Location,
                    Message.Of(TabbitLayoutMessages.ColumnNameDuplicated,
                        ("Entity", block.Name), ("Column", written)));
            }

            found[name] = column;
        }

        return found;
    }

    private Models.Enum ParseEnum(EntityBlock block)
    {
        Log.Information($"Parsing enum `{block.Name}`. ({block.Location})");

        var result = new Models.Enum
        {
            Location = block.Location,
            TargetSide = block.TargetSide,
            RawName = block.RawName,
            Name = block.Name,
            Comment = block.Comment,
            Labels = [],
        };

        var columns = NamedColumns(block, EnumColumns, TabbitLayoutMessages.EnumColumnUnknown);

        foreach (string required in new[] { EnumColumnLabel, EnumColumnValue })
        {
            if (!columns.ContainsKey(required))
            {
                throw new TabbitException(block.Location,
                    Message.Of(TabbitLayoutMessages.EnumColumnMissing,
                        ("Entity", block.Name), ("Column", required)));
            }
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (int rowIndex in block.DataRows)
        {
            var cells = block.Sheet.Rows[rowIndex];

            var labelCell = Cell(cells, columns[EnumColumnLabel]);
            var valueCell = Cell(cells, columns[EnumColumnValue]);

            string rawName = (labelCell?.Value ?? "").Trim();

            // A blank row inside the entity is not possible - one ends the entity - so a blank
            // label is a row somebody left half written.
            if (rawName.Length == 0)
            {
                throw new TabbitException(labelCell?.Location ?? block.Location,
                    Message.Of(TabbitLayoutMessages.EnumCellRequired,
                        ("Entity", block.Name), ("Column", EnumColumnLabel)));
            }

            string name = rawName.ToPascalCase();
            _context.RequiresIdentifier(name, labelCell!.Location);

            if (result.Contains(name))
            {
                throw new TabbitException(labelCell.Location,
                    Message.Of(TabbitLayoutMessages.EnumLabelRedefined,
                        ("Entity", block.Name), ("Label", name)));
            }

            string valueWritten = (valueCell?.Value ?? "").Trim();

            if (valueWritten.Length == 0)
            {
                throw new TabbitException(valueCell?.Location ?? labelCell.Location,
                    Message.Of(TabbitLayoutMessages.EnumCellRequired,
                        ("Entity", block.Name), ("Column", EnumColumnValue)));
            }

            if (!int.TryParse(
                    valueWritten, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value))
            {
                throw new TabbitException(valueCell!.Location,
                    Message.Of(TabbitLayoutMessages.EnumLabelValueNotInteger,
                        ("Entity", block.Name), ("Label", name), ("Value", valueWritten)));
            }

            string alias = ValueOfOptional(cells, columns, EnumColumnAlias);

            if (alias.Length > 0)
            {
                // An alias is a fourth way to write a label in a data cell, so two labels
                // sharing one would make that cell mean either.
                if (aliases.TryGetValue(alias, out string? earlier))
                {
                    throw new TabbitException(
                        Cell(cells, columns[EnumColumnAlias])!.Location,
                        Message.Of(TabbitLayoutMessages.EnumAliasDuplicated,
                            ("Entity", block.Name), ("Alias", alias), ("Label", earlier)));
                }

                aliases[alias] = name;
            }

            result.Labels.Add(new Models.Enum.Label
            {
                Location = labelCell.Location,
                RawName = rawName,
                Name = name,
                Value = value,
                Alias = alias,
                Comment = ValueOfOptional(cells, columns, EnumColumnDesc),
            });
        }

        RefuseAliasesThatShadowLabels(block, result, columns);

        // An enum with no zero entry gives every unassigned field of that type a value with no
        // name, so one is supplied unless the recipe says otherwise.
        _context.ApplyAutoNoneLabel(result, block.Location);

        return result;
    }

    /// <summary>
    /// Refuses an alias that is already some label's own name.
    /// </summary>
    /// <remarks>
    /// A real name always wins the lookup, so such an alias would never resolve anything -
    /// which makes it worse than useless: the author believes those cells reach one label and
    /// they reach another. Checked once every label is in hand, because an alias may shadow a
    /// name declared below it.
    /// </remarks>
    private void RefuseAliasesThatShadowLabels(
        EntityBlock block, Models.Enum declared, Dictionary<string, int> columns)
    {
        if (!columns.TryGetValue(EnumColumnAlias, out int aliasColumn))
            return;

        foreach (var label in declared.Labels)
        {
            if (label.Alias.Length == 0)
                continue;

            var shadowed = declared.Labels.Find(
                other => other != label
                         && (other.Name == label.Alias || other.RawName == label.Alias));

            if (shadowed is null)
                continue;

            var cells = block.Sheet.Rows[label.Location.Row];

            throw new TabbitException(
                Cell(cells, aliasColumn)?.Location ?? label.Location,
                Message.Of(TabbitLayoutMessages.EnumAliasShadowsLabel,
                    ("Entity", block.Name), ("Alias", label.Alias),
                    ("Label", label.Name), ("Shadowed", shadowed.Name)));
        }
    }

    private Models.ConstantSet ParseConstantSet(EntityBlock block)
    {
        Log.Information($"Parsing constant-set `{block.Name}`. ({block.Location})");

        var result = new Models.ConstantSet
        {
            Location = block.Location,
            TargetSide = block.TargetSide,
            RawName = block.RawName,
            Name = block.Name,
            Comment = block.Comment,
            Constants = [],
        };

        var columns = NamedColumns(
            block, ConstColumns, TabbitLayoutMessages.ConstantColumnUnknown);

        foreach (string required in new[] { ConstColumnName, ConstColumnType, ConstColumnValue })
        {
            if (!columns.ContainsKey(required))
            {
                throw new TabbitException(block.Location,
                    Message.Of(TabbitLayoutMessages.ConstantColumnMissing,
                        ("Entity", block.Name), ("Column", required)));
            }
        }

        foreach (int rowIndex in block.DataRows)
        {
            var cells = block.Sheet.Rows[rowIndex];

            var nameCell = Cell(cells, columns[ConstColumnName]);
            var typeCell = Cell(cells, columns[ConstColumnType]);
            var valueCell = Cell(cells, columns[ConstColumnValue]);

            string rawName = (nameCell?.Value ?? "").Trim();
            if (rawName.Length == 0)
            {
                throw new TabbitException(nameCell?.Location ?? block.Location,
                    Message.Of(TabbitLayoutMessages.ConstantCellRequired,
                        ("Entity", block.Name), ("Name", ""), ("Column", ConstColumnName)));
            }

            string name = rawName.ToPascalCase();
            _context.RequiresIdentifier(name, nameCell!.Location);

            if (result.ContainsConstant(name))
            {
                throw new TabbitException(nameCell.Location,
                    Message.Of(TabbitLayoutMessages.ConstantRedefined,
                        ("Entity", block.Name), ("Name", name)));
            }

            string typeWritten = (typeCell?.Value ?? "").Trim();
            if (typeWritten.Length == 0)
            {
                throw new TabbitException(typeCell?.Location ?? nameCell.Location,
                    Message.Of(TabbitLayoutMessages.ConstantCellRequired,
                        ("Entity", block.Name), ("Name", name), ("Column", ConstColumnType)));
            }

            string valueWritten = (valueCell?.Value ?? "").Trim();
            if (valueWritten.Length == 0)
            {
                throw new TabbitException(valueCell?.Location ?? nameCell.Location,
                    Message.Of(TabbitLayoutMessages.ConstantCellRequired,
                        ("Entity", block.Name), ("Name", name), ("Column", ConstColumnValue)));
            }

            // A constant is one value in one cell. There is no row for it to be absent from,
            // so `?` has nothing to mean here - and accepting it silently would read as a
            // permission that was never granted.
            CookingContext.SplitOptionalMarker(typeWritten, out bool constantRequired);
            if (!constantRequired)
            {
                throw new TabbitException(typeCell!.Location,
                    Message.Of(TabbitLayoutMessages.ConstantCannotBeOptional,
                        ("Entity", block.Name), ("Name", name), ("Type", typeWritten)));
            }

            // The folded expression here too, which is what turns the five columns the old
            // notation needed into four: an enum names itself instead of writing `enum` beside
            // its name.
            string pascal = typeWritten.ToPascalCase();
            string resolved = Model.ContainsEnum(pascal) ? pascal : typeWritten.ToLowerInvariant();

            var enumm = Model.ContainsEnum(pascal) ? Model.GetEnum(pascal, typeCell!.Location) : null;
            var type = _context.ParseValueType(resolved, typeCell!.Location);

            result.Constants.Add(new Models.ConstantSet.Constant
            {
                Location = nameCell.Location,
                RawName = rawName,
                Name = name,
                TypeName = resolved,
                Type = type,
                Enum = enumm!,
                Comment = ValueOfOptional(cells, columns, ConstColumnDesc),
                ValueString = valueWritten,
                Value = _context.ParseValue(
                    type, enumm, valueWritten, valueCell!.Location,
                    block.Sheet.Layout.ArrayDelimiter,
                    timeZone: block.Sheet.Layout.TimeZone),
            });
        }

        return result;
    }

    #endregion

    #region Reading cells safely

    private static List<RawCell>? RowOrNull(EntityBlock block, string rowKey)
        => block.HeaderRows.TryGetValue(rowKey, out int row) ? block.Sheet.Rows[row] : null;

    /// <summary>
    /// A cell of a row, or null where the row is shorter than the entity is wide.
    /// </summary>
    /// <remarks>
    /// Sheets arrive squared off, so this should not happen - but a layout that indexes past
    /// the end of a row throws where a report belongs, and the caller has one to make.
    /// </remarks>
    private static RawCell? Cell(List<RawCell> row, int column)
        => column >= 0 && column < row.Count ? row[column] : null;

    private static string ValueOfOptional(
        List<RawCell> cells, Dictionary<string, int> columns, string name)
        => columns.TryGetValue(name, out int column)
            ? (Cell(cells, column)?.Value ?? "").Trim()
            : "";

    #endregion
}
