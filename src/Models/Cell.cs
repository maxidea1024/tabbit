using Tabbit.Models.Raw;

namespace Tabbit.Models;

/// <summary>Cell</summary>
public class Cell
{
    /// <summary>Raw cell</summary>
    public required RawCell RawCell { get; set; }

    /// <summary>Imported value</summary>
    public required object? Value { get; set; }

    /// <summary>
    /// Whether the sheet gave this cell a value, as opposed to leaving it for the type's
    /// empty one.
    /// </summary>
    /// <remarks>
    /// The layout decides what an absent value looks like - a blank cell in a column typed
    /// `int?`, or whatever mark another notation uses - and everything downstream reads this
    /// bool instead. <see cref="Table.RecordElementCount"/> is what it exists for: trimming a
    /// record array cannot ask whether the value is zero, because a cell holding `0` and a
    /// cell holding nothing both parse to zero and only one of them is the author saying so.
    ///
    /// True by default. A cell that was parsed from something is a cell that had something.
    /// </remarks>
    public bool HasValue { get; set; } = true;

    /// <summary>
    /// Which elements of an array cell the sheet gave a value, or null when the question does
    /// not arise - a scalar column, or an array whose elements are required.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="HasValue"/> rather than folded into it: an array that has no value
    /// and an array whose elements have none are different facts, and one bool cannot carry
    /// both. The length is the array's own, so an element and its answer are found at the
    /// same index.
    ///
    /// spec/types/nullable-array-elements.md.
    /// </remarks>
    public bool[]? ElementHasValue { get; set; }
}
