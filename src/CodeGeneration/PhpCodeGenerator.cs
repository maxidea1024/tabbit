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
/// Settings for the PHP target.
/// </summary>
public sealed class PhpRecipe : IOutputRecipe
{
    /// <summary>Directory the generated file and the reader are written into.</summary>
    public string Path { get; set; } = "";

    /// <summary>Namespace the generated file declares.</summary>
    public string Namespace { get; set; } = "GameData";

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
    /// default: one that ships its data alongside its code has no use for it, it is the
    /// only generated file that reaches the network, and it is the only one that wants
    /// an extension - `ext-curl`.
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
/// Emits one PHP file holding every generated type, plus the binary reader.
///
/// PHP 8.1 or later, for two things worth having: backed enums, so an enum carries its
/// declared value rather than needing a lookup table beside it, and typed properties,
/// so a record says what it holds.
///
/// int64 is `int` and that is safe here, unlike in TypeScript and Dart: PHP's integer
/// is a full 64 bits on any 64 bit build, so 2^53+1 survives. What is not safe is
/// reading it with `unpack('P')`, which the reader explains.
///
/// The shape lives in templates/php.sbn.
/// </summary>
[TabbitTarget("php", TargetKind.CodeGeneration, Order = 87)]
public class PhpCodeGenerator : CodeGenerator<PhpRecipe>
{
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private PhpRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Camel;

    /// <summary>
    /// A record group generates a class and a list of it; a member column fills one of its
    /// properties.
    /// </summary>
    /// <remarks>
    /// The twelfth of the thirteen, and the same split as the eleven before it - declaration
    /// per field, reading per wire column.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a class declared beside the element type, and the read reaches it with a
    /// longer member path. Its holder's constructor makes it - a typed property left unset is an
    /// error to read rather than a null. spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `has{Field}` property beside the value one.
    /// </summary>
    /// <remarks>
    /// Not a nullable property. PHP already uses null here for the two things that have no
    /// value - an unresolved reference and a uuid before the read - so a third meaning on the
    /// same word would be one nobody could tell apart. spec/optional-fields.md has the rest.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, PhpRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Camel, "php");

        Generate();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes a file per table, per enum and per constant set, plus the accessor.
    /// </summary>
    /// <remarks>
    /// It used to be one file holding all of it, which made a deleted table a hunk of dead
    /// code inside a file that still parsed. The layout matches the C#, Kotlin and
    /// TypeScript targets.
    ///
    /// PHP is the one that needs wiring rather than just splitting: there is no autoloader
    /// here, so each file requires what it uses. A table requires the reader and any enum
    /// its properties are typed as, from <see cref="TypeDependencies"/>; the accessor requires
    /// every part, so a consumer still includes one file and gets the model.
    ///
    /// Not the tables a table references. A reference resolves to the other table's record or
    /// to one of its fields, and the accessor has required both files long before it links
    /// them - `require_once` would be harmless either way, but a require that is never the
    /// reason a name resolves is one more line for a reader to check.
    /// </remarks>
    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for PHP into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        // Root level, so the reader is one directory down and the parts are beside it.
        var accessorRequires = new List<string> { Require(0, "tabbit/TcbReader.php") };

        accessorRequires.AddRange(view.Enums.Select(e => Require(0, $"enums/{e.Name}.php")));
        accessorRequires.AddRange(view.ConstantSets.Select(s => Require(0, $"constants/{s.Name}.php")));
        accessorRequires.AddRange(view.Tables.Select(t => Require(0, $"tables/{t.TableName}.php")));

