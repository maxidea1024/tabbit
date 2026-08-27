using System.Collections.Generic;
using System.Linq;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Cooking;

/// <summary>
/// What makes a `set` a set and a `map` a map, once their columns are ordinary columns.
/// </summary>
/// <remarks>
/// **Neither is a shape - both are a promise about the values.** A set is an array column
/// and a map is two of equal length, which is what the file already writes; what a
/// declaration adds is that the elements are distinct and that the two columns line up. So
/// this pass reads cells rather than columns, and it is the whole of what containers cost
/// the model.
///
/// Run once the binding has converted the cells, because the promise is about values and
/// the text a sheet wrote is not one - `01` and `1` are one key and two strings.
///
/// spec/types/set-and-map.md sections 5 and 6.
/// </remarks>
public partial class ModelCooker
{
    private static void CheckContainers(Model model, Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            foreach (var container in ContainersOf(table))
            {
                if (!RefuseAContainerInsideAnArray(table, container, diagnostics))
                    continue;

                if (container.Kind == ContainerKind.Set)
                    CheckSet(table, container, diagnostics);
                else
                    CheckMap(table, container, diagnostics);
            }
        }
    }

    /// <summary>One container of a table: what it is, and the columns under it.</summary>
    private readonly record struct Container(
        ContainerKind Kind, string Path, int Level, List<Field> Columns);

    /// <summary>
    /// The containers a table holds, each with every column at or below it.
    /// </summary>
    /// <remarks>
    /// Gathered by the path down to the container's own level, so two members declared with
    /// the same container type are two containers and a `map` whose value is a struct is one.
    /// </remarks>
    private static IEnumerable<Container> ContainersOf(Table table)
        => table.Fields
            .Where(field => field.Container != ContainerKind.None && field.NamePath is not null)
            .GroupBy(field => (
                field.Container,
                field.ContainerLevel,
                Path: string.Join(
                    Helpers.NestedName.MemberSeparator,
                    field.NamePath!.Take(field.ContainerLevel + 1).Select(step => step.Name))))
            .Select(found => new Container(
                found.Key.Container, found.Key.Path, found.Key.ContainerLevel, found.ToList()));

    /// <summary>
    /// Refuses a container that sits inside a numbered group, which is an array of them.
    /// </summary>
    /// <remarks>
    /// **The same wall `set&lt;T&gt;[]` meets, reached from the sheet's side.** A group written
    /// `Bag1.Prices.Key` and `Bag2.Prices.Key` is an array of records each holding a map, so
    /// one column would have to carry a list per element - a list of lists, which the
    /// notation has no cell for. Refused here rather than left to a generator, because what
    /// a generator would emit is code that compiles and reads the wrong place.
    /// spec/types/set-and-map.md section 2.1.
    /// </remarks>
    /// <returns>False when it was reported.</returns>
    private static bool RefuseAContainerInsideAnArray(
        Table table, Container container, Diagnostics diagnostics)
    {
        var field = container.Columns[0];

        for (int level = 0; level <= container.Level; level++)
        {
            if (field.NamePath![level].Index is null)
                continue;

            diagnostics.Error(field.NameLocation, Message.Of(
                SchemaMessages.ContainerInsideAnArray,
                ("Table", table.Name),
                ("Column", container.Path),
                ("Group", field.NamePath![level].Name)));

            return false;
        }

        return true;
    }

    /// <summary>
    /// A set is one column, and no two of its elements may be equal.
    /// </summary>
    private static void CheckSet(Table table, Container container, Diagnostics diagnostics)
    {
        var column = container.Columns[0];

        foreach (var rowSet in table.RowSets)
        foreach (var row in rowSet.Rows)
        {
            if (column.Index >= row.Count || row[column.Index].Value is not System.Array elements)
                continue;

            var seen = new Dictionary<object, int>();

            for (int at = 0; at < elements.Length; at++)
            {
                var value = elements.GetValue(at);
                if (value is null)
                    continue;

                if (seen.TryGetValue(value, out int first))
                {
                    diagnostics.Error(row[column.Index].RawCell?.Location, Message.Of(
                        SchemaMessages.SetDuplicateElement,
                        ("Table", table.Name),
                        ("Column", container.Path),
                        ("Value", value),
                        ("Element", at + 1),
                        ("First", first + 1)));

                    break;
                }

                seen.Add(value, at);
            }
        }
    }

    /// <summary>
    /// A map is a key column and everything under its value, all of one length per row, and
    /// no two keys equal.
    /// </summary>
    private static void CheckMap(Table table, Container container, Diagnostics diagnostics)
    {
        var key = container.Columns.Find(
            field => field.NamePath!.Count == container.Level + 2
                     && field.NamePath![^1].Name == SchemaContainers.KeyMember);

        var values = container.Columns
            .Where(field => field.NamePath!.Count > container.Level + 1
                            && field.NamePath![container.Level + 1].Name == SchemaContainers.ValueMember)
            .ToList();

        // Both halves, or the group is not a map however it was declared. Reported here
        // rather than by the member check, which asks whether a declared member has a column
        // at all and is answered by either of these.
        if (key is null || values.Count == 0)
        {
            diagnostics.Error(container.Columns[0].NameLocation, Message.Of(
                SchemaMessages.MapHalfWritten,
                ("Table", table.Name),
                ("Column", container.Path),
                ("Missing", key is null ? SchemaContainers.KeyMember : SchemaContainers.ValueMember)));

            return;
        }

        foreach (var rowSet in table.RowSets)
        foreach (var row in rowSet.Rows)
        {
            if (key.Index >= row.Count || row[key.Index].Value is not System.Array keys)
                continue;

            // Every column under the value carries one entry per key. A struct value is
            // several columns and each of them is that same length, which is what makes the
            // map read back as the pairs it was written as.
            foreach (var value in values)
            {
                if (value.Index >= row.Count || row[value.Index].Value is not System.Array held)
                    continue;

                if (held.Length == keys.Length)
                    continue;

                diagnostics.Error(row[value.Index].RawCell?.Location, Message.Of(
                    SchemaMessages.MapLengthMismatch,
                    ("Table", table.Name),
                    ("Column", container.Path),
                    ("Keys", keys.Length),
                    ("Values", held.Length),
                    ("Member", FieldPath.Describe(value.NamePath!))));

                break;
            }

            var seen = new Dictionary<object, int>();

            for (int at = 0; at < keys.Length; at++)
            {
                var written = keys.GetValue(at);
                if (written is null)
                    continue;

                if (seen.TryGetValue(written, out int first))
                {
                    diagnostics.Error(row[key.Index].RawCell?.Location, Message.Of(
                        SchemaMessages.MapDuplicateKey,
                        ("Table", table.Name),
                        ("Column", container.Path),
                        ("Value", written),
                        ("Element", at + 1),
                        ("First", first + 1)));

                    break;
                }

                seen.Add(written, at);
            }
        }
    }
}
