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
/// Settings for the Dart target.
/// </summary>
public sealed class DartRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>Base name of the generated library, without its extension.</summary>
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
/// Emits one Dart library holding every generated type, plus the binary reader.
///
/// int64 and both tick counts are BigInt rather than int. Dart's int is 64 bits on the
/// VM and a double on the web, where it carries 53 - and a value past that does not
/// fail there, it comes back changed. The TypeScript target reached the same
/// conclusion for the same reason, which is the argument for the corpus: the trap is a
/// property of the format meeting the language, and it is invisible without a value
/// that exercises it.
///
/// The shape lives in templates/dart.sbn.
/// </summary>
[TabbitTarget("dart", TargetKind.CodeGeneration, Order = 95)]
public class DartCodeGenerator : CodeGenerator<DartRecipe>
{
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private DartRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Camel;

    /// <summary>
    /// A record group generates a class and a list of it; a member column fills one of its
    /// properties.
    /// </summary>
    /// <remarks>
    /// The eleventh of the thirteen, and the same split as the ten before it - declaration
    /// per field, reading per wire column.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a class declared beside the element type, and the read reaches it with
    /// a longer member path. Its member is initialized by calling that class, which is how the
    /// values inside it reach the same empty values a scalar member gets.
    /// spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `has{Field}` property beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `T?`, which is the shape Dart's null safety would suggest. Every generated
    /// property is initialized rather than nullable for the reason this repeats: a caller
    /// reading a value should not have to answer for a row the read never reached.
    /// spec/optional-fields.md has the rest.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, DartRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Camel, "dart");

        Generate();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes the library file and a part per table, per enum and per constant set.
    /// </summary>
    /// <remarks>
    /// `part` rather than a library per file, which is what a Dart code generator does and
    /// what suits this output: a part shares its library's imports, so splitting costs no
    /// per-file import calculation - and Dart requires every file to import what it names.
    /// A consumer still imports one file and gets the model.
    ///
    /// File names are lower_snake_case, as Dart writes them, while the classes inside keep
    /// their PascalCase.
    /// </remarks>
    /// <summary>
    /// The accessor's file, extension left off.
    /// </summary>
    /// <remarks>
    /// Lower case with underscores, like every other file this target writes. The type keeps
    /// the name the recipe gave it; only the file follows the language's own convention, so
    /// one canonical name reads correctly in thirteen of them.
    /// </remarks>
    private string AccessorType => _recipe.AccessorName.ToPascalCase();

    private string AccessorFile => _recipe.AccessorName.ToSnakeCase();
    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Dart into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        // Where each part sits, and how a part refers back to the library. Both spelled with
        // forward slashes: that is what a Dart directive takes, and it keeps the generated
        // text the same wherever the conversion ran.
        var parts = new List<(string Directive, string File, string Template, DartPartView View)>();

        string library = "../" + AccessorFile + ".dart";

        foreach (var table in view.Tables)
        {
            string name = table.TableName.ToSnakeCase();

            parts.Add(($"tables/{name}.dart", System.IO.Path.Combine("tables", name + ".dart"),
                       "dart-table.sbn", new DartPartView { AccessorName = AccessorType, Library = library, Table = table }));
        }

        foreach (var enumm in view.Enums)
        {
            string name = enumm.Name.ToSnakeCase();

            parts.Add(($"enums/{name}.dart", System.IO.Path.Combine("enums", name + ".dart"),
                       "dart-enum.sbn", new DartPartView { AccessorName = AccessorType, Library = library, Enumm = enumm }));
        }

        foreach (var set in view.ConstantSets)
        {
            string name = set.Name.ToSnakeCase();

            parts.Add(($"constants/{name}.dart", System.IO.Path.Combine("constants", name + ".dart"),
                       "dart-constants.sbn", new DartPartView { AccessorName = AccessorType, Library = library, Set = set }));
        }

