using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// What `set` and `map` are, and what a declaration may write inside their brackets.
/// </summary>
/// <remarks>
/// **One file, because the parser deliberately knows none of it.** The notation reads
/// `name&lt;…&gt;` for any name, so which names are containers, how many arguments each takes and
/// what may fill them are questions about types rather than about syntax - and keeping them
/// here is what lets a container be added without editing the parser.
///
/// spec/types/set-and-map.md sections 2 and 6.
/// </remarks>
internal static class SchemaContainers
{
    /// <summary>The member names a `map` group's two columns are written under.</summary>
    public const string KeyMember = "Key";

    /// <summary>The second of them.</summary>
    public const string ValueMember = "Value";

    /// <summary>
    /// Metadata keys that say something about one value rather than about the container.
    /// </summary>
    /// <remarks>
    /// These are the keys that have to be written inside a container's brackets, on the
    /// argument they are about. A `map` has two element positions and the key name does not
    /// say which one is meant, so the position does. Section 2.2 of the spec.
    /// </remarks>
    public static readonly string[] ElementKeys =
        ["min", "max", "allowed", "notDefault", "regex", "text", "asset"];

    /// <summary>
    /// Types whose equality is in the value itself, which is what a key has to be.
    /// </summary>
    /// <remarks>
    /// Floating point is out because equality there is in the spelling; `datetime` and
    /// `timespan` are out because a sheet's cell reaches a value through a timezone reading,
    /// so two cells written the same can arrive different; `bitset` is out because its
    /// notation is a list of flag names that the same set can be written several orders of.
    /// Section 6.1 of the spec.
    /// </remarks>
    private static readonly ValueType[] KeyTypes =
    [
        ValueType.Int32, ValueType.Int64, ValueType.String,
        ValueType.Enum, ValueType.Uuid, ValueType.Bool,
    ];

    /// <summary>Which container a type name is, or none when the name is not one.</summary>
    public static ContainerKind KindOf(string name)
        => name switch
        {
            "set" => ContainerKind.Set,
            "map" => ContainerKind.Map,
            _ => ContainerKind.None,
        };

    /// <summary>How many type arguments a container takes.</summary>
    public static int ArityOf(ContainerKind kind)
        => kind == ContainerKind.Map ? 2 : 1;

    /// <summary>The container a member was declared as, or none.</summary>
    public static ContainerKind KindOf(SchemaTypeRef type)
        => type.Form == SchemaTypeForm.Container ? KindOf(type.Name) : ContainerKind.None;

