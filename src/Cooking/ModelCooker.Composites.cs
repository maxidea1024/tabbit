using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.Cooking;

public partial class ModelCooker
{
    /// <summary>
    /// Turns every composite column into the record it describes, now that every cell has
    /// been read.
    /// </summary>
    /// <remarks>
    /// `vec3f` is a type for exactly as long as parsing lasts. What made it one is the
    /// notation it accepts - a tuple, a hex colour, a name - and that question is settled
    /// once a cell has become a value. Past here `Pos` is three `float` columns named
    /// `Pos.X`, `Pos.Y` and `Pos.Z`, which is **the same shape a sheet writing those three
    /// names by hand produces**: the wire stores a record one column per member, thirteen
    /// generators declare it, and the exporters and the history store already read it.
    ///
    /// So nothing below this line knows the types exist, and the gate that says so is a
    /// byte-for-byte comparison between the two ways of writing the same table.
    ///
    /// **Run here rather than inside the layouts.** Doing it in the parsers would need the
    /// same call added to each of them, and a layout that forgot would carry a composite type
    /// into code that has no case for it. The cost is that the tags a layout already handed
    /// out are stale - a composite is one column at that point and several here - so they are
    /// dropped and reassigned for the tables that changed.
    ///
    /// spec/types/composite-value-types.md section 6.
    /// </remarks>
    private static void ExpandCompositeColumns(
        CookingContext context, Model model, Diagnostics diagnostics)
    {
        RefuseCompositeConstants(model, diagnostics);

        foreach (var table in model.Tables)
        {
            var composites = table.Fields
                .Where(field => CompositeTypes.IsComposite(field.Type))
                .ToList();

            if (composites.Count == 0)
                continue;

            int reportedBefore = diagnostics.Count;

            foreach (var field in composites)
                RefuseWhatTheShapeCannotCarry(table, field, diagnostics);

            // **A table that already failed is not expanded.** Those reports are collected
            // rather than thrown, and the work below clears the tags the sheet wrote and asks
            // for them to be assigned again - which then throws about a tombstone reserving a
            // tag while no live field carries one. That is true of the table this code just
            // made and not of the sheet, so the author is sent to a `#` column they wrote
            // correctly while the report naming the real cause never gets printed.
            if (diagnostics.Count != reportedBefore)
                continue;

            var original = table.Fields.ToList();

            table.Fields = original.SelectMany(field => Expand(table, field)).ToList();

            // Rows first, and then the renumbering. A column that is not a composite is the
            // **same object** in both lists, so renumbering moves the very index the rewrite
            // reads its cells by - and a plain column sitting after a composite one would
            // then be read from the wrong cell, or from past the end of the row.
            RewriteRows(table, original);

            for (int at = 0; at < table.Fields.Count; at++)
                table.Fields[at].Index = at;

            CheckRotations(table, original, diagnostics);
            RefuseNameCollisions(table, diagnostics);

            // The wire columns snapshot a field's type and tag assignment has already built
            // them, so nothing here is visible until they are rebuilt.
            table.InvalidateDerivedColumns();

            // Ordinal tags were positions in a column list that no longer exists. Cleared
            // rather than adjusted, because an ordinal tag has no identity to preserve - and
            // a table whose sheet wrote its tags out has already been refused above.
            foreach (var field in table.Fields)
                field.WireTag = null;

            context.AssignTags(table);
        }
    }

    /// <summary>
    /// Refuses a composite in a constant set.
    /// </summary>
    /// <remarks>
    /// A column is expanded into the record its components make, and there is nothing to do
    /// the same to a constant: a constant set is a list of single values, not a table with
    /// rows, so the type would survive into thirteen generators that have no case for it.
    ///
    /// Refused rather than half-supported. Emitting a vector constant means each language
    /// deciding what type to emit it as, which is the question section 7 of the spec leaves
    /// open - and answering it here, for constants only, would answer it differently from
    /// wherever it is answered for columns.
    /// </remarks>
    private static void RefuseCompositeConstants(Model model, Diagnostics diagnostics)
    {
        foreach (var set in model.ConstantSets)
        {
            foreach (var constant in set.Constants)
            {
                if (!CompositeTypes.IsComposite(constant.Type))
                    continue;

                diagnostics.Error(constant.Location,
                    $"Constant `{set.Name}.{constant.Name}` is `{constant.TypeName}`. A "
                    + "composite type is expanded into one column per component, and a "
                    + "constant has no columns to expand into - so it is not a type a "
                    + "constant can have. Declare the components as separate constants.");
            }
        }
    }