        view.Parts = parts.Select(part => part.Directive).ToList();

        Write(AccessorFile + ".dart", "dart-accessor.sbn", view);

        foreach (var part in parts)
            Write(part.File, part.Template, part.View);
    }

    private void Write(string relative, string templateName, object view)
    {
        string filename = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(_recipe.Path, relative));

        StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render(templateName, view));
    }

    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Dart.tcb_reader.dart",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "tcb_reader.dart"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Dart.updater.dart",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "updater.dart"));
        }
    }

    // --------------------------------------------------------------- view

    private DartFileView BuildView() => new DartFileView
    {
        AccessorName = AccessorType,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private DartEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new DartEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            DefaultLabel = DartName(fallback.Name),
            Labels = enumm.Labels.Select((label, index) => new DartEnumLabelView
            {
                Name = DartCamelName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),

                // A Dart enum body needs a semicolon after the last constant when
                // anything follows it, and the constructor always does.
                Separator = index == enumm.Labels.Count - 1 ? ";" : ",",
            }).ToList(),
        };
    }

    private DartConstantSetView BuildConstantSet(ConstantSet constantSet) => new DartConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new DartConstantView
        {
            Name = DartCamelName(constant.Name),
            Type = ToDartTypeName(constant.Type, constant.Enum, null),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private DartTableView BuildTable(Table table) => new DartTableView
    {
        RawName = table.Name,
        RecordName = table.Name.ToPascalCase() + "Record",
        TableName = table.Name.ToPascalCase() + "Table",
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),

        // A separate list, because declaring a property is per field and reading is per
        // column - and a record group is one column per member of it.
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),

        // One cursor variable for the whole method: switch cases share a scope, so
        // each encodable column assigns it rather than declaring its own. Asked of the
        // columns, because that is what the switch has a case for.
        NeedsCursor = table.WireColumns.Any(UsesCursor),

        NeedsPresence = table.WireColumns.Any(wire => wire.IsNullable),
        NeedsElementPresence = table.WireColumns.Any(wire => wire.HasOptionalElements),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<DartIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf => new DartIndexView
        {
            Member = DartName(sf.Name),
            Suffix = sf.Name.ToPascalCase(),
            KeyType = ResolvedElementType(sf),
            MapName = "_by" + sf.Name.ToPascalCase(),
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

    private DartFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = DartName(sf.Name);
        bool nullable = sf.RowMayBeAbsent;

        var declarations = Declarations(sf, name).ToList();

        // False until the read says otherwise, so a file that does not carry the column
        // leaves the property absent rather than claiming a value it never got.
        if (nullable)
            declarations.Add($"bool {PresenceMember(sf)} = false;");

        // And the per-element answer, empty until the read fills it: an index into an empty
        // list is out of range, and the answer there is that the element has a value.
        // spec/nullable-array-elements.md.
        if (sf.ElementMayBeAbsent)
            declarations.Add($"List<bool> {ElementPresenceMember(sf)} = const <bool>[];");

        return new DartFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Declarations = declarations,
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<DartRecordMemberView>(),
            IsNullable = nullable,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = PresenceMember(sf),
            ElementPresenceMember = ElementPresenceMember(sf),
        };
    }

    /// <summary>
    /// A record group: the class to declare for one element, and the property holding one
    /// or a list of them.
    /// </summary>
    /// <remarks>
    /// A top-level class carrying the table's name. Dart has no nested classes, and the
    /// generated files are parts of one library, so two tables each holding a `Slot` group
    /// would declare the same name twice.
    ///
    /// No reference members: a reference belongs to a member and the model refuses one there,
    /// so nothing here has the index list a reference would be carried as.
    /// </remarks>
    /// <summary>
    /// Members of one level of a record, declaring a class for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the classes it produces. A nested member is initialized by calling its own
    /// class - which is how every value inside it reaches the empty value a scalar member gets.
    /// spec/nested-multi-level.md.
    /// </remarks>
    private List<DartRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<DartRecordTypeView> declared)
    {
        var result = new List<DartRecordMemberView>();

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
                    string key = ToDartTypeName(member.FirstField!.RefKeyType, null, null);

                    declarations.Add(member.IsArray
                        ? $"List<{row}?> {DartName(member.Name)} = "
                          + $"List.filled({member.Fields.Count}, null);"
                        : $"{row}? {DartName(member.Name)};");

                    declarations.Add(member.IsArray
                        ? $"List<{key}> {DartName(member.Name)}Index = "
                          + $"List.filled({member.Fields.Count}, {RefKeyDefault(member.FirstField!.RefKeyType)});"
                        : $"{key} {DartName(member.Name)}Index = "
                          + RefKeyDefault(member.FirstField!.RefKeyType) + ";");
                }
                else
                {
                    // The list is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    declarations.Add((member.IsArray
                                    ? $"List<{ToDartTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null)}>"
                                    : ToDartTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null))
                                + $" {DartName(member.Name)} = "
                                + (member.IsArray
                                    ? $"List.filled({member.Fields.Count}, {MemberDefault(member)})"
                                    : MemberDefault(member))
                                + ";");
                }

                result.Add(new DartRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),
                    Declarations = declarations,
                });

                continue;
            }

            // A level below. The class name carries the path so two records each holding a
            // `Position` do not name one class twice.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new DartRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record.{DartName(group.Name)}",
            });

            result.Add(new DartRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declarations = new[] { $"{typeName} {DartName(member.Name)} = {typeName}();" },
            });
        }

        return result;
    }

    private DartFieldView BuildRecordField(Table table, SerialField sf)
    {
        string name = DartName(sf.Name);
        string entry = RecordTypeName(table, sf);

        // Innermost first, so a class is declared before the one naming it.
        var recordTypes = new List<DartRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, entry, table, sf, recordTypes);

        recordTypes.Add(new DartRecordTypeView
        {
            TypeName = entry,
            Members = members,
            IsOutermost = true,
            Owner = $"{table.Name.ToPascalCase()}Record.{name}",
        });

        // A list with its elements already made, where the length is the sheet's column
        // count. A trimmed group starts empty, because its length is the row's.
        // An array of arrays declares no element type: the outer level has no name for one to
        // belong to, so the inner list is the type. spec/nested-multi-level.md.
        string inner = ToDartTypeName(
            sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull, null);

        string declaration = sf.MembersAreAnonymous
            ? $"List<List<{inner}>> {name} = List.generate({sf.Members.Count}, "
              + $"(_) => List.filled({sf.RecordElementCount}, {MemberDefault(sf.Members[0])}));"
            : sf.IsArray
                ? (table.TrimTrailingArrayElements
                    ? $"List<{entry}> {name} = [];"
                    : $"List<{entry}> {name} = List.generate({sf.RecordElementCount}, (_) => {entry}());")
                : $"{entry} {name} = {entry}();";

        return new DartFieldView
        {
            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,
            Declarations = new[] { declaration },
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
    private DartColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new DartColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = DartName(wire.Group.Name),

            // A record's member column assigns one property of the element rather than the
            // member itself: `record.slot[j].id` instead of `record.slot[j]`.
            MemberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + DartName(part))),
            // A reference member reads into the key beside the row it will resolve to, and the
            // suffix goes on the member rather than after the subscript, because a member that
            // is an array holds one key per element. spec/references-in-records.md.
            MemberRefSuffix = (wire.Member is not null && wire.IsRef) ? "Index" : "",
            MemberAt = wire.MemberAt,

            RecordTypeName = wire.Group.IsRecord ? RecordTypeName(table, wire.Group) : "",
            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,
            ReadElement = ElementReadExpression(wire),
            LengthRead = UsesCursor(wire) ? "cursor.nextLength()" : "reader.readCounter32()",
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = PresenceMember(wire.Group),
            ElementPresenceMember = ElementPresenceMember(wire.Group),
            EmptyValue = EmptyValue(wire),
        };
    }

    /// <summary>
    /// The element type of a record group, which carries the table's name.
    /// </summary>
    /// <remarks>
    /// The generated files are parts of one library, so every type they declare shares a
    /// namespace - two tables each holding a `Slot` group would be the same name twice.
    /// </remarks>
    private static string RecordTypeName(Table table, SerialField sf)
        => table.Name.ToPascalCase() + sf.Name.ToPascalCase() + "Entry";

    /// <summary>
    /// The property a nullable column's presence lands in.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever reads
    /// it, and the model has already required its columns to agree about being optional.
    /// </remarks>
    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : DartName("has_" + sf.Name);

    /// <summary>The member holding which of an array's elements have a value.</summary>
    private string ElementPresenceMember(SerialField sf)
        => sf.IsRecord ? "" : DartName("has_" + sf.Name + "_at");

    /// <summary>What one record member starts at, for the same reason an ordinary one does.</summary>
    /// <summary>What a stored key holds before a row is read.</summary>
    /// <remarks>
    /// Spelled from the key's own type, because a `string` key has no zero.
    /// spec/reference-key-types.md.
    /// </remarks>
    private static string RefKeyDefault(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "''",

            // A uuid is a class here, so its empty value is one of those rather than a
            // literal. spec/reference-key-types.md.
            ValueType.Uuid => "Uuid.empty()",
            _ => "0",
        };

    private string MemberDefault(RecordMember member)
    {
        switch (member.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Int64:
            case ValueType.DateTime:
            case ValueType.TimeSpan: return "BigInt.zero";
            case ValueType.Uuid: return "Uuid.empty()";
            case ValueType.Enum:
                return $"{member.FirstField!.Enum.Name.ToPascalCase()}.of(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// What an absent row's property is set back to, so the binary path lands where the
    /// JSON one does.
    /// </summary>
    /// <remarks>
    /// The property's own type rather than its element's: an optional array declares a
    /// `List`, and its empty value is an empty list rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "[]";

        // The resolved property is a nullable reference to the target row, and absence there
        // is exactly what null says.
        if (wire.ElementType == ValueType.ForeignRecord)
            return "null";

        switch (wire.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Int64:
            case ValueType.DateTime:
            case ValueType.TimeSpan: return "BigInt.zero";
            case ValueType.Uuid: return "Uuid.empty()";
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from. The elements that stay raw by spec keep
    /// reading the reader directly.
    /// </summary>
    private static bool UsesCursor(WireColumn wire)
    {
        // Arrays go through it too. An array block states an encoding for its elements and
        // one for its rows' lengths, and the cursor is what decodes both - so an array's
        // elements are read exactly the way a scalar column's are, one level down.
        //
        // Uuid is the exception, and the same one it has always been: no encoding applies
        // to it, so it has no cursor path to reach.
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
    /// a column that reads the reader directly.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"cursor = TcbColumnCursor(reader, column, count, '{tableName}.{wire.Name}');"
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
    /// The line assigning one row from `value`, the run's decoded value, inside the loop
    /// the template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string name = DartName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + DartName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"loaded[i].{name}Index = value;"
                : $"loaded[i].{name}{memberAccess}Index = value;";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"loaded[i].{name}{memberAccess} = {wire.TagCarrier.Enum.Name.ToPascalCase()}.of(value);";

        return $"loaded[i].{name}{memberAccess} = value;";
    }

    /// <summary>
    /// The field declarations, each initialized.
    ///
    /// Initialized rather than `late`, because Dart's null safety would otherwise turn
    /// a read of an unread record into a runtime failure where every other generated
    /// reader hands back a default.
    /// </summary>
    private IReadOnlyList<string> Declarations(SerialField sf, string name)
    {
        string elementType = ResolvedElementType(sf);

        if (sf.IsRef)
        {
            return sf.IsArray
                ? new[]
                {
                    $"List<{elementType}?> {name} = [];",
                    $"List<int> {name}Index = [];",
                }
                : new[]
                {
                    $"{elementType}? {name};",
                    $"int {name}Index = 0;",
                };
        }

        if (sf.IsArray)
            return new[] { $"List<{elementType}> {name} = [];" };

        return new[] { $"{elementType} {name} = {DefaultValue(sf)};" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Int64:
            case ValueType.DateTime:
            case ValueType.TimeSpan: return "BigInt.zero";
            case ValueType.Uuid: return "Uuid.empty()";
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
            ? "kindVarArray"
            : (wire.IsFixedArray ? "kindFixedArray" : "kindScalar");

        // -1 where one column owns the whole array: the file states how many elements it
        // holds and the read takes it from there, so there is no length here to hold it to.
        // A record member keeps its count - several columns fill one array and the number
        // they agree on is part of the generated shape, so a disagreement is a schema change
        // rather than data. spec/nullable-array-elements.md.
        bool ownsItsArray = wire.IsFixedArray && wire.Member is null;

        int count = wire.IsVariableLengthArray ? 0 : (ownsItsArray ? -1 : wire.Cells.Count);

        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `elementI32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "elementString",
                ValueType.Int64 => "elementI64, elementI32, elementVarint",
                ValueType.Uuid => "elementUuid",
                _ => "elementI32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32: accepted = "elementI32, elementVarint"; break;
                case ValueType.Int64: accepted = "elementI64, elementI32, elementVarint"; break;
                case ValueType.Double: accepted = "elementF64, elementF32, elementI32"; break;
                case ValueType.Float: accepted = "elementF32"; break;
                case ValueType.Bool: accepted = "elementBool"; break;
                case ValueType.String: accepted = "elementString"; break;
                case ValueType.Uuid: accepted = "elementUuid"; break;
                case ValueType.Enum: accepted = "elementVarint"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "elementI64"; break;

                default:
                    throw new TabbitException($"The dart generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one.
        string elements = wire.HasOptionalElements ? ", true" : "";

        return $"checkColumn(column, '{tableName}.{wire.Name}', {kind}, {count}, "
            + $"{nullable}, [{accepted}]{elements});";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member walks the elements without building them - the list was made
    /// with the record - while a trimmed one reads its length from the row, and there the
    /// first member does build because no declaration could have known how long this row's
    /// is.
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
            return "var_array";

        if (wire.IsFixedArray)
            return wire.IsRef ? "serial_ref" : "serial";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private DartAccessorView BuildAccessor() => new DartAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new DartTableSlotView
        {
            Name = DartCamelName(table.Name),
            TableName = table.Name.ToPascalCase() + "Table",

            // Unescaped: this one names the file the exporter wrote.
            DataFileName = table.Name,
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
            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0)
            .Select(x => new DartCrossReferenceView
            {
                Table = DartName(x.Table.Name),
                Fields = x.Fields.Select(sf => new DartReferenceFieldView
                {
                    Name = DartName(sf.Name),
                    RefTable = DartName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + DartName(sf.FirstField!.ResolvedRefField!.Name),
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
    private DartRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = DartName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + DartName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsFixedArray || wire.IsVariableLengthArray;

        string path = !isArray || wire.Group.MembersAreArrays
            ? $"record.{name}{member}"
            : $"record.{name}[i]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[i]" : "";

        return new DartRecordReferenceView
        {
            Access = path + subscript,
            Key = path + "Index" + subscript,

            // Whichever list holds the elements. Its own length rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Count = isArray
                ? (wire.Group.MembersAreArrays ? $"{path}Index.length" : $"record.{name}.length")
                : "",

            RefTable = DartName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The expression filling one value of a member. A column can arrive encoded, so it
    /// reads through the cursor - which also carries the lossless promotions. An array's
    /// elements read through it as well, by the same calls: what differs is only that the
    /// row's length comes from the cursor first.
    /// </summary>
    private string ElementReadExpression(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return ReadExpression(wire);

        // Only the stored index is on the wire; the accessor resolves it once every
        // table is loaded.
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
            // An enum decodes as its int value, converted exactly as the raw read was.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(cursor.nextI32())";

            case ValueType.Int32: return "cursor.nextI32()";
            case ValueType.Int64: return "cursor.nextI64()";
            case ValueType.Double: return "cursor.nextF64()";
            case ValueType.Float: return "cursor.nextF32()";
            case ValueType.Bool: return "cursor.nextBool()";

            // Ticks, so the member is built from what the i64 column carried - which
            // for Dart is the BigInt itself, as the raw read hands back.
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
                    return LanguageProfile.Dart.ReadCall(wire.RefKeyType);

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            default: return LanguageProfile.Dart.ReadCall(wire.ElementType);
        }
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        return ToDartTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull, null);
    }

    private string ToDartTypeName(ValueType type, Models.Enum? enumm, string? refTableName)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return enumm!.Name.ToPascalCase();

            case ValueType.ForeignRecord:
                return refTableName.ToPascalCase() + "Record";

            default:
                return LanguageProfile.Dart.ScalarTypeName(type);
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

            // Parsed rather than written as a literal: an int literal past 2^53 is not
            // exact on the web, which is the whole reason this is a BigInt.
            case ValueType.Int64:
                return $"BigInt.parse('{((long)constant.Value!).ToString(CultureInfo.InvariantCulture)}')";

            case ValueType.Float:
            case ValueType.Double:
                return Decimal(constant.Type == ValueType.Float
                    ? ((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture)
                    : ((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture));

            case ValueType.DateTime:
                return $"BigInt.parse('{((DateTime)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture)}')";

            case ValueType.TimeSpan:
                return $"BigInt.parse('{((TimeSpan)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture)}')";

            case ValueType.Uuid:
                return "Uuid(Uint8List.fromList([" + string.Join(", ",
                    ((Guid)constant.Value!).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "]))";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}.{DartCamelName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the dart generator cannot render.");
        }
    }

    /// <summary>
    /// Gives a rendered float a decimal point when it has none: `3` is an int literal
    /// in Dart and will not initialize a double.
    /// </summary>
    private static string Decimal(string rendered)
        => rendered.Contains('.') || rendered.Contains('E') || rendered.Contains('e')
            ? rendered
            : rendered + ".0";

    private static string Quote(string value)
    {
        var literal = new StringBuilder("'");

        foreach (var c in value ?? "")
        {
            if (c == '\'')
                literal.Append(@"\'");
            else if (c == '\\')
                literal.Append(@"\\");
            else if (c == '$')
                // A dollar starts an interpolation in a Dart string.
                literal.Append(@"\$");
            else if (c == '\n')
                literal.Append(@"\n");
            else if (c == '\r')
                literal.Append(@"\r");
            else if (c == '\t')
                literal.Append(@"\t");
            else if (c < 0x20)
                literal.Append(@"\u{").Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append('}');
            else
                literal.Append(c);
        }

        return literal.Append('\'').ToString();
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// A field name.
    ///
    /// camelCase, and escaped with a trailing underscore when it lands on a reserved
    /// word. Not a leading one: that would make the member private to its library.
    /// </summary>
    private string DartName(string name) => LanguageProfile.Dart.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for the names that are not members - an enum label, a constant,
    /// an accessor's per-table slot. They share a member's casing because Dart writes every
    /// identifier that way, not because they are members.
    /// </summary>
    private static string DartCamelName(string name) => LanguageProfile.Dart.MemberName(name.ToCamelCase());

}
