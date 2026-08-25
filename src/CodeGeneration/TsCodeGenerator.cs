using Tabbit.Recipe;
using Tabbit.Models;
using Tabbit.Targets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;
using Tabbit.Extensions;
using Tabbit.Helpers;

// `using System` brings System.ValueType into scope, which collides with the
// model's own ValueType that this file refers to unqualified throughout.
using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.CodeGeneration;

/// <summary>
/// TypeScript modules. Read the JSON export.
/// </summary>
public class TypescriptRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Name of the generated accessor, which also names the file it lands in.
    ///
    /// The other generated types are files of their own beside it, named after
    /// themselves - a table, an enum and a constant set each get one.
    /// </summary>
    public string AccessorName { get; set; } = "Tables";

    /// <summary>
    /// Namespace to wrap the generated code in. Omitting it puts everything
    /// in the global namespace, where the names may collide with something.
    /// </summary>
    public string Namespace { get; set; } = "";

    /// <summary>
    /// Extension the generated reader expects on table files. Must match the
    /// binary export's FileExtension.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    ///
    /// It fetches the manifest and the changed data files over HTTP and keeps a
    /// local copy current, so a build can take new data without shipping a new
    /// one. Off by default: a project that ships its data alongside its code has
    /// no use for it, and a file nobody calls is a file to explain.
    /// </summary>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>
    /// Whether generated files this run did not write are removed from
    /// <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// On, because the output is a file per table: delete a table from the sheets
    /// and its file stays behind naming types nothing declares any more. Only
    /// files carrying this tool's own header are removed, so a directory holding
    /// your own source is safe.
    ///
    /// Turn it off if you edit the generated files, which is a decision worth a
    /// line in a recipe.
    /// </remarks>
    public bool Sweep { get; set; } = true;

    /// <summary>
    /// Which side this output is built for: "c", "s", or "cs"/blank for
    /// both. Entities and fields marked for the other side are left out.
    ///
    /// Declare the same side on the exporter and on the code generator
    /// that reads its files: the two must agree on the column set or the
    /// generated reader will not match the data.
    /// </summary>
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

    
    /// <summary>
    /// Emits enums as string unions rather than numeric enums.
    ///
    /// Readable in a debugger and in logs, at the cost of not matching the
    /// integers the exported data actually carries.
    /// </summary>
    public bool UseStringEnum { get; set; }
}

/// <summary>
/// Emits a TypeScript module per entity, plus a barrel index and the binary reader.
///
/// A module per entity rather than one file, unlike the C# and C++ generators:
/// TypeScript has a module system, so the imports between generated files are the
/// language's job rather than the reader's.
///
/// The shapes live in templates/ts-*.sbn. This file works out the values they need -
/// type names, read calls, the JSON conversions - and nothing else.
/// </summary>
[TabbitTarget("typescript", TargetKind.CodeGeneration, Order = 30)]
public class TsCodeGenerator : CodeGenerator<TypescriptRecipe>
{
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private TypescriptRecipe _typescriptRecipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Camel;

    /// <summary>
    /// A record group generates an element interface and a member of it, in both read
    /// paths.
    /// </summary>
    /// <remarks>
    /// The second of the thirteen, and the only one where records had to reach the JSON
    /// paths as well - it is the one language that reads JSON.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is an interface declared beside the element type, in both read paths.
    /// </summary>
    /// <remarks>
    /// Both, because this is the one language that reads JSON as well: the binary path
    /// assigns through a longer member path and the JSON path nests one more object. Neither
    /// counts the levels. spec/nested-multi-level.md.
    /// </remarks>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `has{Prop}` accessor beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `T | null`, for the reason in spec/optional-fields.md: one shape across thirteen
    /// languages beats each language's own, and the value accessor keeps its type.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`findByStageAndSlot(stageKey, slotKey)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// `hasXAt(i)` beside the value, filled from the element bitmap the file carries.
    /// spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, TypescriptRecipe typescriptRecipe)
    {
        // A blank path means the entry is inert, as it is in the skeleton recipe.
        // Without this the index and the reader land in the working directory - the
        // same defect the C# target had, and for the same reason: an empty first
        // component makes Path.Combine hand back a relative path rather than nothing.
        if (string.IsNullOrEmpty(typescriptRecipe.Path))
            return;

        SweepStaleOutput(typescriptRecipe.Path, typescriptRecipe.Sweep);

        _typescriptRecipe = typescriptRecipe;

        // Already narrowed to the side this entry is built for. Both (the default)
        // leaves the model unchanged.
        _model = context.Model;
        _memberCase = MemberCasing.From(typescriptRecipe.MemberCase, NameCase.Camel, "typescript");

        GenerateModel();
    }

    /// <summary>
    /// The accessor's file, extension left off - what an import path names.
    /// </summary>
    /// <remarks>
    /// Lower case, like every other file this target writes. The accessor used to be
    /// `tables.ts` written as a literal while the recipe carried an `AccessorName` nothing
    /// read, so a project setting it got neither the file nor the class it asked for.
    /// </remarks>
    private string AccessorFile => TsFileName(_typescriptRecipe.AccessorName);

    /// <summary>The accessor type's name, in the casing this language's types use.</summary>
    private string AccessorType => _typescriptRecipe.AccessorName.ToPascalCase();

