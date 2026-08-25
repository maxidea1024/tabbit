using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Models;

/// <summary>
/// One column of a table.
///
/// A field keeps four separate cell locations because the four header rows that
/// describe it can each be wrong on their own - a bad type and a bad target side are
/// different mistakes in different cells, and a diagnostic should point at the one
/// that is actually at fault.
/// </summary>
public class Field
{
    /// <summary>Cell holding the field's name.</summary>
    [JsonIgnore]
    public required Location NameLocation { get; set; }

    /// <summary>Cell holding the field's type.</summary>
    [JsonIgnore]
    public required Location TypeLocation { get; set; }

    /// <summary>
    /// Cell holding the detail type - the enum name, or the reference target. Blank
    /// for a plain scalar field.
    /// </summary>
    [JsonIgnore]
    public required Location DetailTypeLocation { get; set; }

    /// <summary>Cell holding the field's target side.</summary>
    [JsonIgnore]
    public required Location TargetSideLocation { get; set; }

    /// <summary>
    /// Table this field belongs to. Used by diagnostics that need to name the field
    /// in full.
    /// </summary>
    [JsonIgnore]
    public required Table OwnerTable { get; set; }

    /// <summary>Name exactly as written in the sheet, `*` prefix included.</summary>
    public string RawName { get; set; } = "";

    /// <summary>
    /// Name normalized to Pascal case with any `*` prefix removed. This is what
    /// generated code uses.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>Target side filtering option</summary>
    public TargetSide TargetSide { get; set; }

    /// <summary>
    /// Whether a row has to give this column a value.
    /// </summary>
    /// <remarks>
    /// True unless the type cell ends in `?`. `int` wants a number in every row; `int?` takes
    /// a blank cell and reads it as the type's empty value.
    ///
    /// The marker is on the type because that is where the question lives - "is a number
    /// required here" is about the number - and because it reads as the same thing the
    /// languages generated from it mean by `?`.
    ///
    /// What it changes today is the types a blank cell is already refused for: numbers, dates,
    /// durations, uuids and enum labels. `string` and `bool` have always taken a blank as an
    /// empty string and false, which is documented and relied on, so requiring them is a
    /// separate decision rather than something this quietly turns on.
    /// </remarks>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// The name this column is matched by when another set of the table's rows is laid onto
    /// its columns. Empty means <see cref="RawName"/>, which is the ordinary case.
    /// </summary>
    /// <remarks>
    /// The folding matches by name rather than by position, because two sets of one table's
    /// rows do not have to be the same width. That works as long as the name says which column
    /// it is - and a layout may rename a column to something positional, in which case the
    /// name no longer does. A grid is that case: its column names are ids of another axis, so
    /// the layout renames them to `Value[0]`, `Value[1]`, and a set missing one id would then
    /// be laid out shifted by one from there on.
    ///
    /// So the layout that renames a column may say what to match it by instead. **Nothing here
    /// knows what a grid is** - only that this column has a name of its own for this one
    /// purpose. spec/table-row-sets.md · spec/matrix-tables.md.
    /// </remarks>
    public string SetAlignName { get; set; } = "";

    /// <summary>
    /// Whether every element of this column's array has to be a value.
    /// </summary>
    /// <remarks>
    /// True unless the type cell writes the marker inside the brackets: `int[]` and `int[]?`
    /// want a number in every element, and `int?[]` takes `-` in one of them.
    ///
    /// Independent of <see cref="IsRequired"/>, which answers for the array itself, because
    /// the two are different facts - `int?[]?` says both may be absent and `int?[]` says only
    /// an element may. A column that is not an array leaves this true; there is nothing for
    /// it to say.
    ///
    /// spec/nullable-array-elements.md.
    /// </remarks>
    public bool ElementsRequired { get; set; } = true;

    /// <summary>
    /// The column's name read as a path into the row, or null for an ordinary column.
    /// </summary>
    /// <remarks>
    /// One step per level, outermost first. `Slot1.Id` is `Slot` holding element 1 and then
    /// `Id`; `character[0]["Id"]` is that same path in the other notation. Carried rather
    /// than derived so the folding never re-reads digits out of a name - each layout works
    /// its own notation out and they meet here, which is what keeps one model behind two
    /// notations.
    ///
    /// One step means the column is an element of a plain array. Two or more mean it sits
    /// inside a record, **however deep** - the folding walks the steps and nothing in it
    /// counts them. See spec/nested-multi-level.md.
    ///
    /// <see cref="Name"/> stays a single valid identifier (`Slot1Id`) so duplicate
    /// detection, lookup and every language's spelling rules keep working unchanged; this
    /// is what the folding uses to build the record.
    /// </remarks>
    public List<FieldPathStep>? NamePath { get; set; }

