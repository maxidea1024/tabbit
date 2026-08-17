using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Models;
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
    /// <summary>Number formats accepted in an integer cell.</summary>
    private const NumberStyles IntegerStyles = NumberStyles.Integer | NumberStyles.AllowThousands;

    /// <summary>Number formats accepted in a float or double cell.</summary>
    private const NumberStyles DecimalStyles = NumberStyles.Float | NumberStyles.AllowThousands;

    public CookingContext(Model model, RecipeModel recipe)
    {
        Model = model;
        ArrayDelimiter = ResolveArrayDelimiter(recipe);
        AutoInsertEnumNoneLabel = recipe.AutoInsertEnumNoneLabel;
        Palettes = ResolvePalettes(recipe);
    }

    /// <summary>The model every parser adds to.</summary>
    public Model Model { get; }

    /// <summary>The colour names a cell may write, built in and recipe-declared.</summary>
    public ColorPalettes Palettes { get; }

    /// <summary>
    /// Separator for array cells, taken from the recipe. A source entry may override it.
    /// </summary>
    public char ArrayDelimiter { get; }

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
            throw new TabbitException(
                $"Recipe setting `ArrayDelimiter` is `{delimiter}`, but it must be exactly one character.");
        }

        return delimiter[0];
    }

    /// <summary>
    /// Reads the palettes a recipe declared, leaving a run that declared none with the
    /// built-in one alone.
    /// </summary>
    /// <remarks>
    /// Read once here rather than per cell. A palette file that is missing or malformed is a
    /// fault in the recipe, and reporting it while the first coloured cell happens to be
    /// parsed would make it look like a fault in the sheet.
    /// </remarks>
    private static ColorPalettes ResolvePalettes(RecipeModel recipeModel)
    {
        if (recipeModel.Palettes.Count == 0)
            return ColorPalettes.BuiltInOnly;

        var loaded = new Dictionary<string, IReadOnlyDictionary<string, uint>>();

        foreach (var (name, path) in recipeModel.Palettes)
            loaded[name] = ColorPalettes.ReadFile(name, path);

        return ColorPalettes.With(loaded);
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
                $"`{name}` is not a valid identifier, so it cannot name a field or an entity. "
                + $"A name has to start with a letter or underscore and hold only letters, digits "
                + $"and underscores.");
        }
    }

    public void RequiresValidTypeName(string typeName, Location location)
    {
        if (IsValidTypeName(typeName))
            return;

        throw new TabbitException(location, $"type `{typeName}` is an unrecognized type.");
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
    {
        string text = (typeName ?? "").Trim();

        // After the array brackets, so `int[]?` is an optional array rather than an array of
        // optionals - which is not a thing here: the elements of an array cell are all or
        // nothing together.
        if (text.EndsWith("?", StringComparison.Ordinal))
        {
            required = false;
            return text.Substring(0, text.Length - 1).Trim();
        }

        required = true;
        return text;
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
                    $"type `{typeName}` names something in brackets, but `{bare}` is not a type "
                    + $"that takes one. `text` takes a group and `asset` takes a kind.");
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
            throw new TabbitException(location,
                $"`{written}` opens brackets and names no {what}. Write one - `{example}` - or "
                + $"drop the brackets.");
        }

        if (space is not null && role != StringRole.Text)
        {
            throw new TabbitException(location,
                $"`{written}` has a second name in its brackets, and `asset` takes only a kind. "
                + $"A namespace is a `text` thing - the folders an asset is looked for in come "
                + $"from the recipe, keyed by the kind.");
        }

        if (space is not null && space.Length == 0)
        {
            throw new TabbitException(location,
                $"`{written}` ends in a comma and names no namespace. Write one - "
                + $"`text(Achievement,Quests)` - or drop the comma.");
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

        // One cell holding several named components - a vector, a rotation, a colour. A type
        // for as long as parsing lasts, like `bitset`; the cooker expands a column of one
        // into a record. See spec/composite-value-types.md.
        if (Models.CompositeTypes.ByName(typeName) is not null)
            return true;

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

        throw new TabbitException(location, $"Illegal target-side '{value}'");
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
                throw new TabbitException(location, $"type `{elementName}` cannot be used as an array element.");

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

        // Also the composites. Their names carry the component type - `vec2i` against
        // `vec2f` - so the type row says what a cell holds rather than leaving it to a
        // default nobody reading the sheet can see.
        if (Models.CompositeTypes.ByName(typeName) is { } composite)
            return composite.Type;

        // Also enum.
        if (Model.ContainsEnum(typeName))
            return Models.ValueType.Enum;

        throw new TabbitException(location, $"unsupported type '{typeName}'");
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
                // Zero for most components, and deliberately not for all: a quaternion of
                // four zeros is not a rotation, so its empty value is the identity one.
                if (Models.CompositeTypes.Of(type) is { } composite)
                    return CompositeValues.Empty(composite);

                throw new TabbitException(
                    $"There is no empty value for type `{type}`, so a column of it cannot be optional.");
        }
    }

    /// <param name="arrayDelimiter">
    /// What separates elements of an array cell, when the sheet's own entry named one.
    /// Null takes the recipe-wide delimiter, which is the usual case.
    /// </param>
    /// <param name="required">
    /// Whether a blank cell is an error. False for a column whose type ends in `?`, where a
    /// blank reads as the type's empty value instead.
    /// </param>
    public object? ParseValue(
        Models.ValueType type, Models.Enum? enumm, string? rawValue, Location? location,
        char? arrayDelimiter = null, bool required = true)
    {
        if (Models.ValueTypes.IsArray(type))
            return ParseArrayValue(type, enumm!, rawValue!, location!, arrayDelimiter);

        // An optional column's blank cell. Only reachable for the types a blank was already
        // refused for - a `string` or a `bool` reads a blank as an empty string or false, and
        // has since before this existed - so nothing that worked changes meaning here.
        if (!required && string.IsNullOrEmpty(rawValue))
            return EmptyValueOf(type);

        // A composite reads the whole cell in its own notation - a tuple, a hex colour, a
        // name - so it comes before the radix rewrite below. `#3366CC` is not a number and
        // `0x3366CC` in a colour column is six hex digits rather than the integer 3368140.
        if (Models.CompositeTypes.Of(type) is { } composite)
            return CompositeValues.Parse(composite, rawValue, location, Palettes);

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
                    return DateTime.Parse(rawValue!, CultureInfo.InvariantCulture);

                case Models.ValueType.Uuid:
                    return Guid.Parse(rawValue!);

                case Models.ValueType.Enum:
                    return enumm!.GetLabel(rawValue!, location).Value;

                case Models.ValueType.ForeignRecord:
                    return int.Parse(rawValue!, IntegerStyles, CultureInfo.InvariantCulture);

                default:
                    throw new Exception($"not implemented value type {type}");
            }
        }
        catch (TabbitException)
        {
            // Already carries its own message and location - an enum label that does
            // not exist, or a boolean spelling that is not recognized. Wrapping it
            // would restate the obvious around a better explanation.
            throw;
        }
        catch (Exception ex)
        {
            // Whatever the framework parsers throw: FormatException, OverflowException
            // and friends, whose messages name the problem but not the cell.
            throw new TabbitException(location, $"Cannot parse `{authored}` as a value of type `{type}`. ({ex.Message})");
        }
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
    /// One component of a composite cell, with any radix literal already rewritten.
    /// </summary>
    /// <remarks>
    /// A component is a value of its own type, so it reads that type's notation: `(0xFF,
    /// 0x80, 0x40)` is three integers written in base 16. The whole-cell colour forms are
    /// read before this and never reach it.
    /// </remarks>
    internal static string ComponentLiteral(
        string text, Models.ValueType componentType, Location? location)
        => RadixLiteralBase(text) != 0 && TakesRadixLiteral(componentType)
            ? DecimalOfRadix(text, componentType, location!)
            : text;

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
                $"`{text}` does not fit a signed 64-bit value, and `{type}` is a magnitude. "
                + "A value that uses all 64 bits belongs in a `bitset` column.");
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
                    $"`{text}` is above what `{type}` holds exactly ({exact}), so it would read back "
                    + "as a different value.");
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
                $"`{text}` carries {digits.Length} base-{radix} digits, and 64 bits take at most {limit}.");
        }

        foreach (char digit in digits)
        {
            if (!IsRadixDigit(digit, radix))
            {
                throw new TabbitException(location,
                    $"`{text}` has `{digit}` where a base-{radix} digit belongs.");
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
            throw new TabbitException(location,
                "a `bitset` cell is empty. Write a value, or type the column `bitset?` to say a blank is expected.");
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
                $"`{rawValue}` is above 2^53, where a spreadsheet's numeric cell has already lost the "
                + "exact value. Write it as `0x…` or `0b…`.");
        }

        return (long)value;
    }

    private static string SignRefusal(string text)
        => $"`{text}` carries a sign, and a `bitset` holds a bit pattern rather than a magnitude. "
            + "Every bit set is `0xFFFFFFFFFFFFFFFF`.";

    /// <summary>What is wrong with a decimal `bitset` cell, named by the character that says so.</summary>
    private static string DecimalRefusal(string text, char character) => character switch
    {
        '.' => $"`{text}` has a decimal point, and a flag set has no fractional part. "
            + "`1.0` is refused with `1.5`, so that neither has to be guessed at.",

        ',' => $"`{text}` has a thousands separator, which says nothing about a bit pattern.",

        '-' or '+' => SignRefusal(text),

        'e' or 'E' => $"`{text}` is in exponent notation, which a `bitset` does not read. "
            + "Write `0x…` or `0b…`.",

        '_' => $"`{text}` has a digit separator, which this notation does not take.",

        _ => $"`{text}` has `{character}` where a decimal digit belongs. "
            + "A base-16 or base-2 value is written `0x…` or `0b…`.",
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
        char? arrayDelimiter)
    {
        var elementType = Models.ValueTypes.ElementOf(arrayType);

        if (string.IsNullOrWhiteSpace(rawValue))
            return System.Array.CreateInstance(ElementClrType(elementType, enumm), 0);

        var parts = rawValue.Split(arrayDelimiter ?? ArrayDelimiter);
        var result = System.Array.CreateInstance(ElementClrType(elementType, enumm), parts.Length);

        for (int i = 0; i < parts.Length; i++)
            result.SetValue(ParseValue(elementType, enumm, parts[i].Trim(), location), i);

        return result;
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
            $"`{value}` is not a boolean. Use Y/N, YES/NO, TRUE/FALSE, 1/0, or leave the cell empty for false.");
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
            Comment = "None (automatically inserted by Tabbit)"
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
                $"The index field `{field.Name}` is `{field.TypeName}`, {why}"
                + $" Use a whole-number, string, uuid or enum column as the index.");
        }

        // The index is what every row is identified by and what every reference to this
        // table resolves through, so a blank one has nothing to mean. `int?` gets refused
        // here rather than silently handing several rows the same index 0.
        if (!field.IsRequired)
            throw new TabbitException(field.TypeLocation, "The index field cannot be optional: `int?` is not allowed for the index field, because every row must have an index.");

        if (field.TargetSide != Models.TargetSide.Both)
            throw new TabbitException(field.TargetSideLocation, $"The target-side of the index field must be set to CS.");
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
                        $"Field `{table.Name}.{extra.Name}` is part of the serial field " +
                        $"`{sf.Name}` and carries wire tag {extra.Tag}. A serial field is one " +
                        "column on the wire, so the tag goes on its first member only.");
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
                    $"Table `{table.Name}` has a `#`-excluded column reserving a wire tag, but " +
                    "no live field carries one. Tags are all-or-none per table: give every " +
                    "field its `@N`, or drop the tag from the tombstone.");
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
                $"Table `{table.Name}` tags some fields and not others: " +
                $"{string.Join(", ", untagged)} carry no `@N`. Tags are all-or-none per " +
                "table, because a half-tagged table gets neither mode's guarantees.");
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
                    $"Field `{table.Name}.{name}` declares wire tag {tag}, which {holder} " +
                    "already holds. A tag identifies a column for the life of the data, so it " +
                    "can never be shared or reused.");
            }

            seen[tag] = $"field `{name}`";
        }

        table.HasExplicitTags = true;
    }

    #endregion
}