    private void GenerateModel()
    {
        GenerateIndexTs();

        if (_model.Enums.Count > 0)
        {
            foreach (var enumm in _model.Enums)
                Write($"enums/{TsFileName(enumm.Name)}.ts", "ts-enum.sbn", BuildEnum(enumm));
        }

        // A struct is an entity beside a table and an enum, so it gets a module of its own -
        // one per declaration however many tables named it. Two tables each declaring their
        // own `Effect` would give them types that share a name and are not the same type.
        // spec/polymorphism.md section 7.1.
        foreach (var declared in _model.PolymorphicTypes)
        {
            Write($"structs/{TsFileName(declared.Name)}.ts", "ts-struct.sbn",
                  BuildStruct(declared));
        }

        if (_model.Tables.Count > 0)
        {
            foreach (var table in _model.Tables)
                Write($"tables/{TsFileName(table.Name)}.ts", "ts-table.sbn", BuildTable(table));

            Write($"{AccessorFile}.ts", "ts-tables-set.sbn", new TsTableSetView
            {
                AccessorName = AccessorType,
                AccessorFile = AccessorFile,
                Tables = _model.Tables.Select(table => new TsTableSlotView
                {
                    Member = TsCamelName(table.Name),
                    DataFileName = table.DataFileName,
                    Local = TsLocalName(table.Name),
                    Name = table.Name,
                    File = TsFileName(table.Name),
                }).ToList(),

                BinaryFileExtension = _typescriptRecipe.BinaryTableFileExtension,
                CrossReferences = BuildCrossReferences(),

                // Both kinds: a plain column reaching several tables and a record member
                Imports = System.Linq.Enumerable.Empty<string>()
                                .ToList(),
            });

        }

        if (_model.ConstantSets.Count > 0)
        {
            foreach (var constantSet in _model.ConstantSets)
                Write($"constants/{TsFileName(constantSet.Name)}.ts", "ts-constants.sbn", BuildConstantSet(constantSet));
        }

        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// One abstract type and its variants, as the template reads them.
    /// </summary>
    /// <remarks>
    /// The members are columns, so their types come out of the same conversion a table's
    /// members do. spec/polymorphism.md section 7.1.
    /// </remarks>
    /// <summary>The imports one abstract type's module needs for the types its members name.</summary>
    private IReadOnlyList<string> StructImports(Models.PolymorphicType declared)
    {
        var lines = new List<string>();

        foreach (var field in declared.BaseMembers
                     .Concat(declared.Variants.SelectMany(variant => variant.Members)))
        {
            string line = field.ElementType switch
            {
                ValueType.Enum =>
                    $"import {{ {field.Enum.Name.ToPascalCase()} }} "
                    + $"from '../enums/{TsFileName(field.Enum.Name)}'",
                ValueType.ForeignRecord when field.ResolvedRefTable is not null =>
                    $"import {{ {field.ResolvedRefTable.Name.ToPascalCase()}Record }} "
                    + $"from '../tables/{TsFileName(field.ResolvedRefTable.Name)}'",
                _ => "",
            };

            if (line.Length > 0 && !lines.Contains(line))
                lines.Add(line);
        }

        return lines;
    }

    private TsPolymorphicTypeView BuildStruct(Models.PolymorphicType declared)
        => new TsPolymorphicTypeView
        {
            Name = declared.Name,
            File = TsFileName(declared.Name),
            Imports = StructImports(declared),
            BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
            Variants = declared.Variants
                .Select(variant => new TsVariantView
                {
                    TypeName = variant.Name,
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),
        };

    /// <summary>One member of an abstract type or of one of its variants.</summary>
    /// <remarks>
    /// **A reference member is two properties**, as a reference is anywhere: the declared name
    /// is the key's and the row it resolves to takes the derived one.
    /// spec/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    private TsStructMemberView StructMember(Models.Field field)
    {
        string raw = field.NamePath is { Count: > 1 } ? field.NamePath[^1].Name : field.Name;
        bool toRow = field.IsRef && field.ResolvedRefTable is not null && ResolvesToRow(field);

        return new TsStructMemberView
        {
            RowName = toRow
                ? TsCamelName(RowAccessorName(field.ResolvedRefTable!.Name, raw))
                : "",
            KeyTypeName = field.IsRef
                ? ToTypescriptTypename(field.RefKeyType, null, null)
                : "",
            PropName = TsCamelName(raw),
            // `Enum` refuses a field that is not one, so it is only asked where the type says
            // to ask - the same guard every other caller of this makes.
            FieldType = ToTypescriptTypename(
                field.Type,
                field.Type is ValueType.Enum or ValueType.EnumArray ? field.Enum : null,
                field.RefTableName,
                asArray: Models.ValueTypes.IsArray(field.Type)),
            Comment = CommentLines(field.Comment),
        };
    }

    private void GenerateIndexTs()
    {
        string ns = _typescriptRecipe.Namespace;

        Write("index.ts", "ts-index.sbn", new TsIndexView
        {
            AccessorName = AccessorType,
            AccessorFile = AccessorFile,
            NamespaceOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns}\n{{",
            NamespaceClose = string.IsNullOrEmpty(ns) ? "" : "}",
            Enums = _model.Enums.Select(Exported).ToList(),
            Tables = _model.Tables.Select(x => Exported(x.Name)).ToList(),
            ConstantSets = _model.ConstantSets.Select(Exported).ToList(),
        });
    }

    /// <summary>
    /// Writes the Tcb reader into the output, beside the generated modules.
    ///
    /// Emitted rather than left for the consumer to copy: the generated tables import
    /// it by a relative path, and TypeScript has no include-path setting that would
    /// let a project point somewhere else. Shipping it makes the output directory
    /// self-contained.
    ///
    /// The source is an embedded resource taken from lib/ts, so there is one copy to
    /// maintain and it cannot drift from what is shipped.
    /// </summary>
    /// <summary>
    /// The file a type of this name lives in: lower kebab-case, so `TableData` is
    /// `table-data.ts`.
    /// </summary>
    /// <remarks>
    /// What TypeScript projects write, and the spelling that survives a case-insensitive
    /// filesystem handing its output to a case-sensitive one - a `Tables.ts` imported as
    /// `./tables` builds on a laptop and not on the server that deploys it.
    /// </remarks>
    private static TsExportView Exported(Models.Enum enumm) => Exported(enumm.Name);
    private static TsExportView Exported(ConstantSet set) => Exported(set.Name);

    private static TsExportView Exported(string name)
        => new TsExportView { Name = name, File = TsFileName(name) };

    private static string TsFileName(string name) => name.ToKebabCase().ToLowerInvariant();

    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Ts.tcb_reader.ts",
            GetTsFilename("tabbit/tcb-reader.ts"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // project that ships its data with its code.
        if (_typescriptRecipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Ts.updater.ts",
                GetTsFilename("tabbit/updater.ts"));
        }
    }

    // --------------------------------------------------------------- view

    private TsEnumView BuildEnum(Models.Enum enumm) => new TsEnumView
    {
        Name = enumm.Name,
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select((label, index) => new TsEnumLabelView
        {
            Name = label.Name,

            // A string enum reads better in a debugger and survives a JSON round trip
            // as itself; a numeric one matches what the binary carries.
            Value = _typescriptRecipe.UseStringEnum
                ? $"'{label.Name}'"
                : label.Value.ToString(CultureInfo.InvariantCulture),

            Comment = CommentLines(label.Comment),
            IsLast = index == enumm.Labels.Count - 1,
        }).ToList(),
    };

    private TsConstantSetView BuildConstantSet(ConstantSet constantSet) => new TsConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),

        Imports = constantSet.Constants
                             .Where(c => c.Type == ValueType.Enum)
                             .Select(c => $"import {{ {c.Enum.Name} }} from '../enums/{TsFileName(c.Enum.Name)}'")
                             .Distinct()
                             .ToList(),

        Constants = constantSet.Constants.Select(constant => new TsConstantView
        {
            Name = TsCamelName(constant.Name),
            Type = ToTypescriptTypename(constant.Type, constant.Enum, null),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    /// <summary>
    /// Every reference column in the model, grouped by the table that holds it.
    /// </summary>
    /// <remarks>
    /// A reference is stored as the target row's primary key and turned into the row
    /// itself once every table is in memory - the same two-step every other target
    /// does. The key stays beside the resolved value, because a consumer sometimes
    /// wants the number and because a zero means "points at nothing".
    /// </remarks>
    private IReadOnlyList<TsCrossReferenceView> BuildCrossReferences()
        => _model.Tables
                 .Select(table => new
                 {
                     Table = table,
                     Fields = table.SerialFields.Where(sf => sf.IsRef).ToList(),

                     // A reference that is a member of a record resolves inside the element
                     // rather than beside it, so it is a loop of its own. Read off the wire
                     // columns, which is the same list the read path walks - the two have to
                     // agree about where the key landed. spec/references-in-records.md.
                     RecordFields = table.WireColumns
                                         .Where(wire => wire.Member is not null && wire.IsRef)
                                         .Select(BuildRecordReference)
                                         .ToList(),


                 })
                 .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                              )
                 .Select(x => new TsCrossReferenceView
                 {
                     Table = TsName(x.Table.Name),
                     Fields = x.Fields.Select(BuildReferenceField).ToList(),
                     RecordFields = x.RecordFields,
                 })
                 .ToList();

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// Whole expressions rather than the parts to build them from: which of the three record
    /// shapes this is decides where the element number sits - on the group, on the member, or
    /// nowhere - and the template should not be the place that knows.
    ///
    /// Written straight into the member rather than through a `setReference_` method. Those
    /// exist because a table's own members are private to it; an element of a record group is
    /// a plain object, and there is nothing to go around. spec/references-in-records.md.
    /// </remarks>
    private TsRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string field = "_" + TsName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(name => "." + TsName(name)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsArray;

        // Where the element number goes is the whole difference between the record shapes -
        // the group's array, the member's, or neither. spec/nested-multi-level.md.
        string rowLeaf = wire.Member is not null
            ? TsName(RowAccessorName(refTable!.Name, wire.MemberPath[^1]))
            : TsName(RowAccessorName(refTable!.Name, wire.Group.Name));

        string rowMember = wire.Member is not null
            ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                .Select(part => "." + TsName(part))) + "." + rowLeaf
            : "";

        string rowPath = wire.Member is not null
            ? (!isArray || wire.Group.MembersAreArrays
                ? $"record.{field}{rowMember}"
                : $"record.{field}[i]{rowMember}")
            : $"record.{field}";

        string path = !isArray || wire.Group.MembersAreArrays
            ? $"record.{field}{member}"
            : $"record.{field}[i]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[i]" : "";

        return new TsRecordReferenceView
        {
            Access = rowPath + subscript,
            Key = path + subscript,
            Flag = path + "_F" + subscript,

            // Whichever array holds the elements. `length` rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Count = isArray
                ? (wire.Group.MembersAreArrays ? $"{path}.length" : $"record.{field}.length")
                : "",

            RefTable = TsName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
            RefIsSet = RefIsSetSuffix(wire.RefKeyType),
        };
    }

    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// Zero is the convention for "points at nothing" and its spelling depends on the key:
    /// a 64-bit key is a `bigint` and compares against `0n`, and a string has no zero at all.
    /// The numeric case is `> 0`, which is what the template used to say for every key.
    /// spec/reference-optionality.md · spec/reference-key-types.md.
    /// </remarks>
    private static string RefIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.Int64 => "> 0n",
            ValueType.String or ValueType.Uuid => "!== \"\"",
            _ => "> 0",
        };

    /// <summary>The value a stored key's member holds before a row is read.</summary>
    private static string RefKeyInitial(ValueType keyType)
        => keyType switch
        {
            ValueType.Int64 => "0n",
            ValueType.String or ValueType.Uuid => "\"\"",
            _ => "0",
        };

