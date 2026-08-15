namespace Tabbit.Models;

/// <summary>
/// Where a sheet was read from.
/// </summary>
public enum SourceSheetsType
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>An Excel workbook on disk.</summary>
    Xlsx,

    /// <summary>A Google Sheets document fetched over the API.</summary>
    GoogleSheets,
}
