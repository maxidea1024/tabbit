using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Serilog;
using Tabbit.Extensions;
using Tabbit.Helpers;
using Tabbit.Models;
using Tabbit.Models.Raw;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The layout Tabbit defines: entities are declared with `~~type:Name~~` markers and can
/// sit anywhere on a sheet.
/// </summary>
/// <remarks>
/// Row layout of each entity, top to bottom. The first two rows are the same for
/// all three; what follows differs.
///
/// <code>
///     table  (at least 3 columns wide)
///        ~~table:Name[:side]~~
///        table description
///        field names          &lt;- a `*` prefix marks a secondary index
///        field descriptions
///        field types
///        field detail types   &lt;- enum name, or reference target. Blank otherwise.
///        target sides
///        data rows...
///
///     enum  (3 columns)
///        ~~enum:Name[:side]~~
///        enum description
///        column captions      &lt;- for human readers only; skipped when parsing
///        label | value | description ...
///
///     const  (5 columns)
///        ~~const:Name[:side]~~
///        set description
///        column captions      &lt;- for human readers only; skipped when parsing
///        name | type | detail type | value | description ...
/// </code>
///
/// A definition extends downward while the cell in its first column is non-empty,
/// and rightward while cells are non-empty, so an entity is bounded by blank cells
/// rather than by a declared size. The minimum heights in _possibleEntities are
/// the body only - hence the `- 2`, which drops the marker and description rows.
/// </remarks>
[TabbitLayout("tabbit",
    Summary = "Entities declared with `~~table:Name~~` markers, several to a sheet.")]
public sealed class TabbitLayoutParser : ILayoutParser
{
    public class Size
    {
        public int width;
        public int height;
    }

    private readonly Dictionary<string, Size> _possibleEntities = new Dictionary<string, Size> {
        { "table", new Size{ width = 3, height = 7 - 2 } },
        { "enum", new Size{ width = 3, height = 3 - 2 } },
        { "const", new Size{ width = 5, height = 3 - 2 } },
    };

    private class DefinitionRect
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    private class EntityDefinition
    {
        [JsonIgnore] public RawSheet rawSheet = null!;
        [JsonIgnore] public Location location = null!;
        public string rawName = "";
        public string name = "";
        public string type = "";
        public string? comment;
        public TargetSide targetSide;
        public DefinitionRect rect = null!;
    }

    private CookingContext _context = null!;

    /// <summary>
    /// What the marker scan found, kept between the two passes so the sheets are walked
    /// once rather than once per entity kind.
    /// </summary>
    private List<EntityDefinition> _definitions = [];

    private Model Model => _context.Model;

    public void ParseDeclarations(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;
        _definitions = ScanEntityDefinitions(sheets);

        // Since const and enum have a reference relationship, they must be parsed first.

        foreach (var def in _definitions)
        {
            if (def.type == "enum")
                Model.Enums.Add(ParseEnum(def));
            else if (def.type == "const")
                Model.ConstantSets.Add(ParseConstantSet(def));
        }
    }

    public void ParseTables(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;

        foreach (var def in _definitions)
        {
            if (def.type == "table")
                Model.Tables.Add(ParseTable(def));
        }
    }

    private Models.Enum ParseEnum(EntityDefinition def)
    {
        var result = new Models.Enum
        {
            Location = def.location,
            TargetSide = def.targetSide,
            RawName = def.rawName,
            Name = def.name,
            Comment = def.comment ?? ""
        };

        Log.Information($"Parsing enum `{result.Name}`. ({result.Location})");

        int dataRowStart = def.rect.y + 1; // skip header row
        int dataRowEnd = def.rect.y + def.rect.height;
        int dataColStart = def.rect.x;

        result.Labels = new List<Models.Enum.Label>();

        for (int rowIdx = dataRowStart; rowIdx < dataRowEnd; rowIdx++)
        {
            var row = def.rawSheet.Rows[rowIdx];

            var nameCol = row[dataColStart + 0];
            var valueCol = row[dataColStart + 1];
            var descCol = row[dataColStart + 2];

            // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
            string rawName = nameCol.Value;
            string name = rawName.ToPascalCase();

            // Skip if marked with comments.
            if (_context.IsIgnorantName(name))
                continue;

            // Ensure identifier
            _context.RequiresIdentifier(name, nameCol.Location);

            // Check if the label is already defined.
            if (result.Contains(name))
                throw new TabbitException(nameCol.Location, $"Label '{name}' is already defined in enum '{result.Name}'.");

            if (!int.TryParse(valueCol.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int labelValue))
            {
                throw new TabbitException(valueCol.Location,
                    $"Label '{name}' in enum '{result.Name}' has value `{valueCol.Value}`, which is not an integer.");
            }

            // Add a label.
            var label = new Models.Enum.Label
            {
                Location = nameCol.Location,
                RawName = rawName,
                Name = name,
                Value = labelValue,
                Comment = descCol.Value
            };
            result.Labels.Add(label);
        }

        // An enum with no zero entry gives every unassigned field of that type a
        // value with no name, so one is supplied unless the recipe says otherwise.
        _context.ApplyAutoNoneLabel(result, def.location);

        return result;
    }

