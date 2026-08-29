using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Models.Raw;
using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The `:matrix` declaration - a grid whose column headings are the keys of a second axis.
/// </summary>
/// <remarks>
/// **Two tables come out and nothing below this learns a new shape.** The values table holds
/// one row per row-axis key with an array of cells beside it, which is the shape a sheet
/// already writes as `value[0]` and `value[1]`; the column table holds one row per column-axis
/// key with the position it has in those arrays. <see cref="MatrixShape"/> links them, and the
/// generators are the only thing that reads the link.
///
/// So what this file adds is a way to **write** a grid. The rule that reads one already written
/// - columns whose names are integers - is a different thing in a different layout, and the two
/// are meant to coexist: spec/layout/matrix-declaration.md section 5.
/// </remarks>
public sealed partial class TabbitLayoutParser
{
    /// <summary>The column table's name is the declaration's with this behind it.</summary>
    /// <remarks>
    /// The same word the reading rule's table uses (spec/layout/matrix-tables.md), so a grid's
    /// axis is found under one name whichever notation wrote the sheet.
    /// </remarks>
    private const string ColumnTableSuffix = "Column";

    /// <summary>The column table's second field: where in the grid arrays a key sits.</summary>
    private const string ColumnAtFieldName = "At";

    private (Models.Table Values, Models.Table Columns) ParseMatrix(EntityBlock block)
    {
        Log.Information($"Parsing matrix `{block.Name}`. ({block.Location})");

        var fieldRow = block.Sheet.Rows[block.HeaderRows[RowKeyField]];
        var typeRow = block.Sheet.Rows[block.HeaderRows[RowKeyType]];
        var colRow = block.Sheet.Rows[block.HeaderRows[RowKeyCol]];
        var descRow = RowOrNull(block, RowKeyDesc);

        int rowKeyColumn = block.FirstColumn;
        int gridColumn = block.FirstColumn + 1;

        if (block.LastColumn < gridColumn)
        {
            throw new TabbitException(block.Location,
                Message.Of(TabbitLayoutMessages.MatrixNeedsTwoColumns, ("Entity", block.Name)));
        }

        string rowAxisWritten = MatrixName(block, fieldRow, rowKeyColumn, "row");
        string gridWritten = MatrixName(block, fieldRow, gridColumn, "grid");

        // **Only two cells of these rows mean anything.** The grid is one group running to the
        // last column, so its name and its type are stated once at its first column - the same
        // rule an array group follows in a table. A third cell would be a name for a column
        // that is a key rather than a field, so it is reported instead of read.
        foreach (var row in new[] { fieldRow, typeRow, descRow })
            RefuseCellsBeyondTheGrid(block, row, gridColumn);

        var columnAxis = ReadColumnAxis(block, colRow);
        var keyColumns = GridColumns(block, colRow);

        var values = ParseMatrixValues(
            block, fieldRow, typeRow, descRow,
            rowKeyColumn, gridColumn, rowAxisWritten, gridWritten, keyColumns);

        var columns = ParseMatrixColumns(block, colRow, columnAxis, keyColumns);

        values.Matrix = new MatrixShape
        {
            RowKeyField = values.Fields[0].Name,
            GridField = gridWritten.ToPascalCase(),
            ColumnTable = columns.Name,
            ColumnKeyField = columns.Fields[0].Name,
            ColumnAtField = ColumnAtFieldName,
        };

        return (values, columns);
    }

    /// <summary>
    /// A `:field` cell of a grid: a plain name, with none of the marks a table's columns take.
    /// </summary>
    /// <remarks>
    /// `@N` and a tombstone are refused because **a grid's columns are not schema** - the keys
    /// ride in the data, so there is no column for a tag to name and nothing for a tombstone to
    /// reserve. `*` is refused because a grid has one index and it is the row axis.
    /// spec/layout/matrix-declaration.md section 2.3.
    /// </remarks>
    private string MatrixName(EntityBlock block, List<RawCell> fieldRow, int column, string which)
    {
        var cell = Cell(fieldRow, column);
        string written = (cell?.Value ?? "").Trim();

        if (written.Length == 0)
        {
            throw new TabbitException(cell?.Location ?? block.Location,
                Message.Of(TabbitLayoutMessages.MatrixNameMissing,
                    ("Entity", block.Name), ("Which", which)));
        }

        foreach (char mark in new[] { '@', '*', '#' })
        {
            if (!written.Contains(mark))
                continue;

            throw new TabbitException(cell!.Location,
                Message.Of(TabbitLayoutMessages.MatrixNameMarked,
                    ("Entity", block.Name), ("Written", written), ("Mark", mark.ToString())));
        }

        _context.RequiresIdentifier(written.ToPascalCase(), cell!.Location);

        return written;
    }

