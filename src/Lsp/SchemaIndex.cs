using System.Collections.Generic;
using System.Text;
using Tabbit.Models;
using Tabbit.Schema;

namespace Tabbit.Lsp;

/// <summary>One name written somewhere, and what it is.</summary>
internal sealed record Occurrence
{
    public required string Path { get; init; }

    /// <summary>Counted from zero, as the protocol counts.</summary>
    public required int Line { get; init; }

    public required int Start { get; init; }

    public required int End { get; init; }

    /// <summary>The declaration this name is, when the name is a declaration's own.</summary>
    public SchemaDeclaration? Declares { get; init; }

    /// <summary>The type this name refers to, when it is a use rather than a declaration.</summary>
    public string? Refers { get; init; }

    public LspRange Range => new(new Position(Line, Start), new Position(Line, End));
}

/// <summary>
/// Where every name in a directory's schema files is written, so that pointing at one can be
/// answered.
/// </summary>
/// <remarks>
/// **Built from the syntax, resolved through the declarations.** The two together are what
/// makes "go to this type" answerable: the syntax says a name sits at this line and column,
/// and the declarations say which file declared it.
///
/// **A name is looked up through <see cref="SchemaDeclarations.FindStruct"/> and nothing
/// else.** That table is keyed by the Pascal-cased spelling and matched without case, so any
/// other way of searching it disagrees with it on the first name written in a different case -
/// and disagreeing quietly is worse than not answering.
///
/// The end column is the start plus the length of the name. An identifier has no escapes, so
/// there is nothing here for the lexer to settle.
/// </remarks>
internal sealed class SchemaIndex
{
    private readonly Dictionary<string, List<Occurrence>> _byFile =
        new(System.StringComparer.OrdinalIgnoreCase);

    private readonly SchemaDeclarations _declarations;

    private SchemaIndex(SchemaDeclarations declarations) => _declarations = declarations;

    public static SchemaIndex Build(
        IReadOnlyList<SchemaFile> files, SchemaDeclarations declarations)
    {
        var index = new SchemaIndex(declarations);

        foreach (var file in files)
        {
            foreach (var declared in file.Structs)
            {
                index.Add(Naming(declared));

                if (declared.BaseName is not null && declared.BaseLocation is not null)
                {
                    index.Add(new Occurrence
                    {
                        Path = declared.BaseLocation.Filename,
                        Line = declared.BaseLocation.Row,
                        Start = declared.BaseLocation.Column,
                        End = declared.BaseLocation.Column + declared.BaseName.Length,
                        Refers = declared.BaseName,
                    });
                }

                foreach (var member in declared.Fields)
                {
                    index.Add(Naming(member));
                    index.AddType(member.Type);
                }
            }

            foreach (var declared in file.Enums)
            {
                index.Add(Naming(declared));

                foreach (var entry in declared.Values)
                    index.Add(Naming(entry));
            }
        }

        return index;
    }

    /// <summary>What was written at this position, or nothing.</summary>
    public Occurrence? At(string path, int line, int character)
    {
        if (!_byFile.TryGetValue(path, out var written))
            return null;

        // The end is included, because the caret sits after the last character as often as
        // inside the word - somebody types a name and presses F12 without moving. Nothing is
        // lost by it: every name in this notation has a space or a bracket beside it, so no
        // two of these ever meet.
        foreach (var found in written)
        {
            if (found.Line == line && character >= found.Start && character <= found.End)
                return found;
        }

        return null;
    }

    /// <summary>Where the thing under the cursor was declared.</summary>
    public (string Path, LspRange Range)? DefinitionOf(Occurrence found)
    {
        var declared = found.Declares ?? Resolve(found.Refers);

        if (declared is null)
            return null;

        return (declared.Location.Filename, RangeOf(declared));
    }

    /// <summary>
    /// What to show beside the cursor: the declaration as it was written, and its `///`.
    /// </summary>
    /// <remarks>
    /// **A built-in scalar has no hover, and that is deliberate.** Answering for one would
    /// mean keeping a list of the built-in names here - a third copy after the cooker's and
    /// the highlighting grammar's - to say something nobody needs told about `int`. The
    /// composite types are different only because <see cref="CompositeType"/> can be asked,
    /// so their components come free of any list.
    /// </remarks>
    public string? HoverOf(Occurrence found)
    {
        var declared = found.Declares ?? Resolve(found.Refers);

        if (declared is not null)
        {
            var built = new StringBuilder();

            built.Append("```tbs\n").Append(Signature(declared)).Append("\n```");

            if (declared.Comment.Length > 0)
                built.Append("\n\n").Append(declared.Comment);

            return built.ToString();
        }

        if (found.Refers is null)
            return null;

        var composite = CompositeTypes.BySpelling(found.Refers);

        return composite is null
            ? null
            : $"```tbs\n{composite.Name}\n```\n\n{string.Join(" · ", composite.Components)}";
    }

    /// <summary>
    /// The kinds of name this server can tell apart, in the order the protocol numbers them.
    /// </summary>
    /// <remarks>
    /// **What the highlighting grammar cannot know.** A regex sees a word in type position;
    /// it cannot say whether that word is a struct, an enum, a built-in type or a misspelling.
    /// These do, because they come from the declarations - and a name that matches none of
    /// them is given no token at all, so it is the one word on the line the editor colours
    /// from the grammar alone.
    /// </remarks>
    public static readonly IReadOnlyList<string> TokenTypes =
        ["struct", "enum", "enumMember", "property", "type"];

