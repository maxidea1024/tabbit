using Tabbit.Models;
using Tabbit.Extensions;
using Tabbit.Helpers;
using Tabbit.Recipe;
using Tabbit.Targets;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Settings for the Lua target.
/// </summary>
public sealed class LuaRecipe : IOutputRecipe
{
    /// <summary>Directory the modules are written into. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Name of the accessor module, which is also the file it lands in. Lowercase, the way
    /// Lua names modules.
    /// </summary>
    public string AccessorName { get; set; } = "tables";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    ///
    /// It compares the served manifest with the local copy and fetches what changed, so
    /// a program can take new data without being redeployed. The HTTP itself is the
    /// consumer's - the updater takes a fetch function - and the hash, the directories
    /// and the backoff wait come from tabbit.native. Off by default: one that ships its
    /// data alongside its code has no use for it.
    /// </summary>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>
    /// Whether generated files this run did not write are removed from <see cref="Path"/>.
    /// </summary>
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
/// Emits Lua modules: one per table, per enum and per constant set, the accessor, the
/// reader runtime, and the native C module's source beside it.
///
/// Two runtimes read the output - LuaJIT 2.1 and Lua 5.3+ - and three decisions shape
/// everything here: rows are plain tables behind a strict metatable, so a misspelled
/// field is an error at the access instead of a silent nil; a keyword-named field keeps
/// its name and is reached with bracket syntax; and nothing is global - every file
/// returns a table and finds its siblings through a relative require prefix.
/// spec/lua-language-support.md.
/// </summary>
[TabbitTarget("lua", TargetKind.CodeGeneration, Order = 87)]
public class LuaCodeGenerator : CodeGenerator<LuaRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Run` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private LuaRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Camel;

    protected override bool SupportsNestedFields => true;
    protected override bool SupportsDeepNestedFields => true;
    protected override bool SupportsOptionalFields => true;

    /// <summary>`findByStageAndSlot(stageKey, slotKey)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;
    protected override bool SupportsOptionalElements => true;

    /// <summary>The prefix pattern of a file at the output root: strip one component.</summary>
    private const string RootPatternTop = "^(.-)[^%.]*$";

    /// <summary>The pattern of a file one directory deep: strip two components.</summary>
    private const string RootPatternDeep = "^(.-)[^%.]+%.[^%.]+$";

    protected override void Run(TargetContext context, LuaRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Camel, "lua");

