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
/// Settings for the Go target.
///
/// Declared here rather than in <see cref="RecipeModel"/>, as every target's settings
/// are: the recipe schema does not grow a member per target, so a language added later
/// costs its own files and touches nothing existing.
/// </summary>
public sealed class GoRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Go package the generated file declares.
    /// </summary>
    public string PackageName { get; set; } = "gamedata";

    /// <summary>
    /// Module path the generated go.mod declares, and the prefix the generated file
    /// imports its reader by.
    ///
    /// Go has no relative imports outside GOPATH mode, so the output needs a module of
    /// its own to be buildable at all. Point this at wherever the directory ends up if
    /// the generated code is vendored into a larger module.
    /// </summary>
    public string ModulePath { get; set; } = "gamedata";

    /// <summary>
    /// Whether to write a go.mod beside the generated file.
    ///
    /// On by default, so the output builds as it stands. Turn it off when vendoring the
    /// directory into a module that already has one.
    /// </summary>
    public bool WriteGoMod { get; set; } = true;

    /// <summary>
    /// Whether to write the data updater beside the reader.
    ///
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a build can take new data without being redeployed. Off by
    /// default: a service that ships its data with its binary has no use for it.
    /// </summary>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>Go version the generated go.mod requires.</summary>
    public string GoVersion { get; set; } = "1.21";

    /// <summary>Base name of the generated file, without its extension.</summary>
    public string AccessorName { get; set; } = "Tables";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

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

    /// <summary>
    /// Which side this output is built for: "c", "s", or "cs"/blank for both.
    /// </summary>
    public string TargetSide { get; set; } = "cs";
}

