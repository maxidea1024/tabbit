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
/// Settings for the Swift target.
/// </summary>
public sealed class SwiftRecipe : IOutputRecipe
{
    /// <summary>Source root. The generated files live directly under it.</summary>
    public string Path { get; set; } = "";

    /// <summary>Name of the accessor class, which also names the file.</summary>
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
    /// Whether to write a `Package.swift` that makes the output a SwiftPM package.
    /// </summary>
    /// <remarks>
    /// Off by default, which is the same judgement the Rust target makes about `Cargo.toml`:
    /// dropping the sources into a project that already has a build is the commoner case, and
    /// there a manifest is in the way.
    ///
    /// The manifest declares one target over the flat layout rather than moving the files into
    /// `Sources/`, so turning this on adds a file and moves none.
    /// </remarks>
    public bool WriteManifest { get; set; } = false;

    /// <summary>Module name the generated manifest declares. Only read when it is written.</summary>
    public string ModuleName { get; set; } = "GameData";

    /// <summary>
    /// The swift-crypto requirement the generated manifest declares.
    /// </summary>
    /// <remarks>
    /// A recipe setting rather than a constant, for the reason the Rust target's `ureq`
    /// version is one: the package that has to build is the consumer's and its resolved
    /// versions are theirs to pin.
    ///
    /// What it is for is one call - the MAC's HMAC-SHA-256, which reaches the CPU's SHA
    /// extensions through it and does not through a hand-written one. On Apple platforms
    /// CryptoKit answers instead and the package is never fetched.
    /// spec/targets/swift-language-support.md · doc/languages/swift.md.
    /// </remarks>
    public string SwiftCryptoVersion { get; set; } = "3.0.0";

    /// <summary>
    /// Whether generated files this run did not write are removed from <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// On, because the output is a file per table: delete a table from the sheets and its
    /// file stays behind naming types nothing declares any more. Only files carrying this
    /// tool's own header are removed, so a directory holding your own source is safe.
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
/// Emits a file per table, per enum and per constant set, plus the accessor and the reader.
/// </summary>
/// <remarks>
/// Rows are classes and record elements are structs, which is the one shape decision this
/// target makes differently from the languages that have only one kind of type: a resolved
/// reference has to be a pointer at the row rather than a copy of it, and an element inside
/// a row has no identity worth an allocation. spec/targets/swift-language-support.md.
///
/// The shapes live in templates/swift-*.sbn.
/// </remarks>
[TabbitTarget("swift", TargetKind.CodeGeneration, Order = 86)]
public class SwiftCodeGenerator : CodeGenerator<SwiftRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Run` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private SwiftRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Camel;

    /// <summary>
    /// A record group generates a nested struct and an array of it; a member column fills one
    /// of its properties.
    /// </summary>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a struct nested in the same record, reached by a longer member path.
    /// Its member is initialized by calling that struct. spec/types/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// `Dictionary` and `Set`, with the arrays beside them holding the file's order - this
    /// language's hash containers have none. spec/types/set-and-map.md section 7.
    /// </summary>
    protected override bool SupportsContainers => true;

    /// <summary>
    /// An optional column becomes a `has{Field}` property beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `T?`, which is the shape Swift would suggest. Every generated property is
    /// initialized rather than optional for the reason this repeats: a caller reading a value
    /// should not have to answer for a row the read never reached, and in Swift that answer
    /// would be an unwrap at every use. spec/types/optional-fields.md has the rest.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`findByStageAndSlot(stageKey, slotKey)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// An array whose elements may be absent gets a `[Bool]` beside it, one entry per element.
    /// </summary>
    /// <remarks>
    /// A separate flag from <see cref="SupportsOptionalFields"/> because it is a separate
    /// bitmap in the block and a separate member in the output. The element still occupies its
    /// place in the array - what the bitmap changes is what the row says about it.
    /// spec/types/nullable-array-elements.md.
    /// </remarks>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, SwiftRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Camel, "swift");