    private Models.ConstantSet ParseConstantSet(EntityDefinition def)
    {
        Log.Information($"Parsing constant-set `{def.name}`. ({def.location})");

        var result = new Models.ConstantSet
        {
            Location = def.location,
            TargetSide = def.targetSide,
            RawName = def.rawName,
            Name = def.name,
            Comment = def.comment ?? ""
        };

        int dataRowStart = def.rect.y + 1; // skip header row
        int dataRowEnd = def.rect.y + def.rect.height;
        int dataColStart = def.rect.x;

        result.Constants = new List<Models.ConstantSet.Constant>();

        for (int rowIdx = dataRowStart; rowIdx < dataRowEnd; rowIdx++)
        {
            var row = def.rawSheet.Rows[rowIdx];

            var nameCol = row[dataColStart + 0];
            var typeCol = row[dataColStart + 1];
            var detailTypeCol = row[dataColStart + 2];
            var valueCol = row[dataColStart + 3];
            var descCol = row[dataColStart + 4];

            // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
            string rawName = nameCol.Value;
            string name = rawName.ToPascalCase();

            // Skip if marked with comments.
            if (_context.IsIgnorantName(name))
                continue;

            // Ensure identifier
            _context.RequiresIdentifier(name, nameCol.Location);

            // Whether the name collides with a keyword is asked per language rather than
            // here, because the answer differs per language: LanguageProfile carries each
            // one's reserved words and how it gets out of their way. The reserved-words
            // fixture compiles a table whose columns are named `class`, `delete` and
            // `operator` in every target.
            // Check if the label is already defined.
            if (result.ContainsConstant(name))
            {
                throw new TabbitException(nameCol.Location,
                    $"Constant '{name}' is already defined in constant-set '{result.Name}'.");
            }

            string typeName = typeCol.Value.ToLowerInvariant(); // normalize

            _context.RequiresValidTypeName(typeName, typeCol.Location);

            // A constant is one value written in one cell. There is no row for it to be
            // absent from, so `?` has nothing to mean here - and accepting it silently
            // would read as a permission that was never granted.
            CookingContext.SplitOptionalMarker(typeName, out bool constantRequired);
            if (!constantRequired)
            {
                throw new TabbitException(typeCol.Location,
                    $"type `{typeName}`: a constant cannot be optional. A constant is a single value, " +
                    "so drop the `?`.");
            }

            Models.Enum? enumm = null;
            if (typeName == "enum")
            {
                if (detailTypeCol.Value == "")
                    throw new TabbitException(detailTypeCol.Location, $"In case of enum type, enum name must be specified in detail-type.");

                typeName = detailTypeCol.Value;

                enumm = Model.GetEnum(typeName, detailTypeCol.Location);
            }

            // Add a constant.
            // The type is worked out first so the value can be parsed in the initializer
            // rather than assigned after it. An enum names its type in the detail cell, so
            // a diagnostic about it should point there.
            var type = _context.ParseValueType(
                typeName, enumm is not null ? detailTypeCol.Location : typeCol.Location);

            result.Constants.Add(new Models.ConstantSet.Constant
            {
                Location = nameCol.Location,
                RawName = rawName,
                Name = name,
                TypeName = typeName,
                Type = type,
                Enum = enumm!,
                Comment = descCol.Value,
                ValueString = valueCol.Value,
                Value = _context.ParseValue(
                    type, enumm, valueCol.Value, valueCol.Location,
                    def.rawSheet.Layout?.ArrayDelimiter),
            });
        }

        return result;
    }

    private Models.Table ParseTable(EntityDefinition def)
    {
        Log.Information($"Parsing table `{def.name}`. ({def.location})");

        var result = new Models.Table
        {
            Location = def.location,
            TargetSide = def.targetSide,
            RawName = def.rawName,
            Name = def.name,
            Comment = def.comment ?? "",

            // This is the layout that has the numbering convention, so it is the one that can
            // honour the setting - but only when the entry asked, because whether `Text1` and
            // `Text2` are one array or two fields is the author's intent and a name cannot
            // carry it.
            FoldSerialFields = (def.rawSheet.Layout ?? SheetLayout.Default).FoldSerialFields,

            // Also the author's call, and for the same reason: a shorter array is a different
            // API and nothing in the sheet says whether an empty tail was meant as elements.
            TrimTrailingArrayElements =
                (def.rawSheet.Layout ?? SheetLayout.Default).TrimTrailingArrayElements,
            AllowArrayGaps = (def.rawSheet.Layout ?? SheetLayout.Default).AllowArrayGaps,
        };

        var dataColumnOffsets = ParseTableFields(result, def);

        // Grouped before the cells are read, because grouping is what gives every element of
        // an array the first one's answer about being optional - and reading a cell asks that
        // question. Left to happen later, the parse would take each column's own answer and
        // the wire would take the first's.
        _ = result.SerialFields;

        ParseTableData(result, def, dataColumnOffsets);

        _context.AssignTags(result);

        return result;
    }

