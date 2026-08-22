using Tabbit.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Models;

/// <summary>
/// Everything the sheets declared, after the raw cells have been interpreted.
///
/// This is what the exporters and code generators consume. Cross-table references
/// are resolved before it is handed over, so a field knows the table and field it
/// points at rather than just their names.
/// </summary>
public class Model
{
    /// <summary>Tables, in the order their markers were found.</summary>
    public List<Table> Tables { get; set; } = new List<Table>();

    /// <summary>Enum declarations. Parsed before tables, which may refer to them.</summary>
    public List<Enum> Enums { get; set; } = new List<Enum>();

    /// <summary>Constant sets. Parsed before tables, for the same reason.</summary>
    public List<ConstantSet> ConstantSets { get; set; } = new List<ConstantSet>();

    /// <summary>
    /// The model being worked on, for the few places that cannot reach one directly.
    ///
    /// Ambient state, and not a good pattern: Field.EnumOrNull resolves an enum
    /// through it because a Field holds only its type name. Worth replacing with an
    /// explicit reference, which is why ProjectTo takes care to leave this pointing
    /// at the complete model rather than a filtered view of it.
    /// </summary>
    public static Model Current { get; set; } = null!;

    /// <summary>
    /// Publishes the new instance as <see cref="Current"/>.
    /// </summary>
    public Model()
    {
        SetToCurrent();
    }

    /// <summary>Makes this the ambient model.</summary>
    public void SetToCurrent()
    {
        Current = this;
    }

    /// <summary>Empties every entity list, keeping the instance.</summary>
    public void Reset()
    {
        Tables.Clear();
        Enums.Clear();
        ConstantSets.Clear();
    }


    #region Target side projection

    /// <summary>
    /// Returns the view of this model that belongs in output built for
    /// <paramref name="side"/>: entities and fields marked for the other side are
    /// left out.
    ///
    /// Exporters and generators are handed the projection instead of being taught
    /// to filter, so every one of them applies the rule identically and none of
    /// their traversal code changes.
    ///
    /// The projection is shallow on purpose. Tables are new instances with a
    /// narrowed field list, but they share the original Field objects and the
    /// original Data rows, so <see cref="Field.Index"/> still addresses the right
    /// column. Consumers must therefore read cells through a field's Index rather
    /// than by walking a row positionally.
    /// </summary>
    public Model ProjectTo(TargetSide side)
    {
        // Both is the default and means "everything", so hand back the model
        // itself: no copying, and output is bit-for-bit what it was before target
        // sides existed.
        if (side == TargetSide.Both)
            return this;

        // `new Model()` publishes itself as Model.Current, which Field.EnumOrNull
        // resolves against. That must keep pointing at the complete model: a field
        // surviving the projection may be typed with an enum that does not, and
        // resolution should still succeed - it is emission that is being filtered,
        // not the type system.
        var previousCurrent = Current;

        var projected = new Model();

        foreach (var table in Tables)
        {
            if (!TargetSides.Includes(side, table.TargetSide))
                continue;

            var narrowed = new Table
            {
                Location = table.Location,
                TargetSide = table.TargetSide,
                RawName = table.RawName,
                Name = table.Name,
                Comment = table.Comment,
                Data = table.Data,

                // Carried, and it has to be: a narrowed run exports the same files under the
                // same names, and this is the name the exporter and every generated reader
                // agree on. Left to default it is empty, and the export writes `.tcb`.
                DataFileName = table.DataFileName,

                // Carried, not defaulted: the projection recomputes SerialFields from its
                // narrowed field list, and a table that must not fold must not start
                // folding because a target side was asked for.
                FoldSerialFields = table.FoldSerialFields,
            };

            foreach (var field in table.Fields)
            {
                // The primary index is what every row is addressed by, so it stays
                // regardless of side. ModelCooker already refuses to let it be
                // marked for one side only.
                if (field.Index != 0 && !TargetSides.Includes(side, field.TargetSide))
                    continue;

                narrowed.Fields.Add(field);
            }

            projected.Tables.Add(narrowed);
        }

        foreach (var enumm in Enums)
        {
            if (TargetSides.Includes(side, enumm.TargetSide))
                projected.Enums.Add(enumm);
        }

        foreach (var constantSet in ConstantSets)
        {
            if (TargetSides.Includes(side, constantSet.TargetSide))
                projected.ConstantSets.Add(constantSet);
        }

        Current = previousCurrent;

        return projected;
    }

    #endregion


    #region Tables

    /// <summary>Whether a table of this name exists.</summary>
    public bool ContainsTable(string name) => FindTable(name) is not null;

    /// <summary>
    /// Finds a table, or throws naming the cell that asked for it.
    /// </summary>
    public Table GetTable(string name, Location callerLocation)
    {
        var found = FindTable(name);
        if (found is null)
                throw new TabbitException(callerLocation,
                    Messages.Message.Of(Cooking.CookingMessages.TableNotFound, ("Name", name)));

        return found;
    }

    /// <summary>Finds a table by name, or null.</summary>
    public Table? FindTable(string? name) => Tables.Find(x => x.Name == name);