    private void RefuseCellsBeyondTheGrid(EntityBlock block, List<RawCell>? row, int gridColumn)
    {
        if (row is null)
            return;

        for (int column = gridColumn + 1; column <= block.LastColumn; column++)
        {
            var cell = Cell(row, column);

            if ((cell?.Value ?? "").Trim().Length == 0)
                continue;

            throw new TabbitException(cell!.Location,
                Message.Of(TabbitLayoutMessages.MatrixHeaderBeyondGrid,
                    ("Entity", block.Name), ("Written", cell.Value.Trim())));
        }
    }

    /// <summary>The column axis: its name and the type expression its keys are read as.</summary>
    /// <remarks>
    /// One cell holds both, in the declaration notation's order - `goods foreign Goods`. The
    /// axis has no column of its own to hang a name and a type on, and inventing a row for each
    /// would be two rows that each hold one cell. Section 2.1.
    /// </remarks>
    private (string Name, RawCell TypeCell) ReadColumnAxis(EntityBlock block, List<RawCell> colRow)
    {
        var cell = Cell(colRow, block.FirstColumn);
        string written = (cell?.Value ?? "").Trim();

        if (written.Length == 0)
        {
            throw new TabbitException(cell?.Location ?? block.Location,
                Message.Of(TabbitLayoutMessages.MatrixColumnAxisMissing, ("Entity", block.Name)));
        }

        int space = written.IndexOfAny([' ', '\t']);

        if (space < 0)
        {
            throw new TabbitException(cell!.Location,
                Message.Of(TabbitLayoutMessages.MatrixColumnAxisNeedsType,
                    ("Entity", block.Name), ("Written", written)));
        }

        string name = written.Substring(0, space).Trim();
        string type = written.Substring(space + 1).Trim();

        _context.RequiresIdentifier(name.ToPascalCase(), cell!.Location);

        // A cell of its own for the type half, so everything that reads a type cell - the
        // expression, its brackets, the location a report points at - reads one the same way.
        var typeCell = new RawCell { Location = cell!.Location, Value = type };

        return (name, typeCell);
    }

    /// <summary>
    /// Which columns of the block hold grid cells, in sheet order.
    /// </summary>
    /// <remarks>
    /// A `#` in the `:col` row is a memo column, exactly as it is in a `:field` row. A column
    /// the row leaves blank has no key, so it is no part of the grid - and if anything is
    /// written under it, that is the same report a table makes about data under a column with
    /// no name.
    /// </remarks>
    private List<int> GridColumns(EntityBlock block, List<RawCell> colRow)
    {
        var found = new List<int>();

        for (int column = block.FirstColumn + 1; column <= block.LastColumn; column++)
        {
            var cell = Cell(colRow, column);
            string written = (cell?.Value ?? "").Trim();

            if (written == OmitMark)
                continue;

            if (written.Length == 0)
            {
                RefuseDataUnderUnnamedColumn(block, column);
                continue;
            }

            found.Add(column);
        }

        if (found.Count == 0)
        {
            throw new TabbitException(block.Location,
                Message.Of(TabbitLayoutMessages.MatrixNoColumnKeys, ("Entity", block.Name)));
        }

        return found;
    }

    private Models.Table ParseMatrixValues(
        EntityBlock block,
        List<RawCell> fieldRow, List<RawCell> typeRow, List<RawCell>? descRow,
        int rowKeyColumn, int gridColumn,
        string rowAxisWritten, string gridWritten, List<int> keyColumns)
    {
        var table = new Models.Table
        {
            Location = block.Location,
            TargetSide = block.TargetSide,
            RawName = block.RawName,
            Name = block.Name,
            Comment = block.Comment,
            MetaTags = MetaTagsOf(block),

            // **A grid is never trimmed.** Where an element sits is what it means here, so an
            // array cut short at the last filled cell would lose the meaning of every position
            // after it - `value[at]` stops being the cell the axis named. The recipe's answer
            // is not read, because this is not the recipe's question.
            TrimTrailingArrayElements = false,

            // And for the same reason a hole in the middle is ordinary: a key the sheet has no
            // value for is a blank cell in the grid, not a shorter row.
            AllowArrayGaps = true,
        };

        var headers = new List<ColumnHeader>
        {
            new()
            {
                Column = rowKeyColumn,
                NameCell = fieldRow[rowKeyColumn],
                Written = rowAxisWritten,
                Indexing = true,
            },
        };

        string group = gridWritten.ToPascalCase();

        for (int at = 0; at < keyColumns.Count; at++)
        {
            headers.Add(new ColumnHeader
            {
                Column = keyColumns[at],

                // Every cell of the grid reads the one type the group stated, so they all take
                // their `:type` and `:desc` from the grid's first column.
                HeaderColumn = gridColumn,
                NameCell = fieldRow[gridColumn],
                Written = gridWritten,
                Path = [new FieldPathStep { Name = group, Index = at }],
                IsGroupFirst = at == 0,
            });
        }

        var sources = new List<FieldSource>();

        foreach (var header in headers)
        {
            AddField(
                table, block, header, header.Path?[0].Index, header.Path,
                typeRow, descRow, targetRow: null, sources);
        }

        _ = table.SerialFields;

        ParseData(table, block, sources);

        _context.CheckPrimaryIndexValidity(table.PrimaryIndexField!);
        _context.AssignTags(table);

        return table;
    }