    /// <summary>
    /// Outermost level of <see cref="NamePath"/> - the group this column belongs to - or
    /// null for an ordinary column.
    /// </summary>
    [JsonIgnore]
    public string? GroupName => (NamePath is null || NamePath.Count == 0) ? null : NamePath[0].Name;

    /// <summary>Whether this column sits inside a record, at any depth.</summary>
    [JsonIgnore]
    public bool IsRecordMember => NamePath is not null && NamePath.Count > 1;

    /// <summary>
    /// Whether this column carries which variant each row of its group is.
    /// </summary>
    /// <remarks>
    /// The `$type` column. What the cell holds is a variant name and what the file carries is
    /// that variant's `@N`, which is the same relation an enum column has to its labels -
    /// and the reason no enum is generated for it. spec/polymorphism.md sections 5.2 and 7.1.
    /// </remarks>
    public bool IsDiscriminator
        => NamePath is not null && NamePath.Count > 0 && NamePath[^1].IsDiscriminator;

    /// <summary>
    /// The variants that declare this member, for a column inside a polymorphic group.
    /// </summary>
    /// <remarks>
    /// **Empty means the abstract type's own field** - a base field, present in every row of
    /// the group, which is why it is a plain column and not an optional one. A member several
    /// variants declare is here once per variant: the column is shared, and a row may fill it
    /// only if its own variant is in this list. spec/polymorphism.md sections 5.1 and 5.2.
    /// </remarks>
    public List<string> VariantsDeclaringThis { get; set; } = new List<string>();

    /// <summary>
    /// The variants of the abstract type this column discriminates, in declaration order.
    /// </summary>
    /// <remarks>
    /// Only on the `$type` column. Held here rather than looked up again from the schema
    /// because the exporters and the generators read the model and not the declarations.
    /// </remarks>
    public List<PolymorphicVariant> Variants { get; set; } = new List<PolymorphicVariant>();

    /// <summary>The abstract type a `$type` column names, or null.</summary>
    public string? AbstractTypeName { get; set; }

    /// <summary>
    /// Whether this column is one element of an array of plain values.
    /// </summary>
    /// <remarks>
    /// A path of one level, which is what a serial field is: several columns, one array.
    /// Reached by a layout that states the element number outright instead of leaving it to
    /// be read out of the name - so the folding needs no digit rule and cannot be wrong
    /// about one.
    /// </remarks>
    [JsonIgnore]
    public bool IsArrayElement => NamePath is not null && NamePath.Count == 1;

    /// <summary>
    /// Type as written in the sheet. For an enum field this is the enum's name, and
    /// for a resolved reference it becomes the referenced field's type name.
    /// </summary>
    public string TypeName { get; set; } = "";

    /// <summary>
    /// Position of this field's column within the table.
    ///
    /// How every value is addressed: a row is a flat list of cells and this indexes
    /// into it. Target-side filtering narrows the field list without renumbering, so
    /// an index always refers to the same column of the original sheet.
    /// </summary>
    public int Index { get; set; }

    /// <summary>Resolved type.</summary>
    public ValueType Type { get; set; }

    /// <summary>
    /// The composite type this column was expanded out of, or <see cref="ValueType.None"/>
    /// for a column the sheet wrote as itself.
    /// </summary>
    /// <remarks>
    /// A `vec3f` column reaches the model as three `float` columns, and the three are exactly
    /// what a sheet writing `Pos.X`/`Pos.Y`/`Pos.Z` by hand produces - that identity is what
    /// keeps the wire and thirteen generators out of this feature. What is lost with it is
    /// the knowledge that the three were one thing, and this is where that is kept.
    ///
    /// Nothing reads it yet. It is what a later revision needs to give the group a shared
    /// type - the engine's own vector rather than a struct per column - without having to
    /// infer from three members that they were a vector.
    /// spec/composite-value-types.md section 7.
    /// </remarks>
    [JsonIgnore]
    public ValueType CompositeOrigin { get; set; }

    /// <summary>
    /// What this column's strings are for, beyond being strings. <see cref="StringRole.None"/>
    /// for every other type.
    /// </summary>
    /// <remarks>
    /// Set by a layout from whatever its notation writes - a type name of its own, a marker
    /// row - and read by the targets that act on it. <see cref="Type"/> stays
    /// <see cref="ValueType.String"/> either way, which is what keeps a role free of the
    /// output: see <see cref="StringRole"/>.
    /// </remarks>
    public StringRole Role { get; set; }

