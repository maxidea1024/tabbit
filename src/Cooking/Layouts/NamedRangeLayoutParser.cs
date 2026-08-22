using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog;
using Tabbit.Extensions;
using Tabbit.Helpers;
using Tabbit.Models;
using Tabbit.Models.Raw;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The layout of a live game's workbooks, where a table is a workbook's defined name.
/// </summary>
/// <remarks>
/// Neither markers nor sheet tabs: the boundary of a table is a rectangle the workbook has
/// a name for, so the tab's name means nothing and one sheet can carry several tables side
/// by side. Inside the rectangle:
///
///   row 1     property names; a blank one excludes the column
///   row 2     types
///   row 3..   rows whose first cell begins with `:` - `:required`, `:min`, `:links` and
///             the rest - which declare constraints rather than data. Their count differs
///             per table, so they are recognized by that `:` and not by position.
///   then      the data
///
/// The full survey, and what each piece of it is for, is in
/// samples/named-range/doc/레이아웃-분석-20260808.md.
/// </remarks>
[TabbitLayout("named-range",
    Summary = "A table is a workbook's defined name; two header rows and ':'-keyed constraint rows.",
    UsesNamedRanges = true)]
public sealed class UwoLayoutParser : ILayoutParser
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Cooking;

    /// <summary>Rows of the rectangle, counted from its top.</summary>
    private const int NameRow = 0;
    private const int TypeRow = 1;

    /// <summary>
    /// What a grid's value array is called, and what the table listing its column ids is
    /// called. Neither has a name in the sheet: the header row spells out one axis's ids and
    /// says nothing about the other.
    /// </summary>
    private const string MatrixValueField = "Value";
    private const string MatrixColumnTableSuffix = "Column";

    private CookingContext _context = null!;

    /// <summary>
    /// Nothing to do: this layout declares no enums and no constant sets.
    /// </summary>
    /// <remarks>
    /// It has no way to. A column whose values come from a fixed set is a `number` with a
    /// `:enum` row listing them, which is a constraint on the data rather than a type -
    /// see §4.2 of the analysis. Turning those into real enums is a separate decision,
    /// because the labels are Korean display text rather than identifiers.
    /// </remarks>
    public void ParseDeclarations(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;
    }

    public void ParseTables(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;

        foreach (var sheet in sheets)
        {
            if (sheet.NamedRanges.Count == 0)
            {
                // Ordinary: a workbook in this layout holds working sheets beside its data,
                // and the way it says a sheet is data is by having a name for it.
                Log.Information(
                    $"Skipping sheet `{sheet.Location?.Sheet}`: no defined name covers it. ({sheet.Location})");
                continue;
            }

            foreach (var named in sheet.NamedRanges)
            {
                // A table this layout cannot read is reported and left out, rather than
                // ending the run. These workbooks hold six hundred tables and the author of
                // one wants the list of what is wrong with the sheets - a refusal that stops
                // at the first hides every table after it, which is how one shape mistake
                // used to cut a corpus of 540 tables down to 78.
                Models.Table? table;
                try
                {
                    table = ParseTable(sheet, named);
                }
                catch (TabbitException refusal)
                {
                    context.Diagnostics.Error(refusal);
                    continue;
                }

                if (table is not null)
                    context.Model.Tables.Add(table);
            }
        }
    }

    private Models.Table? ParseTable(RawSheet sheet, RawNamedRange named)
    {
        // `_BCGL`, `_BCCN`: the same table built for one region. The suffix decides which
        // file the original exporter writes, and the table inside keeps the base name.
        // Kept whole here - two tables of one name would collide - and left as a thing to
        // decide, which §6 of the analysis records.
        string rawName = named.Name;

        if (named.Height <= TypeRow + 1)
        {
            Log.Warning(
                $"Skipping `{rawName}`: the range covers {named.Height} row(s), and a table needs "
                + $"a name row, a type row and data. ({sheet.Location})");
            return null;
        }

        var marker = CellAt(sheet, named, NameRow, 0);

        var table = new Models.Table
        {
            Location = marker.Location,
            TargetSide = TargetSide.Both,
            RawName = rawName,
            Name = rawName.ToPascalCase(),
            Comment = "",

            // Serial fields do not apply to this layout at all, whatever a recipe says.
            // There is no numbering convention here: a number in a name is part of the name -
            // `OceanNpcLocal01` is one table, not element 1 of something - so folding on it
            // would invent arrays out of nothing. Arrays are written `name[0]`, which says so
            // outright, and records `name[0]["Member"]`.
            //
            // Spelled out rather than left to the default, because it is a property of the
            // layout and not a setting anybody should be able to switch on for it.
            FoldSerialFields = false,

            // Always, and not because a recipe asked: these sheets' own exporter ends an array
            // at the last element that has a value, so a row with two of three slots filled
            // produces two. Reading them without this gives a third element the original never
            // wrote, and no setting should be able to turn that on.
            TrimTrailingArrayElements = true,

            // Whether a gap in the middle of an array is refused is the recipe's question and
            // not the layout's, so it comes from the source entry like it does for the other
            // layouts. This parser was the only one not passing it through, which made the
            // setting silently do nothing for exactly the sheets that reach it: a full
            // conversion reported 2,475 gaps and turning the setting on changed neither the
            // count nor anything else.
            AllowArrayGaps = (sheet.Layout ?? SheetLayout.Default).AllowArrayGaps,
        };

        Log.Information($"Parsing table `{table.Name}`. ({marker.Location})");

        var matrixColumns = new List<(long Id, RawCell NameCell)>();
        var columns = ParseFields(table, sheet, named, matrixColumns);

        if (columns is null)
            return null;

        if (matrixColumns.Count > 0)
            PrepareMatrix(table, matrixColumns, sheet);

        // Grouped before the cells are read: grouping is what gives every element of an array
        // the first one's answer about being optional, and reading a cell asks that question.
        _ = table.SerialFields;

        ParseData(table, sheet, named, columns);

        _context.AssignTags(table);

        return table;
    }

    /// <summary>One column of the rectangle that survived into the model.</summary>
    private sealed class DataColumn
    {
        public required int RangeColumn { get; init; }
        public required Field Field { get; init; }

        /// <summary>Base-2 text, which is what a `bit` column holds.</summary>
        public required bool IsBinaryText { get; init; }
    }

    private List<DataColumn>? ParseFields(
        Models.Table table, RawSheet sheet, RawNamedRange named,
        List<(long Id, RawCell NameCell)> matrixColumns)
    {
        var columns = new List<DataColumn>();

        for (int col = 0; col < named.Width; col++)
        {
            var nameCell = CellAt(sheet, named, NameRow, col);
            var typeCell = CellAt(sheet, named, TypeRow, col);

            string rawFieldName = nameCell.Value.Trim();
            string rawType = typeCell.Value.Trim();

            // A blank name is how this layout parks a column, and it uses it constantly:
            // the Korean label beside each data column is one of these. `-` in either cell
            // says the same thing explicitly.
            if (rawFieldName.Length == 0 || rawFieldName == "-" || rawType == "-")
                continue;

            if (_context.IsIgnorantName(rawFieldName))
                continue;

            // A column whose name is a number is one coordinate of a grid rather than a
            // field: the name is the other axis's id. Rewritten to the array notation this
            // layout already has, so the folding, the type and the `:required` row all reach
            // it by the paths they always took - and the ids come back out beside the table.
            // spec/matrix-tables.md.
            if (long.TryParse(rawFieldName, NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out long axisId))
            {
                matrixColumns.Add((axisId, nameCell));
                rawFieldName = $"{MatrixValueField}[{matrixColumns.Count - 1}]";
            }

            var field = BuildField(table, sheet, named, col, nameCell, typeCell, rawFieldName, rawType,
                                   out bool isBinaryText);
            if (field is null)
                continue;

            // This layout says required in a `:required` row rather than on the type, so it
            // reads it there and puts it in the same place the type marker would. That is the
            // point of the model carrying it: two notations, one answer.
            //
            // An index is required whatever the row says. These sheets do not mark their keys
            // at all - the `:required` row is about the JSON the exporter emits, and a key was
            // never in question there - so reading the row literally would make every key
            // optional. Forcing rather than refusing, because the sheet is not claiming the
            // key is optional; it just never said.
            field.IsRequired = field.Indexing || ReadRequiredRow(sheet, named, col);

            ReadConstraintRows(field, sheet, named, col);

            if (table.ContainsField(field.Name))
            {
                throw new TabbitException(nameCell.Location,
                    $"Table `{table.Name}` has two columns named `{field.Name}`.");
            }

            field.Index = table.Fields.Count;
            table.Fields.Add(field);
            columns.Add(new DataColumn { RangeColumn = col, Field = field, IsBinaryText = isBinaryText });
        }

        if (table.Fields.Count == 0)
        {
            Log.Warning($"Skipping `{table.RawName}`: no column of it has both a name and a type.");
            return null;
        }

        if (!table.Fields[0].Indexing)
        {
            throw new TabbitException(table.Location,
                $"Table `{table.Name}` has no `key` column. The first column of a table in this "
                + $"layout is typed `key`, which is what every row is addressed by.");
        }

        _context.CheckPrimaryIndexValidity(table.Fields[0]);

        WidenMembersToOneType(table, sheet);

        return columns;
    }

    /// <summary>
    /// Gives every element of a record member the same numeric type: the widest any of them
    /// needed.
    /// </summary>
    /// <remarks>
    /// Only matters when numbers are narrowed, and then it matters a lot. Narrowing reads one
    /// column's values to pick its type, and the elements of one member are separate columns -
    /// so `effect[0]["Val"]` holding whole numbers became an `int` while `effect[1]["Val"]`
    /// with a fraction in it became a `double`. The file stores the member as **one** column
    /// and states one type for it, so the two widths were written under one declaration and a
    /// reader following the declaration walked off the end of the block.
    ///
    /// The model refuses that outright, which is right - it is not a shape the format has. But
    /// refusing is not the answer here, because the sheet did nothing wrong: it wrote one
    /// member and this layout decided to narrow. So the decision is made per member, which is
    /// the unit the type belongs to.
    /// </remarks>
    private void WidenMembersToOneType(Models.Table table, RawSheet sheet)
    {
        if (!NarrowNumbers(sheet))
            return;

        // Grouped by the whole path rather than by group and member, so a level further in
        // is its own member here too - the type belongs to the leaf, whatever its depth.
        foreach (var group in table.Fields.Where(f => f.IsRecordMember)
                                          .GroupBy(f => string.Join(
                                              ".", f.NamePath!.Select(step => step.Name))))
        {
            var fields = group.ToList();
            if (fields.Count < 2)
                continue;

            // Widest wins, and only among the numeric types narrowing produces. Anything
            // else that disagrees is a real mistake in the sheet, and the model reports it
            // against the cell rather than being quietly papered over here.
            var widest = fields
                .Select(f => f.Type)
                .Where(t => NumericWidth(t) > 0)
                .OrderByDescending(NumericWidth)
                .FirstOrDefault();

            if (widest == Models.ValueType.None)
                continue;

            foreach (var field in fields)
            {
                if (field.Type == widest || NumericWidth(field.Type) == 0)
                    continue;

                Log.Debug($"`{table.RawName}.{field.RawName}` widened to `{widest}` to match its member.");

                field.Type = widest;
                field.TypeName = widest switch
                {
                    Models.ValueType.Double => "double",
                    Models.ValueType.Int64 => "bigint",
                    _ => "int",
                };
            }
        }
    }

    /// <summary>How wide a numeric type is, for choosing between two of them. 0 = not one.</summary>
    private static int NumericWidth(Models.ValueType type) => type switch
    {
        Models.ValueType.Int32 => 1,
        Models.ValueType.Int64 => 2,
        Models.ValueType.Double => 3,
        _ => 0,
    };

    private Field BuildField(
        Models.Table table, RawSheet sheet, RawNamedRange named, int col,
        RawCell nameCell, RawCell typeCell, string rawFieldName, string rawType,
        out bool isBinaryText)
    {
        isBinaryText = false;

        // `number:sc`, `string:c`, `key:2000~3999`: the type cell carries the side, or the
        // key's permitted range.
        string[] typeParts = rawType.Split(':');

        // `text(Achievement):c` and `[text(Achievement)]`: the group comes off before the case
        // is folded, because it names an output file and the author's spelling of it is the
        // file's name. The list brackets come off first so a grouped element is read the same
        // inside them as outside. The side qualifier after it is untouched - a sheet already
        // written as `text:c` says exactly what it always said.
        string writtenType = typeParts[0].Trim();

        bool bracketed = writtenType.StartsWith('[') && writtenType.EndsWith(']');
        string listElement = bracketed
            ? writtenType.Substring(1, writtenType.Length - 2).Trim()
            : writtenType;

        string bareType = CookingContext.SplitRoleGroup(
            listElement, out string? roleGroup, out string? roleNamespace);

        string typeName = bracketed
            ? "[" + bareType.ToLowerInvariant() + "]"
            : bareType.ToLowerInvariant();

        string qualifier = typeParts.Length > 1 ? typeParts[1].Trim() : "";

        if (roleGroup is not null && typeName != "text" && typeName != "[text]")
        {
            throw new TabbitException(typeCell.Location,
                $"Column `{nameCell.Value.Trim()}` of `{table.RawName}` is typed `{rawType}`, "
                + $"which names a group. Only `text` is gathered into one.");
        }

        _context.RequiresRoleGroup(
            $"Column `{nameCell.Value.Trim()}` of `{table.RawName}` typed `{rawType}`",
            StringRole.Text, roleGroup, roleNamespace, typeCell.Location);

        var field = new Field
        {
            OwnerTable = table,
            NameLocation = nameCell.Location,
            TypeLocation = typeCell.Location,
            DetailTypeLocation = typeCell.Location,
            TargetSideLocation = typeCell.Location,
            Comment = "",
            RawName = rawFieldName,
            TargetSide = TargetSide.Both,
        };

        // `character[0]["Id"]`: the column name is a path into the row's JSON. Translated
        // into the same record model Tabbit's own `Group.Member` notation produces, so
        // there is one model behind two notations rather than two models.
        if (!UwoColumnPath.TrySplit(rawFieldName, out var path, out string? problem))
        {
            throw new TabbitException(nameCell.Location,
                $"Column `{nameCell.Value.Trim()}` of `{table.RawName}` {problem}");
        }

        if (path is not null)
        {
            // Pascal cased level by level, so the case rules never see a bracket and a level
            // numbered rather than named keeps its number as its name.
            foreach (var step in path)
                step.Name = step.Name.ToPascalCase();

            field.NamePath = path;

            // The generated identifier stays one name: duplicate field names are refused
            // before the folding runs, so every level and every element number has to be in
            // it. A single level is an array of plain values and folds exactly as a serial
            // field does; two or more make a record.
            field.Name = string.Concat(path.Select(step =>
                step.Name + (step.IsIndexed ? step.Index!.Value.ToString(CultureInfo.InvariantCulture) : "")));
        }
        else
        {
            field.Name = rawFieldName.ToPascalCase();
        }

        _context.RequiresIdentifier(field.Name, nameCell.Location);

        switch (typeName)
        {
            case "key":
                field.Indexing = true;
                field.TypeName = "int";
                field.Type = Models.ValueType.Int32;

                // `key:0~200` is the range of ids this table may use. Recorded in the
                // original exporter's output and checked there; there is nowhere in this
                // model to put it, so it is dropped with a note rather than silently.
                if (qualifier.Contains('~'))
                    Log.Debug($"`{table.RawName}.{field.Name}` declares the key range `{qualifier}`, which is not carried into the model.");

                return field;

            case "number":
                // `number` targets JSON's numeric type - one type covering integers, reals and
                // values past 32 bits - and the original exporter writes the cell's text
                // straight into a JSON number. A double carries exactly that, so it is what
                // this reads by default.
                //
                // `UwoNumberType: "narrow"` gives each column the smallest type its values
                // fit instead. Better types and smaller files, at the cost of a type that
                // depends on the data - and it is type information this tool inferred rather
                // than information the sheet held, which is worth keeping separable when the
                // point of a conversion is to compare the two formats.
                if (NarrowNumbers(sheet))
                {
                    (field.TypeName, field.Type) = DetectNumberType(sheet, named, col) switch
                    {
                        NumberKind.Real => ("double", Models.ValueType.Double),
                        NumberKind.Wide => ("bigint", Models.ValueType.Int64),
                        _ => ("int", Models.ValueType.Int32),
                    };

                    if (field.Type != Models.ValueType.Int32)
                        Log.Debug($"`{table.RawName}.{nameCell.Value.Trim()}` narrowed to `{field.TypeName}`.");
                }
                else
                {
                    field.TypeName = "double";
                    field.Type = Models.ValueType.Double;
                }

                ApplySide(field, qualifier);
                return field;

            case "float":
                field.TypeName = "double";
                field.Type = Models.ValueType.Double;
                ApplySide(field, qualifier);
                return field;

            case "string":
            case "text":
                // `text` is a localized string. What separates it from `string` is that the
                // value is also gathered for translation; what reaches the data file is the
                // same string either way, which is why the role sits beside the type rather
                // than in it.
                field.TypeName = "string";
                field.Type = Models.ValueType.String;

                if (typeName == "text")
                {
                    field.Role = StringRole.Text;
                    field.RoleGroup = roleGroup;
                    field.RoleNamespace = roleNamespace;
                }

                ApplySide(field, qualifier);
                return field;

            case "bool":
                field.TypeName = "bool";
                field.Type = Models.ValueType.Bool;
                ApplySide(field, qualifier);
                return field;

            case "bit":
                // A flag set. What is this layout's own is the notation - a bare run of
                // digits is base 2, so `1111111` is 127 - and the cell reader states that by
                // prefixing `0b`. The type itself is the core's, which is where the width,
                // the refusals and the wire element belong. spec/bitset.md.
                field.TypeName = "bitset";
                field.Type = Models.ValueType.Bitset;
                isBinaryText = true;
                ApplySide(field, qualifier);
                return field;

            case "strkey":
                // A key that is a string rather than a number. Six tables of this project use
                // one - animation and socket names, and a couple of settings tables keyed by
                // `name` - and **nothing references any of them**, which is what makes this
                // cheap: the generated lookup is a dictionary over the field's own type, and
                // only pointing *at* such a table is a shape the wire cannot carry.
                field.Indexing = true;
                field.TypeName = "string";
                field.Type = Models.ValueType.String;
                ApplySide(field, qualifier);
                return field;

            default:
                if (typeName.StartsWith('[') && typeName.EndsWith(']'))
                {
                    // `[number]` - one cell holding a list. Exactly one column of this project
                    // uses it, and the separator is the source entry's `ArrayDelimiter`: the
                    // sheet does not say what it is, so the recipe does.
                    string inner = typeName.Substring(1, typeName.Length - 2).Trim();

                    var element = inner switch
                    {
                        "number" => NarrowNumbers(sheet)
                            ? DetectNumberType(sheet, named, col) switch
                            {
                                NumberKind.Real => Models.ValueType.Double,
                                NumberKind.Wide => Models.ValueType.Int64,
                                _ => Models.ValueType.Int32,
                            }
                            : Models.ValueType.Double,
                        "string" or "text" => Models.ValueType.String,
                        "bool" => Models.ValueType.Bool,
                        _ => Models.ValueType.None,
                    };

                    if (element == Models.ValueType.None)
                    {
                        throw new TabbitException(typeCell.Location,
                            $"Column `{nameCell.Value.Trim()}` of `{table.RawName}` is typed `{rawType}`. `{inner}` "
                            + $"is not an element type this layout puts in a list.");
                    }

                    // Every element of the cell is gathered, the same as a scalar `text` cell.
                    // The role is a property of what the column holds, and a list of them holds
                    // more of the same thing.
                    if (inner == "text")
                    {
                        field.Role = StringRole.Text;
                        field.RoleGroup = roleGroup;
                        field.RoleNamespace = roleNamespace;
                    }

                    field.Type = Models.ValueTypes.ArrayOf(element);
                    field.TypeName = element switch
                    {
                        Models.ValueType.Double => "double",
                        Models.ValueType.Int64 => "bigint",
                        Models.ValueType.Int32 => "int",
                        Models.ValueType.Bool => "bool",
                        _ => "string",
                    };

                    ApplySide(field, qualifier);
                    return field;
                }

                throw new TabbitException(typeCell.Location,
                    $"Column `{nameCell.Value.Trim()}` of `{table.RawName}` is typed `{rawType}`, which this "
                    + $"layout does not recognize.");
        }
    }

    /// <summary>
    /// Applies the side qualifier, where the type cell carries one.
    /// </summary>
    /// <remarks>
    /// Membership rather than equality, which is what the original exporter does: `sc` is
    /// both, `c` is the client, `s` is the server. Anything else leaves the column in both,
    /// because a qualifier this layout uses for something else - a key range - must not be
    /// read as "neither side wants this".
    /// </remarks>
    private static void ApplySide(Field field, string qualifier)
    {
        if (qualifier.Length == 0)
            return;

        bool server = qualifier.Contains('s');
        bool client = qualifier.Contains('c');

        if (server && !client)
            field.TargetSide = TargetSide.ServerOnly;
        else if (client && !server)
            field.TargetSide = TargetSide.ClientOnly;
    }

    private void ParseData(
        Models.Table table, RawSheet sheet, RawNamedRange named, List<DataColumn> columns)
    {
        for (int row = TypeRow + 1; row < named.Height; row++)
        {
            var keyCell = CellAt(sheet, named, row, 0);
            string key = keyCell.Value.Trim();

            // A row whose first cell begins with `:` declares constraints - `:required`,
            // `:min`, `:links` - rather than holding data. There is no fixed number of
            // them, which is why they are recognized here and not counted as header rows.
            if (key.StartsWith(':'))
                continue;

            if (key.Length == 0)
            {
                // A row holding nothing at all is a row the original exporter never saw: its
                // OLE DB path does not surface fully empty rows, so its stop-at-empty-key
                // rule cannot fire on one. The largest table of the sample set holds four
                // such rows in the middle of its data, and its deployed export carries every
                // row below them - so an empty row is a skip, measured rather than assumed.
                if (RowIsEmpty(sheet, named, row))
                    continue;

                // The end of the table: a key left blank on a row that holds something. The
                // original exporter is handed that row and stops on the blank key the same
                // way.
                break;
            }

            // A commented-out row, the same convention the column names use.
            if (key.StartsWith('#'))
                continue;

            var cells = new List<Cell>(table.Fields.Count);

            foreach (var column in columns)
            {
                var rawCell = CellAt(sheet, named, row, column.RangeColumn);
                cells.Add(ReadCell(column, rawCell, sheet.Layout?.ArrayDelimiter,
                               sheet.Layout?.OnFormulaError ?? FormulaErrorPolicy.Error,
                               sheet.Layout?.TimeZone));
            }

            table.Data.Add(cells);
        }
    }

    /// <summary>Whether a row of the rectangle holds no value in any of its columns.</summary>
    /// <remarks>
    /// The whole rectangle rather than the columns being read, because what is being decided
    /// is what the original exporter's row enumeration would have been handed - and that
    /// enumeration knows nothing about which columns the header keeps.
    /// </remarks>
    private static bool RowIsEmpty(RawSheet sheet, RawNamedRange named, int row)
    {
        for (int column = 0; column < named.Width; column++)
        {
            if (CellAt(sheet, named, row, column).Value.Trim().Length > 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reads one cell into the value its column's type calls for.
    /// </summary>
    /// <remarks>
    /// `-` is this layout's "no value", and a blank cell is a mistake - the reverse of
    /// Tabbit's own layout, where a blank cell is an empty value. The original exporter
    /// reports a blank and tells the author to write `-`, so the two agree about which is
    /// which; here a `-` becomes the type's empty value, which is what the exporter's
    /// output holds by omitting the property.
    /// </remarks>
    private Cell ReadCell(
        DataColumn column, RawCell rawCell, char? arrayDelimiter,
        FormulaErrorPolicy onFormulaError, TimeZoneInfo? timeZone)
    {
        string text = rawCell.Value.Trim();

        // A broken formula before the blank below, because a cell whose formula failed reads as
        // blank and this layout reports a blank as an author's omission - which it is not. The
        // core applies the policy and says which error it was. spec/formula-errors.md.
        if (rawCell.FormulaError.Length > 0)
        {
            var broken = _context.ReadCell(
                column.Field.Type, column.Field.EnumOrNull, "", rawCell.Location, arrayDelimiter,
                required: column.Field.IsRequired,
                column: $"{column.Field.OwnerTable.Name}.{column.Field.Name}",
                formulaError: rawCell.FormulaError,
                onFormulaError: onFormulaError,
                timeZone: timeZone);

            return new Cell
            {
                RawCell = rawCell,
                Value = broken.Value,
                HasValue = broken.HasValue,
            };
        }

        // `-` is no value, and the original exporter answers it by leaving the property out
        // of the row altogether. The judgment itself is the core's, so that one spelling
        // cannot come to mean two things in two layouts - spec/blank-and-null-cells.md - and
        // what this layout keeps deciding is what a blank means, below.
        if (CookingContext.SaysNoValue(text))
            return EmptyCell(column, rawCell, arrayDelimiter);

        if (text.Length == 0)
        {
            // Blank is a mistake in this layout, and the original exporter says so: it
            // reports the cell and tells the author to write `-`. Reported rather than
            // refused, because refusing would stop a conversion of six hundred tables over
            // a cell whose intent is not in doubt.
            Log.Warning(
                $"`{column.Field.OwnerTable.Name}.{column.Field.Name}` is blank. This layout "
                + $"writes `-` for no value; read as the type's empty value.\n    at {rawCell.Location}");

            return EmptyCell(column, rawCell, arrayDelimiter);
        }

        // A bare run of digits meaning base 2 - `1111111` for 127 - which is this layout's
        // notation and not a general one. Said with the prefix the `bitset` type reads, so
        // the value is converted in one place and the core is not asked to guess a base from
        // a column's provenance. A cell that is not base 2 is reported there, against the
        // cell, naming the digit that is not one.
        if (column.IsBinaryText && text.Length > 0 && !CookingContext.SaysNoValue(text))
            text = "0b" + text;

        // A leading `#` on a localizable string is a mark on the sheet rather than part of
        // the text, and the original exporter drops every one of them before writing the
        // value out. Kept, they travel into the game as part of the string: measured against
        // that exporter's own output, this is around 15,000 values of the sample project.
        //
        // `@` marks the same intent but is not dropped - which is the exporter's behaviour
        // and not an oversight here.
        //
        // **The other half of this rule is not implemented.** A value marked either way is
        // also held back from the gathered text, and that decision has to be made here,
        // before the mark is removed - there is nowhere downstream that can still see it.
        if (column.Field.Role == StringRole.Text)
            text = text.TrimStart('#');

        var reading = _context.ReadCell(
            column.Field.Type, column.Field.EnumOrNull, text, rawCell.Location, arrayDelimiter,
            required: column.Field.IsRequired,
            column: $"{column.Field.OwnerTable.Name}.{column.Field.Name}",
            formulaError: rawCell.FormulaError,
            onFormulaError: onFormulaError,
            timeZone: timeZone);

        return new Cell
        {
            RawCell = rawCell,
            Value = reading.Value,
            HasValue = reading.HasValue,
        };
    }

    /// <summary>The one option this layout reads, and the values it takes.</summary>
    private const string NumberTypeOption = "NumberType";

    /// <summary>
    /// Whether a `number` column takes the smallest type its values fit, rather than the
    /// double the sheet means.
    /// </summary>
    /// <remarks>
    /// `number` here targets JSON's numeric type - one type covering integers, reals and
    /// values past 32 bits - and the original exporter writes the cell's text straight into a
    /// JSON number. A double carries exactly that, so it is the default.
    ///
    /// `narrow` is type information this parser inferred rather than information the sheet
    /// held. Worth having, and worth being able to turn off: when the point of a conversion
    /// is to compare this format against that JSON, counting the narrowing's benefit as the
    /// format's would credit the wrong thing.
    /// </remarks>
    private bool NarrowNumbers(RawSheet sheet)
    {
        var layout = sheet.Layout ?? SheetLayout.Default;

        // Once per sheet is cheap and means a typo is reported even if no `number` column
        // ever asks. Nothing else can report it - the core carries the bag unread.
        layout.RequireKnownOptions($"the `{layout.Id}` layout", NumberTypeOption);

        string value = (layout.Option(NumberTypeOption) ?? "double").Trim().ToLowerInvariant();

        switch (value)
        {
            case "double": return false;
            case "narrow": return true;
        }

        throw new TabbitException(sheet.Location,
            $"`LayoutOptions.{NumberTypeOption}` is `{value}`. It takes `double`, which reads a "
            + $"`number` column as the JSON number it means, or `narrow`, which gives it the "
            + $"smallest type its values fit.");
    }

    /// <summary>
    /// Whether the `:required` row marks this column, which is how this layout says required.
    /// </summary>
    /// <remarks>
    /// `1` means required and `-` means not. A table with no `:required` row at all - a few
    /// have none - leaves every column optional, which is what its own checker concludes from
    /// the same absence.
    /// </remarks>
    /// <summary>How this layout writes yes in a constraint row.</summary>
    private static bool IsYes(string mark)
        => mark == "1" || string.Equals(mark, "true", StringComparison.OrdinalIgnoreCase);

    private static bool ReadRequiredRow(RawSheet sheet, RawNamedRange named, int col)
    {
        for (int row = TypeRow + 1; row < named.Height; row++)
        {
            string key = CellAt(sheet, named, row, 0).Value.Trim();

            // Past the schema rows and into the data, so there was no `:required`.
            if (!key.StartsWith(':'))
                return false;

            if (!string.Equals(key, ":required", StringComparison.OrdinalIgnoreCase))
                continue;

            string mark = CellAt(sheet, named, row, col).Value.Trim();
            return mark == "1" || string.Equals(mark, "true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Reads the `:min`, `:max` and `:enum` rows into the column's constraints.
    /// </summary>
    /// <remarks>
    /// These sheets declare what a column may hold and their own checker reads it back out
    /// of the exported JSON afterwards. Read here instead, the check happens where the cell
    /// is - so a diagnostic can point at the sheet rather than at a line of output.
    ///
    /// `-` is how this layout writes "nothing declared", the same as everywhere else.
    ///
    /// A reference band is left as it is written: `:min`/`:max` on a column that points at
    /// another table state the id range it points into, which the model checks by resolving
    /// the reference rather than by comparing numbers. spec/column-constraints.md.
    /// </remarks>
    private void ReadConstraintRows(
        Field field, RawSheet sheet, RawNamedRange named, int col)
    {
        var minimum = ConstraintCell(sheet, named, col, ":min");
        var maximum = ConstraintCell(sheet, named, col, ":max");
        var allowed = ConstraintCell(sheet, named, col, ":enum");

        // Required inside the object rather than in the row. These sheets declare it 216
        // times and nothing read it until now, so the rule it states was never checked.
        // `1` is how this layout writes yes, the same as `:required`.
        var inRecord = ConstraintCell(sheet, named, col, ":requiredInObject");

        if (inRecord is not null && IsYes(inRecord.Value.Trim()))
        {
            field.Constraints.RequiredInRecord = true;
            field.Constraints.RequiredInRecordLocation = inRecord.Location;
        }

        if (minimum is not null
            && double.TryParse(minimum.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                               out double min))
        {
            field.Constraints.Minimum = min;
            field.Constraints.MinimumLocation = minimum.Location;
        }

        if (maximum is not null
            && double.TryParse(maximum.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                               out double max))
        {
            field.Constraints.Maximum = max;
            field.Constraints.MaximumLocation = maximum.Location;
        }

        if (allowed is not null)
        {
            var values = AllowedValues(field, allowed.Value);

            if (values.Count > 0)
            {
                field.Constraints.AllowedValues = values;
                field.Constraints.AllowedValuesLocation = allowed.Location;
            }
        }

        ReadReferencedTables(field, sheet, named, col);
    }

    /// <summary>
    /// The values a `:enum` cell lists, read the way the sheets' own checker reads them.
    /// </summary>
    /// <remarks>
    /// **A text column's list is quoted, and anything unquoted is not a list at all.** The
    /// original exporter pulls the quoted runs out of the cell and, finding none, writes no
    /// list - so a cell holding a bare `1` on a `string` column declares nothing, and the
    /// checker downstream never sees it.
    ///
    /// Read any other way, that cell says "the only value allowed here is `1`" and every row
    /// of the column breaks it. One sheet does hold such a cell, and reading it as a list of
    /// one produced 1,980 findings about a column nobody had constrained.
    ///
    /// A column of any other type carries a single value rather than a list, which is the
    /// same exporter's other arm - `[value]`, whatever the value is.
    /// </remarks>
    private static List<string> AllowedValues(Field field, string cell)
    {
        string text = cell.Trim();

        if (text.Length == 0 || text == "-")
            return new List<string>();

        if (field.Type is Models.ValueType.String or Models.ValueType.StringArray)
        {
            return Regex.Matches(text, "\"(.*?)\"")
                .Select(match => match.Groups[1].Value)
                .Where(value => value.Length > 0)
                .ToList();
        }

        return new List<string> { text };
    }

    /// <summary>
    /// The rows naming the tables this column's value has to exist in.
    /// </summary>
    /// <remarks>
    /// Two rows, one meaning: one names a single table and the other a list, and the checker
    /// they came from runs the same lookup over both - the list one simply stops at the first
    /// hit. So they land in one place, and a single target is a list of one.
    ///
    /// This is a constraint and not a reference. The original declares it to have a script
    /// check that an id exists somewhere; nothing about the value, the file it is written to
    /// or the code generated from it depends on the declaration. Reading it as a `foreign`
    /// would give it a meaning it never had, and would owe every language a sum type for
    /// the several-table case. spec/multi-target-references.md.
    /// </remarks>
    private static void ReadReferencedTables(
        Field field, RawSheet sheet, RawNamedRange named, int col)
    {
        var tables = new List<string>();

        // Both rows, singular first. Neither is required and a table may carry either or
        // both; a name written twice is kept once, since "in this table or this table" said
        // twice is the same catalogue.
        foreach (string key in new[] { ":link", ":links" })
        {
            var cell = ConstraintCell(sheet, named, col, key);
            if (cell is null)
                continue;

            // A line per name is how these cells are actually written - `"A"` newline `"B"` -
            // with commas turning up too. Not the source entry's delimiter, which is about
            // the data cells; a table name contains none of these.
            foreach (string written in cell.Value.Split('\n', '\r', ',', ';'))
            {
                string name = TargetTableName(written);
                if (name.Length == 0 || tables.Contains(name))
                    continue;

                tables.Add(name);
            }

            field.Constraints.ReferencedTablesLocation ??= cell.Location;
        }

        if (tables.Count > 0)
            field.Constraints.ReferencedTables = tables;
    }

    /// <summary>
    /// The table name out of one written target.
    /// </summary>
    /// <remarks>
    /// Written `file/table`, naming the output file and the table inside it, and just `file`
    /// when the two have the same name. Only the table half is a fact about the model - which
    /// file a project splits its output into is that project's business - so the other half
    /// is dropped here rather than carried into the core.
    ///
    /// Cased the way this layout cases a table it declares, since the name has to find one.
    /// </remarks>
    private static string TargetTableName(string written)
    {
        string text = written.Trim().Trim('"', '\'').Trim();

        int slash = text.LastIndexOf('/');
        if (slash >= 0)
            text = text.Substring(slash + 1);

        text = text.Trim();

        return text.Length == 0 ? "" : text.ToPascalCase();
    }

    /// <summary>
    /// The cell a named constraint row holds for one column, or null when the table has no
    /// such row or the cell says nothing.
    /// </summary>
    private static RawCell? ConstraintCell(RawSheet sheet, RawNamedRange named, int col, string key)
    {
        // Column 0 holds the row's own key, so every constraint row would answer itself
        // there - and the first column is the index, which a bound or a whitelist has
        // nothing to say about anyway.
        if (col == 0)
            return null;

        for (int row = TypeRow + 1; row < named.Height; row++)
        {
            string rowKey = CellAt(sheet, named, row, 0).Value.Trim();

            // Past the schema rows and into the data.
            if (!rowKey.StartsWith(':'))
                return null;

            if (!string.Equals(rowKey, key, StringComparison.OrdinalIgnoreCase))
                continue;

            var cell = CellAt(sheet, named, row, col);
            string text = cell.Value.Trim();

            return (text.Length == 0 || text == "-") ? null : cell;
        }

        return null;
    }

    /// <summary>Which numeric type a `number` column's values need.</summary>
    private enum NumberKind
    {
        /// <summary>Every value is a whole number inside 32 bits.</summary>
        Narrow,

        /// <summary>Whole numbers, but one of them does not fit in 32 bits.</summary>
        Wide,

        /// <summary>At least one value has a fractional part.</summary>
        Real,
    }

    /// <summary>
    /// Reads a `number` column's values to decide which numeric type carries them.
    /// </summary>
    /// <remarks>
    /// Every data cell rather than a sample: a column's one fractional value, or its one
    /// value past 32 bits, can be on any row, and guessing from the first few would give a
    /// type that depends on where it happens to sit.
    ///
    /// `-` and blank do not count - they are "no value" - and neither does a hexadecimal
    /// literal, which is a whole number written another way.
    /// </remarks>
    private static NumberKind DetectNumberType(RawSheet sheet, RawNamedRange named, int col)
    {
        var kind = NumberKind.Narrow;

        for (int row = TypeRow + 1; row < named.Height; row++)
        {
            string key = CellAt(sheet, named, row, 0).Value.Trim();

            if (key.StartsWith(':') || key.StartsWith('#'))
                continue;
            if (key.Length == 0)
                break;

            string text = CellAt(sheet, named, row, col).Value.Trim();
            if (text.Length == 0 || text == "-" || LooksHexadecimal(text))
                continue;

            if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                                 CultureInfo.InvariantCulture, out double value))
            {
                // Not a number at all. Left for the value parser to report against the cell,
                // which says more than a type decision made here could.
                continue;
            }

            // Fractional wins outright - a real cannot be carried by either integer type.
            if (value != Math.Floor(value))
                return NumberKind.Real;

            if (value > int.MaxValue || value < int.MinValue)
                kind = NumberKind.Wide;
        }

        return kind;
    }

    private static bool IsInteger(Models.ValueType type)
        => type == Models.ValueType.Int32 || type == Models.ValueType.Int64;

    /// <summary>
    /// Whether a cell is written in base 16, for the type detection to step over.
    /// </summary>
    /// <remarks>
    /// The conversion itself is no longer here. `0x` used to be turned into a decimal by this
    /// layout, because a colour column really does hold `0x5f0300` and the exporter this one
    /// is measured against reads it as hexadecimal. The core reads those literals now, on
    /// every numeric type, so the layout has nothing left to say about them.
    ///
    /// What remains is the type detection, which must not let a hex cell decide the width:
    /// it parses cells as decimals to choose between `int`, `bigint` and `double`, and
    /// `0x5f0300` is not one.
    /// </remarks>
    private static bool LooksHexadecimal(string text)
        => text.Length > 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X');

    /// <summary>
    /// The type's empty value, for a cell that says it holds none.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than parsed from `""`: the value parser rejects an empty string
    /// for every numeric type, correctly - a blank where a number belongs is exactly what
    /// it exists to catch - and what is happening here is a column saying it has no value,
    /// which is a different thing.
    /// </remarks>
    private Cell EmptyCell(DataColumn column, RawCell rawCell, char? arrayDelimiter)
    {
        // The same parse a value goes through, given nothing. It used to be a switch of its
        // own here, which listed the scalars and answered `""` for everything else - so a
        // `[number]` column holding `-` got an empty string where every reader expects an
        // array, and the binary exporter cast it and threw. Two switches for one question,
        // and only one of them knew about arrays.
        object? value = _context.ParseValue(
            column.Field.Type, column.Field.EnumOrNull, "", rawCell.Location, arrayDelimiter,
            required: false);

        // HasValue false is the whole point of this cell existing separately: it carries the
        // type's empty value while saying the sheet did not put one there, which is what lets
        // an array be trimmed without guessing that a zero means absent.
        return new Cell { RawCell = rawCell, Value = value, HasValue = false };
    }

    /// <summary>
    /// Settles a table whose columns turned out to be one axis of a grid: its value array
    /// must not be trimmed, and the ids that named those columns become a table of their own.
    /// </summary>
    /// <remarks>
    /// The design and the two shapes turned down on the way to this one are in
    /// spec/matrix-tables.md.
    /// </remarks>
    private void PrepareMatrix(
        Models.Table table, List<(long Id, RawCell NameCell)> columns, RawSheet sheet)
    {
        // Every element of the array is one column of the grid, so its type is one type. The
        // rewrite to `Value[k]` means the folding would otherwise report this as a group
        // whose columns disagree, which is true but says nothing about grids.
        var elements = table.Fields.Where(f => f.GroupName == MatrixValueField).ToList();

        foreach (var element in elements)
        {
            if (element.Type == elements[0].Type)
                continue;

            throw new TabbitException(element.TypeLocation,
                $"Table `{table.Name}` is a grid - its column names are ids - but its columns "
                + $"are not all one type: `{elements[0].TypeName}` and `{element.TypeName}`. "
                + $"Every column of a grid is one element of one array, so they state one type.");
        }

        // Position is meaning here. Trimming ends an array at its last value, which is right
        // for a list of slots and wrong for a grid: shorten one row and every lookup past
        // that point reads a different column - or nothing.
        table.TrimTrailingArrayElements = false;

        // The element names above are positional, and another set of this table's rows is laid
        // onto its columns by name. A locale with fewer columns of this axis would then be
        // shifted from its first missing id onwards - measured, and it was: two elements of a
        // 735-column grid read another town's value. So each element says what to match it by,
        // and that is the column id. spec/table-row-sets.md ~ spec/matrix-tables.md.
        for (int position = 0; position < columns.Count && position < elements.Count; position++)
            elements[position].SetAlignName = $"{MatrixValueField}#{columns[position].Id}";

        // A sheet that is another set of some table's rows makes no column table of its own.
        // Once folded, the positions are the table's, so this one would state positions that
        // nothing holds any more. Which sheets those are is the source's own setting, and this
        // layout can read it. spec/table-row-sets.md.
        if (IsAnotherSetsRows(table, sheet))
        {
            Log.Information(
                $"`{table.Name}` is a grid of {columns.Count} column(s) and another set of some "
                + $"table's rows, so its column ids come from the table it folds into. "
                + $"({table.Location})");

            return;
        }

        var companion = new Models.Table
        {
            Location = table.Location,
            TargetSide = table.TargetSide,
            RawName = table.RawName + MatrixColumnTableSuffix,
            Name = table.Name + MatrixColumnTableSuffix,
            FoldSerialFields = false,
            Comment = $"Which element of `{table.Name}.{MatrixValueField}` each column id is at.",
        };

        var id = new Field
        {
            OwnerTable = companion,
            NameLocation = table.Location,
            TypeLocation = table.Location,
            DetailTypeLocation = table.Location,
            TargetSideLocation = table.Location,
            Name = "Id",
            RawName = "Id",
            TypeName = "int",
            Type = Models.ValueType.Int32,
            Indexing = true,
            IsRequired = true,
            TargetSide = TargetSide.Both,
            Comment = "The column id, as the grid's header wrote it.",
            Index = 0,
        };

        var at = new Field
        {
            OwnerTable = companion,
            NameLocation = table.Location,
            TypeLocation = table.Location,
            DetailTypeLocation = table.Location,
            TargetSideLocation = table.Location,
            Name = "At",
            RawName = "At",
            TypeName = "int",
            Type = Models.ValueType.Int32,
            IsRequired = true,
            TargetSide = TargetSide.Both,
            Comment = "Its element of the value array, counting from zero.",
            Index = 1,
        };

        companion.Fields.Add(id);
        companion.Fields.Add(at);

        for (int position = 0; position < columns.Count; position++)
        {
            var cell = columns[position].NameCell;
            long axisId = columns[position].Id;

            // The ids these sheets use fit an int, and the table is generated with an int
            // key. A wider one is refused here rather than wrapping, because a silently
            // truncated id looks up the wrong column for the rest of the build.
            if (axisId is < int.MinValue or > int.MaxValue)
            {
                throw new TabbitException(cell.Location,
                    $"Column id `{axisId}` of `{table.Name}` does not fit a 32-bit integer, and "
                    + $"`{companion.Name}.Id` is one.");
            }

            companion.Data.Add(new List<Cell>
            {
                new Cell { RawCell = cell, Value = (int)axisId, HasValue = true },
                new Cell { RawCell = cell, Value = position, HasValue = true },
            });
        }

        _context.CheckPrimaryIndexValidity(id);
        _context.AssignTags(companion);

        Log.Information(
            $"`{table.Name}` is a grid of {columns.Count} column(s); their ids are in "
            + $"`{companion.Name}`. ({table.Location})");

        // Added here rather than returned, because a grid produces two tables and every other
        // shape produces one - threading a second return value through for this one case would
        // put a grid's existence in the signature of everything that reads a table.
        _context.Model.Tables.Add(companion);
    }

    /// <summary>
    /// Whether this table's name says it is another set of some table's rows.
    /// </summary>
    /// <remarks>
    /// The same pattern the folding uses, read from the same place - the source's own setting.
    /// Asked here because a grid decides one thing differently when it is a set, and the
    /// answer is available before the folding runs. spec/table-row-sets.md.
    /// </remarks>
    private static bool IsAnotherSetsRows(Models.Table table, RawSheet sheet)
    {
        string pattern = (sheet.Layout ?? SheetLayout.Default).TableRowSets;

        if (pattern.Length == 0)
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(table.RawName, pattern);

        if (!match.Success)
            return false;

        var group = match.Groups["table"];

        return group.Success && group.Value.Length > 0
               && !string.Equals(group.Value, table.RawName, StringComparison.Ordinal);
    }

    /// <summary>A cell of the rectangle, addressed from its top-left.</summary>
    private static RawCell CellAt(RawSheet sheet, RawNamedRange named, int row, int column)
        => sheet.Rows[named.Row + row][named.Column + column];
}
