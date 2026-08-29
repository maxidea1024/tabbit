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
    /// <remarks>
    /// 1.23 because that is where `iter.Seq` and range-over-function arrived, which is what
    /// the generated tables are iterated by. A recipe that has to build on an older toolchain
    /// sets this lower and gets the same tables without the iterator - the rows are still
    /// reachable as `Records()`.
    /// </remarks>
    public string GoVersion { get; set; } = "1.23";

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
    /// spec/types/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// `map` for both, with the slices beside them holding the file's order - Go's map has
    /// none of its own. spec/types/set-and-map.md section 7.
    /// </summary>
    protected override bool SupportsContainers => true;

    /// <summary>
    /// An optional column becomes a `Has{Field}` member beside the value one.
    /// </summary>
    /// <remarks>
    /// Not a pointer. It has to work the same for a `string` as for an `int32`, and making
    /// every optional member a pointer would put an allocation and a nil check between the
    /// caller and every value - spec/types/optional-fields.md has the reasoning.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// `FindByStageAndSlot(stage, slot)`, keyed by the text the columns make.
    /// </summary>
    /// <remarks>
    /// A Go map takes a comparable key and a struct of the components would be one, but the
    /// text is what every language can build and the map is unexported - so the type it is
    /// keyed by is this file's business and can change without the surface moving.
    /// </remarks>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// `HasXAt(i)` beside the value, filled from the element bitmap the file carries.
    /// spec/types/nullable-array-elements.md.
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
                // strconv only where a composite key is present: its text is built from
                // the components' and an import Go does not reach for does not compile.
                Imports = Imports(
                    (pair.rendered.Indexes.Any(index => index.IsComposite)
                        ? new[] { "fmt", "strconv" }
                        : new[] { "fmt" })
                        .Concat(RangeOverFunc ? new[] { "iter" } : Array.Empty<string>())
                        .OrderBy(name => name, System.StringComparer.Ordinal)
                        .ToArray(),
                    reader: true),
                RangeOverFunc = RangeOverFunc,
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

        // A struct is an entity beside a table and an enum, so it gets a file of its own - one
        // per declaration however many tables named it. spec/types/polymorphism.md section 7.1.
        foreach (var declared in _model.PolymorphicTypes)
        {
            Write("struct_" + declared.Name.ToSnakeCase() + ".go", "go-struct.sbn",
                  new GoPartView
                  {
                      AccessorName = AccessorType,
                      PackageName = _recipe.PackageName,
                      Imports = Imports(System.Array.Empty<string>(), reader: false),
                      Structure = BuildStruct(declared),
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
    /// One abstract type and its variants, as the template reads them.
    /// </summary>
    /// <remarks>
    /// The members are columns, so their types come out of the same conversion a table's do.
    /// spec/types/polymorphism.md section 7.1.
    /// </remarks>
    private GoPolymorphicTypeView BuildStruct(Models.PolymorphicType declared)
        => new GoPolymorphicTypeView
        {
            Name = GoName(declared.Name),
            SealName = "is" + GoName(declared.Name),
            BaseName = GoName(declared.Name) + "Base",
            BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
            Variants = declared.Variants
                .Select(variant => new GoVariantView
                {
                    TypeName = GoName(variant.Name),
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),
        };

    /// <summary>One member of an abstract type or of one of its variants.</summary>
    /// <remarks>
    /// **A reference member is two fields**, as a reference is anywhere: the declared name is
    /// the key's and the row it resolves to takes the derived one. A variant that carried only
    /// the key would hand a consumer a string where the declaration promised a row.
    /// spec/references/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    private GoStructMemberView StructMember(Models.Field field)
    {
        string raw = field.NamePath is { Count: > 1 } ? field.NamePath[^1].Name : field.Name;
        bool toRow = field.IsRef && field.ResolvedRefTable is not null && ResolvesToRow(field);

        return new GoStructMemberView
        {
            FieldName = GoName(raw),
            FieldType = ToGoTypeName(
                field.Type,
                field.Type is Models.ValueType.Enum or Models.ValueType.EnumArray
                    ? field.Enum
                    : null,
                field.RefTableName),
            Comment = CommentLines(field.Comment),
            RowName = toRow
                ? GoName(RowAccessorName(field.ResolvedRefTable!.Name, raw))
                : "",
            KeyTypeName = field.IsRef
                ? ToGoTypeName(field.RefKeyType, null, null)
                : "",
        };
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

        StagingFiles.WriteAllTextToFile(
            full, GoLayout.Formatted(TemplateEngine.Render(templateName, view)));
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
    /// Whether the version the recipe asks for has range-over-function.
    /// </summary>
    /// <remarks>
    /// A string compare would answer "1.9" is above "1.23", so the two parts are read as
    /// numbers. Anything this cannot parse is treated as too old, which leaves the output
    /// building rather than failing on a version spelling nobody anticipated.
    /// </remarks>
    private bool RangeOverFunc
    {
        get
        {
            string[] parts = (_recipe.GoVersion ?? "").Split('.');

            if (parts.Length < 2
                || !int.TryParse(parts[0], out int major)
                || !int.TryParse(parts[1], out int minor))
            {
                return false;
            }

            return major > 1 || (major == 1 && minor >= 23);
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
            Type = ConstantTypeName(constant),
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
        Matrix = BuildMatrix(table),
        Containers = ContainersOf(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),


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
    /// <summary>
    /// The lookups one struct declares beside its slices.
    /// </summary>
    /// <remarks>
    /// A map's own, because a map is a record and this is that record; and one per member
    /// that is a set, because a set is one slice and has no type of its own to hang anything
    /// on. Both are Go maps - the language has no set - and neither has an order, which is
    /// what the slice beside it is for. spec/types/set-and-map.md section 7.
    /// </remarks>
    private List<GoLookupView> LookupsOf(List<RecordMember> members, Models.ContainerKind own)
    {
        var result = new List<GoLookupView>();

        if (own == Models.ContainerKind.Map
            && members.Find(m => m.Name == Models.ContainerMembers.Key) is { } key)
        {
            var held = members.Find(m => m.Name == Models.ContainerMembers.Value);
            bool storesValue = held is { IsLeaf: true };

            string keyType = ToGoTypeName(
                key.FirstField!.ElementType, key.FirstField!.EnumOrNull, null);

            string valueType = storesValue
                ? ToGoTypeName(held!.FirstField!.ElementType, held!.FirstField!.EnumOrNull, null)
                : "int";

            result.Add(new GoLookupView
            {
                // The value where there is one column to store it, and the entry's position
                // where the value is a struct - under a name that says which.
                Name = storesValue ? "ByKey" : "IndexByKey",
                TypeName = $"map[{keyType}]{valueType}",
                Source = GoName(Models.ContainerMembers.Key),
                StoredValue = storesValue ? GoName(Models.ContainerMembers.Value) + "[j]" : "j",
                Comment = storesValue
                    ? "ByKey answers what each key is mapped to. The slices hold the file's order."
                    : "IndexByKey answers where each key sits among the entries - this map's "
                      + "value is a struct, which is a field per column, so there is no one "
                      + "value to answer with.",
            });
        }

        foreach (var member in members.Where(m => m.Container == Models.ContainerKind.Set))
        {
            string element = ToGoTypeName(
                member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null);

            result.Add(new GoLookupView
            {
                Name = GoName(member.Name) + "Set",
                TypeName = $"map[{element}]bool",
                Source = GoName(member.Name),
                StoredValue = "",
                Comment = $"{GoName(member.Name)}Set answers whether a value is one of the "
                          + $"elements of {GoName(member.Name)}.",
            });
        }

        return result;
    }

    /// <summary>Every lookup in a table, with what reaches it from a record.</summary>
    private List<GoLookupView> ContainersOf(Table table)
    {
        var result = new List<GoLookupView>();

        foreach (var group in table.SerialFields.Where(group => group.IsRecord))
        {
            string root = "." + GoName(group.Name);

            foreach (var lookup in LookupsOf(group.Members, group.Container))
                result.Add(At(lookup, root));

            foreach (var member in group.Members)
                Collect(member, root);
        }

        return result;

        void Collect(RecordMember member, string path)
        {
            string here = path + "." + GoName(member.Name);

            foreach (var lookup in LookupsOf(member.Members, member.Container))
                result.Add(At(lookup, here));

            foreach (var below in member.Members)
                Collect(below, here);
        }

        static GoLookupView At(GoLookupView lookup, string access) => new GoLookupView
        {
            Name = lookup.Name,
            TypeName = lookup.TypeName,
            Source = lookup.Source,
            StoredValue = lookup.StoredValue,
            Comment = lookup.Comment,
            Access = access,
        };
    }

    private IReadOnlyList<GoIndexView> Indexes(Table table)
        => KeyPlans.Of(table).Select(plan =>
        {
            string suffix = plan.Suffix(name => name.ToPascalCase(), "And");

            // **A component that is a reference carries the target's key, not its row.**
            // The two are one edit apart - the column's own name holds the key and the
            // derived name holds the row - and a lookup taking rows is one nobody can
            // call. `KeyComponentView.TypeOf` is the one place that decides, so the type and the
            // shape the key text is built from cannot disagree.
            var components = plan.Components.Select(component =>
            {
                var (keyType, keyEnum) = KeyComponentView.TypeOf(component);

                return new KeyComponentView
                {
                    Param = KeyComponentView.ParamOf(component.Name).ToCamelCase(),
                    Type = ToGoTypeName(keyType, keyEnum, null),
                    Member = GoName(component.Name),
                    Kind = KeyComponentView.KindOf(keyType),
                };
            }).ToList();

            return new GoIndexView
            {
                Member = GoName(plan.Only.Name),

                Suffix = suffix,

                // A composite key is held as the text its components make, so the map is
                // keyed by a string whatever the columns are.
                KeyType = plan.IsComposite ? "string" : IndexKeyType(plan.Only),

                MapName = "by" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                IsComposite = plan.IsComposite,
                IsPrimary = plan.IsPrimary,

                Components = components,

                // Spelled here rather than looped over three times in the template: the
                // parameter list, the subscript and the miss message each need the same
                // columns written a different way, and a single-column key has to come out
                // exactly as it did before this notation existed.
                Params = plan.IsComposite
                    ? string.Join(", ", components.Select(c => c.Param + " " + c.Type))
                    : "key " + IndexKeyType(plan.Only),

                // A method rather than a package function: every generated Go file shares one
                // package, so two tables each keyed by `From,To` would declare the same name.
                Argument = plan.IsComposite
                    ? "t.keyOf" + suffix + "(" + string.Join(", ", components.Select(c => c.Param)) + ")"
                    : "key",

                ValueFormat = plan.IsComposite
                    ? "(" + string.Join(", ", components.Select(_ => "%v")) + ")"
                    : "%v",

                ValueArgs = plan.IsComposite
                    ? string.Join(", ", components.Select(c => c.Param))
                    : "key",
            };
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
    /// different table for the same row. spec/references/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "!= \"\"",
            ValueType.Uuid => "!= (tabbit.UUID{})",
            _ => "> 0",
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
    /// members' empty values. spec/types/nested-multi-level.md.
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
                // spec/references/references-in-records.md.
                string memberType = member.IsRef
                    ? "*" + member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record"
                    : ToGoTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null);
                string keyType = member.IsRef
                    ? ToGoTypeName(member.FirstField!.RefKeyType, null, null)
                    : "";
                // Or the member's own cell holding the list, which is what a `set` and a
                // `map` are - spec/types/set-and-map.md section 4.
                string slice = member.HoldsList ? "[]" : "";

                // The member's own name is the key's, because the key is what the cell
                // holds; the row is linked after loading and takes a derived name.
                // spec/references/reference-surface-naming.md sections 4 and 5.
                var declarations = member.IsRef
                    ? new List<string>
                    {
                        $"{GoName(member.Name)} {slice}{keyType}",
                        $"{GoName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))} {slice}{memberType}",
                    }
                    : new List<string> { $"{GoName(member.Name)} {slice}{memberType}" };


                result.Add(new GoRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),

                    // The array is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns the slice differs.
                    Declarations = declarations,
                    Name = GoName(member.Name),
                    SliceType = member.HoldsList ? "[]" + memberType : "",

                    // Zero where the member's own cell is the list: no declaration knows how
                    // long a row's list is, so the read makes the slice it fills.
                    ElementCount = member.ListIsInTheCell ? 0
                        : member.IsArray ? member.Fields.Count : 0,
                    RefKeySliceType = (member.IsRef && member.IsArray) ? "[]" + keyType : "",

                    RowName = member.IsRef && ResolvesToRow(member.FirstField!)
                        ? GoName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                        : "",
                });

                continue;
            }

            // A level below. The type name carries the path so two records each holding a
            // `Position` do not name one struct twice.
            string typeName = prefix + GoName(member.Name);
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new GoRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record.{GoName(group.Name)}",
                Lookups = LookupsOf(member.Members, member.Container),
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

        // Which abstract type this group is, if it is one. One per declaration however many
        // tables named it. spec/types/polymorphism.md section 7.1.
        var declaredType = sf.Members
                .FirstOrDefault(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                ?.FirstField?.AbstractTypeName is { } abstractName
            ? _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == abstractName.ToPascalCase())
            : null;

        // The entry steps aside for a polymorphic group: the name belongs to the method that
        // hands back the interface, and a field cannot share it. spec/types/polymorphism.md 7.2.
        string entryName = declaredType is null
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);

        // Innermost first, so a struct is declared before the one naming it.
        var recordTypes = new List<GoRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, elementType, table, sf, recordTypes);

        recordTypes.Add(new GoRecordTypeView
        {
            TypeName = elementType,
            Members = members,
            IsOutermost = true,
            Lookups = LookupsOf(sf.Members, sf.Container),
            Owner = $"{table.Name.ToPascalCase()}Record.{name}",
        });

        return new GoFieldView
        {
            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,

            // An array of arrays has no element type to name, so the inner slice is the
            // type - see spec/types/nested-multi-level.md.
            Declarations = new[]
            {
                sf.MembersAreAnonymous
                    ? $"{entryName} [][]{ToGoTypeName(sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull, null)}"
                    : sf.IsArray ? $"{entryName} []{elementType}" : $"{entryName} {elementType}",
            },

            EntryFieldName = entryName,
            VariantsAreArray = declaredType is not null && sf.IsArray,
            EntryAccess = "entry",
            AbstractTypeName = declaredType is null ? "" : GoName(declaredType.Name),
            AbstractBaseName = declaredType is null ? "" : GoName(declaredType.Name) + "Base",
            BaseMembers = (declaredType?.BaseMembers ?? []).Select(StructMember).ToList(),
            Variants = (declaredType?.Variants ?? [])
                .Select(variant => new GoVariantView
                {
                    TypeName = GoName(variant.Name),
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),
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
    /// difference - see spec/types/nested-fields.md.
    /// </remarks>
    /// <summary>
    /// What a group's flat entry is called on the record.
    /// </summary>
    /// <remarks>
    /// Unexported for a polymorphic group: there the group's own name belongs to the method
    /// that hands back the interface, and a field cannot share it with a method. Every read
    /// target has to agree with the declaration, so both ask this.
    /// spec/types/polymorphism.md section 7.2.
    /// </remarks>
    private string EntryName(WireColumn wire)
    {
        string name = GoName(wire.Group.Name);

        bool polymorphic = wire.Group.Members.Any(
            member => member.IsLeaf && member.FirstField is { IsDiscriminator: true });

        return polymorphic ? char.ToLowerInvariant(name[0]) + name.Substring(1) : name;
    }

    private GoColumnView BuildColumn(Table table, WireColumn wire)
    {
        string name = EntryName(wire);

        // A record's member column assigns one field of the element rather than the member
        // itself: `r.Slot[j].Id` instead of `r.Slot[j]`. Everything else about reading it is
        // the same, which is why this is a suffix rather than a second path.
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + GoName(part)));

        string arrayType = (wire.Member is null)
            ? "[]" + ColumnElementType(wire)
            : "[]" + RecordTypeName(table, wire.Group);

        return new GoColumnView
        {
            WireTag = wire.TagCarrier.WireTag!.Value,
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

            // The key carries the member's own name now, so nothing is appended.
            // spec/references/reference-surface-naming.md section 4.
            MemberRefSuffix = "",

            RowName = wire.IsRef && wire.TagCarrier.ResolvedRefTable is not null
                        && ResolvesToRow(wire.TagCarrier)
                ? GoName(RowAccessorName(wire.TagCarrier.ResolvedRefTable.Name, wire.Group.Name))
                : GoName(wire.Group.Name) + "Index",

            RowMemberAccess = (wire.Member is not null && wire.IsRef
                               && ResolvesToRow(wire.TagCarrier))
                ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                    .Select(part => "." + GoName(part)))
                  + "." + GoName(RowAccessorName(
                        wire.TagCarrier.ResolvedRefTable!.Name, wire.MemberPath[^1]))
                : "",
            MemberAt = wire.MemberAt,
            ElementCount = wire.Cells.Count,
            ArrayType = arrayType,
            ElementSliceType = "[]" + ColumnElementType(wire),
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
        if (wire.IsArray)
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
            // spec/references/reference-key-types.md.
            string keyType = ToGoTypeName(sf.FirstField!.RefKeyType, null, null);

            // The column's name is the key's; the row takes the derived one.
            // spec/references/reference-surface-naming.md sections 4 and 5.
            bool toRow = ResolvesToRow(sf.FirstField!);
            string rowName = toRow
                ? GoName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                : name;
            string keyName = toRow ? name : name + "Index";

            return sf.IsArray
                ? new[] { $"{keyName} []{keyType}", $"{rowName} []{elementType}" }
                : new[] { $"{keyName} {keyType}", $"{rowName} {elementType}" };
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
        string kind = wire.IsArray ? "tabbit.KindArray" : "tabbit.KindScalar";


        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `tabbit.ElementI32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/references/reference-key-types.md.
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
                    throw new TabbitDefectException($"The go generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one. A call of its own
        // because Go has no optional parameters and the accepted elements are variadic.
        string check = wire.HasOptionalElements ? "CheckColumnWithElements" : "CheckColumn";

        return $"tabbit.{check}(reader, column, \"{tableName}.{wire.Name}\", {kind}, "
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
            if (!wire.IsArray)
                return "scalar";

            // Which of the two owns the array decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.MemberOwnsTheArray ? "record_member_var" : "record_var";
        }

        // A trimmed array of references: the length is the row's and the key still goes in
        // the array beside the values. Read as a plain `var_array` it assigned an int32 into
        // the slice of pointers, which is a page that does not compile - and nothing held the
        // shape, because `foreign[]` is refused and this is only reachable through a folded
        // group with trimming on. spec/types/variable-length-record-arrays.md.
        if (wire.IsArray)
            return wire.IsRef ? "var_array_ref" : "var_array";

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

        if (wire.IsArray)
            return true;

        // A reference reaches the cursor when the key it carries does. An unconditional yes
        // was the int32 assumption in another place: a target keyed by `uuid` has no cursor
        // path any more than a `uuid` column does. spec/references/reference-key-types.md.
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
        if (wire.IsArray)
            return "";

        // A reference runs on the key it carries, which is not always an int32. An enum's
        // underlying value is one. spec/references/reference-key-types.md.
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

        string name = EntryName(wire);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + GoName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"records[i].{name}{(ResolvesToRow(wire.TagCarrier) ? "" : "Index")} = value"
                : $"records[i].{name}{memberAccess}{(ResolvesToRow(wire.TagCarrier) ? "" : "Index")} = value";
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

        Grids = _model.Tables
            .Select(table => MatrixPlans.Of(table, _model))
            .Where(plan => plan is not null)
            .Select(plan => new GoGridLinkView
            {
                Values = GoPascalName(plan!.Values.Name),
                Columns = GoPascalName(plan.Columns.Name),
            })
            .ToList(),

        CrossReferences = _model.Tables
            .Select(table => new
            {
                Table = table,
                Fields = table.SerialFields.Where(sf => sf.IsRef).ToList(),

                // A reference that is a member of a record resolves inside the element rather
                // than beside it, so it is a loop of its own. Read off the wire columns, which
                // is the same list the read path walks - the two have to agree about where the
                // key landed. spec/references/references-in-records.md.
                RecordFields = table.WireColumns
                                    .Where(wire => wire.Member is not null && wire.IsRef)
                                    .Select(BuildRecordReference)
                                    .ToList(),


            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                         )
            .Select(x => new GoCrossReferenceView
            {
                Table = GoName(x.Table.Name),
                Fields = x.Fields.Select(sf => new GoReferenceFieldView
                {
                    Name = ResolvesToRow(sf.FirstField!)
                        ? GoName(sf.Name)
                        : GoName(sf.Name) + "Index",

                    RowName = ResolvesToRow(sf.FirstField!)
                        ? GoName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                        : GoName(sf.Name),

                    RefTable = GoName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = ReferenceValueExpression(sf),
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
    /// No resolution flag: a nil pointer says whether it resolved, which is how this output
    /// already answers that for a reference outside a record. What the loop ranges over says
    /// which of the three record shapes this is - the group's slice, the member's, or neither.
    /// spec/references/references-in-records.md.
    /// </remarks>
    private GoRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = EntryName(wire);
        string member = string.Concat(wire.MemberPath.Select(part => "." + GoName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsArray;

        string rowLeaf = wire.Member is not null
            ? GoName(RowAccessorName(refTable!.Name, wire.MemberPath[^1]))
            : GoName(RowAccessorName(refTable!.Name, wire.Group.Name));

        string rowMember = wire.Member is not null
            ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                .Select(part => "." + GoName(part))) + "." + rowLeaf
            : "";

        string keyPath = !isArray || wire.MemberOwnsTheArray
            ? $"record.{name}{member}"
            : $"record.{name}[k]{member}";

        string rowPath = wire.Member is not null
            ? (!isArray || wire.MemberOwnsTheArray
                ? $"record.{name}{rowMember}"
                : $"record.{name}[k]{rowMember}")
            : $"record.{rowLeaf}";

        string subscript = (isArray && wire.MemberOwnsTheArray) ? "[k]" : "";

        return new GoRecordReferenceView
        {
            Access = rowPath + subscript,
            Key = keyPath + subscript,

            // Whichever slice holds the elements - ranged rather than counted, because a
            // trimming group's rows differ in how many they carry.
            Range = isArray
                ? (wire.MemberOwnsTheArray ? keyPath : $"record.{name}")
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
        // spec/references/reference-key-types.md.
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
                // being pointed at. spec/references/reference-key-types.md.
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

    /// <summary>
    /// How a constant's type is spelled, arrays included.
    /// </summary>
    /// <remarks>
    /// The type functions answer for an element and let the caller add the brackets, exactly
    /// as a field's do - so an array constant asks for the element and wraps it here.
    /// spec/layout/primary-layout.md section 8.5.
    /// </remarks>
    private string ConstantTypeName(ConstantSet.Constant constant)
    {
        string element = ToGoTypeName(ValueTypes.ElementOf(constant.Type), constant.Enum, null);

        return ValueTypes.IsArray(constant.Type) ? LanguageProfile.Go.ArrayOf(element) : element;
    }

    /// <summary>
    /// The literal a constant is written as.
    /// </summary>
    /// <remarks>
    /// **An array constant is its elements in this language's list literal.** The element
    /// spelling is the scalar one, so this wraps rather than repeats it - and the wrapping is
    /// where the languages differ far more than the elements do.
    ///
    /// A constant never reaches the file, so there is no wire question here: what a language
    /// needs is an expression its compiler accepts in the place a constant is declared.
    /// spec/layout/primary-layout.md section 8.5.
    /// </remarks>
    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        if (!ValueTypes.IsArray(constant.Type))
            return RenderConstantScalar(constant, constant.Type, constant.Value);

        var element = ValueTypes.ElementOf(constant.Type);

        string joined = string.Join(", ",
            ((System.Array)constant.Value!).Cast<object?>()
                .Select(value => RenderConstantScalar(constant, element, value)));

        return "[]" + ToGoTypeName(element, constant.Enum, null) + "{" + joined + "}";
    }

    /// <summary>One element, or a constant that is one value.</summary>
    private string RenderConstantScalar(
        ConstantSet.Constant constant, ValueType type, object? value)
    {
        switch (type)
        {
            case ValueType.String:
                return Quote((string)value!);

            case ValueType.Bool:
                return (bool)value! ? "true" : "false";

            case ValueType.Int32:
                return ((int)value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Float:
                return ((float)value!).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.Double:
                return ((double)value!).ToString("R", CultureInfo.InvariantCulture);

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return "tabbit.UUID{" + string.Join(", ",
                    ((Guid)value!).ToByteArray()
                        .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + "}";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(value!, constant.Location);
                return constant.Enum.Name.ToPascalCase() + label.Name.ToPascalCase();
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", type),
                            ("Generator", "go")));
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

    /// <summary>The type a single-column lookup takes.</summary>
    /// <remarks>
    /// **A reference column's index is keyed by the target's key, not the target's row.**
    /// The column's own name holds the key and the derived name holds the row, so a lookup
    /// typed by the row is one nobody can call - what a caller has is the id it read
    /// somewhere else. `KeyComponentView.TypeOf` is the one place that decides, and the
    /// composite path already asks it; this is the single-column path asking the same.
    /// </remarks>
    /// <summary>The grid accessor for this table, or null when it is not a grid's values.</summary>
    private GoMatrixView? BuildMatrix(Table table)
    {
        if (MatrixPlans.Of(table, _model) is not { } plan)
            return null;

        return new GoMatrixView
        {
            ColumnTable = plan.Columns.Name.ToPascalCase() + "Table",
            ColumnTableName = plan.Columns.Name,
            RowKeyMember = GoName(plan.RowKey.Name),
            RowKeyParam = plan.RowKey.Name.ToCamelCase(),
            RowKeyType = IndexKeyType(plan.RowKey),
            RowLookup = "FindBy" + plan.RowKey.Name.ToPascalCase(),
            ColumnKeyMember = GoName(plan.ColumnKey.Name),
            ColumnKeyParam = plan.ColumnKey.Name.ToCamelCase(),
            ColumnKeyType = IndexKeyType(plan.ColumnKey),
            AtMember = GoName(plan.At.Name),
            GridMember = GoName(plan.Grid.Name),
            GridHasMember = "Has" + plan.Grid.Name.ToPascalCase() + "At",
            CellType = ToGoTypeName(
                plan.Grid.FirstField!.ElementType, plan.Grid.FirstField!.EnumOrNull, null),
            CellsAreOptional = plan.CellsAreOptional,
        };
    }

    private string IndexKeyType(SerialField only)
    {
        var (type, enumm) = KeyComponentView.TypeOf(only);
        return ToGoTypeName(type, enumm, null);
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