    /// <summary>
    /// The column axis as a table: one row per key, holding the key and where it sits.
    /// </summary>
    /// <remarks>
    /// A table rather than a constant array, and the reason is the one v107 wrote down: a count
    /// or a name that reaches generated code turns adding a column into a code deploy. Here the
    /// keys are rows, so a grid grows by editing the sheet. The lookup is then the generated
    /// index rather than a scan, which is what the accessor composes with.
    /// </remarks>
    private Models.Table ParseMatrixColumns(
        EntityBlock block, List<RawCell> colRow,
        (string Name, RawCell TypeCell) axis, List<int> keyColumns)
    {
        var table = new Models.Table
        {
            Location = block.Location,
            TargetSide = block.TargetSide,
            RawName = block.RawName + ColumnTableSuffix,
            Name = block.Name + ColumnTableSuffix,
            Comment = block.Comment,
            MetaTags = MetaTagsOf(block),
        };

        var keyHeader = new ColumnHeader
        {
            Column = block.FirstColumn,
            NameCell = colRow[block.FirstColumn],
            Written = axis.Name,
            Indexing = true,
        };

        var keyField = new Field
        {
            OwnerTable = table,
            NameLocation = keyHeader.NameCell.Location,
            TypeLocation = axis.TypeCell.Location,
            DetailTypeLocation = axis.TypeCell.Location,
            TargetSideLocation = block.Location,
            TargetSide = TargetSide.Both,
            Index = 0,
            Comment = "",
            RawName = axis.Name,
            Name = axis.Name.ToPascalCase(),
            NamePath = null,
            Indexing = true,
        };

        ReadType(keyField, keyHeader, axis.TypeCell, block);
        table.Fields.Add(keyField);

        table.Fields.Add(new Field
        {
            OwnerTable = table,
            NameLocation = keyHeader.NameCell.Location,
            TypeLocation = keyHeader.NameCell.Location,
            DetailTypeLocation = keyHeader.NameCell.Location,
            TargetSideLocation = block.Location,
            TargetSide = TargetSide.Both,
            Index = 1,
            Comment = "",
            RawName = ColumnAtFieldName,
            Name = ColumnAtFieldName,
            TypeName = "int",
            Type = Models.ValueType.Int32,
            NamePath = null,
            Indexing = false,
        });

        _ = table.SerialFields;

        for (int at = 0; at < keyColumns.Count; at++)
        {
            var rawCell = colRow[keyColumns[at]];

            var reading = _context.ReadCell(
                keyField.Type, keyField.EnumOrNull, rawCell.Value, rawCell.Location,
                block.Sheet.Layout.DefaultDelimiter,
                required: true,
                onBlankCell: block.Sheet.Layout.OnBlankCell,
                isReference: keyField.IsRef,
                column: $"{table.Name}.{keyField.Name}",
                formulaError: rawCell.FormulaError,
                onFormulaError: block.Sheet.Layout.OnFormulaError,
                timeZone: block.Sheet.Layout.TimeZone);

            table.Data.Add(
            [
                new Cell { RawCell = rawCell, Value = reading.Value, HasValue = reading.HasValue },

                // The position, pointing at the cell that decides it: the key's own heading is
                // where a report about a position belongs, because moving that cell is what
                // moves the position.
                new Cell { RawCell = rawCell, Value = at },
            ]);
        }

        _context.CheckPrimaryIndexValidity(table.PrimaryIndexField!);
        _context.AssignTags(table);

        return table;
    }
}
