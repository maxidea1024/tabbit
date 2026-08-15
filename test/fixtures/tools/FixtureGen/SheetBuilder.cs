using System;
using System.Collections.Generic;
using NPOI.SS.UserModel;

namespace Tabbit.FixtureGen;

/// <summary>
/// Thin authoring helper over NPOI that lays out Tabbit entities at an explicit
/// (column, row) origin, so fixtures read the same way they look in Excel.
///
/// Tabbit's layout rules (see ModelCooker.ParseDefinitionRect) are:
///
///   marker row      ~~type:Name[:targetside]~~
///   comment row
///   ...entity body, whose required height is type dependent...
///
/// The body of a `table` is 5 header rows (name / comment / type / detail-type /
/// target-side) followed by data rows. `enum` and `const` have a single throwaway
/// header row followed by data rows.
///
/// The rect scanner grows downward while the cell in the entity's first column is
/// non-empty, and rightward while cells are non-empty, so every fixture body must
/// be a solid rectangle with no holes in the first column or the name row.
/// </summary>
public sealed class SheetBuilder
{
    private readonly ISheet _sheet;

    public SheetBuilder(ISheet sheet)
    {
        _sheet = sheet;
    }

    /// <summary>Writes a single string cell at (column, row), both zero-based.</summary>
    public void Set(int column, int row, string value)
    {
        var r = _sheet.GetRow(row) ?? _sheet.CreateRow(row);
        var c = r.GetCell(column) ?? r.CreateCell(column);
        c.SetCellValue(value ?? "");
    }

    /// <summary>Writes a real numeric cell. Used to reproduce Excel-typed values.</summary>
    public void SetNumeric(int column, int row, double value)
    {
        var r = _sheet.GetRow(row) ?? _sheet.CreateRow(row);
        var c = r.GetCell(column) ?? r.CreateCell(column);
        c.SetCellValue(value);
    }

    /// <summary>
    /// Writes a real Excel date cell (numeric cell carrying a date format).
    /// Tabbit currently reads these as raw serial numbers - see XlsxImporter.SafeCellValue.
    /// </summary>
    public void SetDate(int column, int row, DateTime value, string format = "yyyy-mm-dd hh:mm:ss")
    {
        var r = _sheet.GetRow(row) ?? _sheet.CreateRow(row);
        var c = r.GetCell(column) ?? r.CreateCell(column);
        c.SetCellValue(value);

        var style = _sheet.Workbook.CreateCellStyle();
        style.DataFormat = _sheet.Workbook.CreateDataFormat().GetFormat(format);
        c.CellStyle = style;
    }

    /// <summary>
    /// Writes a formula cell whose cached result is an error.
    ///
    /// Tabbit reads cached formula results rather than evaluating anything, so the
    /// cached value is what matters - and it is what a real workbook carries after
    /// Excel has recalculated and left an error behind.
    /// </summary>
    public void SetFormulaError(int column, int row, string formula, FormulaError error)
    {
        var r = _sheet.GetRow(row) ?? _sheet.CreateRow(row);
        var c = r.GetCell(column) ?? r.CreateCell(column);

        c.SetCellFormula(formula);
        c.SetCellErrorValue(error.Code);
    }

    /// <summary>Writes a horizontal run of string cells starting at (column, row).</summary>
    public void SetRow(int column, int row, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
            Set(column + i, row, values[i]);
    }

    /// <summary>
    /// Lays out a table entity and returns the row index just past the last data row,
    /// so callers can stack entities without hand-counting offsets.
    /// </summary>
    public int Table(int column, int row, TableSpec spec)
    {
        string marker = string.IsNullOrEmpty(spec.TargetSide)
            ? $"~~table:{spec.Name}~~"
            : $"~~table:{spec.Name}:{spec.TargetSide}~~";

        Set(column, row + 0, marker);
        Set(column, row + 1, spec.Comment);

        int width = spec.Fields.Count;
        for (int i = 0; i < width; i++)
        {
            var f = spec.Fields[i];
            Set(column + i, row + 2, f.Name);
            Set(column + i, row + 3, f.Comment);
            Set(column + i, row + 4, f.Type);
            Set(column + i, row + 5, f.DetailType);
            Set(column + i, row + 6, f.TargetSide);
        }

        int dataRow = row + 7;
        foreach (var dataLine in spec.Data)
        {
            if (dataLine.Length != width)
            {
                throw new InvalidOperationException(
                    $"Table `{spec.Name}` declares {width} fields but a data row supplies {dataLine.Length} values.");
            }

            for (int i = 0; i < width; i++)
                Set(column + i, dataRow, dataLine[i]);

            dataRow++;
        }

        return dataRow;
    }