    /// <summary>
    /// Separates one side of a nested name from the element number on it: `Slot1` is
    /// `Slot`, element 1, and `Pos` is `Pos` with no number and therefore one element.
    /// </summary>
    /// <remarks>
    /// The same digit rule the serial-field folding uses - exactly one run of them, or
    /// none - because the two notations mean the same thing by a number and disagreeing
    /// about it would be the kind of difference nobody would look for. `Slot1A2` is
    /// refused rather than guessed at.
    ///
    /// Run on either side of the separator: the number is on the group for an array of
    /// records and on the member for a record whose members are arrays, and the digit rule
    /// is the same question in both places.
    /// </remarks>
    private (string levelName, int? index) SplitGroupOrdinal(string groupPart, string rawFieldName, Location location)
    {
        string pascal = groupPart.ToPascalCase();
        string digits = Helper.ExtractNumber(pascal);

        // No number means the level does not repeat. Null rather than zero, because "element
        // zero of one" and "not an array at all" are different shapes and the folding has to
        // be able to tell them apart.
        if (digits.Length == 0)
            return (pascal, null);

        int runs = 0;
        bool inRun = false;
        for (int i = 0; i < pascal.Length; i++)
        {
            if (char.IsDigit(pascal[i]))
            {
                if (!inRun)
                    runs++;
                inRun = true;
            }
            else
            {
                inRun = false;
            }
        }

        if (runs > 1)
        {
            throw new TabbitException(location,
                $"Field name `{rawFieldName}` has more than one run of digits in `{groupPart}`, "
                + $"so which one numbers the elements is ambiguous. Use a single number, as in "
                + $"`Slot1{NestedName.MemberSeparator}Id` or `Pos{NestedName.MemberSeparator}X1`.");
        }

        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ordinal))
        {
            throw new TabbitException(location,
                $"Field name `{rawFieldName}` has element number `{digits}`, which is not an integer.");
        }

        return (Helper.StripNumber(pascal), ordinal);
    }

    private List<int> ParseTableFields(Models.Table table, EntityDefinition def)
    {
        var dataColumnOffsets = new List<int>();

        var fieldNameRow = def.rawSheet.Rows[def.rect.y + 0];
        var fieldCommentRow = def.rawSheet.Rows[def.rect.y + 1];
        var fieldTypeRow = def.rawSheet.Rows[def.rect.y + 2];
        var fieldDetailTypeRow = def.rawSheet.Rows[def.rect.y + 3];
        var fieldTargetSideRow = def.rawSheet.Rows[def.rect.y + 4];

        // Field declarations first; the data pass below needs the types.
        for (int colIdx = def.rect.x; colIdx < def.rect.x + def.rect.width; colIdx++)
        {
            var fieldCommentCell = fieldCommentRow[colIdx];
            var fieldNameCell = fieldNameRow[colIdx];
            var fieldTypeCell = fieldTypeRow[colIdx];
            var fieldDetailTypeCell = fieldDetailTypeRow[colIdx];
            var fieldTargetSideCell = fieldTargetSideRow[colIdx];

            // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
            var rawFieldName = fieldNameCell.Value;

            // The wire tag comes off first: `Price@3` is the name `Price` with tag 3.
            // Before Pascal-casing, which would not survive the `@`.
            (rawFieldName, int? wireTag) = ParseWireTag(rawFieldName, fieldNameCell.Location);

            // Pascal-casing before the serial-field rules see the name is fine: the
            // rules look at where the digits are, and casing moves neither the digits
            // nor their order. `text_en_1` becomes `TextEn1`, which still reads as
            // stem `TextEn` and number 1.
            var fieldName = rawFieldName.ToPascalCase();

            if (_context.IsIgnorantName(fieldName))
            {
                // The primary index is what every row is addressed by, so it cannot be
                // the column somebody commented out.
                if (colIdx == def.rect.x)
                    throw new TabbitException(fieldNameCell.Location, $"The primary index field cannot be omitted.");

                // A tagged tombstone: the column is gone from the model, but its tag
                // stays reserved so it can never identify different data.
                if (wireTag is not null)
                    table.ReservedTags.Add(wireTag.Value);

                continue;
            }

            dataColumnOffsets.Add(colIdx);

            var field = new Field
            {
                OwnerTable = table,
                NameLocation = fieldNameCell.Location,
                TypeLocation = fieldTypeCell.Location,
                DetailTypeLocation = fieldDetailTypeCell.Location,
                TargetSideLocation = fieldTargetSideCell.Location,
                TargetSide = _context.ParseTargetSide(fieldTargetSideCell.Value.ToLowerInvariant(), fieldTargetSideCell.Location),
                Index = table.Fields.Count,
                Comment = fieldCommentCell.Value
            };

            // `Group.Member` folds several columns into a record. Split off the raw name
            // rather than the Pascal-cased one, so the separator cannot be produced or
            // swallowed by the case rules, and each part is normalized on its own.
            //
            // After the commented-out check above, so a column somebody parked with `#`
            // is never held to the notation's rules.
            if (!NestedName.TrySplit(rawFieldName, out var nameParts, out string? nestingProblem))
                throw new TabbitException(fieldNameCell.Location, $"Field name `{rawFieldName}` {nestingProblem}");

            if (nameParts.Count > 1)
            {
                string groupPart = nameParts[0];

                // The primary index is what every row is addressed by and every reference
                // points at, so it has to be one value.
                if (colIdx == def.rect.x)
                {
                    throw new TabbitException(fieldNameCell.Location,
                        $"The primary index field cannot be part of a record group, but `{rawFieldName}` is.");
                }

                if (groupPart.StartsWith("*"))
                {
                    throw new TabbitException(fieldNameCell.Location,
                        $"Field name `{rawFieldName}` marks a record member as a secondary index. "
                        + $"An index must be a single value, so `*` cannot be used on a record group.");
                }

                // Each level on its own: a number on it says the level repeats, and a level
                // that is nothing but digits is numbered rather than named. Which level
                // carries the number is what tells the shapes apart - `Slot1.Id` numbers the
                // record so the group is an array of them, `Pos.X1` numbers the member so
                // the group is one record whose members are arrays, and `Grid1.2` numbers
                // both and names neither. All of them reach the same wire; only the assembly
                // differs. See spec/nested-multi-level.md.
                //
                // Nothing here counts the levels. `Star1.Position.X` is read by the same
                // rule as `Slot1.Id`, one step further in.
                var namePath = new List<Models.FieldPathStep>();

                foreach (string part in nameParts)
                {
                    (string levelName, int? index) =
                        SplitGroupOrdinal(part, rawFieldName, fieldNameCell.Location);

                    namePath.Add(new Models.FieldPathStep { Name = levelName, Index = index });
                }

                field.NamePath = namePath;

                // The generated identifier stays one name, so duplicate detection, lookup
                // and every language's spelling rules keep working untouched. `Slot1.Id`
                // is `Slot1Id` here and reaches a consumer as `Slot[0].Id`.
                fieldName = string.Concat(nameParts.Select(part => part.ToPascalCase()));
            }

            // A single leading `*` marks a secondary index.
            bool indexing = false;
            if (fieldName.StartsWith("*"))
            {
                fieldName = fieldName[1..].Trim();
                indexing = true;

                // Exactly one, not "one or more". Stripping every `*` would quietly
                // accept `**Name` as a typo for `*Name`, and leaving the extras in
                // place produced `` `*Name` is not a valid identifier `` - a message
                // that names the symptom rather than the mistake.
                if (fieldName.StartsWith("*"))
                {
                    throw new TabbitException(fieldNameCell.Location,
                        $"Field name `{rawFieldName}` has more than one leading `*`. " +
                        $"Use a single `*` to mark a secondary index field.");
                }
            }
            field.Indexing = (colIdx == def.rect.x) || indexing;

            // Ensure identifier
            _context.RequiresIdentifier(fieldName, fieldNameCell.Location);

            // Check duplicated name
            if (table.ContainsField(fieldName))
                throw new TabbitException(fieldNameCell.Location, $"Field name `{fieldName}` is a duplicated.");

            field.RawName = rawFieldName;
            field.Name = fieldName;
            field.Tag = wireTag;

            // Case is folded at the end rather than here, because one part of a type name
            // survives into the model as the author wrote it: the group of `text(Achievement)`
            // names an output file. Every marker below is peeled off the original spelling and
            // what is left - the type's own name - is lowered where it always was.
            var fieldType = fieldTypeCell.Value.Trim();
            _context.RequiresValidTypeName(fieldType.ToLowerInvariant(), fieldTypeCell.Location);

            // `int[]?` is an array a row may not have, and `int?[]` is an array holding
            // elements that may be absent. Both markers come off before anything reads the
            // name, and each is answered by the side of the brackets it was written on.
            // spec/nullable-array-elements.md.
            fieldType = CookingContext.SplitOptionalMarkers(
                fieldType, out bool fieldRequired, out bool elementsRequired);

            field.IsRequired = fieldRequired;
            field.ElementsRequired = elementsRequired;

            if (!elementsRequired && !fieldType.EndsWith("[]"))
            {
                throw new TabbitException(fieldTypeCell.Location,
                    $"type `{fieldTypeCell.Value.Trim()}`: the `?` inside the brackets says an "
                    + "element may have no value, and this column is not an array. Write the "
                    + "`?` at the end to say the value itself may be absent.");
            }

            // `int[]`, `string[]`, `enum[]`: one cell holding a delimited list.
            // The bracket suffix is peeled off here so the element name goes
            // through exactly the same handling as a scalar, and put back when the
            // type is finally resolved.
            bool isArrayField = fieldType.EndsWith("[]");
            if (isArrayField)
                fieldType = fieldType.Substring(0, fieldType.Length - 2).Trim();

            // `text(Achievement)`: a string with a role, and the set its values are gathered
            // into. What comes back is the ordinary type to resolve - the role travels beside
            // it on the field - so everything below this line reads `string`.
            fieldType = _context.SplitStringRole(
                fieldType, fieldTypeCell.Location,
                out var role, out string? roleGroup, out string? roleNamespace);

            field.Role = role;

            if (role != StringRole.None)
            {
                ReadRoleGroup(
                    role, roleGroup, roleNamespace, fieldTypeCell, fieldDetailTypeCell,
                    out string? group, out string? space);

                field.RoleGroup = group;
                field.RoleNamespace = space;
            }

            fieldType = fieldType.ToLowerInvariant();

            if (fieldType == "enum")
            {
                if (fieldDetailTypeCell.Value == "")
                    throw new TabbitException(fieldDetailTypeCell.Location, $"In case of enum type, enum name must be specified in detail-type.");

                fieldType = fieldDetailTypeCell.Value;
            }

            if (isArrayField && fieldType == "foreign")
            {
                // Deliberately unsupported rather than half-supported. An array of
                // references means resolving a variable number of targets per row,
                // which the generated readers have no shape for; letting it parse
                // would produce code that silently never resolves.
                throw new TabbitException(fieldTypeCell.Location,
                    "`foreign[]` is not supported. Use a serial field (Ref1, Ref2, ...) for a fixed " +
                    "number of references, or a plain `foreign` for a single one.");
            }

            if (fieldType == "foreign")
            {
                // Split before casing. A bar is not a word separator to the casing, so
                // `weapon|armour` cased as one string leaves the second name as written -
                // which is also why the dotted form below cases each half rather than the
                // whole cell. spec/multi-target-accessors.md.
                var writtenTargets = fieldDetailTypeCell.Value
                    .Split('|')
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0)
                    .ToList();

                if (writtenTargets.Count == 0)
                    throw new TabbitException(fieldDetailTypeCell.Location, $"In case of foreign type, `RefTable[.RefFieldName]` must be specified in detail-type.");

                // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                string detailTypeName = writtenTargets[0].ToPascalCase();

                field.TypeName = "$Unresolved$";

                // Whichever form the reference takes, the cell holds the referenced table's
                // index - and what type that is cannot be known here. The target may not
                // even have been read yet, and its key may be a `string` or a `uuid` as
                // readily as an `int`.
                //
                // So the data pass keeps the cell as written and the conversion waits for
                // resolution, which is the only point at which the answer exists.
                // ModelCooker.ConvertReferenceCells does it. Leaving this Unresolved instead
                // made the dotted form die in ParseValue before resolution ever ran.
                // spec/reference-key-types.md.
                field.Type = Models.ValueType.String;

                // Several tables, so the value is a row of one of them and which one is a
                // question about the value rather than about the column. The model has
                // carried that shape for a while and only a project layout's constraint row
                // could declare it; this is the notation for it.
                // spec/multi-target-accessors.md section 4.
                if (writtenTargets.Count > 1)
                {
                    // The dotted form names a value inside the target, and "one of these
                    // tables, at this field of it" is a second shape nothing has asked for.
                    // Refused by name rather than resolved to whichever half looks right.
                    string? dotted = writtenTargets.Find(part => part.Contains('.'));
                    if (dotted is not null)
                    {
                        throw new TabbitException(fieldDetailTypeCell.Location,
                            $"`{fieldDetailTypeCell.Value}` names several tables and also a field of "
                            + $"one of them (`{dotted}`). A reference to one of several tables names "
                            + $"the tables alone - drop the field name, or point at them from "
                            + $"separate columns.");
                    }

                    // In the order written, and a name written twice is kept once: the order
                    // is what the generated per-target accessors are laid out in, and "this
                    // table or this table" said twice is the same declaration.
                    var names = new List<string>();
                    foreach (string part in writtenTargets)
                    {
                        string name = part.ToPascalCase();
                        if (!names.Contains(name))
                            names.Add(name);
                    }

                    field.RefTableNames = names;
                    field.RefFieldName = null;

                    // `RefTableName` is left empty on purpose when there are several, because
                    // `IsRef` reads it and has to keep meaning "resolves to exactly one
                    // record". A bar with one name behind it is that one record.
                    // spec/multi-target-references.md.
                    if (names.Count == 1)
                        field.RefTableName = names[0];
                }
                else
                {
                    int dot = detailTypeName.IndexOf(".");
                    if (dot < 0)
                    {
                        // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                        field.RefTableName = detailTypeName.ToPascalCase();
                        field.RefTableNames = new List<string> { field.RefTableName };
                        field.RefFieldName = null;
                    }
                    else
                    {
                        // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                        field.RefTableName = detailTypeName.Substring(0, dot).ToPascalCase();
                        field.RefTableNames = new List<string> { field.RefTableName };
                        field.RefFieldName = detailTypeName.Substring(dot + 1).ToPascalCase();

                        // `Table.Index` names the row's own key, which is the row - so it is
                        // cleared and the reference resolves to the record rather than to the
                        // integer, which is what the writer meant either way.
                        if (field.RefFieldName.ToLowerInvariant() == "index")
                            field.RefFieldName = "";
                    }
                }
            }
            else
            {
                // TypeName stays the element's name - for an enum array that is the
                // enum declaration to look up, and the generators append the
                // brackets themselves.
                field.TypeName = fieldType;

                var elementType = _context.ParseValueType(fieldType, fieldTypeCell.Location);

                if (isArrayField)
                {
                    var arrayType = Models.ValueTypes.ArrayOf(elementType);
                    if (arrayType == Models.ValueType.None)
                    {
                        throw new TabbitException(fieldTypeCell.Location,
                            $"type `{fieldType}` cannot be used as an array element.");
                    }

                    field.Type = arrayType;
                }
                else
                {
                    field.Type = elementType;
                }
            }

            table.Fields.Add(field);
        }

        _context.CheckPrimaryIndexValidity(table.Fields[0]);

        return dataColumnOffsets;
    }

    /// <summary>
    /// What a role's brackets would have said, taken from whichever of the two cells that can
    /// say it was used - the group of a `text` column, the kind of an `asset` one.
    /// </summary>
    /// <remarks>
    /// The detail-type row is accepted as well as `text(Group)` because that row is already
    /// where this layout writes the rest of a type - an enum's declaration, a reference's
    /// target - and someone who has learned it will reach for it here too. Refusing it would
    /// be refusing the notation's own convention.
    ///
    /// Both cells filled and disagreeing is an error rather than a precedence rule. There is
    /// no reading of a sheet under which one of two different names was meant, and a silent
    /// winner puts the values in a file, or looks for them in a folder, that the other cell
    /// says they are not in.
    /// </remarks>
    private void ReadRoleGroup(
        StringRole role, string? fromType, string? namespaceFromType,
        RawCell typeCell, RawCell detailTypeCell,
        out string? group, out string? space)
    {
        string written = detailTypeCell.Value.Trim();

        if (fromType is not null && written.Length > 0)
        {
            throw new TabbitException(typeCell.Location,
                $"Column type `{typeCell.Value.Trim()}` already names `{fromType}`, and the "
                + $"detail-type cell beside it says `{written}`. Write it in one of the two.");
        }

        if (fromType is not null)
        {
            group = fromType;
            space = namespaceFromType;
            return;
        }

        if (written.Length == 0)
        {
            group = null;
            space = null;
            return;
        }

        // The same `Group,Namespace` the brackets take, read by the same code - so the two
        // places a sheet may write it cannot come to mean different things.
        CookingContext.SplitGroupAndNamespace(written, out group, out space);

        _context.RequiresRoleGroup(written, role, group, space, detailTypeCell.Location);
    }

    private void ParseTableData(Models.Table table, EntityDefinition def, List<int> dataColumnOffsets)
    {
        int dataRowStart = def.rect.y + 5; // skip header rows(field name, comment, type, detail-type, target-side)
        int dataRowEnd = def.rect.y + def.rect.height;

        for (int rowIdx = dataRowStart; rowIdx < dataRowEnd; rowIdx++)
        {
            var row = new List<Cell>();

            for (int i = 0; i < table.Fields.Count; i++)
            {
                var field = table.Fields[i];

                var rawCell = def.rawSheet.Rows[rowIdx][dataColumnOffsets[i]];

                // What the cell says, decided in one place for every layout: `-` is no value,
                // `\-` is the one character `-`, and a blank is whatever the column's type
                // reads a blank as. spec/blank-and-null-cells.md.
                //
                // A reference's blank is handed on rather than refused here, because a
                // `foreign` cell holds the target's index and a blank one reached `int.Parse`
                // and died as "cannot parse `` as a value of type `Int32`" - true, and useless.
                // It names neither the reference nor a way out of it. Validation answers it
                // against what the column declared, with a message that says how to fix it.
                var reading = _context.ReadCell(
                    field.Type, field.EnumOrNull, rawCell.Value, rawCell.Location,
                    def.rawSheet.Layout?.ArrayDelimiter,
                    required: field.IsRequired,
                    onBlankCell: def.rawSheet.Layout?.OnBlankCell ?? BlankCellPolicy.Error,
                    isReference: field.IsRef,
                    column: $"{table.Name}.{field.Name}",
                    elementsRequired: field.ElementsRequired,
                    formulaError: rawCell.FormulaError,
                    onFormulaError: def.rawSheet.Layout?.OnFormulaError ?? FormulaErrorPolicy.Error);

                // Index uniqueness is checked in ValidateModel rather than here.
                // Doing it inline compared each new value against every row read
                // so far - quadratic - and threw on the first duplicate, so a sheet
                // with several had to be fixed one error per run.

                row.Add(new Cell
                {
                    RawCell = rawCell,
                    Value = reading.Value,

                    // Only `-` says a row has no value. A blank cell holds whatever its type
                    // reads a blank as - an empty string, false, an array of no elements - and
                    // that is a value the author asked for.
                    HasValue = reading.HasValue,

                    // And which of an array's elements said it, where the column allows one
                    // to. Null everywhere else, which is every column that existed before
                    // spec/nullable-array-elements.md.
                    ElementHasValue = reading.ElementHasValue,
                });
            }

            table.Data.Add(row);
        }
    }

    private List<EntityDefinition> ScanEntityDefinitions(IReadOnlyList<RawSheet> sheets)
    {
        var entityDefinitions = new List<EntityDefinition>();

        foreach (var rawSheet in sheets)
        {
            for (int rowIndex = 0; rowIndex < rawSheet.Rows.Count; rowIndex++)
            {
                var rawRow = rawSheet.Rows[rowIndex];

                for (int colIndex = 0; colIndex < rawRow.Count; colIndex++)
                {
                    var rawCell = rawRow[colIndex];

                    if (ParseEntityMarker(rawCell.Value, out string? entityType, out string? rawEntityName, out string? entityName, out string? entityTargetSide, out Size entityMinSize))
                    {
                        // Ensure valid identifier
                        _context.RequiresIdentifier(entityName, rawCell.Location);

                        // Check duplicated name
                        if (entityDefinitions.Where(x => x.name == entityName).Count() > 0)
                            throw new TabbitException(rawCell.Location, $"Entity {entityType}'s name `{entityName}` is a duplicated.");

                        var commentRow = rawSheet.Rows[rowIndex + 1];

                        var entity = new EntityDefinition
                        {
                            rawSheet = rawSheet,
                            location = rawCell.Location,
                            type = entityType!,
                            rawName = rawEntityName!,
                            name = entityName!,
                            comment = commentRow[colIndex].Value,
                            targetSide = _context.ParseTargetSide(entityTargetSide!, rawCell.Location),
                            rect = ParseDefinitionRect(rawSheet, rawCell.Location, entityType!, entityName!, colIndex, rowIndex + 2, entityMinSize) // ignore marker and comment rows
                        };
                        entityDefinitions.Add(entity);
                    }
                }
            }
        }

        return entityDefinitions;
    }

    private DefinitionRect ParseDefinitionRect(RawSheet rawSheet, Location location, string entityType, string entityName, int x, int y, Size minSize)
    {
        // Checks bounds.
        //
        // An empty rectangle used to come back here, and an entity with one silently
        // disappeared: the conversion succeeded, the sheet's marker was still there, and
        // the table was simply not in the output. That is the same shape as every other
        // defect this codebase has had to hunt - not a failure, a different answer - and
        // the minimum-size check immediately below always threw for its own case, so the
        // two disagreed about what an unusable rectangle deserves.
        if (y < 0 || y >= rawSheet.Rows.Count || x < 0 || x >= rawSheet.ColumnCount)
        {
            throw new TabbitException(location,
                $"Entity `{entityType}:{entityName}` starts outside the sheet: its marker points at " +
                $"column {x + 1}, row {y + 1}, and the sheet holds {rawSheet.ColumnCount} column(s) " +
                $"and {rawSheet.Rows.Count} row(s). A marker in the last cell with nothing after it " +
                $"does this.");
        }

        // Check the minimum required size.
        int availWidth = rawSheet.ColumnCount - x;
        int availHeight = rawSheet.Rows.Count - y;
        if (availWidth < minSize.width || availHeight < minSize.height)
        {
            throw new TabbitException(location,
                    $"Entity `{entityType}:{entityName}` must have cells of at least {minSize.width}x{minSize.height} size. " +
                    $"The size of the currently accessible cell is {availWidth}x{availHeight}.");
        }

        // Greedy manner scanning.

        int maxWidth = 0;
        int height = 0;

        for (int rowIdx = y; rowIdx < rawSheet.Rows.Count; rowIdx++)
        {
            var rawCell = rawSheet.Rows[rowIdx][x];

            if (height >= minSize.height) // Since the minimum size has already been met, it stops when an empty cell or entity-marker is encountered.
            {
                if (rawCell.Value == "" || IsEntityMarkerPattern(rawCell.Value))
                    break;
            }
            else
            {
                // If the minimum size has not yet been met and an entity-marker comes, the rule is violated.
                if (IsEntityMarkerPattern(rawCell.Value))
                    throw new TabbitException(rawCell.Location, $"Unexpected entity-marker `{rawCell.Value}`");
            }

            height++;
        }

        for (int rowIdx = y; rowIdx < y + height; rowIdx++)
        {
            var row = rawSheet.Rows[rowIdx];

            int width = 0;
            for (int colIdx = x; colIdx < row.Count; colIdx++)
            {
                var rawCell = row[colIdx];

                if (width >= minSize.width) // Since the minimum size has already been met, it stops when an empty cell or entity-marker is encountered.
                {
                    if (rawCell.Value == "" || IsEntityMarkerPattern(rawCell.Value))
                        break;
                }
                else
                {
                    // If the minimum size has not yet been met and an entity-marker comes, the rule is violated.
                    if (IsEntityMarkerPattern(rawCell.Value))
                        throw new TabbitException(rawCell.Location, $"Unexpected entity-marker `{rawCell.Value}`");
                }

                width++;
            }

            if (width > maxWidth)
                maxWidth = width;
        }

        return new DefinitionRect { x = x, y = y, width = maxWidth, height = height };
    }

    private bool IsEntityMarkerPattern(string marker)
    {
        return ParseEntityMarker(marker, out _, out _, out _, out _, out _);
    }

    private bool ParseEntityMarker(string marker, out string? outType, out string? outRawName, out string? outName, out string? outTargetSide, out Size outMinSize)
    {
        outType = "";
        outRawName = "";
        outName = "";
        outTargetSide = "";
        outMinSize = new Size { width = 0, height = 0 };

        if (marker.Length == 0)
            return false;

        if (!marker.StartsWith("~~"))
            return false;
        marker = marker.Substring(2).Trim();

        if (!marker.EndsWith("~~"))
            return false;
        marker = marker.Substring(0, marker.Length - 2).Trim();

        if (!marker.Contains(":"))
            return false;

        var tokens = marker.Split(":");
        for (int i = 0; i < tokens.Length; i++)
            tokens[i] = tokens[i].Trim();

        // Type
        outType = tokens[0].ToLowerInvariant();

        // Check if it is a recognizable entity type.
        if (!_possibleEntities.TryGetValue(outType, out Size? found))
            return false;

        outMinSize = found;

        // Name
        outRawName = tokens[1];
        outName = outRawName.ToPascalCase();

        // TargetSide
        if (tokens.Length > 2)
            outTargetSide = tokens[2].ToLowerInvariant();

        return true;
    }

    /// <summary>
    /// Splits a field name's `@N` wire-tag suffix off, when it has one.
    /// </summary>
    /// <remarks>
    /// The tag identifies the column in a binary file instead of its position, which is
    /// what lets a reader built from one generation of the model read a file written from
    /// another. `Price@3` is the field `Price` with tag 3; a name with no `@` has no
    /// explicit tag and AssignTags decides what that means for the table.
    /// </remarks>
    private static (string name, int? tag) ParseWireTag(string rawName, Location location)
    {
        int at = rawName.LastIndexOf('@');

        if (at < 0)
            return (rawName, null);

        string digits = rawName.Substring(at + 1).Trim();

        if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int tag))
        {
            throw new TabbitException(location,
                $"Field name `{rawName}` has an `@` where a wire tag goes, but `{digits}` is not a " +
                "positive integer. A tag is written as `Name@3`.");
        }

        if (tag < 1)
        {
            throw new TabbitException(location,
                $"Field `{rawName}` declares wire tag {tag}, but a tag starts at 1.");
        }

        return (rawName.Substring(0, at).Trim(), tag);
    }
}
