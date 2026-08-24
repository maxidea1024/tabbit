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
///
/// **The id is `primary` while this is built beside the layout it replaces**, because the
/// registry refuses two layouts claiming one id. Section 16 step 6 of the spec deletes the old
/// parser and hands this one the id `tabbit`.
/// </remarks>
[TabbitLayout("primary",
    Summary = "Entities declared with `:table` cells, whose column is the entity's marker column.")]
public sealed class PrimaryLayoutParser : ILayoutParser
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

    /// <summary>The header rows an enum or a constant set may carry: their columns are named.</summary>
    private static readonly string[] EntityRowKeys = [RowKeyField, RowKeyDesc];

    private static readonly string[] DeclarationMetaKeys = ["side", "key"];

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

        /// <summary>Rows the conversion reads, in sheet order, `#` rows already dropped.</summary>
        public List<int> DataRows = [];

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
                            Message.Of(PrimaryLayoutMessages.EntityNameDuplicated,
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
                    Message.Of(PrimaryLayoutMessages.DeclarationMetaUnclosed, ("Written", written)));
            }

            ReadDeclarationMeta(
                rest.Substring(open + 1, rest.Length - open - 2), kind, written, cell, meta);

            rest = rest.Substring(0, open).Trim();
        }

        if (rest.Length == 0)
        {
            throw new TabbitException(cell.Location,
                Message.Of(PrimaryLayoutMessages.DeclarationNeedsName,
                    ("Written", written), ("Kind", kind)));
        }

        string name = rest.ToPascalCase();
        _context.RequiresIdentifier(name, cell.Location);

        // Recognized and refused rather than ignored. Moving the primary index off the first
        // column is a statement about the row's identity, and a setting that is quietly
        // dropped would leave a table indexed by a column its author did not choose.
        if (meta.TryGetValue("key", out var key))
        {
            throw new TabbitException(key.At,
                Message.Of(PrimaryLayoutMessages.KeyMetaNotYetSupported, ("Written", written)));
        }

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
                    Message.Of(PrimaryLayoutMessages.DeclarationMetaKeyUnknown,
                        ("Written", written), ("Key", key), ("Kind", kind),
                        ("Known", string.Join(", ", DeclarationMetaKeys))));
            }

            if (into.ContainsKey(key))
            {
                throw new TabbitException(cell.Location,
                    Message.Of(PrimaryLayoutMessages.DeclarationMetaKeyRepeated,
                        ("Written", written), ("Key", key)));
            }

            // Both keys this layout defines take a value, so a bare one is a mistake rather
            // than a flag. Reported with an example of the key that was actually written.
            if (value is null || value.Length == 0)
            {
                throw new TabbitException(cell.Location,
                    Message.Of(PrimaryLayoutMessages.DeclarationMetaValueMissing,
                        ("Written", written), ("Key", key),
                        ("Example", key.ToLowerInvariant() == "side" ? "side=s" : "key=code")));
            }

            into[key] = new MetaEntry(value, cell.Location);
        }
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
                block.DataRows.Add(row);
                continue;
            }

            // A row left out of the conversion. Not counted as data, so it can also be the
            // blank line somebody wanted between two groups of rows - section 3.2 sends them
            // here for exactly that.
            if (value == OmitMark || value == OmitMarkAlternate)
                continue;

            string key = value.ToLowerInvariant();

            if (!AllRowKeys.Contains(key, StringComparer.Ordinal))
            {
                throw new TabbitException(marker!.Location,
                    Message.Of(PrimaryLayoutMessages.MarkerColumnUnknown,
                        ("Entity", block.Name), ("Written", value),
                        ("Keys", string.Join(" · ", AllRowKeys))));
            }

            if (block.HeaderRows.ContainsKey(key))
            {
                throw new TabbitException(marker!.Location,
                    Message.Of(PrimaryLayoutMessages.RowKeyRepeated,
                        ("Entity", block.Name), ("Key", key)));
            }

            // **The report a sorted sheet earns.** Sorting a sheet with the header rows inside
            // the selection scatters them through the data, and this is where that shows: a
            // header row below a row of data. Reported at the row that moved.
            if (block.DataRows.Count > 0)
            {
                throw new TabbitException(marker!.Location,
                    Message.Of(PrimaryLayoutMessages.RowKeyBelowData,
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
                Message.Of(PrimaryLayoutMessages.FieldRowMissing, ("Entity", block.Name)));
        }

        // Not read yet, and refused rather than ignored: every column a `:variant` row names
        // would otherwise be read as the field itself, so a build would silently hold three
        // copies of one price.
        if (block.HeaderRows.TryGetValue(RowKeyVariant, out int variantRow))
        {
            throw new TabbitException(
                block.Sheet.Rows[variantRow][block.MarkerColumn].Location,
                Message.Of(PrimaryLayoutMessages.VariantNotYetSupported, ("Entity", block.Name)));
        }

        if (block.Kind == KindTable)
        {
            if (!block.HeaderRows.ContainsKey(RowKeyType))
            {
                throw new TabbitException(block.Location,
                    Message.Of(PrimaryLayoutMessages.TypeRowMissing, ("Entity", block.Name)));
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
                Message.Of(PrimaryLayoutMessages.TypeRowNotOnEntity,
                    ("Entity", block.Name), ("Kind", block.Kind)));
        }

        foreach (var (key, row) in block.HeaderRows)
        {
            if (EntityRowKeys.Contains(key, StringComparer.Ordinal))
                continue;

            throw new TabbitException(
                block.Sheet.Rows[row][block.MarkerColumn].Location,
                Message.Of(PrimaryLayoutMessages.RowKeyNotOnEntity,
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

        return headers;
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
                Message.Of(PrimaryLayoutMessages.ColumnUnnamedWithData,
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
                Message.Of(PrimaryLayoutMessages.PathProblem,
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
                Message.Of(PrimaryLayoutMessages.RepeatedIndexMark,
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
                    Message.Of(PrimaryLayoutMessages.PathProblem,
                        ("Entity", block.Name), ("Column", written),
                        ("Detail", "has an empty level. A `.` separates one level of the path from the next, so there is a name missing on one side of it.")));
            }

            string name = text;
            int? index = null;

            int open = text.IndexOf('[');
            if (open >= 0)
            {
                if (!text.EndsWith("]", StringComparison.Ordinal))
                {
                    throw new TabbitException(cell.Location,
                        Message.Of(PrimaryLayoutMessages.PathProblem,
                            ("Entity", block.Name), ("Column", written),
                            ("Detail", "opens a bracket and does not close it. An element number is written `slots[0]`, and `[]` on its own puts the elements on the rows below.")));
                }

                anyBrackets = true;
                name = text.Substring(0, open).Trim();
                string digits = text.Substring(open + 1, text.Length - open - 2).Trim();

                if (name.Length == 0)
                {
                    throw new TabbitException(cell.Location,
                        Message.Of(PrimaryLayoutMessages.PathProblem,
                            ("Entity", block.Name), ("Column", written),
                            ("Detail", "has brackets with no name in front of them. Every level of a path in this layout is named.")));
                }

                if (digits.Length == 0)
                {
                    // `[]` puts the elements on the rows below - step 2 of the spec's order.
                    // Refused by name so nothing reads it as a plain field in the meantime.
                    throw new TabbitException(cell.Location,
                        Message.Of(PrimaryLayoutMessages.MultiRowNotYetSupported,
                            ("Entity", block.Name), ("Column", written),
                            ("Example", $"{name}[0]")));
                }

                if (!int.TryParse(
                        digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    || number < 0)
                {
                    throw new TabbitException(cell.Location,
                        Message.Of(PrimaryLayoutMessages.ElementNumberNotInteger,
                            ("Entity", block.Name), ("Column", written), ("Written", digits)));
                }

                index = number;
            }

            steps.Add(new FieldPathStep { Name = name.ToPascalCase(), Index = index });
        }

        if (header.Indexing && anyBrackets)
        {
            throw new TabbitException(cell.Location,
                Message.Of(PrimaryLayoutMessages.IndexMarkOnArrayColumn,
                    ("Entity", block.Name), ("Column", written)));
        }

        if (header.Indexing && steps.Count > 1)
        {
            throw new TabbitException(cell.Location,
                Message.Of(PrimaryLayoutMessages.IndexMarkOnGroupMember,
                    ("Entity", block.Name), ("Column", written), ("Group", steps[0].Name)));
        }

        foreach (var step in steps)
            _context.RequiresIdentifier(step.Name, cell.Location);

        // One level, named, not numbered: a plain field, which the model spells as no path at
        // all rather than as a path of one.
        if (steps.Count == 1 && !steps[0].IsIndexed)
            return null;

        return steps;
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

                // Keyed by the path down to the numbered level, so `stars[0].pos` and
                // `stars[1].pos` are one group and two different groups are never merged by
                // sharing a name one level up.
                string key = string.Join(
                    ".", header.Path.Take(level + 1).Select(step => step.Name));

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
                    Message.Of(PrimaryLayoutMessages.ElementNumbersNotFromZero,
                        ("Entity", block.Name), ("Group", group), ("First", numbers.Min)));
            }

            for (int expected = 0; expected < numbers.Count; expected++)
            {
                if (numbers.Contains(expected))
                    continue;

                throw new TabbitException(at.Location,
                    Message.Of(PrimaryLayoutMessages.ElementNumbersNotConsecutive,
                        ("Entity", block.Name), ("Group", group),
                        ("Present", numbers.Max), ("Missing", expected)));
            }
        }
    }

    #endregion

    #region Tables

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

            // **Not read from the source entry.** An array in this layout is declared with
            // brackets wherever it is, so `Text1` beside `Text2` is two fields and there is no
            // convention for the setting to honour.
            FoldSerialFields = false,

            TrimTrailingArrayElements = block.Sheet.Layout.TrimTrailingArrayElements,
            AllowArrayGaps = block.Sheet.Layout.AllowArrayGaps,
        };

        var headers = ReadColumns(block);
        RequireElementNumbering(block, headers);

        var carried = ParseFields(table, block, headers);

        if (carried.Count == 0)
        {
            throw new TabbitException(block.Location,
                Message.Of(PrimaryLayoutMessages.NoFieldColumns, ("Entity", block.Name)));
        }

        InheritTypesFromElementZero(table, carried);

        // Grouped before the cells are read, because grouping is what gives every element of
        // an array the first one's answer about being optional, and reading a cell asks it.
        _ = table.SerialFields;

        ParseData(table, block, carried);

        _context.CheckPrimaryIndexValidity(table.Fields[0]);
        _context.AssignTags(table);

        return table;
    }

    /// <summary>
    /// Builds a field per column that carries one, and returns those columns in order.
    /// </summary>
    private List<ColumnHeader> ParseFields(
        Models.Table table, EntityBlock block, List<ColumnHeader> headers)
    {
        var typeRow = block.Sheet.Rows[block.HeaderRows[RowKeyType]];
        var descRow = RowOrNull(block, RowKeyDesc);
        var targetRow = RowOrNull(block, RowKeyTarget);

        var carried = new List<ColumnHeader>();

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

            var typeCell = Cell(typeRow, header.Column);
            var descCell = descRow is null ? null : Cell(descRow, header.Column);
            var targetCell = targetRow is null ? null : Cell(targetRow, header.Column);

            string name = NameOf(header);

            if (table.ContainsField(name))
            {
                throw new TabbitException(header.NameCell.Location,
                    Message.Of(PrimaryLayoutMessages.ColumnNameDuplicated,
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
                Tag = header.WireTag,
                NamePath = header.Path,

                // The first field column is the primary index until `key` moves it, which is
                // a step of its own - the declaration refuses that key for now.
                Indexing = table.Fields.Count == 0 || header.Indexing,
            };

            _context.RequiresIdentifier(name, header.NameCell.Location);

            ReadType(field, header, typeCell, block);

            table.Fields.Add(field);
            carried.Add(header);
        }

        return carried;
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
    private static void InheritTypesFromElementZero(
        Models.Table table, List<ColumnHeader> carried)
    {
        for (int at = 0; at < carried.Count; at++)
        {
            if (!carried[at].TypeWasBlank)
                continue;

            var field = table.Fields[at];
            if (field.NamePath is null)
                continue;

            string wanted = MemberKey(field.NamePath);

            for (int other = 0; other < carried.Count; other++)
            {
                if (other == at || carried[other].TypeWasBlank)
                    continue;

                var source = table.Fields[other];

                if (source.NamePath is null
                    || MemberKey(source.NamePath) != wanted
                    || !source.NamePath.All(step => (step.Index ?? 0) == 0))
                {
                    continue;
                }

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
    private static string NameOf(ColumnHeader header)
    {
        if (header.Path is null)
            return header.Written.ToPascalCase();

        return string.Concat(header.Path.Select(
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

        // Everything from the first `(` is meta - the one rule, section 4.2. The keys arrive
        // with step 3; until then a cell that has any is refused rather than read as the type
        // with its constraints quietly dropped.
        int open = written.IndexOf('(');
        if (open >= 0)
        {
            string bare = written.Substring(0, open).Trim();

            throw new TabbitException(at,
                Message.Of(PrimaryLayoutMessages.ColumnMetaNotYetSupported,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Written", written), ("Bare", bare)));
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
                Message.Of(PrimaryLayoutMessages.ColumnTypeMissing,
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
                Message.Of(PrimaryLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", $"is typed `{written}`, and a `?` inside the brackets says an element may be absent - which a column that is not an array has no element to say it of.")));
        }

        header.HoldsArray = isArray;

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
                Message.Of(PrimaryLayoutMessages.PathProblem,
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
                Message.Of(PrimaryLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", "is typed `foreign` and names no table. Write the table after it - `foreign Item`, or `foreign Item|CEquip` for a key that is a row of either.")));
        }

        if (isArray)
        {
            // Deliberately unsupported rather than half-supported: a variable number of
            // targets per row is a shape the generated readers have none for, so letting it
            // parse would produce code that silently never resolves.
            throw new TabbitException(at,
                Message.Of(PrimaryLayoutMessages.PathProblem,
                    ("Entity", block.Name), ("Column", field.Name),
                    ("Detail", "is typed as an array of references, which the generated readers have no shape for. Write the references as numbered columns, one reference each.")));
        }

        // Split before casing: a bar is not a word separator, so casing the whole cell would
        // leave the second name as written.
        var targets = rest.Split('|')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Select(part => part.ToPascalCase())
            .ToList();

        var names = new List<string>();
        foreach (string target in targets)
        {
            if (!names.Contains(target))
                names.Add(target);
        }

        if (names.Count == 0)
        {
            throw new TabbitException(at,
                Message.Of(PrimaryLayoutMessages.PathProblem,
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
        field.RefFieldName = null;

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
    /// The first member column of a group, and element zero of a numbered one. Everything
    /// after it leaves the type cell blank, because the group's type is a property of the
    /// group rather than of each column in it.
    /// </remarks>
    private static bool IsFirstOfItsGroup(ColumnHeader header)
    {
        if (header.Path is null)
            return true;

        // Element zero of every numbered level, and the first member of the group otherwise.
        // A column with any level numbered above zero is never the group's first.
        foreach (var step in header.Path)
        {
            if (step.Index is { } number && number != 0)
                return false;
        }

        return true;
    }

    private void ParseData(Models.Table table, EntityBlock block, List<ColumnHeader> carried)
    {
        foreach (int rowIndex in block.DataRows)
        {
            var cells = block.Sheet.Rows[rowIndex];
            var row = new List<Cell>();

            for (int at = 0; at < table.Fields.Count; at++)
            {
                var field = table.Fields[at];
                var rawCell = Cell(cells, carried[at].Column)
                              ?? throw new TabbitDefectException(
                                  $"Column {carried[at].Column} of `{table.Name}` is outside the row.");

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

                row.Add(new Cell
                {
                    RawCell = rawCell,
                    Value = reading.Value,
                    HasValue = reading.HasValue,
                    ElementHasValue = reading.ElementHasValue,
                });
            }

            table.Data.Add(row);
        }
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
                    Message.Of(PrimaryLayoutMessages.ColumnNameDuplicated,
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

        var columns = NamedColumns(block, EnumColumns, PrimaryLayoutMessages.EnumColumnUnknown);

        foreach (string required in new[] { EnumColumnLabel, EnumColumnValue })
        {
            if (!columns.ContainsKey(required))
            {
                throw new TabbitException(block.Location,
                    Message.Of(PrimaryLayoutMessages.EnumColumnMissing,
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
                    Message.Of(PrimaryLayoutMessages.EnumCellRequired,
                        ("Entity", block.Name), ("Column", EnumColumnLabel)));
            }

            string name = rawName.ToPascalCase();
            _context.RequiresIdentifier(name, labelCell!.Location);

            if (result.Contains(name))
            {
                throw new TabbitException(labelCell.Location,
                    Message.Of(PrimaryLayoutMessages.EnumLabelRedefined,
                        ("Entity", block.Name), ("Label", name)));
            }

            string valueWritten = (valueCell?.Value ?? "").Trim();

            if (valueWritten.Length == 0)
            {
                throw new TabbitException(valueCell?.Location ?? labelCell.Location,
                    Message.Of(PrimaryLayoutMessages.EnumCellRequired,
                        ("Entity", block.Name), ("Column", EnumColumnValue)));
            }

            if (!int.TryParse(
                    valueWritten, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value))
            {
                throw new TabbitException(valueCell!.Location,
                    Message.Of(PrimaryLayoutMessages.EnumLabelValueNotInteger,
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
                        Message.Of(PrimaryLayoutMessages.EnumAliasDuplicated,
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
                Message.Of(PrimaryLayoutMessages.EnumAliasShadowsLabel,
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
            block, ConstColumns, PrimaryLayoutMessages.ConstantColumnUnknown);

        foreach (string required in new[] { ConstColumnName, ConstColumnType, ConstColumnValue })
        {
            if (!columns.ContainsKey(required))
            {
                throw new TabbitException(block.Location,
                    Message.Of(PrimaryLayoutMessages.ConstantColumnMissing,
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
                    Message.Of(PrimaryLayoutMessages.ConstantCellRequired,
                        ("Entity", block.Name), ("Name", ""), ("Column", ConstColumnName)));
            }

            string name = rawName.ToPascalCase();
            _context.RequiresIdentifier(name, nameCell!.Location);

            if (result.ContainsConstant(name))
            {
                throw new TabbitException(nameCell.Location,
                    Message.Of(PrimaryLayoutMessages.ConstantRedefined,
                        ("Entity", block.Name), ("Name", name)));
            }

            string typeWritten = (typeCell?.Value ?? "").Trim();
            if (typeWritten.Length == 0)
            {
                throw new TabbitException(typeCell?.Location ?? nameCell.Location,
                    Message.Of(PrimaryLayoutMessages.ConstantCellRequired,
                        ("Entity", block.Name), ("Name", name), ("Column", ConstColumnType)));
            }

            string valueWritten = (valueCell?.Value ?? "").Trim();
            if (valueWritten.Length == 0)
            {
                throw new TabbitException(valueCell?.Location ?? nameCell.Location,
                    Message.Of(PrimaryLayoutMessages.ConstantCellRequired,
                        ("Entity", block.Name), ("Name", name), ("Column", ConstColumnValue)));
            }

            // A constant is one value in one cell. There is no row for it to be absent from,
            // so `?` has nothing to mean here - and accepting it silently would read as a
            // permission that was never granted.
            CookingContext.SplitOptionalMarker(typeWritten, out bool constantRequired);
            if (!constantRequired)
            {
                throw new TabbitException(typeCell!.Location,
                    Message.Of(PrimaryLayoutMessages.ConstantCannotBeOptional,
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
