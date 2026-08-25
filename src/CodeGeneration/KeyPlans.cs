using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.CodeGeneration;

/// <summary>
/// One key a table's rows are looked up by: one column, or several taken together.
/// </summary>
/// <remarks>
/// **The single and the composite are the same thing at different widths**, which is why they
/// share a plan rather than each generator growing a second loop. A language's table view
/// carries one entry per key and its template writes one lookup per entry; what a composite
/// changes is the number of parameters and what the map is keyed by, not the shape of the
/// surface. spec/primary-layout.md section 3.5.
/// </remarks>
internal sealed class KeyPlan
{
    /// <summary>The columns making up the key, in the order the sheet wrote them.</summary>
    public required IReadOnlyList<SerialField> Components { get; init; }

    /// <summary>Whether the key is made of more than one column.</summary>
    public bool IsComposite => Components.Count > 1;

    /// <summary>The one column, for the single-column case that every language already had.</summary>
    public SerialField Only => Components[0];

    /// <summary>
    /// What the lookup names end in, spelled the way the language spells a name.
    /// </summary>
    /// <remarks>
    /// `And` between the parts rather than nothing between them, because `FindByStageSlot`
    /// reads as one column called `stageSlot` and a reader has no way to tell. It is the word
    /// Rails and Spring Data both settled on for the same lookup, so it is also the one a
    /// reader is most likely to have seen before.
    /// </remarks>
    public string Suffix(System.Func<string, string> spell, string joiner)
        => string.Join(joiner, Components.Select(component => spell(component.Name)));
}

/// <summary>
/// The keys of a table, single and composite alike.
/// </summary>
internal static class KeyPlans
{
    /// <summary>
    /// Every key, singles first in column order and composites after in the order declared.
    /// </summary>
    /// <remarks>
    /// Singles first so that a table without a composite key generates exactly what it
    /// generated before this existed - the whole of the notation's cost then falls on the
    /// sheets that use it, and a golden that moves is a sheet that asked for it.
    ///
    /// A component the table has no field for is skipped rather than reported: the sheet has
    /// already been refused by the layout, and a generator is downstream of that.
    /// </remarks>
    public static IReadOnlyList<KeyPlan> Of(Table table)
    {
        var plans = table.SerialFields
                         .Where(field => field.IsIndexer)
                         .Select(field => new KeyPlan { Components = [field] })
                         .ToList();

        foreach (var key in table.Keys.Where(key => key.IsComposite))
        {
            var components = key.FieldNames
                                .Select(name => table.SerialFields.Find(
                                    field => field.Name == name))
                                .ToList();

            if (components.Any(component => component is null))
                continue;

            plans.Add(new KeyPlan { Components = components! });
        }

        return plans;
    }

    /// <summary>
    /// The key the table's rows are identified by, whatever its width.
    /// </summary>
    /// <remarks>
    /// **Not <c>Of(table)[0]</c>.** That list puts single keys first so a table without a
    /// composite one generates what it always did, which means a table whose primary key is
    /// composite and which also declares a single secondary one has the secondary at [0].
    /// Asked for by name here, so nothing has to know the order.
    /// </remarks>
    public static KeyPlan PrimaryOf(Table table)
    {
        var declared = table.Keys.Find(key => key.IsPrimary);

        if (declared is { IsComposite: true })
        {
            var composite = Of(table).FirstOrDefault(
                plan => plan.IsComposite
                        && plan.Components.Count == declared.FieldNames.Count
                        && plan.Components.Select(component => component.Name)
                               .SequenceEqual(declared.FieldNames));

            if (composite is not null)
                return composite;
        }

        return Of(table)[0];
    }
}

/// <summary>
/// One column of a composite key, as a template needs to see it.
/// </summary>
/// <remarks>
/// Shared by every language rather than repeated per generator, because what a template asks
/// of a component is the same questions everywhere - what to call the parameter, how to
/// spell its type, which record member holds it, and which shape of value it is.
/// The answers differ per language; the questions do not.
/// </remarks>
internal sealed class KeyComponentView
{
    /// <summary>What the lookup calls this parameter.</summary>
    public required string Param { get; set; }

