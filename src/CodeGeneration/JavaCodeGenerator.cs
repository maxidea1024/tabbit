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
/// Settings for the Java target.
/// </summary>
public sealed class JavaRecipe : IOutputRecipe
{
    /// <summary>Source root. The package's directories are created underneath it.</summary>
    public string Path { get; set; } = "";

    /// <summary>Package the generated accessor declares.</summary>
    public string PackageName { get; set; } = "gamedata";

    /// <summary>
    /// Name of the accessor class, and so of its file.
    ///
    /// Every generated type used to nest inside it. They are one file each now, because
    /// Java demands a public type be alone in a file named after it.
    /// </summary>
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
/// Emits a Java package: a file per generated type, plus the binary reader.
///
/// A file each rather than nested types, which is what Java asks for - a public top-level
/// type has to be alone in a file named after it. Two files per table, then, one for the
/// record and one for the table: the alternative was nesting the record inside the table and
/// calling it `VectorsTable.Record`, and a worse name is not worth one fewer file.
///
/// All in one package and flat, so nothing imports another generated type. Same as Go.
///
/// The shape lives in templates/java-*.sbn, one per kind of file, over the shared header in
/// java-file-head.sbn.
/// </summary>
[TabbitTarget("java", TargetKind.CodeGeneration, Order = 80)]
public class JavaCodeGenerator : CodeGenerator<JavaRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private JavaRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Camel;

    /// <summary>
    /// A record group generates a nested class and an array of it; a member column fills one
    /// of its fields.
    /// </summary>
    /// <remarks>
    /// The eighth of the thirteen, and the same split as the seven before it - declaration
    /// per field, reading per wire column.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a class nested in the same record, and the read reaches it with a longer
    /// member path. Its field is constructed at its declaration, because Java would otherwise
    /// leave it null. spec/types/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// `LinkedHashMap` and `LinkedHashSet`, which keep insertion order - so the lookup and
    /// the array agree on what the second entry is. spec/types/set-and-map.md section 7.
    /// </summary>
    protected override bool SupportsContainers => true;

    /// <summary>
    /// An optional column becomes a `has{Field}` field beside the value one.
    /// </summary>
    /// <remarks>
    /// Not a boxed type. It has to work the same for a `String` as for an `int`, and boxing
    /// every optional member would put an allocation and an unboxing between the caller and
    /// every value. spec/types/optional-fields.md has the reasoning.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`findByStageAndSlot(stageKey, slotKey)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/types/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, JavaRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Camel, "java");

