using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Lsp;

/// <summary>
/// What may be written next, worked out from the line being typed.
/// </summary>
/// <remarks>
/// **From the line's text, not from the syntax tree.** A line halfway through being typed is
/// usually not a line the parser accepts, and the tree that exists is of whatever was there
/// before the last keystroke. What decides the answer here is which words are already on this
/// line, which is a question the text can be asked directly.
///
/// **The names offered come from the tables that decide them** - the scalar names from
/// <see cref="ScalarTypes"/>, the composites from <see cref="CompositeTypes"/>, the metadata
/// keys from <see cref="SchemaMetadata"/>. Nothing is listed here twice, so a name added to
/// the notation is offered without this file being touched.
///
/// Being approximately right is the standard. An offer nobody wanted is closed with a
/// keystroke, which is not what a wrong diagnostic costs.
/// </remarks>
internal static class SchemaCompletion
{
    // The numbers the protocol gives the icons beside each entry.
    private const int Class = 7;
    private const int Property = 10;
    private const int Enum = 13;
    private const int Keyword = 14;
    private const int EnumMember = 20;
    private const int Struct = 22;

    /// <summary>The words that begin a line, which is the whole of the notation's grammar.</summary>
    private static readonly string[] Openers =
        ["struct", "abstract", "field", "enum", "value"];

    public static IReadOnlyList<LspCompletionItem> For(
        string line, int character, SchemaDeclarations declarations)
    {
        string before = line[..Math.Min(Math.Max(character, 0), line.Length)];

        // Inside brackets that have not been closed: whatever this line may be annotated with.
        if (before.LastIndexOf('(') > before.LastIndexOf(')'))
            return MetadataKeys(before);

        var written = Written(before);

        if (written.Count == 0)
            return Openers.Select(word => Item(word, Keyword)).ToList();

        if (written[^1] == "extends")
            return AbstractStructs(declarations);

        return written[0] switch
        {
            // A name has been written, so the only word that may follow is what it extends.
            "struct" or "abstract" =>
                written.Count >= 2 ? [Item("extends", Keyword)] : [],

            "field" => AfterMemberName(written, declarations),

            _ => [],
        };
    }

    /// <summary>
    /// The words already finished on this line.
    /// </summary>
    /// <remarks>
    /// The word the cursor is inside is dropped: it is what the editor filters the offers by,
    /// not one of the words that decide which offers those are. `field grade E` asks the same
    /// question as `field grade `.
    /// </remarks>
    private static List<string> Written(string before)
    {
        var words = before.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();

        if (words.Count > 0 && before.Length > 0 && before[^1] is not ' ' and not '\t')
            words.RemoveAt(words.Count - 1);

        return words;
    }

    /// <summary>`field name [@N] type [= value]` - which of those is being written.</summary>
    private static IReadOnlyList<LspCompletionItem> AfterMemberName(
        List<string> written, SchemaDeclarations declarations)
    {
        // The member's own name is being typed, and nobody can offer that.
        if (written.Count < 2)
            return [];

        int equals = written.IndexOf("=");

        if (equals >= 0)
            return DefaultValues(written, equals, declarations);

        // The wire tag may sit between the name and the type, and says nothing about which of
        // them has been written.
        bool typeIsWritten = written.Skip(2).Any(word => !word.StartsWith('@'));

        return typeIsWritten ? [] : TypeNames(declarations);
    }

    /// <summary>
    /// The entries of the enum a member is typed with, for the value after `=`.
    /// </summary>
    /// <remarks>
    /// Only an enum answers this. What a default may be for every other type is a literal, and
    /// there is no list of those to offer.
    /// </remarks>
    private static IReadOnlyList<LspCompletionItem> DefaultValues(
        List<string> written, int equals, SchemaDeclarations declarations)
    {
        for (int at = equals - 1; at >= 2; at--)
        {
            if (written[at].StartsWith('@'))
                continue;

            var declared = declarations.FindEnum(BareTypeName(written[at]));

            return declared is null
                ? []
                : declared.Values
                    .Where(entry => !entry.Meta.Has("removed"))
                    .Select(entry => Item(entry.Name, EnumMember, declared.Name, entry.Comment))
                    .ToList();
        }

        return [];
    }

    /// <summary>Everything a member may be typed with.</summary>
    private static IReadOnlyList<LspCompletionItem> TypeNames(SchemaDeclarations declarations)
    {
        var offered = new List<LspCompletionItem>();

        foreach (string name in ScalarTypes.ByName.Keys)
            offered.Add(Item(name, Class));

        foreach (var composite in CompositeTypes.All)
            offered.Add(Item(composite.Name, Class, string.Join(" · ", composite.Components)));

        offered.Add(Item("set", Class, "set<T>"));
        offered.Add(Item("map", Class, "map<K,V>"));
        offered.Add(Item("foreign", Keyword, "foreign Table"));

        // An abstract struct names a set of variants rather than a shape a value may hold, so
        // it is not something a member is typed with.
        foreach (var declared in declarations.Structs.Values.Where(one => !one.IsAbstract))
            offered.Add(Item(declared.Name, Struct, "struct", declared.Comment));

        foreach (var declared in declarations.Enums.Values)
            offered.Add(Item(declared.Name, Enum, "enum", declared.Comment));

        return offered;
    }

    private static IReadOnlyList<LspCompletionItem> AbstractStructs(SchemaDeclarations declarations)
        => declarations.Structs.Values
            .Where(declared => declared.IsAbstract)
            .Select(declared => Item(declared.Name, Struct, "abstract struct", declared.Comment))
            .ToList();

    /// <summary>The keys the declaration on this line may carry.</summary>
    private static IReadOnlyList<LspCompletionItem> MetadataKeys(string before)
    {
        var words = before.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        string opener = words.Length > 0 ? words[0] : "";

        var keys = opener switch
        {
            "struct" or "abstract" => SchemaMetadata.CarriedOnStruct,

            // A container's argument carries a member's keys too - the brackets there are
            // written on the element, which is a value like any other.
            "field" => SchemaMetadata.CarriedOnField,

            "value" => SchemaMetadata.CarriedOnEnumValue,
            _ => [],
        };

        return keys.Select(key => Item(key, Property)).ToList();
    }

    /// <summary>A type as written, with the markers that are not part of its name taken off.</summary>
    private static string BareTypeName(string written)
    {
        int bracket = written.IndexOf('(');

        if (bracket >= 0)
            written = written[..bracket];

        return written.Replace("[]", "").Replace("?", "");
    }

    private static LspCompletionItem Item(
        string label, int kind, string? detail = null, string comment = "")
        => new()
        {
            Label = label,
            Kind = kind,
            Detail = detail,
            Documentation = comment.Length == 0 ? null : new MarkupContent("markdown", comment),
        };
}