    /// <summary>
    /// Which set this column's values are gathered into, or null to use the table's name.
    /// </summary>
    /// <remarks>
    /// Meaningful only where <see cref="Role"/> is one that gathers. The default is the table
    /// name rather than a single shared set, because the file a value lands in is how anyone
    /// downstream finds it again - and one file per table is the grouping a sheet already has.
    ///
    /// Null rather than the resolved name, so the two cases stay distinguishable: a column
    /// that named its group and a column that took the default should not read the same to a
    /// diagnostic, and a table renamed afterwards should move the second but not the first.
    /// </remarks>
    public string? RoleGroup { get; set; }

    /// <summary>
    /// The namespace this column's values belong to, or null when the sheet named none.
    /// </summary>
    /// <remarks>
    /// Written as the second part of the group - `text(Achievement,Quests)` - and meaningful
    /// only where <see cref="Role"/> is one that gathers. Null leaves the answer to whatever
    /// is writing the output, which usually has one setting for the whole export.
    ///
    /// Separate from <see cref="RoleGroup"/> because the two are separate questions that
    /// happen to be written together: the group decides which file a value lands in, and the
    /// namespace is carried into that file beside the value. A pipeline that keys strings by
    /// namespace does not have to split its files the same way.
    /// </remarks>
    public string? RoleNamespace { get; set; }

    /// <summary>Description from the sheet, emitted as a doc comment.</summary>
    public required string Comment { get; set; }

    /// <summary>
    /// Whether this field is an index, so its values must be unique.
    ///
    /// True for the first column always, and for any field whose name carries a
    /// leading `*`.
    /// </summary>
    [JsonIgnore]
    public bool Indexing { get; set; }

    /// <summary>
    /// What the sheet declared about the values this column may hold, beyond its type.
    /// </summary>
    /// <remarks>
    /// Empty unless a layout filled it in. The type says what a value is and this says
    /// which of those are allowed - see <see cref="ColumnConstraints"/>.
    /// </remarks>
    public ColumnConstraints Constraints { get; set; } = new ColumnConstraints();

    /// <summary>
    /// Table this field references, as written in the detail-type cell. Empty when
    /// the field is not a reference, and when it names more than one.
    /// </summary>
    /// <remarks>
    /// The one table a reference names. `foreign` takes one, so "resolves to exactly one
    /// record" holds for every reference there is - a value that may be an id of any of
    /// several tables is a check and not a reference, written `refs=` and left as the
    /// number it was. spec/reference-surface-naming.md section 6.
    /// </remarks>
    public string? RefTableName { get; set; }

    /// <summary>
    /// Field within the referenced table, for the `RefTable.RefFieldName` form.
    /// Null or empty when the reference names the whole row.
    /// </summary>
    public string? RefFieldName { get; set; }

    /// <summary>
    /// The table actually pointed at, filled in once references are resolved. Null
    /// when resolution failed, which the diagnostics will have reported.
    /// </summary>
    [JsonIgnore]
    public Table? ResolvedRefTable { get; set; }

    /// <summary>
    /// The field actually pointed at, or null for a whole-row reference.
    /// </summary>
    [JsonIgnore]
    public Field? ResolvedRefField { get; set; }

    /// <summary>
    /// The type of the value a reference column actually holds: the target's primary index.
    /// Meaningless on a field that is not a reference.
    /// </summary>
    /// <remarks>
    /// A reference's own <see cref="Type"/> is what the generated code hands back - a record,
    /// or the type of the field a dotted reference names - and that is not what travels. What
    /// travels is the key, and its type is the target's to decide.
    ///
    /// This existed as the constant `int32` in six places: the exporters, the format's
    /// element mapping, the generators' read switches and the SQL schemas. Meanwhile
    /// index keys had been generalized to anything that can tell rows apart, so a table keyed
    /// by `string` could be read and generated but not pointed at. Holding the answer here is
    /// what lets those places ask instead of assume.
    ///
    /// `Int32` until resolution fills it in, which is both the old behaviour and the common
    /// case. spec/reference-key-types.md.
    /// </remarks>
    [JsonIgnore]
    public ValueType RefKeyType { get; set; } = ValueType.Int32;