    /// <summary>Lays out an enum entity. Returns the row index just past the last label.</summary>
    public int Enum(int column, int row, EnumSpec spec)
    {
        string marker = string.IsNullOrEmpty(spec.TargetSide)
            ? $"~~enum:{spec.Name}~~"
            : $"~~enum:{spec.Name}:{spec.TargetSide}~~";

        Set(column, row + 0, marker);
        Set(column, row + 1, spec.Comment);

        // Header row is a placeholder that Tabbit skips; it exists for human readers.
        SetRow(column, row + 2, "name", "value", "description");

        int dataRow = row + 3;
        foreach (var label in spec.Labels)
        {
            SetRow(column, dataRow, label.Name, label.Value, label.Comment);
            dataRow++;
        }

        return dataRow;
    }

    /// <summary>Lays out a const entity. Returns the row index just past the last constant.</summary>
    public int Const(int column, int row, ConstSpec spec)
    {
        string marker = string.IsNullOrEmpty(spec.TargetSide)
            ? $"~~const:{spec.Name}~~"
            : $"~~const:{spec.Name}:{spec.TargetSide}~~";

        Set(column, row + 0, marker);
        Set(column, row + 1, spec.Comment);

        SetRow(column, row + 2, "name", "type", "detail-type", "value", "description");

        int dataRow = row + 3;
        foreach (var c in spec.Constants)
        {
            SetRow(column, dataRow, c.Name, c.Type, c.DetailType, c.Value, c.Comment);
            dataRow++;
        }

        return dataRow;
    }
}

public sealed class FieldSpec
{
    public string Name;
    public string Comment = "";
    public string Type;
    public string DetailType = "";
    public string TargetSide = "cs";

    public static FieldSpec Of(string name, string type, string comment = "", string detailType = "", string targetSide = "cs")
        => new FieldSpec { Name = name, Type = type, Comment = comment, DetailType = detailType, TargetSide = targetSide };
}

public sealed class TableSpec
{
    public string Name;
    public string Comment = "";
    public string TargetSide = "";
    public List<FieldSpec> Fields = new List<FieldSpec>();
    public List<string[]> Data = new List<string[]>();

    public TableSpec Field(FieldSpec f) { Fields.Add(f); return this; }
    public TableSpec Row(params string[] values) { Data.Add(values); return this; }
}

public sealed class EnumLabelSpec
{
    public string Name;
    public string Value;
    public string Comment = "";
}

public sealed class EnumSpec
{
    public string Name;
    public string Comment = "";
    public string TargetSide = "";
    public List<EnumLabelSpec> Labels = new List<EnumLabelSpec>();

    public EnumSpec Label(string name, string value, string comment = "")
    {
        Labels.Add(new EnumLabelSpec { Name = name, Value = value, Comment = comment });
        return this;
    }
}

public sealed class ConstSpec
{
    public string Name;
    public string Comment = "";
    public string TargetSide = "";
    public List<ConstantSpec> Constants = new List<ConstantSpec>();

    public ConstSpec Constant(string name, string type, string value, string comment = "", string detailType = "")
    {
        Constants.Add(new ConstantSpec { Name = name, Type = type, Value = value, Comment = comment, DetailType = detailType });
        return this;
    }
}

public sealed class ConstantSpec
{
    public string Name;
    public string Type;
    public string DetailType = "";
    public string Value;
    public string Comment = "";
}
