using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog;
using Tabbit.Models;
using Tabbit.Models.Raw;

namespace Tabbit.Cooking;

/// <summary>
/// Folds the tables that are really another set of some table's rows into that table.
/// </summary>
/// <remarks>
/// A source whose sheets fill one table's columns in more than once names the extra sets
/// after the table - `Item` and `Item_alt` - and declares the pattern that says so. Read
/// without it, each name is its own table, so a schema that exists once is declared twice and
/// every generator emits a second type for it. That type is one nobody wrote: the sheets said
/// "these rows, also", not "another table".
///
/// **Run after every layout has parsed, and over the whole model.** Not inside a layout,
/// because the question is about table names rather than about how a table is found in a
/// sheet - a marker layout and a defined-name layout can both have it. And not while parsing,
/// because the order names arrive in is the workbook's: `Item_alt` can be read before `Item`,
/// and a fold that assumed otherwise would work by luck.
///
/// The pattern itself is the source's, never this program's. What a tail looks like is a
/// convention of whoever wrote the sheets.
///
/// spec/table-row-sets.md.
/// </remarks>
internal static class TableRowSets
{
    /// <summary>The group that names the table, and the group that names the set.</summary>
    private const string TableGroup = "table";
    private const string SetGroup = "set";

    /// <summary>
    /// Applies the pattern the sources declared, if any did.
    /// </summary>
    /// <param name="sheets">
    /// Every sheet read, for the settings their source entries stamped on them. The pattern is
    /// read from here rather than from the recipe so that a source added by any importer
    /// carries it the same way.
    /// </param>
    public static void Fold(
        CookingContext context, IReadOnlyList<RawSheet> sheets, Diagnostics diagnostics)
    {
        var patterns = sheets
            .Select(sheet => sheet.Layout?.TableRowSets ?? "")
            .Where(pattern => pattern.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (patterns.Count == 0)
            return;

        // A table name means one thing across the model - the tables of every source live in
        // one namespace - so two sources describing that namespace differently is a
        // contradiction rather than two settings to apply in turn.
        if (patterns.Count > 1)
        {
            throw new TabbitException(null,
                $"Two source entries declare different `TableRowSets` patterns: "
                + string.Join(", ", patterns.Select(p => $"`{p}`"))
                + ". Table names are shared across the whole run, so one pattern has to "
                + "describe all of them.");
        }

        Fold(context, Compile(patterns[0]), diagnostics);
    }

    private static Regex Compile(string pattern)
    {
        Regex compiled;

        try
        {
            compiled = new Regex(pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException e)
        {
            throw new TabbitException(null,
                $"`TableRowSets` is `{pattern}`, which is not a regular expression: {e.Message}");
        }

        // Named groups rather than positions, because the two are easy to write in either
        // order and getting them the wrong way round would silently pair every table with a
        // tail as its name.
        var groups = compiled.GetGroupNames();

        if (!groups.Contains(TableGroup) || !groups.Contains(SetGroup))
        {
            throw new TabbitException(null,
                $"`TableRowSets` is `{pattern}`, which needs a `{TableGroup}` group naming the "
                + $"table and a `{SetGroup}` group naming the set - for example "
                + $"`^(?<{TableGroup}>.+?)(?<{SetGroup}>_alt)$`.");
        }

        return compiled;
    }

    private static void Fold(CookingContext context, Regex pattern, Diagnostics diagnostics)
    {
        var model = context.Model;

        var byRawName = new Dictionary<string, Table>(StringComparer.Ordinal);
        foreach (var table in model.Tables)
            byRawName[table.RawName] = table;

        var folded = new List<Table>();

        foreach (var table in model.Tables)
        {
            var match = pattern.Match(table.RawName);
            if (!match.Success)
                continue;

            string ownerName = match.Groups[TableGroup].Value;
            string setName = match.Groups[SetGroup].Value;

            // A name that matches but points at itself would fold a table into itself and
            // lose it. That is a pattern which does not identify a tail, not a naming
            // mistake in the sheets, so it is worth saying which is which.
            if (ownerName.Length == 0 || string.Equals(ownerName, table.RawName, StringComparison.Ordinal))
            {
                throw new TabbitException(table.Location,
                    $"`TableRowSets` matches `{table.RawName}` but its `{TableGroup}` group "
                    + $"captured `{ownerName}`, so the pattern does not say which table this "
                    + $"is a set of rows for.");
            }

            if (!byRawName.TryGetValue(ownerName, out var owner))
            {
                diagnostics.Error(table.Location,
                    $"`{table.RawName}` names another set of `{ownerName}`'s rows, which this "
                    + $"run does not have. A name matching `TableRowSets` contributes rows to "
                    + $"a table rather than declaring one, so the table has to be there - "
                    + $"check the spelling, or that the source holding it is not excluded.");
                continue;
            }

            if (!TryProjectOnto(owner, table, context, out var rows, out string? difference))
            {
                diagnostics.Error(table.Location,
                    $"`{table.RawName}` and `{owner.RawName}` are two sets of one table's rows, "
                    + $"so `{owner.RawName}` has to declare every column `{table.RawName}` "
                    + $"holds. {difference}");
                continue;
            }

            Log.Information(
                $"`{table.RawName}` is another set of `{owner.RawName}`'s rows "
                + $"({rows.Count} row(s)), written as `{owner.Name}{setName}`.");

            owner.ExtraRowSets.Add(new RowSet { Name = setName, Rows = rows });
            folded.Add(table);
        }

        foreach (var table in folded)
            model.Tables.Remove(table);
    }

    /// <summary>
    /// Lays a set's rows out against the table's own columns, or says which column stopped it.
    /// </summary>
    /// <remarks>
    /// **By column name rather than by position**, because two sets of a table's rows do not
    /// have to be the same width. Where a column is an array, one set can hold fewer elements
    /// than the other - it is the same column, filled in less far - and a set laid out
    /// positionally would then read every column after it under the wrong name.
    ///
    /// The table has to declare every column the set holds. A set holding one the table does
    /// not is the case that cannot be resolved: there is one generated type and it comes from
    /// the table, so a column only this set has would be one no consumer can reach. The other
    /// way round is fine, and is the ordinary shape of the difference - the set simply leaves
    /// those cells empty.
    ///
    /// A cell the set does not have is written as the type's empty value with `HasValue`
    /// false, which is the same thing the layouts write for a cell the sheet left blank. That
    /// is what makes a shorter array come out shorter rather than padded: the element count is
    /// taken per row from `HasValue`.
    ///
    /// **A column no set of this table declares stays required. One that a set does not
    /// declare becomes optional**, because otherwise validation reports the value written
    /// here - by this method, deliberately - as a violation. What it costs and why the whole
    /// array turns optional rather than one element of it is in spec/table-row-sets.md 4.2.
    ///
    /// **Matched by <see cref="Field.SetAlignName"/> where a field carries one**, which is how
    /// a grid's columns line up by their ids rather than by the positional names the layout
    /// gave them.
    /// </remarks>
    private static bool TryProjectOnto(
        Table owner, Table other, CookingContext context,
        out List<List<Cell>> rows, out string? difference)
    {
        rows = new List<List<Cell>>();

        var ownerByName = new Dictionary<string, Field>(StringComparer.Ordinal);
        foreach (var field in owner.Fields)
            ownerByName[AlignKey(field)] = field;

        // Where each of the table's columns is in this set's rows, or -1 when the set has
        // none. Worked out once rather than per row.
        var takeFrom = new int[owner.Fields.Count];
        for (int at = 0; at < takeFrom.Length; at++)
            takeFrom[at] = -1;

        foreach (var field in other.Fields)
        {
            if (!ownerByName.TryGetValue(AlignKey(field), out var ours))
            {
                difference = $"`{owner.RawName}` has no `{field.RawName}`.";
                return false;
            }

            if (ours.Type != field.Type)
            {
                difference = $"`{field.RawName}` is `{ours.TypeName}` in `{owner.RawName}` "
                    + $"and `{field.TypeName}` in `{other.RawName}`.";
                return false;
            }

            takeFrom[ours.Index] = field.Index;
        }

        foreach (var row in other.Data)
        {
            var projected = new List<Cell>(owner.Fields.Count);

            for (int at = 0; at < owner.Fields.Count; at++)
            {
                int from = takeFrom[at];

                projected.Add(from >= 0 && from < row.Count
                    ? row[from]
                    : Absent(owner.Fields[at], row, context));
            }

            rows.Add(projected);
        }

        MakeMissingColumnsOptional(owner, takeFrom);

        difference = null;
        return true;
    }

    /// <summary>A cell for a column this set of rows does not have.</summary>
    /// <remarks>
    /// The value comes from the same parse a written cell goes through, given nothing, so a
    /// column whose empty value is not a scalar - an array, a delimited list - gets the shape
    /// its readers expect rather than a null they would cast and throw on.
    ///
    /// The location borrows the row's first cell. It is not this column's cell, because there
    /// is none; what a diagnostic needs from it is which row of which sheet, and that is what
    /// it carries.
    /// </remarks>
    /// <summary>The name a column is matched by: its own unless the layout named one.</summary>
    private static string AlignKey(Field field)
        => field.SetAlignName.Length > 0 ? field.SetAlignName : field.RawName;

    /// <summary>
    /// Marks every column this set did not provide as one that may have no value.
    /// </summary>
    /// <remarks>
    /// The projection above writes `HasValue` false into those cells, and a column declared
    /// required would then have validation report exactly that. The two rules contradicted
    /// each other, and the sheets said which one was right: a set that does not declare a
    /// column is not a set that forgot to fill it in.
    ///
    /// **An element of an array turns the whole array optional.** Requiredness is one answer
    /// per array in this model - element 0 states it and every element takes it - so there is
    /// no room for one element of it to be the optional one. That is stated as the cost in
    /// spec/table-row-sets.md rather than worked around here.
    ///
    /// The derived views are dropped afterwards, because requiredness is copied to the
    /// elements when they are built and a view built before this ran holds the old answer.
    /// </remarks>
    private static void MakeMissingColumnsOptional(Table owner, int[] takeFrom)
    {
        var groupsToRelax = new HashSet<string>(StringComparer.Ordinal);
        bool changed = false;

        for (int at = 0; at < takeFrom.Length; at++)
        {
            if (takeFrom[at] >= 0)
                continue;

            var field = owner.Fields[at];

            if (field.IsRequired)
            {
                field.IsRequired = false;
                changed = true;
            }

            // An array's answer lives on its first element, so the group is named here and
            // that element is relaxed below.
            if (field.GroupName is { Length: > 0 } group)
                groupsToRelax.Add(group);
        }

        foreach (var group in groupsToRelax)
        {
            foreach (var field in owner.Fields)
            {
                if (!string.Equals(field.GroupName, group, StringComparison.Ordinal)
                    || !field.IsRequired)
                {
                    continue;
                }

                field.IsRequired = false;
                changed = true;
            }
        }

        if (changed)
            owner.InvalidateDerivedColumns();
    }

    private static Cell Absent(Field field, List<Cell> row, CookingContext context)
    {
        var where = row.Count > 0 ? row[0].RawCell : null!;

        return new Cell
        {
            RawCell = where,
            Value = context.ParseValue(
                field.Type, field.EnumOrNull, "", where?.Location, arrayDelimiter: null,
                required: false),
            HasValue = false,
        };
    }
}