    /// <summary>
    /// Checks a container member and reports what it cannot be, in the order an author
    /// would fix it.
    /// </summary>
    /// <returns>False when something was reported, so the caller stops rather than piling on.</returns>
    public static bool Check(
        CookingContext context,
        SchemaStruct declared,
        SchemaField member,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        var type = member.Type;
        var kind = KindOf(type.Name);

        if (kind == ContainerKind.None)
        {
            diagnostics.Error(type.Location, Message.Of(
                SchemaMessages.TypeTakesNoArguments, ("Type", type.Name)));

            return false;
        }

        // An array of containers. The file could hold one - a numbered group of maps is a
        // record array, and an array of sets is an array of arrays - but neither has a cell
        // notation settled, and settling it is the same problem `sep` has. Section 2.1.
        if (type.IsArray || type.ElementsAreOptional)
        {
            diagnostics.Error(type.Location, Message.Of(
                SchemaMessages.ContainerArrayNotSupported,
                ("Struct", declared.Name), ("Member", member.Name), ("Type", type.ToString())));

            return false;
        }

        int wanted = ArityOf(kind);

        if (type.Arguments.Count != wanted)
        {
            diagnostics.Error(type.Location, Message.Of(
                SchemaMessages.ContainerArity,
                ("Type", type.Name), ("Wanted", wanted), ("Given", type.Arguments.Count)));

            return false;
        }

        // The element constraints of a container go on the argument they are about. Reported
        // before the arguments themselves, because an author who wrote them outside has
        // written a correct constraint in the wrong place and the rest of the report would
        // be about something else.
        var outside = member.Meta.Entries
            .Where(entry => ElementKeys.Contains(entry.Key))
            .ToList();

        if (outside.Count > 0)
        {
            diagnostics.Error(outside[0].Location, Message.Of(
                SchemaMessages.ContainerElementMetaOutside,
                ("Struct", declared.Name),
                ("Member", member.Name),
                ("Key", outside[0].Key),
                ("Type", type.Name)));

            return false;
        }

        for (int at = 0; at < type.Arguments.Count; at++)
        {
            if (!CheckArgument(
                    context, declared, member, type.Arguments[at],
                    kind == ContainerKind.Map && at == 0, declarations, diagnostics))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks one of a container's arguments: what it is, and whether a key may be it.
    /// </summary>
    private static bool CheckArgument(
        CookingContext context,
        SchemaStruct declared,
        SchemaField member,
        SchemaTypeRef argument,
        bool isKey,
        SchemaDeclarations declarations,
        Diagnostics diagnostics)
    {
        // Three shapes the first release leaves out, each because the cell notation rather
        // than the file is what has no room for it. Section 10 of the spec.
        string? refused =
            argument.Form == SchemaTypeForm.Container ? "container"
            : argument.Form == SchemaTypeForm.Foreign ? "reference"
            : argument.IsArray ? "array"
            : argument.IsOptional || argument.ElementsAreOptional ? "optional"
            : null;

        if (refused is not null)
        {
            diagnostics.Error(argument.Location, Message.Of(
                SchemaMessages.ContainerArgumentUnsupported,
                ("Struct", declared.Name),
                ("Member", member.Name),
                ("Type", argument.ToString()),
                ("What", refused)));

            return false;
        }

        var declaredEnum = declarations.FindEnum(argument.Name);
        var declaredStruct = declarations.FindStruct(argument.Name);

        if (declaredStruct is not null)
        {
            // A struct value is a level of the path rather than a column, which is what the
            // `Value` group already is - so it fits. A key cannot be one: its columns would
            // be several, and uniqueness across several columns is what `uniqueBy` is about
            // rather than what a map key is.
            if (!isKey)
                return true;

            diagnostics.Error(argument.Location, Message.Of(
                SchemaMessages.MapKeyTypeNotAllowed,
                ("Struct", declared.Name),
                ("Member", member.Name),
                ("Type", argument.Name),
                ("Allowed", AllowedKeyTypes())));

            return false;
        }

        // `enum` and `foreign` are words of the notation rather than type names, and
        // `IsValidTypeName` answers yes to both. Written as an argument they are neither a
        // built-in type nor a declaration, which is what the report below says.
        if (declaredEnum is null
            && (!context.IsValidTypeName(argument.Name)
                || argument.Name is "enum" or "foreign"))
        {
            diagnostics.Error(argument.Location, Message.Of(
                SchemaMessages.TypeUnknown,
                ("Struct", declared.Name), ("Member", member.Name), ("Type", argument.Name)));

            return false;
        }

        if (!isKey)
            return true;

        var keyType = declaredEnum is not null
            ? ValueType.Enum
            : context.ParseValueType(argument.Name, argument.Location);

        if (KeyTypes.Contains(keyType))
            return true;

        diagnostics.Error(argument.Location, Message.Of(
            SchemaMessages.MapKeyTypeNotAllowed,
            ("Struct", declared.Name),
            ("Member", member.Name),
            ("Type", argument.Name),
            ("Allowed", AllowedKeyTypes())));

        return false;
    }

    /// <summary>What a key may be, spelled as a sheet spells it, for the report.</summary>
    private static string AllowedKeyTypes()
        => "`int` · `bigint` · `string` · `bool` · `uuid` · enum";

    /// <summary>
    /// The one column a `set` is, or null when the type is not a set.
    /// </summary>
    /// <remarks>
    /// A set is its element type as an array and nothing else - the distinctness check and
    /// the generated container are what make it a set, and neither of those is the column.
    /// </remarks>
    public static SchemaTypeRef? ColumnOfSet(SchemaTypeRef type)
    {
        if (KindOf(type.Name) != ContainerKind.Set || type.Arguments.Count != 1)
            return null;

        var argument = type.Arguments[0];

        return new SchemaTypeRef
        {
            Location = argument.Location,
            Form = argument.Form,
            Name = argument.Name,
            ForeignTables = argument.ForeignTables,
            Arguments = argument.Arguments,
            Meta = argument.Meta,
            IsArray = true,
            // The `?` on `set<int>?` is about whether the row has a set at all, which is
            // exactly what it means on `int[]?`.
            IsOptional = type.IsOptional,
            ElementsAreOptional = false,
        };
    }

    /// <summary>
    /// The member one level below a container: `Key` or `Value` for a map, nothing for a set.
    /// </summary>
    /// <remarks>
    /// **Synthesised rather than declared, because nobody wrote it.** A `map&lt;int,int&gt;` is two
    /// columns and the author named neither - so the two names come from here, and the type
    /// each carries is the argument as an array, which is what a column holding every entry's
    /// key is.
    ///
    /// A set has no level below it at all: its one column is the container itself, so a path
    /// that goes further has named something the declaration does not have.
    /// </remarks>
    public static SchemaField? SlotOf(SchemaField member, string name)
    {
        var type = member.Type;

        if (KindOf(type.Name) != ContainerKind.Map || type.Arguments.Count != 2)
            return null;

        var argument =
            name == KeyMember ? type.Arguments[0]
            : name == ValueMember ? type.Arguments[1]
            : null;

        if (argument is null)
            return null;

        return new SchemaField
        {
            Name = name,
            Location = argument.Location,
            // The declaration's description belongs to the member, not to one of the two
            // columns it became. Repeating it on both would put the same sentence on `Key`
            // and on `Value` in every generated language.
            Comment = "",
            Meta = argument.Meta,
            Type = new SchemaTypeRef
            {
                Location = argument.Location,
                Form = argument.Form,
                Name = argument.Name,
                ForeignTables = argument.ForeignTables,
                Arguments = argument.Arguments,
                // Every entry's key in one column, which is what makes the file need no
                // change: a map is two array columns of equal length.
                IsArray = true,
                // The container's own `?` is about the whole value and stays on the member.
                // An argument may not carry one - `map<K,V?>` is refused - so there is
                // nothing here to carry down.
                IsOptional = false,
                ElementsAreOptional = false,
            },
        };
    }
}