        Generate();
        WriteBinaryReaderRuntime();
    }

    /// <summary>The accessor type's name, in the casing this language's types use.</summary>
    private string AccessorType => _recipe.AccessorName.ToPascalCase();

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Java into `{System.IO.Path.GetFullPath(PackageDir)}`");

        // The accessor holds a field per table and links the references between them. No
        // reader: it never touches a byte itself.
        Write(_recipe.AccessorName, "java-accessor.sbn", new JavaPartView
        {
            PackageName = _recipe.PackageName,
            AccessorName = AccessorType,
            Imports = Imports(new[] { "java.nio.file.Paths" }, reader: false),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A record reads itself, so it names the reader. Its enum-typed fields name
            // enums, and its references name other records - all in this package, so
            // neither is an import.
            Write(pair.rendered.RecordName, "java-record.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Imports(Array.Empty<string>(), reader: true),
                Table = pair.rendered,
            });

            // A table holds the rows and the index, and opens the file.
            Write(pair.rendered.TableName, "java-table.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                AccessorName = AccessorType,
                Imports = Imports(
                    new[]
                    {
                        "java.nio.file.Path", "java.util.ArrayList", "java.util.HashMap",
                        "java.util.List", "java.util.Map",
                    },
                    reader: true),
                Table = pair.rendered,
            });
        }

        foreach (var enumm in view.Enums)
        {
            // An enum is a leaf: it names nothing but the integers it is built from.
            Write(enumm.Name, "java-enum.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Array.Empty<string>(),
                Enumm = enumm,
            });
        }

        // A struct is an entity beside a table and an enum, so it gets a file of its own - one
        // per declaration however many tables named it. The variants are nested classes rather
        // than files of their own: this language allows one public type per file, and a set
        // whose members are scattered over four files is a set nothing holds together.
        // spec/types/polymorphism.md section 7.1.
        foreach (var declared in _model.PolymorphicTypes)
        {
            Write(declared.Name, "java-struct.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Array.Empty<string>(),
                Structure = BuildStruct(declared),
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant set names an enum when one of its constants is typed with one -
            // same package, so no import - and the reader when one is a uuid, whose type is
            // TcbReader.Uuid.
            Write(pair.rendered.Name, "java-constants.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Imports(Array.Empty<string>(), reader: NamesUuid(pair.model)),
                Set = pair.rendered,
            });
        }
    }

    /// <summary>
    /// Where the files go: Java expects a type's file to sit in a directory matching its
    /// package.
    /// </summary>
    private string PackageDir
        => System.IO.Path.Combine(
            new[] { _recipe.Path }.Concat(_recipe.PackageName.Split('.')).ToArray());

    /// <summary>
    /// Flat inside the package rather than in `tables`, `enums` and `constants`
    /// subpackages.
    /// </summary>
    /// <remarks>
    /// A Java directory is a package, so a subdirectory would be a different one and every
    /// generated type would have to import the others. One package instead: nothing imports
    /// anything of this tool's making, and the names carry the grouping - which is the same
    /// answer Go, Python and Rust arrived at.
    /// </remarks>
    private void Write(string typeName, string templateName, JavaPartView view)
    {
        string full = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(PackageDir, typeName + ".java"));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    /// <summary>
    /// Import lines, with a blank entry where Java convention wants a gap between the
    /// java.* group and the rest.
    /// </summary>
    private static IReadOnlyList<string> Imports(IReadOnlyList<string> standard, bool reader)
    {
        var lines = new List<string>();

        foreach (var name in standard)
            lines.Add($"import {name};");

        if (reader)
        {
            if (lines.Count > 0)
                lines.Add("");

            lines.Add("import tabbit.TcbReader;");
        }

        return lines;
    }

    /// <summary>
    /// Whether a constant set has a uuid in it, which is the only way its file reaches the
    /// reader - the constant's own type is TcbReader.Uuid.
    /// </summary>
    private static bool NamesUuid(ConstantSet set)
        => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

    private void WriteBinaryReaderRuntime()
    {
        // Its own `tabbit` package, so the generated accessor's package is free to be
        // anything the consumer wants.
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Java.TcbReader.java",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "TcbReader.java"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Java.TabbitUpdater.java",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "TabbitUpdater.java"));
        }
    }

    // --------------------------------------------------------------- view

    private JavaFileView BuildView() => new JavaFileView
    {
        PackageName = _recipe.PackageName,
        AccessorName = AccessorType,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private JavaEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new JavaEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            DefaultLabel = JavaConstantName(fallback.Name),
            Labels = enumm.Labels.Select((label, index) => new JavaEnumLabelView
            {
                Name = JavaConstantName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
                Separator = index == enumm.Labels.Count - 1 ? ";" : ",",
            }).ToList(),
        };
    }

    /// <summary>
    /// One abstract type and its variants, as the template reads them.
    /// </summary>
    /// <remarks>
    /// The members are columns, so their types come out of the same conversion a table's do.
    /// spec/types/polymorphism.md section 7.1.
    /// </remarks>
    private JavaPolymorphicTypeView BuildStruct(Models.PolymorphicType declared)
        => new JavaPolymorphicTypeView
        {
            Name = declared.Name,
            BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
            Variants = declared.Variants
                .Select(variant => new JavaVariantView
                {
                    TypeName = variant.Name,
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
    private JavaStructMemberView StructMember(Models.Field field)
    {
        string raw = field.NamePath is { Count: > 1 } ? field.NamePath[^1].Name : field.Name;
        bool toRow = field.IsRef && field.ResolvedRefTable is not null && ResolvesToRow(field);

        return new JavaStructMemberView
        {            Name = JavaName(raw),
            TypeName = toRow
                ? field.ResolvedRefTable!.Name.ToPascalCase() + "Record"
                : ToJavaTypeName(
                field.Type,
                field.Type is Models.ValueType.Enum or Models.ValueType.EnumArray
                    ? field.Enum
                    : null,
                field.RefTableName),
            Comment = CommentLines(field.Comment),
            RowName = toRow
                ? JavaName(RowAccessorName(field.ResolvedRefTable!.Name, raw))
                : "",
            KeyTypeName = field.IsRef
                ? ToJavaTypeName(field.RefKeyType, null, null)
                : "",
        };
    }

    private JavaConstantSetView BuildConstantSet(ConstantSet constantSet) => new JavaConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new JavaConstantView
        {
            Name = JavaConstantName(constant.Name),
            Type = ConstantTypeName(constant),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    /// <summary>
    /// The lookups one record class declares beside its arrays.
    /// </summary>
    /// <remarks>
    /// `LinkedHashMap` and `LinkedHashSet` rather than the plain ones: they keep insertion
    /// order, and the insertion order is the file's - so iterating a lookup gives back what
    /// the sheet wrote. spec/types/set-and-map.md section 7.2.
    /// </remarks>
    private List<string> LookupLines(
        List<RecordMember> members, Models.ContainerKind own, SerialField group)
    {
        var lines = new List<string>();

        foreach (var plan in ContainerPlan.Of(members, own, group))
        {
            if (plan.IsMap)
            {
                string keyType = Boxed(plan.Source);

                string valueType = plan.ValueIsOneColumn ? Boxed(plan.Value!) : "Integer";
                string name = plan.ValueIsOneColumn ? "byKey" : "indexByKey";

                lines.Add(plan.ValueIsOneColumn
                    ? "/** What each key is mapped to. Insertion order, which is the file's. */"
                    : "/** Where each key sits among the entries - this map's value is a "
                      + "struct, which is a field per column, so there is no one value to "
                      + "answer with. */");

                lines.Add($"public java.util.LinkedHashMap<{keyType}, {valueType}> {name} = "
                          + $"new java.util.LinkedHashMap<>();");

                continue;
            }

            string element = Boxed(plan.Source);
            string set = JavaName(plan.Source.Name) + "Set";

            lines.Add($"/** The elements of {JavaName(plan.Source.Name)}, for asking whether "
                      + "one is there. */");
            lines.Add($"public java.util.LinkedHashSet<{element}> {set} = "
                      + $"new java.util.LinkedHashSet<>();");
        }

        return lines;
    }

    /// <summary>
    /// The statements filling every lookup in a table, once the rows are read.
    /// </summary>
    private List<string> ContainerFillLines(Table table)
    {
        var lines = new List<string>();

        foreach (var plan in ContainerPlan.Of(table))
        {
            string access = "record." + JavaName(plan.Group.Name)
                + string.Concat(plan.Path.Select(name => "." + JavaName(name)));

            string source = access + "." + JavaName(plan.Source.Name);

            if (plan.IsMap)
            {
                string name = access + "." + (plan.ValueIsOneColumn ? "byKey" : "indexByKey");

                string stored = plan.ValueIsOneColumn
                    ? access + "." + JavaName(plan.Value!.Name) + "[j]"
                    : "j";

                lines.Add($"for (int j = 0; j < {source}.length; j++)");
                lines.Add($"    {name}.put({source}[j], {stored});");

                continue;
            }

            lines.Add($"for (int j = 0; j < {source}.length; j++)");
            lines.Add($"    {access}.{JavaName(plan.Source.Name)}Set.add({source}[j]);");
        }

        return lines;
    }

    /// <summary>
    /// A member's type as a generic argument, where the language has no primitive one.
    /// </summary>
    private string Boxed(RecordMember member)
        => MemberTypeName(member) switch
        {
            "int" => "Integer",
            "long" => "Long",
            "float" => "Float",
            "double" => "Double",
            "boolean" => "Boolean",
            "short" => "Short",
            "byte" => "Byte",
            "char" => "Character",
            var other => other,
        };

    private JavaTableView BuildTable(Table table) => new JavaTableView
    {
        RawName = table.Name,
        RecordName = table.Name.ToPascalCase() + "Record",
        TableName = table.Name.ToPascalCase() + "Table",
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        ContainerFill = ContainerFillLines(table),

        // One cursor variable for the whole read method, assigned per column before its
        // row loop rather than declared once per case. Asked of the columns, because that
        // is what the switch has a case for.
        NeedsCursor = table.WireColumns.Any(UsesCursor),

        NeedsPresence = table.WireColumns.Any(wire => wire.IsNullable),
        NeedsElementPresence = table.WireColumns.Any(wire => wire.HasOptionalElements),

        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),

        // A separate list, because declaring a member is per field and reading is per
        // column - and a record group is one column per member of it.
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<JavaIndexView> Indexes(Table table)
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
                    Type = ToJavaTypeName(keyType, keyEnum, null),
                    Member = JavaName(component.Name),
                    Kind = KeyComponentView.KindOf(keyType),
                };
            }).ToList();

            string args = string.Join(", ", components.Select(component => component.Param));

            return new JavaIndexView
            {
                Member = JavaName(plan.Only.Name),
                Suffix = suffix,
                KeyType = plan.IsComposite ? "String" : Boxed(IndexKeyType(plan.Only)),
                KeyParam = plan.IsComposite ? "String" : IndexKeyType(plan.Only),
                MapName = "by" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                IsComposite = plan.IsComposite,
                Components = components,

                Params = plan.IsComposite
                    ? string.Join(", ", components.Select(c => c.Type + " " + c.Param))
                    : IndexKeyType(plan.Only) + " key",

                Argument = plan.IsComposite ? "keyOf" + suffix + "(" + args + ")" : "key",

                ValueFormat = plan.IsComposite
                    ? "(" + string.Join(", ", components.Select(_ => "%s")) + ")"
                    : "%s",

                ValueArgs = plan.IsComposite ? args : "key",
            };
        }).ToList();

    /// <summary>
    /// The reference type standing in for a primitive, because a Map cannot be keyed by
    /// one.
    /// </summary>
    private static string Boxed(string type)
    {
        return type switch
        {
            "boolean" => "Boolean",
            "int" => "Integer",
            "long" => "Long",
            "float" => "Float",
            "double" => "Double",
            _ => type,
        };
    }

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
    /// spec/references/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "!= null && !$KEY$.isEmpty()",
            ValueType.Uuid => "!= null",
            _ => "!= 0",
        };


    private JavaFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = JavaName(sf.Name);
        string elementType = ResolvedElementType(sf);

        return new JavaFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Declarations = Declarations(sf, name, elementType),
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<JavaRecordMemberView>(),
            IsFixedRecordArray = false,
            ElementCount = 0,
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = PresenceMember(sf),
            ElementPresenceMember = PresenceMember(sf) + "At",
        };
    }

    /// <summary>
    /// Members of one level of a record, declaring a class for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the classes it produces. A nested member is constructed at its declaration -
    /// Java would otherwise leave it null, which is the same crash-one-field-later a null string
    /// member would be. spec/types/nested-multi-level.md.
    /// </remarks>
    private List<JavaRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, SerialField group,
        List<JavaRecordTypeView> declared)
    {
        var result = new List<JavaRecordMemberView>();

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
                // spec/references/references-in-records.md.
                string memberType = member.IsRef
                    ? member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record"
                    : MemberTypeName(member);

                // The member's own name is the key's, because the key is what the cell
                // holds; the row is linked after loading and takes a derived name.
                // spec/references/reference-surface-naming.md sections 4 and 5.
                string rowName = member.IsRef
                    ? JavaName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                    : "";

                var declarations = new List<string>();

                if (member.IsRef)
                {
                    string keyType = ToJavaTypeName(member.FirstField!.RefKeyType, null, null);

                    declarations.Add(
                        keyType + (member.IsArray ? "[] " : " ") + JavaName(member.Name)
                        + (member.IsArray ? $" = new {keyType}[{member.Fields.Count}]" : "")
                        + ";");

                    declarations.Add(
                        memberType + (member.IsArray ? "[] " : " ") + rowName
                        + (member.IsArray ? $" = new {memberType}[{member.Fields.Count}]" : "")
                        + ";");
                }
                else
                {
                    // Or the member's own cell holding the list, which is what a `set` and
                    // a `map` are. Empty rather than sized there: no declaration knows how
                    // long a row's list is, and the read makes the array it fills.
                    // spec/types/set-and-map.md section 4.
                    declarations.Add(
                        memberType + (member.HoldsList ? "[] " : " ") + JavaName(member.Name)
                            + (member.ListIsInTheCell
                                ? $" = new {memberType}[0]"
                                : member.IsArray
                                    ? $" = new {memberType}[{member.Fields.Count}]"
                                    : MemberInitializer(member))
                            + ";");
                }


                result.Add(new JavaRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),

                    // The array is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    Declarations = declarations,
                    Fill = (!member.IsRef && member.IsArray && MemberInitializer(member).Length > 0)
                        ? $"for (int i = 0; i < {member.Fields.Count}; i++) "
                          + $"{JavaName(member.Name)}[i]{MemberInitializer(member)};"
                        : "",
                });

                continue;
            }

            // A level below. The class name carries the path: both are nested in the same
            // record, so two groups each holding a `Position` would otherwise collide.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, group, declared);

            declared.Add(new JavaRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = JavaName(group.Name),
                Lookups = LookupLines(member.Members, member.Container, group),
            });

            result.Add(new JavaRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declarations = new[] { $"{typeName} {JavaName(member.Name)} = new {typeName}();" },
                Fill = "",
            });
        }

        return result;
    }

    /// <summary>
    /// A record group: the class to declare for one element, and the field holding one or
    /// an array of them.
    /// </summary>
    /// <remarks>
    /// A nested class rather than a file of its own, which is what Java would demand of a
    /// public top-level type. Nesting it in the record also scopes the name, so two tables
    /// each holding a `Slot` group do not collide - the other targets without namespaces had
    /// to put the table's name in the type's.
    ///
    /// No reference members: a reference belongs to a member and the model refuses one there,
    /// so nothing here has the index array and the setter a reference would need.
    /// </remarks>
    private JavaFieldView BuildRecordField(Table table, SerialField sf)
    {
        // Which abstract type this group is, if it is one. One per declaration however many
        // tables named it. spec/types/polymorphism.md section 7.1.
        var declaredType = sf.Members
                .FirstOrDefault(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                ?.FirstField?.AbstractTypeName is { } abstractName
            ? _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == abstractName.ToPascalCase())
            : null;

        string name = JavaName(sf.Name);
        string entry = sf.Name.ToPascalCase() + "Entry";
        bool fixedArray = sf.IsArray && !table.TrimTrailingArrayElements;

        // Innermost first, so a class is declared before the one naming it.
        var recordTypes = new List<JavaRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, entry, sf, recordTypes);

        recordTypes.Add(new JavaRecordTypeView
        {
            TypeName = entry,
            Members = members,
            IsOutermost = true,
            Lookups = LookupLines(sf.Members, Models.ContainerKind.None, sf),
            Owner = name,
        });

        // An array of objects arrives null-filled, so the elements are constructed as well
        // as the array. The fixed case can do that at the declaration, as C# does; the
        // trimmed one cannot, because its length is the row's.
        // An array of arrays declares no element type: the outer level has no name for one
        // to belong to, so the inner array is the type. Java sizes both levels in one
        // expression. spec/types/nested-multi-level.md.
        string declaration = sf.MembersAreAnonymous
            ? $"{MemberTypeName(sf.Members[0])}[][] {name} = "
              + $"new {MemberTypeName(sf.Members[0])}[{sf.Members.Count}][{sf.RecordElementCount}];"
            : sf.IsArray
                ? $"{entry}[] {name} = " + (fixedArray
                    ? $"new{entry}Array({sf.RecordElementCount});"
                    : $"new {entry}[0];")
                : $"{entry} {name} = new {entry}();";

        return new JavaFieldView
        {
            VariantsAreArray = declaredType is not null && sf.IsArray,
            EntryAccess = "entry",
            AbstractTypeName = declaredType?.Name ?? "",
            BaseMembers = (declaredType?.BaseMembers ?? []).Select(StructMember).ToList(),
            Variants = (declaredType?.Variants ?? [])
                .Select(variant => new JavaVariantView
                {
                    TypeName = variant.Name,
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),

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

            IsFixedRecordArray = fixedArray,
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
    private JavaColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new JavaColumnView
        {
            WireTag = wire.TagCarrier.WireTag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire)
                ? "elementCount = cursor.nextLength();"
                : "elementCount = reader.readCounter32();",
            RefKeyType = wire.IsRef ? ToJavaTypeName(wire.RefKeyType, null, null) : "",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = JavaName(wire.Group.Name),

            // A record's member column assigns one field of the element rather than the
            // member itself: `record.slot[j].id` instead of `record.slot[j]`.
            MemberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + JavaName(part))),

            // A reference member reads into the key beside the row it will resolve to, and the
            // suffix goes on the member rather than after the subscript, because a member that
            // is an array holds one key per element. spec/references/references-in-records.md.
            MemberRefSuffix = "",

            RowMemberAccess = (wire.Member is not null && wire.IsRef
                               && ResolvesToRow(wire.TagCarrier))
                ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                    .Select(part => "." + JavaName(part)))
                  + "." + JavaName(RowAccessorName(
                        wire.TagCarrier.ResolvedRefTable!.Name, wire.MemberPath[^1]))
                : "",

            RowName = wire.IsRef && wire.TagCarrier.ResolvedRefTable is not null
                        && ResolvesToRow(wire.TagCarrier)
                ? JavaName(RowAccessorName(wire.TagCarrier.ResolvedRefTable.Name, wire.Group.Name))
                : "",
            MemberAt = wire.MemberAt,

            // Qualified, because the element class is nested in the record and this is read
            // from the table class next door.
            RecordTypeName = wire.Group.IsRecord
                ? $"{table.Name.ToPascalCase()}Record.{wire.Group.Name.ToPascalCase()}Entry"
                : "",

            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,
            ElementType = ColumnElementType(wire),
            ReadScalar = ScalarReadExpression(wire),
            ReadElement = ReadExpression(wire),
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = PresenceMember(wire.Group),
            ElementPresenceMember = PresenceMember(wire.Group) + "At",
            EmptyValue = EmptyValue(wire),
        };
    }

    /// <summary>
    /// The field a nullable column's presence lands in.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever reads
    /// it, and the model has already required its columns to agree about being optional.
    /// </remarks>
    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : JavaName("has_" + sf.Name);

    /// <summary>
    /// What an absent row's field is set back to, so the binary path lands where the JSON
    /// one does.
    /// </summary>
    /// <remarks>
    /// The field's own type rather than its element's: an optional array declares `T[]`, and
    /// its empty value is an empty array rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        string elementType = ColumnElementType(wire);

        if (wire.IsArray)
            return $"new {elementType}[0]";

        if (wire.IsRef)
            return "null";

        return wire.ElementType switch
        {
            ValueType.String => "\"\"",
            ValueType.Uuid => "TcbReader.Uuid.empty()",
            ValueType.Enum => $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(0)",
            ValueType.Bool => "false",

            // The rest are primitives whose zero is already the empty value. A float member
            // needs the suffix, because `0` is an int and Java will not narrow one silently.
            ValueType.Float => "0f",
            _ => "0",
        };
    }

    /// <summary>The type one value of a column has, which an array allocation names.</summary>
    /// <remarks>
    /// Both the column and the cell it carries are consulted, because for a reference they
    /// disagree: the column resolves to what the target holds while the cell still says
    /// ForeignRecord.
    /// </remarks>
    private string ColumnElementType(WireColumn wire)
    {
        if (wire.ElementType == ValueType.ForeignRecord)
            return wire.TagCarrier.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        return ToJavaTypeName(wire.TagCarrier.ElementType, wire.TagCarrier.EnumOrNull, null);
    }

    /// <summary>The declared type of one record member.</summary>
    private string MemberTypeName(RecordMember member)
        => ToJavaTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull, null);

    /// <summary>
    /// What a record member is initialized to, for the same reason an ordinary field is: a
    /// reference type left null is a crash one field later.
    /// </summary>
    private string MemberInitializer(RecordMember member)
    {
        return member.ElementType switch
        {
            ValueType.String => " = \"\"",
            ValueType.Uuid => " = TcbReader.Uuid.empty()",
            ValueType.Enum => $" = {member.FirstField!.Enum.Name.ToPascalCase()}.of(0)",
            _ => "",
        };
    }

    /// <summary>
    /// The member declarations, every one of them initialized.
    /// </summary>
    /// <remarks>
    /// Java's own default for a reference is null, and a column the file does not carry
    /// is exactly the case that leaves a member at its default: delete a column, and
    /// code generated before the deletion reads a file that has nothing for it. An
    /// empty string and an empty array are values a consumer can use; null is a crash
    /// one field later.
    ///
    /// A reference is the exception and stays null, because the absence of a referenced
    /// row is what null means here and there is nothing to put in its place.
    /// </remarks>
    private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
    {
        if (sf.IsRef)
        {
            // The key the target is addressed by, not `int`. The record-member path next door
            // has always asked; this one wrote the width in, so a reference array whose target
            // is keyed by anything else declared an array the read could not fill.
            // spec/references/reference-key-types.md.
            string keyType = ToJavaTypeName(sf.FirstField!.RefKeyType, null, null);

            // The column's name is the key's; the row takes the derived one.
            // spec/references/reference-surface-naming.md sections 4 and 5.
            bool toRow = ResolvesToRow(sf.FirstField!);
            string rowName = toRow
                ? JavaName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                : name;
            string keyName = toRow ? name : name + "Index";

            return sf.IsArray
                ? new[] { $"{keyType}[] {keyName} = new {keyType}[0];", $"{elementType}[] {rowName} = new {elementType}[0];" }
                : new[] { $"{keyType} {keyName};", $"{elementType} {rowName};" };
        }

        return sf.IsArray
            ? new[] { $"{elementType}[] {name} = new {elementType}[0];" }
            : new[] { $"{elementType} {name}{Initializer(sf)};" };
    }

    /// <summary>
    /// An empty value of the member's own type, for the declaration to start at.
    /// </summary>
    private string Initializer(SerialField sf)
    {
        return sf.ElementType switch
        {
            // The reference types. Everything else is a primitive whose zero is already
            // an empty value, and saying so again would only be noise.
            ValueType.String => " = \"\"",
            ValueType.Uuid => " = TcbReader.Uuid.empty()",
            ValueType.Enum => $" = {sf.FirstField!.Enum.Name.ToPascalCase()}.of(0)",
            _ => "",
        };
    }

    /// <summary>
    /// The rendered checkColumn call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "TcbReader.KIND_ARRAY" : "TcbReader.KIND_SCALAR";


        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `TcbReader.ELEMENT_I32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/references/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "TcbReader.ELEMENT_STRING",
                ValueType.Int64 => "TcbReader.ELEMENT_I64, TcbReader.ELEMENT_I32, TcbReader.ELEMENT_VARINT",
                ValueType.Uuid => "TcbReader.ELEMENT_UUID",
                _ => "TcbReader.ELEMENT_I32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "TcbReader.ELEMENT_I32, TcbReader.ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "TcbReader.ELEMENT_I64, TcbReader.ELEMENT_I32, TcbReader.ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "TcbReader.ELEMENT_F64, TcbReader.ELEMENT_F32, TcbReader.ELEMENT_I32"; break;
                case ValueType.Float: accepted = "TcbReader.ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "TcbReader.ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "TcbReader.ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "TcbReader.ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "TcbReader.ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "TcbReader.ELEMENT_I64"; break;

                default:
                    throw new TabbitDefectException($"The java generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one. A method of its own
        // because the accepted elements are varargs and Java has no default parameters.
        string check = wire.HasOptionalElements ? "checkColumnWithElements" : "checkColumn";

        return $"TcbReader.{check}(column, \"{tableName}.{wire.Name}\", {kind}, "
            + $"{nullable}, {accepted});";
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
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"cursor = new TcbReader.ColumnCursor(reader, column, count, \"{tableName}.{wire.Name}\");"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to nextI32 or nextString: int32
    /// members, enums, references and strings. The other cursor scalars stay per-row -
    /// their encodings are dictionaries, where the per-row work is already one index
    /// lookup - as do arrays, which are always raw.
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

        string name = JavaName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + JavaName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references/references-in-records.md.
        if (wire.IsRef)
        {
            // The local the run decoded into is the key's - `runSameText` for a string,
            // and assigning `runSameValue` to a String does not compile.
            string local = wire.RefKeyType == ValueType.String ? "runSameText" : "runSameValue";

            return (wire.Member is null)
                ? $"loaded.get(i).{name}{(ResolvesToRow(wire.TagCarrier) ? "" : "Index")} = cursor.{local};"
                : $"loaded.get(i).{name}{memberAccess}{(ResolvesToRow(wire.TagCarrier) ? "" : "Index")} = cursor.{local};";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"loaded.get(i).{name}{memberAccess} = {wire.TagCarrier.Enum.Name.ToPascalCase()}.of(cursor.runSameValue);";

        if (wire.ElementType == ValueType.String)
            return $"loaded.get(i).{name}{memberAccess} = cursor.runSameText;";

            return $"loaded.get(i).{name}{memberAccess} = cursor.runSameValue;";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member loops the elements without allocating - the array was created
    /// with the record - while a trimmed one reads its length from the row, and there the
    /// first member does allocate because no declaration could have known how long this row's
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

            return wire.MemberOwnsTheArray ? "record_member_var" : "record_var";
        }

        // A trimmed array of references: the length is the row's, and the key still goes in the
        // array beside the values. Read as a plain `var_array` it assigned the key straight
        // into the array of rows, which does not compile - and nothing held the shape, because
        // `foreign[]` is refused and this is only reachable through a folded group with
        // trimming on. spec/types/variable-length-record-arrays.md.
        if (wire.IsArray)
            return wire.IsRef ? "var_array_ref" : "var_array";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private JavaAccessorView BuildAccessor() => new JavaAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new JavaTableSlotView
        {
            Name = JavaCamelName(table.Name),
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
                // key landed. spec/references/references-in-records.md.
                RecordFields = table.WireColumns
                                    .Where(wire => wire.Member is not null && wire.IsRef)
                                    .Select(BuildRecordReference)
                                    .ToList(),


            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                         )
            .Select(x => new JavaCrossReferenceView
            {
                Table = JavaName(x.Table.Name),
                RecordName = x.Table.Name.ToPascalCase() + "Record",
                Fields = x.Fields.Select(sf => new JavaReferenceFieldView
                {
                    Name = ResolvesToRow(sf.FirstField!)
                        ? JavaName(sf.Name)
                        : JavaName(sf.Name) + "Index",

                    RowName = ResolvesToRow(sf.FirstField!)
                        ? JavaName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                        : JavaName(sf.Name),

                    RefTable = JavaName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    RefRecordName = sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record",
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + JavaName(sf.FirstField!.ResolvedRefField!.Name),
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
    /// the three record shapes this is - the group's array, the member's, or neither.
    /// spec/references/references-in-records.md.
    /// </remarks>
    private JavaRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = JavaName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + JavaName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsArray;

        string rowLeaf = wire.Member is not null
            ? JavaName(RowAccessorName(refTable!.Name, wire.MemberPath[^1]))
            : JavaName(RowAccessorName(refTable!.Name, wire.Group.Name));

        string rowMember = wire.Member is not null
            ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                .Select(part => "." + JavaName(part))) + "." + rowLeaf
            : "";

        string path = !isArray || wire.MemberOwnsTheArray
            ? $"record.{name}{member}"
            : $"record.{name}[i]{member}";

        string rowPath = wire.Member is not null
            ? (!isArray || wire.MemberOwnsTheArray
                ? $"record.{name}{rowMember}"
                : $"record.{name}[i]{rowMember}")
            : $"record.{rowLeaf}";

        string subscript = (isArray && wire.MemberOwnsTheArray) ? "[i]" : "";

        return new JavaRecordReferenceView
        {
            Access = rowPath + subscript,
            Key = path + subscript,

            // Whichever array holds the elements. Its own length rather than the column
            // count, because a trimming group's rows differ in how many they carry.
            Count = isArray
                ? (wire.MemberOwnsTheArray ? $"{path}.length" : $"record.{name}.length")
                : "",

            RefTable = JavaName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
            RefRecordName = refTable.Name.ToPascalCase() + "Record",
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// What a scalar member reads: through the cursor, because a scalar column can
    /// arrive encoded - which also carries the lossless promotions. Everything the spec
    /// keeps raw falls through to the direct reads.
    /// </summary>
    /// <remarks>
    /// A reference is not read here. Its shape assigns the stored index rather than the
    /// member, so the template renders <see cref="ReadExpression"/> for it - which is the
    /// call that knows a reference travels as an index whatever the member's own type is.
    /// </remarks>
    private string ScalarReadExpression(WireColumn wire)
    {
        if (!UsesCursor(wire) || wire.IsRef)
            return DirectReadExpression(wire);

        return CursorReadExpression(wire);
    }

    /// <summary>
    /// What one element reads, at whatever depth the template places it.
    /// </summary>
    /// <remarks>
    /// An array's elements read through the cursor by the same calls a scalar's value does:
    /// what differs is only that the row's length comes from the cursor first.
    /// </remarks>
    private string ReadExpression(WireColumn wire)
        => UsesCursor(wire) ? CursorReadExpression(wire) : DirectReadExpression(wire);

    private string CursorReadExpression(WireColumn wire)
    {
        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded.
        // The key the target is addressed by, which is not always an int32. `nextI32` for
        // every reference is what kept a table keyed by anything else from being pointed at
        // from this language. spec/references/reference-key-types.md.
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
            // Same conversion as the raw read, just sourcing the int from the cursor.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(cursor.nextI32())";

            case ValueType.Int32: return "cursor.nextI32()";
            case ValueType.Int64: return "cursor.nextI64()";
            case ValueType.Double: return "cursor.nextF64()";
            case ValueType.Float: return "cursor.nextF32()";
            case ValueType.Bool: return "cursor.nextBool()";

            // Ticks, which is what the member holds either way - the i64 column they
            // come off can now be dictionary-encoded, so they come off the cursor.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "cursor.nextI64()";

            default: return "cursor.nextString()";
        }
    }

    private string DirectReadExpression(WireColumn wire)
    {
        switch (wire.ElementType)
        {

            // Enum values travel zig-zag encoded rather than fixed width.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}.of(reader.readEnum())";

                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/references/reference-key-types.md.
                case ValueType.ForeignRecord:
                    return LanguageProfile.Java.ReadCall(wire.RefKeyType);

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            default: return LanguageProfile.Java.ReadCall(wire.ElementType);
        }
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        return ToJavaTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull, null);
    }

    private string ToJavaTypeName(ValueType type, Models.Enum? enumm, string? refTableName)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return enumm!.Name.ToPascalCase();

            case ValueType.ForeignRecord:
                return refTableName.ToPascalCase() + "Record";

            default:
                return LanguageProfile.Java.ScalarTypeName(type);
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
        string element = ToJavaTypeName(ValueTypes.ElementOf(constant.Type), constant.Enum, null);

        return ValueTypes.IsArray(constant.Type) ? LanguageProfile.Java.ArrayOf(element) : element;
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

        return "new " + ToJavaTypeName(element, constant.Enum, null) + "[] { " + joined + " }";
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

            // `L`, or the literal is an int and will not fit.
            case ValueType.Int64:
                return ((long)value!).ToString(CultureInfo.InvariantCulture) + "L";

            // `f`, or the literal is a double and will not narrow implicitly.
            case ValueType.Float:
                return ((float)value!).ToString("R", CultureInfo.InvariantCulture) + "f";

            case ValueType.Double:
                return ((double)value!).ToString("R", CultureInfo.InvariantCulture);

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)value!).Ticks.ToString(CultureInfo.InvariantCulture) + "L";

            case ValueType.TimeSpan:
                return ((TimeSpan)value!).Ticks.ToString(CultureInfo.InvariantCulture) + "L";

            case ValueType.Uuid:
                return "new TcbReader.Uuid(new byte[] { " + string.Join(", ",
                    ((Guid)value!).ToByteArray()
                        .Select(b => "(byte) 0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + " })";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(value!, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}.{JavaConstantName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", type),
                            ("Generator", "java")));
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
    private string IndexKeyType(SerialField only)
    {
        var (type, enumm) = KeyComponentView.TypeOf(only);
        return ToJavaTypeName(type, enumm, null);
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// A field name.
    ///
    /// camelCase, and never escaped: every Java keyword is lowercase and a single word,
    /// while a field name here comes from a sheet column and would have to be exactly
    /// one of them - which the profile's list covers.
    /// </summary>
    private string JavaName(string name) => LanguageProfile.Java.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for a name that is not a member - the accessor's slot per table.
    /// </summary>
    /// <remarks>
    /// camelCase because that is how Java writes an identifier, not because a member is
    /// spelled that way. Sharing one function let the two look like one rule.
    /// </remarks>
    private static string JavaCamelName(string name) => LanguageProfile.Java.MemberName(name.ToCamelCase());

    /// <summary>
    /// A constant or enum label name, SCREAMING_SNAKE_CASE as Java writes them.
    /// </summary>
    private static string JavaConstantName(string name) => name.ToUpperSnakeCase();

}