    #endregion


    #region Enums

    /// <summary>
    /// Whether an enum of this name exists.
    ///
    /// Also how a type name in a sheet is recognized as an enum rather than rejected.
    /// </summary>
    public bool ContainsEnum(string name) => FindEnum(name) is not null;

    /// <summary>
    /// Finds an enum, or throws naming the cell that asked for it.
    /// </summary>
    public Enum? GetEnum(string name, Location? callerLocation)
    {
        var found = FindEnum(name);
        if (found is null)
                throw new TabbitException(callerLocation,
                    Messages.Message.Of(Cooking.CookingMessages.EnumNotFound, ("Name", name)));

        return found;
    }

    /// <summary>Finds an enum by name, or null.</summary>
    public Enum? FindEnum(string name) => Enums.Find(x => x.Name == name);
    #endregion


    #region Constants

    /// <summary>Whether a constant set of this name exists.</summary>
    private bool ContainsConstantSet(string name) => FindConstantSet(name) is not null;

    /// <summary>Finds a constant set by name, or null.</summary>
    private ConstantSet? FindConstantSet(string name) => ConstantSets.Find(x => x.Name == name);

    #endregion


    #region Referencing

    /// <summary>
    /// One hop of a reference chain: the table arrived at, and the field followed
    /// into it.
    /// </summary>
    public class Reference
    {
        /// <summary>Table this hop lands in.</summary>
        public required Table Table { get; set; }

        /// <summary>Field followed, or null when the reference names the whole row.</summary>
        public required Field? Field { get; set; }
    }

    /// <summary>
    /// Resolves every foreign reference in the model, recording what it cannot
    /// resolve instead of throwing.
    ///
    /// Reporting rather than throwing is what lets a broken workbook come back
    /// with all of its problems at once. Resolution failures used to abort the run
    /// on the first one, so they could never join the report that validation
    /// produces a moment later.
    ///
    /// A field whose reference does not resolve is left unresolved. That is safe
    /// because the recorded diagnostics stop the run before anything is generated.
    /// </summary>
    public void SolveTableCrossReferencings(Diagnostics diagnostics)
    {
        foreach (var table in Tables)
        {
            foreach (var field in table.Fields)
            {
                // Several targets is not one record, so there is nothing to resolve to and
                // the field keeps carrying the key. What it does need is the tables, which
                // the generated accessors look rows up in. spec/multi-target-references.md.
                if (field.IsMultiRef)
                {
                    ResolveMultiTargetReference(table, field, diagnostics);
                    continue;
                }

                if (!field.IsRef)
                    continue;

                if (!TryResolveReference(table, field, diagnostics, out var referenceChain))
                    continue;

                // What the cell actually holds, whatever the field goes on to look like: the
                // primary index of the table it names. Its type is that table's to decide,
                // and the places that write and read the value ask here instead of assuming
                // int32. spec/reference-key-types.md.
                //
                // The chain's first hop, not ResolvedRefTable. A chain leaves the latter
                // holding the table it ends at, and the cell was written against the one it
                // starts from - for a single hop the two are the same table, which is why
                // reading the wrong one would go unnoticed.
                field.RefKeyType = referenceChain[0].Table.PrimaryIndexField?.Type
                    ?? Models.ValueType.Int32;

                if (field.ResolvedRefField is null)
                {
                    field.Type = Models.ValueType.ForeignRecord; // the value is a row of the referenced table, not its key
                    field.TypeName = $"{field.ResolvedRefTable!.Name}.Record";
                }
                else
                {
                    // A member of a record may point at a row, and not at one of that row's
                    // values. The whole-row form is what a record member is generated as -
                    // the resolved row beside the key it came from - and the dotted form
                    // resolves to a value instead, which is a second shape inside the element
                    // that no measured sheet asks for. Refused by name rather than half
                    // generated. spec/references-in-records.md.
                    if (field.IsRecordMember)
                    {
                        diagnostics.Error(field.DetailTypeLocation,
                            Messages.Message.Of(Cooking.CookingMessages.RecordReferenceNamesField,
                                ("Table", table.Name), ("Group", field.GroupName),
                                ("Field", field.Name),
                                ("RefField", field.ResolvedRefField.Name)));
                        continue;
                    }

                    field.Type = field.ResolvedRefField.Type;
                    field.TypeName = field.ResolvedRefField.TypeName;
                }

                field.RefChainPath = string.Join("_", referenceChain.Select(x => x.Table.Name.ToPascalCase()));
            }
        }
    }

