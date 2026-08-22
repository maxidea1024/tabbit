using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Serilog;
using Tabbit.Extensions;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Messages;
using Tabbit.Recipe;

namespace Tabbit.Cooking;

/// <summary>
/// The model being built, and everything a layout parser needs that is not about layout.
/// </summary>
/// <remarks>
/// Reading a cell as an `int`, recognizing an enum name, deciding what a boolean spelling
/// means, numbering wire tags - none of that depends on where in a sheet the column was
/// found. It lives here so that a second layout is a second way of locating rows and
/// columns, and not a second answer to what a value means. Two parsers disagreeing about
/// whether `1,000` is a thousand is precisely the failure this tool exists to prevent.
/// </remarks>
public sealed class CookingContext
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Cooking;

    /// <summary>Number formats accepted in an integer cell.</summary>
    private const NumberStyles IntegerStyles = NumberStyles.Integer | NumberStyles.AllowThousands;

    /// <summary>Number formats accepted in a float or double cell.</summary>
    private const NumberStyles DecimalStyles = NumberStyles.Float | NumberStyles.AllowThousands;

    public CookingContext(Model model, RecipeModel recipe, Diagnostics diagnostics)
    {
        Model = model;
        Diagnostics = diagnostics;
        ArrayDelimiter = ResolveArrayDelimiter(recipe);
        TimeZone = Helpers.TimeZones.OfRecipe(recipe.TimeZone);
        AutoInsertEnumNoneLabel = recipe.AutoInsertEnumNoneLabel;
    }

    /// <summary>
    /// Where a parser puts a table it could not read, so the rest of them still get read.
    /// </summary>
    /// <remarks>
    /// A refusal that stops the run answers one question and hides every other - the corpus
    /// this exists for has six hundred tables, and the author of a sheet wants the list of
    /// what is wrong rather than whichever one the reader met first.
    /// </remarks>
    public Diagnostics Diagnostics { get; }

    /// <summary>The model every parser adds to.</summary>
    public Model Model { get; }

    /// <summary>
    /// Separator for array cells, taken from the recipe. A source entry may override it.
    /// </summary>
    public char ArrayDelimiter { get; }

    /// <summary>
    /// The time zone a `datetime` cell's wall clock is read as being in, taken from the
    /// recipe. Null reads one as already being in UTC. A source entry may override it.
    /// </summary>
    /// <remarks>
    /// What leaves this tool is UTC either way - the zone decides what a sheet's `10:30`
    /// means, not what is stored. spec/datetime-timezone.md.
    /// </remarks>
    public TimeZoneInfo? TimeZone { get; }

    /// <summary>Whether to give an enum a zero label it did not declare.</summary>
    public bool AutoInsertEnumNoneLabel { get; }

    /// <summary>
    /// Reads the array delimiter from the recipe, rejecting anything that is not exactly
    /// one character.
    /// </summary>
    private static char ResolveArrayDelimiter(RecipeModel recipeModel)
    {
        string delimiter = recipeModel.ArrayDelimiter;

        if (string.IsNullOrEmpty(delimiter) || delimiter.Length != 1)
        {
            throw new TabbitException(null,
                Message.Of(RecipeMessages.ArrayDelimiterNotOneCharacter,
                    ("Delimiter", delimiter)));
        }

        return delimiter[0];
    }


    #region Names

    /// <summary>Whether a name marks its row or column as commented out.</summary>
    public bool IsIgnorantName(string name)
    {
        return name.StartsWith("#") || name.StartsWith("//");
    }

    public void RequiresIdentifier(string? name, Location? location)
    {
        if (!name!.IsValidIdentifier())
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.InvalidIdentifier, ("Name", name)));
        }
    }

    public void RequiresValidTypeName(string typeName, Location location)
    {
        if (IsValidTypeName(typeName))
            return;

        // A `?` somewhere the two spellings do not allow. Named as the two, because a name
        // reported only as unrecognized sends the author looking for a type that does not
        // exist rather than at the character they put in the wrong place.
        if ((typeName ?? "").Contains('?'))
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.UnrecognizedTypeQuestionMark, ("Type", typeName)));
        }

        throw new TabbitException(location,
            Message.Of(CookingMessages.UnrecognizedType, ("Type", typeName)));
    }

    /// <summary>
    /// Splits the optional marker off a type name: `int?` is `int`, not required.
    /// </summary>
    /// <remarks>
    /// On the type because that is where the question lives - "is a number required here" is
    /// about the number - and because it reads as what the generated languages mean by `?`.
    ///
    /// Required is the default, and always was: a blank cell in a number column has always
    /// been an error. The marker is how a sheet says that a blank is expected there.
    /// </remarks>
    public static string SplitOptionalMarker(string typeName, out bool required)
        => SplitOptionalMarkers(typeName, out required, out _);

    /// <summary>
    /// Splits both optional markers off a type name: `int?[]?` is `int[]`, with the array and
    /// its elements each answering for themselves.
    /// </summary>
    /// <remarks>
    /// Read back to front, which is how the two are told apart. The marker after the brackets
    /// is the array's - `int[]?` is an array that a row may not have - and the one before them
    /// is an element's - `int?[]` is an array a row always has, holding elements that may be
    /// absent. C# reads the same two spellings the same way, which is the reason the marker
    /// sits on the type at all.
    ///
    /// Returns the name with both markers removed, so everything downstream reads the type it
    /// always read. spec/nullable-array-elements.md.
    /// </remarks>
    public static string SplitOptionalMarkers(
        string typeName, out bool required, out bool elementsRequired)
    {
        string text = (typeName ?? "").Trim();

        required = true;
        elementsRequired = true;

        if (text.EndsWith("?", StringComparison.Ordinal))
        {
            required = false;
            text = text.Substring(0, text.Length - 1).Trim();
        }

        if (!text.EndsWith("[]", StringComparison.Ordinal))
            return text;

        string element = text.Substring(0, text.Length - 2).Trim();

        if (element.EndsWith("?", StringComparison.Ordinal))
        {
            elementsRequired = false;
            element = element.Substring(0, element.Length - 1).Trim();
        }

        return element + "[]";
    }

    /// <summary>
    /// Splits the group off a type name: `text(Achievement)` is `text` gathered into
    /// `Achievement`, and `text(Achievement,Quests)` gives that group a namespace too.
    /// </summary>
    /// <remarks>
    /// Parentheses rather than another separator because the layouts that pack everything
    /// about a column into one cell already spend `:` on the target side, and `text:c` has to
    /// keep meaning what it always meant. A bracketed group is unambiguous beside it however
    /// many qualifiers follow, and it reads the same in a layout that has room to spare.
    ///
    /// The namespace is second and optional. It is here rather than only on the recipe entry
    /// because which namespace a string belongs to is a fact about the string - a pipeline
    /// that keys by namespace groups them differently from how the files are split, and a
    /// setting on the output cannot say that. A recipe that has one answer for the whole
    /// export still writes it there and leaves the sheets alone.
    ///
    /// <paramref name="group"/> is null when none was written, and empty when the cell says
    /// `text()` - a distinction the caller reports, since only it knows which cell to point at.
    /// <paramref name="space"/> is null unless a second part was written.
    ///
    /// Nothing is validated here beyond the shape. A group on a type that has no use for one
    /// is refused where the type is recognized, so the message can name the type.
    /// </remarks>
    public static string SplitRoleGroup(string typeName, out string? group, out string? space)
    {
        group = null;
        space = null;

        string text = (typeName ?? "").Trim();

        if (!text.EndsWith(")", StringComparison.Ordinal))
            return text;

        int open = text.IndexOf('(');

        // A closing bracket with nothing opening it. Left in the name so the type-name check
        // refuses it and quotes what was actually written.
        if (open < 0)
            return text;

        SplitGroupAndNamespace(
            text.Substring(open + 1, text.Length - open - 2), out group, out space);

        return text.Substring(0, open).Trim();
    }

    /// <summary>
    /// Reads `Group` or `Group,Namespace`, wherever a layout writes the pair.
    /// </summary>
    /// <remarks>
    /// Shared with the cell some layouts use instead of the brackets, so the two spellings
    /// cannot come to mean different things - which is what this class exists to prevent.
    ///
    /// More than one comma is left in the namespace rather than refused here. What a
    /// namespace may hold is not this method's question, and the caller checks it against the
    /// same identifier rule the group goes through.
    /// </remarks>
    public static void SplitGroupAndNamespace(string written, out string? group, out string? space)
    {
        string text = (written ?? "").Trim();

        int comma = text.IndexOf(',');

        if (comma < 0)
        {
            group = text;
            space = null;
            return;
        }

        group = text.Substring(0, comma).Trim();
        space = text.Substring(comma + 1).Trim();
    }

    /// <summary>
    /// The role a type name carries, and the name written in its brackets.
    /// </summary>
    /// <remarks>
    /// The one place that decides what `text(Group)` and `asset(Kind)` mean, so that two
    /// layouts reading the same notation cannot reach two answers - the reason this class
    /// exists.
    ///
    /// Returns the name with the role's spelling removed, so the caller goes on to resolve an
    /// ordinary type: both roles leave `string`, and the model carries the difference in
    /// <see cref="Field.Role"/> rather than in the type. That is what keeps a column changed
    /// from `string` to `text` out of every exported byte and out of the schema baseline.
    /// </remarks>
    public string SplitStringRole(
        string typeName, Location location, out StringRole role,
        out string? group, out string? space)
    {
        string bare = SplitRoleGroup(typeName, out group, out space);

        role = bare.ToLowerInvariant() switch
        {
            "text" => StringRole.Text,
            "asset" => StringRole.Asset,
            _ => StringRole.None,
        };

        if (role == StringRole.None)
        {
            if (group is not null)
            {
                throw new TabbitException(location,
                    Message.Of(CookingMessages.TypeTakesNoBrackets,
                        ("Type", typeName), ("Bare", bare)));
            }

            return bare;
        }

        RequiresRoleGroup(typeName, role, group, space, location);

        // Every role is a string, so what follows resolves an ordinary one. The role travels
        // beside the type instead of inside it.
        return "string";
    }

    /// <summary>
    /// Checks what a role's brackets held, wherever a layout read them from.
    /// </summary>
    public void RequiresRoleGroup(
        string written, StringRole role, string? group, string? space, Location? location)
    {
        // What the first name means, for the messages. The two roles put different things in
        // the same slot - which set to gather into, which kind of asset - and a message that
        // says "group" to somebody who wrote `asset()` is a message about someone else's typo.
        string what = role == StringRole.Asset ? "kind" : "group";
        string example = role == StringRole.Asset ? "asset(icon)" : "text(Achievement)";

        if (group is not null && group.Length == 0)
        {
            throw new TabbitException(location, Message.Of(CookingMessages.RoleGroupEmpty,
                ("Written", written), ("What", what), ("Example", example)));
        }

        if (space is not null && role != StringRole.Text)
        {
            throw new TabbitException(location, Message.Of(CookingMessages.RoleSpaceNotText,
                ("Written", written)));
        }

        if (space is not null && space.Length == 0)
        {
            throw new TabbitException(location, Message.Of(CookingMessages.RoleSpaceEmpty,
                ("Written", written)));
        }

        if (group is not null)
            RequiresIdentifier(group, location);

        // The same rule as the first name's, because it is the same kind of name: something a
        // pipeline downstream will address by. A namespace holding a quotation mark would end
        // the string it is written into.
        if (space is not null)
            RequiresIdentifier(space, location);
    }

    /// <summary>
    /// Whether a name is one of the types a sheet may declare.
    /// </summary>
    /// <remarks>
    /// The non-throwing half of <see cref="RequiresValidTypeName"/>, for the callers that
    /// are deciding rather than checking - a layout working out whether a sheet is a table
    /// at all cannot use an exception to find out.
    /// </remarks>
    public bool IsValidTypeName(string typeName)
    {
        if (typeName is null)
            return false;

        // `int?`: the optional marker is not part of the type's name, so it comes off before
        // the name is recognized. Callers that want to know about it use SplitOptionalMarker.
        typeName = SplitOptionalMarker(typeName, out _);

        // `int[]`, `string[]` and so on: one cell holding several delimited
        // values. Validity of the element name is the same question as for a
        // scalar, so strip the brackets and ask that.
        if (typeName.EndsWith("[]"))
            typeName = typeName.Substring(0, typeName.Length - 2).Trim();

        // `text(Achievement)`, `asset(icon)`: the bracketed name is not part of the type's
        // name either. Only the types that take one may have it, which is checked here rather
        // than left to the switch - `int(Foo)` should be refused as the whole thing it is,
        // not read as `int`.
        typeName = SplitRoleGroup(typeName, out string? group, out _);

        if (group is not null && typeName != "text" && typeName != "asset")
            return false;

        switch (typeName)
        {
            case "string":
            case "bool":
            case "int":
            case "bigint":
            case "float":
            case "double":
            case "datetime":
            case "timespan":
            case "uuid":

            // Up to 64 flags. A separate name rather than `bigint` because the notation it
            // accepts is narrower, and a type that does not say it holds a pattern has no
            // ground to refuse a sign. See spec/bitset.md.
            case "bitset":

            // Strings with a role. What separates these from `string` is what else is done
            // with the value - gathering it, checking that a file of that name exists - and
            // never what the value is. See StringRole.
            case "text":
            case "asset":

            // Also foreign, enum
            case "foreign":
            case "enum":
                return true;
        }

        return false;
    }

    public TargetSide ParseTargetSide(string value, Location location)
    {
        switch (value)
        {
            case "":
            case "cs": return TargetSide.Both;
            case "s": return TargetSide.ServerOnly;
            case "c": return TargetSide.ClientOnly;
        }

        throw new TabbitException(location,
            Message.Of(CookingMessages.IllegalTargetSide, ("Value", value)));
    }

    #endregion


    #region Types and values

    public Models.ValueType ParseValueType(string typeName, Location location)
    {
        // The optional marker says whether a blank cell is allowed, not what the values are,
        // so it is off before anything here looks at the name.
        typeName = SplitOptionalMarker(typeName, out _);

        if (typeName.EndsWith("[]"))
        {
            string elementName = typeName.Substring(0, typeName.Length - 2).Trim();
            var elementType = ParseValueType(elementName, location);

            var arrayType = Models.ValueTypes.ArrayOf(elementType);
            if (arrayType == Models.ValueType.None)
                throw new TabbitException(location,
                    Message.Of(CookingMessages.TypeNotArrayElement, ("Type", elementName)));

            return arrayType;
        }

        // `text(Achievement)`, `asset(icon)`: the bracketed name says what is done with the
        // values, not what they are. Peeled and dropped - the layout has already recorded it
        // on the field, and IsValidTypeName has already refused brackets on a type that takes
        // none.
        typeName = SplitRoleGroup(typeName, out _, out _);

        // Primitive types.
        switch (typeName)
        {
            case "string": return Models.ValueType.String;

            // Strings, and deliberately indistinguishable from one here. The difference a
            // role makes is in what is done with the value elsewhere, so it travels on the
            // field rather than in the type: StringRole says why.
            case "text":
            case "asset": return Models.ValueType.String;
            case "bool": return Models.ValueType.Bool;
            case "int": return Models.ValueType.Int32;
            case "bigint": return Models.ValueType.Int64;
            case "float": return Models.ValueType.Float;
            case "double": return Models.ValueType.Double;
            case "datetime": return Models.ValueType.DateTime;
            case "timespan": return Models.ValueType.TimeSpan;
            case "uuid": return Models.ValueType.Uuid;
            case "bitset": return Models.ValueType.Bitset;
        }

        // Also enum.
        if (Model.ContainsEnum(typeName))
            return Models.ValueType.Enum;

        throw new TabbitException(location,
            Message.Of(CookingMessages.UnsupportedType, ("Type", typeName)));
    }

    /// <summary>
    /// The empty value of a type, for a blank cell in an optional column.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than parsed from `""`, because the parser rejects an empty string
    /// for every one of these - correctly, since a blank where a number belongs is what it
    /// exists to catch. A column saying a blank is expected is a different statement.
    /// </remarks>
    private static object EmptyValueOf(Models.ValueType type)
    {
        switch (type)
        {
            case Models.ValueType.String: return "";
            case Models.ValueType.Bool: return false;
            case Models.ValueType.Int32: return 0;
            case Models.ValueType.Int64: return 0L;

            // No flags set, which is the one value every flag set has.
            case Models.ValueType.Bitset: return 0L;

            case Models.ValueType.Float: return 0f;
            case Models.ValueType.Double: return 0d;
            case Models.ValueType.TimeSpan: return TimeSpan.Zero;
            case Models.ValueType.DateTime: return default(DateTime);
            case Models.ValueType.Uuid: return Guid.Empty;

            // A label's value, not the label: an enum member is an integer to everything
            // downstream, and zero is the one every enum has - the cooker inserts `None`
            // where a sheet did not declare it.
            case Models.ValueType.Enum: return 0;

            // The absence of a referenced row, which is what zero means for a reference.
            case Models.ValueType.ForeignRecord: return 0;

            default:
                    throw new TabbitException(null,
                        Message.Of(CookingMessages.TypeHasNoEmptyValue, ("Type", type)));
        }
    }

    #region Cells

    /// <summary>What a cell writes to say this row has no value here.</summary>
    /// <remarks>
    /// One spelling, for every type and every layout, because absence is the same fact
    /// wherever it is written. **A blank cell is not it.** A blank is what the column's type
    /// already reads it as - an empty string, false, an array of no elements - and reading a
    /// blank as absence instead is what left `string?` with no way to hold an empty string.
    ///
    /// spec/blank-and-null-cells.md.
    /// </remarks>
    public const string NoValueMark = "-";

    /// <summary>How a cell writes the one character <see cref="NoValueMark"/> is, as a value.</summary>
    /// <remarks>
    /// The whole of the escape: these two spellings are special and nothing else is. `-5`,
    /// `A-1` and `\-a` are the text they look like, so a column of ranges or paths does not
    /// change meaning around this rule.
    ///
    /// The cost is that the string `\-` cannot be written, and it is paid deliberately.
    /// Reading `\\-` as it would make `\` an escape character everywhere, and the corpus
    /// already holds strings with `\n` in them - a line break the game's own text renderer
    /// reads - which would then mean something else.
    /// </remarks>
    public const string EscapedNoValueMark = @"\-";

    /// <summary>Whether a cell's text says this row has no value.</summary>
    public static bool SaysNoValue(string? text) => Trimmed(text) == NoValueMark;

    /// <summary>The text a cell holds as a value, with the escape above read.</summary>
    private static string? ValueTextOf(string? text)
        => Trimmed(text) == EscapedNoValueMark ? NoValueMark : text;

    /// <summary>
    /// Whether a blank cell is a value of this type rather than nothing at all.
    /// </summary>
    /// <remarks>
    /// These three have read a blank as a value since before any of this: the empty string,
    /// false, and an array of no elements. Every other type has no value a blank could be,
    /// which is why a blank in a number column is an error and why `OnBlankCell` exists.
    /// </remarks>
    private static bool ReadsBlankAsValue(Models.ValueType type)
        => type is Models.ValueType.String or Models.ValueType.Bool
            || Models.ValueTypes.IsArray(type);

    private static string Trimmed(string? text) => (text ?? "").Trim();

    /// <summary>One data cell, read.</summary>
    public readonly struct CellReading
    {
        public CellReading(object? value, bool hasValue, bool[]? elementHasValue = null)
        {
            Value = value;
            HasValue = hasValue;
            ElementHasValue = elementHasValue;
        }

        /// <summary>What the cell holds - the type's empty value when it holds none.</summary>
        public object? Value { get; }

        /// <summary>Whether the sheet put a value here. False for `-`, and for nothing else.</summary>
        public bool HasValue { get; }

        /// <summary>
        /// Which elements the sheet gave a value, or null where the question does not arise.
        /// </summary>
        public bool[]? ElementHasValue { get; }
    }

    /// <summary>
    /// Reads one data cell: what it holds, and whether the sheet put anything there.
    /// </summary>
    /// <param name="required">
    /// Whether the column says every row has a value. A `-` in a required column is read as
    /// absence here and reported by validation, so a workbook full of them is answered in one
    /// run rather than one cell per run.
    /// </param>
    /// <param name="onBlankCell">
    /// What a blank does where the type has no reading for one. Strict by default.
    /// </param>
    /// <param name="isReference">
    /// Whether the column points at another table's row. A blank one is read as absence for
    /// the reason spec/reference-optionality.md gives: `int.Parse("")` failing says nothing
    /// about the reference it was, and validation can say all of it.
    /// </param>
    /// <param name="column">
    /// `Table.Field`, for the two reports a blank cell can be worth. Null says nothing about
    /// this cell, which is what a caller reading something other than a table's data wants.
    /// </param>
    /// <param name="elementsRequired">
    /// Whether every element of an array cell has to be a value. False for a column typed
    /// `T?[]`, where an element may be `-`.
    /// </param>
    public CellReading ReadCell(
        Models.ValueType type, Models.Enum? enumm, string? rawValue, Location? location,
        char? arrayDelimiter = null, bool required = true,
        BlankCellPolicy onBlankCell = BlankCellPolicy.Error, bool isReference = false,
        string? column = null, bool elementsRequired = true,
        string formulaError = "", FormulaErrorPolicy onFormulaError = FormulaErrorPolicy.Error,
        TimeZoneInfo? timeZone = null)
    {
        // A cell whose formula ended in an error, reported here because **here is where it is
        // known that anything reads the cell.** The stage that read the workbook cannot know:
        // a named rectangle holds the columns a layout keeps and whatever the sheet's authors
        // put beside them, and one project's sheets hold 10,263 cells of working formulas in
        // columns with no name. Every one of those was reported before this, and none of them
        // is in the data. spec/formula-errors.md.
        if (formulaError.Length > 0)
        {
            if (onFormulaError == FormulaErrorPolicy.Error)
            {
                throw new TabbitException(location,
                    Message.Of(CookingMessages.FormulaError, ("Error", formulaError)));
            }

            // Counted per column, as the blank concession below is: a column of a trimmed
            // array can hold thousands of these, and a thousand lines saying one thing is a
            // thousand lines nobody reads to the end.
            if (column is not null)
            {
                NoteCell($"formula-error:{column}", location,
                    Message.Of(CookingMessages.NoticeFormulaErrorEmpty, ("Column", column)));
            }

            return new CellReading(
                NoValueOf(type, enumm, location, arrayDelimiter), hasValue: true);
        }

        if (SaysNoValue(rawValue))
            return new CellReading(NoValueOf(type, enumm, location, arrayDelimiter), hasValue: false);

        bool blank = string.IsNullOrEmpty(rawValue);

        // Before the type is consulted, because a reference is carried as the text the sheet
        // wrote until the target's key type is known - so its type here is `string`, which
        // reads a blank as a value. Left to that, a blank reference would become the empty
        // key and pass as "points at nothing".
        if (blank && isReference)
            return new CellReading(NoValueOf(type, enumm, location, arrayDelimiter), hasValue: false);

        if (blank && !ReadsBlankAsValue(type))
        {
            if (onBlankCell == BlankCellPolicy.Empty)
            {
                // The concession the recipe made, counted per column: a cell nobody filled in
                // became a zero, and how many of those a run swallowed belongs in the run
                // rather than only in the recipe.
                if (column is not null)
                {
                    NoteCell($"blank-filled:{column}", location,
                        Message.Of(CookingMessages.NoticeBlankFilled, ("Column", column)));
                }

                return new CellReading(
                    NoValueOf(type, enumm, location, arrayDelimiter), hasValue: true);
            }

            throw new TabbitException(location, BlankRefusal(type, required));
        }

        // Temporary. A blank in an optional `string`, `bool` or array column used to mean
        // "no value" and now means the empty string, false, or no elements - the one change in
        // spec/blank-and-null-cells.md that is quiet, because nothing about it fails. One line
        // per column for a release, and then it goes.
        if (blank && !required && column is not null)
        {
            NoteCell($"blank-value:{column}", location,
                Message.Of(CookingMessages.NoticeBlankIsEmptyValue, ("Column", column)));
        }

        // An array reads its own elements, because the escape and the mark belong to each of
        // them rather than to the cell: `\-` as a whole cell is a one-element array holding
        // `-`, and unescaping here would hand the splitter a `-` to refuse.
        if (Models.ValueTypes.IsArray(type))
        {
            var elements = ParseArrayValue(
                type, enumm!, rawValue ?? "", location!, arrayDelimiter, elementsRequired,
                out bool[]? elementHasValue, timeZone);

            return new CellReading(elements, hasValue: true, elementHasValue: elementHasValue);
        }

        return new CellReading(
            ParseValue(
                type, enumm, ValueTextOf(rawValue), location, arrayDelimiter,
                timeZone: timeZone),
            hasValue: true);
    }

    /// <summary>The value a cell carries when it says it has none.</summary>
    /// <remarks>
    /// An array answers with no elements rather than through the scalar table: the empty
    /// value of `int[]` is an `int[]`, and handing back a scalar zero there is what made a
    /// `[number]` column holding `-` reach the binary exporter as a string.
    /// </remarks>
    private object NoValueOf(
        Models.ValueType type, Models.Enum? enumm, Location? location, char? arrayDelimiter)
    {
        return Models.ValueTypes.IsArray(type)
            ? ParseArrayValue(type, enumm!, "", location!, arrayDelimiter)
            : EmptyValueOf(type);
    }

    /// <summary>What is wrong with a blank cell, and the ways out of it.</summary>
    /// <remarks>
    /// Two ids rather than one sentence with a clause chosen for it. The clause was the whole
    /// difference between the two - what to do instead of writing a value - and a catalog
    /// entry cannot hold the choice.
    /// </remarks>
    private static Message BlankRefusal(Models.ValueType type, bool required)
        => Message.Of(
            required ? CookingMessages.BlankCellRequired : CookingMessages.BlankCellOptional,
            ("Type", type));

    private sealed class CellNotice
    {
        public string Message = "";

        /// <summary>Which notice this is, kept beside the text it rendered to.</summary>
        public string? MessageId;

        public Location? First;
        public int Count;
    }

    private readonly Dictionary<string, CellNotice> _cellNotices = new Dictionary<string, CellNotice>();

    /// <summary>
    /// Records something true of a cell that is worth saying once per column.
    /// </summary>
    /// <remarks>
    /// Per column rather than per cell because these are about how a column was written: a
    /// sheet with four hundred blanks in one column has one thing wrong with it, and saying
    /// it four hundred times buries every other report of the run.
    /// </remarks>
    public void NoteCell(string key, Location? location, Message message)
    {
        if (!_cellNotices.TryGetValue(key, out var notice))
        {
            notice = new CellNotice
            {
                Message = message.In(MessageCatalog.Current),
                MessageId = message.Id,
                First = location,
            };

            _cellNotices[key] = notice;
        }

        notice.Count++;
    }

    /// <summary>Reports what those notes added up to, once every sheet has been read.</summary>
    public void ReportCellNotices()
    {
        foreach (var notice in _cellNotices.Values)
        {
            Log.Warning(
                $"{notice.Message} ({notice.Count} {(notice.Count == 1 ? "cell" : "cells")})"
                + $"\n    at {notice.First}");
        }

        _cellNotices.Clear();
    }

    #endregion

    /// <param name="arrayDelimiter">
    /// What separates elements of an array cell, when the sheet's own entry named one.
    /// Null takes the recipe-wide delimiter, which is the usual case.
    /// </param>
    /// <param name="required">
    /// Whether a blank cell is an error. False for a column whose type ends in `?`, where a
    /// blank reads as the type's empty value instead.
    /// </param>
    /// <param name="timeZone">
    /// Which time zone a `datetime` cell's wall clock was written in, when the sheet's own
    /// entry named one. Null takes the recipe-wide setting, which is the usual case.
    /// </param>
    public object? ParseValue(
        Models.ValueType type, Models.Enum? enumm, string? rawValue, Location? location,
        char? arrayDelimiter = null, bool required = true, TimeZoneInfo? timeZone = null)
    {
        if (Models.ValueTypes.IsArray(type))
            return ParseArrayValue(type, enumm!, rawValue!, location!, arrayDelimiter, timeZone);

        // An optional column's blank cell. Only reachable for the types a blank was already
        // refused for - a `string` or a `bool` reads a blank as an empty string or false, and
        // has since before this existed - so nothing that worked changes meaning here.
        if (!required && string.IsNullOrEmpty(rawValue))
            return EmptyValueOf(type);

        // What the cell actually holds, kept for the report. A radix literal is rewritten
        // below, and a message naming `4294967295` where the sheet says `0xFFFFFFFF` sends
        // the author looking for a number that is not in their workbook.
        string? authored = rawValue;

        try
        {
            // `0x1f`, `0b1011`. The base is notation and does not widen the type: the
            // literal becomes the decimal it denotes and goes through the type's own
            // parser, so `0xFFFFFFFF` in an `int` column is the overflow it would have
            // been written out. A column that means a 32-bit pattern is a `bitset`, and
            // that type reads its own literals below.
            if (RadixLiteralBase(rawValue!) != 0 && TakesRadixLiteral(type))
                rawValue = DecimalOfRadix(rawValue!, type, location!);

            switch (type)
            {
                case Models.ValueType.String:
                    return rawValue;

                case Models.ValueType.Bool:
                    return ParseBool(rawValue!, location!);

                case Models.ValueType.Bitset:
                    return ParseBitset(rawValue!, location!);

                // Thousands separators are accepted on the numeric types, because a
                // designer reading a column of large numbers writes `1,000,000`. This
                // is only unambiguous under an invariant culture, where a comma can
                // never be the decimal point.
                case Models.ValueType.Int32:
                    return int.Parse(rawValue!, IntegerStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.Int64:
                    return long.Parse(rawValue!, IntegerStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.Float:
                    return float.Parse(rawValue!, DecimalStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.Double:
                    return double.Parse(rawValue!, DecimalStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.TimeSpan:
                    return TimeSpan.Parse(rawValue!, CultureInfo.InvariantCulture);

                case Models.ValueType.DateTime:
                    // AdjustToUniversal, so a cell that wrote its own offset lands on the
                    // moment it named. Without it, `2022-01-24T10:30:00Z` was read into the
                    // time zone of whatever machine ran the conversion - the same sheet
                    // became one value on a designer's PC and another on a build agent.
                    // A cell with no offset is untouched by it and stays a wall clock,
                    // which is what the zone below is for.
                    return ToUtc(
                        DateTime.Parse(
                            rawValue!, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal),
                        timeZone, location);

                case Models.ValueType.Uuid:
                    return Guid.Parse(rawValue!);

                case Models.ValueType.Enum:
                    return enumm!.GetLabel(rawValue!, location).Value;

                case Models.ValueType.ForeignRecord:
                    return int.Parse(rawValue!, IntegerStyles, CultureInfo.InvariantCulture);

                default:
                    throw new TabbitDefectException($"not implemented value type {type}");
            }
        }
        catch (TabbitException)
        {
            // Already carries its own message and location - an enum label that does
            // not exist, or a boolean spelling that is not recognized. Wrapping it
            // would restate the obvious around a better explanation.
            throw;
        }
        catch (Exception ex) when (ex is not TabbitDefectException)
        {
            // Whatever the framework parsers throw: FormatException, OverflowException
            // and friends, whose messages name the problem but not the cell.
            //
            // A defect passes through instead of being dressed as one of those. The switch
            // above throws one when it meets a value type nobody taught it, and without the
            // guard that arrived as `Cannot parse ... as a value of type` against a cell
            // whose author had written nothing wrong.
            throw new TabbitException(location,
                Message.Of(CookingMessages.ValueUnparsable,
                    ("Written", authored), ("Type", type), ("Detail", ex.Message)));
        }
    }

    /// <summary>
    /// The moment a dated cell names, in UTC.
    /// </summary>
    /// <remarks>
    /// Data leaves this tool in UTC, and a zone is how a wall clock gets there. Without one
    /// a sheet's `10:30` is taken to already be UTC, which is what every value written
    /// before this setting existed was read as - so a recipe that says nothing keeps every
    /// value it had.
    ///
    /// The Kind is dropped on the way out. What is stored is a number of ticks, and the
    /// exports read it as UTC by contract rather than from a flag: a value marked Utc is
    /// refused by the PostgreSQL writer for a `timestamp` column, and marking it would make
    /// the setting change the shape of exports as well as their values.
    ///
    /// spec/datetime-timezone.md.
    /// </remarks>
    private DateTime ToUtc(DateTime parsed, TimeZoneInfo? zone, Location? location)
    {
        // The cell wrote its own offset, so the parse already landed on the moment. `Z` is
        // an answer, not a question, and reading it again as somebody's wall clock would
        // move a value that was never ambiguous.
        if (parsed.Kind != DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Unspecified);

        zone ??= TimeZone;

        if (zone is null || zone.Equals(TimeZoneInfo.Utc))
            return parsed;

        // A wall clock the region's clocks skipped. There is a right answer here only for
        // the person who wrote the cell - an hour earlier and an hour later are both
        // defensible, and picking one silently moves an event by an hour.
        if (zone.IsInvalidTime(parsed))
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.TimeInDstGap,
                    ("Time", parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    ("Zone", zone.Id)));
        }

        // A wall clock the region's clocks passed through twice. Unlike the gap above there
        // is a value to read, so the run continues on the standard-time reading and says how
        // many cells it did that to - one hour a year is not worth stopping a conversion for,
        // and it is worth knowing about.
        if (zone.IsAmbiguousTime(parsed))
        {
            NoteCell($"ambiguous-time:{zone.Id}", location,
                Message.Of(CookingMessages.NoticeAmbiguousTime, ("Zone", zone.Id)));
        }

        return DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(parsed, zone), DateTimeKind.Unspecified);
    }

    // --------------------------------------------------------- radix literals

    /// <summary>
    /// The base a `0x` or `0b` literal is written in, or zero when the text is not one.
    /// </summary>
    /// <remarks>
    /// A sign is stepped over rather than judged here, because whether one is allowed is the
    /// type's question: a magnitude may carry a sign and a bit pattern may not.
    /// </remarks>
    private static int RadixLiteralBase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int at = text[0] is '-' or '+' ? 1 : 0;

        if (text.Length < at + 3 || text[at] != '0')
            return 0;

        return text[at + 1] switch
        {
            'x' or 'X' => 16,
            'b' or 'B' => 2,
            _ => 0,
        };
    }

    /// <summary>Which types read a `0x` or `0b` literal as one of their values.</summary>
    /// <remarks>
    /// The four numeric types, `float` and `double` included. A layout that does not narrow
    /// its number columns widens them to `double`, so a rule stopping at the integers would
    /// miss those columns in the configuration that is the default one - and colour values,
    /// which is where these literals mostly are, sit in exactly them.
    /// </remarks>
    private static bool TakesRadixLiteral(Models.ValueType type)
        => type is Models.ValueType.Int32 or Models.ValueType.Int64
            or Models.ValueType.Float or Models.ValueType.Double;

    /// <summary>
    /// A `0x` or `0b` literal as the decimal it denotes, for the type's own parser to read.
    /// </summary>
    private static string DecimalOfRadix(string text, Models.ValueType type, Location location)
    {
        bool negative = text[0] == '-';
        int at = text[0] is '-' or '+' ? 1 : 0;

        int radix = text[at + 1] is 'x' or 'X' ? 16 : 2;
        ulong magnitude = RadixDigits(text.Substring(at + 2), radix, text, location);

        // Every type reaching here is signed, so the magnitude has to leave room for the
        // sign even when none is written.
        if (magnitude > long.MaxValue)
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.MagnitudeTooLarge, ("Text", text), ("Type", type)));
        }

        // A float column takes the literal only where it holds the integer exactly. Above
        // the mantissa the value reads back as a neighbouring one, and nothing downstream
        // would say so - the same silent failure the whole-number encoding checks for.
        if (type is Models.ValueType.Float or Models.ValueType.Double)
        {
            ulong exact = type == Models.ValueType.Float ? 1UL << 24 : 1UL << 53;

            if (magnitude > exact)
            {
                throw new TabbitException(location,
                    Message.Of(CookingMessages.FloatLosesExactness,
                        ("Text", text), ("Type", type), ("Exact", exact)));
            }
        }

        return (negative ? "-" : "") + magnitude.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The digits of a radix literal, refusing whatever the base does not spell.</summary>
    private static ulong RadixDigits(string digits, int radix, string text, Location location)
    {
        int limit = radix == 16 ? 16 : 64;

        if (digits.Length > limit)
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.RadixTooManyDigits,
                    ("Text", text), ("Digits", digits.Length),
                    ("Radix", radix), ("Limit", limit)));
        }

        foreach (char digit in digits)
        {
            if (!IsRadixDigit(digit, radix))
            {
                throw new TabbitException(location,
                    Message.Of(CookingMessages.RadixBadDigit,
                        ("Text", text), ("Digit", digit), ("Radix", radix)));
            }
        }

        return Convert.ToUInt64(digits, radix);
    }

    private static bool IsRadixDigit(char digit, int radix)
        => radix == 2
            ? digit is '0' or '1'
            : (digit >= '0' && digit <= '9')
                || (digit >= 'a' && digit <= 'f')
                || (digit >= 'A' && digit <= 'F');

    // ----------------------------------------------------------------- bitset

    /// <summary>
    /// A flag set - `0x1f`, `0b1011` or a decimal - as the bit pattern of a 64-bit integer.
    /// </summary>
    /// <remarks>
    /// Stricter than the numeric types deliberately, and **the refusals are the content of
    /// this type**. A bit pattern has no sign, no thousands separator and no fractional
    /// part, so each of those is a mistake rather than a notation to accommodate. `1.0` is
    /// refused alongside `1.5`, because "the fractional part is zero so it is allowed" is
    /// where the ambiguity starts.
    ///
    /// Decimal stops at 2^53. A spreadsheet holds a numeric cell as a double, so a decimal
    /// above that has already been rounded before anything here sees it - measurably:
    /// `9007199254740993` arrives as `9007199254740992`. Refusing it costs no expressible
    /// value, because a numeric cell cannot carry those in the first place.
    ///
    /// `0x` and `0b` reach all 64 bits, this being the one type whose value is a pattern
    /// rather than a magnitude: `0xFFFFFFFFFFFFFFFF` is every flag set, carried as the
    /// signed -1 it shares its bits with.
    /// </remarks>
    private static long ParseBitset(string rawValue, Location location)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            throw new TabbitException(location, Message.Of(CookingMessages.BitsetEmpty));
        }

        int radix = RadixLiteralBase(rawValue!);

        if (radix != 0)
        {
            if (rawValue[0] is '-' or '+')
                throw new TabbitException(location, SignRefusal(rawValue));

            return unchecked((long)RadixDigits(rawValue.Substring(2), radix, rawValue, location));
        }

        foreach (char character in rawValue)
        {
            if (character < '0' || character > '9')
                throw new TabbitException(location, DecimalRefusal(rawValue, character));
        }

        // Every character is a digit by here, so the only way this fails is by being longer
        // than 64 bits hold - which the 2^53 limit below would have refused anyway.
        if (!ulong.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value)
            || value > (1UL << 53))
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.BitsetAbove253, ("Text", rawValue)));
        }

        return (long)value;
    }

    private static Message SignRefusal(string text)
        => Message.Of(CookingMessages.BitsetSigned, ("Text", text));

    /// <summary>What is wrong with a decimal `bitset` cell, named by the character that says so.</summary>
    /// <remarks>
    /// The switch stays and what it chooses between changes: it used to pick a sentence and now
    /// picks an id. Six branches were already six sentences - this only moves where they are
    /// written down, and makes each one something a translator sees whole.
    /// </remarks>
    private static Message DecimalRefusal(string text, char character) => character switch
    {
        '.' => Message.Of(CookingMessages.BitsetDecimalPoint, ("Text", text)),

        ',' => Message.Of(CookingMessages.BitsetThousandsSeparator, ("Text", text)),

        '-' or '+' => SignRefusal(text),

        'e' or 'E' => Message.Of(CookingMessages.BitsetExponent, ("Text", text)),

        '_' => Message.Of(CookingMessages.BitsetDigitSeparator, ("Text", text)),

        _ => Message.Of(CookingMessages.BitsetNotADigit,
            ("Text", text), ("Character", character)),
    };

    /// <summary>
    /// Splits a delimited cell and parses each element.
    ///
    /// An empty cell is an empty array rather than an error: a row that simply has
    /// no values for the column is the common case, and rejecting it would force
    /// designers to invent a placeholder.
    ///
    /// Elements are trimmed, so `1; 2 ;3` reads the same as `1;2;3`.
    /// </summary>
    private object ParseArrayValue(
        Models.ValueType arrayType, Models.Enum enumm, string rawValue, Location location,
        char? arrayDelimiter, TimeZoneInfo? timeZone = null)
        => ParseArrayValue(
            arrayType, enumm, rawValue, location, arrayDelimiter, true, out _, timeZone);

    /// <summary>
    /// The same, answering which elements the sheet gave a value.
    /// </summary>
    private object ParseArrayValue(
        Models.ValueType arrayType, Models.Enum enumm, string rawValue, Location location,
        char? arrayDelimiter, bool elementsRequired, out bool[]? elementHasValue,
        TimeZoneInfo? timeZone = null)
    {
        elementHasValue = null;

        var elementType = Models.ValueTypes.ElementOf(arrayType);

        if (string.IsNullOrWhiteSpace(rawValue))
            return System.Array.CreateInstance(ElementClrType(elementType, enumm), 0);

        var parts = rawValue.Split(arrayDelimiter ?? ArrayDelimiter);
        var result = System.Array.CreateInstance(ElementClrType(elementType, enumm), parts.Length);

        for (int i = 0; i < parts.Length; i++)
        {
            string element = parts[i].Trim();

            // `-` says this element has no value, which only a column typed `T?[]` allows.
            // Refused elsewhere, because in an array of required elements the mark would have
            // nothing to mean and the cell would quietly hold the type's empty value.
            // spec/nullable-array-elements.md.
            if (SaysNoValue(element))
            {
                if (elementsRequired)
                {
                    throw new TabbitException(location,
                        Message.Of(CookingMessages.ArrayElementNoValueMark,
                            ("Element", i + 1), ("Mark", NoValueMark),
                            ("Escaped", EscapedNoValueMark)));
                }

                elementHasValue ??= AllPresent(parts.Length);
                elementHasValue[i] = false;
                result.SetValue(EmptyValueOf(elementType), i);
                continue;
            }

            result.SetValue(
                ParseValue(
                    elementType, enumm, ValueTextOf(element), location, timeZone: timeZone),
                i);
        }

        return result;
    }

    /// <summary>An answer of "present" for every element, for a cell that is about to say otherwise.</summary>
    private static bool[] AllPresent(int count)
    {
        var present = new bool[count];

        for (int at = 0; at < count; at++)
            present[at] = true;

        return present;
    }

    /// <summary>
    /// The CLR element type to allocate an array of.
    ///
    /// Typed rather than object[]: the exporters cast each element to its concrete
    /// type, and JSON serialization of an object[] would render enums as bare
    /// integers inconsistently with the scalar path.
    /// </summary>
    private static System.Type ElementClrType(Models.ValueType elementType, Models.Enum enumm)
    {
        return elementType switch
        {
            Models.ValueType.String => typeof(string),
            Models.ValueType.Bool => typeof(bool),
            Models.ValueType.Int32 => typeof(int),
            Models.ValueType.Int64 => typeof(long),
            Models.ValueType.Bitset => typeof(long),
            Models.ValueType.Float => typeof(float),
            Models.ValueType.Double => typeof(double),
            Models.ValueType.TimeSpan => typeof(System.TimeSpan),
            Models.ValueType.DateTime => typeof(System.DateTime),
            Models.ValueType.Uuid => typeof(System.Guid),
            // Enum labels and record references are both stored as their integer.
            Models.ValueType.Enum => typeof(int),
            Models.ValueType.ForeignRecord => typeof(int),
            _ => typeof(object),
        };
    }

    /// <summary>
    /// Reads a boolean cell.
    ///
    /// Several spellings are accepted because designers reach for whichever reads
    /// best in the sheet: Y/N, YES/NO, TRUE/FALSE, 1/0. Case does not matter.
    ///
    /// An empty cell is false. That is deliberate - a blank means "not set" and
    /// false is the useful reading of that - and it is the one lenient case here.
    ///
    /// Anything else is an error. It used to fall through to false, so `Yes please`
    /// or a misspelled `Ture` became false silently: exactly the human mistake this
    /// tool exists to catch, turned into wrong data instead of a message.
    /// </summary>
    private bool ParseBool(string value, Location location)
    {
        if (value.Length == 0)
            return false;

        switch (value.ToUpperInvariant())
        {
            case "N":
            case "NO":
            case "FALSE":
                return false;

            case "Y":
            case "YES":
            case "TRUE":
                return true;
        }

        // Numeric spellings, so a column of counts can be read as flags: zero is
        // false and anything else is true, as in C.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return number != 0.0;

        throw new TabbitException(location,
            Message.Of(CookingMessages.NotABoolean, ("Value", value)));
    }

    #endregion


    #region Enums

    /// <summary>
    /// Gives an enum a zero label when it declared neither the name nor the value, so a
    /// default-constructed field of that type means something.
    /// </summary>
    /// <remarks>
    /// Every layout wants this and for the same reason, so it is asked here rather than
    /// left to each parser to remember. An enum that already has something at zero is left
    /// exactly as written.
    /// </remarks>
    public void ApplyAutoNoneLabel(Models.Enum enumm, Location location)
    {
        if (!AutoInsertEnumNoneLabel)
            return;

        if (enumm.Contains("None") || enumm.Contains(0))
            return;

        enumm.Labels.Insert(0, new Models.Enum.Label
        {
            Location = location,
            RawName = "None",
            Name = "None",
            Value = 0,
            Comment = "None (automatically inserted by Tabbit)",
            Synthesized = true,
        });
    }

    #endregion


    #region Tables

    public void CheckPrimaryIndexValidity(Models.Field field)
    {
        // The name is not fixed to `index`. Reference resolution reads the target
        // table's own primary-index function name, so a sheet whose first column is
        // called something else resolves the same way.
        //
        // And the type is not fixed to `int`. What a key has to be is a value that compares
        // exactly, reads the same every time and has more of itself than the table has rows -
        // which is `ValueTypes.CanBeIndexKey`, the same question asked of a `*` column. The
        // generated lookup is a dictionary over the field's own type, so a key of any of them
        // needs nothing the int one did not already need.
        //
        // What a non-`int` key does cost is references *to* this table - those store the
        // target's key and the wire stores it as an int32. ValidateReferences refuses them
        // by name, which is why that constraint is not repeated here.
        if (!Models.ValueTypes.CanBeIndexKey(field.Type, out string? why))
        {
                throw new TabbitException(field.TypeLocation,
                    Message.Of(CookingMessages.IndexTypeUnusableInTable,
                        ("Field", field.Name), ("Type", field.TypeName), ("Why", why)));
        }

        // The index is what every row is identified by and what every reference to this
        // table resolves through, so a blank one has nothing to mean. `int?` gets refused
        // here rather than silently handing several rows the same index 0.
        if (!field.IsRequired)
                throw new TabbitException(field.TypeLocation,
                    Message.Of(CookingMessages.IndexFieldOptional));

        if (field.TargetSide != Models.TargetSide.Both)
                throw new TabbitException(field.TargetSideLocation,
                    Message.Of(CookingMessages.IndexFieldTargetSide));
    }

    /// <summary>
    /// Gives every logical column its wire tag, checking the sheet's own against each other.
    /// </summary>
    /// <remarks>
    /// A logical column is a serial field - `Ref1..Ref3` is one column with one tag, carried
    /// on its first member.
    ///
    /// Two modes, decided per table and never mixed. If no field carries a tag, the ordinal
    /// position is the tag: the file is still self-describing, but only appending columns is
    /// safe, because an insertion shifts every ordinal after it. The moment any field carries
    /// one, all of them must - a half-tagged table gets neither mode's guarantees - and then
    /// the tags are checked unique, including against the tombstones' reserved ones.
    /// </remarks>
    public void AssignTags(Models.Table table)
    {
        var serials = table.SerialFields;

        // A serial field is one logical column; the tag goes on its first member.
        foreach (var sf in serials)
        {
            foreach (var extra in sf.NonTagCarryingFields)
            {
                if (extra.Tag is not null)
                {
                        throw new TabbitException(extra.NameLocation,
                            Message.Of(CookingMessages.WireTagOnSerialMember,
                                ("Table", table.Name), ("Field", extra.Name),
                                ("Serial", sf.Name), ("Tag", extra.Tag)));
                }
            }
        }

        // The unit being tagged is a wire column, not a group. They are the same thing for
        // every table written before records existed, and differ for a record group: it
        // stores one column per member, so it takes one tag per member.
        //
        // The same list the writer and the baseline check read, deliberately - three places
        // deciding separately what a tag identifies is how they come to disagree.
        var columns = table.WireColumns;

        var tagged = columns.Where(c => c.TagCarrier.Tag is not null).ToList();

        if (tagged.Count == 0)
        {
            if (table.ReservedTags.Count > 0)
            {
                    throw new TabbitException(table.Location,
                        Message.Of(CookingMessages.WireTagOnlyOnTombstone, ("Table", table.Name)));
            }

            // Ordinal mode: the tag is the column's position, which is safe to append
            // to and nothing else. Recorded as such, because it is what decides how much
            // of a schema change the baseline check can let through.
            table.HasExplicitTags = false;

            for (int position = 0; position < columns.Count; position++)
                columns[position].TagCarrier.Tag = position + 1;

            return;
        }

        if (tagged.Count != columns.Count)
        {
            var untagged = columns
                .Where(c => c.TagCarrier.Tag is null)
                .Select(c => c.Name);

                throw new TabbitException(table.Location,
                    Message.Of(CookingMessages.WireTagsPartial,
                        ("Table", table.Name),
                        ("Untagged", string.Join(", ", untagged))));
        }

        var seen = new Dictionary<int, string>();

        foreach (int reserved in table.ReservedTags)
            seen[reserved] = "a `#`-excluded column";

        foreach (var column in columns)
        {
            var field = column.TagCarrier;
            int tag = field.Tag!.Value;
            string name = column.Name;

            if (seen.TryGetValue(tag, out string? holder))
            {
                    throw new TabbitException(field.NameLocation,
                        Message.Of(CookingMessages.WireTagReused,
                            ("Table", table.Name), ("Field", name),
                            ("Tag", tag), ("Holder", holder)));
            }

            seen[tag] = $"field `{name}`";
        }

        table.HasExplicitTags = true;
    }

    #endregion
}
