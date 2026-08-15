namespace Tabbit.Models.Raw;

/// <summary>
/// One of a workbook's defined names, as a rectangle of a sheet's cell grid.
/// </summary>
/// <remarks>
/// A layout may take a defined name as the thing that declares a table - which is how one
/// real project's workbooks are written, and it is why this exists. Two consequences worth
/// knowing:
///
///   * the sheet tab's name means nothing there, and
///   * one sheet can hold several tables, side by side, because a name can cover any
///     rectangle.
///
/// Coordinates are indexes into <see cref="RawSheet.Rows"/>, not Excel's. The importer
/// translates, because <see cref="RawSheet.Optimize"/> trims blank margins and everything
/// downstream indexes the trimmed grid.
/// </remarks>
public sealed class RawNamedRange
{
    /// <summary>The defined name, exactly as the workbook spells it.</summary>
    public required string Name { get; init; }

    /// <summary>First row of the rectangle, as an index into the sheet's rows.</summary>
    public required int Row { get; init; }

    /// <summary>First column, as an index into a row's cells.</summary>
    public required int Column { get; init; }

    /// <summary>How many rows the rectangle covers.</summary>
    public required int Height { get; init; }

    /// <summary>How many columns it covers.</summary>
    public required int Width { get; init; }

    public override string ToString() => $"{Name} ({Column},{Row} {Width}x{Height})";
}