    private TsReferenceFieldView BuildReferenceField(SerialField sf)
    {
        var refTable = sf.FirstField!.ResolvedRefTable;

        return new TsReferenceFieldView
        {
            PropName = TsName(sf.Name),
            FieldName = "_" + TsName(sf.Name),
            RefTable = TsName(refTable!.Name),

            // The declared name, not the resolved one, because that is what the record's
            // key member is named after and the two have to spell it the same way.
            RefTableType = sf.FirstField!.RefTableName.ToPascalCase() ?? "",
            RefLookup = PrimaryLookup(refTable),

            // A reference to a whole row yields the row; one that names a field yields
            // that field's value.
            Value = sf.ElementType == ValueType.ForeignRecord
                ? "target"
                : "target." + TsName(sf.FirstField!.ResolvedRefField!.Name),

            IsArray = sf.IsArray,
            ElementCount = sf.Fields.Count,
            RefIsSet = RefIsSetSuffix(sf.FirstField!.RefKeyType),
        };
    }

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `getByIndexOrThrow`. The
    /// primary index is whatever the sheet put in the first column - its type is checked
    /// to be `int`, but its name is not.
    /// </remarks>
    private static string PrimaryLookup(Models.Table refTable)
        => "getBy" + refTable.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase() + "OrThrow";

    /// <summary>
    /// The lookup that answers with undefined rather than throwing.
    /// </summary>
    /// <remarks>
    /// What a multi-target column resolves through. A key absent from one of its targets is
    /// the ordinary case - the row is in another of them - so the miss has to be an answer.
    /// spec/multi-target-accessors.md.
    /// </remarks>
    private static string PrimaryFind(Models.Table refTable)
        => "findBy" + refTable.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase();



