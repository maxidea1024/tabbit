using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Tabbit.Messages;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// Reads one schema file into what it declared.
/// </summary>
/// <remarks>
/// **Written by hand, and the grammar is why.** A declaration is a line, the first word of a
/// line says which of five it is, and nothing needs more than the token in front of it -
/// there is no lookahead here and no backtracking. A generator would buy a parse table for
/// that and charge a run-time dependency for it. Section 10 of the design.
///
/// **Indentation means nothing.** A `field` joins the `struct` above it because it is below
/// it, not because it is indented under it, and the examples in the design are indented
/// purely to be read. That is one rule fewer to get wrong, and it has a cost written down in
/// section 9.3: a mistyped `struct` line silently gives its members to the struct before it.
/// The answer to that is a lint that compares indentation against the grouping, not a change
/// here.
///
/// **Everything is reported and nothing is thrown.** A file with six mistakes should say all
/// six, so a line that will not parse is abandoned at its end and the next one is read.
/// </remarks>
public static class SchemaParser
{
    /// <summary>What a name has to look like, in this notation and in every language it reaches.</summary>
    private static readonly Regex Identifier =
        new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>The same, with dots allowed, which is what a metadata key may be.</summary>
    private static readonly Regex MetaKey =
        new Regex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);

    /// <summary>Reads a file's text into its declarations.</summary>
    public static SchemaFile Parse(string source, string path, Diagnostics diagnostics)
    {
        var tokens = SchemaLexer.Read(source, path, diagnostics);
        return new Reader(tokens, path, diagnostics).ReadFile();
    }

    private sealed class Reader
    {
        private readonly List<SchemaToken> _tokens;
        private readonly string _path;
        private readonly Diagnostics _diagnostics;
        private int _at;

        public Reader(List<SchemaToken> tokens, string path, Diagnostics diagnostics)
        {
            _tokens = tokens;
            _path = path;
            _diagnostics = diagnostics;
        }

        private SchemaToken Current => _tokens[_at];

        private void Advance() => _at++;

        private Location Where(SchemaToken token)
            => Location.OfTextFile(_path, token.Line, token.Column);

        public SchemaFile ReadFile()
        {
            var file = new SchemaFile { Path = _path };

            var comment = new List<string>();
            SchemaToken commentStart = default;

            SchemaStruct? openStruct = null;
            SchemaEnum? openEnum = null;

            while (Current.Kind != SchemaTokenKind.EndOfFile)
            {
                if (Current.Kind == SchemaTokenKind.DocComment)
                {
                    if (comment.Count == 0)
                        commentStart = Current;

                    comment.Add(Current.Text);
                    Advance();
                    EndLine();
                    continue;
                }

                if (Current.Kind == SchemaTokenKind.EndOfLine)
                {
                    // A blank line does not detach a description from what follows it. The
                    // block belongs to the next declaration, and how far away that is written
                    // is the author's spacing.
                    Advance();
                    continue;
                }

                if (Current.Kind != SchemaTokenKind.Word)
                {
                    Report(SchemaMessages.UnknownKeyword, Current, ("Written", Current.ToString()));
                    SkipLine();
                    continue;
                }

                string keyword = Current.Text;
                var keywordToken = Current;

                switch (keyword)
                {
                    case "struct":
                    case "abstract":
                    {
                        Close(openStruct);
                        Close(openEnum);
                        openEnum = null;

                        openStruct = ReadStruct(Taken(comment), isAbstract: keyword == "abstract");
                        if (openStruct is not null)
                            file.Structs.Add(openStruct);

                        break;
                    }

                    case "enum":
                    {
                        Close(openStruct);
                        Close(openEnum);
                        openStruct = null;

                        openEnum = ReadEnum(Taken(comment));
                        if (openEnum is not null)
                            file.Enums.Add(openEnum);

                        break;
                    }

                    case "field":
                    {
                        var member = ReadField(Taken(comment));

                        // Null is a line that already reported why it would not parse. It is
                        // still a `field` line, so the question of what it would have joined
                        // is not asked again.
                        if (openStruct is not null)
                        {
                            if (member is not null)
                                openStruct.Fields.Add(member);
                        }
                        else if (openEnum is not null)
                            Report(SchemaMessages.FieldInEnum, keywordToken, ("Enum", openEnum.Name));
                        else
                            Report(SchemaMessages.FieldOutsideStruct, keywordToken);

                        break;
                    }

                    case "value":
                    {
                        var entry = ReadEnumValue(Taken(comment));

                        if (openEnum is not null)
                        {
                            if (entry is not null)
                                openEnum.Values.Add(entry);
                        }
                        else if (openStruct is not null)
                            Report(SchemaMessages.ValueInStruct, keywordToken, ("Struct", openStruct.Name));
                        else
                            Report(SchemaMessages.ValueOutsideEnum, keywordToken);

                        break;
                    }

                    case "extends":
                    {
                        // A line of its own rather than the tail of a `struct` line, which is
                        // the one place it means anything. Reported by name because the word
                        // does exist here, and an unknown-keyword report would send somebody
                        // looking for a spelling mistake instead of a line break.
                        Report(SchemaMessages.ExtendsOnStructLine, keywordToken);
                        comment.Clear();
                        SkipLine();
                        break;
                    }

                    default:
                    {
                        Report(SchemaMessages.UnknownKeyword, keywordToken, ("Written", keyword));
                        comment.Clear();
                        SkipLine();
                        break;
                    }
                }
            }

            Close(openStruct);
            Close(openEnum);

            if (comment.Count > 0)
                Report(SchemaMessages.DocCommentAttachedToNothing, commentStart);

            return file;
        }

        /// <summary>Takes the description block collected so far, leaving none behind.</summary>
        private static string Taken(List<string> comment)
        {
            string text = string.Join("\n", comment);
            comment.Clear();
            return text;
        }

        /// <summary>
        /// Checks what a struct can only be checked for once every member has been read.
        /// </summary>
        private void Close(SchemaStruct? declared)
        {
            if (declared is null || declared.Fields.Count == 0)
                return;

            var seen = new Dictionary<string, SchemaField>(System.StringComparer.Ordinal);
            foreach (var field in declared.Fields)
            {
                if (seen.TryGetValue(field.Name, out var first))
                {
                    _diagnostics.Error(field.Location, Message.Of(
                        SchemaMessages.MemberDuplicate,
                        ("Struct", declared.Name), ("Member", field.Name), ("First", first.Location)));
                    continue;
                }

                seen[field.Name] = field;
            }

            // All or none. A struct where three members are tagged and one is not has no
            // answer to what that one's number is: counting the untagged ones by position
            // would collide with the tags somebody wrote, and skipping them would make a
            // member's number depend on how many members before it happened to be tagged.
            bool anyTagged = declared.Fields.Any(field => field.WireTag > 0);
            if (anyTagged)
            {
                foreach (var field in declared.Fields.Where(field => field.WireTag <= 0))
                {
                    _diagnostics.Error(field.Location, Message.Of(
                        SchemaMessages.WireTagPartial,
                        ("Struct", declared.Name), ("Member", field.Name)));
                }

                var byTag = new Dictionary<int, SchemaField>();
                foreach (var field in declared.Fields.Where(field => field.WireTag > 0))
                {
                    if (byTag.TryGetValue(field.WireTag, out var first))
                    {
                        _diagnostics.Error(field.Location, Message.Of(
                            SchemaMessages.WireTagDuplicate,
                            ("Struct", declared.Name),
                            ("Member", field.Name),
                            ("Tag", field.WireTag),
                            ("First", first.Name)));
                        continue;
                    }

                    byTag[field.WireTag] = field;
                }
            }
        }

        /// <summary>
        /// Checks what an enum can only be checked for once every entry has been read.
        /// </summary>
        /// <remarks>
        /// Only the numbers somebody wrote are compared. What an entry with no number carries
        /// is settled where the declarations are resolved, and two entries colliding there is
        /// a report from that pass rather than from this one.
        /// </remarks>
        private void Close(SchemaEnum? declared)
        {
            if (declared is null)
                return;

            var byName = new Dictionary<string, SchemaEnumValue>(System.StringComparer.Ordinal);
            var byNumber = new Dictionary<long, SchemaEnumValue>();

            foreach (var entry in declared.Values)
            {
                if (byName.TryGetValue(entry.Name, out var sameName))
                {
                    _diagnostics.Error(entry.Location, Message.Of(
                        SchemaMessages.EnumValueDuplicate,
                        ("Enum", declared.Name), ("Entry", entry.Name), ("First", sameName.Location)));
                }
                else
                {
                    byName[entry.Name] = entry;
                }

                if (entry.Number is not { } number)
                    continue;

                if (byNumber.TryGetValue(number, out var sameNumber))
                {
                    _diagnostics.Error(entry.Location, Message.Of(
                        SchemaMessages.EnumNumberDuplicate,
                        ("Enum", declared.Name),
                        ("Entry", entry.Name),
                        ("Number", number),
                        ("First", sameNumber.Name)));
                    continue;
                }

                byNumber[number] = entry;
            }
        }

        /// <summary>
        /// Reads `struct X`, and the three things polymorphism adds to that line.
        /// </summary>
        /// <remarks>
        /// `abstract struct X` · `struct X extends Base` · `struct X extends Base @2`, in that
        /// order and no other. The order is not a preference: `extends` says which set this
        /// joins and `@N` says which member of that set it is, so the tag has nothing to
        /// attach to before the base is named.
        ///
        /// **One level, and the grammar is what enforces it.** `abstract` and `extends` on one
        /// line is refused here rather than in the resolver, because a variant that is itself
        /// abstract leaves a sheet's `:type` cell with two answers to "what shape is this row" -
        /// the leaf and the layer above it. spec/polymorphism.md section 5.1.
        /// </remarks>
        private SchemaStruct? ReadStruct(string comment, bool isAbstract)
        {
            var keyword = Current;
            Advance();

            if (isAbstract)
            {
                if (Current.Kind != SchemaTokenKind.Word || Current.Text != "struct")
                {
                    Report(SchemaMessages.AbstractNeedsStruct, keyword,
                        ("Written", Current.ToString()));
                    SkipLine();
                    return null;
                }

                Advance();
            }

            var name = ReadName();
            if (name is null)
            {
                SkipLine();
                return null;
            }

            string? baseName = null;
            Location? baseAt = null;

            if (Current.Kind == SchemaTokenKind.Word && Current.Text == "extends")
            {
                var extends = Current;
                Advance();

                var written = ReadName();
                if (written is null)
                {
                    SkipLine();
                    return null;
                }

                if (isAbstract)
                {
                    Report(SchemaMessages.AbstractCannotExtend, extends,
                        ("Struct", name.Value.Text), ("Base", written.Value.Text));
                    SkipLine();
                    return null;
                }

                baseName = written.Value.Text;
                baseAt = Where(written.Value);
            }

            var tagAt = Current;
            int tag = ReadWireTag();

            // A struct that joins no set has nothing for a discriminator to tell apart, so a
            // number on it would be a number nothing reads.
            if (tag > 0 && baseName is null)
            {
                Report(SchemaMessages.VariantTagWithoutBase, tagAt,
                    ("Struct", name.Value.Text), ("Tag", tag));
                tag = 0;
            }

            var declared = new SchemaStruct
            {
                Name = name.Value.Text,
                Location = Where(name.Value),
                Comment = comment,
                IsAbstract = isAbstract,
                BaseName = baseName,
                BaseLocation = baseAt,
                VariantTag = tag,
                Meta = ReadMeta(),
            };

            EndLine();
            return declared;
        }

        private SchemaEnum? ReadEnum(string comment)
        {
            Advance();

            var name = ReadName();
            if (name is null)
            {
                SkipLine();
                return null;
            }

            var declared = new SchemaEnum
            {
                Name = name.Value.Text,
                Location = Where(name.Value),
                Comment = comment,
                Meta = ReadMeta(),
            };

            EndLine();
            return declared;
        }

        private SchemaField? ReadField(string comment)
        {
            Advance();

            var name = ReadName();
            if (name is null)
            {
                SkipLine();
                return null;
            }

            int tag = ReadWireTag();

            var type = ReadType();
            if (type is null)
            {
                SkipLine();
                return null;
            }

            string? given = null;
            if (Current.Kind == SchemaTokenKind.Equals)
            {
                var equals = Current;
                Advance();

                given = ReadBareValue();
                if (given is null)
                {
                    Report(SchemaMessages.DefaultValueExpected, equals, ("Member", name.Value.Text));
                    SkipLine();
                    return null;
                }
            }

            var field = new SchemaField
            {
                Name = name.Value.Text,
                Location = Where(name.Value),
                Comment = comment,
                Type = type,
                WireTag = tag,
                DefaultValue = given,
                Meta = ReadMeta(),
            };

            RefuseCommentKey(field.Meta);
            EndLine();
            return field;
        }

        private SchemaEnumValue? ReadEnumValue(string comment)
        {
            Advance();

            var name = ReadName();
            if (name is null)
            {
                SkipLine();
                return null;
            }

            long? number = null;
            if (Current.Kind == SchemaTokenKind.Equals)
            {
                Advance();

                string? written = ReadBareValue();
                if (written is null || !long.TryParse(
                        written, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
                {
                    Report(SchemaMessages.EnumValueNotANumber, name.Value,
                        ("Entry", name.Value.Text), ("Written", written ?? ""));
                    SkipLine();
                    return null;
                }

                number = value;
            }

            var declared = new SchemaEnumValue
            {
                Name = name.Value.Text,
                Location = Where(name.Value),
                Comment = comment,
                Number = number,
                Meta = ReadMeta(),
            };

            RefuseCommentKey(declared.Meta);
            EndLine();
            return declared;
        }

        /// <summary>Reads the name after a keyword, checking it is spelled like one.</summary>
        private SchemaToken? ReadName()
        {
            if (Current.Kind != SchemaTokenKind.Word)
            {
                Report(SchemaMessages.NameExpected, Current, ("Written", Current.ToString()));
                return null;
            }

            var token = Current;
            Advance();

            if (!Identifier.IsMatch(token.Text))
            {
                Report(SchemaMessages.NameNotIdentifier, token, ("Written", token.Text));
                return null;
            }

            return token;
        }

        /// <summary>Reads `@N` if it is there, and zero if it is not.</summary>
        private int ReadWireTag()
        {
            if (Current.Kind != SchemaTokenKind.At)
                return 0;

            var marker = Current;
            Advance();

            if (Current.Kind != SchemaTokenKind.Word
                || !int.TryParse(Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int tag))
            {
                Report(SchemaMessages.WireTagNotANumber, marker, ("Written", Current.ToString()));

                if (Current.Kind == SchemaTokenKind.Word)
                    Advance();

                return 0;
            }

            Advance();

            if (tag <= 0)
            {
                Report(SchemaMessages.WireTagNotPositive, marker, ("Tag", tag));
                return 0;
            }

            return tag;
        }

        /// <summary>
        /// Reads a type as it was written, resolving nothing.
        /// </summary>
        /// <remarks>
        /// The arity of a container is not checked here and neither is the name in front of
        /// its brackets. Both are questions about what the name means, and the parser reading
        /// them would put the list of container types in this file - where adding one would
        /// mean editing the parser rather than the resolver that knows about types.
        /// </remarks>
        private SchemaTypeRef? ReadType()
        {
            if (Current.Kind != SchemaTokenKind.Word)
            {
                Report(SchemaMessages.TypeExpected, Current, ("Written", Current.ToString()));
                return null;
            }

            var start = Current;

            var form = SchemaTypeForm.Named;
            string name = start.Text;
            var targets = new List<string>();
            var arguments = new List<SchemaTypeRef>();

            if (name == "foreign")
            {
                Advance();
                form = SchemaTypeForm.Foreign;
                name = "";

                while (true)
                {
                    var target = ReadName();
                    if (target is null)
                        return null;

                    targets.Add(target.Value.Text);

                    if (Current.Kind != SchemaTokenKind.Pipe)
                        break;

                    Advance();
                }

                if (targets.Count == 0)
                {
                    Report(SchemaMessages.ForeignTargetExpected, start);
                    return null;
                }
            }
            else
            {
                Advance();

                if (Current.Kind == SchemaTokenKind.OpenAngle)
                {
                    Advance();
                    form = SchemaTypeForm.Container;

                    while (true)
                    {
                        var argument = ReadType();
                        if (argument is null)
                            return null;

                        arguments.Add(argument);

                        if (Current.Kind != SchemaTokenKind.Comma)
                            break;

                        Advance();
                    }

                    if (Current.Kind != SchemaTokenKind.CloseAngle)
                    {
                        Report(SchemaMessages.UnexpectedToken, Current,
                            ("Written", Current.ToString()), ("Expected", ">"));
                        return null;
                    }

                    Advance();
                }
            }

            // The first `?` is the element's and the second is the whole value's, which is
            // what makes `int?[]` and `int[]?` two different statements. A type with no
            // brackets has one place to put a `?` and it means the value.
            bool firstQuestion = Take(SchemaTokenKind.Question);

            bool array = false;
            if (Current.Kind == SchemaTokenKind.OpenBracket)
            {
                var bracket = Current;
                Advance();

                if (Current.Kind != SchemaTokenKind.CloseBracket)
                {
                    Report(SchemaMessages.UnexpectedToken, Current,
                        ("Written", Current.ToString()), ("Expected", "]"));
                    return null;
                }

                Advance();
                array = true;

                if (Current.Kind == SchemaTokenKind.OpenBracket
                    || (Current.Kind == SchemaTokenKind.Question
                        && _tokens[_at + 1].Kind == SchemaTokenKind.OpenBracket))
                {
                    Report(SchemaMessages.TypeNestedArray, bracket);
                    return null;
                }
            }

            bool secondQuestion = array && Take(SchemaTokenKind.Question);

            return new SchemaTypeRef
            {
                Location = Where(start),
                Form = form,
                Name = name,
                ForeignTables = targets,
                Arguments = arguments,
                IsArray = array,
                ElementsAreOptional = array && firstQuestion,
                IsOptional = array ? secondQuestion : firstQuestion,
            };
        }

        /// <summary>Reads `( … )` if it is there, and nothing if it is not.</summary>
        private SchemaMeta ReadMeta()
        {
            if (Current.Kind != SchemaTokenKind.OpenParen)
                return SchemaMeta.Empty;

            var open = Current;
            Advance();

            var entries = new List<SchemaMetaEntry>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            if (Current.Kind == SchemaTokenKind.CloseParen)
            {
                Advance();
                return SchemaMeta.Empty;
            }

            while (true)
            {
                if (Current.Kind != SchemaTokenKind.Word || !MetaKey.IsMatch(Current.Text))
                {
                    Report(SchemaMessages.MetaKeyExpected, Current, ("Written", Current.ToString()));
                    SkipToCloseParen();
                    return new SchemaMeta(entries);
                }

                var key = Current;
                Advance();

                string? value = null;
                if (Current.Kind == SchemaTokenKind.Equals)
                {
                    var equals = Current;
                    Advance();

                    value = ReadBareValue();
                    if (value is null)
                    {
                        Report(SchemaMessages.MetaValueExpected, equals, ("Key", key.Text));
                        SkipToCloseParen();
                        return new SchemaMeta(entries);
                    }
                }

                if (!seen.Add(key.Text))
                    Report(SchemaMessages.MetaDuplicateKey, key, ("Key", key.Text));
                else
                    entries.Add(new SchemaMetaEntry(key.Text, value, Where(key)));

                if (Current.Kind == SchemaTokenKind.Comma)
                {
                    Advance();
                    continue;
                }

                if (Current.Kind == SchemaTokenKind.CloseParen)
                {
                    Advance();
                    break;
                }

                // Two different mistakes, and pointing at the same one for both would send
                // somebody to the end of the line for a stray word in the middle of it.
                if (Current.Kind is SchemaTokenKind.EndOfLine or SchemaTokenKind.EndOfFile)
                {
                    Report(SchemaMessages.MetaUnclosed, open);
                }
                else
                {
                    Report(SchemaMessages.UnexpectedToken, Current,
                        ("Written", Current.ToString()), ("Expected", "`,` or `)`"));
                }

                SkipToCloseParen();
                break;
            }

            return new SchemaMeta(entries);
        }

        /// <summary>
        /// Reads one written value - a quoted string, or everything up to a comma, a closing
        /// bracket or a space.
        /// </summary>
        /// <remarks>
        /// Unquoted values are more than one token here: `A|B` is three and `^[a-z]+$` is
        /// five, because the lexer knows nothing about where in a declaration it stands. They
        /// are put back together only while they were written touching - a space ends the
        /// value, which is what the grammar says a bare value is, and what stops
        /// `(x.note=two words)` from silently becoming one word.
        /// </remarks>
        private string? ReadBareValue()
        {
            if (Current.Kind == SchemaTokenKind.String)
            {
                string quoted = Current.Text;
                Advance();
                return quoted;
            }

            var built = new StringBuilder();
            int endOfLast = -1;

            while (Current.Kind != SchemaTokenKind.Comma
                   && Current.Kind != SchemaTokenKind.CloseParen
                   && Current.Kind != SchemaTokenKind.EndOfLine
                   && Current.Kind != SchemaTokenKind.EndOfFile
                   && Current.Kind != SchemaTokenKind.String
                   && Current.Kind != SchemaTokenKind.DocComment)
            {
                if (endOfLast >= 0 && Current.Column != endOfLast)
                    break;

                built.Append(Current.ToString());
                endOfLast = Current.EndColumn;
                Advance();
            }

            return built.Length == 0 ? null : built.ToString();
        }

        /// <summary>
        /// Refuses `comment=`, and says where a description goes instead.
        /// </summary>
        /// <remarks>
        /// The one metadata key that is an error rather than an unknown key. Section 3 of the
        /// design keeps descriptions to `///` alone - they run to several lines, and a second
        /// way to write one that cannot would be a way that stops working at the length
        /// descriptions actually reach. An unknown-key report would say "nothing reads this",
        /// which is true and unhelpful.
        /// </remarks>
        private void RefuseCommentKey(SchemaMeta meta)
        {
            if (meta.LocationOf("comment") is { } location)
                _diagnostics.Error(location, Message.Of(SchemaMessages.MetaCommentKey));
        }

        private bool Take(SchemaTokenKind kind)
        {
            if (Current.Kind != kind)
                return false;

            Advance();
            return true;
        }

        /// <summary>Ends a declaration, reporting whatever was written past its end.</summary>
        private void EndLine()
        {
            if (Current.Kind == SchemaTokenKind.EndOfLine)
            {
                Advance();
                return;
            }

            if (Current.Kind == SchemaTokenKind.EndOfFile)
                return;

            Report(SchemaMessages.UnexpectedToken, Current,
                ("Written", Current.ToString()), ("Expected", "end of line"));

            SkipLine();
        }

        private void SkipLine()
        {
            while (Current.Kind != SchemaTokenKind.EndOfLine
                   && Current.Kind != SchemaTokenKind.EndOfFile)
                Advance();

            if (Current.Kind == SchemaTokenKind.EndOfLine)
                Advance();
        }

        private void SkipToCloseParen()
        {
            while (Current.Kind != SchemaTokenKind.CloseParen
                   && Current.Kind != SchemaTokenKind.EndOfLine
                   && Current.Kind != SchemaTokenKind.EndOfFile)
                Advance();

            if (Current.Kind == SchemaTokenKind.CloseParen)
                Advance();
        }

        private void Report(string id, SchemaToken token, params (string Name, object? Value)[] values)
            => _diagnostics.Error(Where(token), Message.Of(id, values));
    }
}