    /// <summary>The parameter's type, as the language spells it.</summary>
    public required string Type { get; set; }

    /// <summary>The record member holding this column's value.</summary>
    public required string Member { get; set; }

    /// <summary>
    /// Which shape the value is: `string`, `enum`, `int64`, or `number`.
    /// </summary>
    /// <remarks>
    /// **The map key is built as text, and only these reach it** - a key column is held to
    /// <c>ValueTypes.CanBeIndexKey</c>, which turns away everything else. A template writes
    /// its own spelling for each: a string is its own text, an enum is its underlying number's
    /// text so two labels sharing a value stay one key, and everything else is an integer.
    ///
    /// `int64` is told apart from `number` for one language: under LuaJIT a 64-bit integer is
    /// FFI cdata, whose `tostring` is not the decimal a caller passing a plain number would
    /// produce - so the lookup would miss on a value that is in the table. Everywhere else the
    /// two are written the same way, and a template that does not name `int64` gets the
    /// `number` spelling for it.
    /// </remarks>
    public required string Kind { get; set; }

    /// <summary>
    /// The value type a key component's parameter carries, and its enum when it has one.
    /// </summary>
    /// <remarks>
    /// **A reference component carries the target's key, not the target's row.** The column
    /// is a `foreign`, so its own element type is a row - and a lookup taking rows is a
    /// lookup nobody can call: what a caller has is the id it read from somewhere else, and
    /// what the map is keyed by is the text those ids make. A link table is where a composite
    /// key most often comes from, and both of its columns are references.
    /// spec/reference-surface-naming.md sections 4 and 5.
    ///
    /// The enum comes from the target's primary index for the same reason: a table keyed by
    /// an enum label is referenced by that label.
    /// </remarks>
    public static (Models.ValueType Type, Models.Enum? Enum) TypeOf(SerialField component)
    {
        var field = component.FirstField!;

        return field.IsRef
            ? (field.RefKeyType, field.ResolvedRefTable?.PrimaryIndexField?.EnumOrNull)
            : (field.ElementType, field.EnumOrNull);
    }

    /// <summary>What a component's parameter is called, before the language cases it.</summary>
    /// <remarks>
    /// **`Key` on the end, and not the bare column name.** A single-column lookup has always
    /// taken a parameter called `key`, so `stageKey, slotKey` says the same thing at a second
    /// width - and a column called `Type` or `Range` would otherwise become a parameter named
    /// after a keyword in half these languages. No language reserves a word ending in `Key`,
    /// so one rule covers all of them and none needs a list.
    /// </remarks>
    public static string ParamOf(string columnName) => columnName + "Key";

    /// <summary>Which shape a key column's type is.</summary>
    public static string KindOf(Models.ValueType type)
        => type switch
        {
            Models.ValueType.String => "string",
            Models.ValueType.Enum => "enum",
            Models.ValueType.Int64 or Models.ValueType.DateTime or Models.ValueType.TimeSpan
                => "int64",
            _ => "number",
        };
}

/// <summary>
/// One composite key, for the two languages that list their single keys separately.
/// </summary>
/// <remarks>
/// C# and TypeScript publish a `RecordsBy...` dictionary beside each single-column lookup, and
/// a composite key has none to publish: what it is keyed by is the text its columns make, which
/// is this file's business rather than a caller's. So those two keep their existing list of
/// single keys untouched and take composites through a second list - which also means a table
/// with no composite key generates exactly what it generated before.
/// </remarks>
internal sealed class CompositeKeyView
{
    /// <summary>What the lookup names end in.</summary>
    public required string Suffix { get; set; }

    /// <summary>The private map from key text to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The columns as the sheet spells them, for the doc line and the message.</summary>
    public required string FieldName { get; set; }

    /// <summary>The columns making up the key.</summary>
    public required IReadOnlyList<KeyComponentView> Components { get; set; }

    /// <summary>The lookup's parameter list.</summary>
    public required string Params { get; set; }

    /// <summary>What the map is subscripted with, given those parameters.</summary>
    public required string Argument { get; set; }

    /// <summary>How the miss message writes the key.</summary>
    public required string ValueFormat { get; set; }
}
