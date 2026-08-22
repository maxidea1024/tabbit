using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;

namespace Tabbit.Models.Raw;

/// <summary>
/// One sheet of cells, as read and then squared off by <see cref="Optimize"/>.
/// </summary>
public class RawSheet
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Importing;

    /// <summary>Where the sheet starts, once blank leading rows and columns are trimmed.</summary>
    [JsonIgnore]
    public required Location Location { get; set; }

    /// <summary>
    /// How this sheet is to be read, from the recipe entry that imported it.
    /// </summary>
    /// <remarks>
    /// Never null by the time the cooker sees it - the importers stamp every sheet - but
    /// defaulted here as well, so a sheet built by a test or a fixture generator reads the
    /// way an unconfigured one would.
    /// </remarks>
    [JsonIgnore]
    public SheetLayout Layout { get; set; } = SheetLayout.Default;

    /// <summary>Width after trimming and padding. Every row has exactly this many cells.</summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// The workbook's defined names that point into this sheet, in grid coordinates.
    /// </summary>
    /// <remarks>
    /// For the layouts that take a defined name as a table's boundary rather than a marker
    /// or a sheet tab. Attached by the importer, because that is the only place the
    /// workbook is still open - by the time the cooker runs there is a cell grid and
    /// nothing to ask about names.
    ///
    /// Empty for every other layout, which is every sheet unless the recipe entry asked
    /// for one that uses them.
    /// </remarks>
    public List<RawNamedRange> NamedRanges { get; set; } = [];

    /// <summary>
    /// Rows of cells.
    ///
    /// Rectangular after <see cref="Optimize"/>: the entity scanner indexes rows and
    /// columns freely, so a ragged grid would have it reading past the end of a short
    /// row.
    /// </summary>
    public List<List<RawCell>> Rows { get; set; } = [];

    /// <summary>
    /// Trims the blank margins off a sheet and squares off what is left.
    ///
    /// Sheets arrive with whatever shape the author left behind: blank rows above the
    /// data, blank columns to its left, rows of differing length, and - from Google
    /// Sheets - interior rows omitted entirely rather than sent as blanks. All of the
    /// scanning that follows indexes rows and columns directly, so it is squared off
    /// here once instead of every reader guarding against the shape.
    /// </summary>
    /// <returns>False when nothing usable is left, so the caller can drop the sheet.</returns>
    public bool Optimize()
    {
        // Remove top empty rows
        int topEmptyRowCount = 0;
        foreach (var row in this.Rows)
        {
            if (!IsWholeEmptyRow(row))
                break;

            topEmptyRowCount++;
        }
        if (topEmptyRowCount > 0)
            this.Rows.RemoveRange(0, topEmptyRowCount);

        // Remove bottom empty rows
        int bottomEmptyRowCount = 0;
        for (int i = this.Rows.Count - 1; i >= 0; --i)
        {
            if (IsWholeEmptyRow(this.Rows[i]))
                bottomEmptyRowCount++;
            else
                break;
        }
        if (bottomEmptyRowCount > 0)
            this.Rows.RemoveRange(this.Rows.Count - bottomEmptyRowCount, bottomEmptyRowCount);

        // A row with no cells at all carries no position information, which the
        // padding below would have to invent. Such a row is indistinguishable from
        // an absent one - both importers skip rows the source never materialized -
        // and the gap-filling pass at the end reconstructs interior gaps anyway.
        this.Rows.RemoveAll(row => row.Count == 0);

        // Expand max columns.
        int maxColumnCount = 0;
        foreach (var row in this.Rows)
        {
            if (row.Count > maxColumnCount)
                maxColumnCount = row.Count;
        }

        // Padding runs before the column scans, not after.
        //
        // Sheets arrive ragged: each importer emits cells only up to the last one
        // present in that row. IsWholeEmptyColumn indexes every row at the same
        // column, so scanning first threw IndexOutOfRange as soon as a row was
        // shorter than the run of leading blank columns. Squaring the sheet off
        // first also lets the leading-column removal below apply uniformly -
        // previously it skipped short rows, which shifted the remaining rows out
        // of alignment with them.
        foreach (var row in this.Rows)
        {
            int fill = maxColumnCount - row.Count;
            if (fill <= 0)
                continue;

            // Anchored on the row's own last cell, which already carries the right
            // row index. The previous version tracked a separate counter that was
            // only advanced for rows it padded, so it drifted out of step with the
            // sheet as soon as one row was already full width.
            var anchor = row[^1].Location;

            // Asked for once rather than grown into. A list that is appended to past its
            // capacity doubles and copies what it already holds, so padding a row of eight
            // thousand cells out to sixteen thousand copied the row several times over -
            // 0.75 s of array copying across the sample project's sheets.
            // spec/conversion-time.md section 4.
            row.EnsureCapacity(maxColumnCount);

            for (int i = 0; i < fill; i++)
            {
                RawCell rawCell = new RawCell
                {
                    Location = anchor.CloneWithXY(anchor.Column + 1 + i, anchor.Row),
                    Value = "",
                    Note = ""
                };
                row.Add(rawCell);
            }
        }

        int leftEmptyColumnCount = 0;
        for (int i = 0; i < maxColumnCount; i++)
        {
            if (!IsWholeEmptyColumn(this.Rows, i))
                break;

            leftEmptyColumnCount++;
        }
        if (leftEmptyColumnCount > 0)
        {
            maxColumnCount -= leftEmptyColumnCount;
            foreach (var row in this.Rows)
                row.RemoveRange(0, leftEmptyColumnCount);
        }

        int rightEmptyColumnCount = 0;
        for (int i = maxColumnCount-1; i >= 0; i--)
        {
            if (!IsWholeEmptyColumn(this.Rows, i))
                break;

            rightEmptyColumnCount++;
        }
        if (rightEmptyColumnCount > 0)
        {
            foreach (var row in this.Rows)
                row.RemoveRange(row.Count - rightEmptyColumnCount, rightEmptyColumnCount);
            maxColumnCount -= rightEmptyColumnCount;
        }

        this.ColumnCount = maxColumnCount;


        // Google Sheets omits interior blank rows rather than sending them, so the rows
        // that arrive are not necessarily consecutive. Everything downstream indexes rows
        // directly, so the gaps are filled back in here.

        var rows2 = new List<List<RawCell>>();

        for (int rowIdx = 0; rowIdx < Rows.Count; rowIdx++)
        {
            var currentRow = Rows[rowIdx];

            rows2.Add(currentRow);

            if (rowIdx < Rows.Count - 1)
            {
                var nextRow = Rows[rowIdx+1];

                int distance = nextRow[0].Location.Row - currentRow[0].Location.Row;
                if (distance > 1)
                {

                    for (int insertion = 0; insertion < distance - 1; insertion++)
                    {
                        var row = new List<RawCell>(maxColumnCount);
                        var origin = currentRow[0].Location;

                        for (int colIdx = 0; colIdx < maxColumnCount; colIdx++)
                        {
                            var cell = new RawCell {
                                // `insertion`, not `rowIdx`: these rows sit one
                                // after another below the current row. Using the
                                // outer loop index numbered them by their position
                                // in the sheet instead, so any diagnostic landing
                                // on a filled row pointed at an unrelated cell.
                                Location = origin.CloneWithXY(origin.Column + colIdx, origin.Row + insertion + 1),
                                Value = "",
                                Note = ""
                            };
                            row.Add(cell);
                        }

                        rows2.Add(row);
                    }
                }
            }
        }

        Rows = rows2;


        Log.Information($"Optimize sheet: {this.Location}");
        Log.Information($"  => {this.ColumnCount}x{this.Rows.Count} (Shrink => T:{topEmptyRowCount}, B:{bottomEmptyRowCount}, L:{leftEmptyColumnCount}, R:{rightEmptyColumnCount})");

        return this.Rows.Count > 0 && maxColumnCount > 0;
    }

    /// <summary>Whether every cell in a row is empty.</summary>
    private bool IsWholeEmptyRow(List<RawCell> row)
    {
        foreach (var cell in row)
        {
            if (cell.Value.Length > 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a column is empty in every row.
    ///
    /// Requires the rows to be padded to a common width first - it indexes every row
    /// at the same position.
    /// </summary>
    private bool IsWholeEmptyColumn(List<List<RawCell>> rows, int column)
    {
        foreach (var row in rows)
        {
            if (row[column].Value!.Length > 0)
                return false;
        }

        return true;
    }
}
