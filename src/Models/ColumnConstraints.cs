using System.Collections.Generic;
using Newtonsoft.Json;

namespace Tabbit.Models;

/// <summary>
/// What a sheet declared about the values a column may hold, beyond its type.
/// </summary>
/// <remarks>
/// A type says what a value is; these say which of those values are allowed. `int` accepts
/// -1 and 2,000,000,000 alike, and a sheet that means "1 to 99" has nowhere else to say so.
///
/// Empty unless a layout filled it in, and no layout has to: this is a place to put what a
/// notation already carries, not a notation of its own. One project's sheets declare exactly
/// these in rows of their own and check them afterwards with a script; reading them here
/// moves the check to where the cell is, so a diagnostic can point at it.
///
/// Every part carries the cell it came from, because a wrong bound and a wrong value are
/// different mistakes and a message should say which cell is at fault.
///
/// spec/column-constraints.md.
/// </remarks>
public sealed class ColumnConstraints
{
    /// <summary>Smallest value the column accepts, or null when the sheet set none.</summary>
    public double? Minimum { get; set; }

    /// <summary>Where the minimum was declared. Null when there is no minimum.</summary>
    [JsonIgnore]
    public Location? MinimumLocation { get; set; }

    /// <summary>Largest value the column accepts, or null when the sheet set none.</summary>
    public double? Maximum { get; set; }

    /// <summary>Where the maximum was declared. Null when there is no maximum.</summary>
    [JsonIgnore]
    public Location? MaximumLocation { get; set; }

    /// <summary>
    /// Whether this field must have a value wherever the record it belongs to exists.
    /// </summary>
    /// <remarks>
    /// Weaker than required and stronger than nothing: a record array's row may hold no
    /// record at all, and then the question does not arise, but a record that exists with
    /// this member blank is a row that says two things at once.
    ///
    /// A record exists when **any** of its members has a value - the same definition the
    /// trimming of a record array already uses, because the two ask the same question.
    ///
    /// Meaningless on a column that is not a record member, and refused there.
    /// spec/record-member-optionality.md.
    /// </remarks>
    public bool RequiredInRecord { get; set; }

    /// <summary>Where the sheet said so, for the diagnostic that refuses it.</summary>
    public Location? RequiredInRecordLocation { get; set; }

    /// <summary>
    /// The values the column accepts, as the sheet wrote them, or null for no whitelist.
    /// </summary>
    /// <remarks>
    /// Compared as text, because that is what the sheet holds and what a whitelist of
    /// labels means. A numeric column with a whitelist of numbers compares the same way,
    /// since both sides went through the same parse.
    /// </remarks>
    public IReadOnlyList<string>? AllowedValues { get; set; }

    /// <summary>Where the whitelist was declared. Null when there is none.</summary>
    [JsonIgnore]
    public Location? AllowedValuesLocation { get; set; }

    /// <summary>
    /// Tables the column's value must name a row in - one of them is enough. Null when the
    /// sheet named none.
    /// </summary>
    /// <remarks>
    /// A whitelist of sources rather than of values: <see cref="AllowedValues"/> lists what
    /// may be written, this lists where what is written has to exist. The value stays the
    /// integer it was, which is what makes this a constraint and not a reference - a
    /// `foreign` column changes type, carries a record into the generated code and resolves
    /// to exactly one table, and none of that happens here.
    ///
    /// Several tables is the ordinary case and one is not a special one: the layouts that
    /// declare this treat "in this table" and "in any of these" as the same check with a
    /// list of a different length.
    ///
    /// spec/multi-target-references.md.
    /// </remarks>
    public IReadOnlyList<string>? ReferencedTables { get; set; }

    /// <summary>Where the tables were named. Null when none were.</summary>
    [JsonIgnore]
    public Location? ReferencedTablesLocation { get; set; }

    /// <summary>Whether anything at all was declared.</summary>
    [JsonIgnore]
    public bool IsEmpty
        => Minimum is null && Maximum is null && !RequiredInRecord
            && (AllowedValues is null || AllowedValues.Count == 0)
            && (ReferencedTables is null || ReferencedTables.Count == 0);
}
