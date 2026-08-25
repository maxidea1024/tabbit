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
/// Settings for the Unreal target.
/// </summary>
public sealed class UnrealRecipe : IOutputRecipe
{
    /// <summary>
    /// Directory the module is written into. The module's own directory is created
    /// underneath it, so this is usually a project's `Source` or a plugin's.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Module name. Names the directory, the Build.cs and the export macro, and is what
    /// another module lists as a dependency.
    /// </summary>
    public string ModuleName { get; set; } = "TabbitData";

    /// <summary>
    /// Name of the accessor class, which also names the header and the .cpp.
    /// </summary>
    public string AccessorName { get; set; } = "FTables";

    /// <summary>
    /// Whether to write the module's Build.cs.
    ///
    /// On by default, so the output is a module a project can add as it stands. Turn it
    /// off to generate into a module that already exists.
    /// </summary>
    public bool WriteBuildFile { get; set; } = true;

    /// <summary>
    /// Whether to write the data updater beside the reader.
    ///
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a build can take new data without shipping a new one. Off by
    /// default: it puts the HTTP module into the generated Build.cs, and a project
    /// that ships its data inside the .pak has no use for either.
    /// </summary>
    public bool WriteUpdater { get; set; } = false;

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

    /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
    public string TargetSide { get; set; } = "cs";

}

