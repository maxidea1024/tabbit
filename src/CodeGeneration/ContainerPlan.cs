using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Every `set` and `map` in a table, in the terms a generator needs to declare one.
/// </summary>
/// <remarks>
/// **Here rather than once per language, because none of it is about a language.** Which
/// member is a container, what its key and value members are, and what reaches it from a
/// record are the same answers in all of them - what differs is the spelling of a map type
/// and where a lookup is allowed to sit, and that is what each generator adds.
///
/// spec/types/set-and-map.md section 7.
/// </remarks>
internal sealed class ContainerPlan
{
    /// <summary>Which container this is.</summary>
    public required ContainerKind Kind { get; init; }

    /// <summary>The group this sits in.</summary>
    public required SerialField Group { get; init; }

    /// <summary>
    /// The member names from the group down to the record the lookup is declared on.
    /// </summary>
    /// <remarks>
    /// A map's lookup sits on the map, because a map is a record and has a type of its own.
    /// A set's sits on the record holding it: a set is one array and has no type to hang
    /// anything on. So this path ends at the map, or at the record above the set.
    /// </remarks>
    public required IReadOnlyList<string> Path { get; init; }

    /// <summary>
    /// The member the lookup is built from: a map's key column, or the set itself.
    /// </summary>
    public required RecordMember Source { get; init; }

    /// <summary>
    /// A map's value member, or null for a set.
    /// </summary>
    public RecordMember? Value { get; init; }

    /// <summary>
    /// Whether a map's value is one column, so a lookup can answer with it.
    /// </summary>
    /// <remarks>
    /// False where the value is a struct - there it is a member per column and there is no
    /// single value to hand back, so the lookup answers with the entry's position instead.
    /// The two are named differently in every language so that neither can be mistaken for
    /// the other. Section 7.1.
    /// </remarks>
    public bool ValueIsOneColumn => Value is { IsLeaf: true };

    /// <summary>Whether this is a map rather than a set.</summary>
    public bool IsMap => Kind == ContainerKind.Map;

    /// <summary>
    /// The containers of one record type, for the generator declaring that type.
    /// </summary>
    /// <param name="members">The members of the type being declared.</param>
    /// <param name="own">What that type itself is, which is a map or nothing.</param>
    public static List<ContainerPlan> Of(
        List<RecordMember> members, ContainerKind own, SerialField group)
    {
        var result = new List<ContainerPlan>();

        if (own == ContainerKind.Map
            && members.Find(member => member.Name == ContainerMembers.Key) is { } key)
        {
            result.Add(new ContainerPlan
            {
                Kind = ContainerKind.Map,
                Group = group,
                Path = [],
                Source = key,
                Value = members.Find(member => member.Name == ContainerMembers.Value),
            });
        }

        foreach (var member in members.Where(m => m.Container == ContainerKind.Set))
        {
            result.Add(new ContainerPlan
            {
                Kind = ContainerKind.Set,
                Group = group,
                Path = [],
                Source = member,
            });
        }

        return result;
    }

    /// <summary>
    /// Every container in a table, each carrying the path that reaches it from a record.
    /// </summary>
    public static List<ContainerPlan> Of(Table table)
    {
        var result = new List<ContainerPlan>();

        foreach (var group in table.SerialFields.Where(group => group.IsRecord))
        {
            Gather(group.Members, ContainerKind.None, group, []);

            foreach (var member in group.Members)
                Walk(member, group, []);
        }

        return result;

        void Walk(RecordMember member, SerialField group, List<string> prefix)
        {
            var here = new List<string>(prefix) { member.Name };

            Gather(member.Members, member.Container, group, here);

            foreach (var below in member.Members)
                Walk(below, group, here);
        }

        void Gather(
            List<RecordMember> members, ContainerKind own, SerialField group, List<string> path)
        {
            foreach (var plan in Of(members, own, group))
            {
                result.Add(new ContainerPlan
                {
                    Kind = plan.Kind,
                    Group = plan.Group,
                    Path = path,
                    Source = plan.Source,
                    Value = plan.Value,
                });
            }
        }
    }
}
