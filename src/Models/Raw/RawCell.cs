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

    /// <summary>
    /// The cell's note or comment, with the author prefix that Excel and Google
    /// Sheets prepend removed. Becomes the doc comment of whatever the cell defines.
    /// </summary>
    public required string Note { get; set; }
}
