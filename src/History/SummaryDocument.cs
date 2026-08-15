using System.Collections.Generic;

namespace Tabbit.History;

/// <summary>
/// What a conversion produced, described rather than emitted.
///
/// This is the one document. The report a build writes, the rows a snapshot puts in the
/// history, the JSON the API serves and the page the browser draws are all this shape,
/// because two renderings of the same question drift and nothing notices - the CLI says
/// one row count and the web page says another, and both look right.
///
/// Split into <see cref="Run"/> and <see cref="Data"/> on purpose. Data depends only on
/// the sheets, so two conversions of the same commit produce byte-identical data and a
/// golden comparison can hold it to that. Run is the clock, the machine and the commit,
/// which legitimately differ every time.
/// </summary>
public sealed class SummaryDocument
{
    /// <summary>
    /// Version of this document's shape.
    ///
    /// Read by anything consuming a stored summary, which may have been written by an
    /// older build than the one reading it.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    public required SummaryRun Run { get; set; }

    public required SummaryData Data { get; set; }
}

/// <summary>Who produced this, when, and from what.</summary>
public sealed class SummaryRun
{
    public required string GeneratedAt { get; set; }

    public required string? ToolVersion { get; set; }

    /// <summary>
    /// Name of the recipe, without its directory: the full path is where one machine
    /// keeps its checkout and says nothing about the data.
    /// </summary>
    public required string? Recipe { get; set; }

    /// <summary>
    /// What `--target-side` asked for.
    ///
    /// Recorded because it narrows the *output* of the run - but never this document,
    /// which always describes everything the sheets declared.
    /// </summary>
    public required string RequestedTargetSide { get; set; }

    public required SummaryCommit Commit { get; set; }
}

/// <summary>The commit this conversion is of.</summary>
public sealed class SummaryCommit
{
    public string? Hash { get; set; }
    public string? ShortHash { get; set; }
    public string? Branch { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public string? CommittedAt { get; set; }
    public string? Subject { get; set; }

    /// <summary>Where the identity came from: `none`, `commandLine` or `git`.</summary>
    public required string Origin { get; set; }

    /// <summary>Whether the working copy held changes the commit does not describe.</summary>
    public bool Dirty { get; set; }

    /// <summary>
    /// Whether what changed here can honestly be credited to this commit's author.
    ///
    /// False when nothing identified the conversion, and false when the working copy
    /// was dirty. A report that leaves this out ends up naming the wrong person, which
    /// is worse than naming nobody.
    /// </summary>
    public bool Attributable { get; set; }
}

/// <summary>Everything the sheets declared.</summary>
public sealed class SummaryData
{
    /// <summary>The model fingerprint. Equal means an identical model.</summary>
    public required string Hash { get; set; }

    public required SummaryTotals Totals { get; set; }

    /// <summary>Column count per declared type, such as `int` or `string[]`.</summary>
    public IDictionary<string, int> FieldTypes { get; set; } = new Dictionary<string, int>();

    /// <summary>Column count per side: `cs`, `c`, `s`.</summary>
    public IDictionary<string, int> FieldTargetSides { get; set; } = new Dictionary<string, int>();

    public IReadOnlyList<SummarySource> Sources { get; set; } = [];

    public IReadOnlyList<SummaryTable> Tables { get; set; } = [];

    public IReadOnlyList<SummaryEnum> Enums { get; set; } = [];

    public IReadOnlyList<SummaryConstantSet> ConstantSets { get; set; } = [];
}

/// <summary>One number each, for the top of a report.</summary>
public sealed class SummaryTotals
{
    public int Tables { get; set; }
    public int Rows { get; set; }

    /// <summary>Columns, counted across every table.</summary>
    public int Fields { get; set; }

    public long Cells { get; set; }

    /// <summary>Cells holding nothing. A blank cell, not one holding an empty string.</summary>
    public long EmptyCells { get; set; }

    /// <summary>UTF-8 length of every value, which is the data's size independent of format.</summary>
    public long ContentBytes { get; set; }

    public int Enums { get; set; }
    public int EnumLabels { get; set; }
    public int ConstantSets { get; set; }
    public int Constants { get; set; }