        Generate();
        WriteBinaryReaderRuntime();
    }

    /// <summary>The accessor type's name, in the casing this language's types use.</summary>
    private string AccessorType => _recipe.AccessorName.ToPascalCase();

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Swift into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        Write(AccessorType + ".swift", "swift-accessor.sbn", view);

        foreach (var table in view.Tables)
            Write(System.IO.Path.Combine("tables", table.TableName + ".swift"),
                  "swift-table.sbn", Part(table: table));

        foreach (var enumm in view.Enums)
            Write(System.IO.Path.Combine("enums", enumm.Name + ".swift"),
                  "swift-enum.sbn", Part(enumm: enumm));

        // A struct is an entity beside a table and an enum, so it gets a file of its own - one
        // per declaration however many tables named it. spec/types/polymorphism.md section 7.1.
        foreach (var declared in _model.PolymorphicTypes)
        {
            Write(System.IO.Path.Combine("structs", declared.Name + ".swift"),
                  "swift-struct.sbn", Part(structure: BuildStruct(declared)));
        }

        foreach (var set in view.ConstantSets)
            Write(System.IO.Path.Combine("constants", set.Name + ".swift"),
                  "swift-constants.sbn", Part(set: set));

        // Asked for rather than assumed: a project that already has a build wants the
        // sources and not a manifest describing them.
        if (_recipe.WriteManifest)
            WriteManifest(view);
    }

    /// <summary>
    /// One abstract type and its variants, as the template reads them.
    /// </summary>
    /// <remarks>
    /// An `enum` with associated values, which is this language's sum type - and like Kotlin's
    /// sealed class it makes a `switch` exhaustive, so a variant added to the declaration is a
    /// compile error at every consumer rather than a silent default.
    /// spec/types/polymorphism.md section 7.
    /// </remarks>
    private SwiftPolymorphicTypeView BuildStruct(Models.PolymorphicType declared)
        => new SwiftPolymorphicTypeView
        {
            Name = declared.Name,
            BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
            Variants = declared.Variants
                .Select(variant => new SwiftVariantView
                {
                    TypeName = variant.Name,
                    CaseName = SwiftName(variant.Name),
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),
        };

    /// <summary>One member of an abstract type or of one of its variants.</summary>
    /// <remarks>
    /// **A reference member is two of these**, as a reference is anywhere: the declared name is
    /// the key's and the row it resolves to takes the derived one. A variant carrying only the
    /// key would hand a consumer a key where the declaration promised a row.
    /// spec/references/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    private SwiftStructMemberView StructMember(Models.Field field)
    {
        string raw = field.NamePath is { Count: > 1 } ? field.NamePath[^1].Name : field.Name;
        bool toRow = field.IsRef && field.ResolvedRefTable is not null && ResolvesToRow(field);

        return new SwiftStructMemberView
        {            Name = SwiftName(raw),
            TypeName = toRow
                ? field.ResolvedRefTable!.Name.ToPascalCase() + "Record?"
                : ToSwiftTypeName(
                field.Type,
                field.Type is Models.ValueType.Enum or Models.ValueType.EnumArray
                    ? field.Enum
                    : null,
                field.RefTableName),
            Comment = CommentLines(field.Comment),
            RowName = toRow
                ? SwiftName(RowAccessorName(field.ResolvedRefTable!.Name, raw))
                : "",
            KeyTypeName = field.IsRef
                ? ToSwiftTypeName(field.RefKeyType, null, null)
                : "",
        };
    }

    private SwiftPartView Part(
        SwiftTableView? table = null, SwiftEnumView? enumm = null, SwiftConstantSetView? set = null, SwiftPolymorphicTypeView? structure = null)
        => new SwiftPartView
        {
            AccessorName = AccessorType,
            Table = table,
            Enumm = enumm,
            Set = set,
            Structure = structure,
        };

    private void Write(string relative, string templateName, object view)
    {
        string filename = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(_recipe.Path, relative));

        StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render(templateName, view));
    }

    /// <summary>
    /// The manifest, which names the one dependency this output can have.
    /// </summary>
    /// <remarks>
    /// Rendered here rather than from a template because it is nine lines with three values
    /// in them, and a template file would be a fourth place to look for a build failure.
    /// </remarks>
    private void WriteManifest(SwiftFileView view)
    {
        var sources = new List<string> { "\"tabbit\"", $"\"{AccessorType}.swift\"" };

        if (view.Tables.Count > 0) sources.Add("\"tables\"");
        if (view.Enums.Count > 0) sources.Add("\"enums\"");
        if (view.ConstantSets.Count > 0) sources.Add("\"constants\"");

        var text = new StringBuilder();

        text.AppendLine("// swift-tools-version:5.9");
        text.AppendLine("// ------------------------------------------------------------------------------");
        text.AppendLine("// Generated by Tabbit. DO NOT EDIT.");
        text.AppendLine("//");
        text.AppendLine("// Changes to this file may cause incorrect behavior and will be lost if the code is");
        text.AppendLine("// regenerated.");
        text.AppendLine("// ------------------------------------------------------------------------------");
        text.AppendLine("//");
        text.AppendLine("// One target over the files as they are, rather than the Sources/<Target> layout:");
        text.AppendLine("// the commoner use of this output is to drop it into a project that already has a");
        text.AppendLine("// build, and moving the files for the sake of a manifest that may not be wanted");
        text.AppendLine("// would make that use the awkward one.");
        text.AppendLine("//");
        text.AppendLine("// swift-crypto is here for one call: the table MAC's HMAC-SHA-256. On Apple");
        text.AppendLine("// platforms CryptoKit answers instead and this dependency is never fetched; without");
        text.AppendLine("// either, the reader still builds and reads every file but cannot verify a MAC.");
        text.AppendLine("// doc/languages/swift.md says what to do in each case.");
        text.AppendLine("import PackageDescription");
        text.AppendLine();
        text.AppendLine("let package = Package(");
        text.AppendLine($"    name: \"{_recipe.ModuleName}\",");

        // The MAC's HMAC-SHA-256 comes from CryptoKit on Apple platforms, and CryptoKit
        // starts at macOS 10.15 / iOS 13. A manifest that says nothing gets SwiftPM's
        // default floor, which is older - and the package then fails to build with
        // "'HMAC' is only available in macOS 10.15 or newer" rather than with anything
        // that names the cause. Declared here so the floor is the reader's, not the
        // toolchain's default.
        text.AppendLine("    platforms: [.macOS(.v10_15), .iOS(.v13), .tvOS(.v13), .watchOS(.v6)],");
        text.AppendLine("    products: [");
        text.AppendLine($"        .library(name: \"{_recipe.ModuleName}\", targets: [\"{_recipe.ModuleName}\"])");
        text.AppendLine("    ],");
        text.AppendLine("    dependencies: [");
        text.AppendLine("        .package(");
        text.AppendLine("            url: \"https://github.com/apple/swift-crypto.git\",");
        text.AppendLine($"            from: \"{_recipe.SwiftCryptoVersion}\")");
        text.AppendLine("    ],");
        text.AppendLine("    targets: [");
        text.AppendLine("        .target(");
        text.AppendLine($"            name: \"{_recipe.ModuleName}\",");
        text.AppendLine("            dependencies: [.product(name: \"Crypto\", package: \"swift-crypto\")],");
        text.AppendLine("            path: \".\",");
        text.AppendLine($"            sources: [{string.Join(", ", sources)}])");
        text.AppendLine("    ]");
        text.AppendLine(")");

        Emit(System.IO.Path.Combine(_recipe.Path, "Package.swift"), text.ToString());
    }

    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Swift.TcbReader.swift",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "TcbReader.swift"));

        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Swift.Updater.swift",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "Updater.swift"));
        }
    }

    // --------------------------------------------------------------- view

    private SwiftFileView BuildView() => new SwiftFileView
    {
        AccessorName = AccessorType,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private SwiftEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new SwiftEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            DefaultLabel = SwiftName(fallback.Name),
            Labels = enumm.Labels.Select(label => new SwiftEnumLabelView
            {
                Name = SwiftCamelName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };
    }

    private SwiftConstantSetView BuildConstantSet(ConstantSet constantSet) => new SwiftConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new SwiftConstantView
        {
            Name = SwiftCamelName(constant.Name),
            Type = ConstantTypeName(constant),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    /// <summary>
    /// The lookups one record struct declares beside its arrays.
    /// </summary>
    /// <remarks>
    /// Neither of this language's hash containers keeps an order, which is what the array
    /// beside it is for. spec/types/set-and-map.md section 7.2.
    /// </remarks>
    private List<string> LookupLines(
        List<RecordMember> members, Models.ContainerKind own, SerialField group)
    {
        var lines = new List<string>();

        foreach (var plan in ContainerPlan.Of(members, own, group))
        {
            if (plan.IsMap)
            {
                string keyType = ElementTypeOf(plan.Source);
                string valueType = plan.ValueIsOneColumn ? ElementTypeOf(plan.Value!) : "Int";
                string name = plan.ValueIsOneColumn ? "byKey" : "indexByKey";

                lines.Add(plan.ValueIsOneColumn
                    ? "/// What each key is mapped to. The arrays hold the file's order."
                    : "/// Where each key sits among the entries - this map's value is a "
                      + "struct, which is a property per column, so there is no one value to "
                      + "answer with.");

                lines.Add($"public var {name}: [{keyType}: {valueType}] = [:]");

                continue;
            }

            lines.Add($"/// The elements of {SwiftName(plan.Source.Name)}, for asking whether "
                      + "one is there.");
            lines.Add($"public var {SwiftName(plan.Source.Name)}Set: "
                      + $"Set<{ElementTypeOf(plan.Source)}> = []");
        }

        return lines;
    }

    /// <summary>The statements filling every lookup in a table, once the rows are read.</summary>
    private List<string> ContainerFillLines(Table table)
    {
        var lines = new List<string>();

        foreach (var plan in ContainerPlan.Of(table))
        {
            string access = "loaded[i]." + SwiftName(plan.Group.Name)
                + string.Concat(plan.Path.Select(name => "." + SwiftName(name)));

            string source = access + "." + SwiftName(plan.Source.Name);

            if (plan.IsMap)
            {
                string name = access + "." + (plan.ValueIsOneColumn ? "byKey" : "indexByKey");

                string stored = plan.ValueIsOneColumn
                    ? access + "." + SwiftName(plan.Value!.Name) + "[j]"
                    : "j";

                lines.Add($"for j in 0..<{source}.count {{");
                lines.Add($"    {name}[{source}[j]] = {stored}");
                lines.Add("}");

                continue;
            }

            lines.Add($"for j in 0..<{source}.count {{");
            lines.Add($"    {access}.{SwiftName(plan.Source.Name)}Set.insert({source}[j])");
            lines.Add("}");
        }

        return lines;
    }

    /// <summary>One value's type, as a generic argument names it.</summary>
    private string ElementTypeOf(RecordMember member)
        => ToSwiftTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null);

    private SwiftTableView BuildTable(Table table) => new SwiftTableView
    {
        RawName = table.Name,
        RecordName = table.Name.ToPascalCase() + "Record",
        TableName = table.Name.ToPascalCase() + "Table",
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        ContainerFill = ContainerFillLines(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),

        // A separate list, because declaring a property is per field and reading is per
        // column - and a record group is one column per member of it.
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<SwiftIndexView> Indexes(Table table)
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
                    Type = ToSwiftTypeName(keyType, keyEnum, null),
                    Member = SwiftName(component.Name),
                    Kind = KeyComponentView.KindOf(keyType),
                };
            }).ToList();

            string args = string.Join(", ", components.Select(component => component.Param));

            return new SwiftIndexView
            {
                Member = SwiftName(plan.Only.Name),
                Suffix = suffix,
                KeyType = plan.IsComposite ? "String" : IndexKeyType(plan.Only),
                MapName = "by" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                IsComposite = plan.IsComposite,
                Components = components,

                Params = plan.IsComposite
                    ? string.Join(", ", components.Select(c => "_ " + c.Param + ": " + c.Type))
                    : "_ key: " + IndexKeyType(plan.Only),

                Argument = plan.IsComposite
                    ? "Self.keyOf" + suffix + "(" + args + ")"
                    : "key",

                ValueFormat = plan.IsComposite
                    ? "(" + string.Join(", ", components.Select(c => @"\(" + c.Param + ")")) + ")"
                    : @"\(key)",

                ValueArgs = plan.IsComposite ? args : "key",
            };
        }).ToList();

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    private static string PrimaryLookup(Table? refTable)
        => "findBy" + refTable!.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase();

    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// The key type's empty value means "points at nothing", and a multi-target column honours
    /// it in every language: the discriminator is a value a consumer reads.
    /// spec/references/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.String => ".isEmpty == false",
            ValueType.Uuid => "!= nil",
            _ => "!= 0",
        };


    private SwiftFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = SwiftName(sf.Name);

        return new SwiftFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Declarations = Declarations(sf, name),
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<SwiftRecordMemberView>(),
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
    /// collects the structs it produces. A nested member is initialized by calling its own
    /// struct, which is how the values inside it reach the empty values a scalar member gets.
    /// spec/types/nested-multi-level.md.
    /// </remarks>
    private List<SwiftRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, SerialField group,
        List<SwiftRecordTypeView> declared)
    {
        var result = new List<SwiftRecordMemberView>();

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
                // No third member for whether it resolved - a nil row says so.
                // spec/references/references-in-records.md.
                var declarations = new List<string>();

                if (member.IsRef)
                {
                    string row = member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";
                    string key = ToSwiftTypeName(member.FirstField!.RefKeyType, null, null);

                    // The member's own name is the key's; the row takes the derived one.
                    // spec/references/reference-surface-naming.md sections 4 and 5.
                    bool toRow = ResolvesToRow(member.FirstField!);
                    string rowName = toRow
                        ? SwiftName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                        : SwiftName(member.Name);
                    string keyName = toRow ? SwiftName(member.Name) : SwiftName(member.Name) + "Index";

                    declarations.Add(member.IsArray
                        ? $"public var {SwiftName(member.Name)}: [{key}] = "
                          + $"[{key}](repeating: {RefKeyDefault(member.FirstField!.RefKeyType)}, "
                          + $"count: {member.Fields.Count})"
                        : $"public var {SwiftName(member.Name)}: {key} = "
                          + RefKeyDefault(member.FirstField!.RefKeyType));

                    declarations.Add(member.IsArray
                        ? $"public var {rowName}: [{row}?] = "
                          + $"[{row}?](repeating: nil, count: {member.Fields.Count})"
                        : $"public var {rowName}: {row}? = nil");
                }
                else
                {
                    // The array is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    string elementType = ToSwiftTypeName(
                        member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null);

                    // Or the member's own cell holding the list, which is what a `set` and
                    // a `map` are. Empty rather than sized there: no declaration knows how
                    // long a row's list is. spec/types/set-and-map.md section 4.
                    declarations.Add($"public var {SwiftName(member.Name)}: "
                                + (member.HoldsList ? $"[{elementType}]" : elementType)
                                + " = "
                                + (member.ListIsInTheCell
                                    ? "[]"
                                    : member.IsArray
                                        ? $"[{elementType}](repeating: {MemberDefault(member)}, "
                                          + $"count: {member.Fields.Count})"
                                        : MemberDefault(member)));
                }


                result.Add(new SwiftRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),
                    Declarations = declarations,
                });

                continue;
            }

            // A level below. The struct name carries the path: both levels are nested in the
            // same record, so two groups each holding a `Position` would otherwise collide.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, group, declared);

            declared.Add(new SwiftRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Lookups = LookupLines(member.Members, member.Container, group),
                Owner = SwiftName(group.Name),
            });

            result.Add(new SwiftRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declarations = new[] { $"public var {SwiftName(member.Name)}: {typeName} = {typeName}()" },
            });
        }

        return result;
    }

    private SwiftFieldView BuildRecordField(Table table, SerialField sf)
    {
        // Which abstract type this group is, if it is one. One per declaration however many
        // tables named it. spec/types/polymorphism.md section 7.1.
        var declaredType = sf.Members
                .FirstOrDefault(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                ?.FirstField?.AbstractTypeName is { } abstractName
            ? _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == abstractName.ToPascalCase())
            : null;

        string name = SwiftName(sf.Name);
        string entry = sf.Name.ToPascalCase() + "Entry";

        // Innermost first, so a struct is declared before the one naming it.
        var recordTypes = new List<SwiftRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, entry, sf, recordTypes);

        recordTypes.Add(new SwiftRecordTypeView
        {
            TypeName = entry,
            Members = members,
            IsOutermost = true,
            Lookups = LookupLines(sf.Members, sf.Container, sf),
            Owner = name,
        });

        // An array with its elements already made, where the length is the sheet's column
        // count. A trimmed group starts empty, because its length is the row's.
        string initializer = sf.IsArray
            ? (table.TrimTrailingArrayElements
                ? "[]"
                : $"[{entry}](repeating: {entry}(), count: {sf.RecordElementCount})")
            : $"{entry}()";

        string type = sf.IsArray ? $"[{entry}]" : entry;

        // An array of arrays declares no element type: the outer level has no name for one to
        // belong to, so the inner array is the type. spec/types/nested-multi-level.md.
        if (sf.MembersAreAnonymous)
        {
            string inner = ToSwiftTypeName(
                sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull, null);

            type = $"[[{inner}]]";
            initializer = $"[[{inner}]](repeating: [{inner}](repeating: {MemberDefault(sf.Members[0])}, "
                        + $"count: {sf.RecordElementCount}), count: {sf.Members.Count})";
        }

        return new SwiftFieldView
        {
            VariantsAreArray = declaredType is not null && sf.IsArray,
            EntryAccess = "entry",
            AbstractTypeName = declaredType?.Name ?? "",
            BaseMembers = (declaredType?.BaseMembers ?? []).Select(StructMember).ToList(),
            Variants = (declaredType?.Variants ?? [])
                .Select(variant => new SwiftVariantView
                {
                    TypeName = variant.Name,
                    CaseName = SwiftName(variant.Name),
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),

            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,
            Declarations = new[] { $"public var {name}: {type} = {initializer}" },
            IsRecord = true,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            RecordTypeName = entry,

            Members = members,
            RecordTypes = recordTypes,

            // A record group has no presence of its own: absence inside one is the array's
            // length, not a bit per member.
            IsNullable = false,
            PresenceMember = "",
        };
    }

    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where its values
    /// land.
    /// </summary>
    private SwiftColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new SwiftColumnView
        {
            WireTag = wire.TagCarrier.WireTag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire) ? "try cursor.nextLength()" : "try reader.readCounter32()",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = SwiftName(wire.Group.Name),

            // A record's member column assigns one property of the element rather than the
            // member itself: `record.slot[j].id` instead of `record.slot[j]`.
            MemberAccess = (wire.Member is null)
                ? ""
                : string.Concat(wire.MemberPath.Select(part => "." + SwiftName(part))),

            // A reference member reads into the key beside the row it will resolve to, and the
            // suffix goes on the member rather than after the subscript, because a member that
            // is an array holds one key per element. spec/references/references-in-records.md.
            MemberRefSuffix = "",
            MemberAt = wire.MemberAt,

            // Qualified, because the element struct is nested in the record and this is read
            // from the table class beside it.
            RecordTypeName = wire.Group.IsRecord
                ? $"{table.Name.ToPascalCase()}Record.{wire.Group.Name.ToPascalCase()}Entry"
                : "",

            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,

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
    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : SwiftName("has_" + sf.Name);

    /// <summary>
    /// What a stored key holds before a row is read.
    /// </summary>
    /// <remarks>
    /// Spelled from the key's own type. spec/references/reference-key-types.md.
    /// </remarks>
    private static string RefKeyDefault(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "\"\"",
            ValueType.Uuid => "Tcb.Uuid()",
            _ => "0",
        };

    private string MemberDefault(RecordMember member)
    {
        return member.ElementType switch
        {
            ValueType.String => "\"\"",
            ValueType.Bool => "false",
            ValueType.Float => "0",
            ValueType.Double => "0",
            ValueType.Uuid => "Tcb.Uuid()",
            ValueType.Enum => $"{member.FirstField!.Enum.Name.ToPascalCase()}.of(0)",
            _ => "0",
        };
    }

    /// <summary>
    /// What an absent row's property is set back to, so the binary path lands where the JSON
    /// one does.
    /// </summary>
    /// <remarks>
    /// The property's own type rather than its element's: an optional array declares an array,
    /// and its empty value is an empty array rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsArray)
            return "[]";

        // The resolved property is an optional reference to the target row, and absence there
        // is exactly what nil says.
        if (wire.ElementType == ValueType.ForeignRecord)
            return "nil";

        return wire.ElementType switch
        {
            ValueType.String => "\"\"",
            ValueType.Bool => "false",
            ValueType.Float => "0",
            ValueType.Double => "0",
            ValueType.Uuid => "Tcb.Uuid()",
            ValueType.Enum => $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(0)",
            _ => "0",
        };
    }

    /// <summary>
    /// The property declarations, each initialized.
    /// </summary>
    /// <remarks>
    /// A reference declares the key at the key's own type rather than at `Int32`. That
    /// shortcut is a defect this repository has had once already, in six places per language,
    /// and a table keyed by `bigint` or `string` is what finds it.
    /// spec/references/reference-key-types.md.
    /// </remarks>
    private IReadOnlyList<string> Declarations(SerialField sf, string name)
    {
        string elementType = ResolvedElementType(sf);

        if (sf.IsRef)
        {
            string key = ToSwiftTypeName(sf.FirstField!.RefKeyType, null, null);

            // The column's name is the key's; the row takes the derived one.
            // spec/references/reference-surface-naming.md sections 4 and 5.
            bool toRow = ResolvesToRow(sf.FirstField!);
            string rowName = toRow
                ? SwiftName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                : name;
            string keyName = toRow ? name : name + "Index";

            return sf.IsArray
                ? new[]
                {
                    $"public var {keyName}: [{key}] = []",
                    $"public var {rowName}: [{elementType}] = []",
                }
                : new[]
                {
                    $"public var {keyName}: {key} = {RefKeyDefault(sf.FirstField!.RefKeyType)}",
                    $"public var {rowName}: {elementType}? = nil",
                };
        }

        if (sf.IsArray)
            return new[] { $"public var {name}: [{elementType}] = []" };

        return new[] { $"public var {name}: {elementType} = {DefaultValue(sf)}" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "false";
            case ValueType.Float: return "0";
            case ValueType.Double: return "0";
            case ValueType.Uuid: return "Tcb.Uuid()";
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
        string kind = wire.IsArray ? "Tcb.kindArray" : "Tcb.kindScalar";


        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. spec/references/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "Tcb.elementString",
                ValueType.Int64 => "Tcb.elementI64, Tcb.elementI32, Tcb.elementVarint",
                ValueType.Uuid => "Tcb.elementUuid",
                _ => "Tcb.elementI32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32: accepted = "Tcb.elementI32, Tcb.elementVarint"; break;
                case ValueType.Int64: accepted = "Tcb.elementI64, Tcb.elementI32, Tcb.elementVarint"; break;
                case ValueType.Double: accepted = "Tcb.elementF64, Tcb.elementF32, Tcb.elementI32"; break;
                case ValueType.Float: accepted = "Tcb.elementF32"; break;
                case ValueType.Bool: accepted = "Tcb.elementBool"; break;
                case ValueType.String: accepted = "Tcb.elementString"; break;
                case ValueType.Uuid: accepted = "Tcb.elementUuid"; break;
                case ValueType.Enum: accepted = "Tcb.elementVarint"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "Tcb.elementI64"; break;

                default:
                    throw new TabbitDefectException($"The swift generator cannot check type `{wire.Type}`.");
            }
        }

        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one. A function of its own
        // because the accepted elements are variadic.
        string check = wire.HasOptionalElements
            ? "Tcb.checkColumnWithElements" : "Tcb.checkColumn";

        return $"try {check}(column, \"{tableName}.{wire.Name}\", {kind}, "
            + $"{nullable}, {accepted})";
    }

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from.
    /// </summary>
    private static bool UsesCursor(WireColumn wire)
    {
        // Uuid is the exception: no encoding applies to it, so it has no cursor path.
        if (wire.ElementType == ValueType.Uuid)
            return false;

        if (wire.IsArray)
            return true;

        // A reference reaches the cursor when the key it carries does.
        // spec/references/reference-key-types.md.
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

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or nothing for
    /// a column that reads the reader directly.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"let cursor = try Tcb.ColumnCursor(reader, column, count, \"{tableName}.{wire.Name}\")"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row.
    /// </summary>
    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        // A run says "this many rows hold the same value", which an array column's row does
        // not have one of.
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

    /// <summary>
    /// The line assigning one row from the value the run decoded, inside the loop the
    /// template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string name = SwiftName(wire.Group.Name);
        string memberAccess = (wire.Member is null)
            ? ""
            : string.Concat(wire.MemberPath.Select(part => "." + SwiftName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded. spec/references/references-in-records.md.
        if (wire.IsRef)
        {
            string local = wire.RefKeyType == ValueType.String ? "runSameText" : "runSameValue";

            // A dotted reference resolves to a value rather than a row, so there the column's
            // own name belongs to the value and the key takes the derived one.
            // spec/references/reference-surface-naming.md section 9.
            string keySuffix = ResolvesToRow(wire.TagCarrier) ? "" : "Index";

            return (wire.Member is null)
                ? $"loaded[i].{name}{keySuffix} = cursor.{local}"
                : $"loaded[i].{name}{memberAccess}{keySuffix} = cursor.{local}";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"loaded[i].{name}{memberAccess} = "
                 + $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(cursor.runSameValue)";

        if (wire.ElementType == ValueType.String)
            return $"loaded[i].{name}{memberAccess} = cursor.runSameText";

        return $"loaded[i].{name}{memberAccess} = cursor.runSameValue";
    }

    /// <summary>Which read shape a column takes.</summary>
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

        if (wire.IsArray)
            // A trimmed array of references: the length is the row's, and the key still goes
            // in the array beside the values. Read as a plain `var_array` it appended the key
            // to the array of rows, which does not compile - and nothing held the shape,
            // because `foreign[]` is refused and this is only reachable through a folded group
            // with trimming on. spec/types/variable-length-record-arrays.md.
            return wire.IsRef ? "var_array_ref" : "var_array";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private SwiftAccessorView BuildAccessor() => new SwiftAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new SwiftTableSlotView
        {
            Name = SwiftCamelName(table.Name),
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
                // than beside it, so it is a loop of its own. spec/references/references-in-records.md.
                RecordFields = table.WireColumns
                                    .Where(wire => wire.Member is not null && wire.IsRef)
                                    .Select(BuildRecordReference)
                                    .ToList(),


            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                         )
            .Select(x => new SwiftCrossReferenceView
            {
                Table = SwiftName(x.Table.Name),
                Fields = x.Fields.Select(sf => new SwiftReferenceFieldView
                {
                    Name = ResolvesToRow(sf.FirstField!)
                        ? SwiftName(sf.Name)
                        : SwiftName(sf.Name) + "Index",

                    RowName = ResolvesToRow(sf.FirstField!)
                        ? SwiftName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                        : SwiftName(sf.Name),

                    RefTable = SwiftName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + SwiftName(sf.FirstField!.ResolvedRefField!.Name),
                    IsArray = sf.IsArray,
                }).ToList(),
                RecordFields = x.RecordFields,
            })
            .ToList(),
    };

    /// <summary>One reference that is a member of a record, as the linking pass needs it.</summary>
    private SwiftRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = SwiftName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + SwiftName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsArray;

        string rowLeaf = wire.Member is not null
            ? SwiftName(RowAccessorName(refTable!.Name, wire.MemberPath[^1]))
            : SwiftName(RowAccessorName(refTable!.Name, wire.Group.Name));

        string rowMember = wire.Member is not null
            ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                .Select(part => "." + SwiftName(part))) + "." + rowLeaf
            : "";

        string rowPath = wire.Member is not null
            ? (!isArray || wire.MemberOwnsTheArray
                ? $"record.{name}{rowMember}"
                : $"record.{name}[i]{rowMember}")
            : $"record.{rowLeaf}";

        string path = !isArray || wire.MemberOwnsTheArray
            ? $"record.{name}{member}"
            : $"record.{name}[i]{member}";
        string subscript = (isArray && wire.MemberOwnsTheArray) ? "[i]" : "";

        return new SwiftRecordReferenceView
        {
            Access = rowPath + subscript,
            Key = path + subscript,

            // Whichever array holds the elements. Its own size rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Count = isArray
                ? (wire.MemberOwnsTheArray ? $"{path}.count" : $"record.{name}.count")
                : "",

            RefTable = SwiftName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The expression one value reads: through the cursor where the column can arrive
    /// encoded - which also carries the lossless promotions - and the direct read
    /// everywhere else.
    /// </summary>
    private string ValueReadExpression(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return ReadExpression(wire);

        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => "try cursor.nextI64()",
                ValueType.String => "try cursor.nextString()",
                _ => "try cursor.nextI32()",
            };
        }

        switch (wire.ElementType)
        {
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(try cursor.nextI32())";

            case ValueType.Int32: return "try cursor.nextI32()";
            case ValueType.Int64: return "try cursor.nextI64()";
            case ValueType.Double: return "try cursor.nextF64()";
            case ValueType.Float: return "try cursor.nextF32()";
            case ValueType.Bool: return "try cursor.nextBool()";

            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "try cursor.nextI64()";

            default: return "try cursor.nextString()";
        }
    }

    private string ReadExpression(WireColumn wire)
    {
        switch (wire.ElementType)
        {
            // Enum values travel zig-zag encoded rather than fixed width.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(try reader.readEnum())";

            // The key the target is addressed by, which is not always an int32.
            // spec/references/reference-key-types.md.
            case ValueType.ForeignRecord:
                return "try " + LanguageProfile.Swift.ReadCall(wire.RefKeyType);

            default: return "try " + LanguageProfile.Swift.ReadCall(wire.ElementType);
        }
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        return ToSwiftTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull, null);
    }

    private string ToSwiftTypeName(ValueType type, Models.Enum? enumm, string? refTableName)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return enumm!.Name.ToPascalCase();

            case ValueType.ForeignRecord:
                return refTableName.ToPascalCase() + "Record";

            default:
                return LanguageProfile.Swift.ScalarTypeName(type);
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
        string element = ToSwiftTypeName(ValueTypes.ElementOf(constant.Type), constant.Enum, null);

        return ValueTypes.IsArray(constant.Type) ? LanguageProfile.Swift.ArrayOf(element) : element;
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

        string joined = string.Join(", ",
            ((System.Array)constant.Value!).Cast<object?>()
                .Select(value => RenderConstantScalar(
                    constant, ValueTypes.ElementOf(constant.Type), value)));

        return "[" + joined + "]";
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
                return FloatLiteral((float)value!);

            case ValueType.Double:
                return DoubleLiteral((double)value!);

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return "Tcb.Uuid([" + string.Join(", ",
                    ((Guid)value!).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "])";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(value!, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}.{SwiftCamelName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", type),
                            ("Generator", "swift")));
        }
    }

    /// <summary>
    /// A `Double` as Swift source, by its bits where a decimal literal would not do.
    /// </summary>
    /// <remarks>
    /// Three values a decimal literal cannot carry into Swift, and all three can reach a
    /// sheet.
    ///
    /// A subnormal - the corpus has 5e-324, the smallest one there is - parses to the right
    /// value but *warns* that it underflowed, and a consuming project that builds with
    /// warnings as errors would not compile what we generated. Infinity and NaN have no
    /// literal at all; `Infinity` and `NaN`, which is what .NET renders them as, are two
    /// undeclared identifiers.
    ///
    /// The bit pattern is exact in every case, which is the same argument the reader makes
    /// for reading a float as its stored bits rather than through a decimal rendering.
    /// </remarks>
    private static string DoubleLiteral(double value)
    {
        if (double.IsNaN(value)) return "Double.nan";
        if (double.IsPositiveInfinity(value)) return "Double.infinity";
        if (double.IsNegativeInfinity(value)) return "-Double.infinity";

        if (!double.IsNormal(value) && value != 0)
        {
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
            return $"Double(bitPattern: 0x{bits:x16})";
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>The same for a `Float`. See <see cref="DoubleLiteral"/>.</summary>
    private static string FloatLiteral(float value)
    {
        if (float.IsNaN(value)) return "Float.nan";
        if (float.IsPositiveInfinity(value)) return "Float.infinity";
        if (float.IsNegativeInfinity(value)) return "-Float.infinity";

        if (!float.IsNormal(value) && value != 0)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(value);
            return $"Float(bitPattern: 0x{bits:x8})";
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A Swift string literal.
    /// </summary>
    /// <remarks>
    /// Backslash escapes as in C, plus `\(` - which starts an interpolation in a Swift
    /// string and so has to be escaped even though a bare `(` does not.
    /// </remarks>
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
            else if (c == '\0')
                literal.Append(@"\0");
            else if (c < 0x20)
                literal.Append("\\u{" + ((int)c).ToString("x2", CultureInfo.InvariantCulture) + "}");
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
    private string IndexKeyType(SerialField only)
    {
        var (type, enumm) = KeyComponentView.TypeOf(only);
        return ToSwiftTypeName(type, enumm, null);
    }

    /// <summary>
    /// A member name in Swift's casing, escaped in backticks when it lands on a keyword.
    /// </summary>
    private string SwiftName(string name)
        => LanguageProfile.Swift.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for the names that are not members - an enum case, a constant,
    /// an accessor's per-table slot.
    /// </summary>
    private static string SwiftCamelName(string name) => LanguageProfile.Swift.MemberName(name.ToCamelCase());
}
