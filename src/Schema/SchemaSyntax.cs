using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// What one schema file declared.
/// </summary>
/// <remarks>
/// **This is not an intermediate representation.** <see cref="Models.Model"/> is, and these
/// classes hold what was written in a file for exactly as long as it takes to put it there -
/// which is section 10 of the design. Nothing downstream of the cooker sees a type from this
/// namespace.
///
/// So the members below are what the notation says, not what it means: a type is the words
/// that spelled it, a wire tag is zero when none was written, and a default value is the
/// literal as typed. Resolving any of that needs every file open at once, and the pass that
/// has them all is the one that does it.
/// </remarks>
public sealed class SchemaFile
{
    /// <summary>The file this was read from, as the recipe reached it.</summary>
    public required string Path { get; init; }

    /// <summary>Structs, in the order they were declared.</summary>
    public List<SchemaStruct> Structs { get; } = [];

    /// <summary>Enums, in the order they were declared.</summary>
    public List<SchemaEnum> Enums { get; } = [];
}

/// <summary>
/// Whatever a declaration has in common with the others: a name, a description, metadata.
/// </summary>
public abstract class SchemaDeclaration
{
    /// <summary>The name as written.</summary>
    public required string Name { get; init; }

    /// <summary>Where the name was written.</summary>
    public required Location Location { get; init; }

    /// <summary>
    /// The `///` block in front of this declaration, lines joined by newlines. Empty when
    /// there was none.
    /// </summary>
    public string Comment { get; init; } = "";

    /// <summary>What the brackets on the declaration said.</summary>
    public SchemaMeta Meta { get; init; } = SchemaMeta.Empty;
}

/// <summary>An embedded object type - what a sheet's type cell names to use it.</summary>
public sealed class SchemaStruct : SchemaDeclaration
{
    /// <summary>Members, in the order they were declared.</summary>
    /// <remarks>
    /// The order is not decoration. A struct whose members carry no wire tag takes its tags
    /// from this order, so moving a line moves what the file says - section 4.5 of the design
    /// and the reason <see cref="TagsAreWritten"/> exists to be asked about.
    /// </remarks>
    public List<SchemaField> Fields { get; } = [];

    /// <summary>
    /// Whether the members carry wire tags of their own.
    /// </summary>
    /// <remarks>
    /// All or none, which the parser enforces: a struct where some members are tagged and
    /// some are not has two answers to "which member is number three" and no way to tell
    /// which one a reader used.
    /// </remarks>
    public bool TagsAreWritten => Fields.Count > 0 && Fields[0].WireTag > 0;

    /// <summary>The members that carry data, tombstones left out.</summary>
    public IEnumerable<SchemaField> LiveFields => Fields.Where(member => !member.IsRemoved);

    /// <summary>
    /// The tag a member's values travel under.
    /// </summary>
    /// <remarks>
    /// The one written, or the member's position when the struct writes none - counted over
    /// every member including the tombstones, because a tombstone exists precisely to hold a
    /// position nothing else may take. Section 4.5 of the design.
    /// </remarks>
    public int TagOf(SchemaField member)
        => member.WireTag > 0 ? member.WireTag : Fields.IndexOf(member) + 1;
}

/// <summary>One member of a struct.</summary>
public sealed class SchemaField : SchemaDeclaration
{
    /// <summary>The type as written.</summary>
    public required SchemaTypeRef Type { get; init; }

    /// <summary>
    /// The tag this member's values travel under, or zero when the declaration wrote none.
    /// </summary>
    /// <remarks>
    /// Written after the name rather than after the type, which is where a sheet writes it -
    /// `index@1`. Section 4.5 of the design says why: after the type it would sit between the
    /// array markers and the default value, where nobody can see which of the three it
    /// belongs to.
    /// </remarks>
    public int WireTag { get; init; }

    /// <summary>The default value as written, or null when there was none.</summary>
    /// <remarks>
    /// Kept as text. What it means depends on the type, and the type is not resolved yet.
    /// </remarks>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Whether this member is a gravestone - a name and a tag kept so that neither is
    /// handed to something else.
    /// </summary>
    /// <remarks>
    /// The same thing a sheet says with `#name@N`. A reader that was built before the member
    /// was dropped still asks for that tag, and a new member given it would be answered with
    /// the old member's values.
    /// </remarks>
    public bool IsRemoved => Meta.Has("removed");
}

/// <summary>An enumeration.</summary>
public sealed class SchemaEnum : SchemaDeclaration
{
    /// <summary>Entries, in the order they were declared.</summary>
    public List<SchemaEnumValue> Values { get; } = [];
}

/// <summary>One entry of an enumeration.</summary>
public sealed class SchemaEnumValue : SchemaDeclaration
{
    /// <summary>The number this entry carries, or null when the declaration wrote none.</summary>
    public long? Number { get; init; }
}

/// <summary>
/// A type as the notation spelled it.
/// </summary>
/// <remarks>
/// Unresolved on purpose: <see cref="Name"/> is a word, and whether that word is a built-in
/// type, a struct declared three files away or a misspelling is not a question one file can
/// answer. Section 4.6 of the design - declarations resolve after every file is read, the
/// same way tables already do.
/// </remarks>
public sealed class SchemaTypeRef
{
    /// <summary>Where the type was written.</summary>
    public required Location Location { get; init; }

    /// <summary>What kind of type reference this is.</summary>
    public required SchemaTypeForm Form { get; init; }

    /// <summary>
    /// The element type's name for <see cref="SchemaTypeForm.Named"/>, and the container's
    /// own name - `set`, `map` - for the container forms. Empty for a reference.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The tables a <see cref="SchemaTypeForm.Foreign"/> value may be a row of, in the order
    /// they were named. Empty for every other form.
    /// </summary>
    public IReadOnlyList<string> ForeignTables { get; init; } = [];

    /// <summary>
    /// The arguments a container type was given: one for `set`, two for `map`, in order.
    /// </summary>
    public IReadOnlyList<SchemaTypeRef> Arguments { get; init; } = [];

    /// <summary>Whether the type was written as an array.</summary>
    public bool IsArray { get; init; }

    /// <summary>
    /// Whether a row may leave this whole value out - the `?` after the brackets, or the only
    /// `?` when the type is not an array.
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// Whether one element may be absent - the `?` before the brackets. False when the type
    /// is not an array, where there is nothing for it to say.
    /// </summary>
    public bool ElementsAreOptional { get; init; }

    /// <summary>The notation this was read from, rebuilt.</summary>
    public override string ToString()
    {
        string written = Form switch
        {
            SchemaTypeForm.Foreign => "foreign " + string.Join("|", ForeignTables),
            SchemaTypeForm.Container =>
                Name + "<" + string.Join(",", Arguments.Select(a => a.ToString())) + ">",
            _ => Name,
        };

        if (ElementsAreOptional)
            written += "?";

        if (IsArray)
            written += "[]";

        if (IsOptional)
            written += "?";

        return written;
    }
}

/// <summary>Which of the three shapes a type reference was written in.</summary>
public enum SchemaTypeForm
{
    /// <summary>A single name: a built-in type, a struct, or an enum.</summary>
    Named,

    /// <summary>`foreign Table` or `foreign A|B` - a row of one of the tables named.</summary>
    Foreign,

    /// <summary>`set&lt;T&gt;` or `map&lt;K,V&gt;`.</summary>
    Container,
}