    /// <summary>
    /// The declarations a composite column cannot carry, each reported against the type name
    /// the sheet used.
    /// </summary>
    /// <remarks>
    /// Reported here rather than left to the checks downstream. Every one of them would be
    /// caught eventually - a record is not an index key, a half-tagged table is refused - but
    /// by then the column is `Pos.X` and the report would name a column the author never
    /// wrote. The word to give back is the one in the type row.
    /// </remarks>
    private static void RefuseWhatTheShapeCannotCarry(
        Table table, Field field, Diagnostics diagnostics)
    {
        if (field.Indexing)
        {
            diagnostics.Error(field.NameLocation,
                $"Field `{table.Name}.{field.Name}` is marked as an index and is "
                + $"`{field.TypeName}`, whose value has several components. A key is one "
                + "value, so index the table by one of the components instead.");
        }

        if (table.HasExplicitTags)
        {
            diagnostics.Error(field.TypeLocation,
                $"Table `{table.Name}` writes its wire tags out with `@N`, and "
                + $"`{field.Name}` is `{field.TypeName}` - which becomes one column per "
                + $"component ({string.Join(", ", CompositeTypes.Of(field.Type)!.Components)}) "
                + "and so needs a tag for each. Write the components as their own columns "
                + $"(`{field.Name}.{CompositeTypes.Of(field.Type)!.Components[0]}`), each with "
                + "its own tag.");
        }

        if (!field.Constraints.IsEmpty)
        {
            diagnostics.Error(field.NameLocation,
                $"Field `{table.Name}.{field.Name}` is `{field.TypeName}` and declares a "
                + "column constraint. What a range means for a value with several components "
                + "- each component, or the magnitude - is not decided, so the constraint is "
                + "refused rather than read one of the two ways.");
        }
    }

    /// <summary>
    /// One field, or one field per component when it is a composite.
    /// </summary>
    /// <remarks>
    /// The component becomes one more level of the column's name path, which is what makes
    /// the result identical to a hand-written `Pos.X`. Whatever nesting the column already
    /// had composes with it: `Slot1.Pos` of type `vec3f` reaches the model as
    /// `Slot1.Pos.X`, an array of records whose member is a record.
    ///
    /// A number in a plain name is **not** read here. `Pos1` and `Pos2` become two records
    /// rather than an array of two, because deciding that a digit means an array is the
    /// layout's judgement and the one place it is made - re-deriving it here would be a
    /// second answer to the same question. A sheet that means the array writes the group
    /// notation, which this composes with.
    /// </remarks>
    private static IEnumerable<Field> Expand(Table table, Field field)
    {
        var composite = CompositeTypes.Of(field.Type);

        if (composite is null)
        {
            yield return field;
            yield break;
        }

        var basePath = field.NamePath is null
            ? new List<FieldPathStep> { new FieldPathStep { Name = field.Name, Index = null } }
            : new List<FieldPathStep>(field.NamePath);

        string componentTypeName =
            composite.ComponentType == Models.ValueType.Int32 ? "int" : "float";

        foreach (string component in composite.Components)
        {
            var path = new List<FieldPathStep>(basePath)
            {
                new FieldPathStep { Name = component, Index = null },
            };

            yield return new Field
            {
                OwnerTable = table,

                // All four of the header cells the column was declared in. A report about a
                // component points at the cell the author can edit, which is the composite's
                // own header - there is no cell of its own to point at.
                NameLocation = field.NameLocation,
                TypeLocation = field.TypeLocation,
                DetailTypeLocation = field.DetailTypeLocation,
                TargetSideLocation = field.TargetSideLocation,

                RawName = $"{field.RawName}.{component}",
                Name = field.Name + component,
                NamePath = path,

                // The component's part of the name is this tool's, not the sheet's, so the
                // naming rules have nothing to judge here. See `Field.Synthesized`.
                Synthesized = true,

                TargetSide = field.TargetSide,
                IsRequired = field.IsRequired,
                Comment = field.Comment,

                Type = composite.ComponentType,
                TypeName = componentTypeName,

                // Recorded so a later revision can give the group a shared type - the
                // engine's vector rather than a struct per column - without having to work
                // out from three `float` members that they were one. Nothing reads it yet.
                // spec/types/composite-value-types.md section 7.
                CompositeOrigin = composite.Type,

                Index = 0,
            };
        }
    }

