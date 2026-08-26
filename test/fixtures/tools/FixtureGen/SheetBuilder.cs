using System;
using System.Collections.Generic;
using NPOI.SS.UserModel;

namespace Tabbit.FixtureGen;

/// <summary>
/// Thin authoring helper over NPOI that lays out Tabbit entities at an explicit
/// (column, row) origin, so fixtures read the same way they look in Excel.
/// </summary>
/// <remarks>
/// The notation is the one `spec/layout/primary-layout.md` describes:
///
///     :table Name(side=s) | description
///     :field              | columns...
///     :type               | types...
///     :desc               | descriptions...
///     :target             | sides...
///                         | data...
///
/// **The column an entity is placed at is its marker column, and the body starts beside it.**
/// So every entity is one cell wider than its columns, which is what a caller stacking
/// entities side by side has to leave room for.
///
/// An entity ends at a blank row, so a fixture that puts something below one leaves a row.
/// </remarks>
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
    /// Lays out a table in the primary layout: a declaration cell, then keyed header rows.
    /// </summary>
    /// <remarks>
    /// **The same <see cref="TableSpec"/> the old notation is written from**, so a fixture pair
    /// for the equivalence gate has one source of truth. Where the two notations spell a column
    /// differently the spec says so, in `PrimaryName` and `PrimaryType`.
    ///
    /// The marker column is the one the declaration sits in and the body starts beside it, so
    /// every row here is one cell wider than the old notation's.
    /// </remarks>
    public int Table(int column, int row, TableSpec spec, params string[] memoColumns)
    {
        var meta = new List<string>();

        if (!string.IsNullOrEmpty(spec.TargetSide))
            meta.Add($"side={spec.TargetSide}");

        if (!string.IsNullOrEmpty(spec.Meta))
            meta.Add(spec.Meta);

        string declaration = meta.Count == 0
            ? $":table {spec.Name}"
            : $":table {spec.Name}({string.Join(", ", meta)})";

        Set(column, row + 0, declaration);
        Set(column + 1, row + 0, spec.Comment);

        int width = spec.Fields.Count;

        Set(column, row + 1, ":field");
        Set(column, row + 2, ":type");
        Set(column, row + 3, ":desc");
        Set(column, row + 4, ":target");

        for (int i = 0; i < width; i++)
        {
            var f = spec.Fields[i];

            Set(column + 1 + i, row + 1, f.NameForPrimary());
            Set(column + 1 + i, row + 2, f.TypeForPrimary());
            Set(column + 1 + i, row + 3, f.Comment);
            Set(column + 1 + i, row + 4, f.TargetSide);
        }

        // Space for the sheet's author, which leaves no trace in the model. Written to the
        // right of the fields so the equivalence gate covers a memo column being ignored
        // rather than merely allowed.
        for (int m = 0; m < memoColumns.Length; m++)
            Set(column + 1 + width + m, row + 1, "#");

        int dataRow = row + 5;
        foreach (var dataLine in spec.Data)
        {
            if (dataLine.Length != width)
            {
                throw new InvalidOperationException(
                    $"Table `{spec.Name}` declares {width} fields but a data row supplies {dataLine.Length} values.");
            }

            for (int i = 0; i < width; i++)
                Set(column + 1 + i, dataRow, dataLine[i]);

            for (int m = 0; m < memoColumns.Length; m++)
                Set(column + 1 + width + m, dataRow, memoColumns[m]);

            dataRow++;
        }

        return dataRow;
    }

    /// <summary>Lays out an enum in the primary layout, whose columns are named.</summary>
    public int Enum(int column, int row, EnumSpec spec)
    {
        string declaration = string.IsNullOrEmpty(spec.TargetSide)
            ? $":enum {spec.Name}"
            : $":enum {spec.Name}(side={spec.TargetSide})";

        Set(column, row + 0, declaration);
        Set(column + 1, row + 0, spec.Comment);

        Set(column, row + 1, ":field");
        SetRow(column + 1, row + 1, "label", "value", "desc");

        int dataRow = row + 2;
        foreach (var label in spec.Labels)
        {
            SetRow(column + 1, dataRow, label.Name, label.Value, label.Comment);
            dataRow++;
        }

        return dataRow;
    }

    /// <summary>
    /// Lays out a constant set, whose columns are named. Returns the row past the last one.
    /// </summary>
    public int Const(int column, int row, ConstSpec spec)
    {
        string declaration = string.IsNullOrEmpty(spec.TargetSide)
            ? $":const {spec.Name}"
            : $":const {spec.Name}(side={spec.TargetSide})";

        Set(column, row + 0, declaration);
        Set(column + 1, row + 0, spec.Comment);

        Set(column, row + 1, ":field");
        SetRow(column + 1, row + 1, "name", "type", "value", "desc");

        int dataRow = row + 2;
        foreach (var c in spec.Constants)
        {
            // The folded type expression here too, which is what turns the five columns the old
            // notation needed into four: an enum names itself instead of writing `enum` beside
            // its name.
            string type = string.IsNullOrEmpty(c.DetailType) ? c.Type : c.DetailType;

            SetRow(column + 1, dataRow, c.Name, type, c.Value, c.Comment);
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

    /// <summary>
    /// A column whose number meant an array: `Tag1` is `tag[0]`.
    /// </summary>
    /// <remarks>
    /// Said here rather than worked out from the name, because a number with no dot did not say
    /// whether it was an array - a recipe setting said, and a sheet could not
    /// see it. The notation says it now, so which columns meant an array is what a fixture
    /// carries over from that setting.
    /// </remarks>
    public static FieldSpec Numbered(
        string name, string type, string comment = "", string detailType = "",
        string targetSide = "cs")
    {
        int digits = name.Length;
        while (digits > 0 && char.IsDigit(name[digits - 1]))
            digits--;

        if (digits == name.Length)
        {
            throw new InvalidOperationException(
                $"`{name}` ends in no number, so there is nothing for the brackets to hold.");
        }

        int at = int.Parse(
            name.Substring(digits), System.Globalization.CultureInfo.InvariantCulture) - 1;

        return new FieldSpec
        {
            Name = name,
            PrimaryName = $"{name.Substring(0, digits)}[{at}]",
            Type = type,
            Comment = comment,
            DetailType = detailType,
            TargetSide = targetSide,
        };
    }

    /// <summary>
    /// What the primary layout writes in `:field`, where that differs from the old spelling.
    /// </summary>
    /// <remarks>
    /// Null where the two notations agree, which is most columns - `Pos.X` and `*Code` are
    /// written the same either way. It is the numbered groups that differ, because the old
    /// notation put the number in the name and counted from one (`Slot1.Id`) while this one
    /// brackets it and counts from zero (`Slot[0].Id`).
    ///
    /// Spelled out rather than translated. A generator that rewrote names by rule would be a
    /// second implementation of the notation, and the gate comparing the two sheets would then
    /// be testing that implementation against the parser rather than the notations against
    /// each other.
    /// </remarks>
    public string PrimaryName;

    /// <summary>
    /// The folded type expression, where it is not simply the type and detail joined.
    /// </summary>
    public string PrimaryType;

    /// <summary>
    /// A role written as keys, or null when the type is not one.
    /// </summary>
    /// <remarks>
    /// A role is a key beside the type rather than a name in front of it: the old notation wrote
    /// `text` as the type and put the group either in its own brackets or in the detail cell,
    /// and this one writes a `string` that says what it is for. The group and its namespace were
    /// one comma-joined value there and are two keys here.
    /// </remarks>
    private static string Role(string type, string detail)
    {
        foreach (string role in new[] { "text", "asset" })
        {
            foreach (string suffix in new[] { "", "[]", "?", "[]?" })
            {
                if (!string.Equals(type, role + suffix, StringComparison.Ordinal))
                    continue;

                if (detail.Length == 0)
                    return $"string{suffix} ({role})";

                var parts = detail.Split(',');
                string keys = role + "=" + parts[0].Trim();

                if (parts.Length > 1)
                    keys += ", namespace=" + parts[1].Trim();

                return $"string{suffix} ({keys})";
            }
        }

        return null;
    }

    /// <summary>The `:field` cell the primary layout gets for this column.</summary>
    /// <remarks>
    /// The dotted numbers translate by rule - `Slot1.Id` is `Slot[0].Id`, and a level that is
    /// nothing but digits is a level with no name, so `Grid1.2` is `Grid[0][1]`. The numbers
    /// move from counting at one to counting at zero.
    ///
    /// **A number with no dot does not translate.** `Tag1` beside `Tag2` was two fields or one
    /// array depending on a setting the sheet could not see, so which it was is the fixture's
    /// to say - in `PrimaryName`.
    /// </remarks>
    public string NameForPrimary() => PrimaryName ?? Bracketed(Name);

    /// <summary>Turns a dotted numbered name into the bracket form.</summary>
    private static string Bracketed(string name)
    {
        if (!name.Contains('.'))
            return name;

        var built = new System.Text.StringBuilder();

        foreach (string part in name.Split('.'))
        {
            int digits = part.Length;
            while (digits > 0 && char.IsDigit(part[digits - 1]))
                digits--;

            string stem = part.Substring(0, digits);
            string number = part.Substring(digits);

            if (number.Length == 0)
            {
                built.Append(built.Length == 0 ? "" : ".").Append(part);
                continue;
            }

            int at = int.Parse(number, System.Globalization.CultureInfo.InvariantCulture) - 1;

            // A level that is nothing but digits has no name of its own, so its brackets go
            // straight onto the level above rather than after a dot.
            if (stem.Length == 0)
                built.Append('[').Append(at).Append(']');
            else
                built.Append(built.Length == 0 ? "" : ".").Append(stem).Append('[').Append(at).Append(']');
        }

        return built.ToString();
    }

    /// <summary>
    /// The `:type` cell the primary layout gets: one expression rather than a pair.
    /// </summary>
    /// <remarks>
    /// The pair folds by rule for the two kinds that had a detail cell - an enum names itself
    /// and a reference keeps its keyword - so a fixture only states the type when it wants
    /// something else.
    /// </remarks>
    public string TypeForPrimary()
    {
        if (PrimaryType != null)
            return PrimaryType;

        // The old notation could put the group inside the type's own brackets - `text(Common)` -
        // as well as in the detail cell, and the column's own marks went after them:
        // `asset(sfx)?`. Both say the same thing and both become keys here.
        int open = Type.IndexOf('(');
        int close = Type.LastIndexOf(')');

        if (open > 0 && close > open)
        {
            string bare = Type.Substring(0, open);
            string inside = Type.Substring(open + 1, close - open - 1);
            string marks = Type.Substring(close + 1);

            return Role(bare + marks, inside)
                   ?? throw new InvalidOperationException(
                       $"Column `{Name}` is typed `{Type}`, and the fixture does not say how the "
                       + "primary layout writes that. Set PrimaryType.");
        }

        if (string.IsNullOrEmpty(DetailType))
        {
            return Type switch
            {
                "text" => "string (text)",
                "text[]" => "string[] (text)",
                "text?" => "string? (text)",
                "asset" => "string (asset)",
                "asset[]" => "string[] (asset)",
                "asset?" => "string? (asset)",
                _ => Type,
            };
        }

        // An enum names itself, whether one value or a delimited list of them - the brackets
        // belong to the column and the detail cell held the enum.
        if (string.Equals(Type, "enum", StringComparison.Ordinal))
            return DetailType;

        if (string.Equals(Type, "enum[]", StringComparison.Ordinal))
            return DetailType + "[]";

        if (string.Equals(Type, "enum?", StringComparison.Ordinal))
            return DetailType + "?";

        if (string.Equals(Type, "enum[]?", StringComparison.Ordinal))
            return DetailType + "[]?";

        if (string.Equals(Type, "enum?[]", StringComparison.Ordinal))
            return DetailType + "?[]";

        if (string.Equals(Type, "foreign", StringComparison.Ordinal))
            return "foreign " + DetailType;

        // The reference keeps its keyword and the column's own marks stay on the end, which is
        // where the old notation had them too.
        foreach (string suffix in new[] { "?", "[]", "[]?", "?[]" })
        {
            if (string.Equals(Type, "foreign" + suffix, StringComparison.Ordinal))
                return "foreign " + DetailType + suffix;
        }

        if (Role(Type, DetailType) is { } written)
            return written;

        throw new InvalidOperationException(
            $"Column `{Name}` is typed `{Type}` with the detail `{DetailType}`, and the fixture "
            + "does not say how the primary layout writes that. Set PrimaryType.");
    }
}

public sealed class TableSpec
{
    public string Name;
    public string Comment = "";
    public string TargetSide = "";

    /// <summary>Anything else the declaration cell carries, written as it stands.</summary>
    /// <remarks>
    /// `key="stage,slot"` is what this exists for. Held as text rather than modelled, because
    /// the notation is the layout's and a fixture builder that models it would be a second
    /// place the notation is defined.
    /// </remarks>
    public string Meta = "";

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
