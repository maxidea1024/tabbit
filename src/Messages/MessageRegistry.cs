using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tabbit.Messages;

/// <summary>
/// Every message id this build declares, found by scanning for
/// <see cref="TabbitMessagesAttribute"/>.
/// </summary>
/// <remarks>
/// Scanned rather than listed, so that adding a message means editing one file and deleting
/// a layout means deleting one file. The same reason <see cref="Cooking.Layouts.LayoutRegistry"/>
/// scans.
///
/// What this exists for, beyond answering the question, is that it makes "every id in the
/// code" a set a test can hold. Without it a catalog gate would have to grep the sources,
/// which finds a string that looks like an id and misses one built at run time.
/// </remarks>
public static class MessageRegistry
{
    private static readonly Lazy<IReadOnlyList<Declared>> Found = new(Discover);

    /// <summary>One declared id and the class that declared it.</summary>
    public readonly record struct Declared(string Id, string Prefix, string DeclaringType);

    /// <summary>Every id this build declares, in order.</summary>
    public static IReadOnlyList<Declared> All => Found.Value;

    /// <summary>Just the ids.</summary>
    public static IReadOnlyList<string> Ids => All.Select(d => d.Id).ToList();

    private static IReadOnlyList<Declared> Discover()
    {
        var declared = new List<Declared>();

        foreach (var type in typeof(MessageRegistry).Assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<TabbitMessagesAttribute>();
            if (attribute is null)
                continue;

            string wanted = attribute.Prefix + ".";

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                // Constants only. A static readonly string would be a value this cannot read
                // without running the class's initializer, and an id that is computed is an
                // id no gate can enumerate.
                if (!field.IsLiteral || field.FieldType != typeof(string))
                    continue;

                string? id = (string?)field.GetRawConstantValue();
                if (id is null)
                    continue;

                // The prefix is stated once on the class, so a constant that does not carry
                // it is a line copied from another area and left half-edited. Caught here
                // rather than at the catalog, where it would look like a missing entry.
                if (!id.StartsWith(wanted, StringComparison.Ordinal))
                {
                    throw new TabbitDefectException(
                        $"`{type.Name}.{field.Name}` is `{id}`, but the class is marked "
                        + $"[TabbitMessages(\"{attribute.Prefix}\")] so every id in it has to "
                        + $"begin with `{wanted}`.");
                }

                declared.Add(new Declared(id, attribute.Prefix, type.Name));
            }
        }

        var duplicate = declared.GroupBy(d => d.Id, StringComparer.Ordinal)
                                .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new TabbitDefectException(
                $"The id `{duplicate.Key}` is declared in more than one place: "
                + string.Join(", ", duplicate.Select(d => d.DeclaringType)));
        }

        declared.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

        return declared;
    }
}