    /// <summary>
    /// The chain a reference walks, joined with underscores: a field pointing through
    /// A to B to C gives `A_B_C`.
    ///
    /// Used to name generated members so two references that end up at the same type
    /// through different paths do not collide.
    /// </summary>
    [JsonIgnore]
    public string? RefChainPath { get; set; }

    /// <summary>Whether this field references another table.</summary>
    [JsonIgnore]
    /// <summary>
    /// Whether this field's name was invented by the tool rather than written in a cell.
    /// </summary>
    /// <remarks>
    /// **A naming convention is a claim about what people write.** A composite value type
    /// expands into one field per component and the component's part of the name is this
    /// tool's - `.X` · `.Y` · `.Z` on a `vec3f`, `.R` · `.G` · `.B` · `.A` on a `color` - so
    /// holding it to the recipe's `Naming` rules reports a spelling nobody chose and points
    /// at a cell that does not hold it. Enums have had the same exemption since they gained
    /// a synthesized zero label.
    ///
    /// A member of a declared struct is **not** this: somebody wrote that name in a `.tbs`
    /// file, and it is judged - under the spelling the declaration used.
    /// </remarks>
    public bool Synthesized { get; set; }

    public bool IsRef => !string.IsNullOrEmpty(RefTableName);

    /// <summary>
    /// The column's wire tag: what identifies it in a binary file, instead of its position.
    /// </summary>
    /// <remarks>
    /// Comes from an `@N` suffix on the sheet's field name (`Price@3`), or is assigned by
    /// ordinal after the table is parsed when no field in the table carries one. By the time
    /// anything downstream reads it, it is never null - the cooker's AssignTags fills it.
    ///
    /// For a serial field, the tag lives on the first column and identifies the whole
    /// logical column; the other members must not carry one.
    ///
    /// **`WireTag` rather than `Tag`, because "tag" is about to mean something else too** -
    /// the recipe's include/exclude tags, which select what a build carries and have nothing
    /// to do with a column's identity in the file. A variant's `@N` is a third number and is
    /// called a discriminator for the same reason. spec/polymorphism.md section 5.1.1.
    /// </remarks>
    public int? WireTag { get; set; }

    /// <summary>
    /// Whether this field's cells hold a delimited list.
    ///
    /// Only true of the `T[]` types. A serial field is also an array to its
    /// consumers, but that is a property of the group rather than of one column -
    /// see <see cref="SerialField.IsArray"/>.
    /// </summary>
    [JsonIgnore]
    public bool IsArray => ValueTypes.IsArray(Type);

    /// <summary>
    /// Element type for an array field; the field's own type when it is scalar.
    /// </summary>
    [JsonIgnore]
    public ValueType ElementType => ValueTypes.ElementOf(Type);

    /// <summary>
    /// The enum this field's type refers to, or throws if it has none.
    ///
    /// Prefer <see cref="EnumOrNull"/> where the field's type is not already known to
    /// be an enum; this overload exists for the code paths that have just tested it
    /// and would rather not test again.
    /// </summary>
    [JsonIgnore]
    public Enum Enum
    {
        get
        {
            // Element type, so an `enum[]` field resolves against the same
            // declaration a scalar `enum` field would.
            if (ElementType != ValueType.Enum)
            {
                    throw new TabbitException(NameLocation,
                        Messages.Message.Of(Cooking.CookingMessages.FieldIsNotAnEnum,
                            ("Table", OwnerTable?.Name), ("Field", Name), ("Type", TypeName)));
            }

            return Model.Current.GetEnum(TypeName, null)!;
        }
    }

    /// <summary>
    /// The enum this field's type refers to, or null if it has none.
    /// </summary>
    [JsonIgnore]
    public Enum? EnumOrNull
    {
        get
        {
            // Accepts EnumArray as well: an array of enum labels resolves against
            // the same declaration as a scalar one.
            if (ElementType != ValueType.Enum)
                return null;

            return Model.Current.GetEnum(TypeName, null);
        }
    }

    // Reserved for describing database column constraints - nullability, length,
    // uniqueness - from the sheet. Nothing sets either of these: the database
    // exporters derive their column types from ValueType and make every column NOT
    // NULL, since a sheet cell always has a value even when that value is empty.
    // Declaring constraints would need somewhere in the sheet to say so.

    /// <summary>Reserved. Not populated.</summary>
    [JsonIgnore]
    public bool IsNullable { get; set; }

    /// <summary>Reserved. Not populated.</summary>
    [JsonIgnore]
    public int Length { get; set; }
}
