using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.Validation;

/// <summary>
/// Finds the cell a generated record came from.
/// </summary>
/// <remarks>
/// The record a rule holds is the one the generated reader built, and it carries data alone -
/// no location, because it is the same type the consuming project uses and validation does not
/// get to change that. So the position is recovered rather than carried: the record's type
/// names its table, its primary index names its row, and the field name inverts to a column.
///
/// Every part of that is already guaranteed. Index uniqueness is checked by the static pass,
/// so a key identifies one row; the property name is the field name the generator PascalCased,
/// so `nameof(row.MaxStack)` is the field's own name.
///
/// Where the inversion is not exact - a folded array, a record group, a matrix table - the
/// group's first column is the answer. spec/validation-pipeline.md §10 names that as the
/// deliberate approximation, with an element-aware overload as the way out.
/// </remarks>
internal sealed class CellLocator
{
    /// <summary>Suffix the generator puts on a table's class name.</summary>
    private const string TableSuffix = "Table";

    private readonly Model _model;

    /// <summary>Per table, its rows keyed by primary index value. Built on first use.</summary>
    private readonly Dictionary<string, Dictionary<object, List<Cell>>> _rowsByKey =
        new Dictionary<string, Dictionary<object, List<Cell>>>();

    internal CellLocator(Model model) => _model = model;

    /// <summary>
    /// The cell behind one field of one record, or the nearest thing to it.
    /// </summary>
    /// <param name="row">A record from the generated accessor.</param>
    /// <param name="fieldName">
    /// The field, as `nameof(row.Field)` gives it. Null or unknown answers with the table's
    /// own marker cell, which is still a place in a workbook.
    /// </param>
    /// <param name="element">
    /// Which element of an array or record group, or -1 for the group's first column.
    /// </param>
    internal Location? Find(object row, string fieldName, int element = -1)
    {
        var table = TableOf(row);
        if (table is null)
            return null;

        var cells = RowOf(table, row);

        // No field named means the row itself is what is wrong - a combination of columns
        // rather than one of them - so the report points at the primary index, which is where
        // a reader looking for that row starts.
        var field = string.IsNullOrEmpty(fieldName) && table.Fields.Count > 0
            ? table.PrimaryIndexField!
            : FieldOf(table, fieldName, element);

        if (cells is null || field is null)
            return table.Location;

        return field.Index < cells.Count
            ? cells[field.Index].RawCell?.Location ?? field.NameLocation
            : field.NameLocation;
    }

    /// <summary>The table a generated record belongs to.</summary>
    /// <remarks>
    /// Read from the type rather than passed in, because a rule holds the record and nothing
    /// else - `Error(row, ...)` is the call an author should be able to write. The generated
    /// record is nested in its table class, so `ItemTable.Record` names `Item`.
    /// </remarks>
    private Table? TableOf(object row)
    {
        string? owner = row?.GetType().DeclaringType?.Name;

        if (string.IsNullOrEmpty(owner) || !owner.EndsWith(TableSuffix, StringComparison.Ordinal))
            return null;

        return _model.FindTable(owner.Substring(0, owner.Length - TableSuffix.Length));
    }

    /// <summary>The model row this record was built from, found by primary index.</summary>
    private List<Cell>? RowOf(Table table, object row)
    {
        if (table.Fields.Count == 0)
            return null;

        var key = table.PrimaryIndexField!;

        object? value = row.GetType().GetProperty(key.Name)?.GetValue(row);
        if (value is null)
            return null;

        // Locked because the table rules run in parallel and any of them may be the first to
        // ask about a table. A Dictionary two threads add to does not fail; it corrupts.
        Dictionary<object, List<Cell>>? index;

        lock (_rowsByKey)
        {
            if (!_rowsByKey.TryGetValue(table.Name, out index))
            {
                index = new Dictionary<object, List<Cell>>();

                foreach (var candidate in table.Data)
                {
                    object candidateKey = candidate[key.Index].Value!;

                    // First wins. A duplicate is already an error from the static pass, and
                    // pointing at the first of them is better than throwing here.
                    if (candidateKey is not null && !index.ContainsKey(candidateKey))
                        index.Add(candidateKey, candidate);
                }

                _rowsByKey.Add(table.Name, index);
            }
        }

        return index.TryGetValue(value, out var found) ? found : null;
    }

    /// <summary>
    /// The column a field name means, following a group to the element asked for.
    /// </summary>
    private static Field? FieldOf(Table table, string fieldName, int element)
    {
        if (string.IsNullOrEmpty(fieldName))
            return null;

        // A plain column, which is the common case and exact.
        var field = table.FindField(fieldName);
        if (field is not null)
            return field;

        // A group's name: an array folded from several columns, or a record. Its own name is
        // not a column, so the element decides - and with no element, the group's head.
        var group = table.SerialFields.FirstOrDefault(candidate => candidate.Name == fieldName);
        if (group is null)
            return null;

        var columns = group.IsRecord
            ? group.Members.FirstOrDefault()?.Fields
            : group.Fields;

        if (columns is null || columns.Count == 0)
            return null;

        return element >= 0 && element < columns.Count ? columns[element] : columns[0];
    }
}