    private TsTableView BuildTable(Models.Table table)
    {
        var fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList();

        return new TsTableView
        {
            AccessorName = AccessorType,
            AccessorFile = AccessorFile,
            Name = table.Name,
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            Imports = BuildImports(table),
            Fields = fields,
            Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),

            IndexedFields = table.SerialFields
                                 .Select((sf, i) => new { sf, view = fields[i] })
                                 .Where(x => x.sf.IsIndexer)
                                 .Select(x => x.view)
                                 .ToList(),

            CompositeKeys = CompositeKeys(table),

            ReferenceFields = table.SerialFields
                                   .Select((sf, i) => new { sf, view = fields[i] })
                                   .Where(x => x.sf.IsRef)
                                   .Select(x => x.view)
                                   .ToList(),


            // One cursor variable for the whole method: switch cases share a scope in
            // JavaScript too, so each encodable column assigns it rather than declaring
            // its own.
            NeedsCursor = table.WireColumns.Any(UsesCursor),
            NeedsPresence = table.WireColumns.Any(c => c.IsNullable),
            NeedsElementPresence = table.WireColumns.Any(c => c.HasOptionalElements),
        };
    }

    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where its value
    /// lands.
    /// </summary>
    private TsColumnView BuildColumn(Models.Table table, WireColumn wire)
    {
        // A record's member column assigns one field of the element rather than the member
        // itself, which is the whole of what makes it different to read.
        string memberAccess = (wire.Member is null)
            ? ""
            : string.Concat(wire.MemberPath.Select(name => "." + TsName(name)));

        return new TsColumnView
        {
            WireTag = wire.TagCarrier.WireTag!.Value,
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire) ? "cursor.nextLength()" : "reader.readCounter32()",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Kind = ColumnKind(wire),
            BinaryRead = BinaryReadExpression(wire),
            FieldName = "_" + TsName(wire.Group.Name),
            MemberAccess = memberAccess,
            MemberAt = wire.MemberAt,

            // A reference member reads into the key beside the row it will resolve to, and the
            // suffix goes on the member rather than after the subscript - `itemId_index[j]`
            // rather than `itemId[j]_index`. spec/references-in-records.md.
            MemberRefSuffix = "",
            ElementCount = wire.Cells.Count,
            RefTable = wire.TagCarrier.RefTableName.ToPascalCase() ?? "",
            IsFirstMember = wire.IsFirstMember,
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceField = "_" + TsName(wire.Group.Name) + "HasValue",
            ElementPresenceField = "_" + TsName(wire.Group.Name) + "HasValueAt",
            EmptyValue = wire.Group.IsArray ? "[]" : DefaultValue(wire.Group),
            QualifiedGroupName = $"{table.Name.ToPascalCase()}.{wire.Group.Name.ToPascalCase()}",

            // Built from the group rather than passed down, because a member column needs the
            // whole element to create one and it only knows its own member.
            //
            // Through the same builder the declaration uses, so the literal cannot end up
            // giving fewer properties than the interface asks for - which is what a reference
            // member costs, since it is three properties rather than one.
            RecordLiteral = wire.Group.IsRecord
                ? RecordLiteral(BuildRecordMembers(
                    wire.Group.Members, wire.Group.Name.ToPascalCase(), new List<TsRecordTypeView>()))
                : "",
        };
    }

    /// <summary>
    /// Which read shape a column takes, which is the field's declaration kind for
    /// everything that is not part of a record.
    /// </summary>
    private static string ColumnKind(WireColumn wire)
    {
        if (wire.Member is not null)
        {
            if (!wire.IsArray)
                return "record_member";

            // Which of the two owns the array decides where the index goes.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays
                ? "record_member_array"
                : "record_var_array_member";
        }

        // A trimmed array of references: the length is the row's, and the keys still arrive in
        // the array beside the values. Read as a plain `var_array` it pushed a number into the
        // array of rows, which `tsc` refuses - and nothing held the shape, because `foreign[]`
        // is refused and this is only reachable through a folded group with trimming on.
        // spec/variable-length-record-arrays.md.
        if (wire.IsArray)
            return wire.IsRef ? "var_array_ref" : "var_array";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    /// <summary>
    /// The imports this module needs for the types it names.
    ///
    /// The record branch used to be missing, so a module referring to another table's
    /// record named a type it never pulled in and did not compile - the enum branch had
    /// always been the only one.
    /// </summary>
    private IReadOnlyList<string> BuildImports(Models.Table table)
    {
        var imports = new List<string>();

        // Asked of the columns rather than of the groups, because a record group has no
        // element type of its own - what needs importing is named by a leaf, however deep it
        // sits. Reading the group alone left a record member's target unimported, and the
        // generated module then named a type nothing had brought in.
        // spec/references-in-records.md.
        foreach (var field in table.SerialFields.SelectMany(sf => sf.IsRecord
                                                                ? sf.Leaves.Select(leaf => leaf.FirstField)
                                                                : new[] { sf.FirstField }))
        {
            if (field is null)
                continue;

            if (field!.ElementType == ValueType.Enum)
            {
                Add($"import {{ {field.Enum.Name} }} from '../enums/{TsFileName(field.Enum.Name)}'");
            }
            else if (field!.ElementType == ValueType.ForeignRecord)
            {
                // Resolved rather than declared table name: the declared one is the raw
                // detail-type text, while resolution has already followed the reference
                // chain to the table actually being pointed at.
                var refTable = field.ResolvedRefTable;

                if (refTable is not null && refTable.Name != table.Name)
                    Add($"import {{ {refTable.Name.ToPascalCase()}Record }} from './{TsFileName(refTable.Name)}'");
            }
        }

        // The abstract types this table's groups are. Declared in a module of their own -
        // one per declaration however many tables named it - so the table brings the union
        // in rather than declaring its own. spec/polymorphism.md section 7.1.
        foreach (var declared in table.Fields
                     .Where(field => field.IsDiscriminator && field.AbstractTypeName is not null)
                     .Select(field => field.AbstractTypeName!.ToPascalCase())
                     .Distinct())
        {
            Add($"import {{ {declared} }} from '../structs/{TsFileName(declared)}'");
        }

        return imports;

        void Add(string statement)
        {
            if (!imports.Contains(statement))
                imports.Add(statement);
        }
    }

    /// <summary>
    /// The keys made of several columns, each with the lookup it generates.
    /// </summary>
    /// <remarks>
    /// Beside <c>IndexedFields</c> rather than folded into it: a single key publishes its map
    /// and a composite one does not, and a table that declares none generates what it
    /// generated before this notation existed. See <see cref="CompositeKeyView"/>.
    /// </remarks>
    private IReadOnlyList<CompositeKeyView> CompositeKeys(Table table)
        => KeyPlans.Of(table).Where(plan => plan.IsComposite).Select(plan =>
        {
            string suffix = plan.Suffix(name => name.ToPascalCase(), "And");

            var components = plan.Components.Select(component => new KeyComponentView
            {
                Param = KeyComponentView.ParamOf(component.Name).ToCamelCase(),
                Type = ToTypescriptTypename(component.FirstField),
                Member = TsName(component.Name),
                Kind = KeyComponentView.KindOf(component.FirstField!.ElementType),
            }).ToList();

            return new CompositeKeyView
            {
                Suffix = suffix,
                MapName = "_recordsBy" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                Components = components,

                Params = string.Join(", ", components.Select(c => c.Param + ": " + c.Type)),

                Argument = "keyOf" + suffix + "("
                           + string.Join(", ", components.Select(c => c.Param)) + ")",

                ValueFormat = "(" + string.Join(
                    ", ", components.Select(c => "${" + c.Param + "}")) + ")",
            };
        }).ToList();

    private TsFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string prop = TsName(sf.Name);
        string field = "_" + prop;
        string fieldType = ToTypescriptTypename(sf.FirstField);

        return new TsFieldView
        {
            RowPropName = sf.IsRef && sf.FirstField!.ResolvedRefTable is not null
                           && sf.FirstField!.ResolvedRefField is null
                ? TsName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                : "",

            IsRecord = false,
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceField = "_" + prop + "HasValue",
            ElementPresenceField = "_" + prop + "HasValueAt",
            RecordTypeName = "",
            Members = Array.Empty<TsRecordMemberView>(),
            Comment = CommentLines(sf.FirstField!.Comment),
            PropName = prop,
            FieldName = field,
            PascalName = sf.Name.ToPascalCase(),
            DefaultValue = DefaultValue(sf),
            FieldType = fieldType,
            JsonWireType = JsonWireTypeOf(sf),
            ElementCount = sf.Fields.Count,
            RefTable = sf.FirstField!.RefTableName.ToPascalCase() ?? "",
            RefKeyTypeName = ToTypescriptTypename(sf.FirstField!.RefKeyType, null, null),
            RefKeyInitial = RefKeyInitial(sf.FirstField!.RefKeyType),
            Kind = DeclarationKind(table, sf),
            IsArray = sf.IsArray,

            ReferenceSetterType = sf.ElementType == ValueType.ForeignRecord
                ? sf.FirstField!.RefTableName.ToPascalCase() + "Record"
                : fieldType,

            ReferenceIsRecord = sf.ElementType == ValueType.ForeignRecord,

            FromNamedRow = NamedRowAssignment(sf, field, prop),
            FromCompactRow = CompactRowStatements(sf, field, prop),

        };
    }

    /// <summary>
    /// A record group: the element interface to declare, and the member holding one or an
    /// array of them.
    /// </summary>
    /// <remarks>
    /// An interface and object literals rather than a class, because that is what the JSON
    /// paths produce anyway - a compact row is zipped into literals and a named row's
    /// entries already are them - and nothing about a record needs a prototype.
    /// </remarks>
    private TsFieldView BuildRecordField(Table table, SerialField sf)
    {
        string prop = TsName(sf.Name);
        string field = "_" + prop;
        string typeName = sf.Name.ToPascalCase() + "Entry";

        // Trimmed, so the length is this row's: nothing to declare filled and no `_N` to
        // expose, because there is no one count.
        bool perRowLength = sf.IsArray && table.TrimTrailingArrayElements;

        // Innermost first, so an interface is declared before the one naming it - which
        // TypeScript does not require but a reader does.
        var recordTypes = new List<TsRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, sf.Name.ToPascalCase(), recordTypes);

        recordTypes.Add(new TsRecordTypeView
        {
            TypeName = typeName,
            Members = members,
            IsOutermost = true,
        });

        // Which abstract type this group is, if it is one. One per declaration however many
        // tables named it, so this looks the shared entry up rather than working it out again.
        // spec/polymorphism.md section 7.1.
        var declaredType = sf.Members
            .FirstOrDefault(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
            ?.FirstField?.AbstractTypeName is { } abstractName
                ? _model.PolymorphicTypes.FirstOrDefault(
                    candidate => candidate.Name == abstractName.ToPascalCase())
                : null;

        return new TsFieldView
        {
            RowPropName = sf.IsRef && sf.FirstField!.ResolvedRefTable is not null
                           && sf.FirstField!.ResolvedRefField is null
                ? TsName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                : "",

            IsRecord = true,

            // A record group has no presence of its own: absence inside one is the array's
            // length, not a bit per member.
            IsNullable = false,
            PresenceField = "",
            RecordTypeName = typeName,
            Members = members,
            RecordTypes = recordTypes,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            OuterCount = sf.Members.Count,

            // A record has no header cell of its own, so the first member's column comment
            // is the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),
            PropName = prop,
            FieldName = field,
            PascalName = sf.Name.ToPascalCase(),

            // An array of arrays has no element type to name, so the inner array is the
            // type - see spec/nested-multi-level.md.
            AbstractTypeName = declaredType?.Name ?? "",
            AbstractTypeFile = declaredType is null ? "" : TsFileName(declaredType.Name),
            BaseMembers = (declaredType?.BaseMembers ?? []).Select(StructMember).ToList(),
            Variants = (declaredType?.Variants ?? [])
                .Select(variant => new TsVariantView
                {
                    TypeName = variant.Name,
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),

            FieldType = sf.MembersAreAnonymous
                ? ToTypescriptTypename(sf.Members[0].FirstField) + "[]"
                : typeName,
            // An array of records is declared already filled, because each member column
            // fills a property of an element that has to be there - and a member whose
            // column the file does not carry then holds its empty value rather than
            // undefined, which is the guarantee every other member of a record has.
            //
            // A trimmed one starts empty instead: no declaration could know how long this
            // row's array is, so the read creates the elements it turns out to need.
            DefaultValue = sf.MembersAreAnonymous
                ? $"Array.from({{ length: {sf.Members.Count} }}, () => [])"
                : sf.IsArray
                    ? "[]"
                    : RecordLiteral(members),

            // The JSON shape gets an interface of its own, because a member's exported
            // type is not always its member type - a 64-bit integer arrives as a string.
            JsonWireType = sf.MembersAreAnonymous
                ? JsonWireTypeOfMember(sf.Members[0]) + "[][]"
                : typeName + "Json",
            ElementCount = sf.RecordElementCount,
            Kind = sf.MembersAreAnonymous
                ? "array_of_arrays"
                : sf.IsArray ? "record_var_array" : "record",
            IsArray = sf.IsArray,

            FromNamedRow = RecordNamedRowAssignment(sf, members, field, prop),
            FromCompactRow = RecordCompactRowStatements(sf, members, field, perRowLength),

            // None of these apply: a reference belongs to a member, and a member cannot be
            // one yet - the model refuses it.
            RefTable = "",
            RefKeyTypeName = "",
            RefKeyInitial = "",
            ReferenceSetterType = "",
            ReferenceIsRecord = false,
        };
    }

    /// <summary>
    /// Members of one level of a record, declaring an interface for each member that is
    /// itself a record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the interfaces it produces. A member that is a record differs from one that is
    /// a value only in the three strings below - its type, its exported type, and its empty
    /// value - so depth costs nothing past this method. spec/nested-multi-level.md.
    /// </remarks>
    private List<TsRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, List<TsRecordTypeView> declared)
    {
        var result = new List<TsRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                // A reference member holds the row it resolved to, and the key and the flag
                // beside it. The row starts undefined, which is what a reference into a row
                // that is not there stays; the JSON carries only the key, so the exported
                // type is the key's. spec/references-in-records.md.
                bool isRef = member.IsRef;
                string keyType = isRef
                    ? ToTypescriptTypename(member.FirstField!.RefKeyType, null, null)
                    : "";
                string keyDefault = isRef ? RefKeyInitial(member.FirstField!.RefKeyType) : "";

                // The row it resolves to, which is absent until the linking pass runs and
                // stays absent where the key points at nothing.
                string rowType = isRef
                    ? (member.IsArray
                        ? $"({ToTypescriptTypename(member.FirstField)} | undefined)[]"
                        : $"{ToTypescriptTypename(member.FirstField)} | undefined")
                    : ToTypescriptTypename(member.FirstField) + (member.IsArray ? "[]" : "");

                result.Add(new TsRecordMemberView
                {
            RowPropName = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                           && ResolvesToRow(member.FirstField!)
                ? TsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                : "",

                    Comment = CommentLines(member.FirstField!.Comment),
                    PropName = TsName(member.Name),

                    // The array is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    FieldType = rowType,
                    JsonWireType = (isRef ? JsonWireTypeOfKey(member.FirstField!.RefKeyType)
                                          : JsonWireTypeOfMember(member))
                                   + (member.IsArray ? "[]" : ""),
                    DefaultValue = isRef
                        ? (member.IsArray
                            ? "[" + string.Join(", ",
                                Enumerable.Repeat("undefined", member.Fields.Count)) + "]"
                            : "undefined")
                        : member.IsArray
                            ? "[" + string.Join(", ",
                                Enumerable.Repeat(DefaultValueOf(member.ElementType, member.FirstField),
                                                  member.Fields.Count)) + "]"
                            : DefaultValueOf(member.ElementType, member.FirstField),

                    RefKeyTypeName = isRef ? keyType + (member.IsArray ? "[]" : "") : "",
                    RefFlagTypeName = isRef ? "boolean" + (member.IsArray ? "[]" : "") : "",
                    RefKeyDefault = isRef
                        ? (member.IsArray
                            ? "[" + string.Join(", ", Enumerable.Repeat(keyDefault, member.Fields.Count)) + "]"
                            : keyDefault)
                        : "",
                    RefFlagDefault = isRef
                        ? (member.IsArray
                            ? "[" + string.Join(", ", Enumerable.Repeat("false", member.Fields.Count)) + "]"
                            : "false")
                        : "",
                });

                continue;
            }

            // A level below. The type name carries the path so two records each holding a
            // `Position` do not name one interface twice.
            string typeName = prefix + member.Name.ToPascalCase() + "Entry";
            var nested = BuildRecordMembers(member.Members, prefix + member.Name.ToPascalCase(), declared);

            declared.Add(new TsRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
            });

            result.Add(new TsRecordMemberView
            {
            RowPropName = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                           && ResolvesToRow(member.FirstField!)
                ? TsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                : "",

                Comment = CommentLines(member.FirstField!.Comment),
                PropName = TsName(member.Name),
                FieldType = typeName,
                JsonWireType = typeName + "Json",

                // The level below's own literal, which is how a record reaches its members'
                // empty values without a constructor.
                DefaultValue = RecordLiteral(nested),
                IsRecord = true,
            });
        }

        return result;
    }

    /// <summary>An object literal of every member's empty value.</summary>
    /// <remarks>
    /// A reference member contributes three entries rather than one - the row, the key and
    /// the flag - because all three are properties of the element and a literal has to give
    /// every property of the interface it satisfies. spec/references-in-records.md.
    /// </remarks>
    private static string RecordLiteral(IReadOnlyList<TsRecordMemberView> members)
        => "{ " + string.Join(", ", members.SelectMany(MemberLiteralParts)) + " }";

    /// <summary>What one member contributes to that literal.</summary>
    /// <remarks>
    /// A reference member gives three properties and one reaching several tables gives three
    /// as well - the key, the slot and the discriminator - because a literal has to give every
    /// property of the interface it satisfies. spec/multi-target-accessors.md.
    /// </remarks>
    private static IEnumerable<string> MemberLiteralParts(TsRecordMemberView member)
    {
        // The member's own name is the key's, where it is a reference; the row is under the
        // derived name. spec/reference-surface-naming.md sections 4 and 5.
        yield return member.RowPropName.Length > 0
            ? $"{member.RowPropName}: {member.DefaultValue}"
            : $"{member.PropName}: {member.DefaultValue}";

        if (member.RefKeyTypeName.Length > 0)
        {
            yield return $"{member.PropName}: {member.RefKeyDefault}";
            yield return $"{member.PropName}_F: {member.RefFlagDefault}";
        }

    }

    /// <summary>
    /// The assignment reading a record group out of a named JSON row.
    /// </summary>
    /// <remarks>
    /// The JSON carries a record as an object and an array of records as an array of them,
    /// so this rebuilds the literal member by member rather than assigning through. It has
    /// to: a member whose value needs converting on the way in from JSON - a 64-bit
    /// integer arrives as a string - would otherwise land as whatever the file held.
    /// </remarks>
    private string RecordNamedRowAssignment(
        SerialField sf, IReadOnlyList<TsRecordMemberView> members, string field, string prop)
    {
        // No member names, so the outer level is an array in the JSON too.
        if (sf.MembersAreAnonymous)
        {
            string each = FromJsonExpressionOf(sf.Members[0].ElementType, "v");
            return $"this.{field} = dataRow.{prop}.map((inner: any) => inner.map((v: any) => {each}))";
        }

        string literal = NamedRowLiteral(sf.Members, "e");

        return sf.IsArray
            ? $"this.{field} = dataRow.{prop}.map(e => ({literal}))"
            : $"this.{field} = ((e: any) => ({literal}))(dataRow.{prop})";
    }


    private string NamedRowLiteral(List<RecordMember> members, string accessor)
    {
        var parts = members.SelectMany(member =>
        {
            string prop = TsName(member.Name);
            string rowProp = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                ? TsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                : prop;

            if (!member.IsLeaf)
                return new[] { $"{prop}: {NamedRowLiteral(member.Members, $"{accessor}.{prop}")}" };

            // A reference member: the JSON holds the key under the member's own name, and the
            // row it names is filled in by the linking pass. So the property the JSON matches
            // is the key, and the row starts absent exactly as the binary path leaves it.
            // spec/references-in-records.md.
            if (member.IsRef)
            {
                string key = FromJsonExpressionOf(member.FirstField!.RefKeyType, "v");

                return member.IsArray
                    ? new[]
                    {
                        $"{rowProp}: {accessor}.{prop}.map(() => undefined)",
                        $"{prop}: {accessor}.{prop}.map((v: any) => {key})",
                        $"{prop}_F: {accessor}.{prop}.map(() => false)",
                    }
                    : new[]
                    {
                        $"{rowProp}: undefined",
                        $"{prop}: "
                            + FromJsonExpressionOf(member.FirstField!.RefKeyType, $"{accessor}.{prop}"),
                        $"{prop}_F: false",
                    };
            }

            string element = FromJsonExpressionOf(member.ElementType, "v");


            return new[]
            {
                member.IsArray
                    ? $"{prop}: {accessor}.{prop}.map((v: any) => {element})"
                    : $"{prop}: {FromJsonExpressionOf(member.ElementType, $"{accessor}.{prop}")}",
            };
        });

        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>
    /// The statements reading a record group out of a compact JSON row.
    /// </summary>
    /// <remarks>
    /// A compact row holds one entry per cell in wire-column order, so each member's
    /// entries are adjacent and can be taken with a slice - then zipped into the objects.
    /// That adjacency is the whole reason the exporter emits wire-column order: a member's
    /// columns are never adjacent in sheet order, because members interleave.
    ///
    /// A trimmed group is the exception: there each member is one nested entry holding that
    /// row's elements, because a positional format cannot be walked past a count nobody
    /// wrote. So the slice becomes a single read and the length comes from the array itself.
    /// </remarks>
    private IReadOnlyList<string> RecordCompactRowStatements(
        SerialField sf, IReadOnlyList<TsRecordMemberView> members, string field, bool perRowLength)
    {
        var lines = new List<string>();

        if (perRowLength)
        {
            for (int at = 0; at < sf.Members.Count; at++)
                lines.Add($"const {field}_{members[at].PropName} = dataRow[offset++] as any[]");

            string zip = "{ " + string.Join(", ", sf.Members.SelectMany((member, at) =>
            {
                string prop = members[at].PropName;
                string rowProp = members[at].RowPropName.Length > 0 ? members[at].RowPropName : prop;
                string source = $"{field}_{prop}[k]";

                // A reference member: the entry is the key, and the row it names is filled in
                // by the linking pass. spec/references-in-records.md.
                return member.IsRef
                    ? new[]
                    {
                        $"{rowProp}: undefined",
                        $"{prop}: {FromJsonExpressionOf(member.FirstField!.RefKeyType, source)}",
                        $"{prop}_F: false",
                    }
                    : new[] { $"{prop}: {FromJsonExpressionOf(member.ElementType, source)}" };
            })) + " }";

            // The first member's length. Every member carries the same one, and the exporter
            // is what makes that true.
            lines.Add($"this.{field} = Array.from(" +
                      $"{{ length: {field}_{members[0].PropName}.length }}, (_, k) => ({zip}))");

            return lines;
        }

        if (sf.MembersAreAnonymous)
        {
            // Each inner array is its own adjacent run, and the outer level is an array too.
            string each = FromJsonExpressionOf(sf.Members[0].ElementType, "v");

            var inners = sf.Members.Select((member, at) =>
                $"dataRow.slice(offset + {at * sf.RecordElementCount}, "
                + $"offset + {(at + 1) * sf.RecordElementCount}).map((v: any) => {each})");

            lines.Add($"this.{field} = [{string.Join(", ", inners)}]");
            lines.Add($"offset += {sf.Members.Count * sf.RecordElementCount}");
            return lines;
        }

        if (sf.MembersAreArrays)
        {
            // One record, and each member's elements are its own adjacent run - so a slice
            // per member, and no zipping: the members are not interleaved into elements.
            var taken = sf.Members.SelectMany((member, at) =>
            {
                string prop = members[at].PropName;
                string rowProp = members[at].RowPropName.Length > 0 ? members[at].RowPropName : prop;
                string slice = $"dataRow.slice(offset + {at * sf.RecordElementCount}, "
                             + $"offset + {(at + 1) * sf.RecordElementCount})";

                // A reference member: the run holds the keys, and the rows they name are
                // filled in by the linking pass. spec/references-in-records.md.
                if (member.IsRef)
                {
                    string key = FromJsonExpressionOf(member.FirstField!.RefKeyType, "v");

                    return new[]
                    {
                        $"{rowProp}: {slice}.map(() => undefined)",
                        $"{prop}: {slice}.map((v: any) => {key})",
                        $"{prop}_F: {slice}.map(() => false)",
                    };
                }

                string element = FromJsonExpressionOf(member.ElementType, "v");

                // A member reaching several tables: the run holds its keys, and the slot and
                // the discriminator are what the linking fills - one of each per element, so
                // they are sized from the same run. spec/multi-target-accessors.md.
                var field2 = member.FirstField;


                return new[] { $"{prop}: {slice}.map((v: any) => {element})" };
            });

            lines.Add($"this.{field} = {{ {string.Join(", ", taken)} }}");
            lines.Add($"offset += {sf.Members.Count * sf.RecordElementCount}");
            return lines;
        }

        if (!sf.IsArray)
        {
            // One element, so one entry per leaf and no slicing. The literal reads them in
            // source order, which is the order the exporter wrote them.
            lines.Add($"this.{field} = {CompactRowLiteral(sf.Members)}");
            return lines;
        }

        // Each leaf's run of entries first, because a single expression would have to advance
        // the offset inside a map callback and the order of that is not something a reader
        // should have to reason about.
        CompactLeafSlices(sf.Members, field, "", sf.RecordElementCount, lines);

        lines.Add($"this.{field} = Array.from({{ length: {sf.RecordElementCount} }}, (_, k) => "
                  + $"({CompactZipLiteral(sf.Members, field, "")}))");

        return lines;
    }


    private string CompactRowLiteral(List<RecordMember> members)
    {
        var parts = members.SelectMany(member =>
        {
            string prop = TsName(member.Name);
            string rowProp = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                ? TsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                : prop;

            if (!member.IsLeaf)
                return new[] { $"{prop}: {CompactRowLiteral(member.Members)}" };

            // A reference member: the entry is the key, and the row it names is filled in by
            // the linking pass. spec/references-in-records.md.
            if (member.IsRef)
            {
                return new[]
                {
                    $"{rowProp}: undefined",
                    $"{prop}: "
                        + FromJsonExpressionOf(member.FirstField!.RefKeyType, "dataRow[offset++]"),
                    $"{prop}_F: false",
                };
            }

            return new[]
            {
                $"{prop}: {FromJsonExpressionOf(member.ElementType, "dataRow[offset++]")}",
            };
        });

        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>
    /// One local per leaf, holding that leaf's adjacent run of entries.
    /// </summary>
    /// <remarks>
    /// A leaf is a wire column, so a leaf's entries are the adjacent run - not a member's. A
    /// member that is a record covers several columns, and slicing once for it would take the
    /// first leaf's run and call it the whole record.
    /// </remarks>
    private void CompactLeafSlices(
        List<RecordMember> members, string field, string prefix, int count, List<string> lines)
    {
        foreach (var member in members)
        {
            string prop = TsName(member.Name);
            string rowProp = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                ? TsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                : prop;

            if (!member.IsLeaf)
            {
                CompactLeafSlices(member.Members, field, prefix + prop + "_", count, lines);
                continue;
            }

            lines.Add($"const {field}_{prefix}{prop} = dataRow.slice(offset, offset + {count})");
            lines.Add($"offset += {count}");
        }
    }

    /// <summary>
    /// The literal zipping the per-leaf locals into element `k`.
    /// </summary>
    private string CompactZipLiteral(List<RecordMember> members, string field, string prefix)
    {
        var parts = members.SelectMany(member =>
        {
            string prop = TsName(member.Name);
            string rowProp = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                ? TsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                : prop;

            if (!member.IsLeaf)
                return new[] { $"{prop}: {CompactZipLiteral(member.Members, field, prefix + prop + "_")}" };

            // A reference member: the entry is the key, and the row it names is filled in by
            // the linking pass. spec/references-in-records.md.
            if (member.IsRef)
            {
                return new[]
                {
                    $"{rowProp}: undefined",
                    $"{prop}: "
                        + FromJsonExpressionOf(member.FirstField!.RefKeyType, $"{field}_{prefix}{prop}[k]"),
                    $"{prop}_F: false",
                };
            }

            return new[]
            {
                $"{prop}: {FromJsonExpressionOf(member.ElementType, $"{field}_{prefix}{prop}[k]")}",
            };
        });

        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>
    /// An empty value of the member's own type, for the declaration to start at.
    /// </summary>
    /// <remarks>
    /// A column the file does not carry leaves its member at whatever the declaration
    /// gave it, and that is not a hypothetical: delete a column and every build made
    /// before the deletion reads files that have nothing for it. An empty string is a
    /// value a consumer can use; `undefined` is a crash one field later.
    /// </remarks>
    private string DefaultValue(SerialField sf)
    {
        // A reference stays undefined: the absence of a referenced row is what that
        // means here, and there is nothing to put in its place.
        if (sf.IsRef)
            return "undefined";

        return DefaultValueOf(sf.ElementType, sf.FirstField!);
    }

    /// <summary>
    /// The same, asked of an element type directly - for a record's members, which are not
    /// serial fields of their own.
    /// </summary>
    private string DefaultValueOf(ValueType elementType, Models.Field field)
    {
        switch (elementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";

            // A bigint literal, because a number does not assign to one.
            case ValueType.Int64: return "0n";

            // Both travel as ticks and are exposed as a decimal string, and a uuid as
            // its canonical text form.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
            case ValueType.Uuid: return "''";

            // A numeric enum, so its zero is a value whether or not a label names it.
            case ValueType.Enum: return $"0 as {field.Enum.Name}";

            default: return "0";
        }
    }

    private static string DeclarationKind(Table table, SerialField sf)
    {
        if (sf.IsArray)
        {
            if (sf.IsRef)
                return "array_ref";

            // One array declaration since v107. Trimming decides how many elements a row
            // carries, not whether the length is known at generation time.
            return "var_array";
        }

        return sf.IsRef ? "scalar_ref" : "scalar";
    }

    /// <summary>
    /// The assignment reading one field out of a named JSON row.
    /// </summary>
    private string NamedRowAssignment(SerialField sf, string field, string prop)
    {
        // The key, not the row. The row is filled in by the linking pass once every
        // table is loaded, exactly as the binary path leaves it.
        //
        // Converted by the key's type, because a `bigint` key arrives as a string like any
        // other 64-bit value and assigning it raw does not typecheck.
        if (sf.IsRef)
        {
            string index = $"{field}_{sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase()}_index";

            string keys = $"this.{index} = "
                        + FromJsonExpressionOf(sf.FirstField!.RefKeyType, $"dataRow.{prop}");

            // An array of them: the resolved array and the flag are as long as the keys, which
            // is the length the data states. Left to the linking pass they would hold only the
            // elements that resolved, and a row whose second reference points at nothing would
            // have a one-element array. spec/nullable-array-elements.md.
            return sf.IsArray
                ? $"{keys}; {SizedFromKeys(field, index)}"
                : keys;
        }

        // An optional column arrives as `null` when the row had no value, and the member
        // keeps the type's empty one - so the two read paths agree about both halves. The
        // conversion is skipped for a null, because a converter given one would produce a
        // value rather than leave the default alone.
        if (sf.RowMayBeAbsent)
        {
            string present = $"dataRow.{prop} !== null && dataRow.{prop} !== undefined";

            return $"this._{prop}HasValue = {present}; "
                 + $"if (this._{prop}HasValue) {ValueFromNamedRow(sf, field, prop)}";
        }

        return ValueFromNamedRow(sf, field, prop);
    }

    /// <summary>
    /// The resolved array and its flag, made as long as the keys just read.
    /// </summary>
    /// <remarks>
    /// The values are filled in by the linking pass, and only for the keys that point at
    /// something - so nothing else gives these two arrays their length. Every other language
    /// sizes them where it reads, and a shorter array here is a hole a `for ... of` walks past
    /// without a sign. spec/nullable-array-elements.md.
    /// </remarks>
    private static string SizedFromKeys(string field, string index)
        => $"this.{field} = new Array(this.{index}.length).fill(undefined); "
         + $"this.{field}_F = new Array(this.{index}.length).fill(false)";

    /// <summary>The assignment itself, without the presence handling around it.</summary>
    private string ValueFromNamedRow(SerialField sf, string field, string prop)
    {
        if (!NeedsJsonConversion(sf))
        {
            // Array or scalar alike: a value the JSON carries as-is is assigned
            // straight through.
            return $"this.{field} = dataRow.{prop}";
        }

        if (sf.IsArray)
            return $"this.{field} = dataRow.{prop}.map(v => {FromJsonExpression(sf, "v")})";

        return $"this.{field} = {FromJsonExpression(sf, $"dataRow.{prop}")}";
    }

    /// <summary>
    /// The statements reading one field out of a compact JSON row.
    ///
    /// The compact row is flat: a serial field contributes one entry per column,
    /// matching how the binary exporter writes them. Reading a single entry for the
    /// whole group took only its first column and left every later field reading
    /// someone else's value.
    /// </summary>
    private IReadOnlyList<string> CompactRowStatements(SerialField sf, string field, string prop)
    {
        if (sf.IsRef)
        {
            string index = $"{field}_{sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase()}_index";

            // The key's conversion, for the same reason the named row needs one.
            string keyConvert = FromJsonExpressionOf(sf.FirstField!.RefKeyType, "v") == "v"
                ? ""
                : $".map(v => {FromJsonExpressionOf(sf.FirstField!.RefKeyType, "v")})";

            return sf.IsArray
                ? new[]
                {
                    $"this.{index} = dataRow.slice(offset, offset + {sf.Fields.Count}){keyConvert}",
                    SizedFromKeys(field, index),
                    $"offset += {sf.Fields.Count}",
                }
                : new[]
                {
                    $"this.{index} = "
                        + FromJsonExpressionOf(sf.FirstField!.RefKeyType, "dataRow[offset++]"),
                };
        }

        string convert = NeedsJsonConversion(sf)
            ? $".map(v => {FromJsonExpression(sf, "v")})"
            : "";

        if (sf.IsVariableLengthArray)
        {
            // One entry that already is an array, so it is taken whole. A serial field
            // is flattened across N entries and sliced below.
            return new[] { $"this.{field} = dataRow[offset++]{convert}" };
        }

        if (sf.IsArray)
        {
            return new[]
            {
                $"this.{field} = dataRow.slice(offset, offset + {sf.Fields.Count}){convert}",
                $"offset += {sf.Fields.Count}",
            };
        }

        // An optional scalar: the entry is `null` where the row had no value, and the
        // member keeps the type's empty one. Read into a local first, because the offset
        // must advance exactly once whichever branch is taken.
        if (sf.RowMayBeAbsent)
        {
            string local = $"{field}_raw";
            string converted = NeedsJsonConversion(sf) ? FromJsonExpression(sf, local) : local;

            return new[]
            {
                $"const {local} = dataRow[offset++]",
                $"this._{prop}HasValue = {local} !== null && {local} !== undefined",
                $"if (this._{prop}HasValue) this.{field} = {converted}",
            };
        }

        if (NeedsJsonConversion(sf))
            return new[] { $"this.{field} = {FromJsonExpression(sf, "dataRow[offset++]")}" };

        return new[] { $"this.{field} = dataRow[offset++]" };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The call reading one value of a column's element type.
    /// </summary>
    /// <summary>
    /// The rendered checkColumn call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "tabbit.KIND_ARRAY" : "tabbit.KIND_SCALAR";

        string accepted;

        if (wire.IsRef)
        {
            // The key the target is addressed by. `ELEMENT_I32` alone is what a reference
            // accepted while a key could only be an int. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "tabbit.ELEMENT_STRING",
                ValueType.Int64 => "tabbit.ELEMENT_I64, tabbit.ELEMENT_I32, tabbit.ELEMENT_VARINT",
                ValueType.Uuid => "tabbit.ELEMENT_UUID",
                _ => "tabbit.ELEMENT_I32",
            };
        }
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "tabbit.ELEMENT_I32, tabbit.ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "tabbit.ELEMENT_I64, tabbit.ELEMENT_I32, tabbit.ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "tabbit.ELEMENT_F64, tabbit.ELEMENT_F32, tabbit.ELEMENT_I32"; break;
                case ValueType.Float: accepted = "tabbit.ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "tabbit.ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "tabbit.ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "tabbit.ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "tabbit.ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "tabbit.ELEMENT_I64"; break;

                default:
                    throw new TabbitDefectException($"The typescript generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability rides with kind and count: a file that says optional puts a presence
        // bitmap in front of the block, and code not expecting one reads it as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one.
        string elements = wire.HasOptionalElements ? ", true" : "";

        return $"tabbit.checkColumn(column, '{tableName}.{wire.Name}', {kind}, "
            + $"{nullable}, [{accepted}]{elements})";
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

        if (wire.IsArray)
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
    /// a column that reads the reader directly.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"cursor = new tabbit.TcbColumnCursor(reader, column, rowCount, '{tableName}.{wire.Name}')"
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
        if (wire.IsArray)
            return "";

        // A reference runs on the key it carries. `nextSameI32` was the only answer while a
        // key could only be an int. spec/reference-key-types.md.
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
    /// The line assigning one row from `value`, the run's decoded value, inside the loop
    /// the template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string fieldName = "_" + TsName(wire.Group.Name);
        string memberAccess = (wire.Member is null)
            ? ""
            : string.Concat(wire.MemberPath.Select(name => "." + TsName(name)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"records[i].{fieldName}_{wire.TagCarrier.RefTableName.ToPascalCase()}_index = value"
                : $"records[i].{fieldName}{memberAccess} = value";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"records[i].{fieldName}{memberAccess} = value as {ToTypescriptTypename(wire.TagCarrier)}";

        return $"records[i].{fieldName}{memberAccess} = value";
    }

    private string BinaryReadExpression(WireColumn wire)
    {
        // A column can arrive encoded, so it reads through the cursor - which also carries
        // the lossless promotions. An array's elements read through it as well, by the same
        // calls: what differs is only that the row's length comes from the cursor first.
        if (UsesCursor(wire))
        {
            if (wire.ElementType == ValueType.Enum)
                return $"cursor.nextI32() as {ToTypescriptTypename(wire.TagCarrier)}";

            // Only the stored key is on the wire; the value is filled in once every table
            // is loaded. The call is the key's own - `nextI32` for every reference is what
            // kept a table keyed by anything else from being pointed at.
            // spec/reference-key-types.md.
            if (wire.IsRef)
            {
                return wire.RefKeyType switch
                {
                    ValueType.Int64 => "cursor.nextI64()",
                    ValueType.String => "cursor.nextString()",
                    _ => "cursor.nextI32()",
                };
            }

            return wire.ElementType switch
            {
                ValueType.Int32 => "cursor.nextI32()",
                ValueType.Int64 => "cursor.nextI64()",
                ValueType.Double => "cursor.nextF64()",
                ValueType.Float => "cursor.nextF32()",
                ValueType.Bool => "cursor.nextBool()",

                // Ticks, so the member is built from what the i64 column carried -
                // the same text the direct read produces, from the same number.
                ValueType.DateTime => "tabbit.formatDateTimeTicks(cursor.nextI64())",
                ValueType.TimeSpan => "tabbit.formatTimeSpanTicks(cursor.nextI64())",

                _ => "cursor.nextString()",
            };
        }

        return wire.ElementType switch
        {
            // Enum values travel zig-zag encoded rather than fixed width.
            ValueType.Enum => $"reader.readEnum() as {ToTypescriptTypename(wire.TagCarrier)}",
                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/reference-key-types.md.
            ValueType.ForeignRecord => LanguageProfile.Typescript.ReadCall(wire.RefKeyType),
            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in eight other generators.
            _ => LanguageProfile.Typescript.ReadCall(wire.ElementType),
        };
    }

    /// <summary>
    /// The type a value has in the JSON export, which is not always the type the
    /// generated member exposes.
    /// </summary>
    private string JsonWireTypeOf(SerialField sf)
    {
        // A 64-bit integer is exported as a string, because JSON's single numeric
        // type is a double and would round it.
        if (sf.ElementType == ValueType.Int64)
            return "string";

        // A reference is exported as the target row's key. Declaring it as the row type
        // said the JSON carried something it never carries, and the generated assignment
        // then put a number into a member typed as a record.
        //
        // The key's own JSON type, not `number`: a `bigint` key is exported as a string for
        // the same reason any 64-bit value is, and a `uuid` or `string` key as itself.
        // spec/reference-key-types.md.
        if (sf.IsRef)
            return JsonWireTypeOfKey(sf.FirstField!.RefKeyType);

        return ToTypescriptTypename(sf.FirstField);
    }

    /// <summary>
    /// The type a stored key has in the JSON export.
    /// </summary>
    /// <remarks>
    /// The key's own type, not `number`: a `bigint` key is exported as a string for the same
    /// reason any 64-bit value is, and a `uuid` or `string` key as itself.
    /// spec/reference-key-types.md.
    /// </remarks>
    private string JsonWireTypeOfKey(ValueType keyType)
        => keyType == ValueType.Int64
            ? "string"
            : ToTypescriptTypename(keyType, null, null);

    /// <summary>
    /// Wraps a value read from JSON so it becomes the member's type.
    ///
    /// Two types need it. A 64-bit integer arrives as a string and is reconstructed
    /// exactly. A float arrives as the shortest decimal that round-trips it, which in
    /// JavaScript widens to a double a hair away from the stored 32-bit value - so it
    /// is rounded back to float precision, and both read paths then agree.
    /// </summary>
    private string FromJsonExpression(SerialField sf, string source)
        => FromJsonExpressionOf(sf.ElementType, source);

    /// <summary>The same, asked of an element type directly - for a record's members.</summary>
    private string FromJsonExpressionOf(ValueType elementType, string source)
    {
        return elementType switch
        {
            ValueType.Int64 => $"BigInt({source})",
            ValueType.Float => $"Math.fround({source})",
            _ => source,
        };
    }

    /// <summary>
    /// The type a record member's value has in the JSON export, which is not always the
    /// member type - a 64-bit integer is exported as a string.
    /// </summary>
    private string JsonWireTypeOfMember(RecordMember member)
        => member.ElementType == ValueType.Int64 ? "string" : ToTypescriptTypename(member.FirstField);

    /// <summary>
    /// Whether values of this column need converting on the way in from JSON.
    /// </summary>
    private bool NeedsJsonConversion(SerialField sf)
        => sf.ElementType == ValueType.Int64 || sf.ElementType == ValueType.Float;

    /// <summary>
    /// Renders a cooked constant value as a TypeScript literal.
    ///
    /// Types that TypeScript has no native equivalent for - datetime, timespan and
    /// uuid - are surfaced as strings, matching ToTypescriptTypename.
    /// </summary>
    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        switch (constant.Type)
        {
            case ValueType.String:
                return $"'{EscapeTypescriptString((string)constant.Value!)}'";

            case ValueType.Bool:
                return (bool)constant.Value! ? "true" : "false";

            case ValueType.Int32:
                return ((int)constant.Value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                // `n` suffix: a bigint-typed member cannot be initialized from a
                // number literal, and TypeScript rejects it outright.
                return ((long)constant.Value!).ToString(CultureInfo.InvariantCulture) + "n";

            case ValueType.Float:
                return ((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.Double:
                return ((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.DateTime:
                return $"'{((DateTime)constant.Value!).ToString("o", CultureInfo.InvariantCulture)}'";

            case ValueType.TimeSpan:
                return $"'{((TimeSpan)constant.Value!).ToString(null, CultureInfo.InvariantCulture)}'";

            case ValueType.Uuid:
                return $"'{(Guid)constant.Value!}'";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return $"{constant.Enum.Name}.{label.Name}";
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", constant.Type),
                            ("Generator", "TypeScript")));
        }
    }

    private string EscapeTypescriptString(string input)
    {
        var literal = new StringBuilder(input.Length + 2);

        foreach (var c in input)
        {
            switch (c)
            {
                case '\'': literal.Append("\\\'"); break;
                case '\\': literal.Append(@"\\"); break;
                case '\0': literal.Append(@"\0"); break;
                case '\b': literal.Append(@"\b"); break;
                case '\f': literal.Append(@"\f"); break;
                case '\n': literal.Append(@"\n"); break;
                case '\r': literal.Append(@"\r"); break;
                case '\t': literal.Append(@"\t"); break;
                case '\v': literal.Append(@"\v"); break;
                default:
                    if (c >= 0x20 && c <= 0x7e)
                        literal.Append(c);
                    else
                        literal.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
            }
        }

        return literal.ToString();
    }

    // ------------------------------------------------------------- types

    /// <summary>
    /// A member name.
    ///
    /// camelCase, then escaped if TypeScript will not take it. Most reserved words are
    /// legal as member names, so only the few that genuinely are not get renamed -
    /// `constructor` above all, which a class may not declare as an accessor.
    /// </summary>
    private string TsName(string name) => LanguageProfile.Typescript.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for the names that are not members - a constant, an accessor's
    /// per-table slot.
    /// </summary>
    private static string TsCamelName(string name) => LanguageProfile.Typescript.MemberName(name.ToCamelCase());

    /// <summary>
    /// The same name, but usable where TypeScript binds rather than where it names a
    /// member - a `const` in the accessor's read methods.
    /// </summary>
    /// <remarks>
    /// A second list, because the two positions genuinely differ and the profile's list
    /// is the member one on purpose: `package` is a perfectly good property name, so the
    /// generated API keeps it, but `const package = ...` is a syntax error in a module -
    /// generated code is strict-mode, where the reserved words below are all illegal as
    /// bindings. A table called `Package` used to emit exactly that, and the whole
    /// accessor failed to parse; nothing caught it because the fixture had no table
    /// named after a keyword.
    /// </remarks>
    private string TsLocalName(string name)
    {
        string local = TsName(name);

        return BindingReservedWords.Contains(local) ? local + "_" : local;
    }

    /// <summary>
    /// What a `const` may not be called: the always-reserved words, the ones strict mode
    /// adds, `await` for module code, and the two strict mode refuses to let anything
    /// bind.
    /// </summary>
    private static readonly HashSet<string> BindingReservedWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for",
        "function", "if", "import", "in", "instanceof", "new", "null", "return", "super",
        "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with",
        "implements", "interface", "let", "package", "private", "protected", "public",
        "static", "yield", "await", "arguments", "eval",
    };

    private string ToTypescriptTypename(Field? field, bool asArray = false)
    {
        // ElementType, not Type: an array field is rendered by naming its element
        // and letting the caller add the brackets, exactly as a serial field is.
        return ToTypescriptTypename(field!.ElementType, field!.EnumOrNull, field!.RefTableName, asArray);
    }

    private string ToTypescriptTypename(Models.ValueType type, Models.Enum? enumm, string? refTableName, bool asArray = false)
    {
        string result;
        switch (type)
        {
            // The two that name something from the model rather than the language.
            // Why int64 is bigint, and why the three text-shaped types are string, is
            // recorded on the profile itself.
            case ValueType.Enum:
                result = enumm!.Name.ToPascalCase();
                break;

            case ValueType.ForeignRecord:
                result = $"{refTableName.ToPascalCase()}Record";
                break;

            default:
                result = LanguageProfile.Typescript.ScalarTypeName(type);
                break;
        }

        return asArray ? LanguageProfile.Typescript.ArrayOf(result) : result;
    }

    // ----------------------------------------------------------- helpers

    /// <summary>
    /// A comment as the doc-comment lines the templates emit verbatim.
    ///
    /// Rendered here rather than in the template because the wrapping is not a simple
    /// per-line prefix: a comment of one line becomes `/** text * /` on that line, and a
    /// longer one is run together, which is what the printer did.
    /// </summary>
    // `new`, and not the base one: TypeScript wraps the whole comment in `/** */`
    // and runs its lines together, which is a different answer rather than the same
    // one spelled differently.
    private static new IReadOnlyList<string> CommentLines(string comment)
    {
        if (string.IsNullOrEmpty(comment))
            return Array.Empty<string>();

        var text = new StringBuilder("/** ");

        if (comment.Count(c => c == '\n') <= 1)
        {
            // A space, not nothing. Two lines folded onto one need a separator where the
            // break was, and without it the last word of the first line and the first of
            // the second arrive as one word.
            text.Append(comment.Replace("\n", " ").Trim());
        }
        else
        {
            var lines = comment.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                bool last = i == lines.Length - 1;

                if (lines[i].Length == 0 && !last)
                    text.Append('\n');
                else if (lines[i].Length > 0 || !last)
                    text.Append(lines[i]);
            }
        }

        text.Append(" */");

        return text.ToString().Split('\n');
    }

    private string GetTsFilename(string name) => Path.Combine(_typescriptRecipe.Path, name);

    private void Write(string filename, string templateName, object view)
    {
        StagingFiles.WriteAllTextToFile(
            GetTsFilename(filename), TemplateEngine.Render(templateName, view));
    }
}