        Write(_recipe.AccessorName + ".php", "php-accessor.sbn", new PhpPartView
        {
            Namespace = _recipe.Namespace,
            Requires = accessorRequires,
            Tables = view.Tables,
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // One directory down, and it needs whatever enums its properties name.
            var requires = new List<string> { Require(1, "tabbit/TcbReader.php") };

            requires.AddRange(TypeDependencies.EnumsNamedBy(pair.model)
                                              .Concat(TypeDependencies.MultiTargetDiscriminatorsOf(pair.model))
                .Select(enumm => Require(1, $"enums/{EnumName(enumm)}.php")));

            // The accessor, for the encryption key it holds. It requires this file back, and
            // `require_once` marks a file included before it runs it - so the cycle resolves
            // rather than recursing, and a table file stays usable on its own.
            requires.Add(Require(1, _recipe.AccessorName + ".php"));

            Write(System.IO.Path.Combine("tables", pair.rendered.TableName + ".php"), "php-table.sbn",
                  new PhpPartView
                  {
                      Namespace = _recipe.Namespace,
                      Requires = requires,
                      Table = pair.rendered,
                      Accessor = view.Accessor,
                  });
        }

        // An enum and a constant class name nothing outside themselves: a backed enum is
        // its own declaration, and a constant renders as a literal.
        foreach (var enumm in view.Enums)
        {
            Write(System.IO.Path.Combine("enums", enumm.Name + ".php"), "php-enum.sbn",
                  new PhpPartView
                  {
                      Namespace = _recipe.Namespace,
                      Requires = Array.Empty<string>(),
                      Enumm = enumm,
                  });
        }

        foreach (var set in view.ConstantSets)
        {
            Write(System.IO.Path.Combine("constants", set.Name + ".php"), "php-constants.sbn",
                  new PhpPartView
                  {
                      Namespace = _recipe.Namespace,
                      Requires = Array.Empty<string>(),
                      Set = set,
                  });
        }
    }

    /// <summary>
    /// A `require_once` line, relative to a file <paramref name="depth"/> directories below
    /// the output root.
    /// </summary>
    /// <remarks>
    /// Forward slashes, which PHP accepts on every platform - and which keep the generated
    /// text the same wherever the conversion ran.
    /// </remarks>
    private static string Require(int depth, string fromRoot)
    {
        string up = string.Concat(Enumerable.Repeat("/..", depth));

        return $"require_once __DIR__ . '{up}/{fromRoot}';";
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
            "Tabbit.Runtime.Php.TcbReader.php",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "TcbReader.php"));

