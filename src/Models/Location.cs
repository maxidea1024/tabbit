namespace Tabbit.Models;

/// <summary>
/// Declared Cell Location
/// </summary>
public class Location
{
    /// <summary>.xlsx file name or google-sheet id</summary>
    /// <remarks>
    /// Separators are always `/`, whatever the machine uses. This ends up in generated code
    /// as the comment saying which cell a member came from, in the HTML pages, and in every
    /// history record - so keeping the platform's own separator meant the same workbook
    /// produced different output on Windows and Linux. A team with both sees that as a diff
    /// in every generated file, and this repository saw it as a golden tree that could only
    /// ever match the machine it was recorded on.
    /// </remarks>
    public string Filename
    {
        get => _filename;
        set => _filename = value?.Replace('\\', '/') ?? "";
    }
    private string _filename = "";

    /// <summary>Set only for Google Sheets, where a cell has a URL to link to.</summary>
    public string SheetUrl { get; set; } = ""; // built per cell rather than cached, so moving an entity updates its link

    /// <summary>Sheet Name</summary>
    public string Sheet { get; set; } = "";

    /// <summary>Column</summary>
    public int Column { get; set; }

    /// <summary>Row</summary>
    public int Row { get; set; }

    /// <summary>
    /// Whether this points into a text file rather than into a sheet.
    /// </summary>
    /// <remarks>
    /// For the validation rules, which are C# files: a report from one has to name a line
    /// and a column of that file, and `Item.cs :  : A13` is not that. Set explicitly rather
    /// than inferred from an empty <see cref="Sheet"/>, because a sheet location that
    /// somehow lacked its sheet name would then silently print as a source position.
    /// </remarks>
    public bool InTextFile { get; set; }

    /// <summary>A position in a text file, counted from one as an editor counts.</summary>
    public static Location OfTextFile(string filename, int line, int column)
        => new Location
        {
            Filename = filename,
            InTextFile = true,

            // Stored in the sheet fields' own convention - zero based - so CellRange and
            // everything else that reads them keeps one meaning.
            Row = line - 1,
            Column = column - 1,
        };

    public Location CloneWithXY(int column, int row)
    {
        return new Location {
            Filename = this.Filename,
            SheetUrl = this.SheetUrl,
            Sheet = this.Sheet,
            InTextFile = this.InTextFile,
            Column = column,
            Row = row,
        };
    }

    public override string ToString()
    {
        //return $"{Filename} : {Sheet} : {CellRange}";
        //return $"{Filename}/{Sheet}:{CellRange}";
        if (!string.IsNullOrEmpty(SheetUrl))
            return SheetUrl;

        // The shape every compiler and editor already agrees on, so a report from a rule
        // file can be clicked in a terminal the same way a build error can.
        if (InTextFile)
            return $"{Filename}({Row + 1},{Column + 1})";

        return $"{Filename} : {Sheet} : {CellRange}";
    }

    public string CellRange => $"{ColumnName(Column)}{Row + 1}";

    /// <summary>
    /// Spreadsheet column label for a zero-based column index:
    /// 0 -> A, 25 -> Z, 26 -> AA, 701 -> ZZ, 702 -> AAA.
    ///
    /// This is bijective base-26, not plain base-26: there is no zero digit, so
    /// each carry subtracts one. Getting it wrong is what made every reference
    /// past column X point at the wrong cell, including the `&range=` fragment in
    /// the Google Sheets deep links.
    /// </summary>
    public static string ColumnName(int column)
    {
        if (column < 0)
            return "?";

        var name = new System.Text.StringBuilder();

        for (int n = column; n >= 0; n = n / 26 - 1)
            name.Insert(0, (char)('A' + n % 26));

        return name.ToString();
    }
}
