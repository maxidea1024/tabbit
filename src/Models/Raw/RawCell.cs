using Newtonsoft.Json;

namespace Tabbit.Models.Raw;

/// <summary>
/// One cell as read from a sheet, before any meaning is attached to it.
///
/// Everything is text at this stage. The importers render each cell the way the
/// cooker will read it, and the cooker decides what type it should be from the
/// table's type row.
/// </summary>
public class RawCell
{
    /// <summary>
    /// Where the cell is.
    ///
    /// Carried on every cell so a diagnostic raised much later can still point at
    /// the sheet - and at a clickable URL, for Google Sheets sources.
    /// </summary>
    [JsonIgnore]
    public required Location Location { get; set; }

    /// <summary>Cell contents as text, trimmed.</summary>
    public required string Value { get; set; }

    /// <summary>
    /// The Excel error this cell's formula produced - `#N/A`, `#REF!` - or empty.
    /// </summary>
    /// <remarks>
    /// Recorded rather than reported. **Whether a broken formula matters depends on whether
    /// anything reads the cell**, and the stage that reads workbooks cannot know that: which
    /// columns of a named rectangle carry data is the layout's answer, and the layout has not
    /// run yet. A sheet can hold whole columns of working formulas beside its data - one
    /// project's sheets hold ten thousand cells of them - and reporting those says nothing
    /// about the data.
    ///
    /// So the value is already empty here, and the policy is applied later against the cells
    /// that became values. spec/formula-errors.md.
    /// </remarks>
    public string FormulaError { get; set; } = "";

    // **A cell's note is not read. That is a decision, not a gap.**
    //
    // The little pop-up note attached to a cell used to arrive here, so that a layout could
    // take it as the description of whatever the cell defined. It is a feature carried over
    // from the tool this one replaced, where it existed and where - measured against the
    // sheets people actually wrote - **nobody used it.** The reason is the spreadsheet
    // rather than either tool: a note is awkward to type, invisible until hovered, and there
    // is no way to see a column of them at once. What a sheet uses to describe a column is a
    // cell, and that is where `Field.Comment` comes from.
    //
    // Reading them was not free. Every workbook had its `xl/comments*.xml` parts parsed, a
    // `.xlsb` package had its entry list scanned, every cell paid a dictionary lookup, and
    // every cell held a string reference for the length of the run.
    //
    // So it is not read from a workbook, not read from a hosted document, and **not asked
    // for over the wire** - the field mask in `GoogleSheetsImporter` is where that last one
    // is enforced. A sheet that wants to say something about a column says it in a cell.
}