/// <summary>
/// Emits an Unreal module: USTRUCT rows, UENUM enums, a static accessor class, and an
/// Unreal binary reader.
///
/// Its own reader rather than the plain C++ one. That one was shared here at first, on
/// the grounds that the wire format lives in it and the conformance corpus already
/// checks it - but sharing it meant an Unreal module full of std::string, std::vector
/// and a Tabbit Uuid struct, every one of which the engine already provides. The cost
/// was two allocations for each string cell and a text parse for each uuid, converting
/// into what FString and FGuid already were. Worse, that reader reports failure by
/// throwing, and an Unreal module is built with exceptions disabled: a malformed table
/// file terminated the process from inside a function whose signature promised a bool.
///
/// So `lib/unreal` is a sibling of `lib/cpp`, not a wrapper around it. The format is
/// unchanged, so the corpus still applies.
///
/// Written to work on both UE4 and UE5, which costs one thing: a double member carries
/// no UPROPERTY, because UE4's header tool rejects the type outright. The field is read
/// and usable from C++ either way; it is only Blueprint that cannot see it.
///
/// The shapes live in templates/unreal.sbn and templates/unreal-cpp.sbn.
/// </summary>
[TabbitTarget("unreal", TargetKind.CodeGeneration, Order = 90)]
public class UnrealCodeGenerator : CodeGenerator<UnrealRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;


    /// <summary>
    /// A record group generates a USTRUCT and a TArray of it; a member column fills one of
    /// its properties.
    /// </summary>
    /// <remarks>
    /// A USTRUCT rather than a plain struct, because a TArray of one is a property UHT
    /// accepts - which is what lets the row's member keep its UPROPERTY and stay visible to
    /// Blueprint. A member inside it that UHT refuses (a double) loses its own UPROPERTY and
    /// nothing else.
    /// </remarks>
    /// <summary>
    /// MSVC reads a source file with no byte order mark in the system codepage, which
    /// turns a comment taken from a Korean sheet into a line continuation.
    /// </summary>
    protected override bool WritesByteOrderMark => true;

    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a USTRUCT declared before the element type, and the read reaches it with
    /// a longer member path.
    /// </summary>
    /// <remarks>
    /// **This one keeps its reflection**, which an array of arrays did not: a struct member of a
    /// USTRUCT type is a property UHT accepts, where a nested container is not. So the depth
    /// generalization costs this target less than the shape before it did.
    /// spec/nested-multi-level.md.
    /// </remarks>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `bHas{Name}` property beside the value.
    /// </summary>
    /// <remarks>
    /// Not `TOptional`: it is not a property type UHT knows before 5.4, and the member has to
    /// stay a UPROPERTY. The engine answers the same problem the same way - `bOverride_X`
    /// beside `X` in FPostProcessSettings. spec/optional-fields.md has the reasoning.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`FindByStageAndSlot(StageKey, SlotKey)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private UnrealRecipe _recipe = null!;

    protected override void Run(TargetContext context, UnrealRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;


        var view = BuildView();

        Write(System.IO.Path.Combine("Public", _recipe.AccessorName + ".h"), "unreal.sbn", view);
        Write(System.IO.Path.Combine("Private", _recipe.AccessorName + ".cpp"), "unreal-cpp.sbn", view);

        WriteBinaryReaderRuntime();

        if (_recipe.WriteBuildFile)
            WriteBuildFile();
    }

    private string ModuleDir => System.IO.Path.Combine(_recipe.Path, _recipe.ModuleName);

    /// <summary>
    /// The abstract types the sheets used, as the template reads them.
    /// </summary>
    /// <remarks>
    /// **The discriminator + per-variant accessors shape section 7 named.** A `USTRUCT` cannot
    /// take part in inheritance polymorphism the engine's reflection can see, so this target
    /// falls on the same side C does: a `UENUM` says which variant a row is, and one accessor
    /// per variant fills a caller-owned struct. spec/polymorphism.md section 7.
    /// </remarks>
    private IReadOnlyList<UnrealPolymorphicTypeView> BuildStructs()
        => _model.PolymorphicTypes
            .Select(declared => new UnrealPolymorphicTypeView
            {
                Name = "F" + declared.Name,
                BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
                Variants = declared.Variants
                    .Select(variant => new UnrealVariantView
                    {
                        TypeName = "F" + variant.Name,
                        KindName = variant.Name,
                        Suffix = variant.Name,
                        Discriminator = variant.Discriminator,
                        Members = variant.Members.Select(StructMember).ToList(),
                    })
                    .ToList(),
            })
            .ToList();

    /// <summary>One member of an abstract type or of one of its variants.</summary>
    /// <remarks>
    /// **A reference member is two properties**, as a reference is anywhere: the declared name
    /// is the key's and the row it resolves to takes the derived one.
    /// spec/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    private UnrealStructMemberView StructMember(Models.Field field)
    {
        string raw = field.NamePath is { Count: > 1 } ? field.NamePath[^1].Name : field.Name;
        bool toRow = field.IsRef && field.ResolvedRefTable is not null && ResolvesToRow(field);

        return new UnrealStructMemberView
        {
            Name = raw.ToPascalCase(),
            TypeName = ToUnrealTypeName(field.ElementType, field.EnumOrNull),
            Comment = CommentLines(field.Comment),
            RowName = toRow
                ? RowAccessorName(field.ResolvedRefTable!.Name, raw).ToPascalCase()
                : "",
            KeyTypeName = field.IsRef
                ? ToUnrealTypeName(field.RefKeyType, null)
                : "",
        };
    }

    private void Write(string relative, string templateName, UnrealFileView view)
    {
        string filename = System.IO.Path.GetFullPath(System.IO.Path.Combine(ModuleDir, relative));

        Log.Information($"Generating codes for Unreal into `{filename}`");

        StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render(templateName, view), WritesByteOrderMark);
    }

    /// <summary>
    /// A BlueprintType enum's underlying type must be uint8, so a label outside 0 to 255
    /// cannot be one - and the enum widens to int32 and gives up Blueprint instead.
    /// </summary>
    /// <remarks>
    /// This used to throw and refuse the whole conversion, which made the Unreal target the
    /// only one that could not read a model the other eleven read. The values belong to the
    /// sheet: an enum of network error codes or bit flags is ordinary, and a code generator
    /// does not get to reject it.
    ///
    /// So it degrades, and says which label did it. The enum stays a `UENUM`, so it is still
    /// reflected and still serialises; it loses `BlueprintType`, and the fields typed with it
    /// lose their `UPROPERTY`, because UHT will not expose a property Blueprint cannot see.
    /// Everything remains readable from C++, which is where the data is used.
    ///
    /// Warned rather than silent, because a project that wanted the enum in Blueprint would
    /// otherwise find out from a missing pin.
    /// </remarks>
    private Models.Enum.Label? OutOfBlueprintRange(Models.Enum enumm)
    {
        foreach (var label in enumm.Labels)
        {
            if (label.Value < 0 || label.Value > 255)
                return label;
        }

        return null;
    }

    private void WriteBinaryReaderRuntime()
    {
        // Public, because the generated header includes it and anything including that
        // header needs to find it.
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Unreal.TabbitTcbReader.h",
            System.IO.Path.Combine(ModuleDir, "Public", "TabbitTcbReader.h"));

        // Asked for rather than assumed: it reaches the network, and it is what puts the
        // HTTP module into this module's dependencies.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Unreal.TabbitUpdater.h",
                System.IO.Path.Combine(ModuleDir, "Public", "TabbitUpdater.h"));

            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Unreal.TabbitUpdater.cpp",
                System.IO.Path.Combine(ModuleDir, "Private", "TabbitUpdater.cpp"));
        }
    }

    /// <summary>
    /// Writes the module's Build.cs, so the output is a module a project can add as it
    /// stands rather than a pile of files somebody has to wire up.
    /// </summary>
    private void WriteBuildFile()
    {
        var text = new StringBuilder();

        text.Append($"// {GeneratedFileMarker.TextWithWarning}\n");
        text.Append('\n');
        text.Append("using UnrealBuildTool;\n");
        text.Append('\n');
        text.Append("public class ").Append(_recipe.ModuleName).Append(" : ModuleRules\n");
        text.Append("{\n");
        text.Append("    public ").Append(_recipe.ModuleName).Append("(ReadOnlyTargetRules Target) : base(Target)\n");
        text.Append("    {\n");
        text.Append("        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;\n");
        text.Append('\n');
        text.Append("        // Core for FString, TArray, FGuid, FDateTime and the file helpers;\n");
        text.Append("        // CoreUObject for the reflection the USTRUCTs need; Engine for\n");
        text.Append("        // UBlueprintFunctionLibrary, which is what makes the rows reachable\n");
        text.Append("        // from a Blueprint graph at all.\n");
        text.Append("        //\n");
        text.Append("        // No bEnableExceptions: the reader reports a malformed file by returning\n");
        text.Append("        // false, so this module builds with the engine's defaults.\n");

        if (_recipe.WriteUpdater)
        {
            text.Append("        //\n");
            text.Append("        // HTTP is here because the updater is: it fetches the manifest and the\n");
            text.Append("        // changed data files. Turn WriteUpdater off and this goes with it.\n");
        }

        text.Append("        PublicDependencyModuleNames.AddRange(\n");

        // HTTP only when the updater is written. A module that does not patch its data
        // should not carry a dependency on the transport that would.
        text.Append(_recipe.WriteUpdater
            ? "            new string[] { \"Core\", \"CoreUObject\", \"Engine\", \"HTTP\" });\n"
            : "            new string[] { \"Core\", \"CoreUObject\", \"Engine\" });\n");
        text.Append("    }\n");
        text.Append("}\n");

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(ModuleDir, _recipe.ModuleName + ".Build.cs")),
            text.ToString());
    }

    // --------------------------------------------------------------- view

    private UnrealFileView BuildView() => new UnrealFileView
    {
        AccessorName = _recipe.AccessorName,
        ApiMacro = _recipe.ModuleName.ToUpperInvariant() + "_API",
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Structs = BuildStructs(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Accessor = new UnrealAccessorView
        {
            FileExtension = _recipe.BinaryTableFileExtension,
            LibraryName = LibraryName(),
            Tables = _model.Tables.Select(table => new UnrealTableSlotView
            {
                Name = table.Name.ToPascalCase(),
                TableName = TableName(table),
                RecordName = RecordName(table),
                RawName = table.Name,
                // The primary key asked for by name rather than taken off the front of
                // the list, which puts single keys first. See KeyPlans.PrimaryOf.
                PrimaryLookup = "FindBy" + PrimaryOf(table).Suffix,
                PrimaryKeyType = PrimaryOf(table).KeyType,
                PrimaryKeyParam = PrimaryOf(table).KeyParam,
                PrimaryFieldName = PrimaryOf(table).FieldName,
                PrimaryParams = PrimaryOf(table).Params,
                PrimaryArgument = PrimaryOf(table).Argument,

                // Unescaped: this one names the file the exporter wrote.
                DataFileName = table.DataFileName,
            }).ToList(),
        },
    };

    private UnrealEnumView BuildEnum(Models.Enum enumm)
    {
        var offender = OutOfBlueprintRange(enumm);

        if (offender is not null)
        {
            Log.Warning(Messages.Message.Of(Exporters.ExportMessages.LogUnrealEnumNotBlueprint,
                ("Enum", enumm.Name), ("Label", offender.Name),
                ("Value", offender.Value)).In(Messages.MessageCatalog.Current));
        }

        return new UnrealEnumView
    {
        Name = EnumName(enumm),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        BlueprintVisible = offender is null,
        UnderlyingType = offender is null ? "uint8" : "int32",
        NotVisibleBecause = offender is null
            ? null
            : $"label `{offender.Name}` is {offender.Value}, and a BlueprintType enum is uint8.",
        Labels = enumm.Labels.Select(label => new UnrealEnumLabelView
        {
            Name = label.Name.ToPascalCase(),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            DisplayName = label.Name,
            Comment = CommentLines(label.Comment),
        }).ToList(),
    };
    }

    private UnrealTableView BuildTable(Table table)
    {
        // Worked out before the fields, because a local the generated code declares
        // must not land on a member name. `Index` is the usual name of a primary key
        // here, so a loop counter called that would shadow it in every table that has
        // one - legal, and unambiguous, but not what a generator should emit.
        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sf in table.SerialFields)
        {
            // A record group has no single field - FirstField answers null for one by design
            // - so its first member's stands in. Only the name matters here.
            var field = sf.IsRecord ? sf.Members[0].FirstField : sf.FirstField;
            string member = MemberName(field, sf.Name);

            // No linking in this language, so a reference column carries the key and
            // nothing else - and the key wears the column's own name.
            // spec/reference-surface-naming.md sections 4 and 6.
            members.Add(member);
        }

        return new UnrealTableView
        {
            RawName = table.Name,
            RecordName = RecordName(table),
            TableName = TableName(table),
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            Indexes = Indexes(table),
            Fields = table.SerialFields.Select(sf => BuildField(table, sf, members)).ToList(),

            // One cursor variable for the whole read: switch cases share a scope, and
            // C++ does not allow a jump past a live constructor, so each encodable
            // column opens the shared cursor rather than declaring its own.
            NeedsCursor = table.WireColumns.Any(UsesCursor),
            Columns = table.WireColumns.Select(wire => BuildColumn(table, wire, members)).ToList(),
            NeedsPresence = table.WireColumns.Any(wire => wire.IsNullable),
            NeedsElementPresence = table.WireColumns.Any(wire => wire.HasOptionalElements),
        };
    }

    /// <summary>
    /// A name for a local the generated code declares, not taken by any member.
    ///
    /// Almost always the preferred one. The suffix only appears for a sheet that has a
    /// column of that name, and then it is still a name and not a collision.
    /// </summary>
    private static string LocalName(string preferred, ICollection<string> members)
    {
        if (!members.Contains(preferred))
            return preferred;

        for (int suffix = 2; ; suffix++)
        {
            string candidate = preferred + suffix.ToString(CultureInfo.InvariantCulture);

            if (!members.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where it lands.
    /// </summary>
    /// <remarks>
    /// A record group's member columns each fill one field of the generated element type,
    /// which is what `MemberAccess` carries - `Record.Slot[j].Id` rather than
    /// `Record.Slot[j]`. Declaring the member is <see cref="BuildField"/>'s business; these
    /// are separate units and a record group is where they stop being the same one.
    /// </remarks>
    private UnrealColumnView BuildColumn(Table table, WireColumn wire, ICollection<string> members)
    {
        string name = MemberName(wire.TagCarrier, wire.Group.Name);
        string member = wire.Member is null ? "" : string.Concat(wire.MemberPath.Select(part => "." + MemberName(wire.TagCarrier, part)));
        string countLocal = LocalName("ElementCount", members);

        // A reference member is declared as the key it holds, so the read names the key - and
        // the suffix goes on the member rather than after the subscript, because a member that
        // is an array holds one key per element. spec/references-in-records.md.
        string memberRefSuffix = "";

        return new UnrealColumnView
        {
            CountLocal = countLocal,
            LengthRead = LengthRead(wire, countLocal),
            WireTag = wire.TagCarrier.WireTag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            CursorRead = CursorRead(wire, name + member),
            RunCall = RunCall(wire),
            RunValueDeclaration = RunValueDeclaration(wire),
            RunValueName = RunValueName(wire),
            RunSpend = RunSpend(wire, name + member),
            Name = name,
            MemberAccess = member,
            MemberRefSuffix = memberRefSuffix,
            MemberAt = wire.MemberAt,
            OuterCount = wire.Group.IsRecord ? wire.Group.Members.Count : 0,
            ElementCount = wire.Cells.Count,
            ReadCall = ReadCall(wire),
            IsFirstMember = wire.IsFirstMember,
            RecordTypeName = wire.Group.IsRecord ? RecordEntryName(table, wire.Group) : "",
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = "bHas" + name,
            ElementPresenceMember = "bHas" + name + "At",
            EmptyValue = EmptyValueOf(wire),
        };
    }

    /// <summary>What an absent row's value is set to, so both read paths land on the same thing.</summary>
    private string EmptyValueOf(WireColumn wire)
    {
        if (wire.IsArray)
            return "{}";

        return wire.ElementType switch
        {
            ValueType.String => "FString()",
            ValueType.Bool => "false",
            ValueType.Uuid => "FGuid()",
            ValueType.DateTime => "FDateTime()",
            ValueType.TimeSpan => "FTimespan()",
            ValueType.Enum => $"static_cast<{ToUnrealTypeName(wire.ElementType, wire.TagCarrier.EnumOrNull)}>(0)",
            _ => "0",
        };
    }

    /// <summary>
    /// The element type declared for a record group: a USTRUCT of its members.
    /// </summary>
    /// <remarks>
    /// Prefixed with the table because Unreal's type names are global - two tables each
    /// holding a `Slot` group would otherwise declare `FSlotEntry` twice, and UHT would
    /// reject the second.
    /// </remarks>
    private static string RecordEntryName(Table table, SerialField sf)
        => "F" + table.Name.ToPascalCase() + sf.Name.ToPascalCase() + "Entry";

    /// <summary>
    /// Members of one level of a record, declaring a USTRUCT for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the structs it produces - innermost first, because a USTRUCT member needs its
    /// complete type and UHT reads the header in order.
    ///
    /// A struct member of a USTRUCT type is a property UHT accepts, so depth costs this target
    /// no reflection - the opposite of what an array of arrays cost it.
    /// spec/nested-multi-level.md.
    /// </remarks>
    private List<UnrealRecordMemberView> BuildRecordMembers(
        Table table, List<RecordMember> members, string prefix, SerialField group,
        List<UnrealRecordTypeView> declared)
    {
        var result = new List<UnrealRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                result.Add(BuildRecordMember(table, member));
                continue;
            }

            // A level below. Unreal's type names are global, so the name carries the path as
            // well as the table's - two records each holding a `Position` would otherwise
            // declare one struct twice and UHT would reject the second.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(table, member.Members, typeName, group, declared);

            declared.Add(new UnrealRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{RecordName(table)}::{MemberName(group.AnyField, group.Name)}",
            });

            result.Add(new UnrealRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Name = MemberName(member.FirstField, member.Name),
                Declaration = $"{typeName} {MemberName(member.FirstField, member.Name)};",

                // A USTRUCT member of a USTRUCT type is a property UHT accepts, so this level
                // keeps its reflection whatever the levels below it turn out to hold.
                BlueprintVisible = true,
                NotVisibleBecause = null,
            });
        }

        return result;
    }

    private UnrealFieldView BuildField(Table table, SerialField sf, ICollection<string> members)
    {
        // Which abstract type this group is, if it is one. One per declaration however many
        // tables named it. spec/polymorphism.md section 7.1.
        var declaredType = sf.Members
                .FirstOrDefault(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                ?.FirstField?.AbstractTypeName is { } abstractName
            ? _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == abstractName.ToPascalCase())
            : null;

        string name = MemberName(sf.IsRecord ? sf.Members[0].FirstField : sf.FirstField, sf.Name);

        // No element struct for an array of arrays: its outer level has no name, so there is
        // nothing for a USTRUCT to be. Innermost first otherwise.
        var recordTypes = new List<UnrealRecordTypeView>();

        var recordMembers = (sf.IsRecord && !sf.MembersAreAnonymous)
            ? BuildRecordMembers(table, sf.Members, RecordEntryName(table, sf), sf, recordTypes)
            : new List<UnrealRecordMemberView>();

        if (sf.IsRecord && !sf.MembersAreAnonymous)
        {
            recordTypes.Add(new UnrealRecordTypeView
            {
                TypeName = RecordEntryName(table, sf),
                Members = recordMembers,
                IsOutermost = true,
                Owner = $"{RecordName(table)}::{name}",
            });
        }

        return new UnrealFieldView
        {
            VariantsAreArray = declaredType is not null && sf.IsArray,
            EntryAccess = "Entry",
            AbstractTypeName = declaredType is null ? "" : "F" + declaredType.Name,
            KindEnumName = declaredType is null ? "" : "F" + declaredType.Name + "Kind",
            PascalName = sf.Name.ToPascalCase(),
            BaseMembers = (declaredType?.BaseMembers ?? []).Select(StructMember).ToList(),
            Variants = declaredType is null
                ? []
                : BuildStructs().First(s => s.Name == "F" + declaredType.Name).Variants,

            Comment = CommentLines(
                sf.IsRecord ? sf.Members[0].FirstField!.Comment : sf.FirstField!.Comment),
            Name = name,
            ElementCount = sf.IsRecord ? sf.RecordElementCount : sf.Fields.Count,
            Declaration = Declaration(table, sf, name),
            IsRecord = sf.IsRecord,
            MembersAreAnonymous = sf.IsRecord && sf.MembersAreAnonymous,
            RecordTypeName = sf.IsRecord ? RecordEntryName(table, sf) : "",
            Members = recordMembers,
            RecordTypes = recordTypes,

            // A record group has no presence of its own: absence inside one is the array's
            // length, not a bit per member.
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = "bHas" + name,
            ElementPresenceMember = "bHas" + name + "At",

            // Two reasons a member is written without a UPROPERTY, and it is written either
            // way: the value is read and usable from C++, and only Blueprint cannot see it.
            //
            // UE4's header tool rejects a double property and UE5 accepts one, so a double is
            // left unreflected to build on both.
            //
            // And an enum whose labels do not fit uint8 is not a BlueprintType, so UHT will
            // not expose a property of that type either - the enum's own degradation carries
            // through to every field declared with it.
            // A record group's member is a USTRUCT or an array of one, and both are property
            // types UHT accepts - whatever the members inside are. A member that is a double
            // loses its own UPROPERTY below, which does not stop the struct being reflected.
            // Except an array of arrays: UHT has no nested container property, so a
            // `TArray<TArray<T>>` cannot be one. It is declared and read all the same - the
            // same treatment a double member gets, and for the same kind of reason.
            BlueprintVisible = sf.IsRecord
                ? !sf.MembersAreAnonymous
                : sf.ElementType != ValueType.Double && !NamesAWideEnum(sf),
            NotVisibleBecause = sf.IsRecord
                ? (sf.MembersAreAnonymous
                    ? "Unreal's header tool has no nested container property, so an array of "
                      + "arrays cannot be one. It is still read and still holds its values."
                    : null)
                : sf.ElementType == ValueType.Double
                    ? "UE4's header tool does not accept a double property."
                    : WideEnumReason(sf),
        };
    }

    /// <summary>One member of a record group's generated USTRUCT.</summary>
    private UnrealRecordMemberView BuildRecordMember(Table table, RecordMember member)
    {
        var field = member.FirstField;

        // A reference member is the key and nothing else, exactly as a reference outside a
        // record is here: this output keeps references as indices rather than resolving them.
        // So the only thing a member being a reference changes is its name - which has to say
        // what it holds, because the row is not there beside it - and the type of that key.
        // spec/references-in-records.md.
        string name = MemberName(field, member.Name);
        string type = member.IsRef
            ? ToUnrealTypeName(field!.RefKeyType, null)
            : ToUnrealTypeName(field!.ElementType, field!.EnumOrNull);

        return new UnrealRecordMemberView
        {
            Comment = CommentLines(field.Comment),
            Name = name,
            // The array is the member's when the group is one record - same columns, same
            // wire, and only which of the two owns it differs.
            Declaration = member.IsArray
                ? $"TArray<{type}> {name};"
                : $"{type} {name}{(member.IsRef ? RefKeyInitializer(field!.RefKeyType) : DefaultInitializer(field))};",

            // The same two reasons a table's own member loses its UPROPERTY, applied inside
            // the element type: UHT is what rejects them, and it does not care whose struct
            // the property is in.
            BlueprintVisible = field!.ElementType != ValueType.Double
                               && !(field!.ElementType == ValueType.Enum
                                    && OutOfBlueprintRange(field.Enum) is not null),
            NotVisibleBecause = field!.ElementType == ValueType.Double
                ? "UE4's header tool does not accept a double property."
                : field!.ElementType == ValueType.Enum && OutOfBlueprintRange(field.Enum) is not null
                    ? "the enum it names had to widen past uint8, so it is not a BlueprintType."
                    : null,
        };
    }

    /// <summary>
    /// Whether this field is declared with an enum that had to widen past uint8.
    /// </summary>
    private bool NamesAWideEnum(SerialField sf)
        => sf.ElementType == ValueType.Enum && OutOfBlueprintRange(sf.FirstField!.Enum) is not null;

    private string? WideEnumReason(SerialField sf)
    {
        if (sf.ElementType != ValueType.Enum)
            return null;

        var offender = OutOfBlueprintRange(sf.FirstField!.Enum);

        return offender is null
            ? null
            : $"`{EnumName(sf.FirstField!.Enum)}` is not a BlueprintType - label `{offender.Name}` " +
              $"is {offender.Value}, and a BlueprintType enum is uint8.";
    }

    /// <summary>
    /// Which call reads one element of this column, named with what it reads from.
    ///
    /// Every type but an enum resolves by overload, because the Unreal reader has an
    /// overload per engine type rather than one that fills a standard C++ value the
    /// caller then converts. An enum cannot: its underlying type is what travels, so
    /// it goes through the template.
    ///
    /// A reference contributes an int32 index, which is an ordinary overload.
    /// </summary>
    /// <remarks>
    /// The cursor answers the same two names one level down, so a column that arrives
    /// encoded reads its elements through it by the same generated line - what changes is
    /// only which of the two the line is addressed to.
    /// </remarks>
    private static string ReadCall(WireColumn wire)
    {
        string reader = UsesCursor(wire) ? "Cursor.Next" : "Reader.Read";

        if (wire.IsRef)
            return reader + "As";

        switch (wire.ElementType)
        {
            case ValueType.Enum:
                return reader + "EnumAs";

            case ValueType.String:
            case ValueType.Bool:
            case ValueType.Int32:
            case ValueType.Int64:
            case ValueType.Float:
            case ValueType.Double:
            case ValueType.DateTime:
            case ValueType.TimeSpan:
            case ValueType.Uuid:
                return reader + "As";

            default:
                    throw new TabbitDefectException($"The unreal generator cannot read type `{wire.Type}`.");
        }
    }

    /// <summary>
    /// How an array column's row learns how many elements it holds.
    /// </summary>
    /// <remarks>
    /// From the cursor where the column reads through one, because an encoded array's
    /// lengths are their own stream at the front of the block rather than a number in front
    /// of each row. The cursor answers the same call either way, so this is one line and not
    /// a branch in the emitted loop.
    /// </remarks>
    private static string LengthRead(WireColumn wire, string countLocal)
        => UsesCursor(wire)
            ? $"Cursor.NextLength({countLocal});"
            : $"Reader.ReadCounter32({countLocal});";

    /// <summary>
    /// The rendered CheckColumn call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time rather than
    /// in the reader.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "Tabbit::KindArray" : "Tabbit::KindScalar";


        string[] accepted;

        if (wire.IsRef)
        {
            // The key the target is addressed by. `ElementI32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => new[] { "ElementString" },
                ValueType.Int64 => new[] { "ElementI64", "ElementI32", "ElementVarint" },
                ValueType.Uuid => new[] { "ElementUuid" },
                _ => new[] { "ElementI32" },
            };
        }
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = new[] { "ElementI32", "ElementVarint" }; break;
                case ValueType.Int64:
                    accepted = new[] { "ElementI64", "ElementI32", "ElementVarint" }; break;
                case ValueType.Double:
                    accepted = new[] { "ElementF64", "ElementF32", "ElementI32" }; break;
                case ValueType.Float: accepted = new[] { "ElementF32" }; break;
                case ValueType.Bool: accepted = new[] { "ElementBool" }; break;
                case ValueType.String: accepted = new[] { "ElementString" }; break;
                case ValueType.Uuid: accepted = new[] { "ElementUuid" }; break;
                case ValueType.Enum: accepted = new[] { "ElementVarint" }; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = new[] { "ElementI64" }; break;

                default:
                    throw new TabbitDefectException($"The unreal generator cannot check type `{wire.Type}`.");
            }
        }

        string mask = string.Join(
            " | ", accepted.Select(name => $"Tabbit::ElementMask(Tabbit::{name})"));

        // Nullability rides with kind and count, because it is the same kind of fact: a file
        // that says optional puts a presence bitmap in front of the block, and code not
        // expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        return $"Tabbit::CheckColumn(Reader, Column, TEXT(\"{tableName}.{wire.Name}\"), " +
               $"{kind}, {nullable}, {mask});";
    }

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

            default:
                return false;
        }
    }

    /// <summary>
    /// The cursor Open call ahead of an encodable column's row loop, or nothing for a
    /// column that reads the reader directly.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"Cursor.Open(Reader, Column, Header.RowCount, TEXT(\"{tableName}.{wire.Name}\"));"
            : "";

    /// <summary>
    /// The cursor's run call for a scalar whose values the run encodings cover, or empty
    /// for everything else - which then reads row by row as before.
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
    /// The declaration of the local the run's value is decoded into, initialized.
    /// </summary>
    private static string RunValueDeclaration(WireColumn wire)
        => RunCall(wire) switch
        {
            "NextSameString" => "FString RunText;",
            "NextSameI32" => "int32 RunValue = 0;",
            _ => "",
        };

    /// <summary>The local <see cref="RunValueDeclaration"/> declares, by name.</summary>
    private static string RunValueName(WireColumn wire)
        => RunCall(wire) switch
        {
            "NextSameString" => "RunText",
            "NextSameI32" => "RunValue",
            _ => "",
        };

    /// <summary>
    /// The line assigning one row from the value the run decoded, inside the loop the
    /// template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire, string name)
    {
        if (RunCall(wire).Length == 0)
            return "";

        // Only the stored index is on the wire; the caller looks the row up once every
        // table is loaded, exactly as the per-row shape leaves it.
        //
        // Which local the run decoded into is the key's: a string run fills `RunText`, and
        // assigning `RunValue` to an FString does not compile.
        // spec/reference-key-types.md.
        // No `Index` suffix and no second name: this language does not link, so every
        // reference column is one key wearing the column's own name - dotted or not.
        // spec/reference-surface-naming.md, "링킹이 없는 언어".
        if (wire.IsRef)
            return $"Loaded[Row].{name} = {RunValueName(wire)};";

        if (wire.ElementType == ValueType.Enum)
            return $"Loaded[Row].{name} = static_cast<{EnumName(wire.TagCarrier.Enum)}>(RunValue);";

        if (wire.ElementType == ValueType.String)
            return $"Loaded[Row].{name} = RunText;";

        return $"Loaded[Row].{name} = RunValue;";
    }

    /// <summary>
    /// The one-line read through the cursor inside the row loop, or nothing for a
    /// column that reads the reader directly.
    ///
    /// An enum goes through NextEnum, which sources the int32 from whatever the block's
    /// encoding is and casts - the same conversion ReadEnumAs applied to the raw wire.
    /// </summary>
    private static string CursorRead(WireColumn wire, string name)
    {
        if (!UsesCursor(wire))
            return "";

        // An array's elements are read where the array is sized, by the overload its member
        // type picks - so this line, which names the member itself, is not one of them.
        if (wire.IsArray)
            return "";

        // The key the target is addressed by, which is not always an int32.
        // spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => $"Cursor.NextI64(Record.{name});",
                ValueType.String => $"Cursor.NextString(Record.{name});",
                _ => $"Cursor.NextI32(Record.{name});",
            };
        }

        switch (wire.ElementType)
        {
            case ValueType.Int32:
                return $"Cursor.NextI32(Record.{name});";
            case ValueType.Int64:
                return $"Cursor.NextI64(Record.{name});";
            case ValueType.Double:
                return $"Cursor.NextF64(Record.{name});";
            case ValueType.Float:
                return $"Cursor.NextF32(Record.{name});";
            case ValueType.Bool:
                return $"Cursor.NextBool(Record.{name});";
            case ValueType.Enum:
                return $"Cursor.NextEnum(Record.{name});";

            // Ticks, so the member is built from what the i64 column carried - the same
            // construction, and the same range check, the reader's own overload makes.
            case ValueType.DateTime:
                return $"Cursor.NextDateTime(Record.{name});";
            case ValueType.TimeSpan:
                return $"Cursor.NextTimespan(Record.{name});";

            default:
                return $"Cursor.NextString(Record.{name});";
        }
    }

    /// <summary>
    /// The member declaration.
    ///
    /// A reference contributes only its index; resolving it into a pointer would put a
    /// raw pointer inside a USTRUCT, which the garbage collector does not track. The
    /// caller looks it up, as in the Rust output.
    /// </summary>
    private string Declaration(Table table, SerialField sf, string name)
    {
        if (sf.IsRef)
        {
            // **The target's key type, not int32.** A reference carries whatever the target
            // is keyed by - a name, a uuid, an id past 32 bits - and the member inside a
            // record group has always been typed that way. This one was not, and nothing
            // caught it: a top-level reference to a string-keyed table had no golden until
            // the link table in `composite-key`.
            string key = ToUnrealTypeName(
                sf.FirstField!.RefKeyType,
                sf.FirstField!.ResolvedRefTable?.PrimaryIndexField?.EnumOrNull);

            return sf.IsArray
                ? $"TArray<{key}> {name};"
                : $"{key} {name}{RefKeyInitializer(sf.FirstField!.RefKeyType)};";
        }

        // A record group declares the element type above the row struct, so the member is of
        // that type - or an array of it.
        if (sf.IsRecord)
        {
            // An array of arrays declares no element type: the outer level has no name for
            // one to belong to, so it is a nested TArray. spec/nested-multi-level.md.
            if (sf.MembersAreAnonymous)
            {
                string inner = ToUnrealTypeName(
                    sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull);

                return $"TArray<TArray<{inner}>> {name};";
            }

            string entry = RecordEntryName(table, sf);
            return sf.IsArray ? $"TArray<{entry}> {name};" : $"{entry} {name};";
        }

        string elementType = ToUnrealTypeName(sf.FirstField);

        if (sf.IsArray)
            return $"TArray<{elementType}> {name};";

        return $"{elementType} {name}{DefaultInitializer(sf.FirstField)};";
    }

    /// <summary>
    /// What a stored reference key is initialized to.
    /// </summary>
    /// <remarks>
    /// Spelled from the key's own type: `= 0` is a value an int has and an `FString` or an
    /// `FGuid` does not, and both are keys a table may be addressed by.
    /// spec/reference-key-types.md.
    /// </remarks>
    private static string RefKeyInitializer(ValueType keyType)
        => keyType switch
        {
            ValueType.String or ValueType.Uuid => "",
            ValueType.Int64 => " = 0",
            _ => " = 0",
        };

    private string DefaultInitializer(Field? field)
    {
        switch (field!.ElementType)
        {
            // These default-construct themselves.
            case ValueType.String:
            case ValueType.DateTime:
            case ValueType.TimeSpan:
            case ValueType.Uuid:
                return "";

            case ValueType.Bool: return " = false";
            case ValueType.Float: return " = 0.0f";
            case ValueType.Double: return " = 0.0";
            case ValueType.Enum: return $" = static_cast<{EnumName(field.Enum)}>(0)";
            default: return " = 0";
        }
    }

    private static string ReadKind(WireColumn wire)
    {
        // A record's member: the elements come with the record, so a member fills a field of
        // each rather than re-creating them - doing that per member would discard whatever
        // the members before it wrote.
        if (wire.Member is not null)
        {
            if (!wire.IsArray)
                return "record_member";

            // Which of the two owns the array decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_var" : "record_var";
        }

        if (wire.IsArray)
            return wire.IsRef ? "var_array_ref" : "var_array";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The Unreal type a field's values have. A reference's is the key it carries rather
    /// than the record it presents. spec/reference-key-types.md.
    /// </summary>
    private UnrealConstantSetView BuildConstantSet(ConstantSet set) => new UnrealConstantSetView
    {
        Name = "F" + set.Name.ToPascalCase(),
        Location = set.Location.ToString(),
        Comment = CommentLines(set.Comment),
        Constants = set.Constants.Select(constant => new UnrealConstantView
        {
            Name = constant.Name.ToPascalCase(),
            Type = ConstantTypeName(constant),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    /// <summary>
    /// How a constant's type is spelled, arrays included.
    /// </summary>
    /// <remarks>
    /// The type functions answer for an element and let the caller add the brackets, exactly
    /// as a field's do - so an array constant asks for the element and wraps it here.
    /// spec/primary-layout.md section 8.5.
    /// </remarks>
    private string ConstantTypeName(ConstantSet.Constant constant)
    {
        string element = ToUnrealTypeName(
            ValueTypes.ElementOf(constant.Type), constant.Enum);

        return ValueTypes.IsArray(constant.Type)
            ? LanguageProfile.Unreal.ArrayOf(element)
            : element;
    }

    /// <summary>
    /// The literal a constant is written as.
    /// </summary>
    /// <remarks>
    /// **An array constant is its elements in a brace list**, which is what `TArray` takes.
    /// The element spelling is the scalar one, so this wraps rather than repeats it.
    ///
    /// A constant never reaches the file, so there is no wire question here: what a language
    /// needs is an expression its compiler accepts in the place a constant is declared.
    /// spec/primary-layout.md section 8.5.
    /// </remarks>
    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        if (!ValueTypes.IsArray(constant.Type))
            return RenderConstantScalar(constant, constant.Type, constant.Value);

        var element = ValueTypes.ElementOf(constant.Type);

        string joined = string.Join(", ",
            ((System.Array)constant.Value!).Cast<object?>()
                .Select(value => RenderConstantScalar(constant, element, value)));

        return "{ " + joined + " }";
    }

    /// <summary>One element, or a constant that is one value.</summary>
    private string RenderConstantScalar(
        ConstantSet.Constant constant, ValueType type, object? value)
    {
        switch (type)
        {
            case ValueType.String:
                return "TEXT(\"" + EscapeUnrealString((string)value!) + "\")";

            case ValueType.Bool:
                return (bool)value! ? "true" : "false";

            case ValueType.Int32:
                return ((int)value!).ToString(System.Globalization.CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)value!).ToString(System.Globalization.CultureInfo.InvariantCulture)
                       + "LL";

            case ValueType.Float:
                return ((float)value!).ToString(
                    "R", System.Globalization.CultureInfo.InvariantCulture) + "f";

            case ValueType.Double:
                return ((double)value!).ToString(
                    "R", System.Globalization.CultureInfo.InvariantCulture);

            // Ticks, and named: the reader constructs these the same way
            // (`Out = FDateTime(Ticks)`), and neither type converts from an integer on its
            // own. A bare `LL` compiles nowhere.
            case ValueType.DateTime:
                return "FDateTime(" + ((System.DateTime)value!).Ticks.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "LL)";

            case ValueType.TimeSpan:
                return "FTimespan(" + ((System.TimeSpan)value!).Ticks.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "LL)";

            // **The four words, computed the way the reader computes them.** `FGuid` has no
            // constructor from text, and the one from four `uint32` is the shape both the
            // engine and the stub carry - so a constant is built from the same bytes and in
            // the same order as a column, and the two agree by construction rather than by
            // both being right on their own.
            case ValueType.Uuid:
                return UnrealGuidLiteral((System.Guid)value!);

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(value!, constant.Location);

                return EnumName(constant.Enum) + "::" + label.Name.ToPascalCase();
            }

            default:
                throw new TabbitException(constant.Location,
                    Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                        ("Name", constant.Name), ("Type", type),
                        ("Generator", "unreal")));
        }
    }

    /// <summary>
    /// A uuid as `FGuid(A, B, C, D)`, in the words the reader assembles from the wire bytes.
    /// </summary>
    /// <remarks>
    /// The file carries the sixteen bytes .NET writes, and `TabbitTcbReader` folds them into
    /// four words: the first is the low four bytes little-endian, the second is the next two
    /// pairs each little-endian and packed high-then-low, and the last two are four bytes each
    /// big-endian. Repeated here rather than approximated, because a constant and a column
    /// holding the same uuid have to be the same value.
    /// </remarks>
    private static string UnrealGuidLiteral(System.Guid value)
    {
        byte[] bytes = value.ToByteArray();

        uint a = (uint)bytes[0] | (uint)bytes[1] << 8 | (uint)bytes[2] << 16 | (uint)bytes[3] << 24;

        uint data2 = (uint)bytes[4] | (uint)bytes[5] << 8;
        uint data3 = (uint)bytes[6] | (uint)bytes[7] << 8;
        uint b = data2 << 16 | data3;

        uint c = (uint)bytes[8] << 24 | (uint)bytes[9] << 16
                 | (uint)bytes[10] << 8 | bytes[11];

        uint d = (uint)bytes[12] << 24 | (uint)bytes[13] << 16
                 | (uint)bytes[14] << 8 | bytes[15];

        string Word(uint word)
            => "0x" + word.ToString("x8", System.Globalization.CultureInfo.InvariantCulture)
               + "u";

        return $"FGuid({Word(a)}, {Word(b)}, {Word(c)}, {Word(d)})";
    }

    /// <summary>
    /// A string as a `TEXT(...)` literal's body.
    /// </summary>
    /// <remarks>
    /// The same set the C++ target escapes, and for the same reason: non-ASCII passes through
    /// because the file is UTF-8, and a control character would otherwise end the literal or
    /// be invisible in it.
    /// </remarks>
    private static string EscapeUnrealString(string input)
    {
        var literal = new System.Text.StringBuilder(input.Length + 2);

        foreach (char c in input)
        {
            switch (c)
            {
                case '"': literal.Append("\\\""); break;
                case '\\': literal.Append(@"\\"); break;
                case '\n': literal.Append(@"\n"); break;
                case '\r': literal.Append(@"\r"); break;
                case '\t': literal.Append(@"\t"); break;
                default:
                    if (c < 0x20)
                    {
                        literal.Append(@"\x").Append(((int)c).ToString(
                            "x2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        literal.Append(c);
                    }

                    break;
            }
        }

        return literal.ToString();
    }

    private string ToUnrealTypeName(Field? field)
        => ValueTypes.ElementOf(field!.ElementType) == ValueType.ForeignRecord
            ? ToUnrealTypeName(field!.RefKeyType, field.ResolvedRefTable?.PrimaryIndexField?.EnumOrNull)
            : ToUnrealTypeName(field!.ElementType, field!.EnumOrNull);

    private string ToUnrealTypeName(ValueType type, Models.Enum? enumm)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return EnumName(enumm!);

            // Only reached with no field to ask. A reference carries the target's primary
            // index, whose type the target decides, so the overload taking a field answers
            // instead - and `int32` stays right for a key that is one.
            // spec/reference-key-types.md.
            case ValueType.ForeignRecord:
                return "int32";

            default:
                return LanguageProfile.Unreal.ScalarTypeName(type);
        }
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// The Blueprint function library's name: `UTabbitDataLibrary` for an accessor
    /// called `FTabbitData`.
    ///
    /// Unreal's prefix says what a type is - `U` for a UObject, `F` for a plain class -
    /// so the accessor's `F` comes off before the library's `U` goes on. Prefixing
    /// blindly gave `UFTabbitDataLibrary`.
    /// </summary>
    private string LibraryName()
    {
        string name = _recipe.AccessorName;

        // Only when it is a prefix rather than the first letter of a word: `FTabbit`
        // loses its F, and `Foo` does not.
        if (name.Length > 1 && name[0] == 'F' && char.IsUpper(name[1]))
            name = name.Substring(1);

        return "U" + name + "Library";
    }

    /// <summary>Unreal prefixes an enum with E.</summary>
    private static string EnumName(Models.Enum enumm) => "E" + enumm.Name.ToPascalCase();

    /// <summary>Unreal prefixes a struct with F. A row is a struct.</summary>
    private static string RecordName(Table? table) => "F" + table!.Name.ToPascalCase() + "Row";

    /// <summary>The table class is a plain C++ class, which Unreal also prefixes with F.</summary>
    private static string TableName(Table table) => "F" + table.Name.ToPascalCase() + "Table";

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<UnrealIndexView> Indexes(Table table)
        => KeyPlans.Of(table).Select(plan =>
        {
            string keyType = ToUnrealTypeName(plan.Only.FirstField);
            bool copyCosts = keyType == "FString";
            string suffix = plan.Suffix(name => name.ToPascalCase(), "And");

            // **A component that is a reference carries the target's key, not its row.**
            // The two are one edit apart - the column's own name holds the key and the
            // derived name holds the row - and a lookup taking rows is one nobody can
            // call. `KeyComponentView.TypeOf` is the one place that decides, so the type and the
            // shape the key text is built from cannot disagree.
            var components = plan.Components.Select(component =>
            {
                var (keyType, keyEnum) = KeyComponentView.TypeOf(component);
                string spelled = ToUnrealTypeName(keyType, keyEnum);

                return new KeyComponentView
                {
                    Param = KeyComponentView.ParamOf(component.Name).ToPascalCase(),
                    Type = spelled == "FString" ? "const FString&" : spelled,
                    Member = MemberName(component.FirstField, component.Name),
                    Kind = KeyComponentView.KindOf(keyType),
                };
            }).ToList();

            string args = string.Join(", ", components.Select(component => component.Param));

            return new UnrealIndexView
            {
                Member = MemberName(plan.Only.FirstField, plan.Only.Name),
                Suffix = suffix,

                // A TMap key needs GetTypeHash, and FString has one. Building the text is
                // what every language does here; what Unreal saves by it is a USTRUCT key
                // with a hand-written hash for every shape of composite a project declares.
                KeyType = plan.IsComposite ? "FString" : keyType,

                KeyParam = plan.IsComposite
                    ? "const FString&"
                    : copyCosts ? "const " + keyType + "&" : keyType,

                MapName = "By" + suffix,
                LocalName = "LoadedBy" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                IsComposite = plan.IsComposite,
                Components = components,

                Params = plan.IsComposite
                    ? string.Join(", ", components.Select(c => c.Type + " " + c.Param))
                    : (copyCosts ? "const " + keyType + "&" : keyType) + " Key",

                Argument = plan.IsComposite ? "KeyOf" + suffix + "(" + args + ")" : "Key",
            };
        }).ToList();

    /// <summary>The field a `foreign` column's key is looked up in: the first index.</summary>
    /// <remarks>
    /// A reference points at a single-column primary key - one whose is composite is refused
    /// while the model is cooked - so this is only asked where such a field exists.
    /// </remarks>
    private static SerialField PrimaryIndex(Table table)
        => table.SerialFields.First(sf => sf.IsIndexer);

    /// <summary>
    /// The key the Blueprint row getter takes, which is the table's primary one.
    /// </summary>
    /// <remarks>
    /// Built rather than cached because it is asked for a handful of times per table and the
    /// alternative is a field on the generator whose lifetime is one loop iteration.
    /// </remarks>
    private UnrealIndexView PrimaryOf(Table table)
    {
        var primary = KeyPlans.PrimaryOf(table);

        return Indexes(table).First(
            index => index.Suffix == primary.Suffix(name => name.ToPascalCase(), "And"));
    }

    private static string MemberName(Field? field) => MemberName(field, field!.Name);

    /// <summary>
    /// A member name, PascalCase as Unreal writes them.
    ///
    /// A boolean gets the `b` prefix the engine's own style uses, which is worth
    /// following here because the generated types show up beside the engine's in the
    /// editor.
    /// </summary>
    /// <remarks>
    /// The one code target besides Go with no `MemberCase` setting, and for a different
    /// reason: what this spells is not a casing. A bool UPROPERTY is `bIsOpen` - a lower-case
    /// `b` in front of a Pascal-cased name, chosen by the member's type - and that shape has
    /// no snake or camel equivalent to move to. `b` glued to a snake-cased name gives
    /// `bis_open`, which is neither convention.
    ///
    /// The engine's own tooling is the other half of it. UHT reads these declarations and
    /// Unreal's coding standard is not a preference a project overrides per recipe.
    /// </remarks>
    private static string MemberName(Field? field, string name)
    {
        string cased = LanguageProfile.Unreal.MemberName(name.ToPascalCase());

        return field!.ElementType == ValueType.Bool ? "b" + cased : cased;
    }

}