        // Asked for rather than assumed. It reaches the network, it wants `ext-curl`,
        // and it is of no use to a program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Php.TabbitUpdater.php",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "TabbitUpdater.php"));
        }
    }

    // --------------------------------------------------------------- view

    private PhpFileView BuildView() => new PhpFileView
    {
        Namespace = _recipe.Namespace,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private PhpEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new PhpEnumView
        {
            Name = EnumName(enumm),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            DefaultCase = CaseName(fallback.Name),
            Cases = enumm.Labels.Select(label => new PhpEnumCaseView
            {
                Name = CaseName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };
    }

    private PhpConstantSetView BuildConstantSet(ConstantSet constantSet) => new PhpConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new PhpConstantView
        {
            Name = ConstantName(constant.Name),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private PhpTableView BuildTable(Table table) => new PhpTableView
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

        NeedsConstructor = table.SerialFields.Any(sf => sf.IsRecord),

        // Whether any column reads through a cursor. PHP needs no declaration ahead
        // of the first `$cursor = ...` assignment, so unlike the C# template nothing
        // in the read method renders from this - it is here so the templates of the
        // every language can ask the same questions of their views.
        NeedsCursor = table.WireColumns.Any(UsesCursor),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<PhpIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf =>
        {
            string keyType = ResolvedElementType(sf);

            return new PhpIndexView
            {
                Member = PhpName(sf.Name),
                Suffix = sf.Name.ToPascalCase(),
                KeyType = keyType,

                // A PHP array is keyed by int or string and nothing else, so that is what the
                // docblock can honestly claim whatever the column holds - and which of the two
                // is decided by the same conversion the subscript uses, not by the declared
                // parameter type. A uuid is a `Uuid` at the boundary and a string as an offset.
                KeyDocType = OffsetDocType(sf.ElementType),

                KeyOffset = Offset("$key", sf.ElementType),
                MemberOffset = Offset("$record->" + PhpName(sf.Name), sf.ElementType),

                MapName = "by" + sf.Name.ToPascalCase(),
                LocalName = "$by" + sf.Name.ToPascalCase(),
                FieldName = sf.Name.ToPascalCase(),
            };
        }).ToList();

    /// <summary>Which of PHP's two offset types <see cref="Offset"/> produces.</summary>
    private static string OffsetDocType(Models.ValueType elementType)
        => elementType is Models.ValueType.String or Models.ValueType.Uuid ? "string" : "int";

    /// <summary>
    /// A key value as something a PHP array will accept as an offset.
    /// </summary>
    /// <remarks>
    /// PHP arrays are keyed by `int` or `string` and by nothing else. Subscripting one with
    /// anything else is a `TypeError` at runtime, not a coercion - and unlike every other
    /// language here that means a lookup can be declared, shipped and never once tried. So
    /// the conversion is explicit and lives with the index rather than in the template, which
    /// has no way to ask what the column holds.
    ///
    /// Only two types need it. A uuid is a value object, so it goes through the canonical
    /// text its `__toString` already produces - the same spelling the other languages key
    /// on. A backed enum carries its number in `->value`. `int`, `bigint` and `string` are
    /// already offsets.
    ///
    /// This was reachable before the primary index accepted either of them: a `*` column has
    /// taken an enum all along and nothing exercised one. `key-types` is the fixture that does.
    /// </remarks>
    private static string Offset(string expression, Models.ValueType elementType)
    {
        switch (elementType)
        {
            case Models.ValueType.Uuid:
                return $"(string){expression}";

            case Models.ValueType.Enum:
                return $"{expression}->value";

            default:
                return expression;
        }
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
    /// spec/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "!== ''",
            ValueType.Uuid => "->isSet()",
            _ => "!== 0",
        };

    /// <summary>
    /// One column whose value is a row of one of several tables.
    /// </summary>
    private PhpMultiReferenceView BuildMultiReference(MultiTargetColumn column)
        => new PhpMultiReferenceView
        {
            KeyMember = PhpName(column.Group.Name),
            SlotMember = PhpName(column.Group.Name) + "Row",
            TargetMember = PhpName(column.Group.Name) + "Target",
            TargetTypeName = column.Discriminator.Name.ToPascalCase(),
            NoneCase = CaseName("None"),
            KeyIsSet = KeyIsSetSuffix(column.Field.RefKeyType),
            Targets = column.Targets.Select(target => new PhpMultiTargetView
            {
                Table = PhpName(target.Name),
                RecordName = target.Name.ToPascalCase() + "Record",
                Method = PhpName(column.Group.Name + "As" + target.Name.ToPascalCase()),
                Case = CaseName(target.Name),
                Lookup = PrimaryLookup(target),
            }).ToList(),
        };

    private PhpFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = PhpName(sf.Name);
        bool nullable = sf.RowMayBeAbsent;

        var declarations = Declarations(sf, name).ToList();

        // False until the read says otherwise, so a file that does not carry the column
        // leaves the property absent rather than claiming a value it never got.
        if (nullable)
        {
            declarations.Add("");
            declarations.Add($"public bool ${PresenceMember(sf)} = false;");
        }

        // And the per-element answer, empty until the read fills it: an index into an empty
        // array is out of range, and the answer there is that the element has a value.
        // spec/nullable-array-elements.md.
        if (sf.ElementMayBeAbsent)
        {
            declarations.Add("");
            declarations.Add($"public array ${ElementPresenceMember(sf)} = [];");
        }

        return new PhpFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Declarations = declarations,
            ConstructorLines = Array.Empty<string>(),
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<PhpRecordMemberView>(),
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
    /// The starting value is built in the record's constructor rather than at the
    /// declaration, because a PHP property initializer has to be a constant expression and
    /// `new SlotEntry()` is not one - the same rule that already keeps a uuid property
    /// nullable here.
    ///
    /// The class name carries the table's: every generated class shares one namespace, so
    /// two tables each holding a `Slot` group would be the same name twice.
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
    /// collects the classes it produces. A nested member is made in its holder's constructor:
    /// a property initializer has to be a constant expression, and a typed property left unset
    /// is an error to read rather than a null. spec/nested-multi-level.md.
    /// </remarks>
    private List<PhpRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<PhpRecordTypeView> declared, List<string> constructorLines)
    {
        var result = new List<PhpRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                result.Add(new PhpRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),
                    Declarations = MemberDeclarations(member),
                });

                // A reference member that is an array starts as a list of nulls, the same
                // shape a reference array outside a record starts as: the linking pass fills
                // the positions it resolves, and the ones it does not have to already be
                // there. spec/references-in-records.md.
                if (member.IsRef && member.IsArray)
                {
                    constructorLines.Add(
                        $"$this->{PhpName(member.Name)} = array_fill(0, {member.Fields.Count}, null);");
                }

                continue;
            }

            // A level below. The class name carries the path so two records each holding a
            // `Position` do not name one class twice.
            string typeName = prefix + member.Name.ToPascalCase();
            var nestedConstructor = new List<string>();
            var nested = BuildRecordMembers(
                member.Members, typeName, table, group, declared, nestedConstructor);

            declared.Add(new PhpRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record::${PhpName(group.Name)}",
                ConstructorLines = nestedConstructor,
            });

            result.Add(new PhpRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declarations = new[] { $"public {typeName} ${PhpName(member.Name)};" },
            });

            constructorLines.Add($"$this->{PhpName(member.Name)} = new {typeName}();");
        }

        return result;
    }

    private PhpFieldView BuildRecordField(Table table, SerialField sf)
    {
        string name = PhpName(sf.Name);
        string entry = RecordTypeName(table, sf);

        // Innermost first, so a class is declared before the one naming it.
        var recordTypes = new List<PhpRecordTypeView>();
        var entryConstructor = new List<string>();
        var members = BuildRecordMembers(
            sf.Members, entry, table, sf, recordTypes, entryConstructor);

        recordTypes.Add(new PhpRecordTypeView
        {
            TypeName = entry,
            Members = members,
            IsOutermost = true,
            Owner = $"{table.Name.ToPascalCase()}Record::${name}",
            ConstructorLines = entryConstructor,
        });

        var declarations = new List<string>();
        var constructor = new List<string>();

        if (sf.MembersAreAnonymous)
        {
            // An array of arrays needs no element type: the outer level has no name for one
            // to belong to, so the inner list is what an element is. spec/nested-multi-level.md.
            string inner = LanguageProfile.Php.ScalarTypeName(sf.Members[0].ElementType);

            declarations.Add($"/** @var list<list<{inner}>> */");
            declarations.Add($"public array ${name} = [];");

            constructor.Add($"for ($i = 0; $i < {sf.Members.Count}; $i++) {{");
            constructor.Add($"    $this->{name}[] = array_fill(0, {sf.RecordElementCount}, "
                            + $"{MemberDefault(sf.Members[0])});");
            constructor.Add("}");
        }
        else if (sf.IsArray)
        {
            declarations.Add($"/** @var list<{entry}> */");
            declarations.Add($"public array ${name} = [];");

            // A list with its elements already made, where the length is the sheet's column
            // count. A trimmed group stays empty here, because its length is the row's.
            if (!table.TrimTrailingArrayElements)
            {
                constructor.Add($"for ($i = 0; $i < {sf.RecordElementCount}; $i++) {{");
                constructor.Add($"    $this->{name}[] = new {entry}();");
                constructor.Add("}");
            }
        }
        else
        {
            declarations.Add($"public {entry} ${name};");
            constructor.Add($"$this->{name} = new {entry}();");
        }

        return new PhpFieldView
        {
            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,
            Declarations = declarations,
            ConstructorLines = constructor,
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
    private PhpColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new PhpColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire)
                ? "$elementCount = $cursor->nextLength();"
                : "$elementCount = $reader->readCounter32();",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = PhpName(wire.Group.Name),

            // A record's member column assigns one property of the element rather than the
            // member itself: `$record->slot[$j]->id` instead of `$record->slot[$j]`.
            MemberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "->" + PhpName(part))),
            MemberAt = wire.MemberAt,

            // A reference member reads into the key beside the row it will resolve to, and
            // the suffix goes on the member rather than after the subscript - `itemIdIndex[$j]`
            // rather than `itemId[$j]Index`. spec/references-in-records.md.
            MemberRefSuffix = (wire.Member is not null && wire.IsRef) ? "Index" : "",

            RecordTypeName = wire.Group.IsRecord ? RecordTypeName(table, wire.Group) : "",
            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,
            ReadElement = ReadElementExpression(wire),
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
    /// Every generated class shares one namespace, so two tables each holding a `Slot` group
    /// would declare the same name twice.
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
    /// <summary>The member holding which of an array's elements have a value.</summary>
    private string ElementPresenceMember(SerialField sf)
        => sf.IsRecord ? "" : PhpName("has_" + sf.Name + "_at");

    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : PhpName("has_" + sf.Name);

    /// <summary>One record member's declaration, by the same rules an ordinary field follows.</summary>
    private IReadOnlyList<string> MemberDeclarations(RecordMember member)
    {
        string name = PhpName(member.Name);

        // A reference member carries two properties, exactly as a reference outside a record
        // does: the key that came off the wire, and the row it resolves to once every table is
        // loaded. Both inside the element, because a group may hold more than one reference
        // and a name built from the group and the target would collide the moment two members
        // point at one table.
        //
        // No third property for whether it resolved - the resolved one is nullable, and null
        // is what a reference into a row that is not there stays.
        // spec/references-in-records.md.
        if (member.IsRef)
        {
            string row = member.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";
            string key = LanguageProfile.Php.ScalarTypeName(member.FirstField!.RefKeyType);

            return member.IsArray
                ? new[]
                {
                    $"/** @var list<?{row}> */",
                    $"public array ${name} = [];",
                    "",
                    $"/** @var list<{key}> */",
                    $"public array ${name}Index = [];",
                }
                : new[]
                {
                    $"public ?{row} ${name} = null;",
                    "",
                    $"public {RefKeyDeclaration(member.FirstField!.RefKeyType, key)} ${name}Index"
                        + $"{RefKeyInitializer(member.FirstField!.RefKeyType)};",
                };
        }

        string type = MemberTypeName(member);

        // A uuid cannot be defaulted in place: a property initializer has to be a constant
        // expression and `new Uuid(...)` is not one. Nullable and starting null is also
        // honest - it holds nothing until the record is read.
        if (member.ElementType == ValueType.Uuid && !member.IsArray)
            return new[] { $"public ?{type} ${name} = null;" };

        // The array is the member's when the group is one record - same columns, same wire,
        // and only which of the two owns it differs. `array_fill` is not a constant
        // expression either, so the length is spelled out; the constructor fills it.
        if (member.IsArray)
        {
            return new[]
            {
                $"/** @var {type}[] */",
                $"public array ${name} = [];",
            };
        }

        return new[] { $"public {type} ${name} = {MemberDefault(member)};" };
    }

    private string MemberTypeName(RecordMember member)
        => member.ElementType == ValueType.Enum
            ? EnumName(member.FirstField!.Enum)
            : LanguageProfile.Php.ScalarTypeName(member.FirstField!.ElementType);

    /// <summary>
    /// How a stored reference key is typed, and what it starts as.
    /// </summary>
    /// <remarks>
    /// A uuid is a class here, and PHP will not take a constant expression as a class-typed
    /// property's default - so it is nullable and starts null, exactly as a uuid member of a
    /// record does. spec/reference-key-types.md.
    /// </remarks>
    private static string RefKeyDeclaration(ValueType keyType, string typeName)
        => keyType == ValueType.Uuid ? "?" + typeName : typeName;

    private static string RefKeyInitializer(ValueType keyType)
        => keyType switch
        {
            ValueType.String => " = ''",
            ValueType.Uuid => " = null",
            _ => " = 0",
        };

    private string MemberDefault(RecordMember member)
    {
        switch (member.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Enum:
                return $"{EnumName(member.FirstField!.Enum)}::{DefaultCaseOf(member.FirstField!.Enum)}";
            default: return "0";
        }
    }

    /// <summary>
    /// What an absent row's property is set back to, so the binary path lands where the
    /// JSON one does.
    /// </summary>
    /// <remarks>
    /// The property's own type rather than its element's: an optional array holds a list,
    /// and its empty value is an empty list rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "[]";

        // A resolved reference and a uuid are both nullable properties here, and absence is
        // exactly what null says for either.
        if (wire.ElementType == ValueType.ForeignRecord || wire.ElementType == ValueType.Uuid)
            return "null";

        switch (wire.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Enum:
                return $"{EnumName(wire.TagCarrier.Enum)}::{DefaultCaseOf(wire.TagCarrier.Enum)}";
            default: return "0";
        }
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
            ? $"$cursor = new TcbColumnCursor($reader, $column, $count, '{tableName}.{wire.Name}');"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to the int32 or the string read:
    /// int32 members, enums, references and strings. The other cursor scalars stay
    /// per-row - their encodings are dictionaries, where the per-row work is already one
    /// index lookup.
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

        string name = PhpName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "->" + PhpName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"$records[$i]->{name}Index = $value;"
                : $"$records[$i]->{name}{memberAccess}Index = $value;";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"$records[$i]->{name}{memberAccess} = {EnumName(wire.TagCarrier.Enum)}::tryFrom($value) ?? {EnumName(wire.TagCarrier.Enum)}::{DefaultCaseOf(wire.TagCarrier.Enum)};";

            return $"$records[$i]->{name}{memberAccess} = $value;";
    }

    /// <summary>
    /// The property declarations, each typed and initialized.
    ///
    /// Initialized rather than left uninitialized, because reading a typed property
    /// that was never assigned is an Error in PHP - where every other generated reader
    /// hands back a default.
    /// </summary>
    private IReadOnlyList<string> Declarations(SerialField sf, string name)
    {
        string elementType = ResolvedElementType(sf);

        if (sf.IsRef)
        {
            // A reference contributes two properties: the index off the wire, and the
            // record it resolves to once every table is loaded. The resolved one is
            // nullable because a reference into a row that is not there stays null
            // rather than inventing a record.
            return sf.IsArray
                ? new[]
                {
                    $"/** @var list<?{elementType}> */",
                    $"public array ${name} = [];",
                    "",
                    "/** @var list<int> */",
                    $"public array ${name}Index = [];",
                }
                : new[]
                {
                    $"public ?{elementType} ${name} = null;",
                    "",
                    $"public int ${name}Index = 0;",
                };
        }

        if (sf.IsArray)
        {
            return new[]
            {
                $"/** @var list<{elementType}> */",
                $"public array ${name} = [];",
            };
        }

        // A uuid is the one scalar that cannot be defaulted in place: a property
        // initializer has to be a constant expression and `new Uuid(...)` is not. So
        // the property is nullable and starts null, which is also honest - it holds
        // nothing until the record is read.
        if (sf.ElementType == ValueType.Uuid)
            return new[] { $"public ?{elementType} ${name} = null;" };

        return new[] { $"public {elementType} ${name} = {DefaultValue(sf)};" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";

            case ValueType.Enum:
                return $"{EnumName(sf.FirstField!.Enum)}::{DefaultCaseOf(sf.FirstField!.Enum)}";

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
            ? "TcbReader::KIND_VAR_ARRAY"
            : (wire.IsFixedArray ? "TcbReader::KIND_FIXED_ARRAY" : "TcbReader::KIND_SCALAR");

        // -1 where one column owns the whole array: the file states how many elements it
        // holds and the read takes it from there, so there is no length here to hold it to.
        // A record member keeps its count - several columns fill one array and the number
        // they agree on is part of the generated shape, so a disagreement is a schema change
        // rather than data. spec/nullable-array-elements.md.
        bool ownsItsArray = wire.IsFixedArray && wire.Member is null;

        int count = wire.IsVariableLengthArray ? 0 : (ownsItsArray ? -1 : wire.Cells.Count);

        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `TcbReader::ELEMENT_I32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "TcbReader::ELEMENT_STRING",
                ValueType.Int64 => "TcbReader::ELEMENT_I64, TcbReader::ELEMENT_I32, TcbReader::ELEMENT_VARINT",
                ValueType.Uuid => "TcbReader::ELEMENT_UUID",
                _ => "TcbReader::ELEMENT_I32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "TcbReader::ELEMENT_I32, TcbReader::ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "TcbReader::ELEMENT_I64, TcbReader::ELEMENT_I32, TcbReader::ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "TcbReader::ELEMENT_F64, TcbReader::ELEMENT_F32, TcbReader::ELEMENT_I32"; break;
                case ValueType.Float: accepted = "TcbReader::ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "TcbReader::ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "TcbReader::ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "TcbReader::ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "TcbReader::ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "TcbReader::ELEMENT_I64"; break;

                default:
                    throw new TabbitException($"The php generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one.
        string elements = wire.HasOptionalElements ? ", true" : "";

        return $"TcbReader::checkColumn($column, '{tableName}.{wire.Name}', {kind}, {count}, "
            + $"{nullable}, [{accepted}]{elements});";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member walks the elements without building them - the list was made
    /// by the constructor - while a trimmed one reads its length from the row, and there the
    /// first member does build because no constructor could have known how long this row's
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

            // Which of the two owns the array decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_serial" : "record_serial";
        }

        if (wire.IsVariableLengthArray)
            // A trimmed array of references: the length is the row's, and the key still goes
            // in the array beside the values. Read as a plain `var_array` it put the keys where
            // the resolved rows belong, and the linking pass then found nothing to resolve -
            // silently, because this language does not type them apart. Nothing held the shape:
            // `foreign[]` is refused, so it is only reachable through a folded group with
            // trimming on. spec/variable-length-record-arrays.md.
            return wire.IsRef ? "var_array_ref" : "var_array";

        if (wire.IsFixedArray)
            return wire.IsRef ? "serial_ref" : "serial";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private PhpAccessorView BuildAccessor() => new PhpAccessorView
    {
        Name = _recipe.AccessorName.ToPascalCase(),
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new PhpTableSlotView
        {
            Name = PhpCamelName(table.Name),
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
            .Select(x => new PhpCrossReferenceView
            {
                Table = PhpName(x.Table.Name),
                MultiFields = x.MultiFields,
                Fields = x.Fields.Select(sf => new PhpReferenceFieldView
                {
                    Name = PhpName(sf.Name),
                    RefTable = PhpName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "$target"
                        : "$target->" + PhpName(sf.FirstField!.ResolvedRefField!.Name),
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
    /// Whole expressions rather than the parts to build them from: which of the three record
    /// shapes this is decides where the element number sits - on the group, on the member, or
    /// nowhere - and the template should not be the place that knows.
    /// spec/references-in-records.md.
    /// </remarks>
    private PhpRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = PhpName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "->" + PhpName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsFixedArray || wire.IsVariableLengthArray;

        // Where the element number goes is the whole difference between the record shapes -
        // the group's array, the member's, or neither. spec/nested-multi-level.md.
        string path = !isArray || wire.Group.MembersAreArrays
            ? $"$record->{name}{member}"
            : $"$record->{name}[$j]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[$j]" : "";

        return new PhpRecordReferenceView
        {
            Access = path + subscript,
            Key = path + "Index" + subscript,

            // Whichever list holds the elements. `count` rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Count = isArray
                ? (wire.Group.MembersAreArrays ? $"\\count({path})" : $"\\count($record->{name})")
                : "",

            RefTable = PhpName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The expression that reads one value, at whatever depth the template places it.
    /// </summary>
    /// <remarks>
    /// A column can arrive encoded, so it reads through the cursor - which also carries the
    /// lossless promotions. An array's elements read through it as well, by the same calls:
    /// what differs is only that the row's length comes from the cursor first.
    /// <see cref="DirectReadExpression"/> is what is left for the elements no encoding
    /// applies to.
    /// </remarks>
    private string ReadElementExpression(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return DirectReadExpression(wire);

        // The key the target is addressed by, which is not always an int32. `nextI32` for
        // every reference is what kept a table keyed by anything else from being pointed at
        // from this language. spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => "$cursor->nextI64()",
                ValueType.String => "$cursor->nextString()",
                _ => "$cursor->nextI32()",
            };
        }

        switch (wire.ElementType)
        {
            // Enum values travel as their int, decoded by the cursor. `tryFrom` rather
            // than `from` for the same reason as the raw path: a value the sheet never
            // declared lands on the fallback instead of throwing.
            case ValueType.Enum:
            {
                string name = EnumName(wire.TagCarrier.Enum);

                return $"{name}::tryFrom($cursor->nextI32()) ?? {name}::{DefaultCaseOf(wire.TagCarrier.Enum)}";
            }

            case ValueType.Int32: return "$cursor->nextI32()";
            case ValueType.Int64: return "$cursor->nextI64()";
            case ValueType.Double: return "$cursor->nextF64()";
            case ValueType.Float: return "$cursor->nextF32()";
            case ValueType.Bool: return "$cursor->nextBool()";

            // Ticks, exactly as readDateTimeTicks and readTimespanTicks hand them back:
            // a PHP member holds the i64 the column carried, so there is nothing to
            // build around it.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "$cursor->nextI64()";

            default: return "$cursor->nextString()";
        }
    }

    /// <summary>
    /// The read off the reader itself, for a column with no cursor to reach.
    /// </summary>
    private string DirectReadExpression(WireColumn wire)
    {
        switch (wire.ElementType)
        {

            // Enum values travel zig-zag encoded. `tryFrom` rather than `from`, so a
            // value the sheet never declared lands on the fallback instead of throwing
            // - which is what the other generated readers do.
            case ValueType.Enum:
            {
                string name = EnumName(wire.TagCarrier.Enum);

                return $"{name}::tryFrom($reader->readEnum()) ?? {name}::{DefaultCaseOf(wire.TagCarrier.Enum)}";
            }

                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/reference-key-types.md.
                case ValueType.ForeignRecord:
                    return LanguageProfile.Php.ReadCall(wire.RefKeyType);

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            default: return LanguageProfile.Php.ReadCall(wire.ElementType);
        }
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return sf.FirstField!.ResolvedRefTable!.Name.ToPascalCase() + "Record";

        if (sf.ElementType == ValueType.Enum)
            return EnumName(sf.FirstField!.Enum);

        return LanguageProfile.Php.ScalarTypeName(sf.FirstField!.ElementType);
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

            // The text, not a Uuid: a class constant has to be a constant expression
            // and `new` is not one. The caller builds a Uuid from it if it wants one.
            case ValueType.Uuid:
                return Quote(((Guid)constant.Value!).ToString());

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return $"{EnumName(constant.Enum)}::{CaseName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the php generator cannot render.");
        }
    }

    /// <summary>
    /// A single-quoted PHP string.
    ///
    /// Single quotes because they interpolate nothing: a value holding `$name` or a
    /// backslash escape would otherwise be evaluated rather than stored. Only the quote
    /// and the backslash need escaping inside them.
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

    // ------------------------------------------------------------- helpers

    private static string EnumName(Models.Enum enumm) => enumm.Name.ToPascalCase();

    /// <summary>An enum case, PascalCase as PHP's own enums are written.</summary>
    private static string CaseName(string name) => name.ToPascalCase();

    private static string DefaultCaseOf(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return CaseName(fallback.Name);
    }

    /// <summary>A class constant, SCREAMING_SNAKE_CASE as PHP writes them.</summary>
    private static string ConstantName(string name) => name.ToUpperSnakeCase();

    /// <summary>A property name, camelCase.</summary>
    private string PhpName(string name) => LanguageProfile.Php.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for a name that is not a member - the accessor's slot per table.
    /// </summary>
    /// <remarks>
    /// camelCase because that is how PHP writes an identifier, not because a member is
    /// spelled that way. Sharing one function let the two look like one rule.
    /// </remarks>
    private static string PhpCamelName(string name) => LanguageProfile.Php.MemberName(name.ToCamelCase());

}