/// <summary>
/// Emits a single self-contained Go file per recipe entry, plus the binary reader.
///
/// One file rather than one per entity, as for C# and C++: Go resolves identifiers
/// across a package regardless of which file they are in, so splitting would buy
/// nothing and cost the reader a search.
///
/// The shape lives in templates/go.sbn.
/// </summary>
[TabbitTarget("go", TargetKind.CodeGeneration, Order = 50)]
public class GoCodeGenerator : CodeGenerator<GoRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private GoRecipe _recipe = null!;

    /// <summary>
    /// A record group generates a struct and a slice of it; a member column fills one of its
    /// fields.
    /// </summary>
    /// <remarks>
    /// The sixth of the thirteen, and the same split as the five before it - declaration per
    /// field, reading per wire column.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a struct declared beside the element type, and the read reaches it with
    /// a longer member path. A Go struct zero-initializes, so nothing has to be made for it.
    /// spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `Has{Field}` member beside the value one.
    /// </summary>
    /// <remarks>
    /// Not a pointer. It has to work the same for a `string` as for an `int32`, and making
    /// every optional member a pointer would put an allocation and a nil check between the
    /// caller and every value - spec/optional-fields.md has the reasoning.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// `HasXAt(i)` beside the value, filled from the element bitmap the file carries.
    /// spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, GoRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;

        // Already narrowed to the side this entry is built for.
        _model = context.Model;

        Generate();
        WriteBinaryReaderRuntime();

        if (_recipe.WriteGoMod)
            WriteGoMod();
    }

    /// <summary>
    /// Writes a file per table, per enum and per constant set, plus the accessor.
    /// </summary>
    /// <remarks>
    /// It used to be one file holding all of it, which made a deleted table a hunk of dead
    /// code inside a file that still compiled. The layout matches the other targets.
    ///
    /// Go's own difficulty is the imports: an unused one does not compile, so each file gets
    /// exactly what its own text reaches for. Its easiness is the other side of the same
    /// coin - one package, so nothing here imports another generated file and a table can
    /// name another table's record type freely.
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

        Log.Information($"Generating codes for Go into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        // The accessor joins paths and nothing else; the errors it returns come from the
        // tables already wrapped.
        Write(AccessorFile + ".go", "go-accessor.sbn", new GoPartView
        {
            AccessorName = AccessorType,
            PackageName = _recipe.PackageName,
            Imports = Imports(new[] { "path/filepath" }, reader: false),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table reads through the reader and wraps its failures with fmt.Errorf.
            Write(pair.rendered.TableName.ToSnakeCase() + ".go", "go-table.sbn", new GoPartView
            {
                AccessorName = AccessorType,
                PackageName = _recipe.PackageName,
                Imports = Imports(new[] { "fmt" }, reader: true),
                Table = pair.rendered,
            });
        }

        foreach (var enumm in view.Enums)
        {
            // An enum's String falls back to formatting the number.
            Write("enum_" + enumm.Name.ToSnakeCase() + ".go", "go-enum.sbn", new GoPartView
            {
                AccessorName = AccessorType,
                PackageName = _recipe.PackageName,
                Imports = Imports(new[] { "strconv" }, reader: false),
                Enumm = enumm,
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant set names no standard library type, and reaches the reader only
            // for a uuid.
            Write("const_" + pair.rendered.Name.ToSnakeCase() + ".go", "go-constants.sbn",
                  new GoPartView
                  {
                      AccessorName = AccessorType,
                      PackageName = _recipe.PackageName,
                      Imports = Imports(Array.Empty<string>(), reader: NamesUuid(pair.model)),
                      Set = pair.rendered,
                  });
        }
    }

    /// <summary>
    /// Flat rather than in `tables/`, `enums/` and `constants/` as the other targets do.
    ///
    /// A Go directory is a package, so a subdirectory would be a different one - and the
    /// generated types refer to each other without qualification. The names carry the
    /// grouping instead.
    /// </summary>
    private void Write(string filename, string templateName, object view)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, filename));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    /// <summary>
    /// Writes the Tcb reader into a `tabbit` package beside the generated file.
    ///
    /// Emitted rather than fetched, as for the other languages: the output directory is
    /// then self-contained and there is no way to pair generated code with a reader of a
    /// different vintage.
    /// </summary>
    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Go.tcb_reader.go",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "tcb_reader.go"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // service that ships its data with its binary.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Go.updater.go",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "updater.go"));
        }
    }

    /// <summary>
    /// Writes the go.mod that makes the output a module, which is what lets the
    /// generated file import its reader at all.
    /// </summary>
    private void WriteGoMod()
    {
        string filename = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(_recipe.Path, "go.mod"));

        var text = new StringBuilder();
        text.Append("module ").Append(_recipe.ModulePath).Append('\n');
        text.Append('\n');
        text.Append("go ").Append(_recipe.GoVersion).Append('\n');

        StagingFiles.WriteAllTextToFile(filename, text.ToString());
    }

    // --------------------------------------------------------------- view

    /// <summary>
    /// The whole model, which <see cref="Generate"/> then splits into files.
    /// </summary>
    /// <remarks>
    /// No imports here any more: they are per file, because an unused one does not compile
    /// in Go, and a single list for the model would put an unused import in most of them.
    /// </remarks>
    private GoFileView BuildView() => new GoFileView
    {
        AccessorName = AccessorType,
        PackageName = _recipe.PackageName,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    /// <summary>
    /// Exactly the imports the generated file uses.
    ///
    /// Go rejects an unused import outright, so this cannot be a fixed list - a model
    /// with no enums does not mention strconv, and one with no tables does not mention
    /// filepath or fmt.
    /// </summary>
    /// <summary>
    /// The imports one generated file needs, and only those.
    /// </summary>
    /// <remarks>
    /// Per file, because an unused import does not compile in Go. Kotlin can hand every
    /// file the same list and suppress the warning; here each one gets exactly what its own
    /// text reaches for, worked out from what that file is rather than by scanning what was
    /// rendered.
    ///
    /// Nothing imports another generated file: they are all one package, so a table's file
    /// names another table's record type with no import at all.
    /// </remarks>
    /// <param name="standard">Standard library paths, without quotes.</param>
    /// <param name="reader">Whether the file names the emitted reader package.</param>
    private IReadOnlyList<string> Imports(IEnumerable<string> standard, bool reader)
    {
        var imports = standard.Select(path => $"\"{path}\"").ToList();

        if (!reader)
            return imports;

        // Blank line between the standard library and everything else, as gofmt would.
        if (imports.Count > 0)
            imports.Add("");

        imports.Add($"\"{_recipe.ModulePath}/tabbit\"");

        return imports;
    }

    /// <summary>Whether a constant set names the reader's UUID type.</summary>
    private static bool NamesUuid(ConstantSet set)
        => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

    private GoEnumView BuildEnum(Models.Enum enumm) => new GoEnumView
    {
        Name = enumm.Name.ToPascalCase(),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select(label => new GoEnumLabelView
        {
            Name = label.Name.ToPascalCase(),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
        }).ToList(),
    };

    private GoConstantSetView BuildConstantSet(ConstantSet constantSet) => new GoConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new GoConstantView
        {
            Name = constant.Name.ToPascalCase(),
            Type = ToGoTypeName(constant.Type, constant.Enum, null),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private GoTableView BuildTable(Table table) => new GoTableView
    {
        RawName = table.Name,
        RecordName = table.Name.ToPascalCase() + "Record",
        TableName = table.Name.ToPascalCase() + "Table",
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),

        MultiReferences = MultiTargetColumns.Of(table).Select(BuildMultiReference).ToList(),

        // A separate list, because declaring a member is per field and reading is per
        // column - and a record group is one column per member of it.
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),

        NeedsRecordInit = table.SerialFields.Any(
            sf => sf.IsRecord
                  && (sf.MembersAreArrays || (sf.IsArray && !table.TrimTrailingArrayElements))),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<GoIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf => new GoIndexView
        {
            Member = GoName(sf.Name),
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
    /// Read off the referenced table rather than assumed to be `FindByIndex`. The primary
    /// index is whatever the sheet put in the first column, and a sheet that calls it `Id`
    /// generates `FindById`.
    /// </remarks>
    private static string PrimaryLookup(Table? refTable)
        => "FindBy" + refTable!.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase();

    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// Zero - or the key type's empty value - is the convention for "points at nothing", and a
    /// multi-target column has to honour it in every language: the discriminator it produces is
    /// observable, so a language that resolved a zero where another did not would answer a
    /// different table for the same row. spec/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "!= \"\"",
            ValueType.Uuid => "!= (tabbit.UUID{})",
            _ => "> 0",
        };

    /// <summary>
    /// The slot and the discriminator of a record member reaching several tables, or null when
    /// the member reaches one table or none.
    /// </summary>
    /// <remarks>
    /// The element type name is what the methods hang off, so it is passed in: the view has
    /// flattened the nesting by the time this runs and that name carries the whole path.
    /// spec/multi-target-accessors.md.
    /// </remarks>
    private static GoMultiMemberView? MultiMemberOrNull(RecordMember member, string elementTypeName)
    {
        var field = member.FirstField;

        if (field is null || !field.IsMultiRef || field.MultiTargetEnum is null)
            return null;

        string enumType = field.MultiTargetEnum.Name.ToPascalCase();
        string name = GoName(member.Name);

        return new GoMultiMemberView
        {
            ElementTypeName = elementTypeName,
            KeyMember = name,
            SlotMember = name + "Row",
            TargetMember = name + "Target",
            TargetTypeName = enumType,
            IsArray = member.IsArray,
            Targets = field.ResolvedRefTables!.Select(target => new GoMultiTargetView
            {
                Table = "",
                RecordName = target.Name.ToPascalCase() + "Record",
                Method = GoName(target.Name.ToPascalCase() + "By" + member.Name.ToPascalCase()),
                Constant = enumType + target.Name.ToPascalCase(),
                Lookup = "",
            }).ToList(),
        };
    }

    /// <summary>
    /// One multi-target column that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// Which of the three record shapes this is decides where the element number sits, exactly
    /// as it does for a single-target member - and the same reason keeps that decision here
    /// rather than in the template. spec/references-in-records.md.
    /// </remarks>
    private GoMultiRecordReferenceView BuildMultiRecordReference(WireColumn wire)
    {
        string name = GoName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + GoName(part)));
        var field = wire.TagCarrier;

        bool isArray = wire.IsFixedArray || wire.IsVariableLengthArray;

        string path = !isArray || wire.Group.MembersAreArrays
            ? $"record.{name}{member}"
            : $"record.{name}[k]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[k]" : "";

        return new GoMultiRecordReferenceView
        {
            Key = path + subscript,
            Slot = path + "Row" + subscript,
            Target = path + "Target" + subscript,

            // Whichever slice holds the elements. The key member is that slice where the
            // members are the arrays, so there is no separate key slice to range over.
            Range = isArray
                ? (wire.Group.MembersAreArrays ? path : $"record.{name}")
                : "",

            TargetTypeName = field.MultiTargetEnum!.Name.ToPascalCase(),
            KeyIsSet = KeyIsSetSuffix(wire.RefKeyType),
            Targets = field.ResolvedRefTables!.Select(target => new GoMultiTargetView
            {
                Table = GoName(target.Name),
                RecordName = target.Name.ToPascalCase() + "Record",
                Method = "",
                Constant = field.MultiTargetEnum!.Name.ToPascalCase() + target.Name.ToPascalCase(),
                Lookup = PrimaryLookup(target),
            }).ToList(),
        };
    }

    /// <summary>Whether a wire column is a record member reaching several tables.</summary>
    private static bool IsMultiTargetMember(WireColumn wire)
        => wire.Member is not null
           && wire.TagCarrier.IsMultiRef
           && wire.TagCarrier.MultiTargetEnum is not null;

    /// <summary>
    /// One column whose value is a row of one of several tables.
    /// </summary>
    private GoMultiReferenceView BuildMultiReference(MultiTargetColumn column)
        => new GoMultiReferenceView
        {
            KeyMember = GoName(column.Group.Name),
            SlotMember = GoName(column.Group.Name) + "Row",
            TargetMember = GoName(column.Group.Name) + "Target",
            TargetTypeName = column.Discriminator.Name.ToPascalCase(),
            KeyIsSet = KeyIsSetSuffix(column.Field.RefKeyType),
            Targets = column.Targets.Select(target => new GoMultiTargetView
            {
                Table = target.Name.ToPascalCase(),
                RecordName = target.Name.ToPascalCase() + "Record",
                Method = GoName(target.Name.ToPascalCase() + "By" + column.Group.Name.ToPascalCase()),
                Constant = column.Discriminator.Name.ToPascalCase() + target.Name.ToPascalCase(),
                Lookup = PrimaryLookup(target),
            }).ToList(),
        };

    private GoFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = GoName(sf.Name);
        string elementType = ResolvedElementType(sf);

        return new GoFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Declarations = Declarations(sf, name, elementType),
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<GoRecordMemberView>(),
            MembersAreArrays = false,
            IsFixedRecordArray = false,
            ArrayType = "",
            ElementCount = 0,
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = PresenceMember(sf),
            ElementPresenceMember = PresenceMember(sf) + "At",
        };
    }

    /// <summary>
    /// Members of one level of a record, declaring a struct for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the structs it produces. A nested member needs nothing beyond its declaration
    /// line: a Go struct zero-initializes, so there is no factory to call for it to reach its
    /// members' empty values. spec/nested-multi-level.md.
    /// </remarks>
    private List<GoRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<GoRecordTypeView> declared)
    {
        var result = new List<GoRecordMemberView>();

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
                // No third member for whether it resolved - a nil pointer says so, which is
                // how this output already answers that outside a record.
                // spec/references-in-records.md.
                string memberType = member.IsRef
                    ? "*" + member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record"
                    : ToGoTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null);
                string keyType = member.IsRef
                    ? ToGoTypeName(member.FirstField!.RefKeyType, null, null)
                    : "";
                string slice = member.IsArray ? "[]" : "";

                var declarations = new List<string>
                {
                    $"{GoName(member.Name)} {slice}{memberType}",
                };

                if (member.IsRef)
                    declarations.Add($"{GoName(member.Name)}Index {slice}{keyType}");

                // A member reaching several tables keeps the key declared above and gains two
                // more: one slot for the resolved row whatever table it came from, and the
                // discriminator saying which. spec/multi-target-accessors.md.
                var multi = MultiMemberOrNull(member, prefix);

                if (multi is not null)
                {
                    declarations.Add($"// The row {GoName(member.Name)} names, as whichever of its target tables holds");
                    declarations.Add("// it. Read it through the methods below rather than directly: they check the");
                    declarations.Add("// discriminator first.");
                    declarations.Add($"{multi.SlotMember} {slice}any");
                    declarations.Add($"// Which table {GoName(member.Name)} is a row of.");
                    declarations.Add($"{multi.TargetMember} {slice}{multi.TargetTypeName}");
                }

                result.Add(new GoRecordMemberView
                {
                    Multi = multi,
                    Comment = CommentLines(member.FirstField!.Comment),

                    // The array is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns the slice differs.
                    Declarations = declarations,
                    Name = GoName(member.Name),
                    SliceType = member.IsArray ? "[]" + memberType : "",
                    ElementCount = member.IsArray ? member.Fields.Count : 0,
                    RefKeySliceType = (member.IsRef && member.IsArray) ? "[]" + keyType : "",
                });

                continue;
            }

            // A level below. The type name carries the path so two records each holding a
            // `Position` do not name one struct twice.
            string typeName = prefix + GoName(member.Name);
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new GoRecordTypeView
            {
                MultiMembers = nested.Where(m => m.Multi is not null).Select(m => m.Multi!).ToList(),
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record.{GoName(group.Name)}",
            });

            result.Add(new GoRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declarations = new[] { $"{GoName(member.Name)} {typeName}" },
                Name = GoName(member.Name),
                SliceType = "",
                ElementCount = 0,
            });
        }

        return result;
    }

    /// <summary>
    /// A record group: the struct to declare for one element, and the member holding one or
    /// a slice of them.
    /// </summary>
    /// <remarks>
    /// A slice rather than an array even where the length is fixed, so the two record shapes
    /// declare the same thing and only the read differs - which is the same choice the scalar
    /// serial fields here already made.
    ///
    /// No reference members: a reference belongs to a member and the model refuses one there,
    /// so nothing here has the index slice and the setter a reference would need.
    /// </remarks>
    private GoFieldView BuildRecordField(Table table, SerialField sf)
    {
        string name = GoName(sf.Name);
        string elementType = RecordTypeName(table, sf);

        // Innermost first, so a struct is declared before the one naming it.
        var recordTypes = new List<GoRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, elementType, table, sf, recordTypes);

        recordTypes.Add(new GoRecordTypeView
        {
            MultiMembers = members.Where(m => m.Multi is not null).Select(m => m.Multi!).ToList(),
            TypeName = elementType,
            Members = members,
            IsOutermost = true,
            Owner = $"{table.Name.ToPascalCase()}Record.{name}",
        });

        return new GoFieldView
        {
            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,

            // An array of arrays has no element type to name, so the inner slice is the
            // type - see spec/nested-multi-level.md.
            Declarations = new[]
            {
                sf.MembersAreAnonymous
                    ? $"{name} [][]{ToGoTypeName(sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull, null)}"
                    : sf.IsArray ? $"{name} []{elementType}" : $"{name} {elementType}",
            },
            IsRecord = true,
            RecordTypeName = elementType,

            Members = members,
            RecordTypes = recordTypes,

            IsFixedRecordArray = sf.IsArray && !table.TrimTrailingArrayElements,
            MembersAreArrays = sf.MembersAreArrays,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            OuterCount = sf.Members.Count,
            ElementTypeName = sf.MembersAreAnonymous
                ? ToGoTypeName(sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull, null)
                : "",
            ArrayType = "[]" + elementType,
            ElementCount = sf.RecordElementCount,

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
    /// columns each fill one field of the generated element type, which is the whole of the
    /// difference - see spec/nested-fields.md.
    /// </remarks>
    private GoColumnView BuildColumn(Table table, WireColumn wire)
    {
        string name = GoName(wire.Group.Name);

        // A record's member column assigns one field of the element rather than the member
        // itself: `r.Slot[j].Id` instead of `r.Slot[j]`. Everything else about reading it is
        // the same, which is why this is a suffix rather than a second path.
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + GoName(part)));

        string arrayType = (wire.Member is null)
            ? "[]" + ColumnElementType(wire)
            : "[]" + RecordTypeName(table, wire.Group);

        return new GoColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire)
                ? "elementCount := int(cursor.NextLength())"
                : "elementCount := int(reader.ReadCounter32())",
            RefKeyType = wire.IsRef ? ToGoTypeName(wire.RefKeyType, null, null) : "",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = name,
            MemberAccess = memberAccess,

            // A reference member reads into the key beside the row it will resolve to, and
            // the suffix goes on the member rather than after the subscript.
            // spec/references-in-records.md.
            MemberRefSuffix = (wire.Member is not null && wire.IsRef) ? "Index" : "",
            MemberAt = wire.MemberAt,
            ElementCount = wire.Cells.Count,
            ArrayType = arrayType,
            IsFirstMember = wire.IsFirstMember,
            ReadValue = ValueReadExpression(wire),
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = PresenceMember(wire.Group),
            ElementPresenceMember = PresenceMember(wire.Group) + "At",
            EmptyValue = EmptyValue(wire),
        };
    }

    /// <summary>
    /// The element type of a record group, which carries the table's name.
    /// </summary>
    /// <remarks>
    /// A Go directory is a package and this output is one directory, so every generated type
    /// shares a namespace - two tables each holding a `Slot` group would otherwise be the
    /// same name declared twice.
    /// </remarks>
    private static string RecordTypeName(Table table, SerialField sf)
        => table.Name.ToPascalCase() + sf.Name.ToPascalCase() + "Entry";

    /// <summary>
    /// The member a nullable column's presence lands in.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever reads
    /// it, and the model has already required its columns to agree about being optional.
    /// </remarks>
    private static string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : "Has" + GoName(sf.Name);

    /// <summary>
    /// What an absent row's member is set back to, so the binary path lands where the JSON
    /// one does.
    /// </summary>
    /// <remarks>
    /// The member's own type rather than its element's: an optional array declares a slice,
    /// and its empty value is the nil slice rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "nil";

        // The resolved member is a pointer at the referenced row, and absence there is
        // exactly what nil says.
        if (wire.ElementType == ValueType.ForeignRecord)
            return "nil";

        return wire.ElementType switch
        {
            ValueType.String => "\"\"",
            ValueType.Bool => "false",
            ValueType.Uuid => "tabbit.UUID{}",

            // An enum's Go type has int32 underneath, so the untyped constant converts.
            // Ticks are an int64 and a reference's stored index an int32, both of them 0.
            _ => "0",
        };
    }

    /// <summary>
    /// The member declarations for a field. A reference gets two: the resolved value and
    /// the raw index it was read as, because the target is not known until every table
    /// is loaded.
    /// </summary>
    private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
    {
        if (sf.IsRef)
        {
            // The key the target is addressed by, not `int32`. The record-member path next
            // door has always asked; this one wrote the width in, so a reference array whose
            // target is keyed by anything else declared a slice the read could not fill.
            // spec/reference-key-types.md.
            string keyType = ToGoTypeName(sf.FirstField!.RefKeyType, null, null);

            return sf.IsArray
                ? new[] { $"{name} []{elementType}", $"{name}Index []{keyType}" }
                : new[] { $"{name} {elementType}", $"{name}Index {keyType}" };
        }

        return sf.IsArray
            ? new[] { $"{name} []{elementType}" }
            : new[] { $"{name} {elementType}" };
    }

    /// <summary>
    /// The rendered CheckColumn call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsVariableLengthArray
            ? "tabbit.KindVarArray"
            : (wire.IsFixedArray ? "tabbit.KindFixedArray" : "tabbit.KindScalar");

        // -1 where one column owns the whole array: the file states how many elements it
        // holds and the read takes it from there, so there is no length here to hold it to.
        // A record member keeps its count - several columns fill one array and the number
        // they agree on is part of the generated shape, so a disagreement is a schema change
        // rather than data. spec/nullable-array-elements.md.
        bool ownsItsArray = wire.IsFixedArray && wire.Member is null;

        int count = wire.IsVariableLengthArray ? 0 : (ownsItsArray ? -1 : wire.Cells.Count);

        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `tabbit.ElementI32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "tabbit.ElementString",
                ValueType.Int64 => "tabbit.ElementI64, tabbit.ElementI32, tabbit.ElementVarint",
                ValueType.Uuid => "tabbit.ElementUUID",
                _ => "tabbit.ElementI32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "tabbit.ElementI32, tabbit.ElementVarint"; break;
                case ValueType.Int64:
                    accepted = "tabbit.ElementI64, tabbit.ElementI32, tabbit.ElementVarint"; break;
                case ValueType.Double:
                    accepted = "tabbit.ElementF64, tabbit.ElementF32, tabbit.ElementI32"; break;
                case ValueType.Float: accepted = "tabbit.ElementF32"; break;
                case ValueType.Bool: accepted = "tabbit.ElementBool"; break;
                case ValueType.String: accepted = "tabbit.ElementString"; break;
                case ValueType.Uuid: accepted = "tabbit.ElementUUID"; break;
                case ValueType.Enum: accepted = "tabbit.ElementVarint"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "tabbit.ElementI64"; break;

                default:
                    throw new TabbitException($"The go generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one. A call of its own
        // because Go has no optional parameters and the accepted elements are variadic.
        string check = wire.HasOptionalElements ? "CheckColumnWithElements" : "CheckColumn";

        return $"tabbit.{check}(reader, column, \"{tableName}.{wire.Name}\", {kind}, {count}, "
            + $"{nullable}, {accepted})";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member loops the elements without allocating - the slice was created
    /// with the row - while a trimmed one reads its length from the row, and there the first
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

            // Which of the two owns the array decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_serial" : "record_serial";
        }

        // A trimmed array of references: the length is the row's and the key still goes in
        // the array beside the values. Read as a plain `var_array` it assigned an int32 into
        // the slice of pointers, which is a page that does not compile - and nothing held the
        // shape, because `foreign[]` is refused and this is only reachable through a folded
        // group with trimming on. spec/variable-length-record-arrays.md.
        if (wire.IsVariableLengthArray)
            return wire.IsRef ? "var_array_ref" : "var_array";

        if (wire.IsFixedArray)
            return wire.IsRef ? "serial_ref" : "serial";

        return wire.IsRef ? "scalar_ref" : "scalar";
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
    /// a column that reads the reader directly.
    /// </summary>
    /// <remarks>
    /// A declaration rather than an assignment: Go gives each switch case a scope of
    /// its own, so the cases that need a cursor declare one and the rest never name it -
    /// which is what keeps Go's unused-variable error away.
    /// </remarks>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"cursor := tabbit.NewColumnCursor(reader, column, count, \"{tableName}.{wire.Name}\")"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to NextI32 or NextString: int32
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
                ValueType.Int32 => "NextSameI32",
                ValueType.String => "NextSameString",
                _ => "",
            };
        }

        if (wire.ElementType == ValueType.Enum)
            return "NextSameI32";

        return wire.ElementType switch
        {
            ValueType.Int32 => "NextSameI32",
            ValueType.String => "NextSameString",
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

        string name = GoName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + GoName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"records[i].{name}Index = value"
                : $"records[i].{name}{memberAccess}Index = value";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"records[i].{name}{memberAccess} = {wire.TagCarrier.Enum.Name.ToPascalCase()}(value)";

        return $"records[i].{name}{memberAccess} = value";
    }

    private GoAccessorView BuildAccessor() => new GoAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new GoTableSlotView
        {
            Name = GoPascalName(table.Name),
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

                // One of those that is a member of a record resolves per element, so it is a
                // loop of its own again. spec/multi-target-accessors.md.
                MultiRecordFields = table.WireColumns
                                         .Where(IsMultiTargetMember)
                                         .Select(BuildMultiRecordReference)
                                         .ToList(),
            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                        || x.MultiFields.Count > 0 || x.MultiRecordFields.Count > 0)
            .Select(x => new GoCrossReferenceView
            {
                Table = GoName(x.Table.Name),
                Fields = x.Fields.Select(sf => new GoReferenceFieldView
                {
                    Name = GoName(sf.Name),
                    RefTable = GoName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = ReferenceValueExpression(sf),
                    IsArray = sf.IsArray,
                }).ToList(),
                RecordFields = x.RecordFields,
                MultiFields = x.MultiFields,
                MultiRecordFields = x.MultiRecordFields,
            })
            .ToList(),
    };

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// No resolution flag: a nil pointer says whether it resolved, which is how this output
    /// already answers that for a reference outside a record. What the loop ranges over says
    /// which of the three record shapes this is - the group's slice, the member's, or neither.
    /// spec/references-in-records.md.
    /// </remarks>
    private GoRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = GoName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + GoName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsFixedArray || wire.IsVariableLengthArray;

        string path = !isArray || wire.Group.MembersAreArrays
            ? $"record.{name}{member}"
            : $"record.{name}[k]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[k]" : "";

        return new GoRecordReferenceView
        {
            Access = path + subscript,
            Key = path + "Index" + subscript,

            // Whichever slice holds the elements - ranged rather than counted, because a
            // trimming group's rows differ in how many they carry.
            Range = isArray
                ? (wire.Group.MembersAreArrays ? $"{path}Index" : $"record.{name}")
                : "",

            RefTable = GoName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The call reading one value: through the cursor where the column can arrive encoded -
    /// which also carries the lossless promotions - and the direct call otherwise.
    /// </summary>
    /// <remarks>
    /// One call for a scalar row and for an array's element alike. An array block states an
    /// encoding for its elements, so its elements read exactly the way a scalar column's do
    /// and only the row's length is asked for first.
    /// </remarks>
    private string ValueReadExpression(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return ReadExpression(wire);

        // The key the target is addressed by, which is not always an int32. Falling through to
        // the switch below read every reference as one, because a reference's element type is
        // `ForeignRecord` and that is the case the default arm answers.
        // spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => "cursor.NextI64()",
                ValueType.String => "cursor.NextString()",
                _ => "cursor.NextI32()",
            };
        }

        return wire.ElementType switch
        {
            ValueType.Enum => $"{wire.TagCarrier.Enum.Name.ToPascalCase()}(cursor.NextI32())",
            ValueType.Int64 => "cursor.NextI64()",
            ValueType.Double => "cursor.NextF64()",
            ValueType.Float => "cursor.NextF32()",
            ValueType.Bool => "cursor.NextBool()",
            ValueType.String => "cursor.NextString()",

            // Ticks, and the member holds exactly those - so the i64 the column
            // carried is the value, with nothing to construct around it.
            ValueType.DateTime => "cursor.NextI64()",
            ValueType.TimeSpan => "cursor.NextI64()",

            // Int32, and the index a reference travels as.
            _ => "cursor.NextI32()",
        };
    }

    /// <summary>
    /// The call reading one value of a field's element type.
    /// </summary>
    private string ReadExpression(WireColumn wire)
    {
        return wire.ElementType switch
        {
            // Enum values travel zig-zag encoded rather than fixed width.
            ValueType.Enum => $"{wire.TagCarrier.Enum.Name.ToPascalCase()}(reader.ReadEnum())",
                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/reference-key-types.md.
            ValueType.ForeignRecord => LanguageProfile.Go.ReadCall(wire.RefKeyType),
            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            _ => LanguageProfile.Go.ReadCall(wire.ElementType),
        };
    }

    /// <summary>
    /// The type one value of a column has: a pointer to the referenced record, a copy of the
    /// referenced field's value, or the element type itself.
    /// </summary>
    /// <remarks>
    /// Both the column and the cell it carries are consulted, because for a reference they
    /// disagree: the column resolves to what the target holds while the cell still says
    /// ForeignRecord.
    /// </remarks>
    private string ColumnElementType(WireColumn wire)
    {
        if (wire.ElementType == ValueType.ForeignRecord)
            return "*" + wire.TagCarrier.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        return ToGoTypeName(wire.TagCarrier.ElementType, wire.TagCarrier.EnumOrNull, null);
    }

    /// <summary>
    /// What a resolved reference yields: a pointer to the record, or one of its fields.
    /// </summary>
    private string ReferenceValueExpression(SerialField sf)
        => sf.ElementType == ValueType.ForeignRecord
            ? "target"
            : "target." + GoName(sf.FirstField!.ResolvedRefField!.Name);

    /// <summary>
    /// The type a field holds: a pointer to the referenced record, a copy of the
    /// referenced field's value, or the element type itself.
    /// </summary>
    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return "*" + sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        return ToGoTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull, null);
    }

    private string ToGoTypeName(ValueType type, Models.Enum? enumm, string? refTableName)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return enumm!.Name.ToPascalCase();

            case ValueType.ForeignRecord:
                return "*" + refTableName.ToPascalCase() + "Record";

            default:
                return LanguageProfile.Go.ScalarTypeName(type);
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
                return "tabbit.UUID{" + string.Join(", ",
                    ((Guid)constant.Value!).ToByteArray()
                        .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + "}";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return constant.Enum.Name.ToPascalCase() + label.Name.ToPascalCase();
            }

            default:
                throw new TabbitException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the go generator cannot render.");
        }
    }

    /// <summary>
    /// A Go interpreted string literal.
    /// </summary>
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
                literal.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                literal.Append(c);
        }

        return literal.Append('"').ToString();
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// An exported member name.
    ///
    /// Pascal case, which is how Go exports, and which is also why nothing is ever
    /// escaped: every Go keyword is lowercase.
    /// </summary>
    private static string GoName(string name) => LanguageProfile.Go.MemberName(name.ToPascalCase());

    /// <summary>
    /// The same spelling, for a name that is not a member - the accessor's slot per table.
    /// </summary>
    /// <remarks>
    /// PascalCase because that is how Go writes an exported identifier, not because a member
    /// is spelled that way. Sharing one function let the two look like one rule.
    ///
    /// Go is the one target with no `MemberCase` setting, so the two can never actually
    /// diverge here. They are still separate, because which of them a name is remains a fact
    /// about that name: this one is a table's slot on the accessor. Go has no setting because
    /// the first letter's case is what exports a member - spelled any other way, the
    /// generated fields would be unreachable from the package that reads them, which is a
    /// broken output rather than a differently spelled one.
    /// </remarks>
    private static string GoPascalName(string name) => LanguageProfile.Go.MemberName(name.ToPascalCase());

}
