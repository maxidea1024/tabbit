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
/// Settings for the Ruby target.
/// </summary>
public sealed class RubyRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>Module every generated type is nested in.</summary>
    public string ModuleName { get; set; } = "GameData";

    /// <summary>Base name of the generated file, without its extension.</summary>
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
/// Emits one Ruby file holding every generated type, plus the binary reader beside it.
///
/// Enums are modules of integer constants rather than a class per label: the value is
/// what travels on the wire, comparisons against it are what consuming code does, and
/// a module of constants is what Ruby reaches for.
///
/// The shape lives in templates/ruby.sbn.
/// </summary>
[TabbitTarget("ruby", TargetKind.CodeGeneration, Order = 75)]
public class RubyCodeGenerator : CodeGenerator<RubyRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private RubyRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Snake;

    /// <summary>
    /// A record group generates a class and an array of it; a member column fills one of its
    /// attributes.
    /// </summary>
    /// <remarks>
    /// The last of the thirteen. Every target now takes a record, so the refusal that stood
    /// in for the ones that could not is gone.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a class defined before the element type, and the read reaches it with a
    /// longer member path. The constructor makes it, so every value inside it starts where a
    /// scalar member would. spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `has_{field}` attribute beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `nil`. Ruby would take a nil attribute and the check would read naturally, but nil
    /// is also what an unresolved reference is - and a string column reads a blank as `''`,
    /// so there would be two ways to say the same nothing. spec/optional-fields.md has the
    /// reasoning, which is the same one the twelve before it follow.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`find_by_stage_and_slot(stage_key, slot_key)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, RubyRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Snake, "ruby");

        Generate();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes a file per table, per enum and per constant set, plus the accessor.
    /// </summary>
    /// <remarks>
    /// The module is reopened in every file rather than nested from one place, which is how
    /// Ruby works: `module X` opens X whether or not something else already did.
    ///
    /// Requiring is the part that needs care. Ruby has no autoloader here, so the accessor
    /// requires every part - and a table requires the reader, because its `read` names it.
    /// File names are snake_case, as Ruby writes them.
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

        Log.Information($"Generating codes for Ruby into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        // Forward slashes, which `require_relative` takes on every platform, and no
        // extension, which is how Ruby spells it.
        var parts = new List<string> { "tabbit/tcb_reader" };

        parts.AddRange(view.Enums.Select(e => "enums/" + e.Name.ToSnakeCase()));
        parts.AddRange(view.ConstantSets.Select(s => "constants/" + s.Name.ToSnakeCase()));
        parts.AddRange(view.Tables.Select(t => "tables/" + t.TableName.ToSnakeCase()));

        Write(AccessorFile + ".rb", "ruby-accessor.sbn", new RubyPartView
        {
            AccessorName = AccessorType,
            ModuleName = _recipe.ModuleName,
            Requires = parts,
            Accessor = view.Accessor,
        });

        foreach (var table in view.Tables)
        {
            Write(System.IO.Path.Combine("tables", table.TableName.ToSnakeCase() + ".rb"),
                  "ruby-table.sbn", new RubyPartView
                  {
                      AccessorName = AccessorType,
                      ModuleName = _recipe.ModuleName,

                      // One directory down, and its `read` names the reader.
                      Requires = new[] { "../tabbit/tcb_reader" }.ToList(),
                      Table = table,
                  });
        }

        // An enum module and a constant module name nothing outside themselves.
        foreach (var enumm in view.Enums)
        {
            Write(System.IO.Path.Combine("enums", enumm.Name.ToSnakeCase() + ".rb"),
                  "ruby-enum.sbn", new RubyPartView
                  {
                      AccessorName = AccessorType,
                      ModuleName = _recipe.ModuleName,
                      Requires = Array.Empty<string>(),
                      Enumm = enumm,
                  });
        }

        foreach (var set in view.ConstantSets)
        {
            Write(System.IO.Path.Combine("constants", set.Name.ToSnakeCase() + ".rb"),
                  "ruby-constants.sbn", new RubyPartView
                  {
                      AccessorName = AccessorType,
                      ModuleName = _recipe.ModuleName,
                      Requires = Array.Empty<string>(),
                      Set = set,
                  });
        }
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
            "Tabbit.Runtime.Ruby.tcb_reader.rb",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "tcb_reader.rb"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Ruby.updater.rb",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "updater.rb"));
        }
    }

    // --------------------------------------------------------------- view

    private RubyFileView BuildView() => new RubyFileView
    {
        AccessorName = AccessorType,
        ModuleName = _recipe.ModuleName.ToPascalCase(),
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private RubyEnumView BuildEnum(Models.Enum enumm) => new RubyEnumView
    {
        Name = enumm.Name.ToPascalCase(),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select(label => new RubyEnumLabelView
        {
            Name = ConstantName(label.Name),
            Symbol = RubySnakeName(label.Name),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
        }).ToList(),
    };

    private RubyConstantSetView BuildConstantSet(ConstantSet constantSet) => new RubyConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new RubyConstantView
        {
            Name = ConstantName(constant.Name),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private RubyTableView BuildTable(Table table)
    {
        // A reference contributes its index as well as its value, and both are read
        // from outside the record when references are linked.
        var accessors = new List<string>();

        foreach (var sf in table.SerialFields)
        {
            // The column's name is the key's; the row takes the derived one.
            // spec/reference-surface-naming.md sections 4 and 5.
            accessors.Add(RubyName(sf.Name));

            if (sf.IsRef)
                accessors.Add(ResolvesToRow(sf.FirstField!)
                    ? RubyName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                    : RubyName(sf.Name) + "_index");

            if (sf.RowMayBeAbsent)
                accessors.Add(PresenceMember(sf));

            // And the per-element answer, which is an array rather than a flag.
            // spec/nullable-array-elements.md.
            if (sf.ElementMayBeAbsent)
                accessors.Add(ElementPresenceMember(sf));
        }


        return new RubyTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            Indexes = Indexes(table),
            AccessorNames = Symbols(accessors),
            Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),

            // A separate list, because declaring an attribute is per field and reading is
            // per column - and a record group is one column per member of it.
            Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
        };
    }

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<RubyIndexView> Indexes(Table table)
        => KeyPlans.Of(table).Select(plan =>
        {
            string suffix = plan.Suffix(name => name.ToSnakeCase(), "_and_");

            var components = plan.Components.Select(component => new KeyComponentView
            {
                Param = KeyComponentView.ParamOf(component.Name).ToSnakeCase(),
                Type = "",
                Member = RubyName(component.Name),
                Kind = KeyComponentView.KindOf(component.FirstField!.ElementType),
            }).ToList();

            string args = string.Join(", ", components.Select(component => component.Param));

            return new RubyIndexView
            {
                Member = RubyName(plan.Only.Name),
                Suffix = suffix,
                MapName = "@by_" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                IsComposite = plan.IsComposite,
                Components = components,

                Params = plan.IsComposite ? args : "key",

                Argument = plan.IsComposite
                    ? "self.class.key_of_" + suffix + "(" + args + ")"
                    : "key",

                ValueFormat = plan.IsComposite
                    ? "(" + string.Join(", ", components.Select(c => "#{" + c.Param + ".inspect}")) + ")"
                    : "#{key.inspect}",

                ValueArgs = plan.IsComposite ? args : "key",
            };
        }).ToList();

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `find_by_index`. The
    /// primary index is whatever the sheet put in the first column, and a sheet that
    /// calls it `Id` generates `find_by_id`.
    /// </remarks>
    private static string PrimaryLookup(Table? refTable)
        => "find_by_" + refTable!.SerialFields.First(sf => sf.IsIndexer).Name.ToSnakeCase();

    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// The key type's empty value means "points at nothing", and a multi-target column honours
    /// it in every language: the discriminator is a value a consumer reads.
    /// spec/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType == ValueType.String ? "!= ''" : "!= 0";


    private RubyFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = RubyName(sf.Name);
        bool nullable = sf.RowMayBeAbsent;

        var initializers = Initializers(sf, name).ToList();

        // False until the read says otherwise, so a file that does not carry the column
        // leaves the attribute absent rather than claiming a value it never got.
        if (nullable)
            initializers.Add($"@{PresenceMember(sf)} = false");

        // Empty until the read fills it: an index into an empty array is out of range, and
        // the answer there is that the element has a value.
        if (sf.ElementMayBeAbsent)
            initializers.Add($"@{ElementPresenceMember(sf)} = []");

        return new RubyFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Initializers = initializers,
            IsRecord = false,
            RecordTypeName = "",
            RecordAccessorNames = "",
            Members = Array.Empty<RubyRecordMemberView>(),
            IsNullable = nullable,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = PresenceMember(sf),
            ElementPresenceMember = ElementPresenceMember(sf),
        };
    }

    /// <summary>
    /// A record group: the class to declare for one element, and the attribute holding one
    /// or an array of them.
    /// </summary>
    /// <remarks>
    /// A class in the same module as the row, carrying the table's name, because every
    /// generated class sits in that one module.
    ///
    /// No reference members: a reference belongs to a member and the model refuses one there,
    /// so nothing here has the index array a reference would be carried as.
    /// </remarks>
    /// <summary>
    /// Members of one level of a record, declaring a class for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the classes it produces - innermost first, which Ruby requires rather than
    /// prefers: the constructor naming the level below resolves that name when it runs.
    /// spec/nested-multi-level.md.
    /// </remarks>
    private List<RubyRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<RubyRecordTypeView> declared)
    {
        var result = new List<RubyRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                result.Add(new RubyRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),

                    // The array is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    Initializers = MemberInitializers(member),
                });

                continue;
            }

            // A level below. The class name carries the path: every generated class sits in
            // one module, so two records each holding a `Position` would otherwise collide.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new RubyRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record#{RubyName(group.Name)}",
                AccessorNames = Symbols(MemberAccessorNames(member.Members)),
            });

            result.Add(new RubyRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Initializers = new[] { $"@{RubyName(member.Name)} = {typeName}.new" },
            });
        }

        return result;
    }

    private RubyFieldView BuildRecordField(Table table, SerialField sf)
    {
        string name = RubyName(sf.Name);
        string entry = RecordTypeName(table, sf);

        // Innermost first, so a class is defined before the constructor that names it runs.
        var recordTypes = new List<RubyRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, entry, table, sf, recordTypes);

        recordTypes.Add(new RubyRecordTypeView
        {
            TypeName = entry,
            Members = members,
            IsOutermost = true,
            Owner = $"{table.Name.ToPascalCase()}Record#{name}",
            AccessorNames = Symbols(MemberAccessorNames(sf.Members)),
        });

        // An array with its elements already made, where the length is the sheet's column
        // count. A trimmed group starts empty, because its length is the row's.
        // An array of arrays needs no element type: the outer level has no name for one to
        // belong to, so the inner array is what an element is. spec/nested-multi-level.md.
        string initializer = sf.MembersAreAnonymous
            ? $"@{name} = Array.new({sf.Members.Count}) {{ Array.new({sf.RecordElementCount}) "
              + $"{{ {MemberDefault(sf.Members[0])} }} }}"
            : sf.IsArray
                ? (table.TrimTrailingArrayElements
                    ? $"@{name} = []"
                    : $"@{name} = Array.new({sf.RecordElementCount}) {{ {entry}.new }}")
                : $"@{name} = {entry}.new";

        return new RubyFieldView
        {
            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,
            Initializers = new[] { initializer },
            IsRecord = true,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            RecordTypeName = entry,
            RecordAccessorNames = Symbols(MemberAccessorNames(sf.Members)),

            Members = members,
            RecordTypes = recordTypes,

            // A record group has no presence of its own: absence inside one is the array's
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
    /// columns each fill one attribute of the generated element type, which is the whole of
    /// the difference - see spec/nested-fields.md.
    /// </remarks>
    private RubyColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new RubyColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire)
                ? "element_count = cursor.next_length"
                : "element_count = reader.read_counter32",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = RubyName(wire.Group.Name),

            // A record's member column assigns one attribute of the element rather than the
            // member itself: `record.slot[j].id` instead of `record.slot[j]`.
            MemberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + RubyName(part))),
            // A reference member reads into the key beside the row it will resolve to, and the
            // suffix goes on the member rather than after the subscript, because a member that
            // is an array holds one key per element. spec/references-in-records.md.
            MemberRefSuffix = "",

            RowName = wire.IsRef && wire.TagCarrier.ResolvedRefTable is not null
                        && ResolvesToRow(wire.TagCarrier)
                ? (ResolvesToRow(wire.TagCarrier)
                    ? RubyName(RowAccessorName(wire.TagCarrier.ResolvedRefTable.Name, wire.Group.Name))
                    : RubyName(wire.Group.Name) + "_index")
                : "",
            MemberAt = wire.MemberAt,

            RecordTypeName = wire.Group.IsRecord ? RecordTypeName(table, wire.Group) : "",
            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,
            ReadScalar = ScalarReadExpression(wire),
            ReadElement = ElementReadExpression(wire),
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
    /// Every generated class sits in one module, so two tables each holding a `Slot` group
    /// would be the same constant twice.
    /// </remarks>
    private static string RecordTypeName(Table table, SerialField sf)
        => table.Name.ToPascalCase() + sf.Name.ToPascalCase() + "Entry";

    /// <summary>
    /// The attribute a nullable column's presence lands in, without its `@`.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever reads
    /// it, and the model has already required its columns to agree about being optional.
    /// </remarks>
    /// <summary>The member holding which of an array's elements have a value.</summary>
    private string ElementPresenceMember(SerialField sf)
        => sf.IsRecord ? "" : RubyName("has_" + sf.Name + "_at");

    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : RubyName("has_" + sf.Name);

    /// <summary>What one record member starts at, for the same reason an ordinary one does.</summary>
    /// <summary>
    /// What one leaf member of a record element starts as.
    /// </summary>
    /// <remarks>
    /// Two lines for a reference member rather than one: the row it resolved to, and the key
    /// that came off the wire. Both inside the element, because a group may hold more than one
    /// reference and a name built from the group and the target would collide the moment two
    /// members point at one table.
    ///
    /// Nothing for whether it resolved - the row is nil until the linking pass fills it, and
    /// that is the same answer a reference outside a record gives.
    /// spec/references-in-records.md.
    /// </remarks>
    private IReadOnlyList<string> MemberInitializers(RecordMember member)
    {
        string name = RubyName(member.Name);

        if (member.IsRef)
        {
            string key = RefKeyDefault(member.FirstField!.RefKeyType);

            // The member's own name is the key's; the row takes the derived one.
            // spec/reference-surface-naming.md sections 4 and 5.
            bool toRow = ResolvesToRow(member.FirstField!);
                    string rowName = toRow
                        ? RubyName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                        : RubyName(member.Name);
                    string keyName = toRow ? RubyName(member.Name) : RubyName(member.Name) + "_index";

            return member.IsArray
                ? new[]
                {
                    $"@{keyName} = Array.new({member.Fields.Count}) {{ {key} }}",
                    $"@{rowName} = Array.new({member.Fields.Count})",
                }
                : new[]
                {
                    $"@{keyName} = {key}",
                    $"@{rowName} = nil",
                };
        }


        // The array is the member's when the group is one record - same columns, same wire,
        // and only which of the two owns it differs.
        return member.IsArray
            ? new[] { $"@{name} = Array.new({member.Fields.Count}) {{ {MemberDefault(member)} }}" }
            : new[] { $"@{name} = {MemberDefault(member)}" };
    }


    /// <summary>What a stored key holds before a row is read.</summary>
    private static string RefKeyDefault(ValueType keyType)
        => keyType switch
        {
            ValueType.String or ValueType.Uuid => "\"\"",
            _ => "0",
        };

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// No resolution flag: an unresolved row stays as it started, which is how this output
    /// already answers that for a reference outside a record. What the loop walks says which
    /// of the three record shapes this is - the group's list, the member's, or neither.
    /// spec/references-in-records.md.
    /// </remarks>
    private RubyRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = RubyName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + RubyName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsArray;

        string rowLeaf = wire.Member is not null
            ? RubyName(RowAccessorName(refTable!.Name, wire.MemberPath[^1]))
            : RubyName(RowAccessorName(refTable!.Name, wire.Group.Name));

        string rowMember = wire.Member is not null
            ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                .Select(part => "." + RubyName(part))) + "." + rowLeaf
            : "";

        string rowPath = wire.Member is not null
            ? (!isArray || wire.Group.MembersAreArrays
                ? $"record.{name}{rowMember}"
                : $"record.{name}[i]{rowMember}")
            : $"record.{rowLeaf}";

        string path = !isArray || wire.Group.MembersAreArrays
            ? $"record.{name}{member}"
            : $"record.{name}[i]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[i]" : "";

        return new RubyRecordReferenceView
        {
            Access = rowPath + subscript,
            Key = path + subscript,

            // Whichever array holds the elements. Its own length rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Range = isArray
                ? (wire.Group.MembersAreArrays
                    ? $"{path}.each_index"
                    : $"record.{name}.each_index")
                : "",

            RefTable = RubyName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    /// <summary>
    /// The `attr_accessor` symbols one level of a record declares.
    /// </summary>
    /// <remarks>
    /// A reference member is two rather than one: the row it resolved to, and the key that
    /// came off the wire. Built from the same list the initializers are, so a member cannot be
    /// assigned by the read and left without a writer. spec/references-in-records.md.
    /// </remarks>
    private IReadOnlyList<string> MemberAccessorNames(IEnumerable<RecordMember> members)
    {
        var result = new List<string>();

        foreach (var member in members)
        {
            result.Add(RubyName(member.Name));

            if (member.IsLeaf && member.IsRef)
                result.Add(ResolvesToRow(member.FirstField!)
                    ? RubyName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                    : RubyName(member.Name) + "_index");
        }

        return result;
    }

    private string MemberDefault(RecordMember member)
    {
        switch (member.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "Tabbit::Uuid.new";

            // An enum falls through to 0, exactly as an ordinary field of one does here.
            default: return "0";
        }
    }

    /// <summary>
    /// What an absent row's attribute is set back to, so the binary path lands where the
    /// JSON one does.
    /// </summary>
    /// <remarks>
    /// The attribute's own shape rather than its element's: an optional array holds an
    /// array, and its empty value is an empty one rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsArray)
            return "[]";

        // The resolved attribute points at the target row, and absence there is what nil
        // says.
        if (wire.ElementType == ValueType.ForeignRecord)
            return "nil";

        switch (wire.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "Tabbit::Uuid.new";
            default: return "0";
        }
    }

    private IReadOnlyList<string> Initializers(SerialField sf, string name)
    {
        if (sf.IsRef)
        {
            bool toRow = ResolvesToRow(sf.FirstField!);
            string rowName = toRow
                ? RubyName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                : name;
            string keyName = toRow ? name : name + "_index";

            return sf.IsArray
                ? new[] { $"@{keyName} = []", $"@{rowName} = []" }
                : new[] { $"@{keyName} = 0", $"@{rowName} = nil" };
        }

        if (sf.IsArray)
            return new[] { $"@{name} = []" };

        return new[] { $"@{name} = {DefaultValue(sf)}" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "Tabbit::Uuid.new";
            default: return "0";
        }
    }

    /// <summary>
    /// The rendered check_column call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "Tabbit::KIND_ARRAY" : "Tabbit::KIND_SCALAR";


        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `Tabbit::ELEMENT_I32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "Tabbit::ELEMENT_STRING",
                ValueType.Int64 => "Tabbit::ELEMENT_I64, Tabbit::ELEMENT_I32, Tabbit::ELEMENT_VARINT",
                ValueType.Uuid => "Tabbit::ELEMENT_UUID",
                _ => "Tabbit::ELEMENT_I32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "Tabbit::ELEMENT_I32, Tabbit::ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "Tabbit::ELEMENT_I64, Tabbit::ELEMENT_I32, Tabbit::ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "Tabbit::ELEMENT_F64, Tabbit::ELEMENT_F32, Tabbit::ELEMENT_I32"; break;
                case ValueType.Float: accepted = "Tabbit::ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "Tabbit::ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "Tabbit::ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "Tabbit::ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "Tabbit::ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "Tabbit::ELEMENT_I64"; break;

                default:
                    throw new TabbitDefectException($"The ruby generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one.
        string elements = wire.HasOptionalElements ? ", true" : "";

        return $"Tabbit.check_column(column, '{tableName}.{wire.Name}', {kind}, "
            + $"{nullable}, [{accepted}]{elements})";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member walks the elements without building them - the array was made
    /// with the record - while a trimmed one reads its length from the row, and there the
    /// first member does build because no constructor could have known how long this row's
    /// is.
    /// </remarks>
    private static string ReadKind(WireColumn wire)
    {
        if (wire.Member is not null)
        {
            if (!wire.IsArray)
                return "scalar";

            // Which of the two owns the array decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
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

    private RubyAccessorView BuildAccessor() => new RubyAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,
        ReaderNames = Symbols(_model.Tables.Select(table => RubySnakeName(table.Name)).ToList()),

        Tables = _model.Tables.Select(table => new RubyTableSlotView
        {
            Name = RubySnakeName(table.Name),
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


            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                         )
            .Select(x => new RubyCrossReferenceView
            {
                Table = RubyName(x.Table.Name),
                Fields = x.Fields.Select(sf => new RubyReferenceFieldView
                {
                    Name = ResolvesToRow(sf.FirstField!)
                        ? RubyName(sf.Name)
                        : RubyName(sf.Name) + "_index",

                    RowName = ResolvesToRow(sf.FirstField!)
                        ? RubyName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                        : RubyName(sf.Name),

                    RefTable = RubyName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + RubyName(sf.FirstField!.ResolvedRefField!.Name),
                    IsArray = sf.IsArray,
                }).ToList(),
                RecordFields = x.RecordFields,
            })
            .ToList(),
    };

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from.
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
    /// The cursor assignment ahead of an encodable column's row loop, or nothing for a
    /// column that reads the reader directly. An assignment, not a declaration: Ruby
    /// has no block-scoped declarations to collide inside the case dispatch.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"cursor = Tabbit::ColumnCursor.new(reader, column, count, '{tableName}.{wire.Name}')"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to the int32 or the string read:
    /// int32 members, enums, references and strings. The other cursor scalars stay
    /// per-row - their encodings are dictionaries, where the per-row work is already one
    /// index lookup - as do arrays, which are always raw.
    /// </remarks>
    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        // A run says "this many rows hold the same value", which an array column's row does
        // not have one of. Its elements are read one at a time.
        if (wire.IsArray)
            return "";

        // A reference runs on the key it carries, which is not always an int32. An enum's
        // underlying value is one. spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int32 => "next_same_i32",
                ValueType.String => "next_same_string",
                _ => "",
            };
        }

        if (wire.ElementType == ValueType.Enum)
            return "next_same_i32";

        return wire.ElementType switch
        {
            ValueType.Int32 => "next_same_i32",
            ValueType.String => "next_same_string",
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

        string name = RubyName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + RubyName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"records[i].{name}{(ResolvesToRow(wire.TagCarrier) ? "" : "_index")} = value"
                : $"records[i].{name}{memberAccess}{(ResolvesToRow(wire.TagCarrier) ? "" : "_index")} = value";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"records[i].{name}{memberAccess} = value";

            return $"records[i].{name}{memberAccess} = value";
    }

    /// <summary>
    /// The read for a scalar field's row: through the cursor where the column can
    /// arrive encoded - which also carries the lossless promotions - and the direct
    /// reader call where it is raw by spec.
    /// </summary>
    private string ScalarReadExpression(WireColumn wire)
    {
        if (UsesCursor(wire))
        {
            // Enum values arrive as the integer the sheet declared, and a reference
            // contributes only its stored index - the template assigns it to the
            // index member.
            // The key the target is addressed by, which is not always an int32. `next_i32` for
            // every reference is what kept a table keyed by anything else from being pointed at
            // from this language. spec/reference-key-types.md.
            if (wire.IsRef)
            {
                return wire.RefKeyType switch
                {
                    ValueType.Int64 => "cursor.next_i64",
                    ValueType.String => "cursor.next_string",
                    _ => "cursor.next_i32",
                };
            }

            if (wire.ElementType == ValueType.Enum)
                return "cursor.next_i32";

            return wire.ElementType switch
            {
                ValueType.Int32 => "cursor.next_i32",
                ValueType.Int64 => "cursor.next_i64",
                ValueType.Double => "cursor.next_f64",
                ValueType.Float => "cursor.next_f32",
                ValueType.Bool => "cursor.next_bool",

                // Ticks, which is what the member holds - so the i64 the column
                // carried is the member, exactly as the direct read leaves it.
                ValueType.DateTime => "cursor.next_i64",
                ValueType.TimeSpan => "cursor.next_i64",

                _ => "cursor.next_string",
            };
        }

        return ReadExpression(wire);
    }

    /// <summary>
    /// The read for one element of an array column's row, which is the scalar read one
    /// level down - the cursor's call where the column reads through one, and the direct
    /// reader call where it does not.
    /// </summary>
    private string ElementReadExpression(WireColumn wire)
    {
        // A reference travels as the stored index whatever the member's own type is, so a
        // raw one reads an int32 rather than the call that type would name.
        if (wire.IsRef && !UsesCursor(wire))
            return "reader.read_int32";

        return ScalarReadExpression(wire);
    }

    private string ReadExpression(WireColumn wire)
    {
        return wire.ElementType switch
        {
            // Enum values travel zig-zag encoded rather than fixed width, and arrive as
            // the integer the sheet declared.
            ValueType.Enum => "reader.read_enum",
                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/reference-key-types.md.
            ValueType.ForeignRecord => LanguageProfile.Ruby.ReadCall(wire.RefKeyType),
            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            _ => LanguageProfile.Ruby.ReadCall(wire.ElementType),
        };
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
                return ((long)constant.Value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Float:
                return ((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.Double:
                return ((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture);

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return "Tabbit::Uuid.new([" + string.Join(", ",
                    ((Guid)constant.Value!).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "].pack('C*'))";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}::{ConstantName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", constant.Type),
                            ("Generator", "ruby")));
        }
    }

    /// <summary>
    /// A single-quoted Ruby literal, which interpolates nothing and so needs only two
    /// characters escaped.
    /// </summary>
    private static string Quote(string value)
    {
        var literal = new StringBuilder("'");

        foreach (var c in value ?? "")
        {
            if (c == '\'' || c == '\\')
                literal.Append('\\');

            literal.Append(c);
        }

        return literal.Append('\'').ToString();
    }

    private static string Symbols(IReadOnlyList<string> names)
        => string.Join(", ", names.Select(name => ":" + name));

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// An attribute name.
    ///
    /// snake_case, and escaped when it lands on a keyword - which it can, because Ruby
    /// members are lowercase and so is nearly every Ruby keyword.
    /// </summary>
    private string RubyName(string name) => LanguageProfile.Ruby.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for the names that are not members - an enum label's symbol, an
    /// accessor's per-table reader.
    /// </summary>
    private static string RubySnakeName(string name) => LanguageProfile.Ruby.MemberName(name.ToSnakeCase());

    /// <summary>A constant, SCREAMING_SNAKE_CASE as Ruby writes them.</summary>
    private static string ConstantName(string name) => name.ToUpperSnakeCase();

}
