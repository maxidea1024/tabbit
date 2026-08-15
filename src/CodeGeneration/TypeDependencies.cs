using Tabbit.Models;
using System.Collections.Generic;
using System.Linq;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Which other generated types each generated file names.
///
/// Splitting a target's output into a file per table turns one question that never came up
/// into a question every language asks differently: this file names `RankEnum` and
/// `OwnersTable`, so what does it have to say at the top to see them? An import, an
/// include, a require, a forward declaration, or in Kotlin's case nothing at all.
///
/// The answer to *what does it name* is the same for all of them, and it is this. Only the
/// spelling is the generator's business.
/// </summary>
/// <remarks>
/// The graph is a DAG, which is what makes it usable as an include order:
///
///   enums, constant sets  ->  nothing generated
///   a table               ->  the enums it names, the tables it references
///   the accessor          ->  every table
///
/// A table referencing a table is the only edge that can form a cycle - two tables
/// pointing at each other is legal in the sheets and does happen. So the table-to-table
/// edges are reported separately from the rest: a language that needs a strict order (C,
/// C++) forward-declares across them, and one that does not (Python, PHP) can treat them
/// as ordinary dependencies. Nothing here tries to break the cycle by dropping an edge,
/// which would silently generate a file that cannot see a type it names.
/// </remarks>
internal static class TypeDependencies
{
    /// <summary>
    /// The enums a table's record names, in declaration order and without repeats.
    ///
    /// An enum field reads through its own type - `Rank(reader.read_enum())` - so the
    /// table's file names every enum any of its fields is typed with.
    /// </summary>
    public static IReadOnlyList<Models.Enum> EnumsNamedBy(Table table)
        => Distinct(table.SerialFields
            .Where(sf => sf.ElementType == ValueType.Enum)
            .Select(sf => sf.FirstField!.Enum));

    /// <summary>
    /// The tables a table references, in declaration order and without repeats.
    ///
    /// Includes the table itself when a row points at another row of its own table, which
    /// is a self-edge rather than a cycle: a file can always see its own declarations, so
    /// callers filter it out where a self-reference would be a redundant import.
    /// </summary>
    public static IReadOnlyList<Table> TablesReferencedBy(Table table)
        => Distinct(table.SerialFields
            .Where(sf => sf.IsRef)
            .Select(sf => sf.FirstField!.ResolvedRefTable!));

    /// <summary>
    /// The same, minus the table itself.
    /// </summary>
    public static IReadOnlyList<Table> OtherTablesReferencedBy(Table table)
        => TablesReferencedBy(table).Where(other => other != table).ToList();

    /// <summary>
    /// The enums a constant set names.
    ///
    /// A constant of an enum type renders as a label - `Rank.GOLD` - so the set's file
    /// names that enum just as a table's does.
    /// </summary>
    public static IReadOnlyList<Models.Enum> EnumsNamedBy(ConstantSet set)
        => Distinct(set.Constants
            .Where(constant => constant.Type == ValueType.Enum)
            .Select(constant => constant.Enum));

    /// <summary>
    /// Whether any table references another, which is what decides if a target has
    /// cross-reference resolution to generate at all.
    /// </summary>
    public static bool AnyCrossReference(Model model)
        => model.Tables.Any(table => table.SerialFields.Any(sf => sf.IsRef));

    /// <summary>
    /// Reference order by declaration, and each entry once.
    /// </summary>
    /// <remarks>
    /// Distinct() on its own would do, and does keep first-seen order in every runtime this
    /// has ever run on - but that is documented as unspecified, and an import list that
    /// reorders between runs is a diff on every regeneration. Spelled out instead.
    /// </remarks>
    private static IReadOnlyList<T> Distinct<T>(IEnumerable<T> items) where T : class
    {
        var seen = new HashSet<T>();
        var ordered = new List<T>();

        foreach (var item in items)
        {
            if (item is not null && seen.Add(item))
                ordered.Add(item);
        }

        return ordered;
    }
}
