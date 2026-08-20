using System.Collections.Generic;
using Tabbit.Models;

namespace Tabbit.CodeGeneration;

/// <summary>
/// One column whose value is a row of one of several tables, as a generator reads it.
/// </summary>
/// <remarks>
/// The parts a generator cannot get from the field alone: the resolved targets in the order
/// they were named, and the enumeration saying which of them a row landed in. Every language
/// spells the members itself - the names are not here, because a name that crossed language
/// boundaries would have to be spelled in one casing and each language has its own.
///
/// spec/multi-target-accessors.md.
/// </remarks>
internal sealed class MultiTargetColumn
{
    /// <summary>The column, which carries the key its targets are addressed by.</summary>
    public required Field Field { get; init; }

    /// <summary>The group the column is the whole of, which is what names the member.</summary>
    public required SerialField Group { get; init; }

    /// <summary>The tables the value may be a row of, in the order they were named.</summary>
    public required IReadOnlyList<Table> Targets { get; init; }

    /// <summary>The enumeration saying which target answered.</summary>
    public required Models.Enum Discriminator { get; init; }
}

/// <summary>
/// Which of a table's columns get per-target accessors.
/// </summary>
/// <remarks>
/// Asked in one place rather than in each generator, because the answer is a property of the
/// model and getting it wrong in one language produces accessors that never resolve rather
/// than code that fails to compile.
/// </remarks>
internal static class MultiTargetColumns
{
    /// <summary>
    /// The columns of one table that reach several tables and are shaped to carry accessors.
    /// </summary>
    /// <remarks>
    /// **One column holding one value.** A group of numbered columns folded into an array of
    /// keys, and a member of a record group, both reach several tables in the sheets this
    /// comes from - and what a per-target accessor looks like inside those is a further
    /// question: the element number has to sit somewhere in the name, and the record case is
    /// held back at promotion for exactly that reason.
    ///
    /// Those columns are **left as they are** rather than refused. They carry the key today
    /// and go on carrying it, which is the same treatment a column whose targets are not all
    /// in this build gets. Refusing them would turn conversions that work into failures over
    /// a feature they never had.
    /// </remarks>
    internal static List<MultiTargetColumn> Of(Table table)
    {
        var result = new List<MultiTargetColumn>();

        foreach (var group in table.SerialFields)
        {
            if (group.IsRecord || group.IsArray || group.Fields.Count != 1)
                continue;

            var field = group.Fields[0];

            if (!field.IsMultiRef
                || field.ResolvedRefTables is not { Count: > 1 }
                || field.MultiTargetEnum is null)
            {
                continue;
            }

            result.Add(new MultiTargetColumn
            {
                Field = field,
                Group = group,
                Targets = field.ResolvedRefTables,
                Discriminator = field.MultiTargetEnum,
            });
        }

        return result;
    }
}