    public int ReferenceFields { get; set; }
    public int ArrayFields { get; set; }
}

/// <summary>One workbook or document the tables came from.</summary>
public sealed class SummarySource
{
    public required string File { get; set; }
    public int Sheets { get; set; }
    public int Tables { get; set; }
    public int Rows { get; set; }
}

/// <summary>A cell in a sheet, as the report links to it.</summary>
/// <summary>Where a value came from, as far as the source could say.</summary>
/// <remarks>
/// Every part is nullable and that is not laxity: these are read back from a database whose
/// columns allow NULL, and a location is written whenever *any* part of it is known. `required`
/// still applies - a caller must decide about each - but null is one of the decisions.
/// </remarks>
public sealed class SummaryLocation
{
    public required string? File { get; set; }
    public required string? Sheet { get; set; }

    /// <summary>Spreadsheet reference, such as `B12`.</summary>
    public required string? Cell { get; set; }

    /// <summary>Deep link, for the sources that have one. Null for a local workbook.</summary>
    public required string? Url { get; set; }
}

public sealed class SummaryTable
{
    public required string Name { get; set; }
    public string RawName { get; set; } = "";
    public required string Hash { get; set; }

    /// <summary>Columns only, so a schema change can be told from a data change.</summary>
    public required string SchemaHash { get; set; }

    public string TargetSide { get; set; } = "";
    /// <remarks>Filled by `Referencing`, which needs the whole model. Not required for that reason:
    /// nothing can supply it where a SummaryTable is built.</remarks>
    public string? Comment { get; set; } = "";
    public required SummaryLocation? Location { get; set; }

    public int RowCount { get; set; }
    public int FieldCount { get; set; }
    public long CellCount { get; set; }
    public long EmptyCellCount { get; set; }
    public long ContentBytes { get; set; }

    public IReadOnlyList<SummaryField> Fields { get; set; } = [];

    /// <summary>Tables this one points at, by name.</summary>
    public IReadOnlyList<string> References { get; set; } = [];

    /// <summary>Tables pointing at this one, by name.</summary>
    public IReadOnlyList<string> ReferencedBy { get; set; } = [];
}

public sealed class SummaryField
{
    public required string Name { get; set; }
    public required string? RawName { get; set; }

    /// <summary>Type as written in the sheet.</summary>
    public required string TypeName { get; set; }

    /// <summary>Type as the model resolved it.</summary>
    public required string Type { get; set; }

    public required string TargetSide { get; set; }
    public required string? Comment { get; set; }
    public required SummaryLocation? Location { get; set; }

    public bool IsIndex { get; set; }
    public bool IsArray { get; set; }
    public bool IsReference { get; set; }
    public required string? RefTable { get; set; }
    public required string? RefField { get; set; }

    /// <summary>Rows where this column is blank.</summary>
    public int EmptyCount { get; set; }

    /// <summary>
    /// How many different values the column holds.
    /// </summary>
    public int DistinctCount { get; set; }

    /// <summary>
    /// Whether <see cref="DistinctCount"/> stopped counting.
    ///
    /// Counting distinct values costs a set of them, and a column of unique keys in a
    /// large table would hold the whole column in memory to report a number equal to
    /// the row count. Past the cap the count is the cap, and this says so rather than
    /// letting a wrong number read as a right one.
    /// </summary>
    public bool DistinctCapped { get; set; }

    /// <summary>Longest value, in characters. Null for the types where length says nothing.</summary>
    public int? MaxLength { get; set; }
}

public sealed class SummaryEnum
{
    public required string Name { get; set; }
    public required string? Comment { get; set; }
    public required string TargetSide { get; set; }
    public required SummaryLocation? Location { get; set; }

    public IReadOnlyList<SummaryEnumLabel> Labels { get; set; } = [];

    /// <summary>Columns typed with this enum, as `Table.field`.</summary>
    public IReadOnlyList<string> UsedBy { get; set; } = [];
}

public sealed class SummaryEnumLabel
{
    public required string Name { get; set; }
    public int Value { get; set; }
    public required string? Comment { get; set; }
}

public sealed class SummaryConstantSet
{
    public required string Name { get; set; }
    public required string? Comment { get; set; }
    public required string TargetSide { get; set; }
    public required SummaryLocation? Location { get; set; }

    public IReadOnlyList<SummaryConstant> Constants { get; set; } = [];
}

public sealed class SummaryConstant
{
    public required string Name { get; set; }
    public required string TypeName { get; set; }
    public required string? Value { get; set; }
    public required string? Comment { get; set; }
}