    public static readonly IReadOnlyList<string> TokenModifiers = ["declaration"];

    /// <summary>
    /// Every name in one file, in the packed form the protocol asks for.
    /// </summary>
    /// <remarks>
    /// Five numbers each - the line and column as a step from the token before, then the
    /// length, the kind, and the modifiers as a bit set. Sorted first, because the steps are
    /// meaningless in any other order.
    /// </remarks>
    public IReadOnlyList<int> TokensFor(string path)
    {
        if (!_byFile.TryGetValue(path, out var written))
            return [];

        var tokens = new List<(int Line, int Start, int Length, int Kind, int Modifiers)>();

        foreach (var found in written)
        {
            int kind = KindOf(found, out int modifiers);

            if (kind >= 0)
                tokens.Add((found.Line, found.Start, found.End - found.Start, kind, modifiers));
        }

        tokens.Sort((left, right) => left.Line != right.Line
            ? left.Line.CompareTo(right.Line)
            : left.Start.CompareTo(right.Start));

        var packed = new List<int>(tokens.Count * 5);
        int line = 0;
        int start = 0;

        foreach (var token in tokens)
        {
            int downBy = token.Line - line;

            packed.Add(downBy);
            packed.Add(downBy == 0 ? token.Start - start : token.Start);
            packed.Add(token.Length);
            packed.Add(token.Kind);
            packed.Add(token.Modifiers);

            line = token.Line;
            start = token.Start;
        }

        return packed;
    }

    /// <summary>Which kind a name is, or -1 for one nothing here recognises.</summary>
    private int KindOf(Occurrence found, out int modifiers)
    {
        modifiers = 0;

        if (found.Declares is not null)
        {
            // The one place a name is introduced rather than used.
            modifiers = 1;

            return found.Declares switch
            {
                SchemaStruct => 0,
                SchemaEnum => 1,
                SchemaEnumValue => 2,
                SchemaField => 3,
                _ => -1,
            };
        }

        switch (Resolve(found.Refers))
        {
            case SchemaStruct: return 0;
            case SchemaEnum: return 1;
        }

        if (found.Refers is null)
            return -1;

        // A built-in name is one this server recognises even though no file declares it. A
        // name that is neither gets nothing, which is the point.
        return ScalarTypes.Has(found.Refers) || CompositeTypes.BySpelling(found.Refers) is not null
            ? 4
            : -1;
    }

    /// <summary>The declaration a written name means, or nothing when no file declares it.</summary>
    private SchemaDeclaration? Resolve(string? written)
        => written is null
            ? null
            : _declarations.FindStruct(written) ?? (SchemaDeclaration?)_declarations.FindEnum(written);

    private void Add(Occurrence found)
    {
        if (!_byFile.TryGetValue(found.Path, out var written))
            _byFile[found.Path] = written = [];

        written.Add(found);
    }

    /// <summary>
    /// Records the names inside a type, and only the ones a file can declare.
    /// </summary>
    /// <remarks>
    /// A container's own name - `set`, `map` - is not a declaration, and the tables a
    /// `foreign` names are answered by a workbook rather than by these files. Section 3 of
    /// spec/ops/lsp.md.
    /// </remarks>
    private void AddType(SchemaTypeRef type)
    {
        if (type.Form == SchemaTypeForm.Named)
        {
            Add(new Occurrence
            {
                Path = type.Location.Filename,
                Line = type.Location.Row,
                Start = type.Location.Column,
                End = type.Location.Column + type.Name.Length,
                Refers = type.Name,
            });
        }

        foreach (var argument in type.Arguments)
            AddType(argument);
    }

    private static Occurrence Naming(SchemaDeclaration declared) => new()
    {
        Path = declared.Location.Filename,
        Line = declared.Location.Row,
        Start = declared.Location.Column,
        End = declared.Location.Column + declared.Name.Length,
        Declares = declared,
    };

    private static LspRange RangeOf(SchemaDeclaration declared)
        => new(new Position(declared.Location.Row, declared.Location.Column),
               new Position(declared.Location.Row,
                            declared.Location.Column + declared.Name.Length));

    /// <summary>The declaration line, rebuilt from what was written.</summary>
    private static string Signature(SchemaDeclaration declared) => declared switch
    {
        SchemaStruct declaring =>
            (declaring.IsAbstract ? "abstract " : "") + "struct " + declaring.Name
            + (declaring.BaseName is null ? "" : " extends " + declaring.BaseName)
            + (declaring.VariantDiscriminator > 0 ? " @" + declaring.VariantDiscriminator : ""),

        SchemaEnum declaring => "enum " + declaring.Name,

        SchemaField member =>
            "field " + member.Name
            + (member.WireTag > 0 ? " @" + member.WireTag : "")
            + " " + member.Type
            + (member.DefaultValue is null ? "" : " = " + member.DefaultValue),

        SchemaEnumValue entry =>
            "value " + entry.Name + (entry.Number is null ? "" : " = " + entry.Number),

        _ => declared.Name,
    };
}