    /// <summary>
    /// Finds the tables a multi-target reference names, and reports the ones that are not
    /// there.
    /// </summary>
    /// <remarks>
    /// No chain and no resolved field: such a column carries an id, and every table it names
    /// is somewhere that id may live. Which one holds it is answered per target in the
    /// generated code rather than decided here - deciding it is what would need a sum type.
    ///
    /// The type is left alone, which is the whole of how "does not resolve to one record" is
    /// expressed: the field is not a <see cref="ValueType.ForeignRecord"/>, so nothing that
    /// reads one has to learn a new state. spec/multi-target-references.md.
    /// </remarks>
    private void ResolveMultiTargetReference(Table table, Field field, Diagnostics diagnostics)
    {
        var targets = new List<Table>();

        foreach (string name in field.RefTableNames!)
        {
            var target = FindTable(name);
            if (target is null)
            {
                diagnostics.Error(field.DetailTypeLocation,
                    Messages.Message.Of(Cooking.CookingMessages.ReferenceTableMissing,
                        ("Table", table.Name), ("Field", field.Name), ("Target", name)));
                return;
            }

            targets.Add(target);
        }

        field.ResolvedRefTables = targets;

        // The key every one of them is addressed by has to be the same, because the column
        // carries one value. Sheets that name several tables key them alike; saying so is
        // what stops a generated accessor from looking a `string` up in an `int` dictionary.
        var keyTypes = targets
            .Select(t => t.PrimaryIndexField?.Type ?? Models.ValueType.Int32)
            .Distinct()
            .ToList();

        if (keyTypes.Count > 1)
        {
            diagnostics.Error(field.DetailTypeLocation,
                Messages.Message.Of(Cooking.CookingMessages.MultiTargetKeysDiffer,
                    ("Table", table.Name), ("Field", field.Name),
                    ("Targets", string.Join("`, `", targets.Select(t => t.Name))),
                    ("KeyTypes", string.Join(
                        "` and `", keyTypes.Select(k => k.ToString().ToLowerInvariant())))));
            return;
        }

        field.RefKeyType = keyTypes[0];

        // And the column becomes that key. It was read as an ordinary number - a `double`,
        // in the layout this comes from - and what it actually holds is the id its targets
        // are addressed by. Saying so is what makes the value travel at the key's width
        // instead of a wider one that happens to fit it.
        //
        // Arrays are left alone: the element type is the key already, and rewriting the
        // declared type would say the column holds one value where it holds several.
        if (!ValueTypes.IsArray(field.Type))
        {
            field.Type = field.RefKeyType;
            field.TypeName = field.RefKeyType.ToString().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Walks a reference to whatever it ultimately points at, following further
    /// references along the way.
    ///
    /// Resolving the target and describing the chain used to be two methods that
    /// walked the same links with the same rules, which meant every fix had to be
    /// made twice. They are one walk now: the chain falls out of the traversal
    /// that resolves the target.
    /// </summary>
    /// <returns>False when the reference could not be resolved, having recorded why.</returns>
    private bool TryResolveReference(Table table, Field refererField, Diagnostics diagnostics, out List<Reference> referenceChain)
    {
        referenceChain = new List<Reference>();

        var fieldNode = refererField;

        // Tracks the fields already walked. The only cycle check used to be "does
        // this land back on the table we started from", so a cycle that excludes
        // the starting table - B.g points at C.h, C.h back at B.g - spun forever
        // and the tool hung with no output at all.
        var visited = new HashSet<Field> { refererField };

        for (; ; )
        {
            var refTable = FindTable(fieldNode.RefTableName);
            if (refTable is null)
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    Messages.Message.Of(Cooking.CookingMessages.ReferenceTableMissing,
                        ("Table", table.Name), ("Field", refererField.Name),
                        ("Target", fieldNode.RefTableName)));
                return false;
            }

            // Refused only where a chain can form. A whole-row reference stops the moment
            // its target is found, so pointing at your own table is a row naming another row
            // of the same table - which is how a grouping id is written, and 21,261 rows of
            // one live workbook are exactly that. The dotted form does walk on, and there a
            // reference into its own table is the first step of a loop.
            // spec/multi-target-references.md.
            if (refTable == table && !string.IsNullOrEmpty(fieldNode.RefFieldName))
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    Messages.Message.Of(Cooking.CookingMessages.ReferenceFieldOfOwnTable,
                        ("Table", table.Name), ("Field", refererField.Name),
                        ("RefField", fieldNode.RefFieldName)));
                return false;
            }

            refererField.ResolvedRefTable = refTable;

            if (string.IsNullOrEmpty(fieldNode.RefFieldName))
            {
                refererField.ResolvedRefField = null;
                referenceChain.Add(new Reference { Table = refTable, Field = null });
                return true;
            }

            var refField = refTable.FindField(fieldNode.RefFieldName);
            if (refField is null)
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    Messages.Message.Of(Cooking.CookingMessages.ReferenceFieldMissing,
                        ("Table", table.Name), ("Field", refererField.Name),
                        ("RefTable", fieldNode.RefTableName),
                        ("RefField", fieldNode.RefFieldName),
                        ("Resolved", refTable.Name)));
                return false;
            }

            referenceChain.Add(new Reference { Table = refTable, Field = refField });

            if (!refField.IsRef)
            {
                refererField.ResolvedRefField = refField;
                return true;
            }

            if (!visited.Add(refField))
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    Messages.Message.Of(Cooking.CookingMessages.ReferenceCycle,
                        ("Table", table.Name), ("Field", refererField.Name),
                        ("Returns", $"{refField.OwnerTable?.Name}.{refField.Name}")));
                return false;
            }

            fieldNode = refField; // Chain
        }
    }

    #endregion
}
