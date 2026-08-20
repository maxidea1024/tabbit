using Tabbit.Models;
using Tabbit.Extensions;
using Tabbit.Helpers;
using Tabbit.Recipe;
using Tabbit.Targets;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Settings for the Kotlin target.
/// </summary>
public sealed class KotlinRecipe : IOutputRecipe
{
    /// <summary>Source root. The package's directories are created underneath it.</summary>
    public string Path { get; set; } = "";

    /// <summary>Package the generated file declares.</summary>
    public string PackageName { get; set; } = "gamedata";

    /// <summary>Name of the accessor object, which also names the file.</summary>
    public string AccessorName { get; set; } = "Tables";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    /// </summary>
    /// <remarks>
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a program can take new data without being redeployed. Off by
    /// default: one that ships its data alongside its code has no use for it, and this
    /// is the only generated file that reaches the network.
    /// </remarks>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>
    /// Whether generated files this run did not write are removed from <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// On, because the output is a file per table: delete a table from the sheets and its
    /// file stays behind naming types nothing declares any more. Only files carrying this
    /// tool's own header are removed, so a directory holding your own source is safe.
    ///
    /// Turn it off if you edit the generated files, which is a decision worth a line in a
    /// recipe.
    /// </remarks>
    public bool Sweep { get; set; } = true;

    /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
    public string TargetSide { get; set; } = "cs";

    /// <summary>
    /// Spelling of the generated record members. Blank keeps the one this language normally
    /// uses.
    /// </summary>
    /// <remarks>
    /// Takes `pascal`, `camel`, `snake` or `upper-snake`. For a project whose own code has a
    /// convention the generated code should match, which is the one place the two meet: every
    /// other generated name is a type, a file or a method, and those follow the language.
    ///
    /// It moves the members and nothing else. The type names, the lookup methods, the
    /// element-count constants and the data files stay as they are - a member's spelling is
    /// not a fact about any of them, and a setting that moved all of them together would be
    /// renaming the output rather than spelling it.
    /// </remarks>
    public string MemberCase { get; set; } = "";
}

/// <summary>
/// Emits one Kotlin file holding every generated type, plus the binary reader.
///
/// A Kotlin reader rather than the Java one, even though Kotlin would call it happily:
/// kotlinc reads Java sources for resolution but does not compile them, so a pure
/// Kotlin project would need javac in its build purely to get a reader.
///
/// The shape lives in templates/kotlin.sbn.
/// </summary>
[TabbitTarget("kotlin", TargetKind.CodeGeneration, Order = 85)]
public class KotlinCodeGenerator : CodeGenerator<KotlinRecipe>
{
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private KotlinRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Camel;

    /// <summary>
    /// A record group generates a nested class and a list of it; a member column fills one of
    /// its properties.
    /// </summary>
    /// <remarks>
    /// The ninth of the thirteen, and the same split as the eight before it - declaration per
    /// field, reading per wire column.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a class nested in the same record, and the read reaches it with a longer
    /// member path. Its member is initialized by calling that class - Kotlin has no
    /// uninitialized property to leave it at. spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `has{Field}` property beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `T?`, which is the shape Kotlin would suggest. Every generated property is
    /// initialized rather than nullable for a reason this repeats: a caller reading a value
    /// should not have to answer for a row the read never reached. spec/optional-fields.md
    /// has the rest.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, KotlinRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Camel, "kotlin");

        Generate();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes a file per table, per enum and per constant set, plus the accessor.
    /// </summary>
    /// <remarks>
    /// It used to be one file holding all of it, which made a deleted table a hunk of dead
    /// code inside a file that still compiled. The layout is the one the TypeScript and C#
    /// targets have, because a consumer working in more than one should not have to learn
    /// a shape per language.
    ///
    /// Kotlin has no rule tying a file's name to what is in it, so these names are for
    /// people rather than the compiler - which is why the table files keep the `Table`
    /// suffix their class has.
    /// </remarks>
    /// <summary>The accessor type's name, in the casing this language's types use.</summary>
    private string AccessorType => _recipe.AccessorName.ToPascalCase();

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Kotlin into `{System.IO.Path.GetFullPath(PackageDir)}`");