    /// <summary>
    /// Spreads each composite cell's components across the columns that now hold them.
    /// </summary>
    private static void RewriteRows(Table table, List<Field> original)
    {
        foreach (var row in table.Data)
        {
            var rewritten = new List<Cell>(table.Fields.Count);

            foreach (var field in original)
            {
                var cell = row[field.Index];
                var composite = CompositeTypes.Of(field.Type);

                if (composite is null)
                {
                    rewritten.Add(cell);
                    continue;
                }

                var components = (System.Array)cell.Value!;

                for (int at = 0; at < composite.Arity; at++)
                {
                    rewritten.Add(new Cell
                    {
                        RawCell = cell.RawCell,
                        Value = components.GetValue(at),

                        // One answer for the whole cell. The components came from one cell,
                        // so "did the sheet give this a value" has one answer and giving the
                        // components their own would invent a state the notation cannot say.
                        HasValue = cell.HasValue,
                    });
                }
            }

            row.Clear();
            row.AddRange(rewritten);
        }
    }

    /// <summary>
    /// Reports the rotations that are not rotations.
    /// </summary>
    /// <remarks>
    /// A quaternion of length zero is refused: it names no rotation, and every use of one
    /// divides by that length. Drift away from unit length is a **warning**, because a
    /// quaternion typed into a sheet by hand is rounded and being a little off is normal - a
    /// build that must not ship one turns on `TreatWarningsAsErrors`, the same switch an
    /// undrawn asset is held to.
    ///
    /// An axis-angle's axis is refused at zero for the same reason and with no tolerance
    /// above it: the axis is a direction, and its length carries nothing.
    /// </remarks>
    private static void CheckRotations(Table table, List<Field> original, Diagnostics diagnostics)
    {
        const float Tolerance = 1e-4f;

        var rotations = original
            .Where(field => field.Type is Models.ValueType.Quat or Models.ValueType.AxisAngle)
            .ToList();

        if (rotations.Count == 0)
            return;

        foreach (var field in rotations)
        {
            // Read from the expanded columns rather than the cells above, so this checks what
            // the model actually carries.
            var components = table.Fields
                .Where(expanded => expanded.RawName.StartsWith(
                    field.RawName + ".", System.StringComparison.Ordinal))
                .ToList();

            bool isQuaternion = field.Type == Models.ValueType.Quat;

            // A quaternion's length is over all four components; an axis-angle's is over the
            // three that are the axis, because the fourth is an angle.
            int counted = isQuaternion ? 4 : 3;

            for (int at = 0; at < table.Data.Count; at++)
            {
                double sum = 0;

                for (int component = 0; component < counted; component++)
                {
                    float value = (float)table.Data[at][components[component].Index].Value!;
                    sum += (double)value * value;
                }

                double length = System.Math.Sqrt(sum);
                var location = table.Data[at][components[0].Index].RawCell.Location;

                if (length == 0)
                {
                    diagnostics.Error(location, isQuaternion
                        ? $"`{table.Name}.{field.Name}` is a quaternion of length zero, which "
                          + "is not a rotation. The rotation that turns nothing is "
                          + "`(0, 0, 0, 1)`, or `identity`."
                        : $"`{table.Name}.{field.Name}` has the zero vector as its axis, which "
                          + "names no direction. Write the axis the angle turns around.");

                    continue;
                }

                if (isQuaternion && System.Math.Abs(length - 1) > Tolerance)
                {
                    diagnostics.Warn(location,
                        $"`{table.Name}.{field.Name}` has length {length:0.#####}, and a "
                        + "rotation quaternion has length 1. Values are carried through as "
                        + "written; normalize the cell if that was not intended.");
                }
            }
        }
    }

    /// <summary>
    /// Refuses a component whose name is already a column's.
    /// </summary>
    /// <remarks>
    /// `Pos` of type `vec2f` beside a column called `PosX` is two fields named `PosX`, and
    /// the duplicate check the layout ran happened before either existed. Reported against
    /// the composite, because that is the column whose expansion produced the clash.
    /// </remarks>
    private static void RefuseNameCollisions(Table table, Diagnostics diagnostics)
    {
        var seen = new HashSet<string>();

        foreach (var field in table.Fields)
        {
            if (seen.Add(field.Name))
                continue;

            diagnostics.Error(field.NameLocation,
                $"Table `{table.Name}` ends up with two fields called `{field.Name}`. A "
                + $"composite column expands to one field per component (`{field.RawName}`), "
                + "and one of those has the name of a column that was already there.");
        }
    }
}