        Generate();
        WriteBinaryReaderRuntime();
    }

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Lua into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        Write(_recipe.AccessorName + ".lua", "lua-accessor.sbn", new LuaPartView
        {
            RootPattern = RootPatternTop,
            // And the discriminator of every column reaching several tables: linking compares
            // against it, so the accessor names that module as well as the table ones.
            // spec/multi-target-accessors.md.
            Requires = _model.Tables.Select(TableRequire).ToList(),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table names the enums its fields are typed with. Not the tables it
            // references: resolution happens in the accessor, and requiring them here
            // would turn two tables pointing at each other into a require cycle.
            Write(System.IO.Path.Combine("tables", TableModule(pair.model) + ".lua"),
                "lua-table.sbn", new LuaPartView
                {
                    RootPattern = RootPatternDeep,
                    Requires = TypeDependencies.EnumsNamedBy(pair.model)
                                                   .Select(EnumRequire).ToList(),
                    AccessorModule = _recipe.AccessorName,
                    Table = pair.rendered,
                });
        }

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            Write(System.IO.Path.Combine("enums", EnumModule(pair.model) + ".lua"),
                "lua-enum.sbn", new LuaPartView
                {
                    RootPattern = RootPatternDeep,
                    Requires = Array.Empty<string>(),
                    Enumm = pair.rendered,
                });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            Write(System.IO.Path.Combine("constants", ConstantsModule(pair.model) + ".lua"),
                "lua-constants.sbn", new LuaPartView
                {
                    RootPattern = RootPatternDeep,
                    Requires = TypeDependencies.EnumsNamedBy(pair.model)
                                                   .Select(EnumRequire).ToList(),
                    Set = pair.rendered,
                });
        }
    }

    private void Write(string filename, string templateName, LuaPartView view)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, filename));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    // ------------------------------------------------------- module layout

    private static string TableModule(Table table) => table.Name.ToSnakeCase() + "_table";
    private static string EnumModule(Models.Enum enumm) => "enum_" + enumm.Name.ToSnakeCase();
    private static string ConstantsModule(ConstantSet set) => "const_" + set.Name.ToSnakeCase();

    private static string TableRequire(Table table)
        => $"local {table.Name.ToPascalCase()}Table = require(_root .. \"tables.{TableModule(table)}\")";

    private static string EnumRequire(Models.Enum enumm)
        => $"local {enumm.Name.ToPascalCase()} = require(_root .. \"enums.{EnumModule(enumm)}\")";

    private void WriteBinaryReaderRuntime()
    {
        string runtime = System.IO.Path.Combine(_recipe.Path, "tabbit");

        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Lua.tcb_reader.lua",
            System.IO.Path.Combine(runtime, "tcb_reader.lua"));

        // Both numeric backends, whichever runtime ends up loading the output.
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Lua.tcb_ops_jit.lua",
            System.IO.Path.Combine(runtime, "tcb_ops_jit.lua"));

        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Lua.tcb_ops_53.lua",
            System.IO.Path.Combine(runtime, "tcb_ops_53.lua"));

        // The native module's source, always: it is the encryption, MAC and manifest-hash
        // path, and shipping the .c beside the reader is what lets a project turn those on
        // by compiling one file. A project reading plain files ignores it.
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Lua.tabbit_native.c",
            System.IO.Path.Combine(runtime, "native", "tabbit_native.c"));

        // Asked for rather than assumed. It reaches the network - through the fetch its
        // caller hands it - and it is of no use to a program that ships its data
        // alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Lua.updater.lua",
                System.IO.Path.Combine(runtime, "updater.lua"));
        }
    }

    // --------------------------------------------------------------- view

    private LuaFileView BuildView() => new LuaFileView
    {
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private LuaEnumView BuildEnum(Models.Enum enumm) => new LuaEnumView
    {
        Name = enumm.Name.ToPascalCase(),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select(label => new LuaEnumLabelView
        {
            Key = Key(LuaCamelName(label.Name)),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
        }).ToList(),
    };

    private LuaConstantSetView BuildConstantSet(ConstantSet constantSet) => new LuaConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new LuaConstantView
        {
            Key = Key(LuaCamelName(constant.Name)),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private LuaTableView BuildTable(Table table)
    {
        var fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList();

        // The record's declared field names - the same list Python's `__slots__` carries.
        // A reference contributes its key as well as its value, and an optional column its
        // presence flag; a name outside this list is a typo the strict metatable reports.
        var known = new List<string>();
        var annotations = new List<string>();

        foreach (var sf in table.SerialFields)
        {
            if (sf.IsRef)
            {
                // Two fields: the keys off the wire, and what the linking pass resolved
                // them to. A dotted reference hands back a value rather than a row, so
                // there the column's own name stays on the value and the keys take the
                // `Index` one. spec/reference-surface-naming.md sections 4, 5 and 9.
                string keyName = RefIndexName(sf.FirstField!, sf.Name);
                string rowName = RefRowName(sf.FirstField!, sf.Name);

                known.Add(keyName);
                annotations.Add($"---@field {keyName} "
                    + (sf.IsArray ? KeyAnnotation(sf.FirstField!.RefKeyType) + "[]"
                                  : KeyAnnotation(sf.FirstField!.RefKeyType)));

                known.Add(rowName);
                annotations.Add(FieldAnnotation(table, sf, rowName));
            }
            else
            {
                known.Add(LuaName(sf.Name));
                annotations.Add(FieldAnnotation(table, sf));
            }

            if (sf.RowMayBeAbsent)
            {
                known.Add(PresenceName(sf));
                annotations.Add($"---@field {PresenceName(sf)} boolean");
            }

            if (sf.ElementMayBeAbsent)
            {
                known.Add(ElementPresenceName(sf));
                annotations.Add($"---@field {ElementPresenceName(sf)} boolean[]");
            }
        }


        return new LuaTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            Indexes = Indexes(table),
            RecordFieldNames = QuotedList(known),
            TableFieldNames = QuotedList(
                new[] { "records" }.Concat(Indexes(table).Select(index => index.MapName)).ToList()),
            Annotations = annotations,
            Fields = fields,

            // A separate list, because declaring a field is per field and reading is per
            // column - and a record group is one column per member of it.
            Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
        };
    }

    private IReadOnlyList<LuaIndexView> Indexes(Table table)
        => KeyPlans.Of(table).Select(plan =>
        {
            string suffix = plan.Suffix(name => name.ToPascalCase(), "And");

            var components = plan.Components.Select(component => new KeyComponentView
            {
                Param = KeyComponentView.ParamOf(component.Name).ToCamelCase(),
                Type = "",
                Member = Access(LuaName(component.Name)),
                Kind = KeyComponentView.KindOf(component.FirstField!.ElementType),
            }).ToList();

            string args = string.Join(", ", components.Select(component => component.Param));

            return new LuaIndexView
            {
                Access = Access(LuaName(plan.Only.Name)),
                Suffix = suffix,
                MapName = "by" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),

                // A composite key is already the text the map is keyed by, so there is no
                // int64 arriving at the subscript for the int64 normalization to catch.
                NormalizesInt64 = !plan.IsComposite && KeyIsInt64(plan.Only.ElementType),

                IsComposite = plan.IsComposite,
                Components = components,

                Params = plan.IsComposite ? args : "key",

                Argument = plan.IsComposite
                    ? "keyOf" + suffix + "(" + args + ")"
                    : "key",

                ValueFormat = plan.IsComposite
                    ? "(" + string.Join(", ", components.Select(_ => "%s")) + ")"
                    : "%s",

                ValueArgs = plan.IsComposite
                    ? string.Join(", ", components.Select(c => "tostring(" + c.Param + ")"))
                    : "tostring(key)",
            };
        }).ToList();

    /// <summary>
    /// Whether a key type is an int64 - which is FFI cdata under LuaJIT, where a table
    /// key compares by identity rather than value, so the map is keyed by the decimal
    /// string in both runtimes instead.
    /// </summary>
    private static bool KeyIsInt64(ValueType type)
        => type is ValueType.Int64 or ValueType.DateTime or ValueType.TimeSpan;

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
            ValueType.String => "~= ''",
            ValueType.Uuid => "~= tcb.UUID_EMPTY",
            _ => "~= 0",
        };


    private LuaFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        var initializers = Initializers(sf).ToList();

        // false until the read says otherwise, so a file that does not carry the column
        // leaves the field absent rather than claiming a value it never got.
        if (sf.RowMayBeAbsent)
            initializers.Add($"{Key(PresenceName(sf))} = false,");

        // Empty until the read fills it: an index into an empty list answers nil, and
        // `hasXAt` should be asked first anyway.
        if (sf.ElementMayBeAbsent)
            initializers.Add($"{Key(ElementPresenceName(sf))} = {{}},");

        return new LuaFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Initializers = initializers,
            IsRecord = false,
        };
    }

    private List<LuaRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<LuaRecordTypeView> declared)
    {
        var result = new List<LuaRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                result.Add(new LuaRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),
                    Initializers = MemberInitializers(member),
                });

                continue;
            }

            // A level below. The type name carries the path so two records each holding a
            // `Position` do not name one constructor twice.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new LuaRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record{Access(LuaName(group.Name))}",
                FieldNames = QuotedList(MemberFieldNames(member.Members)),
                Annotations = member.Members.SelectMany(m => MemberAnnotations(m, typeName)).ToList(),
            });

            result.Add(new LuaRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Initializers = new[] { $"{Key(LuaName(member.Name))} = new{typeName}()," },
            });
        }

        return result;
    }

    private LuaFieldView BuildRecordField(Table table, SerialField sf)
    {
        string entry = RecordTypeName(table, sf);

        // Innermost first, and required rather than tidy: a constructor names the level
        // below, and that local has to exist by the time the line runs.
        var recordTypes = new List<LuaRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, entry, table, sf, recordTypes);

        recordTypes.Add(new LuaRecordTypeView
        {
            TypeName = entry,
            Members = members,
            IsOutermost = true,
            Owner = $"{table.Name.ToPascalCase()}Record{Access(LuaName(sf.Name))}",
            FieldNames = QuotedList(MemberFieldNames(sf.Members)),
            Annotations = sf.Members.SelectMany(m => MemberAnnotations(m, entry)).ToList(),
        });

        // A list with its elements already made, where the length is the sheet's column
        // count. A trimmed group starts empty, because its length is the row's. An array
        // of arrays needs no element type: the outer level has no name for one to belong
        // to, so an inner list is what an element is. spec/nested-multi-level.md.
        string initializer;

        if (sf.MembersAreAnonymous)
        {
            string inner = string.Join(", ", sf.Members.Select(
                _ => $"tcb.repeated({sf.RecordElementCount}, {MemberDefault(sf.Members[0])})"));

            initializer = $"{Key(LuaName(sf.Name))} = {{ {inner} }},";
        }
        else if (sf.IsArray)
        {
            initializer = table.TrimTrailingArrayElements
                ? $"{Key(LuaName(sf.Name))} = {{}},"
                : $"{Key(LuaName(sf.Name))} = tcb.filledArray({sf.RecordElementCount}, new{entry}),";
        }
        else
        {
            initializer = $"{Key(LuaName(sf.Name))} = new{entry}(),";
        }

        return new LuaFieldView
        {
            // A record has no header cell of its own, so the first member's column comment
            // is the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),
            Initializers = new[] { initializer },
            IsRecord = true,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            RecordTypes = recordTypes,
        };
    }

    private LuaColumnView BuildColumn(Table table, WireColumn wire)
    {
        string kind = ReadKind(wire);
        string group = LuaName(wire.Group.Name);
        string groupAccess = Access(group);

        // The member path in its value form, and in its key form - where a reference
        // member's last piece becomes the `Index` field the wire's key lands in.
        string memberAccess = wire.Member is null
            ? ""
            : string.Concat(wire.MemberPath.Select(part => Access(LuaName(part))));

        string memberKeyAccess = memberAccess;

        if (wire.Member is not null && wire.IsRef)
        {
            var path = wire.MemberPath.ToList();
            memberKeyAccess = string.Concat(
                path.Take(path.Count - 1).Select(part => Access(LuaName(part))))
                + Access(RefIndexName(wire.TagCarrier, path[^1]));
        }

        string memberTarget = wire.IsRef ? memberKeyAccess : memberAccess;

        // The whole assignment targets, loop variables baked in - see LuaColumnView.
        string scalarTarget = wire.Member is null
            ? (wire.IsRef ? "record" + Access(RefIndexName(wire.TagCarrier, wire.Group.Name)) : "record" + groupAccess)
            : "record" + groupAccess + memberTarget;

        string elementTarget = kind switch
        {
            "record_var" => $"record{groupAccess}[element]{memberTarget}",
            "record_member_var" => $"record{groupAccess}{memberTarget}[element]",
            "array_of_arrays_member" => $"record{groupAccess}[{wire.MemberAt + 1}][element]",
            _ => "",
        };

        // The list those elements go in, for the two kinds that build it per row. The group
        // is not it: a member that is the array owns its own list, and one inner level of an
        // array of arrays is a slot of the outer one.
        string elementContainer = kind switch
        {
            "record_member_var" => $"record{groupAccess}{memberTarget}",
            "array_of_arrays_member" => $"record{groupAccess}[{wire.MemberAt + 1}]",
            _ => "",
        };

        string valuesTarget = kind switch
        {
            "var_array" or "serial" => "record" + groupAccess,

            // A reference array reads keys, so what the read fills is the key list and the
            // value list is cleared for the linking pass. Trimmed or not, only the length
            // differs. spec/variable-length-record-arrays.md.
            "serial_ref" or "var_array_ref" => "record" + Access(RefIndexName(wire.TagCarrier, wire.Group.Name)),
            _ => "",
        };

        return new LuaColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            Kind = kind,
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            ScalarTarget = scalarTarget,
            ElementTarget = elementTarget,
            ValuesTarget = valuesTarget,
            // The rows the linking pass fills go under the derived name; the keys read
            // above wear the column's own. spec/reference-surface-naming.md sections 4 and 9.
            SecondaryClear = kind is "serial_ref" or "var_array_ref"
                ? $"record{Access(RefRowName(wire.TagCarrier, wire.Group.Name))} = {{}}"
                : "",
            GroupTarget = "record" + groupAccess,
            ElementContainer = elementContainer,
            RecordConstructor = wire.Group.IsRecord ? "new" + RecordTypeName(table, wire.Group) : "",
            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,
            ReadScalar = UsesCursor(wire) ? CursorReadExpression(wire) : ReadExpression(wire),
            ReadElement = UsesCursor(wire) ? CursorReadExpression(wire) : ReadExpression(wire),
            LengthRead = UsesCursor(wire)
                ? "local elementCount = cursor:nextLength()"
                : "local elementCount = reader:readCounter32()",
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceTarget = "record" + Access(PresenceName(wire.Group)),
            ElementPresenceTarget = wire.HasOptionalElements
                ? "record" + Access(ElementPresenceName(wire.Group)) : "",
            EmptyTarget = "record" + groupAccess,
            EmptyValue = EmptyValue(wire),
        };
    }

    private static string RecordTypeName(Table table, SerialField sf)
        => table.Name.ToPascalCase() + sf.Name.ToPascalCase() + "Entry";

    private string PresenceName(SerialField sf)
        => sf.IsRecord ? "" : LuaName("has_" + sf.Name);

    private string ElementPresenceName(SerialField sf)
        => sf.IsRecord ? "" : LuaName("has_" + sf.Name + "_at");

    /// <summary>The field a reference's stored key lands in: the column's own name.</summary>
    /// <remarks>
    /// The column's name is the key's, because the key is what the cell holds - the row is
    /// linked after loading and takes a derived name.
    /// spec/reference-surface-naming.md sections 4 and 5. Never bracketed: the name
    /// out of keyword-hood, so the composite is always a plain identifier.
    /// </remarks>
    private string RefIndexName(Models.Field field, string name)
        => ResolvesToRow(field) ? LuaName(name) : LuaName(name) + "Index";

    /// <summary>
    /// The field a reference resolves into: the derived name for a whole row, and the
    /// column's own for a dotted reference - which hands back a value, so the name the
    /// sheet wrote is what it belongs on. spec/reference-surface-naming.md section 9.
    /// </summary>
    private string RefRowName(Models.Field field, string name)
        => ResolvesToRow(field)
            ? LuaName(RowAccessorName(field.ResolvedRefTable!.Name, name))
            : LuaName(name);

    private IReadOnlyList<string> MemberInitializers(RecordMember member)
    {
        string key = Key(LuaName(member.Name));

        if (member.IsRef)
        {
            key = Key(RefIndexName(member.FirstField!, member.Name));
            string keyDefault = RefKeyDefault(member.FirstField!.RefKeyType);

            // The resolved row starts nil - a nil table entry is no entry, so only the
            // key gets a line; the strict metatable's declared list is what keeps the
            // value field readable. spec/references-in-records.md.
            string rowKey = Key(RefRowName(member.FirstField!, member.Name));

            return member.IsArray
                ? new[]
                {
                    $"{rowKey} = {{}},",
                    $"{key} = tcb.repeated({member.Fields.Count}, {keyDefault}),",
                }
                : new[] { $"{key} = {keyDefault}," };
        }


        return member.IsArray
            ? new[] { $"{key} = tcb.repeated({member.Fields.Count}, {MemberDefault(member)})," }
            : new[] { $"{key} = {MemberDefault(member)}," };
    }


    private static string RefKeyDefault(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "\"\"",
            ValueType.Uuid => "tcb.UUID_EMPTY",
            _ => "0",
        };

    private LuaRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string group = Access(LuaName(wire.Group.Name));

        var path = wire.MemberPath.ToList();
        var refTable = wire.TagCarrier.ResolvedRefTable;

        // The member's own name is the key's; the row takes the derived one.
        // spec/reference-surface-naming.md sections 4 and 5.
        string memberKey = string.Concat(
            path.Take(path.Count - 1).Select(part => Access(LuaName(part))))
            + (path.Count > 0 ? Access(RefIndexName(wire.TagCarrier, path[^1])) : "");

        string member = path.Count > 0
            ? string.Concat(path.Take(path.Count - 1).Select(part => Access(LuaName(part))))
              + Access(RefRowName(wire.TagCarrier, path[^1]))
            : Access(RefRowName(wire.TagCarrier, wire.Group.Name));

        string groupRow = path.Count > 0 ? group : Access(RefRowName(wire.TagCarrier, wire.Group.Name));
        string memberRow = path.Count > 0 ? member : "";

        bool isArray = wire.IsArray;

        string access = !isArray || wire.Group.MembersAreArrays
            ? $"record{groupRow}{memberRow}"
            : $"record{groupRow}[i]{memberRow}";
        string key = !isArray || wire.Group.MembersAreArrays
            ? $"record{group}{memberKey}"
            : $"record{group}[i]{memberKey}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[i]" : "";

        return new LuaRecordReferenceView
        {
            Access = access + subscript,
            Key = key + subscript,

            // Whichever list holds the elements. Its own length rather than the column
            // count, because a trimming group's rows differ in how many they carry.
            Range = isArray
                ? (wire.Group.MembersAreArrays ? key : $"record{group}")
                : "",

            RefTable = "loaded" + refTable!.Name.ToPascalCase(),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    private IReadOnlyList<string> MemberFieldNames(IEnumerable<RecordMember> members)
    {
        var result = new List<string>();

        foreach (var member in members)
        {
            if (member.IsLeaf && member.IsRef)
            {
                result.Add(RefIndexName(member.FirstField!, member.Name));
                result.Add(RefRowName(member.FirstField!, member.Name));
            }
            else
            {
                result.Add(LuaName(member.Name));
            }
        }

        return result;
    }

    private string MemberDefault(RecordMember member)
    {
        switch (member.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "tcb.UUID_EMPTY";
            case ValueType.Enum: return EnumDefault(member.FirstField!.Enum);
            default: return "0";
        }
    }

    /// <summary>
    /// The value an enum-typed field starts at: the zero label when there is one, and the
    /// first otherwise - the same fallback every generated enum agrees on.
    /// </summary>
    private static string EnumDefault(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return fallback.Value.ToString(CultureInfo.InvariantCulture);
    }

    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsArray)
            return "{}";

        // The resolved field points at the target row, and absence there is what nil
        // says - assigning it removes the entry, and the strict metatable's declared
        // list keeps the read a nil rather than an error.
        if (wire.ElementType == ValueType.ForeignRecord)
            return "nil";

        switch (wire.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "tcb.UUID_EMPTY";
            case ValueType.Enum: return EnumDefault(wire.TagCarrier.Enum);
            default: return "0";
        }
    }

    private static bool UsesCursor(WireColumn wire)
    {
        // Uuid is the exception, and the same one it has always been: no encoding
        // applies to it, so it has no cursor path to reach.
        if (wire.ElementType == ValueType.Uuid)
            return false;

        if (wire.IsArray)
            return true;

        if (wire.IsRef)
            return wire.RefKeyType != ValueType.Uuid;

        switch (wire.ElementType)
        {
            case ValueType.Int32:
            case ValueType.Int64:
            case ValueType.Double:
            case ValueType.Float:
            case ValueType.Bool:
            case ValueType.Enum:
            case ValueType.String:
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return true;

            default:
                return false;
        }
    }

    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"local cursor = tcb.newCursor(reader, column, count, \"{tableName}.{wire.Name}\")"
            : "";

    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        if (wire.IsArray)
            return "";

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

    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string group = LuaName(wire.Group.Name);
        string groupAccess = Access(group);

        string memberAccess = wire.Member is null
            ? ""
            : string.Concat(wire.MemberPath.Select(part => Access(LuaName(part))));

        // Only the stored key is on the wire; the value is filled in once every table is
        // loaded, exactly as the per-row shape does it.
        if (wire.IsRef)
        {
            if (wire.Member is null)
                return $"records[i]{Access(RefIndexName(wire.TagCarrier, wire.Group.Name))} = value";

            var path = wire.MemberPath.ToList();
            string memberKey = string.Concat(
                path.Take(path.Count - 1).Select(part => Access(LuaName(part))))
                + Access(RefIndexName(wire.TagCarrier, path[^1]));

            return $"records[i]{groupAccess}{memberKey} = value";
        }

        return $"records[i]{groupAccess}{memberAccess} = value";
    }

    private static string CursorReadExpression(WireColumn wire)
    {
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => "cursor:nextI64()",
                ValueType.String => "cursor:nextString()",
                _ => "cursor:nextI32()",
            };
        }

        switch (wire.ElementType)
        {
            // An enum is its integer value: Lua declares no enum type, and the generated
            // enum module is a table of these values under their names.
            case ValueType.Enum:
            case ValueType.Int32: return "cursor:nextI32()";
            case ValueType.Int64: return "cursor:nextI64()";
            case ValueType.Double: return "cursor:nextF64()";
            case ValueType.Float: return "cursor:nextF32()";
            case ValueType.Bool: return "cursor:nextBool()";
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "cursor:nextI64()";

            default: // String; UsesCursor admits nothing else here.
                return "cursor:nextString()";
        }
    }

    private IReadOnlyList<string> Initializers(SerialField sf)
    {
        string key = Key(LuaName(sf.Name));

        if (sf.IsRef)
        {
            key = Key(RefIndexName(sf.FirstField!, sf.Name));

            // The resolved row starts nil, which in Lua is no entry at all - so only the
            // key list gets a line, and the strict metatable's declared list is what
            // keeps `row.owner` a nil rather than an error.
            // The column's own name is the key's, and the row it resolves to takes the
            // derived one. spec/reference-surface-naming.md sections 4 and 5.
            string rowKey = Key(RefRowName(sf.FirstField!, sf.Name));

            return sf.IsArray
                ? new[] { $"{key} = {{}},", $"{rowKey} = {{}}," }
                : new[] { $"{key} = 0," };
        }

        if (sf.IsArray)
            return new[] { $"{key} = {{}}," };

        return new[] { $"{key} = {DefaultValue(sf)}," };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "tcb.UUID_EMPTY";
            case ValueType.Enum: return EnumDefault(sf.FirstField!.Enum);
            default: return "0";
        }
    }

    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "tcb.KIND_ARRAY" : "tcb.KIND_SCALAR";


        string accepted;

        if (wire.IsRef)
        {
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "tcb.ELEMENT_STRING",
                ValueType.Int64 => "tcb.ELEMENT_I64, tcb.ELEMENT_I32, tcb.ELEMENT_VARINT",
                ValueType.Uuid => "tcb.ELEMENT_UUID",
                _ => "tcb.ELEMENT_I32",
            };
        }
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "tcb.ELEMENT_I32, tcb.ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "tcb.ELEMENT_I64, tcb.ELEMENT_I32, tcb.ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "tcb.ELEMENT_F64, tcb.ELEMENT_F32, tcb.ELEMENT_I32"; break;
                case ValueType.Float: accepted = "tcb.ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "tcb.ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "tcb.ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "tcb.ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "tcb.ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "tcb.ELEMENT_I64"; break;

                default:
                    throw new TabbitDefectException($"The lua generator cannot check type `{wire.Type}`.");
            }
        }

        string nullable = wire.IsNullable ? "true" : "false";
        string elements = wire.HasOptionalElements ? ", true" : "";

        return $"tcb.checkColumn(column, \"{tableName}.{wire.Name}\", {kind}, "
            + $"{nullable}, {{ {accepted} }}{elements})";
    }

    private static string ReadKind(WireColumn wire)
    {
        if (wire.Member is not null)
        {
            if (!wire.IsArray)
                return "scalar";

            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_var" : "record_var";
        }

        if (wire.IsArray)
            // A trimmed array of references: the length is the row's, and the key still goes
            // in the array beside the values. Read as a plain `var_array` it put the keys where
            // the resolved rows belong, and the linking pass then found nothing to resolve -
            // silently, because this language does not type them apart. Nothing held the shape:
            // `foreign[]` is refused, so it is only reachable through a folded group with
            // trimming on. spec/variable-length-record-arrays.md.
            return wire.IsRef ? "var_array_ref" : "var_array";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private LuaAccessorView BuildAccessor() => new LuaAccessorView
    {
        Name = _recipe.AccessorName,
        FileExtension = _recipe.BinaryTableFileExtension,
        FieldNames = QuotedList(_model.Tables.Select(table => LuaCamelName(table.Name)).ToList()),

        Tables = _model.Tables.Select(table => new LuaTableSlotView
        {
            Access = Access(LuaCamelName(table.Name)),
            Key = Key(LuaCamelName(table.Name)),
            Loaded = "loaded" + table.Name.ToPascalCase(),
            TableName = table.Name.ToPascalCase() + "Table",

            // Unescaped: this one names the file the exporter wrote.
            DataFileName = table.DataFileName,
        }).ToList(),

        CrossReferences = _model.Tables
            .Select(table => new
            {
                Table = table,
                Fields = table.SerialFields.Where(sf => sf.IsRef).ToList(),
                RecordFields = table.WireColumns
                                    .Where(wire => wire.Member is not null && wire.IsRef)
                                    .Select(BuildRecordReference)
                                    .ToList(),


            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                         )
            .Select(x => new LuaCrossReferenceView
            {
                Loaded = "loaded" + x.Table.Name.ToPascalCase(),
                RecordFields = x.RecordFields,
                Fields = x.Fields.Select(sf => new LuaReferenceFieldView
                {
                    Access = "record" + Access(RefRowName(sf.FirstField!, sf.Name)),
                    KeyAccess = "record" + Access(RefIndexName(sf.FirstField!, sf.Name)),
                    RefTable = "loaded" + sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase(),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target" + Access(LuaName(sf.FirstField!.ResolvedRefField!.Name)),
                    IsArray = sf.IsArray,
                }).ToList(),
            })
            .ToList(),
    };

    // ----------------------------------------------------------- rendering

    private string ReadExpression(WireColumn wire)
    {
        switch (wire.ElementType)
        {
            case ValueType.Enum:
                return "reader:readEnum()";

            case ValueType.ForeignRecord:
                return LanguageProfile.Lua.ReadCall(wire.RefKeyType);

            default: return LanguageProfile.Lua.ReadCall(wire.ElementType);
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
                return Int64Literal((long)constant.Value!);

            case ValueType.Float:
                return NumberLiteral(((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture));

            case ValueType.Double:
                return NumberLiteral(((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture));

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return Int64Literal(((DateTime)constant.Value!).Ticks);

            case ValueType.TimeSpan:
                return Int64Literal(((TimeSpan)constant.Value!).Ticks);

            case ValueType.Uuid:
                return "\"" + ((Guid)constant.Value!).ToString("D") + "\"";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return constant.Enum.Name.ToPascalCase() + Access(LuaCamelName(label.Name));
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", constant.Type),
                            ("Generator", "lua")));
        }
    }

    /// <summary>
    /// An int64 as source text: a plain literal inside the 2^53 both runtimes hold
    /// exactly, and through the parser beyond it - a LuaJIT literal is a double, and a
    /// constant that rounds is exactly the failure the bigint type exists to prevent.
    /// </summary>
    private static string Int64Literal(long value)
    {
        const long exact = 1L << 53;

        string digits = value.ToString(CultureInfo.InvariantCulture);

        return value >= -exact && value <= exact
            ? digits
            : $"tcb.int64FromString(\"{digits}\")";
    }

    /// <summary>
    /// A float or double as source text: the values Lua has no literal for by name, and
    /// a decimal point on whole numbers so 5.3+ keeps the float subtype.
    /// </summary>
    private static string NumberLiteral(string rendered)
    {
        if (rendered == "Infinity")
            return "math.huge";

        if (rendered == "-Infinity")
            return "-math.huge";

        if (rendered == "NaN")
            return "(0 / 0)";

        return rendered.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0 ? rendered : rendered + ".0";
    }

    // -------------------------------------------------------- annotations

    private string FieldAnnotation(Table table, SerialField sf, string? rename = null)
    {
        string name = rename ?? LuaName(sf.Name);

        if (sf.IsRecord)
        {
            string entry = sf.MembersAreAnonymous
                ? ScalarAnnotation(sf.Members[0].ElementType) + "[]"
                : RecordTypeName(table, sf);

            return $"---@field {name} {(sf.IsArray || sf.MembersAreAnonymous ? entry + "[]" : entry)}";
        }

        if (sf.IsRef)
        {
            // A dotted reference hands back one of the target's values rather than the row,
            // so what this column holds is that value's type.
            // spec/reference-surface-naming.md section 9.
            if (!ResolvesToRow(sf.FirstField!))
            {
                string value = ScalarAnnotation(sf.FirstField!.ResolvedRefField!.Type);

                return $"---@field {name} {(sf.IsArray ? value + "[]" : value)}";
            }

            string row = sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

            return sf.IsArray
                ? $"---@field {name} {row}[]"
                : $"---@field {name} {row}|nil";
        }

        string scalar = ScalarAnnotation(sf.ElementType);

        return $"---@field {name} {(sf.IsArray ? scalar + "[]" : scalar)}";
    }

    private IEnumerable<string> MemberAnnotations(RecordMember member, string prefix)
    {
        if (!member.IsLeaf || !member.IsRef)
        {
            yield return MemberAnnotation(member, prefix);
            yield break;
        }

        // The member's own name is the key's; the row takes the derived one.
        // spec/reference-surface-naming.md sections 4 and 5.
        string keyName = RefIndexName(member.FirstField!, member.Name);
        string rowName = RefRowName(member.FirstField!, member.Name);
        bool toRow = ResolvesToRow(member.FirstField!);
        string rowType = toRow
            ? member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record"
            : ScalarAnnotation(member.FirstField!.ResolvedRefField!.Type);
        string keyType = KeyAnnotation(member.FirstField!.RefKeyType);

        yield return member.IsArray
            ? $"---@field {keyName} {keyType}[]"
            : $"---@field {keyName} {keyType}";

        yield return member.IsArray
            ? $"---@field {rowName} {rowType}[]"
            : $"---@field {rowName} {(toRow ? rowType + "|nil" : rowType)}";
    }

    private string MemberAnnotation(RecordMember member, string prefix)
    {
        string name = LuaName(member.Name);

        if (!member.IsLeaf)
            return $"---@field {name} {prefix + member.Name.ToPascalCase()}";

        if (member.IsRef)
        {
            string row = member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

            return member.IsArray
                ? $"---@field {name} {row}[]"
                : $"---@field {name} {row}|nil";
        }

        string scalar = ScalarAnnotation(member.ElementType);

        return $"---@field {name} {(member.IsArray ? scalar + "[]" : scalar)}";
    }

    private string ScalarAnnotation(ValueType type)
        => type == ValueType.Enum ? "integer" : LanguageProfile.Lua.ScalarTypeName(type);

    private static string KeyAnnotation(ValueType keyType)
        => keyType switch
        {
            ValueType.String or ValueType.Uuid => "string",
            _ => "integer",
        };

    // ------------------------------------------------------------- helpers

    /// <summary>A field name: camelCase, its spelling kept even on a keyword.</summary>
    private string LuaName(string name) => name.ToCase(_memberCase);

    /// <summary>
    /// The same spelling, for the names that are not members - an enum label, a constant,
    /// an accessor's per-table slot.
    /// </summary>
    private static string LuaCamelName(string name) => name.ToCamelCase();

    private static bool IsReserved(string name)
        => LanguageProfile.Lua.ReservedMemberNames.Contains(name);

    /// <summary>The name in table-constructor position: `hp` or `["end"]`.</summary>
    private static string Key(string name) => IsReserved(name) ? $"[\"{name}\"]" : name;

    /// <summary>The name in access position: `.hp` or `["end"]`.</summary>
    private static string Access(string name) => IsReserved(name) ? $"[\"{name}\"]" : "." + name;

    /// <summary>The names quoted and comma separated, for a strict metatable's list.</summary>
    private static string QuotedList(IReadOnlyList<string> names)
        => string.Join(", ", names.Select(name => $"\"{name}\""));

    private static string Quote(string value)
    {
        var literal = new StringBuilder("\"");

        foreach (var c in value ?? "")
        {
            if (c == '"')
                literal.Append("\\\"");
            else if (c == '\\')
                literal.Append(@"\\");
            else if (c == '\n')
                literal.Append(@"\n");
            else if (c == '\r')
                literal.Append(@"\r");
            else if (c == '\t')
                literal.Append(@"\t");
            else if (c < 0x20)
                // Three digits always: a shorter \d escape absorbs a digit that follows.
                literal.Append('\\').Append(((int)c).ToString("D3", CultureInfo.InvariantCulture));
            else
                literal.Append(c);
        }

        return literal.Append('"').ToString();
    }

    // `new`, and not the base one: comments land in `--` lines, which nothing needs
    // escaping for beyond splitting.
    private static new IReadOnlyList<string> CommentLines(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Array.Empty<string>();

        return comment.Replace("\r\n", "\n").Split('\n');
    }
}
