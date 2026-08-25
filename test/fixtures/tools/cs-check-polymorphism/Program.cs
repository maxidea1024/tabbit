// Read-back check for a polymorphic record group.
//
// **A compile answers most of this and nothing else can.** The variant types only mean
// something if `is` actually narrows to them, and that is a statement about generated code
// that has to be compiled to be true. A generator that emitted the union flat would still
// produce a file this program cannot build.
//
// What the read adds on top: that the discriminator picked the right variant per row, and
// that a member belonging to another variant is not on the object at all. The second is the
// one the union notation makes easy to get wrong - every row has blank cells that are not
// its own, and a build that put them on the object would look fine until someone read one.
//
// spec/polymorphism.md sections 5.2 and 7.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-polymorphism <binary-table-directory>");
            return 2;
        }

        try
        {
            await PolyAccessor.ReadAllAsync(args[0]);

            var report = new Dictionary<string, object>
            {
                ["Skill"] = PolyAccessor.Skill.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["name"] = r.Name,

                    // The variant, named by the type rather than by the number. A row whose
                    // discriminator picked the wrong one shows as a different word here.
                    ["kind"] = r.Effect.GetType().Name,

                    // The abstract type's own field, read through the base type - which is the
                    // whole point of it being one column.
                    ["chance"] = r.Effect.Chance,

                    // And the members that only exist on one variant, reached the way a
                    // consumer reaches them. `is` has to narrow for this to compile.
                    ["own"] = Own(r.Effect),
                }).ToList(),

                // **The array of them**, where each element carries its own discriminator - so
                // one row's shapes may differ. Section 5.3.
                ["Combo"] = PolyAccessor.Combo.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["name"] = r.Name,
                    ["kinds"] = string.Join(
                        ",", r.Effects.Select(effect => effect.GetType().Name)),
                    ["own"] = string.Join(",", r.Effects.Select(Own)),
                }).ToList(),
            };

            Console.WriteLine(JsonSerializer.Serialize(report));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    /// <summary>What one effect carries beyond the base field, per variant.</summary>
    /// <remarks>
    /// Pattern matching rather than a discriminator switch, because that is the surface the
    /// spec chose for a language that has it - and a variant type that did not inherit the
    /// base would not compile here. spec/polymorphism.md section 7.
    /// </remarks>
    private static string Own(Effect effect)
    {
        if (effect is DamageEffect damage)
            return $"damage={damage.Damage},pierces={damage.Pierces}";

        if (effect is HealEffect heal)
            return $"amount={heal.Amount}";

        if (effect is NoEffect)
            return "none";

        return "<unknown>";
    }
}