        Write(_recipe.AccessorName + ".kt", "kotlin-accessor.sbn", view);

        foreach (var table in view.Tables)
            Write(System.IO.Path.Combine("tables", table.TableName + ".kt"),
                  "kotlin-table.sbn", Part(table: table));

        foreach (var enumm in view.Enums)
            Write(System.IO.Path.Combine("enums", enumm.Name + ".kt"),
                  "kotlin-enum.sbn", Part(enumm: enumm));

        foreach (var set in view.ConstantSets)
            Write(System.IO.Path.Combine("constants", set.Name + ".kt"),
                  "kotlin-constants.sbn", Part(set: set));
    }

    /// <summary>
    /// The package's own directory, which the generated files live under.
    /// </summary>
    private string PackageDir => System.IO.Path.Combine(
        new[] { _recipe.Path }.Concat(_recipe.PackageName.Split('.')).ToArray());

    private KotlinPartView Part(
        KotlinTableView? table = null, KotlinEnumView? enumm = null, KotlinConstantSetView? set = null)
        => new KotlinPartView
        {
            PackageName = _recipe.PackageName,
            AccessorName = AccessorType,
            Table = table,
            Enumm = enumm,
            Set = set,
        };

    private void Write(string relative, string templateName, object view)
    {
        string filename = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(PackageDir, relative));

        StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render(templateName, view));
    }

    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Kotlin.TcbReader.kt",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "TcbReader.kt"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Kotlin.TabbitUpdater.kt",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "TabbitUpdater.kt"));
        }
    }

    // --------------------------------------------------------------- view

    private KotlinFileView BuildView() => new KotlinFileView
    {
        PackageName = _recipe.PackageName,
        AccessorName = AccessorType,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private KotlinEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new KotlinEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            DefaultLabel = ConstantName(fallback.Name),
            Labels = enumm.Labels.Select((label, index) => new KotlinEnumLabelView
            {
                Name = ConstantName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),

                // A Kotlin enum body needs a semicolon after the last constant when
                // anything follows it, and a companion object always does.
                Separator = index == enumm.Labels.Count - 1 ? ";" : ",",
            }).ToList(),
        };
    }

    private KotlinConstantSetView BuildConstantSet(ConstantSet constantSet) => new KotlinConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new KotlinConstantView
        {
            Name = ConstantName(constant.Name),
            Type = ToKotlinTypeName(constant.Type, constant.Enum, null),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private KotlinTableView BuildTable(Table table) => new KotlinTableView
    {
        RawName = table.Name,
        RecordName = table.Name.ToPascalCase() + "Record",
        TableName = table.Name.ToPascalCase() + "Table",
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        MultiReferences = MultiTargetColumns.Of(table).Select(BuildMultiReference).ToList(),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),

        // A separate list, because declaring a property is per field and reading is per
        // column - and a record group is one column per member of it.
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<KotlinIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf => new KotlinIndexView
        {
            Member = KotlinName(sf.Name),
            Suffix = sf.Name.ToPascalCase(),
            KeyType = ResolvedElementType(sf),
            MapName = "by" + sf.Name.ToPascalCase(),
            FieldName = sf.Name.ToPascalCase(),
        }).ToList();

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `findByIndex`. The primary
    /// index is whatever the sheet put in the first column, and a sheet that calls it `Id`
    /// generates `findById`.
    /// </remarks>
    private static string PrimaryLookup(Table? refTable)
        => "findBy" + refTable!.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase();

    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// The key type's empty value means "points at nothing", and a multi-target column honours
    /// it in every language: the discriminator is a value a consumer reads.
    /// spec/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.String => ".isNotEmpty()",
            ValueType.Uuid => "!= null",
            _ => "!= 0",
        };

    /// <summary>
    /// One column whose value is a row of one of several tables.
    /// </summary>
    private KotlinMultiReferenceView BuildMultiReference(MultiTargetColumn column)
        => new KotlinMultiReferenceView
        {
            KeyMember = KotlinName(column.Group.Name),
            SlotMember = KotlinName(column.Group.Name) + "Row",
            TargetMember = KotlinName(column.Group.Name) + "Target",
            TargetTypeName = column.Discriminator.Name.ToPascalCase(),
            NoneLabel = ConstantName("None"),
            KeyIsSet = KeyIsSetSuffix(column.Field.RefKeyType),
            Targets = column.Targets.Select(target => new KotlinMultiTargetView
            {
                Table = KotlinName(target.Name),
                RecordName = target.Name.ToPascalCase() + "Record",
                Method = KotlinName(column.Group.Name + "As" + target.Name.ToPascalCase()),
                Label = ConstantName(target.Name),
                Lookup = PrimaryLookup(target),
            }).ToList(),
        };

    private KotlinFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = KotlinName(sf.Name);

        return new KotlinFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Declarations = Declarations(sf, name),
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<KotlinRecordMemberView>(),
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = PresenceMember(sf),
            ElementPresenceMember = PresenceMember(sf) + "At",
        };
    }

    /// <summary>
    /// A record group: the class to declare for one element, and the property holding one or
    /// a list of them.
    /// </summary>
    /// <remarks>
    /// Nested in the record, as the Java target does it and for the second of that target's
    /// two reasons: it scopes the name, so two tables each holding a `Slot` group do not
    /// collide in a package they share. Kotlin would have allowed a second top-level class in
    /// the file; that is what would have needed the table's name in the type's.
    ///
    /// No reference members: a reference belongs to a member and the model refuses one there,
    /// so nothing here has the index list and the setter a reference would need.
    /// </remarks>
    /// <summary>
    /// Members of one level of a record, declaring a class for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the classes it produces. A nested member is initialized by calling its own
    /// class, which is how the values inside it reach the empty values a scalar member gets -
    /// Kotlin has no uninitialized property to leave it at. spec/nested-multi-level.md.
    /// </remarks>
    private List<KotlinRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, SerialField group,
        List<KotlinRecordTypeView> declared)
    {
        var result = new List<KotlinRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                // A reference member holds two things where an ordinary one holds a value:
                // the row it resolved to, and the key that came off the wire. Both inside the
                // element, because a group may hold more than one reference and a name built
                // from the group and the target would collide the moment two members point at
                // one table.
                //
                // No third member for whether it resolved - a null row says so, which is how
                // this output already answers that outside a record.
                // spec/references-in-records.md.
                var declarations = new List<string>();

                if (member.IsRef)
                {
                    string row = member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";
                    string key = ToKotlinTypeName(member.FirstField!.RefKeyType, null, null);

                    declarations.Add(member.IsArray
                        ? $"var {KotlinName(member.Name)}: MutableList<{row}?> = "
                          + $"MutableList({member.Fields.Count}) {{ null }}"
                        : $"var {KotlinName(member.Name)}: {row}? = null");

                    declarations.Add(member.IsArray
                        ? $"var {KotlinName(member.Name)}Index: MutableList<{key}> = "
                          + $"MutableList({member.Fields.Count}) {{ {RefKeyDefault(member.FirstField!.RefKeyType)} }}"
                        : $"var {KotlinName(member.Name)}Index: {key} = "
                          + RefKeyDefault(member.FirstField!.RefKeyType));
                }
                else
                {
                    // The list is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    declarations.Add($"var {KotlinName(member.Name)}: "
                                + (member.IsArray
                                    ? $"MutableList<{ToKotlinTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null)}>"
                                    : ToKotlinTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null))
                                + " = "
                                + (member.IsArray
                                    ? $"MutableList({member.Fields.Count}) {{ {MemberDefault(member)} }}"
                                    : MemberDefault(member)));
                }

                result.Add(new KotlinRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),
                    Declarations = declarations,
                });

                continue;
            }

            // A level below. The class name carries the path: both levels are nested in the
            // same record, so two groups each holding a `Position` would otherwise collide.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, group, declared);

            declared.Add(new KotlinRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = KotlinName(group.Name),
            });

            result.Add(new KotlinRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declarations = new[] { $"var {KotlinName(member.Name)}: {typeName} = {typeName}()" },
            });
        }

        return result;
    }

    private KotlinFieldView BuildRecordField(Table table, SerialField sf)
    {
        string name = KotlinName(sf.Name);
        string entry = sf.Name.ToPascalCase() + "Entry";

        // Innermost first, so a class is declared before the one naming it.
        var recordTypes = new List<KotlinRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, entry, sf, recordTypes);

        recordTypes.Add(new KotlinRecordTypeView
        {
            TypeName = entry,
            Members = members,
            IsOutermost = true,
            Owner = name,
        });

        // A list with its elements already made, where the length is the sheet's column
        // count. A trimmed group starts empty, because its length is the row's.
        string initializer = sf.IsArray
            ? (table.TrimTrailingArrayElements
                ? "ArrayList()"
                : $"MutableList({sf.RecordElementCount}) {{ {entry}() }}")
            : $"{entry}()";

        string type = sf.IsArray ? $"MutableList<{entry}>" : entry;

        // An array of arrays declares no element type: the outer level has no name for one to
        // belong to, so the inner list is the type. spec/nested-multi-level.md.
        if (sf.MembersAreAnonymous)
        {
            string inner = ToKotlinTypeName(
                sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull, null);

            type = $"MutableList<MutableList<{inner}>>";
            initializer = $"MutableList({sf.Members.Count}) {{ MutableList({sf.RecordElementCount}) "
                        + $"{{ {MemberDefault(sf.Members[0])} }} }}";
        }

        return new KotlinFieldView
        {
            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,
            Declarations = new[] { $"var {name}: {type} = {initializer}" },
            IsRecord = true,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            RecordTypeName = entry,

            Members = members,
            RecordTypes = recordTypes,

            // A record group has no presence of its own: absence inside one is the list's
            // length, not a bit per member. WireColumn.Of says the same about the wire.
            IsNullable = false,
            PresenceMember = "",
        };
    }

    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where its values
    /// land.
    /// </summary>
    /// <remarks>
    /// The member it fills is the group's for a scalar column. A record group's member
    /// columns each fill one property of the generated element type, which is the whole of
    /// the difference - see spec/nested-fields.md.
    /// </remarks>
    private KotlinColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new KotlinColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire) ? "cursor.nextLength()" : "reader.readCounter32()",
            RefKeyType = wire.IsRef ? ToKotlinTypeName(wire.RefKeyType, null, null) : "",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = KotlinName(wire.Group.Name),

            // A record's member column assigns one property of the element rather than the
            // member itself: `record.slot[j].id` instead of `record.slot[j]`.
            MemberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + KotlinName(part))),

            // A reference member reads into the key beside the row it will resolve to, and the
            // suffix goes on the member rather than after the subscript, because a member that
            // is an array holds one key per element. spec/references-in-records.md.
            MemberRefSuffix = (wire.Member is not null && wire.IsRef) ? "Index" : "",
            MemberAt = wire.MemberAt,

            // Qualified, because the element class is nested in the record and this is read
            // from the table class beside it.
            RecordTypeName = wire.Group.IsRecord
                ? $"{table.Name.ToPascalCase()}Record.{wire.Group.Name.ToPascalCase()}Entry"
                : "",

            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,

            // The same expression at both positions, now that an array's elements read
            // through the cursor as well. Two properties still, because the template puts
            // them in two places and a reader of it should not have to know they agree.
            ReadScalar = ValueReadExpression(wire),
            ReadElement = ValueReadExpression(wire),
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = PresenceMember(wire.Group),
            ElementPresenceMember = PresenceMember(wire.Group) + "At",
            EmptyValue = EmptyValue(wire),
        };
    }

    /// <summary>
    /// The property a nullable column's presence lands in.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever reads
    /// it, and the model has already required its columns to agree about being optional.
    /// </remarks>
    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : KotlinName("has_" + sf.Name);

    /// <summary>What one record member starts at, for the same reason an ordinary one does.</summary>
    /// <summary>What a stored key holds before a row is read.</summary>
    /// <remarks>
    /// Spelled from the key's own type, because a `bigint` key is a `Long` and `0` does not
    /// assign to one. spec/reference-key-types.md.
    /// </remarks>
    private static string RefKeyDefault(ValueType keyType)
        => keyType switch
        {
            ValueType.Int64 => "0L",
            ValueType.String => "\"\"",
            ValueType.Uuid => "Uuid()",
            _ => "0",
        };

    private string MemberDefault(RecordMember member)
    {
        return member.ElementType switch
        {
            ValueType.String => "\"\"",
            ValueType.Bool => "false",
            ValueType.Int64 => "0L",
            ValueType.Float => "0.0f",
            ValueType.Double => "0.0",
            ValueType.DateTime => "0L",
            ValueType.TimeSpan => "0L",
            ValueType.Uuid => "Uuid()",
            ValueType.Enum => $"{member.FirstField!.Enum.Name.ToPascalCase()}.of(0)",
            _ => "0",
        };
    }

    /// <summary>
    /// What an absent row's property is set back to, so the binary path lands where the JSON
    /// one does.
    /// </summary>
    /// <remarks>
    /// The property's own type rather than its element's: an optional array declares a list,
    /// and its empty value is an empty list rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "ArrayList()";

        // The resolved property is a nullable reference to the target row, and absence there
        // is exactly what null says.
        if (wire.ElementType == ValueType.ForeignRecord)
            return "null";

        return wire.ElementType switch
        {
            ValueType.String => "\"\"",
            ValueType.Bool => "false",
            ValueType.Int64 => "0L",
            ValueType.Float => "0.0f",
            ValueType.Double => "0.0",
            ValueType.DateTime => "0L",
            ValueType.TimeSpan => "0L",
            ValueType.Uuid => "Uuid()",
            ValueType.Enum => $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(0)",
            _ => "0",
        };
    }

    /// <summary>
    /// The property declarations, each initialized.
    ///
    /// Initialized rather than `lateinit`, because Kotlin's null safety would otherwise
    /// turn a read of an unread record into a runtime failure where every other
    /// generated reader hands back a default.
    /// </summary>
    private IReadOnlyList<string> Declarations(SerialField sf, string name)
    {
        string elementType = ResolvedElementType(sf);

        if (sf.IsRef)
        {
            // The key the target is addressed by, not `Int`. The record-member path next door
            // has always asked; this one wrote the width in, so a reference whose target is
            // keyed by anything else declared a member the read could not fill.
            // spec/reference-key-types.md.
            string keyType = ToKotlinTypeName(sf.FirstField!.RefKeyType, null, null);

            return sf.IsArray
                ? new[]
                {
                    $"var {name}: MutableList<{elementType}> = ArrayList()",
                    $"var {name}Index: MutableList<{keyType}> = ArrayList()",
                }
                : new[]
                {
                    $"var {name}: {elementType}? = null",
                    $"var {name}Index: {keyType} = {RefKeyDefault(sf.FirstField!.RefKeyType)}",
                };
        }

        if (sf.IsArray)
            return new[] { $"var {name}: MutableList<{elementType}> = ArrayList()" };

        return new[] { $"var {name}: {elementType} = {DefaultValue(sf)}" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "false";
            case ValueType.Int64: return "0L";
            case ValueType.Float: return "0.0f";
            case ValueType.Double: return "0.0";
            case ValueType.DateTime:
            case ValueType.TimeSpan: return "0L";
            case ValueType.Uuid: return "Uuid()";
            case ValueType.Enum:
                return $"{sf.FirstField!.Enum.Name.ToPascalCase()}.of(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// The rendered checkColumn call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsVariableLengthArray
            ? "KIND_VAR_ARRAY"
            : (wire.IsFixedArray ? "KIND_FIXED_ARRAY" : "KIND_SCALAR");

        // -1 where one column owns the whole array: the file states how many elements it
        // holds and the read takes it from there, so there is no length here to hold it to.
        // A record member keeps its count - several columns fill one array and the number
        // they agree on is part of the generated shape, so a disagreement is a schema change
        // rather than data. spec/nullable-array-elements.md.
        bool ownsItsArray = wire.IsFixedArray && wire.Member is null;

        int count = wire.IsVariableLengthArray ? 0 : (ownsItsArray ? -1 : wire.Cells.Count);

        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `ELEMENT_I32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "ELEMENT_STRING",
                ValueType.Int64 => "ELEMENT_I64, ELEMENT_I32, ELEMENT_VARINT",
                ValueType.Uuid => "ELEMENT_UUID",
                _ => "ELEMENT_I32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32: accepted = "ELEMENT_I32, ELEMENT_VARINT"; break;
                case ValueType.Int64: accepted = "ELEMENT_I64, ELEMENT_I32, ELEMENT_VARINT"; break;
                case ValueType.Double: accepted = "ELEMENT_F64, ELEMENT_F32, ELEMENT_I32"; break;
                case ValueType.Float: accepted = "ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "ELEMENT_I64"; break;

                default:
                    throw new TabbitException($"The kotlin generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one. A function of its own
        // because the accepted elements are varargs.
        string check = wire.HasOptionalElements ? "checkColumnWithElements" : "checkColumn";

        return $"{check}(column, \"{tableName}.{wire.Name}\", {kind}, {count}, "
            + $"{nullable}, {accepted})";
    }

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from. The scalar elements that stay raw by spec keep
    /// reading the reader directly.
    /// </summary>
    private static bool UsesCursor(WireColumn wire)
    {
        // Arrays go through it too. An array block states an encoding for its elements and
        // one for its rows' lengths, and the cursor is what decodes both - so an array's
        // elements are read exactly the way a scalar column's are, one level down.
        //
        // Uuid is the exception, and the same one it has always been: no encoding applies to
        // it, so it has no cursor path to reach.
        if (wire.ElementType == ValueType.Uuid)
            return false;

        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return true;

        // A reference reaches the cursor when the key it carries does. An unconditional yes
        // was the int32 assumption in another place: a target keyed by `uuid` has no cursor
        // path any more than a `uuid` column does. spec/reference-key-types.md.
        if (wire.IsRef)
            return wire.RefKeyType != ValueType.Uuid;

        switch (wire.ElementType)
        {
            // Int64 and Double are here for their promotions as well as their own
            // dictionaries: the file may carry an i32 column - encoded - where the
            // member has since widened.
            case ValueType.Int32:
            case ValueType.Int64:
            case ValueType.Double:
            case ValueType.Float:
            case ValueType.Bool:
            case ValueType.Enum:
            case ValueType.String:

            // Ticks are an i64 column, so they meet the i64 dictionary like any other.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return true;

            // Uuid is the one scalar left raw: sixteen-byte entries rarely repeat
            // enough to pay for the index beside them.
            default:
                return false;
        }
    }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or nothing for
    /// a column that reads the reader directly. A `val`: each `when` branch is its own
    /// scope, so the declaration lives and dies with the column that made it.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"val cursor = ColumnCursor(reader, column, count, \"{tableName}.{wire.Name}\")"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to nextI32 or nextString: int32
    /// members, enums, references and strings. The other cursor scalars stay per-row -
    /// their encodings are dictionaries, where the per-row work is already one index
    /// lookup.
    /// </remarks>
    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        // A run says "this many rows hold the same value", which an array column's row does
        // not have one of. Its elements are read one at a time.
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "";

        // A reference runs on the key it carries, which is not always an int32. An enum's
        // underlying value is one. spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int32 => "nextSameI32",
                ValueType.String => "nextSameString",
                _ => "",
            };
        }

        if (wire.ElementType == ValueType.Enum)
            return "nextSameI32";

        return wire.ElementType switch
        {
            ValueType.Int32 => "nextSameI32",
            ValueType.String => "nextSameString",
            _ => "",
        };
    }

    /// <summary>
    /// The line assigning one row from the value the run decoded, inside the loop the
    /// template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string name = KotlinName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + KotlinName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            // The local the run decoded into is the key's - `runSameText` for a string.
            string local = wire.RefKeyType == ValueType.String ? "runSameText" : "runSameValue";

            return (wire.Member is null)
                ? $"loaded[i].{name}Index = cursor.{local}"
                : $"loaded[i].{name}{memberAccess}Index = cursor.{local}";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"loaded[i].{name}{memberAccess} = {wire.TagCarrier.Enum.Name.ToPascalCase()}.of(cursor.runSameValue)";

        if (wire.ElementType == ValueType.String)
            return $"loaded[i].{name}{memberAccess} = cursor.runSameText";

            return $"loaded[i].{name}{memberAccess} = cursor.runSameValue";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member loops the elements without allocating - the list was made with
    /// the record - while a trimmed one reads its length from the row, and there the first
    /// member does allocate because no declaration could have known how long this row's is.
    /// </remarks>
    private static string ReadKind(WireColumn wire)
    {
        if (wire.Member is not null)
        {
            if (wire.IsVariableLengthArray)
                return "record_var";

            if (!wire.IsFixedArray)
                return "scalar";

            // Which of the two owns the list decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_serial" : "record_serial";
        }

        if (wire.IsVariableLengthArray)
            // A trimmed array of references: the length is the row's, and the key still goes in
            // the list beside the values. Read as a plain `var_array` it added the key to the
            // list of rows, which does not compile - and nothing held the shape, because
            // `foreign[]` is refused and this is only reachable through a folded group with
            // trimming on. spec/variable-length-record-arrays.md.
            return wire.IsRef ? "var_array_ref" : "var_array";

        if (wire.IsFixedArray)
            return wire.IsRef ? "serial_ref" : "serial";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private KotlinAccessorView BuildAccessor() => new KotlinAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new KotlinTableSlotView
        {
            Name = KotlinCamelName(table.Name),
            TableName = table.Name.ToPascalCase() + "Table",

            // Unescaped: this one names the file the exporter wrote.
            DataFileName = table.DataFileName,
        }).ToList(),

        CrossReferences = _model.Tables
            .Select(table => new
            {
                Table = table,
                Fields = table.SerialFields.Where(sf => sf.IsRef).ToList(),

                // A reference that is a member of a record resolves inside the element rather
                // than beside it, so it is a loop of its own. Read off the wire columns, which
                // is the same list the read path walks - the two have to agree about where the
                // key landed. spec/references-in-records.md.
                RecordFields = table.WireColumns
                                    .Where(wire => wire.Member is not null && wire.IsRef)
                                    .Select(BuildRecordReference)
                                    .ToList(),

                // A column reaching several tables is looked up in each of them in turn, so it
                // is a loop of its own too. spec/multi-target-accessors.md.
                MultiFields = MultiTargetColumns.Of(table).Select(BuildMultiReference).ToList(),
            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0 || x.MultiFields.Count > 0)
            .Select(x => new KotlinCrossReferenceView
            {
                Table = KotlinName(x.Table.Name),
                MultiFields = x.MultiFields,
                Fields = x.Fields.Select(sf => new KotlinReferenceFieldView
                {
                    Name = KotlinName(sf.Name),
                    RefTable = KotlinName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + KotlinName(sf.FirstField!.ResolvedRefField!.Name),
                    IsArray = sf.IsArray,
                }).ToList(),
                RecordFields = x.RecordFields,
            })
            .ToList(),
    };

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// No resolution flag: a null row says whether it resolved, which is how this output
    /// already answers that for a reference outside a record. The loop bound says which of
    /// the three record shapes this is - the group's list, the member's, or neither.
    /// spec/references-in-records.md.
    /// </remarks>
    private KotlinRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = KotlinName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + KotlinName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsFixedArray || wire.IsVariableLengthArray;

        string path = !isArray || wire.Group.MembersAreArrays
            ? $"record.{name}{member}"
            : $"record.{name}[i]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[i]" : "";

        return new KotlinRecordReferenceView
        {
            Access = path + subscript,
            Key = path + "Index" + subscript,

            // Whichever list holds the elements. Its own size rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Count = isArray
                ? (wire.Group.MembersAreArrays ? $"{path}Index.size" : $"record.{name}.size")
                : "",

            RefTable = KotlinName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The expression one value reads: through the cursor where the column can arrive
    /// encoded - which also carries the lossless promotions - and the direct read
    /// everywhere else.
    /// </summary>
    /// <remarks>
    /// One expression for a scalar row and for an array element alike. An array's elements
    /// read through the cursor by the same calls a scalar column's do; what differs is only
    /// that the row's length comes from the cursor first.
    /// </remarks>
    private string ValueReadExpression(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return ReadExpression(wire);

        // Only the stored index is on the wire for a reference; the template assigns
        // it to the Index member, and the accessor resolves it once every table loads.
        // The key the target is addressed by, which is not always an int32. `nextI32` for
        // every reference is what kept a table keyed by anything else from being pointed at
        // from this language. spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => "cursor.nextI64()",
                ValueType.String => "cursor.nextString()",
                _ => "cursor.nextI32()",
            };
        }

        switch (wire.ElementType)
        {
            // Enum values are still ints on the wire; the conversion stays, the int
            // now comes from the cursor.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(cursor.nextI32())";

            case ValueType.Int32: return "cursor.nextI32()";
            case ValueType.Int64: return "cursor.nextI64()";
            case ValueType.Double: return "cursor.nextF64()";
            case ValueType.Float: return "cursor.nextF32()";
            case ValueType.Bool: return "cursor.nextBool()";

            // Ticks, which is what the member holds, now read from the i64 column
            // through the cursor rather than straight off the reader.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "cursor.nextI64()";

            default: return "cursor.nextString()";
        }
    }

    private string ReadExpression(WireColumn wire)
    {
        switch (wire.ElementType)
        {

            // Enum values travel zig-zag encoded rather than fixed width.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(reader.readEnum())";

                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/reference-key-types.md.
                case ValueType.ForeignRecord:
                    return LanguageProfile.Kotlin.ReadCall(wire.RefKeyType);

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            default: return LanguageProfile.Kotlin.ReadCall(wire.ElementType);
        }
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        return ToKotlinTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull, null);
    }

    private string ToKotlinTypeName(ValueType type, Models.Enum? enumm, string? refTableName)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return enumm!.Name.ToPascalCase();

            case ValueType.ForeignRecord:
                return refTableName.ToPascalCase() + "Record";

            default:
                return LanguageProfile.Kotlin.ScalarTypeName(type);
        }
    }

    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        switch (constant.Type)
        {
            case ValueType.String:
                return Quote((string)constant.Value!);

            case ValueType.Bool:
                return (bool)constant.Value! ? "true" : "false";

            case ValueType.Int32:
                return ((int)constant.Value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)constant.Value!).ToString(CultureInfo.InvariantCulture) + "L";

            case ValueType.Float:
                return ((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture) + "f";

            case ValueType.Double:
                return ((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture);

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture) + "L";

            case ValueType.TimeSpan:
                return ((TimeSpan)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture) + "L";

            case ValueType.Uuid:
                return "Uuid(byteArrayOf(" + string.Join(", ",
                    ((Guid)constant.Value!).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture) + ".toByte()")) + "))";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}.{ConstantName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the kotlin generator cannot render.");
        }
    }

    private static string Quote(string value)
    {
        var literal = new StringBuilder("\"");

        foreach (var c in value ?? "")
        {
            if (c == '"')
                literal.Append("\\\"");
            else if (c == '\\')
                literal.Append(@"\\");
            else if (c == '$')
                // A dollar starts a template expression in a Kotlin string.
                literal.Append(@"\$");
            else if (c == '\n')
                literal.Append(@"\n");
            else if (c == '\r')
                literal.Append(@"\r");
            else if (c == '\t')
                literal.Append(@"\t");
            else if (c < 0x20)
                literal.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                literal.Append(c);
        }

        return literal.Append('"').ToString();
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// A property name.
    ///
    /// camelCase, and escaped with backticks when it lands on a keyword - which Kotlin
    /// accepts for exactly this, unlike Java, where the name has to change instead.
    /// </summary>
    private string KotlinName(string name) => LanguageProfile.Kotlin.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for a name that is not a member - the accessor's slot per table.
    /// </summary>
    /// <remarks>
    /// camelCase because that is how Kotlin writes an identifier, not because a member is
    /// spelled that way. Sharing one function let the two look like one rule.
    /// </remarks>
    private static string KotlinCamelName(string name) => LanguageProfile.Kotlin.MemberName(name.ToCamelCase());

    /// <summary>An enum constant, SCREAMING_SNAKE_CASE as Kotlin writes them.</summary>
    private static string ConstantName(string name) => name.ToUpperSnakeCase();

}
